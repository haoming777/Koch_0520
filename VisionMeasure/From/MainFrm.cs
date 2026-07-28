using CommonLib;
using Config;
using Hardware;
using Models;
using MT.Camera.SDK;
using OpenCvSharp;
using PLC调试.Class;
using Stations;
using Sunny.UI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using Newtonsoft.Json;
using System.Threading.Tasks;
using System.Windows.Forms;
using VisionMeasure.Utils;
using VisionMeasure.From;
using XL.Controls;
using static CommonLib.Class_Config;
using BmpConverter = OpenCvSharp.Extensions.BitmapConverter;
// 解决 Point 和 Size 二义性问题 - 使用别名
using CvPoint = OpenCvSharp.Point;
using CvSize = OpenCvSharp.Size;
using DrawPoint = System.Drawing.Point;
using DrawSize = System.Drawing.Size;
using Timer = System.Windows.Forms.Timer;
using VisionMeasure.Stations;  // 引入 FrontStationProcessor 所在的命名空间

namespace VisionMeasure
{
	/// <summary>
	/// 主窗体 — 系统核心调度器, 实现ICamera接口直接接收大华相机事件
	/// 职责: 硬件管理(ZMC+PLC+8相机+触发管理器) | AI模型(11个GPU0/GPU1) | 4工位调度 | UI更新 | 配置热更新 | 安全锁 | 班次管理
	/// 启动: InitHardware→InitCameras→InitAiModels→InitStations→InitUI
	/// 关键设计: 相机直连(不再通过CameraManager) | 预加载优化(Program.cs后台加载) | 热更新(无需重启)
	/// </summary>
	public partial class MainFrm : Form, ICamera
	{
		/// <summary>手动测试模式：true时停止所有自动触发</summary>
		public static bool ManualTestMode = false;
		/// <summary>各工位启用开关</summary>
		public static bool FrontEnabled = true, BackEnabled = true, EndFaceEnabled = true, SideEnabled = true;
		// ========== 硬件管理层 ==========
		private MotionControlManager _motionMgr;
		private CameraTriggerManager _triggerMgr;
		private PLC调试.Class.S7_1500Class _s7Plc;
		private Hardware.PlcResultService _plcResultService;
		private Hardware.StationType? _pendingTestStation;  // 非null时=测试模式, 推理完成后弹窗发送PLC

		// ========== AI模型管理层 ==========
		private AiModelManager _aiModels;

		/// <summary>预加载的AI模型管理器（由Program.cs设置以避免重复加载）</summary>
		public static AiModelManager PreloadedModels { get; set; }

		/// <summary>预加载的SKU数据库</summary>
		public static SkuDatabase PreloadedSkuDb { get; set; }
		private static MotionControlManager _staticMotionMgr;
		public static MotionControlManager GetMotionManager() => _staticMotionMgr;

		// ========== 工位处理器 ==========
		private FrontStationProcessor _frontStation;
		private EndFaceStationProcessor _endFaceStation;
		private BackStationProcessor _backStation;
		private SideStationProcessor _sideStation;
		private long _sidePendingCount;  // IN13↓排队计数器(Interlocked操作, 替代旧的bool)
		private long _sideEmptyCycleCount = 0;  // 侧面空触发连续计数(用于检测没有盒子空跑)
		private long _lastSideImageCount = 0;   // 上一次侧面检测周期收图总数(诊断漏拍)
		private long _lastFrontImageTicks = 0;  // 最后一次正面收到图像的Ticks(保留, 原用途不变)
		private long _lastEndFaceImageTicks = 0;  // 最后一次端面收到图像的Ticks(用于侧面活件判断)
		private const long NoProductTimeoutTicks = 120L * 10000 * 1000; // 2分钟全工位无图→才认为产线已停(应对变速和暂停,不拦截正常生产)
		private long _lastAcceptedIn13Tick = 0;  // 上一次接受的触发(用于去抖, <2s的重复信号忽略)
		private long _lastCarouselRefreshTicks = 0;  // 上一次轮播刷新时间(节流用)
													 // 侧面触发防重: IN5=皮带停止, IN13=工件到位, 两者同时为1且未拍过才触发
		private int _sideTriggered = 0;  // 0=未触发, 1=当前工件已触发过
		private long _lastSideStatusLogTicks = 0;  // 上一次侧面状态日志时间
		private const int IN5_BELT_STOP = 5;
		private const int IN13_POSITION = 13;

		// ========== 数据管理 ==========
		private SkuDatabase _skuDb;
		private SkuData _currentSku;
		private PerformanceMonitor _perfMonitor;
		private SystemResourceMonitor _sysResMonitor;
		private DetectionParameters _detectionParams;
		private SQLiteHelper _dbHelper;

		// ========== 高速保存器 ==========
		private HighSpeedImageSaver _imageSaver;

		// ========== 产品ID计数器 ==========
		private long _productIdCounter = 0;

		// ========== BGR/RGB通道验证(仅首帧) ==========
		private static bool _bgrVerifyDone = false;

		// ========== 状态灯控件 ==========
		private List<UILight> _frontStatusLights = new List<UILight>();
		private List<UILight> _backStatusLights = new List<UILight>();
		private List<UILight> _upperStatusLights = new List<UILight>();
		private List<UILight> _lowerStatusLights = new List<UILight>();
		private List<UILight> _leftSideStatusLights = new List<UILight>();
		private List<UILight> _rightSideStatusLights = new List<UILight>();

		// ========== 轮播图索引标签 ==========
		private Label _endFaceIndexLabel;
		private Label _sideIndexLabel;

		// ========== SKU搜索 ==========
		private ComboBox _skuSearchCombo;

		// ========== 班次 ==========
		private string _currentShift = "";
		private DateTime _shiftStartTime;
		private System.Timers.Timer _shiftCheckTimer;
		private System.Windows.Forms.Timer _statusPollTimer;

		// ========== 工具类 ==========
		private bool _isClosing = false;
		private Loading _loadingForm;

		// ========== 公共成员（供其他窗体访问）==========
		public IntPtr g_handle => _motionMgr?.Handle ?? IntPtr.Zero;
		public HCModbusClass modbusClass = new HCModbusClass();

		public DaHuaSDK camera1SDK, camera2SDK, camera3SDK, camera4SDK;
		public DaHuaSDK camera5SDK, camera6SDK, camera7SDK, camera8SDK;

		// ========== SKU搜索 ==========
		private TextBox _skuSearchTextBox;
		private ListBox _skuResultListBox;
		private Panel _skuSearchPanel;

		// ========== 公共访问方法 ==========
		public IntPtr GetMotionHandle() => g_handle;
		public HCModbusClass GetModbusClass() => modbusClass;

		public DaHuaSDK GetCamera1() => camera1SDK;
		public DaHuaSDK GetCamera2() => camera2SDK;
		public DaHuaSDK GetCamera3() => camera3SDK;
		public DaHuaSDK GetCamera4() => camera4SDK;
		public DaHuaSDK GetCamera5() => camera5SDK;
		public DaHuaSDK GetCamera6() => camera6SDK;
		public DaHuaSDK GetCamera7() => camera7SDK;
		public DaHuaSDK GetCamera8() => camera8SDK;

		public MainFrm(Loading loadingForm = null)
		{
			_loadingForm = loadingForm;
			InitializeComponent();
			this.FormClosing += MainFrm_FormClosing;
		}

		/// <summary>更新Loading画面进度: 调用_loadingForm.UpdateProgress(百分比, 提示文字), 每步初始化后调用</summary>
		private void UpdateLoadingProgress(int percent, string message)
		{
			_loadingForm?.UpdateProgress(percent, message);
		}

		#region 窗体加载

