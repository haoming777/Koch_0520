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

		/// <summary>从MainFrm获取相机SDK实例的构造函数</summary>
		public MainFrm(IntPtr handle, HCModbusClass modbusClass1,
			DaHuaSDK sdk1, DaHuaSDK sdk2, DaHuaSDK sdk3, DaHuaSDK sdk4,
			DaHuaSDK sdk5, DaHuaSDK sdk6, DaHuaSDK sdk7, DaHuaSDK sdk8)
		{
			InitializeComponent();
			g_handle = handle;
			modbusClass = modbusClass1;
			cam1 = sdk1; cam2 = sdk2; cam3 = sdk3; cam4 = sdk4;
			cam5 = sdk5; cam6 = sdk6; cam7 = sdk7; cam8 = sdk8;
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

			// 添加8个相机选项
				uiComboBox_cam.Items.Clear();
				uiComboBox_cam.Items.Add("相机1-正面左"); uiComboBox_cam.Items.Add("相机2-正面右");
				uiComboBox_cam.Items.Add("相机3-上端面"); uiComboBox_cam.Items.Add("相机4-下端面");
				uiComboBox_cam.Items.Add("相机5-背面左"); uiComboBox_cam.Items.Add("相机6-背面右");
				uiComboBox_cam.Items.Add("相机7-左侧面"); uiComboBox_cam.Items.Add("相机8-右侧面");
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
					return;
				}
				UnsubscribeAllOnImage();
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"关闭窗口异常: {ex.Message}");
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
				// 警告：实时取像会切换相机模式，影响正常生产触发
				DialogResult dr = MessageBox.Show(
					"实时取像将切换相机为连续采集模式，\r\n期间该相机无法响应生产触发信号！\r\n\r\n确定继续吗？",
					"警告", MessageBoxButtons.OKCancel, MessageBoxIcon.Warning);
				if (dr != DialogResult.OK) return;

				uiButton4.Text = "停止实时";
				uiButton3.Enabled = false;
				TriggerCameraMethod(false);
				uiComboBox_cam.Enabled = false;
				uiComboBox_axis.Enabled = false;
			}
			else
			{
				uiButton4.Text = "实时取像";
				uiButton3.Enabled = true;
				TriggerFlag = false;

				// 恢复相机到触发模式
				try
				{
					daHuaSDK.StopStreamGrabber();
					Thread.Sleep(50);
					daHuaSDK.SetAcquisitionMode(0);  // 单帧模式
					daHuaSDK.SetTriggerMode(1);      // 触发模式
					daHuaSDK.setTriggerSource(1);    // 外触发
					daHuaSDK.StartStreamGrabber();
					toolClass.SaveLog("实时取像停止，已恢复触发模式");
				}
				catch (Exception ex) { toolClass.SaveLog($"恢复触发模式失败: {ex.Message}"); }

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
		private string _selectedCamKey = "";

		private void Cam1_OnImage(Bitmap bitmap, string cameraName, string cameraKey)
		{
			try
			{
				// 只显示当前选中相机的图像
				if (string.IsNullOrEmpty(_selectedCamKey) || cameraKey != _selectedCamKey) return;
				if (this.IsHandleCreated && !this.IsDisposed && bitmap != null)
				{
					this.BeginInvoke(new Action(() =>
					{
						try { if (!this.IsDisposed && xlPictureBox1 != null) { var old = xlPictureBox1.Image; xlPictureBox1.Image = bitmap; old?.Dispose(); } }
						catch { }
					}));
				}
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"相机回调异常: {ex.Message}");
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
				// 先取消所有订阅
				UnsubscribeAllOnImage();
				int idx = uiComboBox_cam.SelectedIndex + 1;
				// 选择新相机
				switch (idx)
				{
					case 1: daHuaSDK = cam1; break;
					case 2: daHuaSDK = cam2; break;
					case 3: daHuaSDK = cam3; break;
					case 4: daHuaSDK = cam4; break;
					case 5: daHuaSDK = cam5; break;
					case 6: daHuaSDK = cam6; break;
					case 7: daHuaSDK = cam7; break;
					case 8: daHuaSDK = cam8; break;
				}
				if (daHuaSDK != null)
				{
					daHuaSDK.OnImage += Cam1_OnImage;
					_selectedCamKey = daHuaSDK.curCameraKey;
					cameraSNLb.Text = daHuaSDK.curCameraKey;
					exposureLb.Text = daHuaSDK.GetExposureTime().ToString();
					gainLb.Text = daHuaSDK.GetGainRaw().ToString();
				}
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"切换相机异常: {ex.Message}");
			}
		}

		private void UnsubscribeAllOnImage()
		{
			if (cam1 != null) cam1.OnImage -= Cam1_OnImage;
			if (cam2 != null) cam2.OnImage -= Cam1_OnImage;
			if (cam3 != null) cam3.OnImage -= Cam1_OnImage;
			if (cam4 != null) cam4.OnImage -= Cam1_OnImage;
			if (cam5 != null) cam5.OnImage -= Cam1_OnImage;
			if (cam6 != null) cam6.OnImage -= Cam1_OnImage;
			if (cam7 != null) cam7.OnImage -= Cam1_OnImage;
			if (cam8 != null) cam8.OnImage -= Cam1_OnImage;
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
				if (daHuaSDK == null)
				{
					MessageBox.Show("未选择相机或相机未初始化");
					return;
				}

				if (type)
				{
					// 单次拍照：使用软件触发
					daHuaSDK.setTriggerSource(0);  // 切换到软触发
					Thread.Sleep(50);
					daHuaSDK.ExecuteSoftwareTrigger();
					Thread.Sleep(50);
					daHuaSDK.setTriggerSource(1);  // 恢复外触发
					toolClass.SaveLog($"单次拍照完成 Camera={daHuaSDK.curCameraKey}");
				}
				else
				{
					// 实时取像：切换到连续采集模式
					TriggerFlag = true;
					daHuaSDK.StopStreamGrabber();
					Thread.Sleep(50);
					daHuaSDK.SetTriggerMode(0);     // 实时模式（非触发）
					daHuaSDK.SetAcquisitionMode(1); // 连续采集
					daHuaSDK.StartStreamGrabber();
					toolClass.SaveLog($"实时取像开始 Camera={daHuaSDK.curCameraKey}");
				}
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
