using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using VisionMeasure.From;
using VisionMeasure.Utils;
using CommonLib;
using Config;
using Hardware;
using Models;

namespace VisionMeasure
{
	static class Program
	{
		[STAThread]
		static void Main()
		{
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

						loadingFrm.UpdateProgress(30, "正在加载Yolo/Vimo AI模型...");
						var modelConfig = ModelPathConfig.LoadFromSysConfig();
						var aiModels = new AiModelManager(modelConfig);
						aiModels.LoadAllModels();

						loadingFrm.UpdateProgress(60, "正在连接运动控制卡...");
						string controlIp = SystemConfig.GetValue("MotionControlIp", "192.168.0.11");
						var motionMgr = new MotionControlManager(controlIp, detectionParams.Camera.GetSimulateMode());
						motionMgr.Connect();

						loadingFrm.UpdateProgress(75, "正在连接PLC...");
						string plcIp = SystemConfig.GetValue("PlcIp", "192.168.1.101");
						int plcPort = SystemConfig.GetInt("PlcPort", 502);
						var plcComm = new PlcCommunication(plcIp, plcPort, detectionParams.Camera.GetSimulateMode());
						plcComm.Connect();

						loadingFrm.UpdateProgress(90, "正在初始化8个相机SDK（并行加载）...");
						var cameraMgr = new CameraManager(detectionParams.Camera.GetSimulateMode());
						cameraMgr.InitializeAll();
						cameraMgr.StartAll();

						loadingFrm.UpdateProgress(100, "加载完成，准备启动...");
						Thread.Sleep(300);
					}
					catch (Exception ex)
					{
						MessageBox.Show($"初始化失败，系统将退出:\n{ex.Message}", "严重错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
						Environment.Exit(0);
					}
				}).ContinueWith(t =>
				{
					loadingFrm.Invoke(new Action(() =>
					{
						Loading.CloseLoadingScreen(loadingFrm);
						var mainFrm = new MainFrm();
						mainFrm.ShowDialog();
						Application.Exit();
					}));
				});

				Application.Run();
			}
			else
			{
				Application.Run(new MainFrm());
			}
		}
	}
}