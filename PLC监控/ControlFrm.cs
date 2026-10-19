using Sunny.UI;
using System;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using CommonLib;
using static cszmcaux.zmcaux;

namespace PLC监控
{
	public partial class ControlFrm : UIForm
	{
		private System.Threading.Timer _uiTimer;
		private int _uiUpdating;  // 防止ZAux后台读取并发
		private AxisParamConfig _axisCfg;
		private IntPtr _handle = IntPtr.Zero;
		private bool _connected;
		private bool _sharedHandle;  // 与主程序共享连接(不允许多次连接/断开)
		private volatile bool _homingAbort;  // 停止回零按钮置位: 通知回零流程中止(不清零坐标/不写完成标志)
		private bool _in12ManualMode, _in12Prev;
		private Thread _in12Thread;
		private bool _simRunning, _simLoop, _simLoopFlag;
		private int _simMaxPhoto, _simDelayMs;
		private float _simFwdSpd, _simRetSpd;
		private Thread _simThread;
		public static bool IsManualIn12Mode { get; private set; }

		public ControlFrm() { InitializeComponent(); }
		public ControlFrm(IntPtr handle) { InitializeComponent(); _handle = handle; _sharedHandle = true; _connected = handle != IntPtr.Zero; }

		private void ControlFrm_Load(object sender, EventArgs e)
		{
			_axisCfg = AxisParamConfig.Load();
			_axisCfg.SafetyLockActiveHigh = false;  // SetInvertIn(8,1)反转后GetIn读0才是关门
			PopulateUIFromConfig();
			// 回零模式(0~14号模式)开放调整, 保存/下发时写入AxisParams.json; 其余回零辅助输入保持隐藏
			cbHomeMode.Visible = true; lblHomeMode.Visible = true;
			UpdateConnUI();

			// 用System.Threading.Timer替代WinForms Timer:
			//    ZAux读取全部在后台线程执行，不阻塞UI消息泵，避免白屏/未响应
			_uiTimer = new System.Threading.Timer(OnUiTimerCallback, null, 100, 60);
		}

		void PopulateUIFromConfig()
		{
			txtSpeed.Text = _axisCfg.Speed.ToString();
			txtAcc.Text = _axisCfg.Accel.ToString();
			txtDec.Text = _axisCfg.Decel.ToString();
			txtLspeed.Text = _axisCfg.Lspeed.ToString();
			txtSramp.Text = _axisCfg.Sramp.ToString();
			txtMaxSpeed.Text = _axisCfg.MaxSpeed.ToString();
			txtCreep.Text = _axisCfg.CreepSpeed.ToString();
			txtStartPos.Text = _axisCfg.StartPos.ToString();
			txtEndPos.Text = _axisCfg.EndPos.ToString();
			txtFwdSpeed.Text = (_axisCfg.FwdSpeed > 0 ? _axisCfg.FwdSpeed : 50f).ToString();
			txtRetSpeed.Text = (_axisCfg.RetSpeed > 0 ? _axisCfg.RetSpeed : 100f).ToString();
			txtMaxPhoto.Text = (_axisCfg.MaxPhotoCount > 0 ? _axisCfg.MaxPhotoCount : 12).ToString();
			txtCycleDelay.Text = (_axisCfg.CycleDelayMs > 0 ? _axisCfg.CycleDelayMs : 500).ToString();
			cbAxis.SelectedIndex = _axisCfg.Axis;
			cbHomeMode.SelectedIndex = Math.Max(0, Math.Min(cbHomeMode.Items.Count - 1, _axisCfg.DatumMode));
		}

