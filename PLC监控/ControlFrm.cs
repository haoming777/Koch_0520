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
using System.Runtime.InteropServices.ComTypes;

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

			uiRadioButton1.Checked = true;
			uiRadioButton1_CheckedChanged(null, null);
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
					this.Invoke(new Action(() =>
					{
						location_1_Txt.Text = myZmcaux.GetLocation(g_handle, 1).ToString("F2");
						location_2_Txt.Text = myZmcaux.GetLocation(g_handle, 0).ToString("F2");
						location_3_Txt.Text = myZmcaux.GetLocation(g_handle, 2).ToString("F2");
					}));
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
				else if (axis == 2)
				{
					_Config.axis2_Units = Convert.ToSingle(units_Txt.Text);
					_Config.axis2_Speed = Convert.ToSingle(speed_Txt.Text);
					_Config.axis2_Accel = Convert.ToSingle(accel_Txt.Text);
					_Config.axis2_Decel = Convert.ToSingle(decel_Txt.Text);
					_Config.axis2_Sramp = Convert.ToSingle(sramp_Txt.Text);
					_Config.axis2_Lspeed = Convert.ToSingle(lspeed_Txt.Text);
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
					toolClass.SaveLog("goBtn，point：" + point_Txt.Text);
					float x = Convert.ToSingle(point_Txt.Text);

					this.Invoke(new Action(() =>
					{
						goBtn.Enabled = false;
						leftBtn.Enabled = false;
						rightBtn.Enabled = false;

						myZmcaux.GoPosition(g_handle, axis, x);

						goBtn.Enabled = true;
						leftBtn.Enabled = true;
						rightBtn.Enabled = true;
					}));

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
				myZmcaux.Vmove(g_handle, axis, -1);
				//toolClass.SaveLog($"Vmove -1 axis：{axis}");
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
				myZmcaux.Vmove(g_handle, axis, 1);
				//toolClass.SaveLog($"Vmove 1 axis：{axis}");
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
				myZmcaux.StopMove(g_handle, axis);
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
				axis = 1;
				toolClass.SaveLog($"轴号发生变化axis：{axis}");
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
				axis = 0;
				toolClass.SaveLog($"轴号发生变化axis：{axis}");
				GetParam(axis);
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"手动调试时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}

		private void uiRadioButton3_CheckedChanged(object sender, EventArgs e)
		{
			try
			{
				axis = 2;
				toolClass.SaveLog($"轴号发生变化axis：{axis}");
				GetParam(axis);
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"手动调试时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}

		/// <summary>
		/// 正面回零
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void initZBtn_Click(object sender, EventArgs e)
		{
			try
			{
				Task.Run(() =>
				{

					int datum = _Config.axis1_Datum;
					int fwd = _Config.axis1_Fwd;
					int rev = _Config.axis1_Rev;

					myZmcaux.StopMove(g_handle, 1);
					toolClass.SaveLog($"\r\n轴1开始初始化,\r\naxis1_Datum: {datum},\r\naxis1_Fwd: {fwd},\r\axis1_Rev: {rev}");
					int res = myZmcaux.GoHome(g_handle, 1, datum, fwd, rev, Convert.ToSingle(_Config.axis_CreepSpeed_Init));
					if (res == 0)
					{
						toolClass.SaveLog("轴1初始化完成");
					}
					else
					{
						toolClass.SaveLog($"轴1初始化失败：res：{res}");
					}
				});

			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"手动调试时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}

		/// <summary>
		/// 反面回零
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void initFBtn_Click(object sender, EventArgs e)
		{
			try
			{
				Task.Run(() =>
				{

					int datum = _Config.axis0_Datum;
					int fwd = _Config.axis0_Fwd;
					int rev = _Config.axis0_Rev;

					myZmcaux.StopMove(g_handle, 0);
					toolClass.SaveLog($"\r\n轴0开始初始化,\r\naxis0_Datum: {datum},\r\naxis0_Fwd: {fwd},\r\naxis0_Rev: {rev}");
					int res = myZmcaux.GoHome(g_handle, 0, datum, fwd, rev, Convert.ToSingle(_Config.axis_CreepSpeed_Init));
					if (res == 0)
					{
						toolClass.SaveLog("轴0初始化完成");
					}
					else
					{
						toolClass.SaveLog($"轴0初始化失败：res：{res}");
					}

				});
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"手动调试时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}

		private void uiButton1_Click(object sender, EventArgs e)
		{
			try
			{
				Task.Run(() =>
				{

					int datum = _Config.axis2_Datum;
					int fwd = _Config.axis2_Fwd;
					int rev = _Config.axis2_Rev;

					myZmcaux.StopMove(g_handle, 2);
					toolClass.SaveLog($"\r\n轴2开始初始化,\r\naxis2_Datum: {datum},\r\naxis2_Fwd: {fwd},\r\naxis2_Rev: {rev}");
					int res = myZmcaux.GoHome(g_handle, 2, datum, fwd, rev, Convert.ToSingle(_Config.axis_CreepSpeed_Init));
					if (res == 0)
					{
						toolClass.SaveLog("轴2初始化完成");
					}
					else
					{
						toolClass.SaveLog($"轴2初始化失败：res：{res}");
					}

				});
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"手动调试时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}

		/// <summary>
		/// 同时回零
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void initAllBtn_Click(object sender, EventArgs e)
		{
			try
			{
				//int datum0 = _Config.axis0_Datum;
				//int fwd0 = _Config.axis0_Fwd;
				//int rev0 = _Config.axis0_Rev;

				//int datum1 = _Config.axis1_Datum;
				//int fwd1 = _Config.axis1_Fwd;
				//int rev1 = _Config.axis1_Rev;

				//myZmcaux.StopMove(g_handle, 0);
				//toolClass.SaveLog("轴0开始初始化");
				//myZmcaux.GoHome(g_handle, 0, datum0, fwd0, rev0, 1);
				//toolClass.SaveLog("轴0初始化完成");

				//myZmcaux.StopMove(g_handle, 1);
				//toolClass.SaveLog("轴1开始初始化");
				//myZmcaux.GoHome(g_handle, 1, datum1, fwd1, rev1, 1);
				//toolClass.SaveLog("轴1初始化完成");
				Task.Run(() =>
				{

					myZmcaux.GoHomePlus(g_handle);

				});
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"手动调试时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}

    
    }
}
