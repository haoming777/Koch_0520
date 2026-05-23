namespace PLC监控
{
	partial class ControlFrm
	{
		private System.ComponentModel.IContainer components = null;

		protected override void Dispose(bool disposing)
		{
			if (disposing && (components != null)) components.Dispose();
			base.Dispose(disposing);
		}

		private void InitializeComponent()
		{
			this.gbConn = new Sunny.UI.UIPanel();
			this.lblStatus = new Sunny.UI.UILabel();
			this.btnDisconnect = new Sunny.UI.UIButton();
			this.btnConnect = new Sunny.UI.UIButton();
			this.txtIP = new Sunny.UI.UITextBox();

			this.gbMotion = new Sunny.UI.UIPanel();
			this.cbAxis = new Sunny.UI.UIComboBox();
			this.lblPos = new Sunny.UI.UILabel();
			this.txtTargetPos = new Sunny.UI.UITextBox();
			this.btnMoveAbs = new Sunny.UI.UIButton();
			this.btnJogP = new Sunny.UI.UIButton();
			this.btnJogN = new Sunny.UI.UIButton();
			this.btnStop = new Sunny.UI.UIButton();

			this.gbParams = new Sunny.UI.UIPanel();
			this.txtSpeed = new Sunny.UI.UITextBox();
			this.txtAcc = new Sunny.UI.UITextBox();
			this.txtDec = new Sunny.UI.UITextBox();
			this.btnSaveParams = new Sunny.UI.UIButton();

			this.gbPhoto = new Sunny.UI.UIPanel();
			this.txtStartPos = new Sunny.UI.UITextBox();
			this.txtEndPos = new Sunny.UI.UITextBox();
			this.btnSetStart = new Sunny.UI.UIButton();
			this.btnSetEnd = new Sunny.UI.UIButton();
			this.btnSavePhoto = new Sunny.UI.UIButton();

			this.gbIO = new Sunny.UI.UIPanel();
			this.btnCam1 = new Sunny.UI.UIButton();
			this.btnCam2 = new Sunny.UI.UIButton();

			this.SuspendLayout();

			// 扁平化面板通用设置
			System.Drawing.Color panelColor = System.Drawing.Color.FromArgb(243, 249, 255);

			// ================= 1. 连接面板 =================
			this.gbConn.FillColor = panelColor;
			this.gbConn.RectColor = System.Drawing.Color.FromArgb(216, 229, 248);
			this.gbConn.Size = new System.Drawing.Size(900, 70);
			this.gbConn.Location = new System.Drawing.Point(20, 50);
			this.gbConn.Text = "";

			this.txtIP.Text = "192.168.0.11";
			this.txtIP.Location = new System.Drawing.Point(20, 18);
			this.txtIP.Size = new System.Drawing.Size(150, 35);

			this.btnConnect.Text = "连接设备";
			this.btnConnect.Location = new System.Drawing.Point(190, 18);
			this.btnConnect.Size = new System.Drawing.Size(100, 35);
			this.btnConnect.Click += new System.EventHandler(this.btnConnect_Click);

			this.btnDisconnect.Text = "断开";
			this.btnDisconnect.Style = Sunny.UI.UIStyle.Red;
			this.btnDisconnect.Location = new System.Drawing.Point(300, 18);
			this.btnDisconnect.Size = new System.Drawing.Size(100, 35);
			this.btnDisconnect.Click += new System.EventHandler(this.btnDisconnect_Click);

			this.lblStatus.Text = "状态: 未连接";
			this.lblStatus.ForeColor = System.Drawing.Color.Red;
			this.lblStatus.Font = new System.Drawing.Font("微软雅黑", 12F, System.Drawing.FontStyle.Bold);
			this.lblStatus.Location = new System.Drawing.Point(420, 18);
			this.lblStatus.Size = new System.Drawing.Size(300, 35);

			this.gbConn.Controls.Add(txtIP);
			this.gbConn.Controls.Add(btnConnect);
			this.gbConn.Controls.Add(btnDisconnect);
			this.gbConn.Controls.Add(lblStatus);

			// ================= 2. 参数设置面板 =================
			this.gbParams.FillColor = panelColor;
			this.gbParams.RectColor = System.Drawing.Color.FromArgb(216, 229, 248);
			this.gbParams.Size = new System.Drawing.Size(435, 120);
			this.gbParams.Location = new System.Drawing.Point(20, 140);
			this.gbParams.Text = "";

			this.txtSpeed.Watermark = "运行速度";
			this.txtSpeed.Location = new System.Drawing.Point(20, 20);
			this.txtSpeed.Size = new System.Drawing.Size(120, 35);

			this.txtAcc.Watermark = "加速度";
			this.txtAcc.Location = new System.Drawing.Point(150, 20);
			this.txtAcc.Size = new System.Drawing.Size(120, 35);

			this.txtDec.Watermark = "减速度";
			this.txtDec.Location = new System.Drawing.Point(280, 20);
			this.txtDec.Size = new System.Drawing.Size(120, 35);

			this.btnSaveParams.Text = "保存运动参数";
			this.btnSaveParams.Location = new System.Drawing.Point(20, 70);
			this.btnSaveParams.Size = new System.Drawing.Size(380, 35);
			this.btnSaveParams.Click += new System.EventHandler(this.btnSaveParams_Click);

			this.gbParams.Controls.Add(txtSpeed);
			this.gbParams.Controls.Add(txtAcc);
			this.gbParams.Controls.Add(txtDec);
			this.gbParams.Controls.Add(btnSaveParams);

			// ================= 3. 拍照位置面板 =================
			this.gbPhoto.FillColor = panelColor;
			this.gbPhoto.RectColor = System.Drawing.Color.FromArgb(216, 229, 248);
			this.gbPhoto.Size = new System.Drawing.Size(445, 120);
			this.gbPhoto.Location = new System.Drawing.Point(475, 140);
			this.gbPhoto.Text = "";

			this.txtStartPos.Watermark = "拍照起点";
			this.txtStartPos.Location = new System.Drawing.Point(20, 20);
			this.txtStartPos.Size = new System.Drawing.Size(120, 35);

			this.btnSetStart.Text = "获取当前";
			this.btnSetStart.Location = new System.Drawing.Point(150, 20);
			this.btnSetStart.Size = new System.Drawing.Size(70, 35);
			this.btnSetStart.Click += new System.EventHandler(this.btnSetStart_Click);

			this.txtEndPos.Watermark = "拍照终点";
			this.txtEndPos.Location = new System.Drawing.Point(230, 20);
			this.txtEndPos.Size = new System.Drawing.Size(120, 35);

			this.btnSetEnd.Text = "获取当前";
			this.btnSetEnd.Location = new System.Drawing.Point(360, 20);
			this.btnSetEnd.Size = new System.Drawing.Size(70, 35);
			this.btnSetEnd.Click += new System.EventHandler(this.btnSetEnd_Click);

			this.btnSavePhoto.Text = "保存拍照区间配置";
			this.btnSavePhoto.Location = new System.Drawing.Point(20, 70);
			this.btnSavePhoto.Size = new System.Drawing.Size(410, 35);
			this.btnSavePhoto.Click += new System.EventHandler(this.btnSavePhoto_Click);

			this.gbPhoto.Controls.Add(txtStartPos);
			this.gbPhoto.Controls.Add(btnSetStart);
			this.gbPhoto.Controls.Add(txtEndPos);
			this.gbPhoto.Controls.Add(btnSetEnd);
			this.gbPhoto.Controls.Add(btnSavePhoto);

			// ================= 4. 运动控制面板 =================
			this.gbMotion.FillColor = panelColor;
			this.gbMotion.RectColor = System.Drawing.Color.FromArgb(216, 229, 248);
			this.gbMotion.Size = new System.Drawing.Size(900, 100);
			this.gbMotion.Location = new System.Drawing.Point(20, 280);
			this.gbMotion.Text = "";

			this.cbAxis.Items.AddRange(new object[] { "轴 0", "轴 1", "轴 2", "轴 3" });
			this.cbAxis.SelectedIndex = 0;
			this.cbAxis.Location = new System.Drawing.Point(20, 32);
			this.cbAxis.Size = new System.Drawing.Size(100, 35);

			this.lblPos.Text = "当前位置: 0.000";
			this.lblPos.Font = new System.Drawing.Font("微软雅黑", 14F, System.Drawing.FontStyle.Bold);
			this.lblPos.Location = new System.Drawing.Point(130, 32);
			this.lblPos.Size = new System.Drawing.Size(200, 35);

			this.txtTargetPos.Watermark = "目标位置";
			this.txtTargetPos.Location = new System.Drawing.Point(340, 32);
			this.txtTargetPos.Size = new System.Drawing.Size(100, 35);

			this.btnMoveAbs.Text = "绝对移动";
			this.btnMoveAbs.Style = Sunny.UI.UIStyle.Green;
			this.btnMoveAbs.Location = new System.Drawing.Point(450, 32);
			this.btnMoveAbs.Size = new System.Drawing.Size(100, 35);
			this.btnMoveAbs.Click += new System.EventHandler(this.btnMoveAbs_Click);

			this.btnJogN.Text = "<< JOG-";
			this.btnJogN.Location = new System.Drawing.Point(570, 32);
			this.btnJogN.Size = new System.Drawing.Size(90, 35);
			this.btnJogN.MouseDown += new System.Windows.Forms.MouseEventHandler(this.btnJogN_MouseDown);
			this.btnJogN.MouseUp += new System.Windows.Forms.MouseEventHandler(this.btnJog_MouseUp);

			this.btnJogP.Text = "JOG+ >>";
			this.btnJogP.Location = new System.Drawing.Point(670, 32);
			this.btnJogP.Size = new System.Drawing.Size(90, 35);
			this.btnJogP.MouseDown += new System.Windows.Forms.MouseEventHandler(this.btnJogP_MouseDown);
			this.btnJogP.MouseUp += new System.Windows.Forms.MouseEventHandler(this.btnJog_MouseUp);

			this.btnStop.Text = "急 停";
			this.btnStop.Style = Sunny.UI.UIStyle.Red;
			this.btnStop.Location = new System.Drawing.Point(780, 32);
			this.btnStop.Size = new System.Drawing.Size(100, 35);
			this.btnStop.Click += new System.EventHandler(this.btnStop_Click);

			this.gbMotion.Controls.Add(cbAxis);
			this.gbMotion.Controls.Add(lblPos);
			this.gbMotion.Controls.Add(txtTargetPos);
			this.gbMotion.Controls.Add(btnMoveAbs);
			this.gbMotion.Controls.Add(btnJogN);
			this.gbMotion.Controls.Add(btnJogP);
			this.gbMotion.Controls.Add(btnStop);

			// ================= 5. IO 触发面板 =================
			this.gbIO.FillColor = panelColor;
			this.gbIO.RectColor = System.Drawing.Color.FromArgb(216, 229, 248);
			this.gbIO.Size = new System.Drawing.Size(900, 80);
			this.gbIO.Location = new System.Drawing.Point(20, 400);
			this.gbIO.Text = "";

			this.btnCam1.Text = "手动触发 左相机(OUT8)";
			this.btnCam1.Location = new System.Drawing.Point(20, 22);
			this.btnCam1.Size = new System.Drawing.Size(220, 35);
			this.btnCam1.Click += new System.EventHandler(this.btnCam1_Click);

			this.btnCam2.Text = "手动触发 右相机(OUT9)";
			this.btnCam2.Location = new System.Drawing.Point(260, 22);
			this.btnCam2.Size = new System.Drawing.Size(220, 35);
			this.btnCam2.Click += new System.EventHandler(this.btnCam2_Click);

			this.gbIO.Controls.Add(btnCam1);
			this.gbIO.Controls.Add(btnCam2);

			// ================= 窗体设置 =================
			this.ClientSize = new System.Drawing.Size(940, 500);
			this.Controls.Add(this.gbConn);
			this.Controls.Add(this.gbParams);
			this.Controls.Add(this.gbPhoto);
			this.Controls.Add(this.gbMotion);
			this.Controls.Add(this.gbIO);
			this.Name = "ControlFrm";
			this.Text = "运动控制与调试面板";
			this.Load += new System.EventHandler(this.ControlFrm_Load);
			this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.ControlFrm_Closing);
			this.ResumeLayout(false);
		}

		private Sunny.UI.UIPanel gbConn;
		private Sunny.UI.UITextBox txtIP;
		private Sunny.UI.UIButton btnConnect;
		private Sunny.UI.UIButton btnDisconnect;
		private Sunny.UI.UILabel lblStatus;

		private Sunny.UI.UIPanel gbParams;
		private Sunny.UI.UITextBox txtSpeed;
		private Sunny.UI.UITextBox txtAcc;
		private Sunny.UI.UITextBox txtDec;
		private Sunny.UI.UIButton btnSaveParams;

		private Sunny.UI.UIPanel gbPhoto;
		private Sunny.UI.UITextBox txtStartPos;
		private Sunny.UI.UITextBox txtEndPos;
		private Sunny.UI.UIButton btnSetStart;
		private Sunny.UI.UIButton btnSetEnd;
		private Sunny.UI.UIButton btnSavePhoto;

		private Sunny.UI.UIPanel gbMotion;
		private Sunny.UI.UIComboBox cbAxis;
		private Sunny.UI.UILabel lblPos;
		private Sunny.UI.UITextBox txtTargetPos;
		private Sunny.UI.UIButton btnMoveAbs;
		private Sunny.UI.UIButton btnJogP;
		private Sunny.UI.UIButton btnJogN;
		private Sunny.UI.UIButton btnStop;

		private Sunny.UI.UIPanel gbIO;
		private Sunny.UI.UIButton btnCam1;
		private Sunny.UI.UIButton btnCam2;
	}
}