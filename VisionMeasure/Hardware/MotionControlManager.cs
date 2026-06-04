using CommonLib;
using System;
using System.Threading;
using System.Threading.Tasks;
using VisionMeasure.Utils;using CommonLib;
using static cszmcaux.zmcaux;

namespace Hardware
{
	public class MotionControlManager
	{
		private readonly takephotoVm _zmc = new takephotoVm();
		private IntPtr _handle = IntPtr.Zero;
		private readonly string _ipAddress;
		private bool _connected;
		private bool _simulateMode;
		public int ConnectionTimeoutMs { get; set; } = 3000;

		public IntPtr Handle => _handle;
		public bool IsConnected => _connected && _handle != IntPtr.Zero;

		public MotionControlManager(string ipAddress, bool simulateMode = true)
		{
			_ipAddress = ipAddress;
			_simulateMode = simulateMode;
		}

		public bool Connect()
		{
			if (_simulateMode)
			{
				Logger.Info($"模拟模式：运动控制卡连接成功 IP={_ipAddress}");
				_connected = true;
				return true;
			}

			try
			{
				Logger.Info($"正在连接运动控制卡: {_ipAddress}");

				var connectTask = Task.Run(() => _zmc.Connect(ref _handle, _ipAddress));

				if (connectTask.Wait(ConnectionTimeoutMs))
				{
					_connected = connectTask.Result;
					if (_connected)
					{
						Logger.Info($"运动控制卡连接成功, IP={_ipAddress}");
						return true;
					}
				}

				Logger.Warning($"运动控制卡连接超时或失败, IP={_ipAddress}");
				_connected = false;
				return false;
			}
			catch (Exception ex)
			{
				Logger.Error($"运动控制卡连接异常: {ex.Message}");
				_connected = false;
				return false;
			}
		}

		public void InitAxes()
		{
			if (_simulateMode)
			{
				Logger.Info("模拟模式：轴初始化完成");
				return;
			}

			if (!IsConnected)
			{
				Logger.Warning("运动控制卡未连接，跳过InitAxes");
				return;
			}
			_zmc.Init(_handle);
			Logger.Info("轴初始化完成");
		}

		public bool MoveAbs(int axis, float position)
		{
			if (_simulateMode)
			{
				Logger.Debug($"模拟模式：轴{axis}移动到位置: {position}");
				return true;
			}

			if (!IsConnected) return false;

			try
			{
				_zmc.MoveAbs(_handle, axis, position);
				Logger.Debug($"轴{axis}移动到位置: {position}");
				return true;
			}
			catch (Exception ex)
			{
				Logger.Error($"MoveAbs失败 轴{axis}: {ex.Message}");
				return false;
			}
		}

		public bool GoPosition(int axis, float position)
		{
			if (_simulateMode)
			{
				Logger.Debug($"模拟模式：轴{axis}定点移动到: {position}");
				return true;
			}

			if (!IsConnected) return false;

			try
			{
				_zmc.GoPosition(_handle, axis, position);
				Logger.Debug($"轴{axis}定点移动到: {position}");
				return true;
			}
			catch (Exception ex)
			{
				Logger.Error($"GoPosition失败 轴{axis}: {ex.Message}");
				return false;
			}
		}

		public float GetPosition(int axis)
		{
			if (_simulateMode) return 0;

			if (!IsConnected) return -1;

			try
			{
				return _zmc.GetLocation(_handle, axis);
			}
			catch (Exception ex)
			{
				Logger.Error($"GetPosition失败 轴{axis}: {ex.Message}");
				return -1;
			}
		}

		public bool IsMoving(int axis)
		{
			if (_simulateMode) return false;

			if (!IsConnected) return false;

			try
			{
				return _zmc.IFInMotionsMethod(_handle, axis);
			}
			catch (Exception ex)
			{
				Logger.Error($"IsMoving失败 轴{axis}: {ex.Message}");
				return false;
			}
		}

		public bool StopAxis(int axis)
		{
			if (_simulateMode) return true;
			if (!IsConnected) return false;
			try { return _zmc.StopMove(_handle, axis); }
			catch (Exception ex) { Logger.Error($"StopAxis失败 轴{axis}: {ex.Message}"); return false; }
		}

		/// <summary>紧急停止(模式0=立即停)，用于安全锁触发</summary>
		public bool EmergencyStop(int axis)
		{
			if (_simulateMode) return true;
			if (!IsConnected) return false;
			try
			{
				int ret = ZAux_Direct_Single_Cancel(_handle, axis, 0);
				Logger.Warning($"[安全锁] 轴{axis}紧急停止 mode=0 ret={ret}");
				return ret == 0;
			}
			catch (Exception ex) { Logger.Error($"EmergencyStop失败 轴{axis}: {ex.Message}"); return false; }
		}

