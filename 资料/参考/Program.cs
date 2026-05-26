using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

//挂钩
//namespace HookInspectionSystem
//{
//	internal static class Program
//	{
//		/// <summary>
//		/// 应用程序的主入口点。
//		/// </summary>
//		[STAThread]
//		static void Main()
//		{
//			Application.EnableVisualStyles();
//			Application.SetCompatibleTextRenderingDefault(false);
//			Application.Run(new HookInspectionForm挂钩());
//		}
//	}
//}

//正面
//namespace HookInspectionSystem
//{
//	internal static class Program
//	{
//		/// <summary>
//		/// 应用程序的主入口点。
//		/// </summary>
//		[STAThread]
//		static void Main()
//		{
//			Application.EnableVisualStyles();
//			Application.SetCompatibleTextRenderingDefault(false);
//			Application.Run(new FrontDamageForm());
//		}
//	}
//}
//侧面
//namespace YoloMigration
//{
//	internal static class Program
//	{
//		/// <summary>
//		/// 应用程序的主入口点。
//		/// </summary>
//		[STAThread]
//		static void Main()
//		{
//			Application.EnableVisualStyles();
//			Application.SetCompatibleTextRenderingDefault(false);
//			Application.Run(new SideDefectForm());
//		}
//	}
//}

//端面
namespace HookInspectionSystem
{
	internal static class Program
	{
		/// <summary>
		/// 应用程序的主入口点。
		/// </summary>
		[STAThread]
		static void Main()
		{
			Application.EnableVisualStyles();
			Application.SetCompatibleTextRenderingDefault(false);
			Application.Run(new DownDefectForm());
		}
	}
}