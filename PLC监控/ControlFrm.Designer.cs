namespace PLC监控
{
	partial class ControlFrm
	{
		private System.ComponentModel.IContainer components = null;
		protected override void Dispose(bool disposing) { if (disposing && (components != null)) components.Dispose(); base.Dispose(disposing); }

		private void InitializeComponent()
		{
			var pc = System.Drawing.Color.FromArgb(245, 250, 255);
			var rc = System.Drawing.Color.FromArgb(170, 200, 230);
			int R = 10;
			var f9 = new System.Drawing.Font("微软雅黑", 9F);
			var f10 = new System.Drawing.Font("微软雅黑", 10F);
			var f14 = new System.Drawing.Font("微软雅黑", 14F, System.Drawing.FontStyle.Bold);
			System.Drawing.Color T = System.Drawing.Color.Transparent;

			System.Drawing.Font HdrFont = new System.Drawing.Font("微软雅黑", 10F, System.Drawing.FontStyle.Bold);
			System.Drawing.Color HdrBg = System.Drawing.Color.FromArgb(230, 240, 255);
			const int HdrH = 28, PAD = 15, CH = 26, BH = 32, LH = 20; // header height, padding, control height, button height, label height

			// ====== 连接面板 ======
			this.gbConn = new Sunny.UI.UIPanel() { FillColor = pc, RectColor = rc, Radius = R, Size = new System.Drawing.Size(900, 82), Location = new System.Drawing.Point(20, 18) };
			var hdrConn = new Sunny.UI.UILabel() { Text = "连接设置", Font = HdrFont, BackColor = HdrBg, Location = new System.Drawing.Point(0, 0), Size = new System.Drawing.Size(900, HdrH), TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
			this.txtIP = new Sunny.UI.UITextBox() { Text = "192.168.0.11", Location = new System.Drawing.Point(PAD, 36), Size = new System.Drawing.Size(160, 30), Radius = R };
			this.btnConnect = new Sunny.UI.UIButton() { Text = "连接", Location = new System.Drawing.Point(190, 36), Size = new System.Drawing.Size(85, 30), Radius = R };
			this.btnDisconnect = new Sunny.UI.UIButton() { Text = "断开", Style = Sunny.UI.UIStyle.Red, Location = new System.Drawing.Point(285, 36), Size = new System.Drawing.Size(85, 30), Radius = R };
			this.lblStatus = new Sunny.UI.UILabel() { Text = "● 未连接", ForeColor = System.Drawing.Color.Red, BackColor = T, Font = new System.Drawing.Font("微软雅黑", 11F, System.Drawing.FontStyle.Bold), Location = new System.Drawing.Point(390, 39), Size = new System.Drawing.Size(490, 24) };
			this.btnConnect.Click += new System.EventHandler(this.btnConnect_Click);
			this.btnDisconnect.Click += new System.EventHandler(this.btnDisconnect_Click);
			this.gbConn.Controls.Add(hdrConn); this.gbConn.Controls.Add(txtIP); this.gbConn.Controls.Add(btnConnect); this.gbConn.Controls.Add(btnDisconnect); this.gbConn.Controls.Add(lblStatus);

			// ====== 轴状态面板 ======
			this.gbStatus = new Sunny.UI.UIPanel() { FillColor = pc, RectColor = rc, Radius = R, Size = new System.Drawing.Size(435, 210), Location = new System.Drawing.Point(20, 115) };
			this.gbStatus.Controls.Add(Hdr("轴状态", 435));
			int Y = 35;
			this.lblDpos = Label("DPOS: --", PAD, Y, 395, 26, f14);
			Y += 28; this.lblMpos = Label("MPOS: --", PAD, Y, 195, LH, f9);
			this.lblCurSpeed = Label("速度: --", 220, Y, 195, LH, f9);
			Y += 22; this.lblAxisStatus = Label("轴状态: --", PAD, Y, 195, LH, f9);
			this.lblIdle = Label("运动: --", 220, Y, 195, LH, f9);
			Y += 22; this.lblFe = Label("跟随误差: --", PAD, Y, 395, LH, f9);
			int bY = 158;
			this.btnServoOn = Button("伺服ON", Sunny.UI.UIStyle.Green, PAD, bY, 95, BH);
			this.btnServoOff = Button("伺服OFF", Sunny.UI.UIStyle.Red, 125, bY, 95, BH);
			this.btnAlarmClear = Button("报警清除", Sunny.UI.UIStyle.Orange, 232, bY, 95, BH);
			this.btnServoOn.Click += new System.EventHandler(this.btnServoOn_Click);
			this.btnServoOff.Click += new System.EventHandler(this.btnServoOff_Click);
			this.btnAlarmClear.Click += new System.EventHandler(this.btnAlarmClear_Click);
			this.gbStatus.Controls.Add(lblDpos); this.gbStatus.Controls.Add(lblMpos); this.gbStatus.Controls.Add(lblCurSpeed);
			this.gbStatus.Controls.Add(lblAxisStatus); this.gbStatus.Controls.Add(lblIdle); this.gbStatus.Controls.Add(lblFe);
			this.lblInitState = Label("初始化: 检测中...", PAD, 130, 395, 22, f10); this.lblInitState.ForeColor = System.Drawing.Color.Gray;
			this.gbStatus.Controls.Add(btnServoOn); this.gbStatus.Controls.Add(btnServoOff); this.gbStatus.Controls.Add(btnAlarmClear);
			this.gbStatus.Controls.Add(lblInitState);

			// ====== 回零面板 ======
			this.gbHome = new Sunny.UI.UIPanel() { FillColor = pc, RectColor = rc, Radius = R, Size = new System.Drawing.Size(445, 210), Location = new System.Drawing.Point(475, 115) };
			this.gbHome.Controls.Add(Hdr("回零", 445));
			Y = 35;
			this.cbHomeMode = Combo(PAD, Y, 110); this.lblHomeMode = Label("模式", PAD + 115, Y + 3, 35, LH, f9);
			this.txtHomeSpeed = TextBox(195, Y, 58); this.lblHomeSpeed = Label("高速", 256, Y + 3, 30, LH, f9);
			this.txtCreep = TextBox(300, Y, 58); this.lblCreepLabel = Label("爬行", 361, Y + 3, 30, LH, f9);
			this.txtHomeOffset = TextBox(395, Y, 38); this.lblHomeOffset = Label("偏移", 395 + 42, Y + 3, 30, LH, f9);
			for (int i = 0; i <= 14; i++) this.cbHomeMode.Items.Add(i + "号模式");
			this.cbHomeMode.SelectedIndex = 0;
			int hY2 = 158;
			this.btnHome = Button("开始回零", Sunny.UI.UIStyle.Green, PAD, hY2, 140, BH);
			this.btnHomeStop = Button("停止回零", Sunny.UI.UIStyle.Red, 172, hY2, 140, BH);
			this.btnHome.Click += new System.EventHandler(this.btnHome_Click);
			this.btnHomeStop.Click += new System.EventHandler(this.btnHomeStop_Click);
			this.gbHome.Controls.Add(lblHomeMode); this.gbHome.Controls.Add(cbHomeMode); this.gbHome.Controls.Add(lblHomeSpeed);
			this.gbHome.Controls.Add(txtHomeSpeed); this.gbHome.Controls.Add(lblCreepLabel); this.gbHome.Controls.Add(txtCreep);
			this.gbHome.Controls.Add(lblHomeOffset); this.gbHome.Controls.Add(txtHomeOffset);
			this.gbHome.Controls.Add(btnHome); this.gbHome.Controls.Add(btnHomeStop);
			this.cbHomeMode.Visible = false; this.lblHomeMode.Visible = false;
			this.txtHomeSpeed.Visible = false; this.lblHomeSpeed.Visible = false;
			this.txtCreep.Visible = false; this.lblCreepLabel.Visible = false;
			this.txtHomeOffset.Visible = false; this.lblHomeOffset.Visible = false;

			// ====== 轴参数面板 ======
			this.gbParams = new Sunny.UI.UIPanel() { FillColor = pc, RectColor = rc, Radius = R, Size = new System.Drawing.Size(435, 218), Location = new System.Drawing.Point(20, 340) };
			this.gbParams.Controls.Add(Hdr("轴参数", 435));
			Y = 35;
			this.cbAxis = Combo(PAD, Y, 80); this.lblAxis = Label("轴号", PAD + 85, Y + 3, 30, LH, f9);
			this.txtSpeed = TextBox(140, Y, 100); this.lblSpeed = Label("速度", 243, Y + 3, 30, LH, f9);
			this.txtAcc = TextBox(290, Y, 100); this.lblAccel = Label("加速度", 395, Y + 3, 42, LH, f9);
			Y += 32;
			this.txtDec = TextBox(PAD, Y, 100); this.lblDecel = Label("减速度", PAD + 105, Y + 3, 42, LH, f9);
			this.txtLspeed = TextBox(170, Y, 80); this.lblLspeed = Label("起始速度", 253, Y + 3, 54, LH, f9);
			this.txtSramp = TextBox(325, Y, 90); this.lblSramp = Label("S曲线", 418, Y + 3, 36, LH, f9);
			this.txtMaxSpeed = TextBox(PAD, Y + 32, 80); this.lblMaxSpeed = Label("最高速度", PAD + 85, Y + 35, 55, LH, f9);
			var cby = 165;
			this.btnSaveParams = Button("保存到本地", Sunny.UI.UIStyle.Gray, PAD, cby, 185, BH);
			this.btnApplyAll = Button("下发到控制卡", Sunny.UI.UIStyle.Green, 220, cby, 195, BH);
			this.btnSaveParams.Click += new System.EventHandler(this.btnSaveParams_Click);
			this.btnApplyAll.Click += new System.EventHandler(this.btnApplyAll_Click);
			this.cbAxis.Items.AddRange(new object[] { "轴 0", "轴 1", "轴 2", "轴 3" }); this.cbAxis.SelectedIndex = 0;
			this.gbParams.Controls.Add(lblAxis); this.gbParams.Controls.Add(cbAxis); this.gbParams.Controls.Add(lblSpeed);
			this.gbParams.Controls.Add(txtSpeed); this.gbParams.Controls.Add(lblAccel); this.gbParams.Controls.Add(txtAcc);
			this.gbParams.Controls.Add(lblDecel); this.gbParams.Controls.Add(txtDec); this.gbParams.Controls.Add(lblLspeed);
			this.gbParams.Controls.Add(txtLspeed); this.gbParams.Controls.Add(lblSramp); this.gbParams.Controls.Add(txtSramp);
			this.gbParams.Controls.Add(lblMaxSpeed); this.gbParams.Controls.Add(txtMaxSpeed);
			this.gbParams.Controls.Add(btnSaveParams); this.gbParams.Controls.Add(btnApplyAll);

			// ====== 拍照区间面板 ======
			this.gbPhoto = new Sunny.UI.UIPanel() { FillColor = pc, RectColor = rc, Radius = R, Size = new System.Drawing.Size(445, 218), Location = new System.Drawing.Point(475, 340) };
			this.gbPhoto.Controls.Add(Hdr("拍照区间", 445));
			Y = 35;
			this.txtStartPos = TextBox(PAD, Y, 100); this.lblStart = Label("起点", PAD + 105, Y + 3, 30, LH, f9);
			this.btnSetStart = Button("当前", Sunny.UI.UIStyle.Gray, 153, Y, 55, CH);
			this.txtEndPos = TextBox(220, Y, 100); this.lblEnd = Label("终点", 324, Y + 3, 30, LH, f9);
			this.btnSetEnd = Button("当前", Sunny.UI.UIStyle.Gray, 364, Y, 55, CH);
			this.lblPhotoHint = Label("保存后侧工位拍照使用此区间参数", PAD, 115, 410, LH, f9);
			this.lblPhotoHint.ForeColor = System.Drawing.Color.Gray;
			this.btnSavePhoto = Button("保存拍照区间配置", Sunny.UI.UIStyle.Gray, PAD, 165, 410, BH);
			this.btnSetStart.Click += new System.EventHandler(this.btnSetStart_Click);
			this.btnSetEnd.Click += new System.EventHandler(this.btnSetEnd_Click);
			this.btnSavePhoto.Click += new System.EventHandler(this.btnSavePhoto_Click);
			this.gbPhoto.Controls.Add(lblStart); this.gbPhoto.Controls.Add(txtStartPos); this.gbPhoto.Controls.Add(btnSetStart);
			this.gbPhoto.Controls.Add(lblEnd); this.gbPhoto.Controls.Add(txtEndPos); this.gbPhoto.Controls.Add(btnSetEnd);
			this.gbPhoto.Controls.Add(lblPhotoHint); this.gbPhoto.Controls.Add(btnSavePhoto);

			// ====== 运动控制面板 ======
			this.gbMotion = new Sunny.UI.UIPanel() { FillColor = pc, RectColor = rc, Radius = R, Size = new System.Drawing.Size(900, 82), Location = new System.Drawing.Point(20, 573) };
			this.gbMotion.Controls.Add(Hdr("运动控制", 900));
			int mY = 36;
			this.lblTarget = Label("目标位置", PAD, mY + 3, 55, LH, f9);
			this.txtTargetPos = TextBox(70, mY, 80);
			this.btnMoveAbs = Button("绝对移动", Sunny.UI.UIStyle.Green, 162, mY, 95, CH);
			this.btnMoveRel = Button("相对移动", Sunny.UI.UIStyle.Blue, 268, mY, 95, CH);
			this.btnJogN = Button("<< JOG-", Sunny.UI.UIStyle.Gray, 395, mY, 75, CH);
			this.btnJogP = Button("JOG+ >>", Sunny.UI.UIStyle.Gray, 480, mY, 75, CH);
			this.btnJogFastN = Button("<< FAST-", Sunny.UI.UIStyle.Orange, 565, mY, 82, CH);
			this.btnJogFastP = Button("FAST+ >>", Sunny.UI.UIStyle.Orange, 657, mY, 82, CH);
			this.btnStop = Button("急 停", Sunny.UI.UIStyle.Red, 765, mY, 80, CH);
			this.btnMoveAbs.Click += new System.EventHandler(this.btnMoveAbs_Click);
			this.btnMoveRel.Click += new System.EventHandler(this.btnMoveRel_Click);
			this.btnJogN.MouseDown += new System.Windows.Forms.MouseEventHandler(this.btnJogN_MouseDown);
			this.btnJogP.MouseDown += new System.Windows.Forms.MouseEventHandler(this.btnJogP_MouseDown);
			this.btnJogFastN.MouseDown += new System.Windows.Forms.MouseEventHandler(this.btnJogFastN_MouseDown);
			this.btnJogFastP.MouseDown += new System.Windows.Forms.MouseEventHandler(this.btnJogFastP_MouseDown);
			this.btnJogN.MouseUp += new System.Windows.Forms.MouseEventHandler(this.btnJog_MouseUp);
			this.btnJogP.MouseUp += new System.Windows.Forms.MouseEventHandler(this.btnJog_MouseUp);
			this.btnJogFastN.MouseUp += new System.Windows.Forms.MouseEventHandler(this.btnJog_MouseUp);
			this.btnJogFastP.MouseUp += new System.Windows.Forms.MouseEventHandler(this.btnJog_MouseUp);
			this.btnStop.Click += new System.EventHandler(this.btnStop_Click);
			this.gbMotion.Controls.Add(lblTarget); this.gbMotion.Controls.Add(txtTargetPos);
			this.gbMotion.Controls.Add(btnMoveAbs); this.gbMotion.Controls.Add(btnMoveRel);
			this.gbMotion.Controls.Add(btnJogN); this.gbMotion.Controls.Add(btnJogP); this.gbMotion.Controls.Add(btnJogFastN); this.gbMotion.Controls.Add(btnJogFastP); this.gbMotion.Controls.Add(btnStop);

			// ====== 模拟运行面板 ======
			this.gbSim = new Sunny.UI.UIPanel() { FillColor = pc, RectColor = rc, Radius = R, Size = new System.Drawing.Size(900, 105), Location = new System.Drawing.Point(20, 670) };
			this.gbSim.Controls.Add(Hdr("模拟运行", 900));
			int s1 = 36, s2 = 66;
			this.lblSimStatus = Label("就绪 — 点击开始模拟", PAD, s1 + 2, 350, LH, f9);
			this.lblSimCount = Label("拍照: 0", 370, s1 + 2, 130, LH, f9);
			this.lblLastTrig = Label("最近触发: --", 505, s1 + 2, 380, LH, f9); this.lblLastTrig.ForeColor = System.Drawing.Color.DarkGreen;
			this.btnSimRun = Button("开始模拟", Sunny.UI.UIStyle.Green, PAD, s2, 105, CH);
			this.btnSimStop = Button("停止", Sunny.UI.UIStyle.Red, 135, s2, 60, CH);
			this.chkSimLoop = new Sunny.UI.UICheckBox() { Text = "循环", Location = new System.Drawing.Point(205, s2), Size = new System.Drawing.Size(55, 26) };
			this.lblMaxPhoto = Label("最多拍", 530, s2 + 3, 48, LH, f9);
			this.txtMaxPhoto = TextBox(578, s2, 40); this.txtMaxPhoto.Text = "12";
			this.lblCycleDelay = Label("间隔ms", 625, s2 + 3, 50, LH, f9);
			this.txtCycleDelay = TextBox(675, s2, 55); this.txtCycleDelay.Text = "500";
			this.lblFwdSpeed = Label("前进速度", 265, s2 + 3, 55, LH, f9);
			this.txtFwdSpeed = TextBox(322, s2, 68);
			this.lblRetSpeed = Label("返回速度", 398, s2 + 3, 55, LH, f9);
			this.txtRetSpeed = TextBox(455, s2, 68);
			this.btnSimRun.Click += new System.EventHandler(this.btnSimRun_Click);
			this.btnSimStop.Click += new System.EventHandler(this.btnSimStop_Click);
			this.gbSim.Controls.Add(lblSimStatus); this.gbSim.Controls.Add(lblSimCount); this.gbSim.Controls.Add(lblLastTrig);
			this.gbSim.Controls.Add(btnSimRun); this.gbSim.Controls.Add(btnSimStop); this.gbSim.Controls.Add(chkSimLoop);
			this.gbSim.Controls.Add(lblMaxPhoto); this.gbSim.Controls.Add(txtMaxPhoto);
			this.gbSim.Controls.Add(lblCycleDelay); this.gbSim.Controls.Add(txtCycleDelay);
			this.gbSim.Controls.Add(lblFwdSpeed); this.gbSim.Controls.Add(txtFwdSpeed); this.gbSim.Controls.Add(lblRetSpeed); this.gbSim.Controls.Add(txtRetSpeed);

			// ====== IO 触发面板 ======
			this.gbIO = new Sunny.UI.UIPanel() { FillColor = pc, RectColor = rc, Radius = R, Size = new System.Drawing.Size(900, 100), Location = new System.Drawing.Point(20, 790) };
			this.gbIO.Controls.Add(Hdr("IO 触发", 900));
			int ioY = 36;
			this.btnCam1 = Button("触发左相机(OUT14→Cam7)", Sunny.UI.UIStyle.Gray, PAD, ioY, 240, 28);
			this.btnCam2 = Button("触发右相机(OUT15→Cam8)", Sunny.UI.UIStyle.Gray, 270, ioY, 240, 28);
			this.chkIn12Manual = new Sunny.UI.UICheckBox() { Text = "IN12边沿→OUT14/15", Location = new System.Drawing.Point(525, ioY), Size = new System.Drawing.Size(185, 28) };
			this.btnIn12Manual = new Sunny.UI.UIButton() { Text = "IN12状态", FillColor = System.Drawing.Color.Gray, Location = new System.Drawing.Point(710, ioY), Size = new System.Drawing.Size(95, 28), Radius = R };
			int ioY2 = 70;
			this.lblIn12 = Label("IN12: --", PAD, ioY2, 200, LH, f9);
			this.lblIn13 = Label("IN13: --", 240, ioY2, 200, LH, f9);
			this.lblCamHint = Label("OUT14→相机7/光源7  |  OUT15→相机8/光源8", 460, ioY2, 425, LH, f9);
			this.lblCamHint.ForeColor = System.Drawing.Color.Gray;
			this.btnCam1.Click += new System.EventHandler(this.btnCam1_Click);
			this.btnCam2.Click += new System.EventHandler(this.btnCam2_Click);
			this.chkIn12Manual.CheckedChanged += new System.EventHandler(this.chkIn12Manual_CheckedChanged);
			this.gbIO.Controls.Add(btnCam1); this.gbIO.Controls.Add(btnCam2);
			this.gbIO.Controls.Add(chkIn12Manual); this.gbIO.Controls.Add(btnIn12Manual);
			this.gbIO.Controls.Add(lblIn12); this.gbIO.Controls.Add(lblIn13); this.gbIO.Controls.Add(lblCamHint);

			// ====== 窗体 ======
			this.ClientSize = new System.Drawing.Size(940, 908);
			this.Controls.Add(gbConn); this.Controls.Add(gbStatus); this.Controls.Add(gbHome);
			this.Controls.Add(gbParams); this.Controls.Add(gbPhoto);
			this.Controls.Add(gbMotion); this.Controls.Add(gbSim); this.Controls.Add(gbIO);
			this.Name = "ControlFrm";
			this.Text = "运动控制与调试面板";
			this.Load += new System.EventHandler(this.ControlFrm_Load);
			this.FormClosing += new System.Windows.Forms.FormClosingEventHandler(this.ControlFrm_Closing);
			this.ResumeLayout(false);
		}

		// ---- 辅助：制作面板标题栏 ----
		Sunny.UI.UILabel Hdr(string text, int w)
		{
			return new Sunny.UI.UILabel() { Text = "  " + text, Font = new System.Drawing.Font("微软雅黑", 10F, System.Drawing.FontStyle.Bold),
				BackColor = System.Drawing.Color.FromArgb(220, 235, 252),
				Size = new System.Drawing.Size(w, 28), Location = new System.Drawing.Point(0, 0), TextAlign = System.Drawing.ContentAlignment.MiddleLeft };
		}
		Sunny.UI.UILabel Label(string t, int x, int y, int w, int h, System.Drawing.Font f) { return new Sunny.UI.UILabel() { Text = t, BackColor = System.Drawing.Color.Transparent, Font = f, Location = new System.Drawing.Point(x, y), Size = new System.Drawing.Size(w, h) }; }
		Sunny.UI.UIButton Button(string t, Sunny.UI.UIStyle s, int x, int y, int w, int h) { return new Sunny.UI.UIButton() { Text = t, Style = s, Location = new System.Drawing.Point(x, y), Size = new System.Drawing.Size(w, h), Radius = 10 }; }
		Sunny.UI.UITextBox TextBox(int x, int y, int w) { return new Sunny.UI.UITextBox() { Location = new System.Drawing.Point(x, y), Size = new System.Drawing.Size(w, 26), Radius = 10 }; }
		Sunny.UI.UIComboBox Combo(int x, int y, int w) { return new Sunny.UI.UIComboBox() { Location = new System.Drawing.Point(x, y), Size = new System.Drawing.Size(w, 26), Radius = 10 }; }

		private Sunny.UI.UIPanel gbConn, gbStatus, gbHome, gbParams, gbPhoto, gbMotion, gbSim, gbIO;
		private Sunny.UI.UILabel lblStatus, lblDpos, lblMpos, lblCurSpeed, lblAxisStatus, lblIdle, lblFe, lblInitState;
		private Sunny.UI.UIButton btnConnect, btnDisconnect, btnServoOn, btnServoOff, btnAlarmClear;
		private Sunny.UI.UITextBox txtIP;
		private Sunny.UI.UILabel lblHomeMode, lblHomeSpeed, lblCreepLabel, lblHomeOffset;
		private Sunny.UI.UIComboBox cbHomeMode;
		private Sunny.UI.UITextBox txtHomeSpeed, txtCreep, txtHomeOffset;
		private Sunny.UI.UIButton btnHome, btnHomeStop;
		private Sunny.UI.UILabel lblAxis, lblSpeed, lblAccel, lblDecel, lblLspeed, lblSramp, lblMaxSpeed;
		private Sunny.UI.UIComboBox cbAxis;
		private Sunny.UI.UITextBox txtSpeed, txtAcc, txtDec, txtLspeed, txtSramp, txtMaxSpeed;
		private Sunny.UI.UIButton btnSaveParams, btnApplyAll;
		private Sunny.UI.UILabel lblStart, lblEnd, lblPhotoHint;
		private Sunny.UI.UITextBox txtStartPos, txtEndPos;
		private Sunny.UI.UIButton btnSetStart, btnSetEnd, btnSavePhoto;
		private Sunny.UI.UILabel lblTarget;
		private Sunny.UI.UITextBox txtTargetPos;
		private Sunny.UI.UIButton btnMoveAbs, btnMoveRel, btnJogN, btnJogP, btnJogFastN, btnJogFastP, btnStop;
		private Sunny.UI.UILabel lblSimStatus, lblSimCount, lblLastTrig, lblFwdSpeed, lblRetSpeed, lblMaxPhoto, lblCycleDelay;
		private Sunny.UI.UITextBox txtFwdSpeed, txtRetSpeed, txtMaxPhoto, txtCycleDelay;
		private Sunny.UI.UIButton btnSimRun, btnSimStop;
		private Sunny.UI.UICheckBox chkSimLoop;
		private Sunny.UI.UILabel lblIn12, lblIn13, lblCamHint;
		private Sunny.UI.UIButton btnCam1, btnCam2, btnIn12Manual;
		private Sunny.UI.UICheckBox chkIn12Manual;
	}
}
