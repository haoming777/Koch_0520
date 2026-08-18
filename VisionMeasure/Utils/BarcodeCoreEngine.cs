using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using CommonLib;
using Microsoft.Win32;
using Newtonsoft.Json;
using OpenCvSharp;

namespace VisionMeasure.Utils
{
    /// <summary>
    /// BarcodeCore.dll (协力自动化) 条码识别引擎封装.
    /// 替换原 ZXing.Net 解码: 裁图/OpenCV预处理仍由 BackStationProcessor 完成,
    /// 本类只负责"把裁好处理好的图交给引擎解码 → 取回文本与四点坐标".
    /// 部署文件(必须与 exe 同目录): BarcodeCore.dll / opencv_world4140.dll / zbar-0.dll
    /// / iconv-2.dll / XLUsbDogShim.dll / XL.UsbDog.dll (见 Libs/BarcodeCore).
    /// 环境要求: VC++ 2015-2022 x64 运行库 + .NET Framework 4.x + USB加密狗.
    /// 独立日志: Logs/Barcode_yyyyMMdd.log (与主日志分离, 便于单独排查条码问题).
    /// </summary>
    public static class BarcodeCoreEngine
    {
        // ====== C ABI 结果结构体 (352 字节, 与 barcode_api.h 对齐) ======
        [StructLayout(LayoutKind.Sequential)]
        private struct BarcodeResultRaw
        {
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 256)] public byte[] TextBytes;   // UTF-8, NUL结尾
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 64)]  public byte[] FormatBytes; // UTF-8, NUL结尾
            public int X1, Y1;  // 左上
            public int X2, Y2;  // 右上
            public int X3, Y3;  // 右下
            public int X4, Y4;  // 左下 (输入图像坐标系)
        }
        private const int BarcodeResultSize = 352;

        private static class BarcodeNative
        {
            private const string DllName = "BarcodeCore.dll";
            [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int InitBarcodeEngine([MarshalAs(UnmanagedType.LPUTF8Str)] string configJsonPath);
            [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern int DecodeImage(IntPtr buffer, int width, int height, int stride, out IntPtr pResults);
            [DllImport(DllName, CallingConvention = CallingConvention.Cdecl)]
            internal static extern void FreeBarcodeResult(IntPtr pResults);
        }

        private static readonly object _initLock = new object();
        private static readonly object _errorLogLock = new object();
        private static bool _initialized;
        private static long _lastInitAttemptTicks;
        private const long InitRetryIntervalTicks = 30L * TimeSpan.TicksPerSecond;   // 初始化失败后30秒内不重复尝试
        private const long ErrorLogIntervalTicks = 30L * TimeSpan.TicksPerSecond;     // 同类解码错误主日志限频
        private static readonly Dictionary<int, long> _lastErrorLogTicks = new Dictionary<int, long>();

        // ====== 初始化 ======

        /// <summary>
        /// 确保引擎已初始化(线程安全, 可重复调用).
        /// 失败后30秒内自动退避, force=true 强制立即重试(参数保存后调用).
        /// </summary>
        public static bool EnsureInitialized(bool force = false)
        {
            lock (_initLock)
            {
                if (_initialized && !force) return true;

                long now = DateTime.Now.Ticks;
                if (!force && _lastInitAttemptTicks != 0 && now - _lastInitAttemptTicks < InitRetryIntervalTicks)
                    return false;
                _lastInitAttemptTicks = now;

                CheckEnvironment();
                string configPath = GetOrCreateConfig();
                try
                {
                    int rc = BarcodeNative.InitBarcodeEngine(configPath);
                    if (rc == 0)
                    {
                        _initialized = true;
                        BarcodeLogger.Info("InitBarcodeEngine 成功" + (string.IsNullOrEmpty(configPath) ? " (默认配置)" : $" (config={configPath})"));
                        Logger.Info("[BarcodeCore] 引擎初始化成功");
                        return true;
                    }
                    _initialized = false;
                    string msg = MapInitError(rc);
                    BarcodeLogger.Error($"InitBarcodeEngine 失败 rc={rc}: {msg}");
                    Logger.Error($"[BarcodeCore] 引擎初始化失败 rc={rc}: {msg}");
                    return false;
                }
                catch (DllNotFoundException ex)
                {
                    _initialized = false;
                    BarcodeLogger.Error($"加载 BarcodeCore.dll 失败: {ex.Message} (请核对6个依赖DLL是否齐全、VC++ 2015-2022 x64运行库是否已安装)");
                    Logger.Error($"[BarcodeCore] 加载DLL失败: {ex.Message}");
                    return false;
                }
                catch (BadImageFormatException ex)
                {
                    _initialized = false;
                    BarcodeLogger.Error($"BarcodeCore.dll 架构不匹配: {ex.Message} (64位程序必须配 x64 版DLL)");
                    Logger.Error($"[BarcodeCore] 架构不匹配: {ex.Message}");
                    return false;
                }
                catch (EntryPointNotFoundException ex)
                {
                    _initialized = false;
                    BarcodeLogger.Error($"BarcodeCore.dll 接口不匹配: {ex.Message} (DLL版本过旧?)");
                    Logger.Error($"[BarcodeCore] 接口不匹配: {ex.Message}");
                    return false;
                }
                catch (Exception ex)
                {
                    _initialized = false;
                    BarcodeLogger.Error($"初始化异常: {ex.Message}");
                    Logger.Error($"[BarcodeCore] 初始化异常: {ex.Message}");
                    return false;
                }
            }
        }

        /// <summary>重新初始化(重新加载 barcode.config.json, 与在途解码互不阻塞)</summary>
        public static void ReInitialize() { EnsureInitialized(force: true); }

        private static string MapInitError(int rc)
        {
            switch (rc)
            {
                case -1: return "配置文件不可读(检查路径与权限)";
                case -2: return "配置 JSON 解析失败(键名大小写敏感)";
                case -3: return "引擎内部异常";
                case -4: return "未找到加密狗(插狗 / 核对XLUsbDogShim.dll与XL.UsbDog.dll是否同目录 / 需.NET Framework 4.x)";
                default: return $"未知错误码 {rc}";
            }
        }

        // ====== 环境检查 (问题3: 跑不起来时日志里能直接看出原因) ======

        /// <summary>初始化前的环境自检: 依赖文件/VC++运行库/.NET版本/加密狗, 全部写入条码独立日志</summary>
        private static void CheckEnvironment()
        {
            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            try
            {
                // 1. 依赖文件清单 (6个)
                string[] required = { "BarcodeCore.dll", "opencv_world4140.dll", "zbar-0.dll", "iconv-2.dll", "XLUsbDogShim.dll", "XL.UsbDog.dll" };
                var missing = required.Where(f => !File.Exists(Path.Combine(baseDir, f))).ToList();
                if (missing.Count > 0)
                {
                    BarcodeLogger.Error($"依赖文件缺失({missing.Count}/{required.Length}): {string.Join(", ", missing)} (目录={baseDir})");
                    Logger.Error($"[BarcodeCore] 依赖文件缺失: {string.Join(", ", missing)}");
                }
                else
                {
                    BarcodeLogger.Info($"依赖文件齐全 {required.Length}/{required.Length} (目录={baseDir})");
                }

                // 2. VC++ 2015-2022 Redistributable (BarcodeCore.dll 是 C++ 编译, 缺了会报"找不到指定的模块")
                string vcX64 = CheckVcRedist("x64", RegistryView.Registry64);
                string vcX86 = CheckVcRedist("x86", RegistryView.Registry32);
                BarcodeLogger.Info($"VC++运行库: x64={vcX64}, x86={vcX86}");
                if (vcX64 != "已安装" && vcX64 != "检测失败")
                {
                    Logger.Error($"[BarcodeCore] 未检测到 VC++ 2015-2022 x64 运行库({vcX64}), 请安装 vc_redist.x64.exe, 否则 BarcodeCore.dll 将加载失败");
                }

                // 3. .NET Framework (加密狗桥接需要 4.x)
                string netVer = CheckNetFramework();
                BarcodeLogger.Info($".NET Framework: {netVer}");

                // 4. 加密狗预检 (主程序已做过校验, 此处仅提前定位条码引擎的 -4 报错)
                try
                {
                    var dogType = Type.GetType("XL.UsbDog.XLUsbDogClass, XL.UsbDog");
                    if (dogType == null)
                    {
                        var asm = System.Reflection.Assembly.LoadFrom(Path.Combine(baseDir, "XL.UsbDog.dll"));
                        dogType = asm.GetType("XL.UsbDog.XLUsbDogClass");
                    }
                    var inst = Activator.CreateInstance(dogType);
                    bool dog = (bool)dogType.GetMethod("FindUsbDog", new Type[0]).Invoke(inst, null);
                    BarcodeLogger.Info($"加密狗预检: {(dog ? "在线" : "未找到")}");
                    if (!dog) Logger.Error("[BarcodeCore] 加密狗预检: 未找到加密狗(引擎将返回-4拒绝解码)");
                }
                catch (Exception ex) { BarcodeLogger.Warn($"加密狗预检异常: {ex.Message}"); }
            }
            catch (Exception ex)
            {
                BarcodeLogger.Error($"环境检查异常: {ex.Message}");
            }
        }

        private static string CheckVcRedist(string arch, RegistryView view)
        {
            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, view))
                using (var key = baseKey.OpenSubKey($@"SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\{arch}"))
                {
                    if (key == null) return "未安装";
                    int installed = Convert.ToInt32(key.GetValue("Installed", 0));
                    string ver = key.GetValue("Version") as string;
                    return installed == 1 ? "已安装" + (string.IsNullOrEmpty(ver) ? "" : $"({ver})") : "未安装";
                }
            }
            catch { return "检测失败"; }
        }

        private static string CheckNetFramework()
        {
            try
            {
                using (var baseKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry32))
                using (var key = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full"))
                {
                    if (key == null) return "未知";
                    int release = Convert.ToInt32(key.GetValue("Release", 0));
                    if (release >= 528040) return $"4.8+ (Release={release})";
                    if (release >= 461808) return $"4.7.2+ (Release={release})";
                    return $"低于4.7.2 (Release={release})";
                }
            }
            catch { return "检测失败"; }
        }

        // ====== 配置文件 ======

        /// <summary>获取 Config\barcode.config.json, 不存在则自动创建生产默认配置(单码快扫+关调试预览+强制加密狗)</summary>
        private static string GetOrCreateConfig()
        {
            try
            {
                if (!File.Exists(BarcodeEngineConfig.ConfigPath))
                {
                    new BarcodeEngineConfig().Save();
                    BarcodeLogger.Info($"配置文件不存在, 已自动创建: {BarcodeEngineConfig.ConfigPath}");
                    Logger.Info($"[BarcodeCore] 已自动创建默认配置 {BarcodeEngineConfig.ConfigPath}");
                }
                return BarcodeEngineConfig.ConfigPath;
            }
            catch (Exception ex)
            {
                BarcodeLogger.Error($"创建配置文件失败: {ex.Message}, 引擎改用全默认配置");
                Logger.Warning($"[BarcodeCore] 配置文件创建失败, 改用默认配置: {ex.Message}");
                return null; // NULL = 全默认配置
            }
        }

        // ====== 解码 ======

        /// <summary>
        /// 解码一张已裁好/预处理好的图像(灰度1通道或BGR/BGRA均可, 通道数由 stride/width 自动推断).
        /// 返回: &gt;0=识别到N个码(results非null) / 0=未识别到(不是异常) / 负数=错误码(-1参数非法 -2未初始化 -3内部异常 -4未找到加密狗)
        /// </summary>
        public static int Decode(Mat image, out List<BarcodeTextResult> results)
        {
            results = null;
            if (image == null || image.Empty())
            {
                BarcodeLogger.Error("Decode: 输入图像为空");
                return -1;
            }
            if (!EnsureInitialized()) return -2;

            IntPtr pResults = IntPtr.Zero;
            int n;
            try
            {
                n = BarcodeNative.DecodeImage(image.Data, image.Width, image.Height, (int)image.Step(), out pResults);
            }
            catch (Exception ex)
            {
                BarcodeLogger.Error($"DecodeImage 调用异常: {ex.Message}");
                return -3;
            }

            if (n < 0) { LogDecodeError(n); return n; }
            if (n == 0) return 0;

            try
            {
                results = new List<BarcodeTextResult>(n);
                for (int i = 0; i < n; i++)
                {
                    var raw = Marshal.PtrToStructure<BarcodeResultRaw>(IntPtr.Add(pResults, i * BarcodeResultSize));
                    results.Add(new BarcodeTextResult
                    {
                        Text = DecodeUtf8Z(raw.TextBytes),
                        Format = DecodeUtf8Z(raw.FormatBytes),
                        X1 = raw.X1, Y1 = raw.Y1,
                        X2 = raw.X2, Y2 = raw.Y2,
                        X3 = raw.X3, Y3 = raw.Y3,
                        X4 = raw.X4, Y4 = raw.Y4
                    });
                }
                return n;
            }
            catch (Exception ex)
            {
                BarcodeLogger.Error($"解析识别结果异常: {ex.Message}");
                results = null;
                return -3;
            }
            finally
            {
                BarcodeNative.FreeBarcodeResult(pResults); // ★ 必须与 DecodeImage 成对释放
            }
        }

        /// <summary>解码错误码 → 可读信息, 写入独立日志(全量) + 主日志(30秒限频防刷屏)</summary>
        private static void LogDecodeError(int code)
        {
            string map;
            switch (code)
            {
                case -1: map = "参数非法(检查传入图像缓冲与尺寸)"; break;
                case -2: map = "引擎未初始化(先调InitBarcodeEngine)"; break;
                case -3: map = "引擎内部异常(可重新Init重试)"; break;
                case -4: map = "未找到加密狗(狗被拔/组件缺失)"; break;
                default: map = $"未知错误码{code}"; break;
            }
            BarcodeLogger.Error($"DecodeImage 返回 {code}: {map}");

            lock (_errorLogLock)
            {
                long now = DateTime.Now.Ticks;
                if (_lastErrorLogTicks.TryGetValue(code, out long last) && now - last < ErrorLogIntervalTicks) return;
                _lastErrorLogTicks[code] = now;
            }
            Logger.Error($"[BarcodeCore] 解码失败 code={code}: {map}");
        }

        private static string DecodeUtf8Z(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0) return "";
            int len = Array.IndexOf(bytes, (byte)0);
            if (len < 0) len = bytes.Length;
            return Encoding.UTF8.GetString(bytes, 0, len);
        }
    }

    /// <summary>解码结果: 条码文本/类型/旋转矩形四角(输入图像坐标系, 顺序=左上→右上→右下→左下)</summary>
    public class BarcodeTextResult
    {
        public string Text;
        public string Format;
        public int X1, Y1, X2, Y2, X3, Y3, X4, Y4;
        public override string ToString() => $"{Text} ({Format})";
    }

    /// <summary>
    /// 条码独立日志: Logs/Barcode_yyyyMMdd.log, 按天滚动.
    /// 与主日志(Logs/yyyyMMdd.log)、PLC日志、Error_*.log 完全分离, 专记条码引擎相关活动.
    /// (BarcodeCore.dll 自身不写文件日志, 只输出 OutputDebugString/stderr, 文件日志由本类实现)
    /// </summary>
    public static class BarcodeLogger
    {
        private static readonly object _lock = new object();
        private static string _currentFile = "";

        public static void Info(string msg) { Write("INFO", msg); }
        public static void Warn(string msg) { Write("WARN", msg); }
        public static void Error(string msg) { Write("ERROR", msg); }

        private static void Write(string level, string msg)
        {
            try
            {
                string file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", $"Barcode_{DateTime.Now:yyyyMMdd}.log");
                lock (_lock)
                {
                    if (file != _currentFile)
                    {
                        var dir = Path.GetDirectoryName(file);
                        if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                        _currentFile = file;
                    }
                    File.AppendAllText(file, $"[{DateTime.Now:HH:mm:ss.fff}] [{level}] {msg}{Environment.NewLine}", Encoding.UTF8);
                }
            }
            catch { /* 日志写盘失败不阻断业务 */ }
        }
    }

    /// <summary>
    /// barcode.config.json 引擎配置(键名与DLL约定一致).
    /// 界面只开放★参数, 其余字段(加密狗/调试预览/并行槽/Roi)保持默认并随文件完整读写.
    /// 改参数 → 保存 → BarcodeCoreEngine.ReInitialize() 即实时生效.
    /// </summary>
    public class BarcodeEngineConfig
    {
        public class SecuritySection
        {
            public bool RequireDongle { get; set; } = true;
            public string DongleDeveloperId { get; set; } = "";
        }

        public class EngineStrategySection
        {
            public bool EnableZXingFallback { get; set; } = true;
            public bool AutoRotationCorrection { get; set; } = true;
            /// <summary>false=单码快扫(binary命中即返回, 生产推荐 约2.3倍速); true=多码全扫(同区域多码场景)</summary>
            public bool EnableMultiScan { get; set; } = false;
            /// <summary>调试中间图开关, 生产必须保持 false</summary>
            public bool EnableDebugPreview { get; set; } = false;
            public int ParallelSlots { get; set; } = 0;
        }

        public class ImageProcessingSection
        {
            public bool UseAdaptiveThreshold { get; set; } = true;
            /// <summary>自适应窗口 11~71 强制奇数; 小码建议≤35</summary>
            public int AdaptiveBlockSize { get; set; } = 35;
            /// <summary>仅 UseAdaptiveThreshold=false 时生效</summary>
            public int GlobalThreshold { get; set; } = 128;
            public bool EnableMorphology { get; set; } = true;
            public int MorphologyKernelSize { get; set; } = 3;
            /// <summary>unsharp锐化强度 0~10, 0=关闭, 抗运动模糊可加大</summary>
            public double SharpenIntensity { get; set; } = 1.5;
        }

        public class RoiSection
        {
            public bool Enabled { get; set; } = false;
            public int X { get; set; } = 0;
            public int Y { get; set; } = 0;
            public int Width { get; set; } = 0;
            public int Height { get; set; } = 0;
        }

        public SecuritySection Security { get; set; } = new SecuritySection();
        public EngineStrategySection EngineStrategy { get; set; } = new EngineStrategySection();
        public ImageProcessingSection ImageProcessing { get; set; } = new ImageProcessingSection();
        public RoiSection Roi { get; set; } = new RoiSection();

        public static string ConfigPath => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "barcode.config.json");

        public static BarcodeEngineConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var cfg = JsonConvert.DeserializeObject<BarcodeEngineConfig>(File.ReadAllText(ConfigPath, Encoding.UTF8));
                    if (cfg != null)
                    {
                        if (cfg.Security == null) cfg.Security = new SecuritySection();
                        if (cfg.EngineStrategy == null) cfg.EngineStrategy = new EngineStrategySection();
                        if (cfg.ImageProcessing == null) cfg.ImageProcessing = new ImageProcessingSection();
                        if (cfg.Roi == null) cfg.Roi = new RoiSection();
                        return cfg;
                    }
                }
            }
            catch (Exception ex) { BarcodeLogger.Error($"加载 barcode.config.json 失败: {ex.Message}, 使用默认配置"); }
            return new BarcodeEngineConfig();
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                File.WriteAllText(ConfigPath, JsonConvert.SerializeObject(this, Formatting.Indented), Encoding.UTF8);
            }
            catch (Exception ex) { BarcodeLogger.Error($"保存 barcode.config.json 失败: {ex.Message}"); }
        }
    }
}
