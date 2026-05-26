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

						loadingFrm.UpdateProgress(30, "正在预加载AI模型...");
						var modelConfig = ModelPathConfig.LoadFromSysConfig();
						var aiModels = new AiModelManager(modelConfig);
						aiModels.LoadAllModels();

						// 传递给MainFrm以避免重复加载
						MainFrm.PreloadedSkuDb = skuDb;
						MainFrm.PreloadedModels = aiModels;

						loadingFrm.UpdateProgress(80, "预加载完成，正在启动主界面...");
						Thread.Sleep(200);
						loadingFrm.UpdateProgress(100, "启动中...");
					}
					catch (Exception ex)
					{
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
				Application.Run(new MainFrm());
			}
		}
	}
}