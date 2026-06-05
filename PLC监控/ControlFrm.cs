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
		private System.Windows.Forms.Timer _uiTimer;
		private AxisParamConfig _axisCfg;
		private IntPtr _handle = IntPtr.Zero;
		private bool _connected;
		private bool _in12ManualMode, _in12Prev;
		private Thread _in12Thread;
		private bool _simRunning, _simLoop, _simLoopFlag;
		private int _simMaxPhoto, _simDelayMs;
		private float _simFwdSpd, _simRetSpd;
		private Thread _simThread;
		public static bool IsManualIn12Mode { get; private set; }

		public ControlFrm() { InitializeComponent(); }
		public ControlFrm(IntPtr handle) { InitializeComponent(); _handle = handle; _connected = handle != IntPtr.Zero; }

		private void ControlFrm_Load(object sender, EventArgs e)
		{
			_axisCfg = AxisParamConfig.Load();
			PopulateUIFromConfig();
			UpdateConnUI();

			_uiTimer = new System.Windows.Forms.Timer { Interval = 60 };
			_uiTimer.Tick += OnUiTick;
			_uiTimer.Start();
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
		}

		void OnUiTick(object sender, EventArgs e)
		{
			if (!_connected) return;
			int a = cbAxis.SelectedIndex >= 0 ? cbAxis.SelectedIndex : 0;

			float dpos = 0, mpos = 0, spd = 0, fe = 0;
			int idle = 0, axisSts = 0, enable = 0;
			ZAux_Direct_GetDpos(_handle, a, ref dpos);
			ZAux_Direct_GetMpos(_handle, a, ref mpos);
			ZAux_Direct_GetMspeed(_handle, a, ref spd);
			ZAux_Direct_GetFe(_handle, a, ref fe);
			ZAux_Direct_GetIfIdle(_handle, a, ref idle);
			ZAux_Direct_GetAxisStatus(_handle, a, ref axisSts);
			ZAux_Direct_GetAxisEnable(_handle, a, ref enable);

			lblDpos.Text = "DPOS: " + dpos.ToString("F3");
			lblMpos.Text = "MPOS: " + mpos.ToString("F3");
			lblCurSpeed.Text = "速度: " + spd.ToString("F2");
			lblAxisStatus.Text = "轴状态: 0x" + axisSts.ToString("X4") + (enable == 1 ? " [使能]" : " [未使能]");
			lblIdle.Text = "运动: " + (idle == -1 ? "空闲" : "运行中");
			lblFe.Text = "跟随误差: " + fe.ToString("F3") + "  正限:-- 负限:--";
			// 读限位状态
			uint fwdLim = 0, revLim = 0;
			ZAux_Direct_GetIn(_handle, 14, ref fwdLim);
			ZAux_Direct_GetIn(_handle, 15, ref revLim);
			lblFe.Text = "跟随误差: " + fe.ToString("F3") + "  正限:" + (fwdLim == 1 ? "触发" : "正常") + " 负限:" + (revLim == 1 ? "触发" : "正常");

			if (!_in12ManualMode)
			{
				uint v12 = 0, v13 = 0;
				ZAux_Direct_GetIn(_handle, 12, ref v12);
				ZAux_Direct_GetIn(_handle, 13, ref v13);
				lblIn12.Text = "IN12: " + (v12 == 1 ? "高" : "低");
				lblIn13.Text = "IN13: " + (v13 == 1 ? "高" : "低");
			}
		}

		void UpdateConnUI()
		{
			lblStatus.Text = _connected ? "● 已连接" : "● 未连接";
			lblStatus.ForeColor = _connected ? Color.LimeGreen : Color.Red;
			btnConnect.Enabled = !_connected;
			btnDisconnect.Enabled = _connected;
		}

		// ====== 连接 ======
		private void btnConnect_Click(object sender, EventArgs e)
		{
			try
			{
				int ret = ZAux_FastOpen(2, txtIP.Text.Trim(), 1000, out _handle);
				if (ret == 0)
				{
					_connected = true; UpdateConnUI();
					SaveParamsFromUI(); _axisCfg.Save();
					_axisCfg = AxisParamConfig.Load();
					PopulateUIFromConfig();
					ApplyAll();

					// 检查当前位置是否在起始位置
					int a = _axisCfg.Axis;
					float dpos = 0; ZAux_Direct_GetDpos(_handle, a, ref dpos);
					float startPos = _axisCfg.StartPos;
					if (Math.Abs(dpos - startPos) > 0.5f)
					{
						var dr = MessageBox.Show("轴" + a + " 当前位置 " + dpos.ToString("F2") + "，不在起始位置 " + startPos.ToString("F2") + "\n\n是否自动返回起始位置？", "位置检查", MessageBoxButtons.YesNo, MessageBoxIcon.Warning);
						if (dr == DialogResult.Yes)
						{
							if (CheckAndWaitSafety()) { SetMotionParams(a, _axisCfg.RetSpeed); ZAux_Direct_Single_MoveAbs(_handle, a, startPos); }
							UIMessageTip.ShowOk(this, "正在返回起始位置 " + startPos.ToString("F2"));
						}
					}
					UIMessageTip.ShowOk(this, "连接成功");
				}
				else UIMessageTip.ShowError(this, "连接失败: 错误码" + ret);
			}
			catch (Exception ex) { UIMessageTip.ShowError(this, ex.Message); }
		}

		private void btnDisconnect_Click(object sender, EventArgs e)
		{
			if (_handle != IntPtr.Zero) { ZAux_Close(_handle); _handle = IntPtr.Zero; }
			_connected = false; UpdateConnUI();
		}

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

		private void btnAlarmClear_Click(object sender, EventArgs e)
		{
			if (!_connected) return;
			int a = SelAxis();
			var sb = new StringBuilder(256);
			int ret1 = ZAux_DirectCommand(_handle, "AXISSTATUS(" + a + ")=0", sb, 256);
			Thread.Sleep(50);
			float mpos = 0; ZAux_Direct_GetMpos(_handle, a, ref mpos);
			int ret2 = ZAux_Direct_SetDpos(_handle, a, mpos);
			Logger.Info($"轴{a} 报警清除: Cmd ret={ret1}, DPOS=MPOS={mpos} ret={ret2}");
			UIMessageTip.ShowOk(this, ret1 == 0 ? "报警清除 已发送" : "报警清除失败:" + ret1);
		}

		// ====== 回零 ======
		private void btnHome_Click(object sender, EventArgs e)
		{
			if (!_connected) return;
			int a = SelAxis();
			int mode = cbHomeMode.SelectedIndex;
			float.TryParse(txtHomeSpeed.Text, out float hiSpd);
			float.TryParse(txtCreep.Text, out float creep);
			float.TryParse(txtHomeOffset.Text, out float offset);
			ZAux_Direct_SetSpeed(_handle, a, hiSpd > 0 ? hiSpd : 20f);
			ZAux_Direct_SetCreep(_handle, a, creep > 0 ? creep : 10f);
			int ret;
			if (Math.Abs(offset) > 0.001f)
				ret = ZAux_Direct_UserDatum(_handle, a, mode, hiSpd, creep, offset);
			else
				ret = ZAux_Direct_Single_Datum(_handle, a, mode);
			Logger.Info($"轴{a} 回零 模式{mode} 高速{hiSpd} 爬行{creep} 偏移{offset}, ret={ret}");
			UIMessageTip.ShowOk(this, ret == 0 ? "回零已启动" : "回零失败:" + ret);
		}

		private void btnHomeStop_Click(object sender, EventArgs e)
		{
			if (!_connected) return;
			int a = SelAxis();
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

		private void btnMoveAbs_Click(object sender, EventArgs e) { if (!_connected || !CheckAndWaitSafety()) return; float.TryParse(txtTargetPos.Text, out float t); Task.Run(() => { int a = SelAxis(); ZAux_Direct_Single_MoveAbs(_handle, a, t); MonitorSafetyDuringMove(a, _axisCfg.StartPos); }); }
		private void btnMoveRel_Click(object sender, EventArgs e) { if (!_connected || !CheckAndWaitSafety()) return; float.TryParse(txtTargetPos.Text, out float t); int a = SelAxis(); Task.Run(() => { ZAux_Direct_Single_Move(_handle, a, t); MonitorSafetyDuringMove(a, _axisCfg.StartPos); }); }
		private void btnStop_Click(object sender, EventArgs e) { if (!_connected) return; ZAux_Direct_Single_Cancel(_handle, SelAxis(), 2); }

		private void btnJogN_MouseDown(object sender, MouseEventArgs e) { JogStart(-1, false); }
		private void btnJogP_MouseDown(object sender, MouseEventArgs e) { JogStart(1, false); }
		private void btnJogFastN_MouseDown(object sender, MouseEventArgs e) { JogStart(-1, true); }
		private void btnJogFastP_MouseDown(object sender, MouseEventArgs e) { JogStart(1, true); }
		private void btnJog_MouseUp(object sender, MouseEventArgs e) { if (!_connected) return; ZAux_Direct_Single_Cancel(_handle, SelAxis(), 2); }

		void JogStart(int dir, bool fast) { if (!_connected || !CheckAndWaitSafety()) return; int a = SelAxis(); float spd = fast ? _axisCfg.Speed : (_axisCfg.Speed * 0.2f); if (spd <= 0) spd = fast ? 50 : 10; ZAux_Direct_SetSpeed(_handle, a, spd); ZAux_Direct_Single_Vmove(_handle, a, dir); }


		// ====== 安全锁 ======
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
					if (returnToStart) { SimUI($"安全锁恢复→回起点({startPos:F1})"); ZAux_Direct_Single_MoveAbs(_handle, axis, startPos); }
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
			if (_connected && _handle != IntPtr.Zero)
			{
				try { ZAux_Direct_Single_Cancel(_handle, SelAxis(), 2); } catch { }
				Thread.Sleep(20);
				try { ZAux_Close(_handle); } catch { }
				_handle = IntPtr.Zero;
			}
			_uiTimer?.Stop();
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
			ZAux_Direct_Single_MoveAbs(_handle, a, startPos);
			if (!WaitArriveNoPhoto(a, startPos, 120, startPos)) { SimUI("超时: 未到达起点"); _simRunning = false; EnableSimBtn(); return; }

			do
			{
				// === 前进拍照 ===
				int thisOut14 = 0, thisOut15 = 0;
				SimUI("前进(" + fwdSpd + "mm/s) → " + endPos.ToString("F1") + "  总计:" + totalPhoto);
				if (!CheckAndWaitSafety()) { SimUI("安全锁-中止"); break; }
				SetMotionParams(a, fwdSpd);
				ZAux_Direct_Single_MoveAbs(_handle, a, endPos);
				bool prevIn12 = false;
				float prevDpos = startPos;
				while (_simRunning)
				{
					// 运动中安全锁检查：不安全→急停→等待恢复→恢复模式处理
					if (!PollSafety())
					{
						ZAux_Direct_Single_Cancel(_handle, a, 0); SimUI("安全锁! 前进暂停");
						if (!WaitForSafetyRestore()) break;
						if (_axisCfg.SafetyLockRecovery == 1) { SimUI("安全锁恢复→回起点"); ZAux_Direct_Single_MoveAbs(_handle, a, startPos); break; }
						SimUI("安全锁恢复→继续前进"); ZAux_Direct_Single_MoveAbs(_handle, a, endPos);
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
				ZAux_Direct_Single_MoveAbs(_handle, a, startPos);
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
				if (!PollSafety()) { ZAux_Direct_Single_Cancel(_handle, a, 0); SimUI("安全锁! 暂停"); if (!WaitForSafetyRestore()) return false; if (_axisCfg.SafetyLockRecovery == 1) { SimUI("安全锁恢复→回起点"); ZAux_Direct_Single_MoveAbs(_handle, a, startRefPos); return false; } SimUI("安全锁恢复→继续"); ZAux_Direct_Single_MoveAbs(_handle, a, target); i = 0; continue; }
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
