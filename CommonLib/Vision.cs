using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Cognex.VisionPro;
using Cognex.VisionPro.ToolBlock;
using Cognex.VisionPro.CalibFix;
using static CommonLib.Class_Config;
using XL.Tool;
using System.Windows.Forms;
using System.IO;

namespace CommonLib
{
	public class Vision : IMainListener
	{
		XLToolClass toolClass = new XLToolClass();
		public CogToolBlock camera1Vpp { get; set; }
		/// <summary>
		/// 型号切换事件
		/// </summary>
		public delegate void ChangeModel(string Model);
		public event ChangeModel ChangeMode;
		public bool ChangeCheckProduct(string productId, string spec)
		{
			if (spec.Equals(_Config.CurCheckSpec))
				return true;

			try
			{
				_Config.CurCheckSpec = spec;
				_Config.CurProductId = productId;
				ChangeMode(_Config.CurCheckSpec);

				if (LoadVision())
				{
					//toolClass.SaveLog($"型号切换完成：{spec}");
					if (spec == _Config.CurCheckSpec)
					{
						return true;

					}
					else
					{
						return false;
					}
				}
				else
				{
					return false;
				}
			}
			catch (Exception ex)
			{
				toolClass.SaveLog("加载切换型号错误..." + "\n\r" + ex.Message);
				return false;
			}
		}
		public bool SaveVpp(int index)
		{
			index++;
			try
			{
				switch (index)
				{
					case 1:
						CogSerializer.SaveObjectToFile(camera1Vpp, $@"{_Config._vppPath + _Config.CurCheckSpec}_camera1.vpp");
						break;
					default:
						break;
				}
				toolClass.SaveLog("算法保存完成");
				return true;
			}
			catch (Exception ex)
			{
				toolClass.SaveLog("算法保存错误..." + "\n\r" + ex.Message);
				return false;
			}
		}

		public CogToolBlock GetVpp(int index)//
		{

			//index = index + 1;
			//if (index == 1)
			//{
			//	return camera1Vpp;
			//}
			return camera1Vpp;
		}

		public void CopyVpp(string spec, int index = 1)
		{
			try
			{
				for (int i = 1; i <= index; i++)
				{
					string filename = $@"{_Config._vppPath + _Config.CurCheckSpec}_camera{i}.vpp";
					string filenameNew = $@"{_Config._vppPath + spec}_camera{i}.vpp";

					if (File.Exists(filename))
					{
						File.Copy(filename, filenameNew);
					}
				}

			}
			catch (Exception ex)
			{
				toolClass.SaveLog(ex.StackTrace);
			}

		}

		public void DelVpp(string spec, int index = 1)
		{

			try
			{
				for (int i = 1; i <= index; i++)
				{
					string filename = $@"{_Config._vppPath + spec}_camera{i}.vpp";

					if (File.Exists(filename))
					{
						File.Delete(filename);
					}
				}

			}
			catch (Exception ex)
			{
				toolClass.SaveLog(ex.StackTrace);
			}
		}

		/// <summary>
		/// 加载视觉算法
		/// </summary>
		public bool LoadVision()
		{
			//try
			//{
			//	camera1Vpp = CogSerializer.LoadObjectFromFile($@"{_Config._vppPath + _Config.CurCheckSpec}_camera1.vpp") as CogToolBlock;
			return true;
			//}
			//catch (Exception ex)
			//{
			//	toolClass.SaveLog("加载视觉算法错误..." + "\n\r" + ex.Message);

			//	return false;
			//}
		}
	}
}
