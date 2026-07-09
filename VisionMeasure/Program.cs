using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using VisionMeasure.From;
using VisionMeasure.Utils;
using CommonLib;
using Config;
using Models;

namespace VisionMeasure
{
	static class Program
	{
		/// <summary>USB加密狗验证类实例</summary>
		private static XLUsbDogClass UsbDogClass;
		private static System.Threading.Mutex _singleInstanceMutex;

		[STAThread]
		static void Main()
		{
			// ★ 全局异常捕获: 防止未处理异常导致程序崩溃(静默退出没有任何日志)
			Application.ThreadException += (s, e) =>
			{
				Logger.Error($"!!! 未处理UI线程异常: {e.Exception.Message}\n{e.Exception.StackTrace}");
				Logger.LogErrorToFile("Crash", $"UI线程异常: {e.Exception.Message}\n{e.Exception.StackTrace}");
			};
			AppDomain.CurrentDomain.UnhandledException += (s, e) =>
			{
				var ex = e.ExceptionObject as Exception;
				string msg = ex != null ? $"{ex.Message}\n{ex.StackTrace}" : e.ExceptionObject?.ToString() ?? "未知异常";
				Logger.Error($"!!! 未处理AppDomain异常(程序即将崩溃): {msg}");
				try { Logger.LogErrorToFile("Crash", $"AppDomain异常: {msg}"); } catch { }
				try { Logger.Shutdown(); } catch { }  // 尽量刷出日志
			};

			// 单实例互斥(全程持有Mutex不释放, 防止重复打开)
			bool createdNew;
			_singleInstanceMutex = new System.Threading.Mutex(false, "Global\\KochVisionMeasure_SingleInstance", out createdNew);
			if (!createdNew)
			{
				MessageBox.Show("程序已在运行中，无法重复打开。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}

			// ================================================================
			// USB加密狗验证
			// ================================================================
			try
			{
				UsbDogClass = new XLUsbDogClass();
				bool dogValid = UsbDogClass.FindUsbDog();
				if (!dogValid)
				{
					MessageBox.Show("未检测到加密狗，程序无法启动。\n请插入USB加密狗后重试。",
						"加密狗验证失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
					return;
				}
				Logger.Info("USB加密狗验证通过");
			}
			catch (Exception ex)
			{
				Logger.Error($"USB加密狗验证异常: {ex.Message}");
				MessageBox.Show($"加密狗验证失败: {ex.Message}\n请检查加密狗是否正确插入。",
					"加密狗验证失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
				return;
			}

			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);

			SystemConfig config = SystemConfig.Load();
			bool useLoadingScreen = true;

			if (useLoadingScreen)
			{
				Loading loadingFrm = Loading.ShowLoadingScreen();

				Task.Run(() =>
				{
					try
					{
						loadingFrm.UpdateProgress(10, "正在加载检测参数...");
						var detectionParams = DetectionParameters.Instance;

						loadingFrm.UpdateProgress(20, "正在加载SKU数据...");
						var skuDb = new SkuDatabase();
						skuDb.LoadData();

						loadingFrm.UpdateProgress(30, "正在预加载AI模型...");
						var modelConfig = ModelPathConfig.LoadFromSysConfig();
						var aiModels = new AiModelManager(modelConfig);
						aiModels.LoadAllModels();

						MainFrm.PreloadedSkuDb = skuDb;
						MainFrm.PreloadedModels = aiModels;

						loadingFrm.UpdateProgress(80, "预加载完成，正在启动主界面...");
						Thread.Sleep(200);
						loadingFrm.UpdateProgress(100, "启动中...");
					}
					catch (Exception ex)
					{
						Logger.Error($"预加载失败: {ex.Message}\n{ex.StackTrace}");
						Logger.LogErrorToFile("Preload", $"预加载失败: {ex.Message}\n{ex.StackTrace}");
						MessageBox.Show($"预加载失败，系统将在主界面初始化时重试:\n{ex.Message}", "警告", MessageBoxButtons.OK, MessageBoxIcon.Warning);
					}
				}).ContinueWith(t =>
				{
					loadingFrm.Invoke(new Action(() =>
					{
						Loading.CloseLoadingScreen(loadingFrm);
						var mainFrm = new MainFrm(loadingFrm);
						mainFrm.ShowDialog();
						Application.Exit();
					}));
				});

				Application.Run();
			}
			else
			{
			}
		}
	}

	/// <summary>
	/// USB加密狗操作封装类
	/// 依赖: XL.UsbDog.dll
	/// </summary>
	public class XLUsbDogClass
	{
		/// <summary>查找并验证加密狗</summary>
		public bool FindUsbDog()
		{
			try
			{
				var dogType = Type.GetType("XL.UsbDog.XLUsbDogClass, XL.UsbDog");
				if (dogType == null)
				{
					// 尝试 Assembly.LoadFrom 加载DLL
					try
					{
						var asm = System.Reflection.Assembly.LoadFrom(
							System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "XL.UsbDog.dll"));
						dogType = asm.GetType("XL.UsbDog.XLUsbDogClass");
					}
					catch { }
				}
				if (dogType == null)
				{
					Logger.Error("XL.UsbDog.dll 未找到");
					return false;
				}

				var instance = Activator.CreateInstance(dogType);
				var findMethod = dogType.GetMethod("FindUsbDog", new Type[0]);
				if (findMethod == null)
				{
					Logger.Error("FindUsbDog 方法未找到");
					return false;
				}

				var result = findMethod.Invoke(instance, null);
				return result is bool b && b;
			}
			catch (Exception ex)
			{
				Logger.Error($"FindUsbDog异常: {ex.Message}");
				return false;
			}
		}
	}
}
