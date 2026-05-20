// Program.cs
using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using Utils;

namespace VisionMeasure
{
	static class Program
	{
		[STAThread]
		static void Main()
		{
			// 全局异常处理
			Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
			Application.ThreadException += Application_ThreadException;
			AppDomain.CurrentDomain.UnhandledException += CurrentDomain_UnhandledException;

			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);

			try
			{
				Application.Run(new MainFrm());
			}
			catch (Exception ex)
			{
				Logger.Error($"程序崩溃: {ex.Message}\n{ex.StackTrace}");
				MessageBox.Show($"程序发生严重错误:\n{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			finally
			{
				Logger.Shutdown();
			}
		}

		private static void Application_ThreadException(object sender, System.Threading.ThreadExceptionEventArgs e)
		{
			Logger.Error($"UI线程异常: {e.Exception.Message}\n{e.Exception.StackTrace}");
			MessageBox.Show($"发生错误:\n{e.Exception.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
		}

		private static void CurrentDomain_UnhandledException(object sender, UnhandledExceptionEventArgs e)
		{
			var ex = e.ExceptionObject as Exception;
			Logger.Error($"未处理异常: {ex?.Message}\n{ex?.StackTrace}");

			if (!e.IsTerminating)
			{
				MessageBox.Show($"发生错误:\n{ex?.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}
	}
}