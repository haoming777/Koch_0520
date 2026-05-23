using Sunny.UI;
using Sunny.UI.Win32;
using System;
using System.Threading.Tasks;
using System.Windows.Forms;
using CommonLib;
using ZmcUniversalLib;
namespace PLC监控   // 【关键】必须使用你原来的项目命名空间
{
	public partial class ControlFrm : UIForm
	{
		private Timer _uiTimer;

		public ControlFrm()
		{
			InitializeComponent();
		}

		private void ControlFrm_Load(object sender, EventArgs e)
		{
			// 加载配置的参数显示在界面上
			var conf = SystemConfig.Instance;
			txtSpeed.Text = conf.AxisSpeed.ToString();
			txtAcc.Text = conf.AxisAccel.ToString();
			txtDec.Text = conf.AxisDecel.ToString();
			txtStartPos.Text = conf.PhotoStartPos.ToString();
			txtEndPos.Text = conf.PhotoEndPos.ToString();

			// 开启定时器，用于高速刷新轴当前位置
			_uiTimer = new Timer { Interval = 50 };
			_uiTimer.Tick += UiTimer_Tick;
			_uiTimer.Start();

			UpdateConnUI();
		}

		private void ControlFrm_Closing(object sender, FormClosingEventArgs e)
		{
			_uiTimer?.Stop();
		}

		// ================== 1. 实时更新UI状态 ==================
		private void UiTimer_Tick(object sender, EventArgs e)
		{
			if (ZmcManager.ZmcCtrl != null && ZmcManager.ZmcCtrl.IsConnected)
			{
				int axis = cbAxis.SelectedIndex >= 0 ? cbAxis.SelectedIndex : 0;
				float pos = ZmcManager.ZmcCtrl.GetPosition(axis);
				lblPos.Text = $"当前位置: {pos:F3}";
			}
		}

		private void UpdateConnUI()
		{
			bool isConn = ZmcManager.ZmcCtrl != null && ZmcManager.ZmcCtrl.IsConnected;
			lblStatus.Text = isConn ? "状态: 已连接" : "状态: 未连接";
			lblStatus.ForeColor = isConn ? System.Drawing.Color.LimeGreen : System.Drawing.Color.Red;
			btnConnect.Enabled = !isConn;
			btnDisconnect.Enabled = isConn;
		}

		// ================== 2. 连接与断开 ==================
		private void btnConnect_Click(object sender, EventArgs e)
		{
			try
			{
				ZmcManager.InitAndConnect(txtIP.Text.Trim(), 2);
				UpdateConnUI();
				UIMessageTip.ShowOk(this, "控制卡连接成功");
			}
			catch (Exception ex)
			{
				UIMessageTip.ShowError(this, $"连接失败: {ex.Message}");
			}
		}

		private void btnDisconnect_Click(object sender, EventArgs e)
		{
			ZmcManager.Disconnect();
			UpdateConnUI();
			UIMessageTip.ShowWarning(this, "已断开连接");
		}

		// ================== 3. 参数与区间保存 ==================
		private void btnSaveParams_Click(object sender, EventArgs e)
		{
			if (float.TryParse(txtSpeed.Text, out float sp) && float.TryParse(txtAcc.Text, out float acc) && float.TryParse(txtDec.Text, out float dec))
			{
				SystemConfig.Instance.AxisSpeed = sp;
				SystemConfig.Instance.AxisAccel = acc;
				SystemConfig.Instance.AxisDecel = dec;
				// 同步下发给控制器
				if (ZmcManager.ZmcCtrl != null && ZmcManager.ZmcCtrl.IsConnected)
				{
					int axis = cbAxis.SelectedIndex >= 0 ? cbAxis.SelectedIndex : 0;
					ZmcManager.ZmcCtrl.SetSpeed(axis, sp, acc, dec);
				}
				UIMessageTip.ShowOk(this, "运动参数已保存");
			}
			else UIMessageTip.ShowError(this, "输入格式错误");
		}

		private void btnSetStart_Click(object sender, EventArgs e)
		{
			if (ZmcManager.ZmcCtrl == null || !ZmcManager.ZmcCtrl.IsConnected) return;
			float pos = ZmcManager.ZmcCtrl.GetPosition(cbAxis.SelectedIndex);
			txtStartPos.Text = pos.ToString("F3");
		}

		private void btnSetEnd_Click(object sender, EventArgs e)
		{
			if (ZmcManager.ZmcCtrl == null || !ZmcManager.ZmcCtrl.IsConnected) return;
			float pos = ZmcManager.ZmcCtrl.GetPosition(cbAxis.SelectedIndex);
			txtEndPos.Text = pos.ToString("F3");
		}

		private void btnSavePhoto_Click(object sender, EventArgs e)
		{
			if (float.TryParse(txtStartPos.Text, out float s) && float.TryParse(txtEndPos.Text, out float d))
			{
				SystemConfig.Instance.PhotoStartPos = s;
				SystemConfig.Instance.PhotoEndPos = d;
				UIMessageTip.ShowOk(this, "拍照区间已保存");
			}
			else UIMessageTip.ShowError(this, "输入格式错误");
		}

		// ================== 4. 运动控制 (绝对定位与JOG) ==================
		private void btnMoveAbs_Click(object sender, EventArgs e)
		{
			if (ZmcManager.ZmcCtrl == null || !ZmcManager.ZmcCtrl.IsConnected) return;
			if (float.TryParse(txtTargetPos.Text, out float target))
			{
				ZmcManager.ZmcCtrl.MoveAbs(cbAxis.SelectedIndex, target);
				Logger.Info($"手动绝对移动 轴{cbAxis.SelectedIndex} -> {target}");
			}
			else UIMessageTip.ShowError(this, "请输入正确的目标位置");
		}

		// 鼠标按下：开始持续点动
		private void btnJogN_MouseDown(object sender, MouseEventArgs e)
		{
			if (ZmcManager.ZmcCtrl != null && ZmcManager.ZmcCtrl.IsConnected)
				ZmcManager.ZmcCtrl.JogMove(cbAxis.SelectedIndex, -1);
		}

		private void btnJogP_MouseDown(object sender, MouseEventArgs e)
		{
			if (ZmcManager.ZmcCtrl != null && ZmcManager.ZmcCtrl.IsConnected)
				ZmcManager.ZmcCtrl.JogMove(cbAxis.SelectedIndex, 1);
		}

		// 鼠标抬起：停止点动
		private void btnJog_MouseUp(object sender, MouseEventArgs e)
		{
			if (ZmcManager.ZmcCtrl != null && ZmcManager.ZmcCtrl.IsConnected)
				ZmcManager.ZmcCtrl.StopAxis(cbAxis.SelectedIndex);
		}

		private void btnStop_Click(object sender, EventArgs e)
		{
			if (ZmcManager.ZmcCtrl != null && ZmcManager.ZmcCtrl.IsConnected)
				ZmcManager.ZmcCtrl.StopAxis(cbAxis.SelectedIndex);
		}

		// ================== 5. IO 强制触发 ==================
		private void btnCam1_Click(object sender, EventArgs e) => TriggerOutAsync(8);
		private void btnCam2_Click(object sender, EventArgs e) => TriggerOutAsync(9);

		private void TriggerOutAsync(int port)
		{
			if (ZmcManager.ZmcCtrl == null || !ZmcManager.ZmcCtrl.IsConnected) return;
			Task.Run(() =>
			{
				ZmcManager.ZmcCtrl.WriteOut(port, 1);
				System.Threading.Thread.Sleep(50);
				ZmcManager.ZmcCtrl.WriteOut(port, 0);
			});
		}
	}
}