		/// <summary>
		/// 系统启动入口, 按依赖顺序初始化所有子系统
		/// 流程: DB→参数→SKU→性能监控→图像保存器→硬件→相机→AI模型→工位处理器→UI界面→班次检查
		/// 最后: 最大化窗口 + 添加手动测试按钮
		/// </summary>
		private async void MainFrm_Load(object sender, EventArgs e)
		{
			try
			{
				Logger.Info("========== 系统启动 ==========");
				Logger.Info($"版本: 2026-07-27-PLC重构 构建时间: {System.IO.File.GetLastWriteTime(System.Reflection.Assembly.GetExecutingAssembly().Location):yyyy-MM-dd HH:mm:ss}");

				uiMonitor1.Activte = true;
				uiMonitor2.Activte = true;

				// 初始化数据库
				UpdateLoadingProgress(5, "正在初始化数据库...");
				Logger.Info("正在初始化数据库...");
				_dbHelper = new SQLiteHelper();

				// 加载检测参数
				UpdateLoadingProgress(10, "正在加载检测参数...");
				Logger.Info("正在加载检测参数...");
				_detectionParams = DetectionParameters.Instance;

				// 初始化SKU数据库（优先使用预加载）
				UpdateLoadingProgress(15, "正在加载SKU数据...");
				Logger.Info("正在加载SKU数据...");
				if (PreloadedSkuDb != null)
				{
					_skuDb = PreloadedSkuDb;
					PreloadedSkuDb = null;
					Logger.Info("使用预加载的SKU数据，跳过重复加载");
				}
				else
				{
					_skuDb = new SkuDatabase();
					_skuDb.LoadData();
				}

				// 初始化性能监控
				UpdateLoadingProgress(20, "正在初始化性能监控...");
				Logger.Info("正在初始化性能监控...");
				_perfMonitor = new PerformanceMonitor();

				// 初始化系统资源监控（每5s采样CPU/内存, 每30s输出报告）
				_sysResMonitor = new SystemResourceMonitor(5000, 30);

				// 配置ThreadPool以充分利用14核CPU (i5-14600K: 6P+8E)
				// 默认min threads过少会导致Task.Run排队等待
				int minWorker, minIo;
				System.Threading.ThreadPool.GetMinThreads(out minWorker, out minIo);
				int cores = Environment.ProcessorCount;
				int newMinWorker = Math.Max(minWorker, cores * 2); // 每核2线程
				System.Threading.ThreadPool.SetMinThreads(newMinWorker, minIo);
				Logger.Info($"[Sys] ThreadPool: cores={cores} minWorker={minWorker}→{newMinWorker}");

				// 初始化高速保存器
				UpdateLoadingProgress(25, "正在初始化图像保存器...");
				Logger.Info("正在初始化图像保存器...");
				_imageSaver = new HighSpeedImageSaver("主保存器", 4, 500);
				_Config.PlcIP = "192.168.0.10";
				_Config.PlcPort = 102;
				// 初始化硬件（运动控制卡 + PLC）
				UpdateLoadingProgress(30, "正在连接运动控制卡...");
				Logger.Info("正在初始化硬件...");
				InitHardware();
				UpdateLoadingProgress(45, "运动控制卡连接成功");

				// 初始化相机
				UpdateLoadingProgress(50, "正在初始化相机SDK...");
				Logger.Info("正在初始化相机SDK...");
				InitCameras();
				UpdateLoadingProgress(65, "相机初始化完成");

				// 初始化AI模型
				UpdateLoadingProgress(70, "正在加载AI模型...");
				Logger.Info("正在加载AI模型...");
				InitAiModels();
				UpdateLoadingProgress(85, "AI模型加载完成");

				// 从配置读取工位启用开关
				FrontEnabled = _detectionParams.Station.FrontEnabled;
				BackEnabled = _detectionParams.Station.BackEnabled;
				EndFaceEnabled = _detectionParams.Station.EndFaceEnabled;
				SideEnabled = _detectionParams.Station.SideEnabled;
				Logger.Info($"工位开关: 正面={FrontEnabled}, 背面={BackEnabled}, 端面={EndFaceEnabled}, 侧面={SideEnabled}");
				// 初始化工位处理器
				UpdateLoadingProgress(88, "正在初始化工位处理器...");
				Logger.Info("正在初始化工位处理器...");
				InitStations();

				// 初始化UI
				UpdateLoadingProgress(90, "正在初始化界面...");
				Logger.Info("正在初始化界面...");
				InitUI();

				// 绑定统计控件
				UpdateLoadingProgress(92, "正在绑定统计控件...");
				Logger.Info("正在绑定统计控件...");
				BindStatisticsControls();

				// 启动班次检查
				StartShiftCheckTimer();

				// 改用System.Timers.Timer(后台线程)替代Forms.Timer(UI线程), 硬件IO不再阻塞UI
				// 轮询间隔从500ms提升到1000ms, 状态灯是给人看的, 1秒刷新足够了
				var _statusBgTimer = new System.Timers.Timer(1000);
				_statusBgTimer.Elapsed += (_, evt) => StatusPollTick();
				_statusBgTimer.AutoReset = true;
				_statusBgTimer.Start();
				_statusPollTimer = new System.Windows.Forms.Timer { Interval = 1000 }; // 保留引用兼容

				// 刷新显示
				RefreshCarouselDisplays();
				// 验证数据是否正确加载
				var testSku = _skuDb.GetBySkuNumber("181712303");
				if (testSku != null)
				{
					Logger.Info($"测试SKU: {testSku.SkuNumber}, P={testSku.P}, Z={testSku.Z}, MM={testSku.MM}");
				}

				this.WindowState = FormWindowState.Maximized;

				UpdateLoadingProgress(100, "系统初始化完成，准备启动...");
				Logger.Info("系统初始化完成");
			ModelPerfTracker.Start();  // 启动模型耗时周期统计(5min)

				xlPictureBox5.ISRealTimeDisplay = true;
				xlPictureBox6.ISRealTimeDisplay = true;



				//// 添加手动测试按钮
				//var btnTest = new Sunny.UI.UIButton
				//{
				//	Text = "手动测试",
				//	Size = new System.Drawing.Size(100, 36),
				//	Location = new System.Drawing.Point(800, 10),
				//	Anchor = AnchorStyles.Top | AnchorStyles.Right,
				//	FillColor = System.Drawing.Color.FromArgb(0, 122, 204),
				//	Radius = 6,
				//	Font = new System.Drawing.Font("微软雅黑", 9F)
				//};
				//btnTest.Click += BtnManualTest_Click;
				//this.Controls.Add(btnTest);
				//btnTest.BringToFront();
			}
			catch (Exception ex)
			{
				Logger.Error($"系统初始化失败: {ex.Message}\r\n{ex.StackTrace}");
				MessageBox.Show($"系统初始化失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
				Application.Exit();
			}
		}

		#endregion

		#region 硬件初始化

		/// <summary>
		/// 初始化硬件层: 运动控制卡 + PLC
		/// 流程: ZMC(从DetectionParams.ControlIp读取IP→Connect→InitAxes→ApplyAxisParams) + PLC(从SystemConfig读取IP/端口→Connect)
		/// 注意事项: UseSimulateMode=false使用真实硬件, 连接失败时UI状态灯变红
		/// </summary>
		private void InitHardware()
		{
			// 是否使用模拟模式（没有真实硬件时使用）
			bool useSimulateMode = false;  // 改为 false 使用真实硬件

			// 运动控制卡
			string controlIp = _detectionParams.Motion.ControlIp;
			_motionMgr = new MotionControlManager(controlIp, useSimulateMode);
			_staticMotionMgr = _motionMgr;

			if (_motionMgr.Connect())
			{
				_motionMgr.InitAxes();
				var axisCfg = CommonLib.AxisParamConfig.Load();
				_motionMgr.ApplyAxisParams(axisCfg);
				cszmcaux.zmcaux.ZAux_Direct_SetInvertIn(_motionMgr.Handle, 8, 1);
				_motionMgr.SetHardwareSafetyAlarm(0, 8);
				Logger.Info("轴" + axisCfg.Axis + "参数已从本地加载并应用");
				if (MotionState != null) MotionState.State = UILightState.On;
				Logger.Info("运动控制卡初始化成功");
			}
			else
			{
				if (MotionState != null) MotionState.State = UILightState.Off;
				Logger.Warning("运动控制卡连接失败，将使用模拟模式");
			}

			// 加载缺陷→PLC配置
			var _ = Config.StationDefectConfig.Instance;

			// 初始化 S7-1500 PLC 通讯
			if (!useSimulateMode)
			{
				_s7Plc = new PLC调试.Class.S7_1500Class();

				// 订阅PLC状态变更事件 — 心跳检测到断线/重连时更新UI指示灯
				_s7Plc.EventConnectState += (connected, msg) =>
				{
					this.BeginInvoke(new Action(() =>
					{
						if (PlcState != null)
							PlcState.State = connected ? UILightState.On : UILightState.Off;
						if (connected)
							Logger.Info($"[PLC状态] 已连接: {msg}");
						else
							Logger.Warning($"[PLC状态] 已断开: {msg}");
					}));
				};

				if (_s7Plc.ConnectModbus())
				{
					if (PlcState != null) PlcState.State = UILightState.On;
					Logger.Info("S7-1500 PLC连接成功, 心跳已启动(200ms DB47.DBX12.5)");
				}
				else
				{
					if (PlcState != null) PlcState.State = UILightState.Off;
					Logger.Warning("S7-1500 PLC连接失败, 将无法发送检测结果");
				}
				_plcResultService = new Hardware.PlcResultService(_s7Plc, useSimulateMode);
			}
			else
			{
				Logger.Info("模拟模式: 跳过PLC连接, PlcResultService使用模拟输出");
				_plcResultService = new Hardware.PlcResultService(null, useSimulateMode);
			}
		}

		/// <summary>
		/// 初始化8个大华工业相机, 并启动触发管理器
		/// 流程: new DaHuaSDK → SetCameraInterface(this) → 订阅OnImage → SetCameraByKey(SN) → Open
		///       → StopStreamGrabber → AcquisitionMode(0单帧) → TriggerMode(1触发) → TriggerSource(1外触发)
		///       → SetExposureTime → StartStreamGrabber → 赋值cameraNSDK字段
		/// 注意事项: Open后先Stop再配模式后才Start(否则模式可能不生效)
		/// 最后: 初始化CameraTriggerManager(3线程) + ZMC心跳(150ms)
		/// </summary>
		private void InitCameras()
		{
			bool useSimulateMode = _detectionParams.Camera.GetSimulateMode();
			var camCfg = _detectionParams.Camera;
			Logger.Info($"========== 初始化相机（模拟模式: {useSimulateMode}） ==========");

			// 相机配置数组: (字段引用, 序列号, 名称, 相机ID)
			var cameraConfigs = new (DaHuaSDK field, string sn, string name, int id)[]
			{
				(null, camCfg.Camera1SN, "正面左", 1),
				(null, camCfg.Camera2SN, "正面右", 2),
				(null, camCfg.Camera3SN, "上端面", 3),
				(null, camCfg.Camera4SN, "下端面", 4),
				(null, camCfg.Camera5SN, "背面左", 5),
				(null, camCfg.Camera6SN, "背面右", 6),
				(null, camCfg.Camera7SN, "左侧面", 7),
				(null, camCfg.Camera8SN, "右侧面", 8),
			};

			int successCount = 0;
			foreach (var cfg in cameraConfigs)
			{
				try
				{
					if (useSimulateMode)
					{
						Logger.Info($"[Camera{cfg.id}] {cfg.name} 模拟模式初始化成功");
						UpdateCameraState(cfg.id, true);
						successCount++;
						continue;
					}

					if (string.IsNullOrEmpty(cfg.sn))
					{
						Logger.Warning($"[Camera{cfg.id}] {cfg.name} 序列号未配置，跳过");
						continue;
					}

					Logger.Info($"[Camera{cfg.id}] {cfg.name} 开始初始化, SN={cfg.sn}");

					var sdk = new DaHuaSDK();
					Logger.Debug($"[Camera{cfg.id}] DaHuaSDK实例已创建");

					sdk.SetCameraInterface(this);
					Logger.Debug($"[Camera{cfg.id}] SetCameraInterface完成");

					// 订阅OnImage事件
					SubscribeCameraImageEvent(sdk, cfg.id);
					Logger.Debug($"[Camera{cfg.id}] OnImage事件已订阅");

					sdk.SetCameraByKey(cfg.sn);
					Logger.Debug($"[Camera{cfg.id}] SetCameraByKey完成 SN={cfg.sn}");

					sdk.Open();
					Logger.Info($"[Camera{cfg.id}] {cfg.name} Open成功");

					sdk.StopStreamGrabber();
					Logger.Debug($"[Camera{cfg.id}] StopStreamGrabber完成");

					sdk.SetAcquisitionMode(0);
					sdk.SetTriggerMode(1);
					sdk.setTriggerSource(1);
					Logger.Debug($"[Camera{cfg.id}] 模式设置: AcquisitionMode=0, TriggerMode=1, TriggerSource=1");

					// 设置曝光时间
					SetCameraExposure(sdk, cfg.id, camCfg);

					sdk.StartStreamGrabber();
					Logger.Info($"[Camera{cfg.id}] {cfg.name} StartStreamGrabber完成, 初始化成功");

					// 赋值给公共字段
					SetCameraField(cfg.id, sdk);

					UpdateCameraState(cfg.id, true);
					successCount++;
				}
				catch (Exception ex)
				{
					Logger.Error($"[Camera{cfg.id}] {cfg.name} 初始化失败: {ex.Message}\r\n{ex.StackTrace}");
					UpdateCameraState(cfg.id, false);
				}
			}

			Logger.Info($"========== 相机初始化完成: {successCount}/{cameraConfigs.Length} ==========");

			// 初始化触发管理器
			if (_motionMgr != null && _motionMgr.IsConnected && !useSimulateMode)
			{
				CameraTriggerConfig.ApplyIn12EdgeMode();
				// 从DetectionParams加载触发脉冲宽度(ms)，覆盖硬编码默认值
				int pw = _detectionParams.Camera.PulseWidthMs;
				if (pw > 0)
				{
					CameraTriggerConfig.DefaultPulseWidthMs = pw;
					foreach (var kv in CameraTriggerConfig.TriggerConfigs)
						kv.Value.PulseWidthMs = pw;
				}
				_triggerMgr = new CameraTriggerManager(_motionMgr, useSimulateMode);
				_triggerMgr.OnTriggered += OnCameraTriggered;
				Hardware.CameraTriggerManager.ExternalTriggerEnabled = true; // 相机触发由外部ZMC BASIC程序控制
				_triggerMgr.Start();
				Logger.Info("触发管理器已启动");
				// 每150ms重置心跳标志，ZMC BASIC程序监控此标志检测PC是否存活
				_motionMgr?.StartHeartbeat();
			}
			else
			{
				Logger.Warning("运动控制卡未连接或模拟模式，跳过触发管理器初始化");
			}
		}

		/// <summary>根据相机ID订阅对应的OnImage事件</summary>
		private void SubscribeCameraImageEvent(DaHuaSDK sdk, int cameraId)
		{
			switch (cameraId)
			{
				case 1: sdk.OnImage += OnCamera1Image; break;
				case 2: sdk.OnImage += OnCamera2Image; break;
				case 3: sdk.OnImage += OnCamera3Image; break;
				case 4: sdk.OnImage += OnCamera4Image; break;
				case 5: sdk.OnImage += OnCamera5Image; break;
				case 6: sdk.OnImage += OnCamera6Image; break;
				case 7: sdk.OnImage += OnCamera7Image; break;
				case 8: sdk.OnImage += OnCamera8Image; break;
			}
		}

		/// <summary>将DaHuaSDK实例赋值给对应的公共字段</summary>
		private void SetCameraField(int cameraId, DaHuaSDK sdk)
		{
			switch (cameraId)
			{
				case 1: camera1SDK = sdk; break;
				case 2: camera2SDK = sdk; break;
				case 3: camera3SDK = sdk; break;
				case 4: camera4SDK = sdk; break;
				case 5: camera5SDK = sdk; break;
				case 6: camera6SDK = sdk; break;
				case 7: camera7SDK = sdk; break;
				case 8: camera8SDK = sdk; break;
			}
		}

		/// <summary>设置相机曝光时间</summary>
		private void SetCameraExposure(DaHuaSDK sdk, int cameraId, DetectionParameters.CameraParams camCfg)
		{
			try
			{
				double exp = 5000.0;
				switch (cameraId)
				{
					case 1: exp = camCfg.ExposureTime1; break;
					case 2: exp = camCfg.ExposureTime2; break;
					case 3: exp = camCfg.ExposureTime3; break;
					case 4: exp = camCfg.ExposureTime4; break;
					case 5: exp = camCfg.ExposureTime5; break;
					case 6: exp = camCfg.ExposureTime6; break;
					case 7: exp = camCfg.ExposureTime7; break;
					case 8: exp = camCfg.ExposureTime8; break;
				}
				sdk.SetExposureTime(exp);
				Logger.Debug($"[Camera{cameraId}] 曝光时间: {exp}us");
			}
			catch (Exception ex)
			{
				Logger.Warning($"[Camera{cameraId}] 设置曝光时间失败: {ex.Message}");
			}
		}

		/// <summary>
		/// 相机触发回调 - 当检测到触发信号并输出脉冲后调用
		/// </summary>
		/// <param name="cameraId">被触发的相机ID</param>
		/// <summary>
		/// 相机触发回调(CameraTriggerManager线程)
		/// Camera7(IN13↑): 检查侧面轴位置→不在起点则预归位(带安全锁监控)→超时15s
		/// Camera8(IN13↓): 空闲→StartDetection | 正忙→标记_sideTriggerPending→后台轮询等完成后自动启动
		/// 参数: cameraId = 被触发的相机ID(7或8)
		/// 注意事项: 在MonitorLoop线程(Highest+CPU绑定)中执行, 耗时操作全部Task.Run异步
		/// </summary>

		private bool IsAxisInitialized()
		{
			try
			{
				if (_motionMgr == null || !_motionMgr.IsConnected) return false;
				ushort[] mbArr = { 0 };
				cszmcaux.zmcaux.ZAux_Modbus_Get4x(_motionMgr.Handle, 100, 1, mbArr);
				return mbArr[0] == 1;
			}
			catch { return false; }
		}
		private void OnCameraTriggered(int cameraId)
		{
			// 侧面工位：
			// IN13上升沿(Camera7) → 检查轴位置，不在起点则预归位
			// 注意: 侧面工位正在执行检测时(_sideStation.IsMoving=true)不干涉轴运动
			//    因为StartMotion正在控制轴从起点→终点→起点，中途IN13↑是正常现象
			if (cameraId == 7 && SideEnabled && IsAxisInitialized() && _sideStation != null && _sideStation.MotionEnabled && _motionMgr != null && _motionMgr.IsConnected)
			{
				// 侧面工位正在检测中，不干涉运动控制
				if (_sideStation.IsMoving)
				{
					// 不记录日志（正常现象，IN13上升沿是轴经过传感器位置时的正常信号）
					return;
				}
				float curPos = _motionMgr.GetPosition(_sideStation.SideAxis);
				float startPos = _sideStation.StartPosition;
				if (Math.Abs(curPos - startPos) > 0.5f)
				{
					Logger.Info($"[Side] IN13↑ 轴不在起点(cur={curPos:F1}, target={startPos:F1})，预归位");
					int axis = _sideStation.SideAxis;
					int lockPort = _sideStation.SafetyLockPort;
					bool lockHigh = _sideStation.SafetyLockActiveHigh;
					bool returnToStart = _sideStation.RecoveryMode == SideStationProcessor.SafetyRecovery.ReturnToStart;
					Task.Run(() =>
					{
						// 安全锁检查
						if (!_motionMgr.CheckSafetyLock(lockPort, lockHigh))
						{
							Logger.Info($"[Side] 预归位等待安全锁 IN{lockPort}=0...");
							while (!_motionMgr.CheckSafetyLock(lockPort, lockHigh))
								Thread.Sleep(10);
							Logger.Info($"[Side] 安全锁释放，开始预归位");
						}
						_motionMgr.SetSpeed(axis, _sideStation.ReturnSpeed);
						_motionMgr.MoveAbs(axis, startPos);
						// 运动中监控安全锁
						bool stopped = false;
						var sw = System.Diagnostics.Stopwatch.StartNew();
						while (sw.ElapsedMilliseconds < 15000)
						{
							if (!_motionMgr.IsMoving(axis)) break;
							if (!_motionMgr.CheckSafetyLock(lockPort, lockHigh))
							{
								if (!stopped)
								{
									Logger.Warning("[Side] 预归位中安全锁触发! 急停");
									_motionMgr.EmergencyStop(axis);
									stopped = true;
								}
								Thread.Sleep(10); continue;
							}
							if (stopped)
							{
								stopped = false;
								Logger.Info("[Side] 安全锁恢复，继续预归位");
								_motionMgr.MoveAbs(axis, startPos);
							}
							Thread.Sleep(10);
						}
						Logger.Info($"[Side] 预归位完成 pos={_motionMgr.GetPosition(axis):F1}");
					});
				}
			}
			// IN13下降沿(Camera8) → 尝试 IN5+IN13 组合触发
			if (cameraId == 8 && SideEnabled && IsAxisInitialized() && _sideStation != null && _sideStation.MotionEnabled)
			{
				TrySideTrigger(DateTime.Now.Ticks);
			}

			// 侧面触发轮询: 每次触发器事件都检查 IN5+IN13 电平
			//   解锁: _sideTriggered=1 且 (IN13=0 或 IN5=0) → 重置锁
			//   触发: _sideTriggered=0 且 IN5=1+IN13=1 → TrySideTrigger
			//   排队: 侧面空闲且 pending>0 → 消费排队
			if (_sideStation != null && _sideStation.MotionEnabled && SideEnabled
				&& _motionMgr != null && _motionMgr.IsConnected)
			{
				bool in5 = false, in13 = false;
				bool canRead = _motionMgr.GetInput(IN5_BELT_STOP, out in5)
							& _motionMgr.GetInput(IN13_POSITION, out in13);

				if (canRead)
				{
					// 锁管理: IN5=1(皮带运行) 或 IN13=0(工件离开) → 解锁
					if (Interlocked.CompareExchange(ref _sideTriggered, 0, 0) == 1)
					{
						if (in5 || !in13)
						{
							Interlocked.Exchange(ref _sideTriggered, 0);
							Logger.Info($"[Side] 🔓 触发锁解锁 IN5={in5} IN13={in13}");
						}
					}
					// 未锁定 + IN5=0(皮带停) + IN13=1(工件到位) → 尝试触发
					else if (!in5 && in13)
					{
						TrySideTrigger(DateTime.Now.Ticks);
					}

					// 周期状态日志(每30s): 方便排查为什么侧面不触发
					long nowTicks2 = DateTime.Now.Ticks;
					long lastLog = Interlocked.Read(ref _lastSideStatusLogTicks);
					if (nowTicks2 - lastLog > 30L * 10000 * 1000)
					{
						Interlocked.Exchange(ref _lastSideStatusLogTicks, nowTicks2);
						int locked = Interlocked.CompareExchange(ref _sideTriggered, 0, 0);
						long pending = Interlocked.Read(ref _sidePendingCount);
						long lastImg = Interlocked.Read(ref _lastEndFaceImageTicks);
						double sinceImg = lastImg > 0 ? (nowTicks2 - lastImg) / 10000.0 / 1000 : -1;
						Logger.Info($"[Side] 状态 IN5={in5}({(in5 ? "运行" : "停止")}) IN13={in13}({(in13 ? "有料" : "无料")}) 锁={locked} 排队={pending} 距上次端面图={sinceImg:F0}s 运动={_sideStation.IsMoving}");
					}
				}

				// 排队消费: 空闲时处理积压pending
				if (!_sideStation.IsMoving)
				{
					long pending = Interlocked.Read(ref _sidePendingCount);
					if (pending > 0)
					{
						long after = Interlocked.Decrement(ref _sidePendingCount);
						if (after >= 0)
						{
							Logger.Info("[Side] 排队触发(pending剩余=" + after + ")");
							TrySideTrigger(DateTime.Now.Ticks);
						}
						else
						{
							Interlocked.Increment(ref _sidePendingCount);
						}
					}
				}
			}
		}
		/// <summary>
		/// 侧面触发检查: IN5(皮带停止) + IN13(工件到位) + !_sideTriggered + !IsMoving → StartDetection
		/// 触发后设 _sideTriggered=1 防重, IN13=0或IN5=0时解锁
		/// </summary>
		private int _sideNoEndFaceWarnCount = 0;
	private void TrySideTrigger(long nowTicks)
	{
		if (_sideStation == null) return;
		if (_motionMgr == null || !_motionMgr.IsConnected) return;

		// 忙: 排队等待空闲后消费
		if (_sideStation.IsMoving)
		{
			long p = Interlocked.Increment(ref _sidePendingCount);
			Logger.Debug($"[Side] 侧面正忙, IN5+IN13触发排队(pending={p})");
			return;
		}

		bool in5State, in13State;
		if (!_motionMgr.GetInput(IN5_BELT_STOP, out in5State)
			|| !_motionMgr.GetInput(IN13_POSITION, out in13State))
		{
			Logger.Warning("[Side] TrySideTrigger: 读取IN5/IN13失败");
			return;
		}

		// IN5=0(皮带停止), IN13=1(工件到位) — 条件不满足, 静默(每秒检查, 不刷屏)
		if (in5State || !in13State) return;

		if (Interlocked.CompareExchange(ref _sideTriggered, 0, 0) == 1) return; // 已锁, 静默

		// 活件检测: 端面收到过图像
		long lft = Interlocked.Read(ref _lastEndFaceImageTicks);
		if (lft == 0 || (nowTicks - lft) > NoProductTimeoutTicks)
		{
			// 每30次(约30s)告警一次, 避免刷屏
			if (Interlocked.Increment(ref _sideNoEndFaceWarnCount) % 30 == 1)
				Logger.Warning($"[Side] TrySideTrigger: 无端面图像, 判定无活件 (距上次端面图={(lft>0?(nowTicks-lft)/10000.0/1000:-1):F0}s)");
			return;
		}
		Interlocked.Exchange(ref _sideNoEndFaceWarnCount, 0);  // 重置计数

		// 去抖: 距上次接受 <2s
		long lastAccepted = Interlocked.Read(ref _lastAcceptedIn13Tick);
		if (lastAccepted > 0 && (nowTicks - lastAccepted) / 10000.0 < 2000) return;

		Interlocked.Exchange(ref _lastAcceptedIn13Tick, nowTicks);
		Interlocked.Exchange(ref _sideTriggered, 1);
		Logger.Info($"[Side] IN5+IN13触发! IN5={in5State} IN13={in13State} (皮带停+工件到位)");
		_sideStation.StartDetection();
	}

		/// <summary>
		/// 加载AI模型(共11个)
		/// 流程: 优先用Program.cs预加载的PreloadedModels, 否则ModelPathConfig→AiModelManager→LoadAllModels
		/// GPU分配: GPU0=YOLO(目标检测), GPU1=ViMo(OCR/分割/分类)
		/// </summary>
		private void InitAiModels()
		{
			if (PreloadedModels != null)
			{
				_aiModels = PreloadedModels;
				PreloadedModels = null;
				Logger.Info("使用预加载的AI模型，跳过重复加载");
				return;
			}
			var modelConfig = ModelPathConfig.LoadFromSysConfig();
			_aiModels = new AiModelManager(modelConfig);
			_aiModels.LoadAllModels();
		}

		/// <summary>
		/// 初始化4个工位处理器(Front/Back/EndFace/Side)
		/// 加载: AxisParams.json(运动轴参数) + DetectionParams.json(Enable*开关/安全锁/边缘模式) + SkuDatabase(SKU)
		/// 绑定: OnResultReady→OnStationResult | 侧面额外OnRealTimeDisplay→xlPic5/6
		/// </summary>
		private void InitStations()
		{
			string imgPath = _detectionParams.Save.ImageSavePath;
			// 恢复上次SKU
			string lastSku = _detectionParams.LastSkuNumber;
			if (!string.IsNullOrEmpty(lastSku))
			{
				var saved = _skuDb.GetBySkuNumber(lastSku);
				if (saved != null)
				{
					_currentSku = saved;
					Logger.Info($"恢复上次SKU: {lastSku}, P={_currentSku.P}");
				}
				else _currentSku = _skuDb.Search("").FirstOrDefault() ?? new SkuData { P = 8, Z = 2, MM = 42 };
			}
			else _currentSku = _skuDb.Search("").FirstOrDefault() ?? new SkuData { P = 8, Z = 2, MM = 42 };

			_frontStation = new FrontStationProcessor(_aiModels, _detectionParams);
			_frontStation.OnResultReady += OnStationResult;
			_frontStation.ReverseBoxOrder = _detectionParams.Station.FrontReverseBox;
			_frontStation.UpdateSku(_currentSku);
			_frontStation.InitThresholdsFromModel();  // 从模型best.json加载阈值
			_frontStation.EnableBoxBreakCheck = _detectionParams.Front.EnableBoxBreakCheck;
			_frontStation.OnPlcResult += (codes, p, ok, ng) =>
			{
				if (_plcResultService != null && _frontStation.StatusList != null && _frontStation.StatusList.Count > 0)
				{
					var statusList = _frontStation.StatusList;
					if (_pendingTestStation == Hardware.StationType.Front)
					{
						// 测试模式: 弹窗勾选后手动发送
						_pendingTestStation = null;
						this.BeginInvoke(new Action(() =>
							new PlcTestSendForm(Hardware.StationType.Front, statusList, _plcResultService, p).ShowDialog()));
					}
					else
					{
						var plcSw = System.Diagnostics.Stopwatch.StartNew();
						Config.StationDefectConfig.Instance.Resolve("Front", statusList, out ushort rejectBits, out int stopLevel, out string stopReason);
						Logger.Info($"[PLC-Front] P={p} OK={ok} NG={ng} 逐盒:[{string.Join("][", statusList)}] → 剔除位=0x{rejectBits:X4} 停机={stopLevel}{(stopReason.Length > 0 ? " 原因:" + stopReason : "")}");
						if (!_plcResultService.SendStationResult(Hardware.StationType.Front, rejectBits, stopLevel, p))
							Logger.Error("[PLC-Front] SendStationResult 返回 false!");
						if (!_plcResultService.SendStationComplete(Hardware.StationType.Front))
							Logger.Error("[PLC-Front] SendStationComplete 返回 false!");
						double plcMs = plcSw.Elapsed.TotalMilliseconds;
						Logger.Info($"[PLC-Front] ⏱ PLC发送耗时={plcMs:F2}ms");
						if (plcMs > 50) Logger.Warning($"[PLC-Front] ⚠ PLC发送偏慢: {plcMs:F0}ms (正常<10ms, 请检查S7-1500网络)");
						ModelPerfTracker.RecordDefects("Front", statusList);
						ModelPerfTracker.RecordPlcResult("Front", ok, ng);
					}
				}
			};
			_frontStation.Start();

			_endFaceStation = new EndFaceStationProcessor(_aiModels, imgPath, _currentSku.P, _imageSaver, _perfMonitor);
			_endFaceStation.OnResultReady += OnStationResult;
			_endFaceStation.ReverseBoxOrder = _detectionParams.Station.EndFaceReverseBox;
			_endFaceStation.OnStatusUpdate += OnEndFaceStatusUpdate;
			_endFaceStation.UpdateSku(_currentSku);
			_endFaceStation.InitThresholdsFromModel();  // 从模型best.json加载阈值
			_endFaceStation.EnableUpperDefectCheck = _detectionParams.EndFace.EnableUpperDefectCheck;
			_endFaceStation.Start();

			_backStation = new BackStationProcessor(_aiModels, imgPath, _currentSku, _imageSaver, _perfMonitor);
			_backStation.ReverseBoxOrder = _detectionParams.Station.BackReverseBox;
			_backStation.OnResultReady += OnStationResult;
			_backStation.InitThresholdsFromModel();  // 从模型best.json加载阈值
			_backStation.EnableBarcodeCheck = _detectionParams.Back.EnableBarcodeCheck;
			_backStation.EnableHookCheck = _detectionParams.Back.EnableHookCheck;
			_backStation.EnableBoxBreakCheck = _detectionParams.Back.EnableBoxBreakCheck;
			_backStation.Start();

			_sideStation = new SideStationProcessor(_aiModels, imgPath, _currentSku, _motionMgr, _imageSaver, _perfMonitor);
			_sideStation.OnResultReady += OnStationResult;
			_sideStation.OnResultReady += (result) =>
			{
				// 侧面空触发检测: 记录本次收图数, 连续空触发超阈值告警
				int curCount = _sideStation.ImageCount;
				Interlocked.Exchange(ref _lastSideImageCount, curCount);
				if (curCount == 0)
				{
					long emptyCnt = Interlocked.Increment(ref _sideEmptyCycleCount);
					if (emptyCnt <= 3 || emptyCnt % 10 == 0)
						Logger.Warning("[Side] ⚠ 空触发: 本周期未收到任何侧面图像! (连续空触发=" + emptyCnt + "次, 可能没有盒子通过)");
				}
				else
				{
					Interlocked.Exchange(ref _sideEmptyCycleCount, 0);
				}
			};
			_sideStation.OnStatusUpdate += OnSideStatusUpdate;
			_sideStation.OnRealTimeDisplay += (side, bmp) =>
			{
				if (bmp == null) return;
				this.BeginInvoke(new Action(() =>
				{
					if (side == Side.Left)
						UpdatePictureBox(xlPictureBox5, bmp);
					else
						UpdatePictureBox(xlPictureBox6, bmp);
				}));
			};

			// 从AxisParams.json加载运动轴参数（与ControlFrm共用同一配置）
			var axisCfg = AxisParamConfig.Load();
			_sideStation.SideAxis = axisCfg.Axis;
			_sideStation.StartPosition = axisCfg.StartPos;
			_sideStation.EndPosition = axisCfg.EndPos;
			_sideStation.ForwardSpeed = axisCfg.FwdSpeed;
			_sideStation.ReturnSpeed = axisCfg.RetSpeed;
			_sideStation.Accel = axisCfg.Accel;
			_sideStation.Decel = axisCfg.Decel;
			_sideStation.FwdInPort = axisCfg.FwdIn;
			_sideStation.RevInPort = axisCfg.RevIn;
			_sideStation.DatumInPort = axisCfg.DatumIn;

			// 同步IN12边缘模式
			_sideStation.EdgeMode = CameraTriggerConfig.In12EdgeMode == CameraTriggerConfig.SideSensorEdgeMode.RisingRightFallingLeft
				? SideStationProcessor.TriggerEdgeMode.RisingRightFallingLeft
				: SideStationProcessor.TriggerEdgeMode.RisingLeftFallingRight;
			_sideStation.ReverseBoxOrder = _detectionParams.Station.SideReverseBox;
			_sideStation.UseContinuousMode = _detectionParams.Side.UseContinuousMode;
			_sideStation.MissingAsNg = _detectionParams.Side.MissingAsNg;
			int sidePw = _detectionParams.Camera.PulseWidthMs;
			if (sidePw > 0) _sideStation.TriggerPulseMs = sidePw;
			_sideStation.MotionEnabled = _detectionParams.Side.MotionEnabled;
			_sideStation.EnableSideDefectCheck = _detectionParams.Side.EnableSideDefectCheck;
			_sideStation.SafetyLockPort = _detectionParams.Side.SafetyLockPort;
			_sideStation.SafetyLockActiveHigh = false; // SetInvertIn(8,1)反转后强制=false
			_sideStation.RecoveryMode = (SideStationProcessor.SafetyRecovery)_detectionParams.Side.SafetyLockRecovery;
			Logger.Info($"侧面工位配置: Axis={_sideStation.SideAxis} Pos={_sideStation.StartPosition}~{_sideStation.EndPosition} FwdSpd={_sideStation.ForwardSpeed} RetSpd={_sideStation.ReturnSpeed} Acc={_sideStation.Accel}/{_sideStation.Decel} 限位IN{_sideStation.FwdInPort}/{_sideStation.RevInPort}/{_sideStation.DatumInPort} EdgeMode={_sideStation.EdgeMode} Pulse={_sideStation.TriggerPulseMs}ms 安全锁IN={(_sideStation.SafetyLockPort > 0 ? _sideStation.SafetyLockPort.ToString() : "禁用")} 恢复模式={(_sideStation.RecoveryMode == SideStationProcessor.SafetyRecovery.ReturnToStart ? "返回起始位" : "继续执行")}");

			_sideStation.InitThresholdsFromModel();  // 从模型best.json加载阈值
			_sideStation.Start();
		}

		#endregion

		#region 相机回调

		/// <summary>
		/// 旧相机回调入口（保留兼容），新代码使用独立的OnCameraNImage事件
		/// 显示控件映射: xlPictureBox1=正面, xlPictureBox2=背面, xlPictureBox3=上端面, xlPictureBox4=下端面, xlPictureBox5=左侧面, xlPictureBox6=右侧面
		/// </summary>
		/// <summary>旧相机回调入口(保留兼容): 按cameraId分派到各工位处理器, 新代码使用独立OnCameraNImage事件</summary>
		private void OnCameraImageReceived(int cameraId, Bitmap image)
		{
			if (_isClosing || image == null) return;
			long pid = Interlocked.Increment(ref _productIdCounter);

			switch (cameraId)
			{
				case 1: if (FrontEnabled) _frontStation?.OnCam1(image, pid); break;
				case 2: if (FrontEnabled) _frontStation?.OnCam2(image, pid); break;
				case 3: if (EndFaceEnabled) _endFaceStation?.OnCam5(image, pid); break;
				case 4: if (EndFaceEnabled) _endFaceStation?.OnCam6(image, pid); break;
				case 5: if (BackEnabled) _backStation?.OnCam3(image, pid); break;
				case 6: if (BackEnabled) _backStation?.OnCam4(image, pid); break;
				case 7: if (SideEnabled) _sideStation?.OnCam7(image, pid); break;
				case 8: if (SideEnabled) _sideStation?.OnCam8(image, pid); break;
			}
		}

		#region ICamera 接口实现

		/// <summary>ICamera接口: 相机打开回调→通过序列号查找ID→UpdateCameraState(id, true)</summary>
		public void OnCameraOpen(string cameraName, string cameraKey)
		{
			Logger.Info($"[ICamera] 相机打开: Name={cameraName}, Key={cameraKey}");
			int camId = GetCameraIdByKey(cameraKey);
			if (camId > 0) UpdateCameraState(camId, true);
		}

		/// <summary>ICamera接口: 相机关闭回调→UpdateCameraState(id, false)</summary>
		public void OnCameraClose(string cameraName, string cameraKey)
		{
			Logger.Warning($"[ICamera] 相机关闭: Name={cameraName}, Key={cameraKey}");
			int camId = GetCameraIdByKey(cameraKey);
			if (camId > 0) UpdateCameraState(camId, false);
		}

		/// <summary>ICamera接口: 相机掉线回调→UpdateCameraState(id, false)</summary>
		public void OnCameraConnectLoss(string cameraName, string cameraKey)
		{
			Logger.Warning($"[ICamera] 相机掉线: Name={cameraName}, Key={cameraKey}");
			int camId = GetCameraIdByKey(cameraKey);
			if (camId > 0) UpdateCameraState(camId, false);
		}

		#endregion

		#region 各相机OnImage事件处理

		/// <summary>
		/// Camera1图像回调 — DaHuaSDK.OnImage事件触发
		/// 流程: ProductId自增→Interlocked计数→工位开关检查→分发到正面左→FrontStation.OnCam1
		/// 参数: bitmap=原始图像, cameraName=相机名称, cameraKey=序列号
		/// 注意事项: 在SDK回调线程执行, 不做耗时操作(仅入队/配对缓冲)
		/// </summary>
		private void OnCamera1Image(Bitmap bitmap, string cameraName, string cameraKey)
		{
			try
			{
				if (_isClosing || bitmap == null) return;

				// 首帧BGR/RGB通道验证(仅执行一次)
				if (!_bgrVerifyDone)
				{
					_bgrVerifyDone = true;
					try
					{
						var bmpClr = bitmap.GetPixel(0, 0);
						using (var mat = OpenCvSharp.Extensions.BitmapConverter.ToMat(bitmap))
						{
							var vec = mat.At<OpenCvSharp.Vec3b>(0, 0);
							Logger.Info("========== BGR/RGB 通道验证 ==========");
							Logger.Info($"[验证] Bitmap.GetPixel(0,0): B={bmpClr.B} G={bmpClr.G} R={bmpClr.R}");
							Logger.Info($"[验证] Mat[0,0] 通道:    [0]={vec[0]} [1]={vec[1]} [2]={vec[2]}");
							bool isBgr = (vec[0] == bmpClr.B && vec[1] == bmpClr.G && vec[2] == bmpClr.R);
							bool isRgb = (vec[0] == bmpClr.R && vec[1] == bmpClr.G && vec[2] == bmpClr.B);
							string result = isBgr ? "✓ Mat是BGR格式, swapRB:true正确" :
											isRgb ? "✗ Mat是RGB格式! swapRB:true会导致R/B颠倒！请改swapRB:false" :
											"? 通道不完全匹配，需人工判断";
							Logger.Info($"[验证] 结论: {result}");
							Logger.Info("========================================");
						}
					}
					catch (Exception vex)
					{
						Logger.Error($"[验证] BGR检查异常: {vex.Message}");
					}
				}

				long pid = Interlocked.Increment(ref _productIdCounter);
				Interlocked.Exchange(ref _lastFrontImageTicks, DateTime.Now.Ticks);  // 记录正面收图时间, 用于侧面活件判断
				Logger.Info($"[Camera1] 正面左 SN={cameraKey} 收到图像 {bitmap.Width}x{bitmap.Height}, ProductId={pid}");
				Interlocked.Increment(ref Hardware.CameraTriggerManager.ImageReceivedCount[1]);
				if (FrontEnabled) _frontStation?.OnCam1(bitmap, pid);
			}
			catch (Exception ex) { Logger.Error($"[Camera1] OnImage异常: {ex.Message}"); }
		}

		/// <summary>
		/// Camera2图像回调 — DaHuaSDK.OnImage事件触发
		/// 流程: ProductId自增→Interlocked计数→工位开关检查→分发到正面右→FrontStation.OnCam2
		/// 参数: bitmap=原始图像, cameraName=相机名称, cameraKey=序列号
		/// 注意事项: 在SDK回调线程执行, 不做耗时操作(仅入队/配对缓冲)
		/// </summary>
		private void OnCamera2Image(Bitmap bitmap, string cameraName, string cameraKey)
		{
			try
			{
				if (_isClosing || bitmap == null) return;
				long pid = Interlocked.Increment(ref _productIdCounter);
				Interlocked.Exchange(ref _lastFrontImageTicks, DateTime.Now.Ticks);  // 记录正面收图时间, 用于侧面活件判断
				Logger.Info($"[Camera2] 正面右 SN={cameraKey} 收到图像 {bitmap.Width}x{bitmap.Height}, ProductId={pid}");
				Interlocked.Increment(ref Hardware.CameraTriggerManager.ImageReceivedCount[2]);
				if (FrontEnabled) _frontStation?.OnCam2(bitmap, pid);
			}
			catch (Exception ex) { Logger.Error($"[Camera2] OnImage异常: {ex.Message}"); }
		}

		/// <summary>
		/// Camera3图像回调 — DaHuaSDK.OnImage事件触发
		/// 流程: ProductId自增→Interlocked计数→工位开关检查→分发到上端面→EndFaceStation.OnCam5
		/// 参数: bitmap=原始图像, cameraName=相机名称, cameraKey=序列号
		/// 注意事项: 在SDK回调线程执行, 不做耗时操作(仅入队/配对缓冲)
		/// </summary>
		private void OnCamera3Image(Bitmap bitmap, string cameraName, string cameraKey)
		{
			try
			{
				if (_isClosing) { Logger.Warning("[Camera3] 收图时程序正在关闭, 丢弃"); return; }
				if (bitmap == null) { Logger.Warning("[Camera3] 收到null图像"); return; }
				long pid = Interlocked.Increment(ref _productIdCounter);
				Logger.Info($"[Camera3] 上端面 SN={cameraKey} 收到图像 {bitmap.Width}x{bitmap.Height}, ProductId={pid}");
				Interlocked.Increment(ref Hardware.CameraTriggerManager.ImageReceivedCount[3]);
				Interlocked.Exchange(ref _lastEndFaceImageTicks, DateTime.Now.Ticks);
				if (!EndFaceEnabled) { Logger.Warning("[Camera3] 端面工位已禁用, 丢弃图像"); return; }
				if (_endFaceStation == null) { Logger.Error("[Camera3] _endFaceStation为null, 丢弃图像"); return; }
				_endFaceStation.OnCam5(bitmap, pid);
			}
			catch (Exception ex) { Logger.Error($"[Camera3] OnImage异常: {ex.Message}"); }
		}

		/// <summary>
		/// Camera4图像回调 — DaHuaSDK.OnImage事件触发
		/// 流程: ProductId自增→Interlocked计数→工位开关检查→分发到下端面→EndFaceStation.OnCam6
		/// 参数: bitmap=原始图像, cameraName=相机名称, cameraKey=序列号
		/// 注意事项: 在SDK回调线程执行, 不做耗时操作(仅入队/配对缓冲)
		/// </summary>
		private void OnCamera4Image(Bitmap bitmap, string cameraName, string cameraKey)
		{
			try
			{
				if (_isClosing) { Logger.Warning("[Camera4] 收图时程序正在关闭, 丢弃"); return; }
				if (bitmap == null) { Logger.Warning("[Camera4] 收到null图像"); return; }
				long pid = Interlocked.Increment(ref _productIdCounter);
				Logger.Info($"[Camera4] 下端面 SN={cameraKey} 收到图像 {bitmap.Width}x{bitmap.Height}, ProductId={pid}");
				Interlocked.Increment(ref Hardware.CameraTriggerManager.ImageReceivedCount[4]);
				Interlocked.Exchange(ref _lastEndFaceImageTicks, DateTime.Now.Ticks);
				if (!EndFaceEnabled) { Logger.Warning("[Camera4] 端面工位已禁用, 丢弃图像"); return; }
				if (_endFaceStation == null) { Logger.Error("[Camera4] _endFaceStation为null, 丢弃图像"); return; }
				_endFaceStation.OnCam6(bitmap, pid);
			}
			catch (Exception ex) { Logger.Error($"[Camera4] OnImage异常: {ex.Message}"); }
		}

		/// <summary>
		/// Camera5图像回调 — DaHuaSDK.OnImage事件触发
		/// 流程: ProductId自增→Interlocked计数→工位开关检查→分发到背面左→BackStation.OnCam3
		/// 参数: bitmap=原始图像, cameraName=相机名称, cameraKey=序列号
		/// 注意事项: 在SDK回调线程执行, 不做耗时操作(仅入队/配对缓冲)
		/// </summary>
		private void OnCamera5Image(Bitmap bitmap, string cameraName, string cameraKey)
		{
			try
			{
				if (_isClosing || bitmap == null) return;
				long pid = Interlocked.Increment(ref _productIdCounter);
				Logger.Info($"[Camera5] 背面左 SN={cameraKey} 收到图像 {bitmap.Width}x{bitmap.Height}, ProductId={pid}");
				Interlocked.Increment(ref Hardware.CameraTriggerManager.ImageReceivedCount[5]);
				if (BackEnabled) _backStation?.OnCam3(bitmap, pid);
			}
			catch (Exception ex) { Logger.Error($"[Camera5] OnImage异常: {ex.Message}"); }
		}

		/// <summary>
		/// Camera6图像回调 — DaHuaSDK.OnImage事件触发
		/// 流程: ProductId自增→Interlocked计数→工位开关检查→分发到背面右→BackStation.OnCam4
		/// 参数: bitmap=原始图像, cameraName=相机名称, cameraKey=序列号
		/// 注意事项: 在SDK回调线程执行, 不做耗时操作(仅入队/配对缓冲)
		/// </summary>
		private void OnCamera6Image(Bitmap bitmap, string cameraName, string cameraKey)
		{
			try
			{
				if (_isClosing || bitmap == null) return;
				long pid = Interlocked.Increment(ref _productIdCounter);
				Logger.Info($"[Camera6] 背面右 SN={cameraKey} 收到图像 {bitmap.Width}x{bitmap.Height}, ProductId={pid}");
				Interlocked.Increment(ref Hardware.CameraTriggerManager.ImageReceivedCount[6]);
				if (BackEnabled) _backStation?.OnCam4(bitmap, pid);
			}
			catch (Exception ex) { Logger.Error($"[Camera6] OnImage异常: {ex.Message}"); }
		}

		/// <summary>
		/// Camera7图像回调 — DaHuaSDK.OnImage事件触发
		/// 流程: ProductId自增→Interlocked计数→工位开关检查→分发到左侧面→SideStation.OnCam7
		/// 参数: bitmap=原始图像, cameraName=相机名称, cameraKey=序列号
		/// 注意事项: 在SDK回调线程执行, 不做耗时操作(仅入队/配对缓冲)
		/// </summary>
		private void OnCamera7Image(Bitmap bitmap, string cameraName, string cameraKey)
		{
			try
			{
				if (_isClosing) { Logger.Warning("[Camera7] 收图时程序正在关闭, 丢弃"); return; }
				if (bitmap == null) { Logger.Warning("[Camera7] 收到null图像"); return; }
				long pid = Interlocked.Increment(ref _productIdCounter);
				Logger.Info($"[Camera7] 左侧面 SN={cameraKey} 收到图像 {bitmap.Width}x{bitmap.Height}, ProductId={pid}");
				Interlocked.Increment(ref Hardware.CameraTriggerManager.ImageReceivedCount[7]);
				if (!SideEnabled) { Logger.Warning("[Camera7] 侧面工位已禁用, 丢弃图像"); return; }
				if (_sideStation == null) { Logger.Error("[Camera7] _sideStation为null, 丢弃图像"); return; }
				_sideStation.OnCam7(bitmap, pid);
			}
			catch (Exception ex) { Logger.Error($"[Camera7] OnImage异常: {ex.Message}"); }
		}

		/// <summary>
		/// Camera8图像回调 — DaHuaSDK.OnImage事件触发
		/// 流程: ProductId自增→Interlocked计数→工位开关检查→分发到右侧面→SideStation.OnCam8
		/// 参数: bitmap=原始图像, cameraName=相机名称, cameraKey=序列号
		/// 注意事项: 在SDK回调线程执行, 不做耗时操作(仅入队/配对缓冲)
		/// </summary>
		private void OnCamera8Image(Bitmap bitmap, string cameraName, string cameraKey)
		{
			try
			{
				if (_isClosing) { Logger.Warning("[Camera8] 收图时程序正在关闭, 丢弃"); return; }
				if (bitmap == null) { Logger.Warning("[Camera8] 收到null图像"); return; }
				long pid = Interlocked.Increment(ref _productIdCounter);
				Logger.Info($"[Camera8] 右侧面 SN={cameraKey} 收到图像 {bitmap.Width}x{bitmap.Height}, ProductId={pid}");
				Interlocked.Increment(ref Hardware.CameraTriggerManager.ImageReceivedCount[8]);
				if (!SideEnabled) { Logger.Warning("[Camera8] 侧面工位已禁用, 丢弃图像"); return; }
				if (_sideStation == null) { Logger.Error("[Camera8] _sideStation为null, 丢弃图像"); return; }
				_sideStation.OnCam8(bitmap, pid);
			}
			catch (Exception ex) { Logger.Error($"[Camera8] OnImage异常: {ex.Message}"); }
		}

		#endregion

		/// <summary>统一的相机连接状态更新（修复B1: case 6/7/8之前错误检查camera5State）</summary>
		/// <summary>
		/// 统一相机连接状态更新(BeginInvoke跨线程)
		/// 参数: cameraId=相机编号1~8, isConnected=是否已连接
		/// 注意事项: 已修复case6/7/8错误检查camera5State的bug; 每个case先判null防崩溃
		/// </summary>
		private void UpdateCameraState(int cameraId, bool isConnected)
		{
			this.BeginInvoke(new Action(() =>
			{
				var state = isConnected ? UILightState.On : UILightState.Off;
				switch (cameraId)
				{
					case 1: if (camera1State != null) camera1State.State = state; break;
					case 2: if (camera2State != null) camera2State.State = state; break;
					case 3: if (camera3State != null) camera3State.State = state; break;
					case 4: if (camera4State != null) camera4State.State = state; break;
					case 5: if (camera5State != null) camera5State.State = state; break;
					case 6: if (camera6State != null) camera6State.State = state; break;
					case 7: if (camera7State != null) camera7State.State = state; break;
					case 8: if (camera8State != null) camera8State.State = state; break;
				}
				Logger.Debug($"[Camera{cameraId}] 状态更新: {(isConnected ? "已连接" : "已断开")}");
			}));
		}

		/// <summary>根据相机序列号(Key)查找相机ID</summary>
		/// <summary>
		/// 通过相机序列号查找相机ID
		/// 参数: cameraKey=序列号(ICamera回调传回SN不是ID)
		/// 返回值: 相机ID(1~8), 未匹配返回0
		/// </summary>
		private int GetCameraIdByKey(string cameraKey)
		{
			if (string.IsNullOrEmpty(cameraKey)) return 0;
			var camCfg = _detectionParams?.Camera;
			if (camCfg == null) return 0;
			if (camCfg.Camera1SN == cameraKey) return 1;
			if (camCfg.Camera2SN == cameraKey) return 2;
			if (camCfg.Camera3SN == cameraKey) return 3;
			if (camCfg.Camera4SN == cameraKey) return 4;
			if (camCfg.Camera5SN == cameraKey) return 5;
			if (camCfg.Camera6SN == cameraKey) return 6;
			if (camCfg.Camera7SN == cameraKey) return 7;
			if (camCfg.Camera8SN == cameraKey) return 8;
			return 0;
		}

		private void OnCameraConnectionChanged(int cameraId, bool isConnected)
		{
			UpdateCameraState(cameraId, isConnected);
		}

		#endregion

		#region 工位结果回调

		private void OnStationResult(Bitmap mergedImage, bool[] ngArray, long okCount, long ngCount)
		{
			if (mergedImage == null) return;
			if (this.InvokeRequired)
			{
				this.BeginInvoke(new Action(() => OnStationResult(mergedImage, ngArray, okCount, ngCount)));
				return;
			}
			// 正面工位合并结果显示在 xlPictureBox1
			UpdatePictureBox(xlPictureBox1, mergedImage);
			if (OK_zheng_Lb != null) OK_zheng_Lb.Text = okCount.ToString();
			if (NG_zheng_Lb != null) NG_zheng_Lb.Text = ngCount.ToString();
			long ft2 = okCount + ngCount;
			if (Yield_zheng_Lb != null) Yield_zheng_Lb.Text = ft2 > 0 ? (okCount * 100.0 / ft2).ToString("F1") + "%" : "0%";
		}
		/// <summary>
		/// 工位检测结果→UI更新(BeginInvoke跨线程)
		/// Back→xlPic2 | EndFaceUpper→xlPic3 | EndFaceLower→xlPic4 | SideLeft→xlPic5 | SideRight→xlPic6
		/// 同时调用UpdateStatistics刷新OK/NG/良率
		/// 参数: result = 各工位检测结果(渲染图Bitmap+缺陷列表+OK状态)
		/// </summary>
		private void OnStationResult(ProductResult result)
		{
			this.BeginInvoke(new Action(() =>
			{
				// 显示渲染图像到对应控件
				if (result.BackResult.HasValue && _plcResultService != null && _backStation?.StatusList != null)
				{
					int p = _currentSku?.P ?? 8;
					var statusList = _backStation.StatusList;
					if (_pendingTestStation == Hardware.StationType.Back)
					{
						_pendingTestStation = null;
						new PlcTestSendForm(Hardware.StationType.Back, statusList, _plcResultService, p).ShowDialog();
					}
					else
					{
						var plcSw = System.Diagnostics.Stopwatch.StartNew();
						Config.StationDefectConfig.Instance.Resolve("Back", statusList, out ushort rejectBits, out int stopLevel, out string stopReason);
						Logger.Info($"[PLC-Back] pid={result.ProductId} P={p} 逐盒:[{string.Join("][", statusList)}] → 剔除位=0x{rejectBits:X4} 停机={stopLevel}{(stopReason.Length > 0 ? " 原因:" + stopReason : "")}");
						if (!_plcResultService.SendStationResult(Hardware.StationType.Back, rejectBits, stopLevel, p))
							Logger.Error($"[PLC-Back] pid={result.ProductId} SendStationResult 返回 false!");
						if (!_plcResultService.SendStationComplete(Hardware.StationType.Back))
							Logger.Error($"[PLC-Back] pid={result.ProductId} SendStationComplete 返回 false!");
						double plcMs = plcSw.Elapsed.TotalMilliseconds;
						Logger.Info($"[PLC-Back] ⏱ PLC发送耗时={plcMs:F2}ms");
						if (plcMs > 50) Logger.Warning($"[PLC-Back] ⚠ PLC发送偏慢: {plcMs:F0}ms (正常<10ms, 请检查S7-1500网络)");
						ModelPerfTracker.RecordDefects("Back", statusList);
						ModelPerfTracker.RecordPlcResult("Back", statusList.Count(s => s == "OK"), statusList.Count(s => s != "OK"));
					}
				}
				if (result.BackRenderImage != null)
					UpdatePictureBox(xlPictureBox2, result.BackRenderImage);
				if (result.EndFaceResult.HasValue && _plcResultService != null && _endFaceStation?.StatusList != null)
				{
					int p = _currentSku?.P ?? 8;
					var statusList = _endFaceStation.StatusList;
					if (_pendingTestStation == Hardware.StationType.EndFace)
					{
						_pendingTestStation = null;
						new PlcTestSendForm(Hardware.StationType.EndFace, statusList, _plcResultService, p).ShowDialog();
					}
					else
					{
						var plcSw = System.Diagnostics.Stopwatch.StartNew();
						Config.StationDefectConfig.Instance.Resolve("EndFace", statusList, out ushort rejectBits, out int stopLevel, out string stopReason);
						Logger.Info($"[PLC-EndFace] pid={result.ProductId} P={p} 逐盒:[{string.Join("][", statusList)}] → 剔除位=0x{rejectBits:X4} 停机={stopLevel}{(stopReason.Length > 0 ? " 原因:" + stopReason : "")}");
						if (!_plcResultService.SendStationResult(Hardware.StationType.EndFace, rejectBits, stopLevel, p))
							Logger.Error($"[PLC-EndFace] pid={result.ProductId} SendStationResult 返回 false!");
						if (!_plcResultService.SendStationComplete(Hardware.StationType.EndFace))
							Logger.Error($"[PLC-EndFace] pid={result.ProductId} SendStationComplete 返回 false!");
						double plcMs = plcSw.Elapsed.TotalMilliseconds;
						Logger.Info($"[PLC-EndFace] ⏱ PLC发送耗时={plcMs:F2}ms");
						if (plcMs > 50) Logger.Warning($"[PLC-EndFace] ⚠ PLC发送偏慢: {plcMs:F0}ms (正常<10ms, 请检查S7-1500网络)");
						ModelPerfTracker.RecordDefects("EndFace", statusList);
						ModelPerfTracker.RecordPlcResult("EndFace", statusList.Count(s => s == "OK"), statusList.Count(s => s != "OK"));
					}
				}
				if (result.EndFaceRenderImage != null)
					UpdatePictureBox(xlPictureBox3, result.EndFaceRenderImage);
				if (result.EndFaceLowerRenderImage != null)
					UpdatePictureBox(xlPictureBox4, result.EndFaceLowerRenderImage);
				// SideRenderImage/SideLeftRenderImage是同一张图, 避免重复更新xlPic5
				if (result.SideResult.HasValue && _plcResultService != null && _sideStation?.StatusList != null)
				{
					int p = _currentSku?.P ?? 8;
					var statusList = _sideStation.StatusList;
					if (_pendingTestStation == Hardware.StationType.Side)
					{
						_pendingTestStation = null;
						new PlcTestSendForm(Hardware.StationType.Side, statusList, _plcResultService, p).ShowDialog();
					}
					else
					{
						var plcSw = System.Diagnostics.Stopwatch.StartNew();
						Config.StationDefectConfig.Instance.Resolve("Side", statusList, out ushort rejectBits, out int stopLevel, out string stopReason);
						Logger.Info($"[PLC-Side] pid={result.ProductId} P={p} 逐盒:[{string.Join("][", statusList)}] → 剔除位=0x{rejectBits:X4} 停机={stopLevel}{(stopReason.Length > 0 ? " 原因:" + stopReason : "")}");
						if (!_plcResultService.SendStationResult(Hardware.StationType.Side, rejectBits, stopLevel, p))
							Logger.Error($"[PLC-Side] pid={result.ProductId} SendStationResult 返回 false!");
						if (!_plcResultService.SendStationComplete(Hardware.StationType.Side))
							Logger.Error($"[PLC-Side] pid={result.ProductId} SendStationComplete 返回 false!");
						double plcMs = plcSw.Elapsed.TotalMilliseconds;
						Logger.Info($"[PLC-Side] ⏱ PLC发送耗时={plcMs:F2}ms");
						if (plcMs > 50) Logger.Warning($"[PLC-Side] ⚠ PLC发送偏慢: {plcMs:F0}ms (正常<10ms, 请检查S7-1500网络)");
						ModelPerfTracker.RecordDefects("Side", statusList);
						ModelPerfTracker.RecordPlcResult("Side", statusList.Count(s => s == "OK"), statusList.Count(s => s != "OK"));
					}
				}
				if (result.SideLeftRenderImage != null)
					UpdatePictureBox(xlPictureBox5, result.SideLeftRenderImage);
				if (result.SideRightRenderImage != null)
					UpdatePictureBox(xlPictureBox6, result.SideRightRenderImage);

				UpdateStatistics(result);  // 每次工位结果到达即更新计数
			}));
		}

		/// <summary>
		/// 刷新4工位统计标签
		/// 流程: 从各processor读取累计OK/NG→更新UI→良率=OK×100÷(OK+NG)(1位小数)
		/// 注意事项: BeginInvoke线程安全, 4工位独立统计
		/// </summary>
		private void UpdateStatistics(ProductResult result)
		{
			// 更新正面统计 (调用者已在UI线程/BeginInvoke中, 无需再次BeginInvoke)
			if (_frontStation != null)
			{
				if (OK_zheng_Lb != null) OK_zheng_Lb.Text = _frontStation.OkCount.ToString();
				if (NG_zheng_Lb != null) NG_zheng_Lb.Text = _frontStation.NgCount.ToString();
				long ft = _frontStation.OkCount + _frontStation.NgCount;
				if (Yield_zheng_Lb != null) Yield_zheng_Lb.Text = (ft > 0 ? (_frontStation.OkCount * 100.0 / ft).ToString("F1") + "%" : "0%");
			}
			if (_backStation != null)
			{
				if (OK_fan_Lb != null) OK_fan_Lb.Text = _backStation.OkCount.ToString();
				if (NG_fan_Lb != null) NG_fan_Lb.Text = _backStation.NgCount.ToString();
				long bt = _backStation.OkCount + _backStation.NgCount;
				if (Yield_fan_Lb != null) Yield_fan_Lb.Text = (bt > 0 ? (_backStation.OkCount * 100.0 / bt).ToString("F1") + "%" : "0%");
			}
			if (_endFaceStation != null)
			{
				if (OK_duanmian_Lb != null) OK_duanmian_Lb.Text = _endFaceStation.OkCount.ToString();
				if (NG_duanmian_Lb != null) NG_duanmian_Lb.Text = _endFaceStation.NgCount.ToString();
				long et = _endFaceStation.OkCount + _endFaceStation.NgCount;
				if (Yield_duanmian_Lb != null) Yield_duanmian_Lb.Text = (et > 0 ? (_endFaceStation.OkCount * 100.0 / et).ToString("F1") + "%" : "0%");
			}
			if (_sideStation != null)
			{
				if (OK_cemian_Lb != null) OK_cemian_Lb.Text = _sideStation.OkCount.ToString();
				if (NG_cemian_Lb != null) NG_cemian_Lb.Text = _sideStation.NgCount.ToString();
				long st2 = _sideStation.OkCount + _sideStation.NgCount;
				if (Yield_cemian_Lb != null) Yield_cemian_Lb.Text = (st2 > 0 ? (_sideStation.OkCount * 100.0 / st2).ToString("F1") + "%" : "0%");
			}
		}

		private long _lastEndFaceCarouselRefreshTicks = 0;  // 端面轮播刷新节流(同侧面300ms)

		/// <summary>端面状态更新回调: 300ms节流刷新轮播图, 避免频繁UI更新导致卡顿</summary>
		private void OnEndFaceStatusUpdate(List<string> upperStatus, List<string> lowerStatus, List<string> mergedStatus, int p)
		{
			// 节流: 同侧面300ms
			long now = DateTime.Now.Ticks;
			if (now - Interlocked.Read(ref _lastEndFaceCarouselRefreshTicks) < 300 * 10000) return;
			Interlocked.Exchange(ref _lastEndFaceCarouselRefreshTicks, now);

			this.BeginInvoke(new Action(() =>
			{
				if (_endFaceIndexLabel != null && _endFaceStation != null)
				{
					_endFaceIndexLabel.Text = $"{_endFaceStation.CurrentIndex + 1}/{p}";
				}
				RefreshCarouselDisplays();
			}));
		}
		/// <summary>侧面状态更新回调: 更新轮播索引标签+RefreshCarouselDisplays刷新显示</summary>
		private void OnSideStatusUpdate(List<string> leftStatus, List<string> rightStatus, List<string> mergedStatus, int p)
		{
			// 节流: 300ms内只刷新一次, 避免UI消息队列过载导致界面卡顿
			long now = DateTime.Now.Ticks;
			if (now - Interlocked.Read(ref _lastCarouselRefreshTicks) < 300 * 10000) return;
			Interlocked.Exchange(ref _lastCarouselRefreshTicks, now);

			this.BeginInvoke(new Action(() =>
			{
				if (_sideIndexLabel != null && _sideStation != null)
				{
					_sideIndexLabel.Text = $"{_sideStation.CurrentIndex + 1}/{p}";
				}
				RefreshCarouselDisplays();
			}));
		}

		#endregion

		#region 窗体按钮

		private void mainTitleBar1_OnMenuButtonClick(object sender, EventArgs e)
		{
			DrawPoint point = new DrawPoint(3, 5);
			TabFrm tabFrm = new TabFrm(point, this);
			tabFrm.Show();
		}

		private void mainTitleBar1_OnMinButtonClick(object sender, EventArgs e)
		{
			this.WindowState = FormWindowState.Minimized;
		}

		private void mainTitleBar1_OnCloseButtonClick(object sender, EventArgs e)
		{
			if (MessageBox.Show("确定要退出程序吗？", "退出确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
			{
				this.Close();
				System.Windows.Forms.Application.Exit();
			}
		}

		#endregion

		#region UI初始化

		/// <summary>
		/// 初始化用户界面
		/// 流程: SKU搜索控件(ComboBox+300ms防抖)→加载参数→绑定按钮→恢复计数→SKU显示→测试按钮
		/// 按钮: 检测参数→DetectionParametersForm(11Tab)→OnParametersChanged热更新(无需重启)
		/// </summary>
		private void InitUI()
		{
			SetupSkuSearch();
			LoadSkuParams();
			if (OpenNGimageBtn != null)
				OpenNGimageBtn.Click += (s2, e2) =>
				{
					string d = Path.Combine(_detectionParams.Save.ImageSavePath,
						DateTime.Now.ToString("yyMMdd"));
					if (!Directory.Exists(d))
						d = _detectionParams.Save.ImageSavePath;
					if (Directory.Exists(d))
						Process.Start("explorer.exe", d);
				};
			BindButtonEvents();
			LoadCounts();
			UpdateSkuDisplay();
			InitTestButtons();
		}

		/// <summary>创建4个工位测试按钮: 正面/背面/端面/侧面, 添加到tableLayoutPanel34, 绑定Click事件</summary>
		private void InitTestButtons()
		{
			if (tableLayoutPanel34 == null) return;
			tableLayoutPanel34.SuspendLayout();
			tableLayoutPanel34.Controls.Clear();
			tableLayoutPanel34.RowCount = 4;
			for (int i = 0; i < 4; i++)
			{
				string text; EventHandler handler;
				switch (i)
				{
					case 0: text = "正面测试"; handler = TestFrontBtn_Click; break;
					case 1: text = "背面测试"; handler = TestBackBtn_Click; break;
					case 2: text = "端面测试"; handler = TestEndFaceBtn_Click; break;
					default: text = "侧面测试"; handler = TestSideBtn_Click; break;
				}
				var btn = new Button
				{
					Text = text,
					Dock = DockStyle.Fill,
					FlatStyle = FlatStyle.Flat,
					BackColor = Color.FromArgb(52, 152, 219),
					ForeColor = Color.White,
					Font = new Font("微软雅黑", 10F, FontStyle.Bold),
					Margin = new Padding(2)
				};
				btn.Click += handler;
				tableLayoutPanel34.Controls.Add(btn, 0, i);
			}
			tableLayoutPanel34.ResumeLayout();
			Logger.Info("工位测试按钮已初始化");
		}

		/// <summary>选择图片文件: OpenFileDialog→过滤jpg/png/bmp/tiff→返回Bitmap, 测试用</summary>
		private Bitmap PickImage(string title)
		{
			using (var dlg = new OpenFileDialog { Title = title, Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.tiff" })
			{
				if (dlg.ShowDialog() == DialogResult.OK)
				{
					try { return new Bitmap(dlg.FileName); }
					catch (Exception ex)
					{
						Logger.Error("加载图片失败: " + ex.Message);
						MessageBox.Show("加载图片失败: " + ex.Message);
					}
				}
			}
			return null;
		}

		/// <summary>
		/// 正面离线测试(不依赖相机/PLC)
		/// 流程: 选左右图→SkipCrop=true→送入FrontStation(P号OCR+盒子破损)
		/// 注意: 测试完恢复SkipCrop=false
		/// </summary>
		private void TestFrontBtn_Click(object sender, EventArgs e)
		{
			if (_frontStation == null)
			{
				MessageBox.Show("正面工位未初始化");
				return;
			}
			var left = PickImage("选择正面左图 (相机1)");
			if (left == null) return;
			var right = PickImage("选择正面右图 (相机2)");
			if (right == null)
			{
				left.Dispose();
				return;
			}
			Logger.Info("[Test] 正面测试开始 " + left.Width + "x" + left.Height + " / " + right.Width + "x" + right.Height);
			_pendingTestStation = Hardware.StationType.Front;  // 测试模式: 推理完成后弹窗发送PLC
			_frontStation.SkipCrop = true;
			_frontStation.OnCam1(left, DateTime.Now.Ticks);
			_frontStation.OnCam2(right, DateTime.Now.Ticks);
			_frontStation.SkipCrop = false;
		}

		/// <summary>背面离线测试: 选左右图→SkipCrop→送入BackStation(条码+日期码+挂钩)</summary>
		private void TestBackBtn_Click(object sender, EventArgs e)
		{
			if (_backStation == null)
			{
				MessageBox.Show("背面工位未初始化");
				return;
			}
			var left = PickImage("选择背面左图 (相机5)");
			if (left == null) return;
			var right = PickImage("选择背面右图 (相机6)");
			if (right == null) { left.Dispose(); return; }
			Logger.Info("[Test] 背面测试开始 " + left.Width + "x" + left.Height + " / " + right.Width + "x" + right.Height);
			_pendingTestStation = Hardware.StationType.Back;  // 测试模式: 推理完成后弹窗发送PLC
			_backStation.SkipCrop = true;
			_backStation.OnCam3(left, DateTime.Now.Ticks);
			_backStation.OnCam4(right, DateTime.Now.Ticks);
			_backStation.SkipCrop = false;
		}

		/// <summary>端面离线测试: 选上下图→TestProcessPair(跳过运动轴,直接批处理)</summary>
		private void TestEndFaceBtn_Click(object sender, EventArgs e)
		{
			if (_endFaceStation == null)
			{
				MessageBox.Show("端面工位未初始化");
				return;
			}
			var upper = PickImage("选择上端面图片 (相机3)");
			if (upper == null) return;
			var lower = PickImage("选择下端面图片 (相机4)");
			if (lower == null)
			{
				upper.Dispose();
				return;
			}
			Logger.Info("[Test] 端面测试开始 " + upper.Width + "x" + upper.Height + " / " + lower.Width + "x" + lower.Height);
			_pendingTestStation = Hardware.StationType.EndFace;  // 测试模式: 推理完成后弹窗发送PLC
			_endFaceStation.TestProcessPair(upper, lower);
		}

		/// <summary>侧面离线测试: 选左右图→TestProcessPair(跳过运动轴,直接推理+汇总)</summary>
		private void TestSideBtn_Click(object sender, EventArgs e)
		{
			if (_sideStation == null)
			{
				MessageBox.Show("侧面工位未初始化");
				return;
			}
			var left = PickImage("选择左侧面图片 (相机7)");
			if (left == null) return;
			var right = PickImage("选择右侧面图片 (相机8)");
			if (right == null) { left.Dispose(); return; }
			Logger.Info("[Test] 侧面测试开始 " + left.Width + "x" + left.Height + " / " + right.Width + "x" + right.Height);
			_pendingTestStation = Hardware.StationType.Side;  // 测试模式: 推理完成后弹窗发送PLC
			_sideStation.TestProcessPair(left, right);
		}



		/// <summary>持久化班次计数→Config/counts.json(班次+日期+4工位OK/NG), 关闭时调用</summary>
		private void SaveCounts()
		{
			try
			{
				var data = new Dictionary<string, string>()
				{
					{ "shift", GetCurrentShift() },
					{ "date", DateTime.Now.ToString("yyyyMMdd") },
					{ "frontOk", _frontStation?.OkCount.ToString() ?? "0" },
					{ "frontNg", _frontStation?.NgCount.ToString() ?? "0" },
					{ "backOk", _backStation?.OkCount.ToString() ?? "0" },
					{ "backNg", _backStation?.NgCount.ToString() ?? "0" },
					{ "endOk", _endFaceStation?.OkCount.ToString() ?? "0" },
					{ "endNg", _endFaceStation?.NgCount.ToString() ?? "0" },
					{ "sideOk", _sideStation?.OkCount.ToString() ?? "0" },
					{ "sideNg", _sideStation?.NgCount.ToString() ?? "0" }
				};
				File.WriteAllText(
					Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "counts.json"),
					Newtonsoft.Json.JsonConvert.SerializeObject(data));
			}
			catch { }
		}
		/// <summary>恢复班次计数←Config/counts.json, 班次/日期不匹配则从0开始, BeginInvoke恢复UI</summary>
		private void LoadCounts()
		{
			try
			{
				var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "counts.json");
				if (!File.Exists(path)) return;
				var data = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(File.ReadAllText(path));
				if (data == null) return;
				this.BeginInvoke(new Action(() =>
				{
					string savedShift = data.ContainsKey("shift") ? data["shift"] : "";
					bool shiftMismatch = !string.IsNullOrEmpty(savedShift)
						&& savedShift != GetCurrentShift();
					bool dateMismatch = data.ContainsKey("date")
						&& data["date"] != DateTime.Now.ToString("yyyyMMdd");
					if (shiftMismatch || dateMismatch)
					{
						Logger.Info("计数班次不匹配(" + savedShift + "!=" + GetCurrentShift() + "),从0开始");
						return;
					}
					if (data.ContainsKey("frontOk"))
					{
						if (OK_zheng_Lb != null) OK_zheng_Lb.Text = data["frontOk"];
						if (NG_zheng_Lb != null) NG_zheng_Lb.Text = data["frontNg"];
						if (OK_fan_Lb != null) OK_fan_Lb.Text = data["backOk"];
						if (NG_fan_Lb != null) NG_fan_Lb.Text = data["backNg"];
						if (OK_duanmian_Lb != null) OK_duanmian_Lb.Text = data["endOk"];
						if (NG_duanmian_Lb != null) NG_duanmian_Lb.Text = data["endNg"];
						if (OK_cemian_Lb != null) OK_cemian_Lb.Text = data["sideOk"];
						if (NG_cemian_Lb != null) NG_cemian_Lb.Text = data["sideNg"];
					}
					long fOk = long.Parse(data.ContainsKey("frontOk") ? data["frontOk"] : "0");
					long fNg = long.Parse(data.ContainsKey("frontNg") ? data["frontNg"] : "0");
					long bOk = long.Parse(data.ContainsKey("backOk") ? data["backOk"] : "0");
					long bNg = long.Parse(data.ContainsKey("backNg") ? data["backNg"] : "0");
					long eOk = long.Parse(data.ContainsKey("endOk") ? data["endOk"] : "0");
					long eNg = long.Parse(data.ContainsKey("endNg") ? data["endNg"] : "0");
					long sOk = long.Parse(data.ContainsKey("sideOk") ? data["sideOk"] : "0");
					long sNg = long.Parse(data.ContainsKey("sideNg") ? data["sideNg"] : "0");
					_frontStation?.RestoreCounts(fOk, fNg);
					_backStation?.RestoreCounts(bOk, bNg);
					_endFaceStation?.RestoreCounts(eOk, eNg); _sideStation?.RestoreCounts(sOk, sNg);
					Logger.Info("计数已从本地恢复");
				}));
			}
			catch { }
		}

		/// <summary>持久化SKU手动参数→Config/sku_params.json(P/Z/MM/条码/日期码格式)</summary>
		private void SaveSkuParams()
		{
			try
			{
				var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "sku_params.json");
				var data = new Dictionary<string, string>();
				foreach (var kv in new (string, Control)[] {
					("SKU", _skuSearchCombo), ("P", P_Lb), ("Z", Z_Lb), ("MM", MM_Lb),
					("FrontPNumber", FrontPNumber_Lb), ("BackBarcode", BackBarcode_Lb), ("CodingFormat", CodingFormat_Lb)
				})
				{
					if (kv.Item2 == null) continue;
					string v = kv.Item2 is ComboBox cb ? cb.Text : kv.Item2.Text;
					if (!string.IsNullOrWhiteSpace(v)) data[kv.Item1] = v;
				}
				File.WriteAllText(path, Newtonsoft.Json.JsonConvert.SerializeObject(data));
			}
			catch (Exception ex) { Logger.Error("保存SKU参数失败: " + ex.Message); }
		}

		/// <summary>恢复SKU参数←Config/sku_params.json→UI控件+_currentSku→推4工位+端面更新P</summary>
		private void LoadSkuParams()
		{
			try
			{
				var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "sku_params.json");
				if (!File.Exists(path)) return;
				var json = File.ReadAllText(path);
				var data = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
				if (data == null) return;
				this.BeginInvoke(new Action(() =>
				{
					foreach (var kv in data)
					{
						Control ctrl = null;
						switch (kv.Key)
						{
							case "SKU": ctrl = _skuSearchCombo; break;
							case "P": ctrl = P_Lb; break;
							case "Z": ctrl = Z_Lb; break;
							case "MM": ctrl = MM_Lb; break;
							case "FrontPNumber": ctrl = FrontPNumber_Lb; break;
							case "BackBarcode": ctrl = BackBarcode_Lb; break;
							case "CodingFormat": ctrl = CodingFormat_Lb; break;
						}
						if (ctrl != null)
						{
							if (ctrl is ComboBox cb)
							{
								int idx = cb.FindStringExact(kv.Value);
								if (idx >= 0)
									cb.SelectedIndex = idx;
								else
									cb.Text = kv.Value;
							}
							else ctrl.Text = kv.Value;
						}
					}
					if (_currentSku != null)
					{
						// 校验SKU编号：只有匹配时才恢复用户编辑的值，防止A款SKU的编辑覆盖到B款
						bool skuMatched = data.TryGetValue("SKU", out string savedSkuNum)
							&& savedSkuNum == _currentSku.SkuNumber;
						if (skuMatched)
						{
							if (data.TryGetValue("P", out string rp2) && int.TryParse(rp2, out int rpi) && rpi > 0) _currentSku.P = rpi;
							if (data.TryGetValue("Z", out string rz2) && int.TryParse(rz2, out int rzi)) _currentSku.Z = rzi;
							if (data.TryGetValue("MM", out string rm2) && int.TryParse(rm2, out int rmi)) _currentSku.MM = rmi;
							if (data.TryGetValue("FrontPNumber", out string fp)) _currentSku.FrontPCode = fp;
							if (data.TryGetValue("BackBarcode", out string bc)) _currentSku.BackBarcode = bc;
							if (data.TryGetValue("CodingFormat", out string cf)) _currentSku.CodingFormat = cf;
							Logger.Debug($"SKU参数已恢复(SKU匹配): P={_currentSku.P} Z={_currentSku.Z} MM={_currentSku.MM} P号={_currentSku.FrontPCode} 条码={_currentSku.BackBarcode} 格式={_currentSku.CodingFormat}");
						}
						else
						{
							Logger.Debug($"SKU参数跳过恢复(SKU不匹配: 保存={savedSkuNum} 当前={_currentSku.SkuNumber})");
						}
					}
					// 恢复的P/MM可能和CSV不同, 重新匹配裁图比例
					_skuDb.ApplyCropData(_currentSku);
					_frontStation?.UpdateSku(_currentSku); _backStation?.UpdateSku(_currentSku);
					_sideStation?.UpdateSku(_currentSku); _endFaceStation?.UpdateSku(_currentSku);
					_endFaceStation?.UpdatePCount(_currentSku?.P ?? 12);
					Logger.Info("SKU参数已从本地恢复并推送到各工位");
				}));
			}
			catch (Exception ex) { Logger.Error("加载SKU参数失败: " + ex.Message); }
		}

		/// <summary>保存所有模型参数到best.json: 各工位YOLO/ViMo模型的阈值同步回meta文件</summary>
		private void SaveAllModelParams()
		{
			try
			{
				_aiModels.FrontBoxBreakModel?.SaveParams(_aiModels.FrontBoxBreakModel.MetaPath);
				_aiModels.EndFaceUpperModel?.SaveParams(_aiModels.EndFaceUpperModel.MetaPath);
				_aiModels.EndFaceLowerModel?.SaveParams(_aiModels.EndFaceLowerModel.MetaPath);
				_aiModels.BackHookModel?.SaveParams(_aiModels.BackHookModel.MetaPath);
				_aiModels.SideDefectModel?.SaveParams(_aiModels.SideDefectModel.MetaPath);
				Logger.Info("模型参数已同步保存到best.json");
			}
			catch (Exception ex) { Logger.Error("保存模型参数失败: " + ex.Message); }
		}

		/// <summary>
		/// 创建SKU搜索控件(ComboBox替换原TextBox)
		/// 流程: TextChanged→300ms防抖Timer→SkuDatabase.Search→BeginUpdate批量更新下拉→EndUpdate
		///       选中后→更新_currentSku→推4工位→保存LastSkuNumber到DetectionParams.json
		/// 注意事项: BeginUpdate/EndUpdate避免每次Add刷新UI; 回车键也可直接输入SKU
		/// </summary>
		private void SetupSkuSearch()
		{
			if (SKU_Txt == null) return;
			Logger.Info("初始化SKU搜索控件");

			SKU_Txt.Visible = false;

			// 创建搜索框（Parent未就绪时挂到窗体）
			var comboParent = SKU_Txt.Parent ?? (Control)this;
			_skuSearchCombo = new ComboBox
			{
				Font = new Font("微软雅黑", 10F),
				DropDownStyle = ComboBoxStyle.DropDown,
				Width = SKU_Txt.Width,
				Height = SKU_Txt.Height,
				Location = SKU_Txt.Location,
				Text = ""
			};

			comboParent.Controls.Add(_skuSearchCombo);
			_skuSearchCombo.BringToFront();

			// 使用 System.Windows.Forms.Timer 防抖
			System.Windows.Forms.Timer debounceTimer = new System.Windows.Forms.Timer();
			debounceTimer.Interval = 300;

			_skuSearchCombo.TextChanged += (s, e) =>
			{
				// 重新启动计时器
				debounceTimer.Stop();
				debounceTimer.Start();
			};

			debounceTimer.Tick += (timerSender, timerE) =>
			{
				debounceTimer.Stop();

				string keyword = _skuSearchCombo.Text;
				if (string.IsNullOrWhiteSpace(keyword))
				{
					_skuSearchCombo.Items.Clear();
					return;
				}

				// 防卡顿：BeginUpdate/EndUpdate批量更新，避免每Add一次就刷新UI
				var results = _skuDb.Search(keyword);
				_skuSearchCombo.BeginUpdate();
				_skuSearchCombo.Items.Clear();
				foreach (var sku in results)
					_skuSearchCombo.Items.Add(sku.SkuNumber);
				_skuSearchCombo.EndUpdate();

				// 选中文本保持输入内容，避免光标跳到最前面
				if (_skuSearchCombo.Items.Count > 0)
				{
					_skuSearchCombo.DroppedDown = true;
					_skuSearchCombo.Select(keyword.Length, 0);
				}
			};

			// 用户选择时触发
			_skuSearchCombo.SelectedIndexChanged += (s, e) =>
			{
				if (_skuSearchCombo.SelectedItem != null)
				{
					string skuNum = _skuSearchCombo.SelectedItem.ToString();
					Logger.Info($"选择SKU: {skuNum}");

					_currentSku = _skuDb.GetBySkuNumber(skuNum);
					if (_currentSku != null)
					{
						UpdateSkuDisplay();
						_frontStation?.UpdateSku(_currentSku);
						_backStation?.UpdateSku(_currentSku);
						// 保存到配置
						_detectionParams.LastSkuNumber = skuNum;
						_detectionParams.SaveToFile();
						SaveSkuParams();
						// 根据当前P值重新匹配裁图比例
						_skuDb.ApplyCropData(_currentSku);
						_sideStation?.UpdateSku(_currentSku);
						_endFaceStation?.UpdateSku(_currentSku);
						_endFaceStation?.UpdatePCount(_currentSku.P);
						Logger.Info($"SKU已切换: {skuNum}, P={_currentSku.P}, Z={_currentSku.Z}, MM={_currentSku.MM}");
					}
					else
					{
						Logger.Warning($"未找到SKU: {skuNum}");
					}

					_skuSearchCombo.DroppedDown = false;
				}
			};

			// 回车键确认
			_skuSearchCombo.KeyDown += (s, e) =>
			{
				if (e.KeyCode == Keys.Enter)
				{
					string skuNum = _skuSearchCombo.Text;
					if (!string.IsNullOrWhiteSpace(skuNum))
					{
						_currentSku = _skuDb.GetBySkuNumber(skuNum);
						if (_currentSku != null)
						{
							UpdateSkuDisplay();
							_frontStation?.UpdateSku(_currentSku);
							_backStation?.UpdateSku(_currentSku);
							// 根据当前P值重新匹配裁图比例
							_skuDb.ApplyCropData(_currentSku);
							_sideStation?.UpdateSku(_currentSku);
							_detectionParams.LastSkuNumber = skuNum;
							_detectionParams.SaveToFile();
							_endFaceStation?.UpdateSku(_currentSku);
							_endFaceStation?.UpdatePCount(_currentSku.P);
							Logger.Info($"SKU已切换(回车): {skuNum}, P={_currentSku.P}");
						}
						_skuSearchCombo.DroppedDown = false;
					}
				}
			};
		}
		/// <summary>应用SKU切换: 更新显示标签→推送到4个工位处理器→端面更新P值</summary>
		/// <summary>从数据库拉取最新SKU数据</summary>
		public void RefreshSkuFromDatabase()
		{
			try
			{
				_skuDb.CurrentDataSource = SkuDatabase.DataSourceType.SqlServer;
				_skuDb.SqlConnectionString = Class_Config._Config.DatabaseConnectionString ?? "";
				if (string.IsNullOrEmpty(_skuDb.SqlConnectionString))
				{
					MessageBox.Show("数据库连接字符串未配置，请先在 setup.ini [database] 中设置 ConnectionString",
						"提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
					return;
				}
				bool ok = _skuDb.Refresh();
				if (ok)
					MessageBox.Show("SKU数据已从数据库刷新成功!", "刷新成功",
						MessageBoxButtons.OK, MessageBoxIcon.Information);
				else
					MessageBox.Show("从数据库刷新失败，请检查连接配置和网络。" + Environment.NewLine +
						"已自动降级为本地CSV加载。", "刷新失败",
						MessageBoxButtons.OK, MessageBoxIcon.Warning);
			}
			catch (Exception ex)
			{
				Logger.Error("数据库刷新异常: " + ex.Message);
				MessageBox.Show("刷新异常: " + ex.Message, "错误",
					MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private void ApplySkuChange()
		{
			if (_currentSku == null) return;

			Logger.Info($"应用SKU: {_currentSku.SkuNumber}");

			// 更新显示区域
			UpdateSkuDisplay();

			// 更新工位处理器
			_frontStation?.UpdateSku(_currentSku);
			_backStation?.UpdateSku(_currentSku);
			_sideStation?.UpdateSku(_currentSku);
			_endFaceStation?.UpdatePCount(_currentSku.P);
		}

		private void UpdateSkuDisplay()
		{
			if (_currentSku == null)
			{
				Logger.Warning("_currentSku is null");
				return;
			}

			if (this.InvokeRequired)
			{
				this.Invoke(new Action(UpdateSkuDisplay));
				return;
			}

			try
			{
				Logger.Debug($"更新SKU显示: {_currentSku.SkuNumber}");
				Logger.Debug($"  P={_currentSku.P}, Z={_currentSku.Z}, MM={_currentSku.MM}");
				Logger.Debug($"  P号码(背卡P号)={_currentSku.FrontPCode}");
				Logger.Debug($"  条形码={_currentSku.BackBarcode}");
				Logger.Debug($"  打码格式={_currentSku.CodingFormat}");

				if (_skuSearchCombo != null) _skuSearchCombo.Text = _currentSku.SkuNumber ?? "";
				if (P_Lb != null) P_Lb.Text = _currentSku.P.ToString();
				if (Z_Lb != null) Z_Lb.Text = _currentSku.Z.ToString();
				if (MM_Lb != null) MM_Lb.Text = _currentSku.MM.ToString();

				// 正面P号码标准 - 使用 FrontPNumber_Lb
				if (FrontPNumber_Lb != null)
				{
					FrontPNumber_Lb.Text = string.IsNullOrEmpty(_currentSku.FrontPCode) ? "-" : _currentSku.FrontPCode;
				}

				// 背面条形码标准 - 使用 BackBarcode_Lb
				if (BackBarcode_Lb != null)
				{
					BackBarcode_Lb.Text = string.IsNullOrEmpty(_currentSku.BackBarcode) ? "-" : _currentSku.BackBarcode;
				}

				// 打码格式
				if (CodingFormat_Lb != null)
				{
					CodingFormat_Lb.Text = string.IsNullOrEmpty(_currentSku.CodingFormat) ? "-" : _currentSku.CodingFormat;
				}
			}
			catch (Exception ex)
			{
				Logger.Error($"更新SKU显示失败: {ex.Message}");
			}
		}
		/// <summary>初始化轮播图索引标签: 端面(xlPic3下方)+侧面(xlPic5下方), 显示"当前/总数"</summary>
		private void InitCarouselLabels()
		{
			// 只添加索引标签，不添加按钮
			if (xlPictureBox3 != null && _endFaceIndexLabel == null)
			{
				_endFaceIndexLabel = new Label
				{
					Text = "1/8",
					Width = 80,
					Height = 30,
					TextAlign = ContentAlignment.MiddleCenter,
					Font = new Font("微软雅黑", 10F, FontStyle.Bold),
					ForeColor = Color.White,
					BackColor = Color.FromArgb(47, 60, 76),
					Location = new DrawPoint(xlPictureBox3.Left + xlPictureBox3.Width / 2 - 40, xlPictureBox3.Bottom + 5)
				};
				this.Controls.Add(_endFaceIndexLabel);
				_endFaceIndexLabel.BringToFront();
			}

			if (xlPictureBox5 != null && _sideIndexLabel == null)
			{
				_sideIndexLabel = new Label
				{
					Text = "1/8",
					Width = 80,
					Height = 30,
					TextAlign = ContentAlignment.MiddleCenter,
					Font = new Font("微软雅黑", 10F, FontStyle.Bold),
					ForeColor = Color.White,
					BackColor = Color.FromArgb(47, 60, 76),
					Location = new DrawPoint(xlPictureBox5.Left + xlPictureBox5.Width / 2 - 40, xlPictureBox5.Bottom + 5)
				};
				this.Controls.Add(_sideIndexLabel);
				_sideIndexLabel.BringToFront();
			}
		}

		/// <summary>刷新工位显示: 端面(上→xlPic3/下→xlPic4)+侧面(左→xlPic5/右→xlPic6), 左右独立不再轮播</summary>
		private void RefreshCarouselDisplays()
		{
			try
			{
				// 端面轮播图 — 上端面→xlPictureBox3, 下端面→xlPictureBox4
				if (_endFaceStation != null)
				{
					var upperMat = _endFaceStation.GetCurrentUpperImage();
					if (upperMat != null && !upperMat.Empty())
					{
						var bmp = BmpConverter.ToBitmap(upperMat);
						UpdatePictureBox(xlPictureBox3, bmp);
						upperMat.Dispose();
					}
					var lowerMat = _endFaceStation.GetCurrentLowerImage();
					if (lowerMat != null && !lowerMat.Empty())
					{
						var bmp = BmpConverter.ToBitmap(lowerMat);
						UpdatePictureBox(xlPictureBox4, bmp);
						lowerMat.Dispose();
					}
				}

				// 侧面显示 — 左侧面→xlPictureBox5, 右侧面→xlPictureBox6 (左右独立, 不混显)
				if (_sideStation != null)
				{
					var leftBmp = _sideStation.GetCurrentLeftImage();
					if (leftBmp != null)
					{
						UpdatePictureBox(xlPictureBox5, leftBmp);
					}
					var rightBmp = _sideStation.GetCurrentRightImage();
					if (rightBmp != null)
					{
						UpdatePictureBox(xlPictureBox6, rightBmp);
					}
				}
			}
			catch (Exception ex)
			{
				Logger.Error($"刷新轮播图失败: {ex.Message}");
			}
		}

		/// <summary>
		/// 更新PictureBox图像(支持跨线程BeginInvoke)
		/// 流程: InvokeRequired→BeginInvoke递归→Clone副本→pb.Image=新→Dispose旧
		/// 参数: pb=目标控件, image=要显示的Bitmap
		/// 注意事项: Clone避免外部释放导致Paint异常; >1920px等比缩放; 先赋值再Dispose避免闪烁
		/// </summary>
		// 每PictureBox最小更新间隔(ms), 避免同控件短时间内重复Bitmap分配
		private readonly Dictionary<XLPictureBox, long> _pbLastUpdateTicks = new Dictionary<XLPictureBox, long>();
		private const long PbUpdateIntervalTicks = 100 * 10000; // 100ms最小间隔

		private void UpdatePictureBox(XLPictureBox pb, Bitmap image)
		{
			if (pb == null || image == null) return;

			if (pb.InvokeRequired)
			{
				pb.BeginInvoke(new Action(() => UpdatePictureBox(pb, image)));
				return;
			}

			// 节流: 同控件100ms内不重复更新, 避免Bitmap Clone堆积
			long nowTicks = DateTime.Now.Ticks;
			lock (_pbLastUpdateTicks)
			{
				if (_pbLastUpdateTicks.TryGetValue(pb, out long lastTicks))
				{
					if (nowTicks - lastTicks < PbUpdateIntervalTicks)
						return; // 距上次更新不足100ms, 跳过
				}
				_pbLastUpdateTicks[pb] = nowTicks;
			}

			try
			{
				// 优化: 如果图像引用和当前一样, 跳过(避免重复Clone)
				if (object.ReferenceEquals(pb.Image, image))
				{
					Logger.Debug("[UI] UpdatePictureBox跳过(同引用)");
					return;
				}
				Bitmap display;
				int maxW = 1920;
				if (image.Width > maxW)
				{
					float scale = (float)maxW / image.Width;
					int newH = (int)(image.Height * scale);
					display = new Bitmap(image, new DrawSize(maxW, newH));
				}
				else
				{
					display = new Bitmap(image);  // Clone独立副本，避免外部释放导致Paint时"参数无效"
				}
				var old = pb.Image;
				pb.Image = display;
				old?.Dispose();
			}
			catch (Exception ex)
			{
				Logger.Error($"更新图片显示失败: {ex.Message}");
			}
		}

		/// <summary>
		/// 绑定所有按钮事件
		/// 清空→ClearAll | 检测参数→Form(11Tab)→OnParametersChanged热更新 | 保存SKU→json | 各工位小清空 | 打开NG目录
		/// </summary>
		private void BindButtonEvents()
		{
			// 总清空
			if (clearBtn != null) clearBtn.Click += (s, e) => ClearAllStatistics();
			// 检测参数
			if (SetDetectionParametersBtn != null)
				SetDetectionParametersBtn.Click += (s, e) =>
				{
					var form = new DetectionParametersForm(_detectionParams);
					form.OnParametersChanged += (s2, e2) =>
					{
						_frontStation?.ReloadModelParams();
						_backStation?.ReloadModelParams();
						_endFaceStation?.ReloadModelParams();
						_sideStation?.ReloadModelParams();
						if (_frontStation != null)
						{
							_frontStation.EnablePNumberCheck = _detectionParams.Front.EnablePNumberCheck;
							_frontStation.EnableBoxBreakCheck = _detectionParams.Front.EnableBoxBreakCheck;
						}
						if (_backStation != null)
						{
							_backStation.EnableBarcodeCheck = _detectionParams.Back.EnableBarcodeCheck;
							_backStation.EnableHookCheck = _detectionParams.Back.EnableHookCheck;
							_backStation.EnableBoxBreakCheck = _detectionParams.Back.EnableBoxBreakCheck;
						}
						if (_endFaceStation != null) _endFaceStation.EnableUpperDefectCheck = _detectionParams.EndFace.EnableUpperDefectCheck;
						if (_sideStation != null)
						{
							_sideStation.MotionEnabled = _detectionParams.Side.MotionEnabled;
							_sideStation.EnableSideDefectCheck = _detectionParams.Side.EnableSideDefectCheck;
							_sideStation.SafetyLockPort = _detectionParams.Side.SafetyLockPort;
							_sideStation.SafetyLockActiveHigh = false; // SetInvertIn(8,1)反转后强制=false
							_sideStation.RecoveryMode = (SideStationProcessor.SafetyRecovery)_detectionParams.Side.SafetyLockRecovery;
						}
						Logger.Info("所有工位ModelParams已重新加载，无需重启");
					};
					form.ShowDialog();
				};
			// SKU保存并立即生效（手动输入优先于CSV数据）
			if (saveBtn != null) saveBtn.Click += (s, e) =>
			{
				string sku = _skuSearchCombo?.Text?.Trim() ?? "";
				if (string.IsNullOrWhiteSpace(sku)) return;
				_currentSku = _skuDb.GetBySkuNumber(sku) ?? new SkuData();
				// 记录修改前值
				int oldP = _currentSku.P, oldZ = _currentSku.Z, oldMM = _currentSku.MM;
				string oldFP = _currentSku.FrontPCode ?? "-", oldBC = _currentSku.BackBarcode ?? "-", oldCF = _currentSku.CodingFormat ?? "-";
				// 手动输入的值覆盖CSV
				if (int.TryParse(P_Lb?.Text, out int pv) && pv > 0) _currentSku.P = pv;
				if (int.TryParse(Z_Lb?.Text, out int zv)) _currentSku.Z = zv;
				if (int.TryParse(MM_Lb?.Text, out int mv)) _currentSku.MM = mv;
				if (FrontPNumber_Lb != null && !string.IsNullOrWhiteSpace(FrontPNumber_Lb.Text)) _currentSku.FrontPCode = FrontPNumber_Lb.Text.Trim();
				if (BackBarcode_Lb != null && !string.IsNullOrWhiteSpace(BackBarcode_Lb.Text)) _currentSku.BackBarcode = BackBarcode_Lb.Text.Trim();
				if (CodingFormat_Lb != null && !string.IsNullOrWhiteSpace(CodingFormat_Lb.Text)) _currentSku.CodingFormat = CodingFormat_Lb.Text.Trim();
				if (_currentSku.P <= 0) _currentSku.P = 12;
				// 根据最新P值重新匹配裁图比例.csv中的裁图像素
				_skuDb.ApplyCropData(_currentSku);
				UpdateSkuDisplay();
				// 生成变更摘要
				var changes = new List<string>();
				if (oldP != _currentSku.P) changes.Add("P: " + oldP + " → " + _currentSku.P);
				if (oldZ != _currentSku.Z) changes.Add("Z: " + oldZ + " → " + _currentSku.Z);
				if (oldMM != _currentSku.MM) changes.Add("MM: " + oldMM + " → " + _currentSku.MM);
				if (oldFP != (_currentSku.FrontPCode ?? "-")) changes.Add("P号: " + oldFP + " → " + _currentSku.FrontPCode);
				if (oldBC != (_currentSku.BackBarcode ?? "-")) changes.Add("条码: " + oldBC + " → " + _currentSku.BackBarcode);
				if (oldCF != (_currentSku.CodingFormat ?? "-")) changes.Add("格式: " + oldCF + " → " + _currentSku.CodingFormat);
				string diff = changes.Count > 0 ? "\n\n变更:\n" + string.Join("\n", changes) : "";
				_frontStation?.UpdateSku(_currentSku);
				_backStation?.UpdateSku(_currentSku);
				_sideStation?.UpdateSku(_currentSku);
				_endFaceStation?.UpdateSku(_currentSku);
				_endFaceStation?.UpdatePCount(_currentSku.P);
				_detectionParams.LastSkuNumber = sku;
				_detectionParams.SaveToFile();
				SaveSkuParams();
				SaveCounts();
				MessageBox.Show("SKU【" + sku + "】保存成功！\nP=" + _currentSku.P + " Z=" + _currentSku.Z + " MM=" + _currentSku.MM + diff, "保存成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
			};
			// 各工位小清空
			if (Clear_zheng_Btn != null)
				Clear_zheng_Btn.Click += (ss, ee) =>
				{
					_frontStation?.ClearCounters();
					if (OK_zheng_Lb != null) OK_zheng_Lb.Text = "0";
					if (NG_zheng_Lb != null) NG_zheng_Lb.Text = "0";
					if (Yield_zheng_Lb != null) Yield_zheng_Lb.Text = "0%";
				};
			if (Clear_fan_Btn != null)
				Clear_fan_Btn.Click += (ss, ee) =>
				{
					_backStation?.ClearCounters();
					if (OK_fan_Lb != null) OK_fan_Lb.Text = "0";
					if (NG_fan_Lb != null) NG_fan_Lb.Text = "0";
					if (Yield_fan_Lb != null) Yield_fan_Lb.Text = "0%";
				};
			if (Clear_duanmian_Btn != null)
				Clear_duanmian_Btn.Click += (ss, ee) =>
				{
					_endFaceStation?.ClearCounters();
					if (OK_duanmian_Lb != null) OK_duanmian_Lb.Text = "0";
					if (NG_duanmian_Lb != null) NG_duanmian_Lb.Text = "0";
					if (Yield_duanmian_Lb != null) Yield_duanmian_Lb.Text = "0%";
				};
			if (Clear_cemian_Btn != null)
				Clear_cemian_Btn.Click += (ss, ee) =>
				{
					_sideStation?.ClearCounters();
					if (OK_cemian_Lb != null) OK_cemian_Lb.Text = "0";
					if (NG_cemian_Lb != null) NG_cemian_Lb.Text = "0";
					if (Yield_cemian_Lb != null) Yield_cemian_Lb.Text = "0%";
				};
		}

		/// <summary>初始化统计标签: 4工位OK/NG/良率全部归零</summary>
		private void BindStatisticsControls()
		{
			// 初始化统计显示
			if (OK_zheng_Lb != null) OK_zheng_Lb.Text = "0";
			if (NG_zheng_Lb != null) NG_zheng_Lb.Text = "0";
			if (Yield_zheng_Lb != null) Yield_zheng_Lb.Text = "0%";

			if (OK_fan_Lb != null) OK_fan_Lb.Text = "0";
			if (NG_fan_Lb != null) NG_fan_Lb.Text = "0";
			if (Yield_fan_Lb != null) Yield_fan_Lb.Text = "0%";

			if (OK_duanmian_Lb != null) OK_duanmian_Lb.Text = "0";
			if (NG_duanmian_Lb != null) NG_duanmian_Lb.Text = "0";
			if (Yield_duanmian_Lb != null) Yield_duanmian_Lb.Text = "0%";

			if (OK_cemian_Lb != null) OK_cemian_Lb.Text = "0";
			if (NG_cemian_Lb != null) NG_cemian_Lb.Text = "0";
			if (Yield_cemian_Lb != null) Yield_cemian_Lb.Text = "0%";
		}

		#endregion

		#region 统计清除

		/// <summary>清除所有工位统计(确认对话框→4工位清零→UI归零)</summary>
		private void ClearAllStatistics()
		{
			if (MessageBox.Show("确认清除所有统计数据吗？", "确认",
				MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
			{
				_frontStation?.ClearCounters();
				_backStation?.ClearCounters();
				_endFaceStation?.ClearCounters();
				_sideStation?.ClearCounters();

				if (OK_zheng_Lb != null) OK_zheng_Lb.Text = "0";
				if (NG_zheng_Lb != null) NG_zheng_Lb.Text = "0";
				if (Yield_zheng_Lb != null) Yield_zheng_Lb.Text = "0%";
				if (OK_fan_Lb != null) OK_fan_Lb.Text = "0";
				if (NG_fan_Lb != null) NG_fan_Lb.Text = "0";
				if (Yield_fan_Lb != null) Yield_fan_Lb.Text = "0%";
				if (OK_duanmian_Lb != null) OK_duanmian_Lb.Text = "0";
				if (NG_duanmian_Lb != null) NG_duanmian_Lb.Text = "0";
				if (Yield_duanmian_Lb != null) Yield_duanmian_Lb.Text = "0%";
				if (OK_cemian_Lb != null) OK_cemian_Lb.Text = "0";
				if (NG_cemian_Lb != null) NG_cemian_Lb.Text = "0";
				if (Yield_cemian_Lb != null) Yield_cemian_Lb.Text = "0%";

				Logger.Info("所有统计数据已清除");
			}
		}

		#endregion

		#region 班次管理

		/// <summary>启动班次检查(60s): 早08~16/中16~24/晚00~08, 切换自动保存</summary>
		private void StartShiftCheckTimer()
		{
			_shiftCheckTimer = new System.Timers.Timer(60000);
			_shiftCheckTimer.Elapsed += (s, e) => CheckShiftChange();
			_shiftCheckTimer.AutoReset = true;
			_shiftCheckTimer.Start();

			_currentShift = GetCurrentShift();
			_shiftStartTime = DateTime.Now;
			Logger.Info($"当前班次: {_currentShift}");
		}

		/// <summary>检查班次是否切换: 比对_currentShift→切换时保存上一班数据→清零所有计数器→更新班次+起始时间</summary>
		private void CheckShiftChange()
		{
			string newShift = GetCurrentShift();
			if (_currentShift != newShift)
			{
				Logger.Info($"班次切换: {_currentShift} -> {newShift}，保存统计并清零计数器");
				SaveCurrentShiftStatistics();

				// 清零4工位内部计数器
				_frontStation?.ClearCounters();
				_backStation?.ClearCounters();
				_endFaceStation?.ClearCounters();
				_sideStation?.ClearCounters();

				// 重置UI标签（定时器在线程池回调，需BeginInvoke回UI线程）
				this.BeginInvoke(new Action(() =>
				{
					if (OK_zheng_Lb != null) OK_zheng_Lb.Text = "0";
					if (NG_zheng_Lb != null) NG_zheng_Lb.Text = "0";
					if (Yield_zheng_Lb != null) Yield_zheng_Lb.Text = "0%";
					if (OK_fan_Lb != null) OK_fan_Lb.Text = "0";
					if (NG_fan_Lb != null) NG_fan_Lb.Text = "0";
					if (Yield_fan_Lb != null) Yield_fan_Lb.Text = "0%";
					if (OK_duanmian_Lb != null) OK_duanmian_Lb.Text = "0";
					if (NG_duanmian_Lb != null) NG_duanmian_Lb.Text = "0";
					if (Yield_duanmian_Lb != null) Yield_duanmian_Lb.Text = "0%";
					if (OK_cemian_Lb != null) OK_cemian_Lb.Text = "0";
					if (NG_cemian_Lb != null) NG_cemian_Lb.Text = "0";
					if (Yield_cemian_Lb != null) Yield_cemian_Lb.Text = "0%";
				}));

				_currentShift = newShift;
				_shiftStartTime = DateTime.Now;
				Logger.Info($"新班次 {_currentShift} 计数器已清零");
			}
		}

		/// <summary>根据当前时间返回班次名: 00~08=晚班, 08~16=早班, 16~24=中班</summary>
		private string GetCurrentShift()
		{
			var now = DateTime.Now.TimeOfDay;
			if (now >= TimeSpan.Parse("00:00:00") && now <= TimeSpan.Parse("07:59:59"))
				return "晚班";
			if (now >= TimeSpan.Parse("08:00:00") && now <= TimeSpan.Parse("15:59:59"))
				return "早班";
			return "中班";
		}

		/// <summary>保存当前班次统计数据到SQLite: 计算4工位OK/NG总和→_dbHelper.SaveShiftStatistics(班次名+时间+总数+OK+NG)</summary>
		private void SaveCurrentShiftStatistics()
		{
			try
			{
				long totalOk = 0, totalNg = 0;

				if (OK_zheng_Lb != null) totalOk += long.Parse(OK_zheng_Lb.Text);
				if (NG_zheng_Lb != null) totalNg += long.Parse(NG_zheng_Lb.Text);
				if (OK_fan_Lb != null) totalOk += long.Parse(OK_fan_Lb.Text);
				if (NG_fan_Lb != null) totalNg += long.Parse(NG_fan_Lb.Text);
				if (OK_duanmian_Lb != null) totalOk += long.Parse(OK_duanmian_Lb.Text);
				if (NG_duanmian_Lb != null) totalNg += long.Parse(NG_duanmian_Lb.Text);
				if (OK_cemian_Lb != null) totalOk += long.Parse(OK_cemian_Lb.Text);
				if (NG_cemian_Lb != null) totalNg += long.Parse(NG_cemian_Lb.Text);

				_dbHelper.SaveShiftStatistics(_currentShift, _shiftStartTime, (int)(totalOk + totalNg), (int)totalOk, (int)totalNg);
				Logger.Info($"班次统计已保存: {_currentShift}, OK={totalOk}, NG={totalNg}");
			}
			catch (Exception ex)
			{
				Logger.Error($"保存班次统计失败: {ex.Message}");
			}
		}

		#endregion

		#region 窗体关闭

		/// <summary>
		/// 程序关闭流程(按依赖顺序释放所有资源)
		/// SaveCounts→SaveShift→_sideTriggerPending=false→工位Dispose→触发管理器→8相机Close→硬件Disconnect→性能/保存器/计时器→AI→Logger
		/// </summary>

		private void StatusPollTick(object sender = null, EventArgs e = null)
		{
			try
			{
				if (_motionMgr == null || !_motionMgr.IsConnected) return;
				var h = _motionMgr.Handle;
				if (h == IntPtr.Zero) return;

				ushort[] mbArr = { 0 };
				cszmcaux.zmcaux.ZAux_Modbus_Get4x(h, 100, 1, mbArr);
				float initVal = mbArr[0];
				if (InitState != null)
					InitState.State = (initVal == 1f) ? UILightState.On : UILightState.Off;

				uint val = 0;
				cszmcaux.zmcaux.ZAux_Direct_GetIn(h, 8, ref val);
				if (SafetyDoorState != null)
					SafetyDoorState.State = (val == 0) ? UILightState.On : UILightState.Off; // SetInvertIn后GetIn返回反转值

				// 侧面触发检查(每1s): IN5下降沿不会触发任何相机事件,
				// 导致 OnCameraTriggered 轮询不执行, 侧面永远不触发.
				// 在此独立检查 IN5+IN13 电平, 不依赖相机触发事件.
				if (_sideStation != null && _sideStation.MotionEnabled && SideEnabled)
				{
					bool in5 = false, in13 = false;
					if (_motionMgr.GetInput(IN5_BELT_STOP, out in5)
						& _motionMgr.GetInput(IN13_POSITION, out in13))
					{
						// 解锁: IN5=1(皮带运行) 或 IN13=0(工件离开)
						if (Interlocked.CompareExchange(ref _sideTriggered, 0, 0) == 1)
						{
							if (in5 || !in13)
							{
								Interlocked.Exchange(ref _sideTriggered, 0);
								Logger.Info($"[Side] 🔓 触发锁解锁(定时) IN5={in5} IN13={in13}");
							}
						}
						// 触发: IN5=0(皮带停) + IN13=1(工件到位)
						else if (!in5 && in13)
						{
							Logger.Debug("[Side] 定时器检测到 IN5=0+IN13=1, 尝试触发");
							TrySideTrigger(DateTime.Now.Ticks);
						}
					}
				}
			}
			catch { }
		}
		private void MainFrm_FormClosing(object sender, FormClosingEventArgs e)
		{
			try
			{
				Logger.Info("应用程序正在关闭...");
				_isClosing = true;

				SaveCounts();
				SaveCurrentShiftStatistics();

				Interlocked.Exchange(ref _sidePendingCount, 0);  // 清零排队计数
				_frontStation?.Dispose();
				_backStation?.Dispose();
				_endFaceStation?.Dispose();
				_sideStation?.Dispose();

				_triggerMgr?.Dispose();         // 释放触发管理器（包含后台线程）

				DisposeAllCameras();            // 释放所有相机SDK实例
				_motionMgr?.Disconnect();
				_s7Plc?.CloseModbus();         // 断开PLC连接
				_plcResultService?.Dispose();
				_perfMonitor?.Dispose();
				ModelPerfTracker.Stop();
				_sysResMonitor?.Dispose();
				_imageSaver?.Dispose();
				_statusPollTimer?.Stop(); _statusPollTimer?.Dispose();
				_shiftCheckTimer?.Stop();
				_shiftCheckTimer?.Dispose();
				_aiModels?.Dispose();

				Logger.Shutdown();
			}
			catch (Exception ex)
			{
				Console.WriteLine($"关闭异常: {ex.Message}");
			}
		}
		/// <summary>释放所有相机SDK实例</summary>
		/// <summary>释放8个相机SDK(逐个StopStreamGrabber→Close, 独立try-catch)</summary>
		private void DisposeAllCameras()
		{
			var cameras = new[] { camera1SDK, camera2SDK, camera3SDK, camera4SDK, camera5SDK, camera6SDK, camera7SDK, camera8SDK };
			foreach (var cam in cameras)
			{
				if (cam == null) continue;
				try { cam.StopStreamGrabber(); } catch (Exception ex) { Logger.Error($"相机StopStreamGrabber异常: {ex.Message}"); }
				try { cam.Close(); } catch (Exception ex) { Logger.Error($"相机Close异常: {ex.Message}"); }
			}
			Logger.Info("所有相机已释放");
		}


		#endregion

		#region 测试窗体入口

		public void OpenTestForm()
		{
			// 测试窗体逻辑
			var testForm = new Form
			{
				Text = "算法调试",
				Size = new DrawSize(800, 600),
				StartPosition = FormStartPosition.CenterParent
			};
			testForm.ShowDialog();
		}
		/// <summary>
		/// 获取运动控制管理器
		/// </summary>
		public MotionControlManager GetMotionControlManager()
		{
			return _motionMgr;
		}
		/// <summary>
		/// 获取相机管理器（已弃用，现在相机由MainFrm直接管理）
		/// </summary>
		[Obsolete("相机现在由MainFrm直接管理，请使用GetDaHuaSDK(int cameraId)")]
		public CameraManager GetCameraManager()
		{
			return null;
		}

		/// <summary>
		/// 获取AI模型管理器
		/// </summary>
		public AiModelManager GetAiModelManager()
		{
			return _aiModels;
		}

		private void BtnManualTest_Click(object sender, EventArgs e)
		{
			using (var d = new OpenFileDialog { Title = "选择背面左图", Filter = "图像|*.jpg;*.jpeg;*.png;*.bmp;*.tif" })
			{
				if (d.ShowDialog() != DialogResult.OK) return;
				var left = new Bitmap(d.FileName);
				using (var d2 = new OpenFileDialog { Title = "选择背面右图", Filter = "图像|*.jpg;*.jpeg;*.png;*.bmp;*.tif" })
				{
					if (d2.ShowDialog() != DialogResult.OK) return;
					var right = new Bitmap(d2.FileName);
					_backStation?.UpdateSku(new SkuData
					{
						SkuNumber = "MANUAL",
						P = _currentSku?.P ?? 12,
						Z = _currentSku?.Z ?? 2,
						MM = _currentSku?.MM ?? 42,
						BackBarcode = _currentSku?.BackBarcode,
						CodingFormat = _currentSku?.CodingFormat,
						FrontPCode = _currentSku?.FrontPCode
					});
					_backStation?.OnCam3(left, 0);
					_backStation?.OnCam4(right, 0);
					UIMessageTip.ShowOk(this, "已提交测试");
				}
			}
		}
		#endregion
	}
}
