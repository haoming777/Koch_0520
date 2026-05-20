using CommonLib;
using Littleluck.Class;
using MT.Camera.SDK;
using PLC调试.Class;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using XL.Tool;
using static CommonLib.Class_Config;


namespace SetCamera
{
	public partial class MainFrm : Form
	{
		public MainFrm()
		{
			InitializeComponent();
		}

		public MainFrm(IntPtr handle, HCModbusClass modbusClass1)
		{
			InitializeComponent();
			g_handle = handle;
			modbusClass = modbusClass1;
		}
		static IntPtr g_handle;
		takephotoVm myZmcaux = new takephotoVm();
		private int axis = 0;           // 轴号

		public DaHuaSDK cam1, cam2, cam3, cam4, cam5, cam6, cam7, cam8;
		Bitmap bitmap = null;
		XLToolClass toolClass = new XLToolClass();
		HCModbusClass modbusClass;

		/// <summary>
		/// 指向当前选中得相机
		/// </summary>
		DaHuaSDK daHuaSDK = new DaHuaSDK();

		Thread updeteThread;

		//int cam1TriggerPath = 4;
		//int cam2TriggerPath = 5;
		//int cam3TriggerPath = 5;
		//int cam4TriggerPath = 5;
		//int cam5TriggerPath = 5;
		//int tempTriggerPath = 0;

		string cam1TriggerPath = "MX7080.4";
		string cam2TriggerPath = "MX7080.4";
		string cam3TriggerPath = "MX7080.4";
		string cam4TriggerPath = "MX7080.4";
		string cam5TriggerPath = "MX7080.4";
		string tempTriggerPath = "MX7080.4";