	/// <summary>后台线程ZAux读取 + BeginInvoke更新UI (不再阻塞UI消息泵)</summary>
	void OnUiTimerCallback(object state)
	{
		// 防止并发: 上次未读完则跳过本轮
		if (Interlocked.CompareExchange(ref _uiUpdating, 1, 0) != 0) return;
		try
		{
			if (IsDisposed || Disposing) return;
			if (!_connected || _handle == IntPtr.Zero)
			{
				this.BeginInvoke(new Action(() => { if (!IsDisposed && lblInitState != null) { lblInitState.Text = "初始化: 未连接"; lblInitState.ForeColor = System.Drawing.Color.Gray; } }));
				return;
			}
			int a = cbAxis.SelectedIndex >= 0 ? cbAxis.SelectedIndex : 0;

			// ── 所有ZAux同步调用均在后台线程执行，不阻塞UI ──
			float dpos = 0, mpos = 0, spd = 0, fe = 0;
			int idle = 0, axisSts = 0, enable = 0;
			ZAux_Direct_GetDpos(_handle, a, ref dpos);
			ZAux_Direct_GetMpos(_handle, a, ref mpos);
			ZAux_Direct_GetMspeed(_handle, a, ref spd);
			ZAux_Direct_GetFe(_handle, a, ref fe);
			ZAux_Direct_GetIfIdle(_handle, a, ref idle);
			ZAux_Direct_GetAxisStatus(_handle, a, ref axisSts);
			ZAux_Direct_GetAxisEnable(_handle, a, ref enable);

			float initFlag = 0;
			try { ushort[] mbArr = { 0 }; ZAux_Modbus_Get4x(_handle, 100, 1, mbArr); initFlag = mbArr[0]; } catch { }

			uint fwdLim = 0, revLim = 0;
			ZAux_Direct_GetIn(_handle, 14, ref fwdLim);
			ZAux_Direct_GetIn(_handle, 15, ref revLim);

			uint v12 = 0, v13 = 0;
			if (!_in12ManualMode)
			{
				ZAux_Direct_GetIn(_handle, 12, ref v12);
				ZAux_Direct_GetIn(_handle, 13, ref v13);
			}

			// ── 批量BeginInvoke更新所有UI标签 ──
			var fwdLimTxt = fwdLim == 1 ? "触发" : "正常";
			var revLimTxt = revLim == 1 ? "触发" : "正常";
			var initTxt = initFlag == 1 ? "初始化: ● 已完成(绿灯)" : "初始化: ● 未完成(红灯) — 请执行回零";
			var initClr = initFlag == 1 ? System.Drawing.Color.LimeGreen : System.Drawing.Color.Red;
			var idleTxt = idle == -1 ? "空闲" : "运行中";
			var almTxt = (axisSts & 1) != 0 ? " [ALM告警!]" : "";
			var in12Txt = _in12ManualMode ? (lblIn12?.Text ?? "IN12: --") : "IN12: " + (v12 == 1 ? "高" : "低");
			var in13Txt = _in12ManualMode ? (lblIn13?.Text ?? "IN13: --") : "IN13: " + (v13 == 1 ? "高" : "低");

			this.BeginInvoke(new Action(() =>
			{
				if (IsDisposed) return;
				if (lblDpos != null) lblDpos.Text = "DPOS: " + dpos.ToString("F3");
				if (lblMpos != null) lblMpos.Text = "MPOS: " + mpos.ToString("F3");
				if (lblCurSpeed != null) lblCurSpeed.Text = "速度: " + spd.ToString("F2");
				if (lblAxisStatus != null) lblAxisStatus.Text = "轴状态: 0x" + axisSts.ToString("X4") + (enable == 1 ? " [使能]" : " [未使能]") + almTxt;
				if (lblIdle != null) lblIdle.Text = "运动: " + idleTxt;
				if (lblFe != null) lblFe.Text = "跟随误差: " + fe.ToString("F3") + "  正限:" + fwdLimTxt + " 负限:" + revLimTxt;
				if (lblInitState != null) { lblInitState.Text = initTxt; lblInitState.ForeColor = initClr; }
				if (lblIn12 != null) lblIn12.Text = in12Txt;
				if (lblIn13 != null) lblIn13.Text = in13Txt;
			}));
		}
		catch { /* ZAux调用异常静默忽略，下一轮重试 */ }
		finally { Interlocked.Exchange(ref _uiUpdating, 0); }
	}

		void UpdateConnUI()
		{
			lblStatus.Text = _connected ? "● 已连接" : "● 未连接";
			lblStatus.ForeColor = _connected ? Color.LimeGreen : Color.Red;
			btnConnect.Enabled = !_connected;
			btnDisconnect.Enabled = _connected;
		}

		// ====== 连接 ======
		private void btnConnect_Click(object sender, EventArgs e) { if (_sharedHandle) { UIMessageTip.ShowWarning(this, "已共享主程序连接, 无需重复连接"); return; } }
		private void btnDisconnect_Click(object sender, EventArgs e) { if (_sharedHandle) { UIMessageTip.ShowWarning(this, "共享主程序连接, 无法在此断开"); return; } }

		// ====== 轴状态按钮 ======
		private void btnServoOn_Click(object sender, EventArgs e)
		{
			if (!_connected) return;
			int a = SelAxis();
			int ret = ZAux_Direct_SetAxisEnable(_handle, a, 1);
			UIMessageTip.ShowOk(this, ret == 0 ? "伺服ON 已发送" : "伺服ON 失败:" + ret);
			Logger.Info($"轴{a} 伺服ON, ret={ret}");
		}

		private void btnServoOff_Click(object sender, EventArgs e)
		{
			if (!_connected) return;
			int a = SelAxis();
			int ret = ZAux_Direct_SetAxisEnable(_handle, a, 0);
			UIMessageTip.ShowOk(this, ret == 0 ? "伺服OFF 已发送" : "伺服OFF 失败:" + ret);
			Logger.Info($"轴{a} 伺服OFF, ret={ret}");
		}