		public void SetSpeed(int axis, float speed) { if (!_simulateMode && IsConnected) try { ZAux_Direct_SetSpeed(_handle, axis, speed); } catch (Exception ex) { Logger.Error("SetSpeed: " + ex.Message); } }
		public void SetAccel(int axis, float accel) { if (!_simulateMode && IsConnected) try { ZAux_Direct_SetAccel(_handle, axis, accel); } catch (Exception ex) { Logger.Error("SetAccel: " + ex.Message); } }
		public void SetDecel(int axis, float decel) { if (!_simulateMode && IsConnected) try { ZAux_Direct_SetDecel(_handle, axis, decel); } catch (Exception ex) { Logger.Error("SetDecel: " + ex.Message); } }
		public void SetLimitIn(int axis, int fwd, int rev, int datum) { if (_simulateMode || !IsConnected) return; try { ZAux_Direct_SetFwdIn(_handle, axis, fwd); ZAux_Direct_SetRevIn(_handle, axis, rev); ZAux_Direct_SetDatumIn(_handle, axis, datum); Logger.Info("限位设置: 轴" + axis + " FWD=IN" + fwd + " REV=IN" + rev + " DATUM=IN" + datum); } catch (Exception ex) { Logger.Error("SetLimitIn: " + ex.Message); } }
	public void ApplyAxisParams(CommonLib.AxisParamConfig p) { if (_simulateMode || !IsConnected) return; try { int a = p.Axis; ZAux_Direct_SetAtype(_handle, a, p.Atype); ZAux_Direct_SetUnits(_handle, a, p.Units); ZAux_Direct_SetSpeed(_handle, a, p.Speed); ZAux_Direct_SetAccel(_handle, a, p.Accel); ZAux_Direct_SetDecel(_handle, a, p.Decel); ZAux_Direct_SetLspeed(_handle, a, p.Lspeed); ZAux_Direct_SetSramp(_handle, a, p.Sramp); ZAux_Direct_SetCreep(_handle, a, p.CreepSpeed); ZAux_Direct_SetFwdIn(_handle, a, p.FwdIn); ZAux_Direct_SetRevIn(_handle, a, p.RevIn); ZAux_Direct_SetDatumIn(_handle, a, p.DatumIn); Logger.Info("轴" + a + "参数已应用: 类型=" + p.Atype + " 速度=" + p.Speed + " 限位IN" + p.FwdIn + "/" + p.RevIn + "/" + p.DatumIn); } catch (Exception ex) { Logger.Error("ApplyAxisParams: " + ex.Message); } }

		public bool SetOutput(int port, bool on)
		{
			if (_simulateMode)
			{
				Logger.Debug($"模拟模式：设置输出{port} = {on}");
				return true;
			}

			if (!IsConnected) return false;

			try
			{
				return _zmc.SetOut(_handle, port, on ? 1u : 0u);
			}
			catch (Exception ex)
			{
				Logger.Error($"SetOutput失败 端口{port}: {ex.Message}");
				return false;
			}
		}

		public bool GetInput(int port, out bool value)
		{
			value = false;

			if (_simulateMode)
			{
				// 模拟模式：随机返回状态
				value = new Random().Next(2) == 1;
				return true;
			}

			if (!IsConnected) return false;

			try
			{
				uint val = 100;
				if (_zmc.GetIn(_handle, port, ref val))
				{
					value = (val == 1);
					return true;
				}
			}
			catch (Exception ex)
			{
				Logger.Error($"GetInput失败 端口{port}: {ex.Message}");
			}
			return false;
		}

		public int GetModbusValue(int addr)
		{
			if (_simulateMode) return 1;

			if (!IsConnected) return -1;

			try
			{
				return _zmc.GetModbusValue(_handle, addr);
			}
			catch (Exception ex)
			{
				Logger.Error($"GetModbusValue失败 地址{addr}: {ex.Message}");
				return -1;
			}
		}

		public void SetModbusValue(int addr, int value)
		{
			if (_simulateMode) return;

			if (!IsConnected) return;

			try
			{
				_zmc.SetModbusValue(_handle, addr, value);
			}
			catch (Exception ex)
			{
				Logger.Error($"SetModbusValue失败 地址{addr}: {ex.Message}");
			}
		}

		public bool WaitForMoveComplete(int axis, int timeoutMs = 5000)
		{
			if (_simulateMode) return true;

			if (!IsConnected) return false;

			var sw = System.Diagnostics.Stopwatch.StartNew();
			while (sw.ElapsedMilliseconds < timeoutMs)
			{
				if (!IsMoving(axis))
					return true;
				Thread.Sleep(10);
			}

			Logger.Warning($"轴{axis}运动超时({timeoutMs}ms)");
			return false;
		}
		public void HwPulse(int outPort, int pulseWidthMs)
		{
			if (_simulateMode || !IsConnected) return;
			// 用Thread.Sleep代替SpinWait，不烧CPU，给MonitorLoop让出时间片
			try
			{
				int pw = Math.Max(1, pulseWidthMs);
				SetOutput(outPort, true);
				if (pw > 2)
					Thread.Sleep(pw - 2);  // 大段时间让出CPU，最后2ms用SpinWait精确收尾
				var sw = System.Diagnostics.Stopwatch.StartNew();
				long targetTicks = pw * System.Diagnostics.Stopwatch.Frequency / 1000;
				var spinWait = new System.Threading.SpinWait();
				while (sw.ElapsedTicks < targetTicks)
					spinWait.SpinOnce();
				SetOutput(outPort, false);
				Logger.Debug($"[HwPulse] OUT{outPort} 脉冲 {pw}ms");
			}
			catch (Exception ex)
			{
				Logger.Error($"[HwPulse] OUT{outPort} 失败: {ex.Message}");
				try { SetOutput(outPort, false); } catch { }
			}
		}

