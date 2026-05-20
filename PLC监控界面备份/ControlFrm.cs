using cszmcaux;
using Littleluck.Class;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using static cszmcaux.zmcaux;
using XL.Tool;
using CommonLib;
using static CommonLib.Class_Config;
using Sunny.UI;
using Sunny.UI.Win32;

namespace PLC监控
{
	public partial class ControlFrm : Form
	{
		XLToolClass toolClass = new XLToolClass();
		static IntPtr g_handle;
		takephotoVm myZmcaux = new takephotoVm();
		private int axis = 0;           // 轴号

		Thread updeteThread;

		public ControlFrm(IntPtr handle)
		{
			InitializeComponent();
			g_handle = handle;
		}

		private void ControlFrm_Load(object sender, EventArgs e)
		{
			GetParam(axis);
			updeteThread = new Thread(UpdateLocation);
			updeteThread.IsBackground = true;
			updeteThread.Start();

		}

		private void ControlFrm_FormClosing(object sender, FormClosingEventArgs e)
		{
			// timer1.Enabled = false;
			// ZAux_Close(g_handle);
			// g_handle = (IntPtr)0;
		}
		private void UpdateLocation()
		{
			try
			{
				while (true)
				{
					Thread.Sleep(10);
					location1_Txt.Text = myZmcaux.GetLocation(g_handle, 0).ToString("F3");
					location2_Txt.Text = myZmcaux.GetLocation(g_handle, 1).ToString("F3");
				}
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"保存数据时异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}

		}


		private void saveBtn_Click(object sender, EventArgs e)
		{
			try
			{
				_Config.zhengPosition = Convert.ToDouble(left_Txt.Text);
				_Config.fanPosition = Convert.ToDouble(right_Txt.Text);

				ControlParms parms = new ControlParms()
				{
					Units = Convert.ToSingle(units_Txt.Text),
					Speed = Convert.ToSingle(speed_Txt.Text),
					Accel = Convert.ToSingle(accel_Txt.Text),
					Decel = Convert.ToSingle(decel_Txt.Text),
					Sramp = Convert.ToSingle(sramp_Txt.Text),
					Lspeed = Convert.ToSingle(lspeed_Txt.Text),
				};

				if (axis == 0)
				{
					_Config.axis0_Units = Convert.ToSingle(units_Txt.Text);
					_Config.axis0_Speed = Convert.ToSingle(speed_Txt.Text);
					_Config.axis0_Accel = Convert.ToSingle(accel_Txt.Text);
					_Config.axis0_Decel = Convert.ToSingle(decel_Txt.Text);
					_Config.axis0_Sramp = Convert.ToSingle(sramp_Txt.Text);
					_Config.axis0_Lspeed = Convert.ToSingle(lspeed_Txt.Text);
				}
				else if (axis == 1)
				{
					_Config.axis1_Units = Convert.ToSingle(units_Txt.Text);
					_Config.axis1_Speed = Convert.ToSingle(speed_Txt.Text);
					_Config.axis1_Accel = Convert.ToSingle(accel_Txt.Text);
					_Config.axis1_Decel = Convert.ToSingle(decel_Txt.Text);
					_Config.axis1_Sramp = Convert.ToSingle(sramp_Txt.Text);
					_Config.axis1_Lspeed = Convert.ToSingle(lspeed_Txt.Text);
				}
				else
				{
					toolClass.SaveLog("轴错误axis：" + axis);
				}


				int res = myZmcaux.SetParse(g_handle, axis, parms);
				MessageBox.Show(res == 0 ? "参数保存成功" : $"保存失败 错误码：{res}");
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"保存数据时异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}

		private void GetParam(int axis)
		{
			try
			{
				ControlParms parms = myZmcaux.GetParms(g_handle, axis, out int res);
				if (parms == null)
					return;
				units_Txt.Text = parms.Units.ToString();
				speed_Txt.Text = parms.Speed.ToString();
				accel_Txt.Text = parms.Accel.ToString();
				decel_Txt.Text = parms.Decel.ToString();
				sramp_Txt.Text = parms.Sramp.ToString();
				lspeed_Txt.Text = parms.Lspeed.ToString();


				left_Txt.Text = _Config.zhengPosition.ToString();
				right_Txt.Text = _Config.fanPosition.ToString();
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"获取数据时异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}

		private void leftBtn_Click(object sender, EventArgs e)
		{

		}

		private void rightBtn_Click(object sender, EventArgs e)
		{
			try
			{

			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"手动调试时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}

		private void goBtn_Click(object sender, EventArgs e)
		{
			try
			{
				Task.Run(() =>
				{

					float x = Convert.ToSingle(point_Txt.Text);

					goBtn.Enabled = false;
					leftBtn.Enabled = false;
					rightBtn.Enabled = false;

					myZmcaux.GoPosition(g_handle, axis, x);

					goBtn.Enabled = true;
					leftBtn.Enabled = true;
					rightBtn.Enabled = true;
				});

			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"手动调试时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}

		private void stopBtn_Click(object sender, EventArgs e)
		{
			try
			{
				//myZmcaux.StopMethod(g_handle);
				myZmcaux.StopMove(g_handle, axis);
				toolClass.SaveLog($"停止成功");
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"手动调试时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}

		private void leftBtn_MouseDown(object sender, MouseEventArgs e)
		{
			try
			{
				myZmcaux.Vmove(g_handle, axis, 1);
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"手动调试时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}



		private void rightBtn_MouseDown(object sender, MouseEventArgs e)
		{
			try
			{
				myZmcaux.Vmove(g_handle, axis, -1);
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"手动调试时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}

		private void leftBtn_MouseUp(object sender, MouseEventArgs e)
		{
			try
			{
				rightBtn_MouseUp(null, null);
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"手动调试时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}
		private void rightBtn_MouseUp(object sender, MouseEventArgs e)
		{
			try
			{
				myZmcaux.StopMove(g_handle, 0);
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"手动调试时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}


		private void zuo_Click(object sender, EventArgs e)
		{
			try
			{
				point_Txt.Text = left_Txt.Text;
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"手动调试时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}

		private void you_Click(object sender, EventArgs e)
		{
			try
			{
				point_Txt.Text = right_Txt.Text;
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"手动调试时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}

		private void uiRadioButton1_CheckedChanged(object sender, EventArgs e)
		{
			try
			{
				axis = 0;
				GetParam(axis);
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"手动调试时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}

		private void uiRadioButton2_CheckedChanged(object sender, EventArgs e)
		{
			try
			{
				axis = 1;
				GetParam(axis);
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"手动调试时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}
	}
}