		/// <summary>
	/// 报警清除: 1.清AXISSTATUS 2.清ALM 3.延时 4.重新使能 5.确认
	/// </summary>
	private void btnAlarmClear_Click(object sender, EventArgs e)
		{
			if (!_connected) return;
			int a = 0;
			// 1. 读取清除前状态
			int stsBefore = 0;
			ZAux_Direct_GetAxisStatus(_handle, a, ref stsBefore);
			Logger.Info($"[报警清除] 清除前状态=0x{stsBefore:X4} ALM={(stsBefore & 1) != 0}");

			// 2. 取消运动 + 关闭使能
			ZAux_Direct_Single_Cancel(_handle, a, 0);
			ZAux_Direct_SetAxisEnable(_handle, a, 0);
			Thread.Sleep(50);

			// 2. 清除轴状态寄存器
			var sb = new StringBuilder(256);
			int ret1 = ZAux_DirectCommand(_handle, "AXISSTATUS(" + a + ")=0", sb, 256);
			int retRls = ZAux_DirectCommand(_handle, "RELEASE(" + a + ")", sb, 256);
			Logger.Info($"[报警清除] AXISSTATUS=0 ret={ret1}, RELEASE ret={retRls}");


			// 3. 重新使能
			Thread.Sleep(100);
			// 3. 重新使能
			int ret2 = ZAux_Direct_SetAxisEnable(_handle, a, 1);
			Logger.Info($"[报警清除] SetAxisEnable ret={ret2}");

			// 5. DPOS对齐MPOS
			float mpos = 0; ZAux_Direct_GetMpos(_handle, a, ref mpos);
			int ret3 = ZAux_Direct_SetDpos(_handle, a, mpos);

			// 6. 确认清除结果
			int stsAfter = 0;
			ZAux_Direct_GetAxisStatus(_handle, a, ref stsAfter);
			bool almCleared = (stsAfter & 1) == 0;
			Logger.Info($"[报警清除] 清除后状态=0x{stsAfter:X4} ALM={(stsAfter & 1) != 0} 清除{(almCleared ? "成功" : "失败")}");

			UIMessageTip.ShowOk(this, almCleared ? $"报警已清除 状态=0x{stsAfter:X4}" : $"报警未清除! 状态=0x{stsAfter:X4}, 请检查硬件");
		}