		/// <summary>开启连续插补模式（运动段之间不减速，MoveOp在段间精确触发）</summary>
		public void SetMerge(int axis, bool on)
		{
			if (_simulateMode || !IsConnected) return;
			try { ZAux_Direct_SetMerge(_handle, axis, on ? 1 : 0); }
			catch (Exception ex) { Logger.Error("SetMerge: " + ex.Message); }
		}

		/// <summary>运动到指定位置后，自动脉冲输出（硬件级，零PC延迟）</summary>
		public void MoveAbsAndPulse(int axis, float targetPos, int outPort, int pulseMs)
		{
			if (_simulateMode || !IsConnected) return;
			try
			{
				ZAux_Direct_Single_MoveAbs(_handle, axis, targetPos);
				ZAux_Direct_MoveOp2(_handle, axis, outPort, 1, pulseMs);
			}
			catch (Exception ex) { Logger.Error($"MoveAbsAndPulse 轴{axis} OUT{outPort}: {ex.Message}"); }
		}

		public int GetInMulti(int startPort, int endPort)
		{
			if (_simulateMode || !IsConnected) return 0;
			Int32 bits = 0;
			ZAux_Direct_GetInMulti(_handle, startPort, endPort, out bits);
			return bits;
		}

		/// <summary>批量设置输出端口（一次API调用设置多路）</summary>
		public void SetOutMulti(int startPort, int endPort, uint[] states)
		{
			if (_simulateMode || !IsConnected) return;
			ZAux_Direct_SetOutMulti(_handle, (ushort)startPort, (ushort)endPort, states);
		}

			/// <summary>安全锁检查: true=安全可运动, false=不安全</summary>
		public bool CheckSafetyLock(int port, bool activeHigh)
		{
			if (port <= 0 || !IsConnected) return true;
			try
			{
				uint val = 0;
				ZAux_Direct_GetIn(_handle, port, ref val);
				return activeHigh ? (val == 1) : (val == 0);
			}
			catch (Exception ex)
			{
				Logger.Error($"[安全锁] IN{port}读取失败: {ex.Message}");
				return false;
			}
		}

		// ── 心跳 ──
		private CancellationTokenSource _heartbeatCts;

		public void StartHeartbeat()
		{
			if (_simulateMode || !IsConnected) return;
			StopHeartbeat();
			_heartbeatCts = new CancellationTokenSource();
			var token = _heartbeatCts.Token;
			Task.Run(async () =>
			{
				while (!token.IsCancellationRequested)
				{
					try { ZAux_Direct_SetUserVar(_handle, "HeartBeat_Flag", 0f); }
					catch { }
					try { await Task.Delay(150, token); }
					catch { break; }
				}
			}, token);
			Logger.Info("心跳已启动 (每150ms)");
		}

		public void StopHeartbeat()
		{
			_heartbeatCts?.Cancel();
			_heartbeatCts?.Dispose();
			_heartbeatCts = null;
		}

		public bool GoHomeAll()
		{
			if (_simulateMode)
			{
				Logger.Info("模拟模式：回零完成");
				return true;
			}

			if (!IsConnected) return false;

			try
			{
				return _zmc.GoHomePlus(_handle);
			}
			catch (Exception ex)
			{
				Logger.Error($"GoHomeAll失败: {ex.Message}");
				return false;
			}
		}

		public void SetGreenLight()
		{
			if (_simulateMode) return;
			if (!IsConnected) return;
			_zmc.SetGreenLight(_handle);
		}

		public void SetRedLight(bool buzzer = false)
		{
			if (_simulateMode) return;
			if (!IsConnected) return;
			_zmc.SetRedLight(_handle, buzzer);
		}

		public void SetYellowLight()
		{
			if (_simulateMode) return;
			if (!IsConnected) return;
			_zmc.SetYellowLight(_handle);
		}

		public void ResetAlarm()
		{
			if (_simulateMode) return;
			if (!IsConnected) return;
			_zmc.ResetAlarm(_handle);
		}

		public void Disconnect()
		{
			StopHeartbeat();
			if (_simulateMode)
			{
				_connected = false;
				Logger.Info("模拟模式：运动控制卡已断开");
				return;
			}

			if (_handle != IntPtr.Zero)
			{
				try
				{
					_zmc.CloseConnect(_handle);
				}
				catch (Exception ex)
				{
					Logger.Error($"断开运动控制卡失败: {ex.Message}");
				}
				_handle = IntPtr.Zero;
			}
			_connected = false;
			Logger.Info("运动控制卡已断开");
		}
	}
}