		#region 页面事件
		private void MainFrm_Load(object sender, EventArgs e)
		{
			try
			{
				updeteThread = new Thread(UpdateLocation);
				updeteThread.IsBackground = true;
				updeteThread.Start();

				leftLb.Text = _Config.zhengPosition.ToString("F2");
				rightLb.Text = _Config.fanPosition.ToString("F2");
				roundLb.Text = _Config.roundPosition.ToString("F2");

				xlPictureBox1.ISRealTimeDisplay = true;
				//cam1 = GlobalVar.CameraSdk1;
				//cam1.OnImage += Cam1_OnImage;
				//cam2.OnImage += Cam1_OnImage;
				//cam3.OnImage += Cam1_OnImage;
				//cam4.OnImage += Cam1_OnImage;
				//cam5.OnImage += Cam1_OnImage;

				uiComboBox_cam.SelectedIndex = 0;
				uiComboBox_axis.SelectedIndex = 0;

				cam1TriggerPath = _Config.Output_Camera1;
				cam2TriggerPath = _Config.Output_Camera2;
				cam3TriggerPath = _Config.Output_Camera3;
				cam4TriggerPath = _Config.Output_Camera4;
				cam5TriggerPath = _Config.Output_Camera5;



				uiComboBox1_SelectedIndexChanged(null, null);
				uiComboBox_axis_SelectedIndexChanged(null, null);

			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"加载相机信息错误！！！ \r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}


		private void MainFrm_FormClosing(object sender, FormClosingEventArgs e)
		{
			try
			{
				if (uiButton4.Text == "停止实时")
				{
					MessageBox.Show("请先停止实时取像模式！", "系统提示", MessageBoxButtons.OK, MessageBoxIcon.Stop);
					e.Cancel = true;
				}
				cam1.OnImage -= Cam1_OnImage;
				cam2.OnImage -= Cam1_OnImage;
				cam3.OnImage -= Cam1_OnImage;
				cam4.OnImage -= Cam1_OnImage;
				cam5.OnImage -= Cam1_OnImage;
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"程序错误！！！ \r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}
		/// <summary>
		/// 单张取像
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void uiButton3_Click(object sender, EventArgs e)
		{
			try
			{
				TriggerCameraMethod(true);
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message);
				return;
			}
		}

		/// <summary>
		/// 实时取像
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void uiButton4_Click(object sender, EventArgs e)
		{
			if (uiButton4.Text.Equals("实时取像"))
			{
				uiButton4.Text = "停止实时";

				uiButton3.Enabled = false;
				//xlPictureBox1.ISRealTimeDisplay = true;
				TriggerCameraMethod(false);
				//daHuaSDK.SetTriggerMode(0);
				uiComboBox_cam.Enabled = false;
				uiComboBox_axis.Enabled = false;
			}
			else
			{
				uiButton4.Text = "实时取像";

				uiButton3.Enabled = true;
				//daHuaSDK.SetTriggerMode(1);
				//xlPictureBox1.ISRealTimeDisplay = false;
				TriggerFlag = false;
				uiComboBox_cam.Enabled = true;
				uiComboBox_axis.Enabled = true;
			}
		}

		/// <summary>
		/// 
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void saveImageBtn_Click(object sender, EventArgs e)
		{
			try
			{
				if (this.xlPictureBox1.Image == null)
				{
					return;
				}
				SaveFileDialog dlg = new SaveFileDialog();
				dlg.Filter = "Bmp 图片|*.bmp";
				dlg.FilterIndex = 0;
				dlg.RestoreDirectory = true;//保存对话框是否记忆上次打开的目录
				dlg.CheckPathExists = true;//
				if (dlg.ShowDialog() == DialogResult.OK)
					this.xlPictureBox1.Image.Save(dlg.FileName, System.Drawing.Imaging.ImageFormat.Bmp);
				MessageBox.Show("保存图片完成", "系统提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
			catch (Exception ex)
			{

				toolClass.SaveLog($"手动调试时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
				throw;
			}
		}


		private void gainTxt_KeyDown(object sender, KeyEventArgs e)
		{
			try
			{
				if (e.KeyCode == Keys.Enter)
				{
					daHuaSDK.SetGainRaw(double.Parse(gainTxt.Text));
					uiComboBox1_SelectedIndexChanged(null, null);
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message);
				toolClass.SaveLog($"手动调试时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
				return;
			}
		}


		private void exposureTxt_KeyDown(object sender, KeyEventArgs e)
		{
			try
			{
				if (e.KeyCode == Keys.Enter)
				{
					daHuaSDK.SetExposureTime(double.Parse(exposureTxt.Text));
					uiComboBox1_SelectedIndexChanged(null, null);
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message);
				toolClass.SaveLog($"手动调试时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
				return;
			}
		}


		#endregion

		#region 相机回调事件
		private void Cam1_OnImage(Bitmap bitmap, string cameraName, string cameraKey)
		{
			try
			{
				if (this.IsHandleCreated)
				{
					this.xlPictureBox1.Invoke((EventHandler)delegate
					{
						this.xlPictureBox1.Image = bitmap;
					});
				}
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"手动调试时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}

		#endregion

		private void uiComboBox_axis_SelectedIndexChanged(object sender, EventArgs e)
		{
			try
			{
				switch (uiComboBox_axis.SelectedIndex + 1)
				{
					case 1:
						axis = 1;

						uiComboBox_cam.SelectedIndex = 3;
						break;
					case 2:
						axis = 0;
						uiComboBox_cam.SelectedIndex = 4;
						break;
					case 3:
						axis = 2;
						uiComboBox_cam.SelectedIndex = 2;

						break;
					default:
						break;

				}
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"手动调试时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}


		private void uiComboBox1_SelectedIndexChanged(object sender, EventArgs e)
		{
			try
			{
				switch (uiComboBox_cam.SelectedIndex + 1)
				{
					case 1:
						tempTriggerPath = cam1TriggerPath;
						cam2.OnImage -= Cam1_OnImage;
						cam3.OnImage -= Cam1_OnImage;
						cam4.OnImage -= Cam1_OnImage;
						cam5.OnImage -= Cam1_OnImage;
						cam1.OnImage += Cam1_OnImage;
						daHuaSDK = cam1;
						toolClass.SaveLog($"切换为相机一：tempTriggerPath: {tempTriggerPath} ------------------------------------------------------------------------");



						break;
					case 2:
						tempTriggerPath = cam2TriggerPath;
						cam1.OnImage -= Cam1_OnImage;
						cam3.OnImage -= Cam1_OnImage;
						cam4.OnImage -= Cam1_OnImage;
						cam5.OnImage -= Cam1_OnImage;
						cam2.OnImage += Cam1_OnImage;
						daHuaSDK = cam2;
						toolClass.SaveLog($"切换为相机二：tempTriggerPath: {tempTriggerPath}	------------------------------------------------------------------------");
						break;
					case 3:
						tempTriggerPath = cam3TriggerPath;
						cam1.OnImage -= Cam1_OnImage;
						cam2.OnImage -= Cam1_OnImage;
						cam4.OnImage -= Cam1_OnImage;
						cam5.OnImage -= Cam1_OnImage;
						cam3.OnImage += Cam1_OnImage;
						daHuaSDK = cam3;

						uiComboBox_axis.SelectedIndex = 2;

						toolClass.SaveLog($"切换为相机三：tempTriggerPath: {tempTriggerPath} ------------------------------------------------------------------------");
						break;
					case 4:
						tempTriggerPath = cam4TriggerPath;
						cam1.OnImage -= Cam1_OnImage;
						cam3.OnImage -= Cam1_OnImage;
						cam2.OnImage -= Cam1_OnImage;
						cam5.OnImage -= Cam1_OnImage;
						cam4.OnImage += Cam1_OnImage;
						daHuaSDK = cam4;
						uiComboBox_axis.SelectedIndex = 0;
						toolClass.SaveLog($"切换为相机四：tempTriggerPath: {tempTriggerPath}------------------------------------------------------------------------");
						break;
					case 5:
						tempTriggerPath = cam5TriggerPath;
						cam1.OnImage -= Cam1_OnImage;
						cam3.OnImage -= Cam1_OnImage;
						cam4.OnImage -= Cam1_OnImage;
						cam2.OnImage -= Cam1_OnImage;
						cam5.OnImage += Cam1_OnImage;
						daHuaSDK = cam5;
						uiComboBox_axis.SelectedIndex = 1;
						toolClass.SaveLog($"切换为相机五：tempTriggerPath: {tempTriggerPath}------------------------------------------------------------------------");
						break;
					default:
						break;

				}
				cameraSNLb.Text = daHuaSDK.curCameraKey;
				exposureLb.Text = daHuaSDK.GetExposureTime().ToString();
				gainLb.Text = daHuaSDK.GetGainRaw().ToString();
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"手动调试时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}

		}


		#region 运动控制部分
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
					toolClass.SaveLog("goBtn，完成：" + myZmcaux.GetLocation(g_handle, axis));

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

		private void GetParam(int axis)
		{
			try
			{
				ControlParms parms = myZmcaux.GetParms(g_handle, axis, out int res);
				if (parms == null)
					return;
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"获取数据时异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}

		private void leftTxt_KeyDown(object sender, KeyEventArgs e)
		{

			try
			{
				if (e.KeyCode == Keys.Enter)
				{
					_Config.zhengPosition = Convert.ToDouble(leftTxt.Text);
					Thread.Sleep(10);
					leftTxt.Text = _Config.zhengPosition.ToString("F2");
					leftLb.Text = _Config.zhengPosition.ToString("F2");
					roundLb.Text = _Config.roundPosition.ToString("F2");

				}
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message);
				return;
			}

		}

		private void rightTxt_KeyDown(object sender, KeyEventArgs e)
		{
			try
			{
				if (e.KeyCode == Keys.Enter)
				{
					_Config.fanPosition = Convert.ToDouble(rightTxt.Text);
					Thread.Sleep(10);
					rightTxt.Text = _Config.fanPosition.ToString("F2");
					rightLb.Text = _Config.fanPosition.ToString("F2");
					roundLb.Text = _Config.roundPosition.ToString("F2");
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message);
				return;
			}
		}

		private void uiLabel8_DoubleClick(object sender, EventArgs e)
		{
			try
			{
				leftTxt.Text = location2_Txt.Text;
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message);
				return;
			}
		}

		private void uiLabel9_DoubleClick(object sender, EventArgs e)
		{
			try
			{
				rightTxt.Text = location1_Txt.Text;
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message);
				return;
			}
		}

		private void leftLb_DoubleClick(object sender, EventArgs e)
		{
			try
			{
				point_Txt.Text = leftLb.Text;
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message);
				return;
			}
		}

		private void rightLb_DoubleClick(object sender, EventArgs e)
		{
			try
			{
				point_Txt.Text = rightLb.Text;
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message);
				return;
			}
		}

		private void UpdateLocation()
		{
			try
			{
				while (true)
				{
					Thread.Sleep(10);
					location1_Txt.Text = myZmcaux.GetLocation(g_handle, 0).ToString("F2");
					location2_Txt.Text = myZmcaux.GetLocation(g_handle, 1).ToString("F2");
					location3_Txt.Text = myZmcaux.GetLocation(g_handle, 2).ToString("F2");
				}
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"保存数据时异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}

		}

		bool TriggerFlag = true;
		/// <summary>
		/// 触发拍照
		/// </summary>
		/// <param name="type">true: 单次； false：连续</param>
		public void TriggerCameraMethod(bool type)
		{
			try
			{
				TriggerFlag = true;

				if (!modbusClass.modbusState)
				{
					MessageBox.Show($"modbusClass.modbusState: {modbusClass.modbusState}");
					return;
				}

				Task.Run(() =>
				{
					while (TriggerFlag)
					{
						//myZmcaux.SetOut(g_handle, tempTriggerPath, 1);
						//Thread.Sleep(10);
						//myZmcaux.SetOut(g_handle, tempTriggerPath, 0);

						//toolClass.SaveLog(tempTriggerPath+"");
						modbusClass.modbusTcp.Write(tempTriggerPath, true);
						Thread.Sleep(100);
						if (type)
						{
							TriggerFlag = false;
							modbusClass.modbusTcp.Write(tempTriggerPath, false);
							return;
						}
					}
					modbusClass.modbusTcp.Write(tempTriggerPath, false);
				});
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"手动调试时...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}

		#region Low
		/// <summary>
		/// 触发拍照
		/// </summary>
		/// <param name="type">true: 单次； false：连续</param>
		//public void TriggerCameraMethod(bool type)
		//{
		//	try
		//	{
		//		TriggerFlag = true;

		//		Task.Run(() =>
		//		{
		//			while (TriggerFlag)
		//			{
		//				myZmcaux.SetOut(g_handle, tempTriggerPath, 1);
		//				//Thread.Sleep(10);
		//				myZmcaux.SetOut(g_handle, tempTriggerPath, 0);

		//				//toolClass.SaveLog(tempTriggerPath+"");
		//				if (type)
		//				{
		//					TriggerFlag = false;
		//					return;
		//				}
		//			}
		//		});
		//	}
		//	catch (Exception ex)
		//	{
		//		toolClass.SaveLog($"手动调试时...\r\n {ex.Message} \r\n {ex.StackTrace}");
		//	}
		//}
		#endregion

		#endregion
	}
}