		// ====== 回零 ======
		/// <summary>
		/// 轴初始化回零 — 调用共享实现 CommonLib.AxisHomingService(与主程序启动自动初始化共用)
		/// 回零模式取自AxisParams.json(DatumMode, 本界面可调整); 限位检查: 正限=cfg.FwdIn/负限=cfg.RevIn/原点=cfg.DatumIn
		/// 成功后写 MODBUS[100]=1 (断电重置, 软件重启保留); 按[停止回零]中止时不写标志
		/// </summary>
		private void btnHome_Click(object sender, EventArgs e)
		{
			Logger.Info($"[回零-按钮] 被点击 connected={_connected} handle={_handle}");
			if (!_connected || _handle == IntPtr.Zero) { Logger.Warning($"[回零-按钮] 拒绝: connected={_connected} handle={_handle}"); return; }
			int a = _axisCfg.Axis;
			_homingAbort = false;

			// 在后台线程执行，不阻塞UI
			Task.Run(() =>
			{
			Logger.Info($"========== 轴{a} 开始初始化回零 (模式{_axisCfg.DatumMode}) ==========");

			// 0. 打开调试界面时禁用侧面工位
			AxisParamConfig.SideEnabledOverride = false;
			Logger.Info("[回零] 临时禁用侧面工位(初始化期间禁止轴运动)");

			// 回零全流程(限位/使能/安全锁/ALM/MODBUS[100]/等待/清零/写标志)由共享服务统一实现
			var res = AxisHomingService.Home(_handle, _axisCfg, a,
				msg => Logger.Info(msg),
				() => _connected && _handle != IntPtr.Zero,
				true,
				() => _homingAbort);

			if (res.IsSuccess)
			{
				Logger.Info($"========== 轴{a} 初始化完成 ==========");
				this.BeginInvoke(new Action(() => UIMessageTip.ShowOk(this, res.Message)));
			}
			else
			{
				Logger.Warning($"[回零-失败] {res.Code}: {res.Message}");
				this.BeginInvoke(new Action(() => UIMessageTip.ShowError(this, res.Message)));
			}
			// 检查侧面工位是否被禁用, 提示恢复
			if (res.IsSuccess && !AxisParamConfig.SideEnabledOverride)
			{
				this.BeginInvoke(new Action(() =>
				{
					var dr = MessageBox.Show("轴初始化完成! 是否恢复侧面工位并退出调试界面?", "初始化完成", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
					if (dr == DialogResult.Yes)
					{
						AxisParamConfig.SideEnabledOverride = true;
						this.Close();
						Logger.Info("用户选择启用侧面工位并退出调试界面");
					}
				}));
			}
			});
		}

		private void btnHomeStop_Click(object sender, EventArgs e)
		{
			if (!_connected) return;
			int a = SelAxis();
			_homingAbort = true;  // 通知回零流程中止(不再清零坐标/写完成标志)
			ZAux_Direct_Single_Cancel(_handle, a, 2);
			Logger.Info($"轴{a} 停止回零");
			UIMessageTip.ShowOk(this, "回零已停止");
		}

		// ====== 参数 ======
		private void btnSaveParams_Click(object sender, EventArgs e)
		{
			SaveParamsFromUI(); _axisCfg.Save();
			if (_connected) ApplyAll();
			UIMessageTip.ShowOk(this, "已保存");
		}

		private void btnApplyAll_Click(object sender, EventArgs e)
		{
			SaveParamsFromUI(); _axisCfg.Save();
			if (_connected) ApplyAll();
			else UIMessageTip.ShowError(this, "请先连接控制卡");
		}

		void SaveParamsFromUI()
		{
			float.TryParse(txtSpeed.Text, out _axisCfg.Speed);
			float.TryParse(txtAcc.Text, out _axisCfg.Accel);
			float.TryParse(txtDec.Text, out _axisCfg.Decel);
			float.TryParse(txtLspeed.Text, out _axisCfg.Lspeed);
			float.TryParse(txtSramp.Text, out _axisCfg.Sramp);
			float.TryParse(txtCreep.Text, out _axisCfg.CreepSpeed);
			int.TryParse(txtMaxSpeed.Text, out _axisCfg.MaxSpeed);
			_axisCfg.Axis = cbAxis.SelectedIndex >= 0 ? cbAxis.SelectedIndex : 0;
			if (cbHomeMode != null && cbHomeMode.SelectedIndex >= 0) _axisCfg.DatumMode = cbHomeMode.SelectedIndex;
			float.TryParse(txtStartPos.Text, out _axisCfg.StartPos);
			float.TryParse(txtEndPos.Text, out _axisCfg.EndPos);
			float.TryParse(txtFwdSpeed.Text, out _axisCfg.FwdSpeed);
			float.TryParse(txtRetSpeed.Text, out _axisCfg.RetSpeed);
		}

		void ApplyAll()
		{
			if (!_connected) return;
			int a = _axisCfg.Axis;
			var log = new StringBuilder();
			log.AppendLine("===== 轴" + a + " 参数下发 =====");
			log.Append(SetAndCheckInt("Atype", () => ZAux_Direct_SetAtype(_handle, a, 65),
				() => { int v = 0; ZAux_Direct_GetAtype(_handle, a, ref v); return v; }, 65));
			log.Append(SetAndCheck("Units", () => ZAux_Direct_SetUnits(_handle, a, 13107.2f),
				() => { float v = 0; ZAux_Direct_GetUnits(_handle, a, ref v); return v; }, 13107.2f));
			log.Append(SetAndCheck("Speed", () => ZAux_Direct_SetSpeed(_handle, a, _axisCfg.Speed),
				() => { float v = 0; ZAux_Direct_GetSpeed(_handle, a, ref v); return v; }, _axisCfg.Speed));
			log.Append(SetAndCheck("Accel", () => ZAux_Direct_SetAccel(_handle, a, _axisCfg.Accel),
				() => { float v = 0; ZAux_Direct_GetAccel(_handle, a, ref v); return v; }, _axisCfg.Accel));
			log.Append(SetAndCheck("Decel", () => ZAux_Direct_SetDecel(_handle, a, _axisCfg.Decel),
				() => { float v = 0; ZAux_Direct_GetDecel(_handle, a, ref v); return v; }, _axisCfg.Decel));
			log.Append(SetAndCheck("Lspeed", () => ZAux_Direct_SetLspeed(_handle, a, _axisCfg.Lspeed),
				() => { float v = 0; ZAux_Direct_GetLspeed(_handle, a, ref v); return v; }, _axisCfg.Lspeed));
			log.Append(SetAndCheck("Sramp", () => ZAux_Direct_SetSramp(_handle, a, _axisCfg.Sramp),
				() => { float v = 0; ZAux_Direct_GetSramp(_handle, a, ref v); return v; }, _axisCfg.Sramp));
			log.Append(SetAndCheck("Creep", () => ZAux_Direct_SetCreep(_handle, a, _axisCfg.CreepSpeed),
				() => { float v = 0; ZAux_Direct_GetCreep(_handle, a, ref v); return v; }, _axisCfg.CreepSpeed));
			log.Append(SetAndCheckInt("FwdIn", () => ZAux_Direct_SetFwdIn(_handle, a, 14),
				() => { int v = 0; ZAux_Direct_GetFwdIn(_handle, a, ref v); return v; }, 14));
			log.Append(SetAndCheckInt("RevIn", () => ZAux_Direct_SetRevIn(_handle, a, 15),
				() => { int v = 0; ZAux_Direct_GetRevIn(_handle, a, ref v); return v; }, 15));
			log.Append(SetAndCheckInt("DatumIn", () => ZAux_Direct_SetDatumIn(_handle, a, 16),
				() => { int v = 0; ZAux_Direct_GetDatumIn(_handle, a, ref v); return v; }, 16));
			log.Append(SetAndCheckInt("MaxSpeed", () => ZAux_Direct_SetMaxSpeed(_handle, a, 1000),
				() => { int v = 0; ZAux_Direct_GetMaxSpeed(_handle, a, ref v); return v; }, 1000));
			Logger.Info(log.ToString());
			UIMessageTip.ShowOk(this, "参数下发完成，结果见日志");
		}

		static string SetAndCheck(string name, Action set, Func<float> get, float expected)
		{
			set(); Task.Delay(5).Wait();
			float actual = get();
			return name + ": 写" + expected + " 读" + actual + " " + (Math.Abs(actual - expected) < 0.01f ? "[OK]" : "[NG]") + "\n";
		}
		static string SetAndCheckInt(string name, Action set, Func<int> get, int expected)
		{
			set(); Task.Delay(5).Wait();
			int actual = get();
			return name + ": 写" + expected + " 读" + actual + " " + (actual == expected ? "[OK]" : "[NG]") + "\n";
		}

		// ====== 运动控制 ======
		int SelAxis() => cbAxis.SelectedIndex >= 0 ? cbAxis.SelectedIndex : 0;

		private void btnMoveAbs_Click(object sender, EventArgs e) { if (!_connected || !CheckAxisEnable() || !CheckAndWaitSafety()) return; float.TryParse(txtTargetPos.Text, out float t); Task.Run(() => { int a = SelAxis(); SetMotionParams(a, _axisCfg.Speed); ZAux_Direct_Single_MoveAbs(_handle, a, t); MonitorSafetyDuringMove(a, _axisCfg.StartPos); }); }
		private void btnMoveRel_Click(object sender, EventArgs e) { if (!_connected || !CheckAxisEnable() || !CheckAndWaitSafety()) return; float.TryParse(txtTargetPos.Text, out float t); int a = SelAxis(); Task.Run(() => { SetMotionParams(a, _axisCfg.Speed); ZAux_Direct_Single_Move(_handle, a, t); MonitorSafetyDuringMove(a, _axisCfg.StartPos); }); }
		private void btnStop_Click(object sender, EventArgs e) { if (!_connected) return; ZAux_Direct_Single_Cancel(_handle, SelAxis(), 2); }

		private void btnJogN_MouseDown(object sender, MouseEventArgs e) { JogStart(-1, false); }
		private void btnJogP_MouseDown(object sender, MouseEventArgs e) { JogStart(1, false); }
		private void btnJogFastN_MouseDown(object sender, MouseEventArgs e) { JogStart(-1, true); }
		private void btnJogFastP_MouseDown(object sender, MouseEventArgs e) { JogStart(1, true); }
		private void btnJog_MouseUp(object sender, MouseEventArgs e) { if (!_connected) return; ZAux_Direct_Single_Cancel(_handle, SelAxis(), 2); }


		/// <summary>检查轴使能状态, 未使能则提示并阻止运动</summary>
		private bool CheckAxisEnable()
		{
			if (!_connected) return false;
			int a = 0;  // 固定轴0
			int enable = 0;
			ZAux_Direct_GetAxisEnable(_handle, a, ref enable);
			if (enable != 1)
			{
				UIMessageTip.ShowError(this, "轴0 未使能(伺服OFF), 请先点击[伺服ON]");
				return false;
			}
			return true;
		}
		void JogStart(int dir, bool fast) { if (!_connected || !CheckAxisEnable() || !CheckSafety()) return; }  // CheckSafety非阻塞, 不卡UI int a = SelAxis(); float spd = fast ? Math.Min(_axisCfg.Speed, 100f) : Math.Min(_axisCfg.Speed * 0.2f, 20f); if (spd <= 0) spd = fast ? 50 : 10; ZAux_Direct_SetSpeed(_handle, a, spd); ZAux_Direct_Single_Vmove(_handle, a, dir); }


		// ====== 安全锁 ======
		/// <summary>非阻塞安全检查(仅轮询一次, JogStart等UI线程调用)</summary>
		private bool CheckSafety()
		{
			if (!_connected || _handle == IntPtr.Zero) return false;
			int port = _axisCfg.SafetyLockPort;
			if (port <= 0) return true;
			try
			{
				uint val = 0; ZAux_Direct_GetIn(_handle, port, ref val);
				bool safe = _axisCfg.SafetyLockActiveHigh ? (val == 1) : (val == 0);
				if (!safe) UIMessageTip.ShowWarning(this, "安全锁触发, 请关门后重试");
				return safe;
			}
			catch { return false; }
		}
		/// <summary>检查安全锁，不安全则阻塞等待。返回false=连接断开</summary>
		private bool CheckAndWaitSafety()
		{
			if (!_connected || _handle == IntPtr.Zero) return false;
			int port = _axisCfg.SafetyLockPort;
			if (port <= 0) return true;
			bool activeHigh = _axisCfg.SafetyLockActiveHigh;
			try
			{
				uint val = 0; ZAux_Direct_GetIn(_handle, port, ref val);
				bool safe = activeHigh ? (val == 1) : (val == 0);
				if (!safe)
				{
					SimUI($"安全锁触发 IN{port}=0 等待关门...");
					while (_connected && _handle != IntPtr.Zero)
					{
						ZAux_Direct_GetIn(_handle, port, ref val);
						if (activeHigh ? (val == 1) : (val == 0)) { SimUI($"安全锁释放 IN{port}=1 继续"); return true; }
						Thread.Sleep(20);
					}
					return false;
				}
				return true;
			}
			catch { return false; }
		}

		/// <summary>运动中安全锁监控：不安全则急停，恢复后根据模式继续或回起点</summary>
		private bool MonitorSafetyDuringMove(int axis, float startPos, int timeoutMs = 15000)
		{
			int port = _axisCfg.SafetyLockPort;
			if (port <= 0 || !_connected) return true;
			bool activeHigh = _axisCfg.SafetyLockActiveHigh;
			bool returnToStart = _axisCfg.SafetyLockRecovery == 1;
			bool stopped = false;
			int elapsed = 0;
			while (elapsed < timeoutMs && _connected)
			{
				uint val = 0;
				try { ZAux_Direct_GetIn(_handle, port, ref val); } catch { return false; }
				bool safe = activeHigh ? (val == 1) : (val == 0);
				if (!safe)
				{
					if (!stopped) { SimUI("安全锁触发! 急停"); ZAux_Direct_Single_Cancel(_handle, axis, 0); stopped = true; }
					Thread.Sleep(10); elapsed += 10;
					continue;
				}
				if (stopped)
				{
					stopped = false;
					float dpos = 0; ZAux_Direct_GetDpos(_handle, axis, ref dpos);
					if (returnToStart) { SimUI($"安全锁恢复→回起点({startPos:F1})"); SafeMoveAbs(axis, startPos, 50f); }
					else { SimUI($"安全锁恢复→继续(当前位置{dpos:F1})"); }
				}
				if (CheckAxisStopped(axis)) return true;
				Thread.Sleep(10); elapsed += 10;
			}
			return true;
		}

		private bool CheckAxisStopped(int axis)
		{
			int idle = 0;
			try { ZAux_Direct_GetIfIdle(_handle, axis, ref idle); } catch { return true; }
			return idle != 0;
		}
		// ====== 拍照区间 ======
		private void btnSetStart_Click(object sender, EventArgs e) { if (!_connected) return; float pos = 0; ZAux_Direct_GetDpos(_handle, SelAxis(), ref pos); txtStartPos.Text = pos.ToString("F2"); }
		private void btnSetEnd_Click(object sender, EventArgs e) { if (!_connected) return; float pos = 0; ZAux_Direct_GetDpos(_handle, SelAxis(), ref pos); txtEndPos.Text = pos.ToString("F2"); }
		private void btnCam1_Click(object sender, EventArgs e) { TriggerOut(14, "OUT14→Cam7"); }
		private void btnCam2_Click(object sender, EventArgs e) { TriggerOut(15, "OUT15→Cam8"); }
		void TriggerOut(int port, string label) { if (!_connected) return; Task.Run(() => { ZAux_Direct_SetOp(_handle, port, 1); Thread.Sleep(20); ZAux_Direct_SetOp(_handle, port, 0); }); this.BeginInvoke(new Action(() => lblLastTrig.Text = "最近触发: " + label + " @" + DateTime.Now.ToString("HH:mm:ss.fff"))); }
		private void btnSavePhoto_Click(object sender, EventArgs e) { SaveParamsFromUI(); _axisCfg.Save(); UIMessageTip.ShowOk(this, "拍照区间已保存: " + _axisCfg.StartPos + " ~ " + _axisCfg.EndPos); }

		// ====== 关闭窗口 - 先停轴再断开 ======
		private void ControlFrm_Closing(object sender, FormClosingEventArgs e)
		{
			_simRunning = false; _in12ManualMode = false; IsManualIn12Mode = false;
			// 共享连接不关闭(主程序还在用)
			if (!_sharedHandle && _connected && _handle != IntPtr.Zero)
			{
				try { ZAux_Direct_Single_Cancel(_handle, SelAxis(), 2); } catch { }
				Thread.Sleep(20);
				try { ZAux_Close(_handle); } catch { }
				_handle = IntPtr.Zero;
			}
			if (_sharedHandle) { _connected = false; _handle = IntPtr.Zero; } // 仅清理引用
				AxisParamConfig.SideEnabledOverride = true;
			_uiTimer?.Dispose();  // System.Threading.Timer 用 Dispose 释放
		}

		// ====== IN12手动触发 ======
		private void chkIn12Manual_CheckedChanged(object sender, EventArgs e)
		{
			_in12ManualMode = chkIn12Manual.Checked; IsManualIn12Mode = _in12ManualMode;
			if (_in12ManualMode) { btnIn12Manual.FillColor = Color.Green; _in12Prev = false; _in12Thread = new Thread(In12Loop) { IsBackground = true }; _in12Thread.Start(); }
			else btnIn12Manual.FillColor = Color.Gray;
		}
		void In12Loop()
		{
			while (_in12ManualMode && _connected)
			{
				uint v12 = 0; ZAux_Direct_GetIn(_handle, 12, ref v12);
				bool cur = (v12 == 1);
				if (cur != _in12Prev)
				{
					int cam = cur ? 7 : 8, outP = cur ? 14 : 15;
					this.BeginInvoke(new Action(() => lblIn12.Text = "IN12: " + (cur ? "↑→Cam" + cam : "↓→Cam" + cam)));
					ZAux_Direct_SetOp(_handle, outP, 1); Thread.Sleep(50); ZAux_Direct_SetOp(_handle, outP, 0);
				}
				_in12Prev = cur;
				Thread.Sleep(5);
			}
		}

		// ====== 模拟运行 ======
		private void btnSimRun_Click(object sender, EventArgs e)
		{
			if (!_connected) { UIMessageTip.ShowError(this, "请先连接控制卡"); return; }
			if (_simRunning) return;
			SaveParamsFromUI();
			_in12ManualMode = false; IsManualIn12Mode = false;
			this.BeginInvoke(new Action(() => { chkIn12Manual.Checked = false; btnIn12Manual.FillColor = Color.Gray; }));
			// 在UI线程读取所有参数，避免跨线程访问控件
			_simLoopFlag = chkSimLoop.Checked;
			int.TryParse(txtMaxPhoto.Text, out _simMaxPhoto); if (_simMaxPhoto <= 0) _simMaxPhoto = 12;
			int.TryParse(txtCycleDelay.Text, out _simDelayMs); if (_simDelayMs <= 0) _simDelayMs = 500;
			float.TryParse(txtFwdSpeed.Text, out _simFwdSpd); if (_simFwdSpd <= 0) _simFwdSpd = 50;
			float.TryParse(txtRetSpeed.Text, out _simRetSpd); if (_simRetSpd <= 0) _simRetSpd = 100;
			_simRunning = true;
			btnSimRun.Enabled = false; btnSimStop.Enabled = true;
			_simThread = new Thread(SimLoop) { IsBackground = true };
			_simThread.Start();
		}

		private void btnSimStop_Click(object sender, EventArgs e)
		{
			_simRunning = false;
			if (_connected) ZAux_Direct_Single_Cancel(_handle, SelAxis(), 2);
			btnSimRun.Enabled = true; btnSimStop.Enabled = false;
			SimUI("已停止");
		}

		void SimLoop()
		{
			int a = SelAxis();
			float startPos = _axisCfg.StartPos;
			float endPos = _axisCfg.EndPos;
			if (Math.Abs(endPos - startPos) < 0.01f) { SimUI("起点=终点，请检查区间"); _simRunning = false; EnableSimBtn(); return; }
			bool fwd = endPos > startPos;
			float fwdSpd = _simFwdSpd, retSpd = _simRetSpd;
			int maxPhoto = _simMaxPhoto, delayMs = _simDelayMs;
			bool loopMode = _simLoopFlag;
			int totalPhoto = 0, cntRise = 0, cntFall = 0, cntOut14 = 0, cntOut15 = 0;

			// 先回到起点
			SimUI("回起点(" + retSpd + "mm/s) → " + startPos.ToString("F1"));
			if (!CheckAndWaitSafety()) { SimUI("安全锁-连接断开"); _simRunning = false; EnableSimBtn(); return; }
			SetMotionParams(a, retSpd);
			SafeMoveAbs(a, startPos, retSpd);
			if (!WaitArriveNoPhoto(a, startPos, 120, startPos)) { SimUI("超时: 未到达起点"); _simRunning = false; EnableSimBtn(); return; }

			do
			{
				// === 前进拍照 ===
				int thisOut14 = 0, thisOut15 = 0;
				SimUI("前进(" + fwdSpd + "mm/s) → " + endPos.ToString("F1") + "  总计:" + totalPhoto);
				if (!CheckAndWaitSafety()) { SimUI("安全锁-中止"); break; }
				SetMotionParams(a, fwdSpd);
				SafeMoveAbs(a, endPos, fwdSpd);
				bool prevIn12 = false;
				float prevDpos = startPos;
				while (_simRunning)
				{
					// 运动中安全锁检查：不安全→急停→等待恢复→恢复模式处理
					if (!PollSafety())
					{
						ZAux_Direct_Single_Cancel(_handle, a, 0); SimUI("安全锁! 前进暂停");
						if (!WaitForSafetyRestore()) break;
						if (_axisCfg.SafetyLockRecovery == 1) { SimUI("安全锁恢复→回起点"); SafeMoveAbs(a, startPos, retSpd); break; }
						SimUI("安全锁恢复→继续前进"); SafeMoveAbs(a, endPos, fwdSpd);
					}
					float dpos = 0; ZAux_Direct_GetDpos(_handle, a, ref dpos);
					bool arrived = fwd ? (dpos >= endPos - 0.1f) : (dpos <= endPos + 0.1f);
					bool enough = thisOut14 >= maxPhoto && thisOut15 >= maxPhoto;
					if (arrived || enough) break;
					bool movingForward = fwd ? (dpos >= prevDpos) : (dpos <= prevDpos);

					uint v12 = 0; ZAux_Direct_GetIn(_handle, 12, ref v12);
					bool curIn12 = (v12 == 1);
					if (movingForward && curIn12 != prevIn12)
					{
						totalPhoto++;
						if (curIn12) { cntRise++; cntOut14++; thisOut14++; } else { cntFall++; cntOut15++; thisOut15++; }
						int outP = curIn12 ? 14 : 15, cam = curIn12 ? 7 : 8;
						ZAux_Direct_SetOp(_handle, outP, 1); Thread.Sleep(50); ZAux_Direct_SetOp(_handle, outP, 0);
						string tag = curIn12 ? "↑OUT14(Cam7)" : "↓OUT15(Cam8)";
						SimUI(tag + " C7:" + thisOut14 + "/" + maxPhoto + " C8:" + thisOut15 + "/" + maxPhoto + "  总计:" + totalPhoto);
						this.BeginInvoke(new Action(() =>
						{
							lblSimCount.Text = "C7:" + thisOut14 + "/" + maxPhoto + " C8:" + thisOut15 + "/" + maxPhoto;
							lblLastTrig.Text = tag + " @" + DateTime.Now.ToString("HH:mm:ss.fff");
						}));
					}
					prevIn12 = curIn12;
					prevDpos = dpos;
					Thread.Sleep(5);
				}
				if (!_simRunning) break;
				ZAux_Direct_Single_Cancel(_handle, a, 2);
				string reason = (thisOut14 >= maxPhoto && thisOut15 >= maxPhoto) ? "各拍够" + maxPhoto + "张" : "到终点";
				SimUI(reason + "，立即返回起点  总计:" + totalPhoto + " ↑" + cntRise + "↓" + cntFall);

				// === 返回起点（每次前进后必定返回，不管是否循环）===
				SimUI("返回起点(" + retSpd + "mm/s) → " + startPos.ToString("F1"));
				if (!CheckAndWaitSafety()) { SimUI("安全锁-中止返回"); if (loopMode) continue; else break; }
				SetMotionParams(a, retSpd);
				SafeMoveAbs(a, startPos, retSpd);
				if (!WaitArriveNoPhoto(a, startPos, 120, startPos)) { if (!_simRunning) break; SimUI("超时: 未回到起点"); break; }
				if (!_simRunning) break;
				SimUI("已回到起点  总计:" + totalPhoto + " ↑" + cntRise + "↓" + cntFall);

				if (loopMode && _simRunning)
				{
					SimUI("等待 " + delayMs + "ms 后下一轮...");
					Thread.Sleep(delayMs);
				}

			} while (loopMode && _simRunning);

			if (_simRunning) SimUI("结束 — 总计" + totalPhoto + "次 ↑" + cntRise + "↓" + cntFall + " OUT14:" + cntOut14 + " OUT15:" + cntOut15);
			_simRunning = false;
			EnableSimBtn();
		}

		// 回程等待：只等位置，不触发拍照。支持安全锁暂停/恢复
		bool WaitArriveNoPhoto(int a, float target, int timeoutSec, float startRefPos)
		{
			float tol = 0.5f;
			int maxIter = timeoutSec * 200;  // 10ms per iteration
			for (int i = 0; i < maxIter; i++)
			{
				if (!_simRunning) return false;
				// 安全锁检查：不安全→急停→等待→恢复
				if (!PollSafety()) { ZAux_Direct_Single_Cancel(_handle, a, 0); SimUI("安全锁! 暂停"); if (!WaitForSafetyRestore()) return false; if (_axisCfg.SafetyLockRecovery == 1) { SimUI("安全锁恢复→回起点"); SafeMoveAbs(a, startRefPos, 100f); return false; } SimUI("安全锁恢复→继续"); SafeMoveAbs(a, target, 100f); i = 0; continue; }
				float dpos = 0; ZAux_Direct_GetDpos(_handle, a, ref dpos);
				if (Math.Abs(dpos - target) < tol) return true;
				Thread.Sleep(5);
			}
			return false;
		}

		bool PollSafety() { if (_axisCfg.SafetyLockPort <= 0 || !_connected) return true; uint v = 0; ZAux_Direct_GetIn(_handle, _axisCfg.SafetyLockPort, ref v); return _axisCfg.SafetyLockActiveHigh ? (v == 1) : (v == 0); }

		bool WaitForSafetyRestore()
		{
			while (_simRunning && _connected)
			{
				if (PollSafety()) return true;
				Thread.Sleep(20);
			}
			return false;
		}

		void SimUI(string msg) { this.BeginInvoke(new Action(() => lblSimStatus.Text = msg)); }
		void EnableSimBtn() { this.BeginInvoke(new Action(() => { btnSimRun.Enabled = true; btnSimStop.Enabled = false; })); }

		/// <summary>安全运动: 先设速度再MoveAbs, 防止速度残留导致窜出限位</summary>
		void SafeMoveAbs(int a, float pos, float spd)
		{
			ZAux_Direct_SetSpeed(_handle, a, spd);
			Thread.Sleep(5);
			ZAux_Direct_Single_MoveAbs(_handle, a, pos);
		}
		void SetMotionParams(int a, float spd)
		{
			// 运动时只改速度，其他参数(限位/加减速/S曲线/最高速)在ApplyAll已设好
			ZAux_Direct_SetSpeed(_handle, a, spd);
			Thread.Sleep(5);
			float actual = 0; ZAux_Direct_GetSpeed(_handle, a, ref actual);
			if (Math.Abs(actual - spd) > 1f)
				Logger.Warning($"SetSpeed 轴{a}: 期望{spd} 实际{actual}");
		}
	}
}
