using CommonLib;
using System;
using System.Threading;
using System.Threading.Tasks;
using VisionMeasure.Utils;using CommonLib;
using static cszmcaux.zmcaux;

namespace Hardware
{
	/// <summary>
	/// 运动控制管理器 — 封装ZMC运动控制卡的所有操作
	/// 核心职责:
	///   1. 连接/断开ZMC控制器 (通过IP地址)
	///   2. 轴运动控制: 绝对运动(MoveAbs)、定点运动(GoPosition)、停止(StopAxis)
	///   3. IO操作: 读写输入/输出端口、批量IO操作
	///   4. 安全锁: 硬件告警绑定(ALM_IN)、紧急停止(EmergencyStop)、告警清除
	///   5. 轴参数: 速度/加速度/减速度/限位设置
	///   6. 高级功能: 硬件脉冲(MoveOp2)、连续插补(SetMerge)、心跳
	/// 支持模拟模式(simulateMode=true时不调用实际硬件)
	/// </summary>
	public class MotionControlManager
	{
		private readonly takephotoVm _zmc = new takephotoVm();
		private IntPtr _handle = IntPtr.Zero;
		private readonly string _ipAddress;
		private bool _connected;
		private bool _simulateMode;
		/// <summary>连接超时时间(毫秒)，默认3000ms</summary>
		public int ConnectionTimeoutMs { get; set; } = 3000;

		/// <summary>ZMC控制器句柄(IntPtr)，供外部直接调用 ZAux_Direct_* API</summary>
		public IntPtr Handle => _handle;
		/// <summary>是否已连接: _connected=true 且 _handle != IntPtr.Zero</summary>
		public bool IsConnected => _connected && _handle != IntPtr.Zero;

		/// <summary>
		/// 构造函数
		/// </summary>
		/// <param name="ipAddress">ZMC控制器IP地址</param>
		/// <param name="simulateMode">模拟模式: true=不连接真实硬件，所有操作返回成功</param>
		public MotionControlManager(string ipAddress, bool simulateMode = true)
		{
			_ipAddress = ipAddress;
			_simulateMode = simulateMode;
		}

		/// <summary>
		/// 连接运动控制卡
		/// 模拟模式: 直接标记已连接
		/// 真实模式: Task.Run异步连接, 等待ConnectionTimeoutMs超时
		/// </summary>
		/// <returns>连接成功返回true</returns>
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

		/// <summary>初始化所有轴 (调用_zmc.Init)</summary>
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

		/// <summary>
		/// 设置硬件安全锁告警输入 — 门开时ZMC控制器自身立即急停
		/// 这是双层安全防护的硬件层: ZAux_Direct_SetAlmIn将IN端口绑定为轴的硬件告警
		/// 当IN=0时(门开)，ZMC控制器在硬件级别停止轴运动，不依赖PC CPU
		/// ★ CPU 100%时仍然有效
		/// </summary>
		/// <param name="axis">轴号</param>
		/// <param name="port">输入端口号(IN8=安全锁), port≤0则跳过</param>
		public void SetHardwareSafetyAlarm(int axis, int port)
		{
			if (_simulateMode || !IsConnected || port <= 0) return;
			try
			{
				// ALM_IN=1触发告警, IN8=1(关门)→需反转→ALM_IN=0(不告警); IN8=0(开门)→反转=1→急停
				int retAlm = ZAux_Direct_SetAlmIn(_handle, axis, port);
				int retInv = ZAux_Direct_SetInvertIn(_handle, port, 1);
				Logger.Info($"[安全锁] 硬件告警: 轴{axis}←IN{port}(反转) SetAlmIn={retAlm} SetInvertIn={retInv}");
			}
			catch (Exception ex) { Logger.Error($"[安全锁] 硬件告警设置失败: {ex.Message}"); }
		}

		/// <summary>
		/// 清除硬件告警状态 — 安全锁恢复(门关)后必须调用
		/// 先ResetAlarm清除告警标志，再SetAxisEnable(1)重新使能轴
		/// ★ 必须在重新MoveAbs之前调用，否则轴无法运动
		/// </summary>
		/// <param name="axis">轴号</param>
		public void ClearHardwareAlarm(int axis)
		{
			if (_simulateMode || !IsConnected) return;
			try
			{
				_zmc.ResetAlarm(_handle);
				ZAux_Direct_SetAxisEnable(_handle, axis, 1);
				Logger.Debug($"[安全锁] 硬件告警已清除 轴{axis}");
			}
			catch (Exception ex) { Logger.Error($"[安全锁] 清除告警失败: {ex.Message}"); }
		}

		/// <summary>绝对运动: 轴移动到指定绝对位置</summary>
		/// <param name="axis">轴号</param>
		/// <param name="position">目标位置</param>
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

		/// <summary>定点运动: 与MoveAbs类似，走ZMC内部封装的GoPosition</summary>
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

		/// <summary>获取轴当前位置(DPOS)</summary>
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

		/// <summary>检查轴是否正在运动</summary>
		/// <returns>运动中返回true，空闲或模拟模式返回false</returns>
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

		/// <summary>普通停止轴运动(减速停止)</summary>
		public bool StopAxis(int axis)
		{
			if (_simulateMode) return true;
			if (!IsConnected) return false;
			try { return _zmc.StopMove(_handle, axis); }
			catch (Exception ex) { Logger.Error($"StopAxis失败 轴{axis}: {ex.Message}"); return false; }
		}

		/// <summary>
		/// 紧急停止 — 安全锁专用(mode=0=立即停止, 无减速)
		/// 对比 StopAxis(减速停止)，此方法使用 ZAux_Direct_Single_Cancel(mode=0)
		/// mode=0: 立即停止(刹车) | mode=2: 减速停止(正常停止)
		/// ★ 安全锁触发时必须用此方法，确保轴在最短时间内停止
		/// </summary>
		/// <param name="axis">轴号</param>
		/// <returns>成功返回true</returns>
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

		/// <summary>设置轴运行速度</summary>
		public void SetSpeed(int axis, float speed) { if (!_simulateMode && IsConnected) try { ZAux_Direct_SetSpeed(_handle, axis, speed); } catch (Exception ex) { Logger.Error("SetSpeed: " + ex.Message); } }
		/// <summary>设置轴加速度</summary>
		public void SetAccel(int axis, float accel) { if (!_simulateMode && IsConnected) try { ZAux_Direct_SetAccel(_handle, axis, accel); } catch (Exception ex) { Logger.Error("SetAccel: " + ex.Message); } }
		/// <summary>设置轴减速度</summary>
		public void SetDecel(int axis, float decel) { if (!_simulateMode && IsConnected) try { ZAux_Direct_SetDecel(_handle, axis, decel); } catch (Exception ex) { Logger.Error("SetDecel: " + ex.Message); } }
		/// <summary>设置轴限位IO: 正限/负限/原点分别绑定到指定IN端口</summary>
		public void SetLimitIn(int axis, int fwd, int rev, int datum) { if (_simulateMode || !IsConnected) return; try { ZAux_Direct_SetFwdIn(_handle, axis, fwd); ZAux_Direct_SetRevIn(_handle, axis, rev); ZAux_Direct_SetDatumIn(_handle, axis, datum); Logger.Info("限位设置: 轴" + axis + " FWD=IN" + fwd + " REV=IN" + rev + " DATUM=IN" + datum); } catch (Exception ex) { Logger.Error("SetLimitIn: " + ex.Message); } }
		/// <summary>批量应用轴参数(类型/脉冲当量/速度/加速度/限位等)，从AxisParamConfig读取</summary>
		public void ApplyAxisParams(CommonLib.AxisParamConfig p) { if (_simulateMode || !IsConnected) return; try { int a = p.Axis; ZAux_Direct_SetAtype(_handle, a, p.Atype); ZAux_Direct_SetUnits(_handle, a, p.Units); ZAux_Direct_SetSpeed(_handle, a, p.Speed); ZAux_Direct_SetAccel(_handle, a, p.Accel); ZAux_Direct_SetDecel(_handle, a, p.Decel); ZAux_Direct_SetLspeed(_handle, a, p.Lspeed); ZAux_Direct_SetSramp(_handle, a, p.Sramp); ZAux_Direct_SetCreep(_handle, a, p.CreepSpeed); ZAux_Direct_SetFwdIn(_handle, a, p.FwdIn); ZAux_Direct_SetRevIn(_handle, a, p.RevIn); ZAux_Direct_SetDatumIn(_handle, a, p.DatumIn); Logger.Info("轴" + a + "参数已应用: 类型=" + p.Atype + " 速度=" + p.Speed + " 限位IN" + p.FwdIn + "/" + p.RevIn + "/" + p.DatumIn); } catch (Exception ex) { Logger.Error("ApplyAxisParams: " + ex.Message); } }

		/// <summary>设置单个输出端口</summary>
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

		/// <summary>读取单个输入端口</summary>
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

		/// <summary>读取Modbus地址的值</summary>
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

		/// <summary>写入Modbus地址的值</summary>
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

		/// <summary>等待轴运动完成(带超时)</summary>
		/// <param name="axis">轴号</param>
		/// <param name="timeoutMs">超时时间(毫秒)，默认5000</param>
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

		/// <summary>硬件脉冲输出: 拉高→延时→拉低，用于触发外部设备</summary>
		/// <param name="outPort">输出端口</param>
		/// <param name="pulseWidthMs">脉冲宽度(毫秒)</param>
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

		/// <summary>
		/// 开启/关闭连续插补模式 — 运动段之间不减速
		/// 用于侧面工位连续运动拍照时，保证匀速经过所有拍照点
		/// MoveOp2在段间精确触发(硬件级，零PC延迟)
		/// </summary>
		/// <param name="axis">轴号</param>
		/// <param name="on">true=开启连续插补</param>
		public void SetMerge(int axis, bool on)
		{
			if (_simulateMode || !IsConnected) return;
			try { ZAux_Direct_SetMerge(_handle, axis, on ? 1 : 0); }
			catch (Exception ex) { Logger.Error("SetMerge: " + ex.Message); }
		}

		/// <summary>
		/// 运动到指定位置后自动脉冲输出 — 硬件级，零PC延迟
		/// 先发MoveAbs指令，再用MoveOp2绑定输出脉冲
		/// ★ 此方法由ZMC控制器内部执行，到达位置后自动触发脉冲，不受PC延迟影响
		/// </summary>
		/// <param name="axis">轴号</param>
		/// <param name="targetPos">目标位置</param>
		/// <param name="outPort">输出端口(相机触发)</param>
		/// <param name="pulseMs">脉冲宽度(ms)</param>
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

		/// <summary>
		/// 批量读取输入端口 — 一次API调用读取 startPort~endPort 所有端口状态
		/// 返回int位掩码: bit0=IN(startPort), bit1=IN(startPort+1), ...
		/// ★ CameraTriggerManager.MonitorLoop使用此方法高频扫描(2000Hz)，避免逐个GetInput竞争ZMC
		/// </summary>
		/// <param name="startPort">起始端口号</param>
		/// <param name="endPort">结束端口号</param>
		/// <returns>位掩码，bit0对应startPort</returns>
		public int GetInMulti(int startPort, int endPort)
		{
			if (_simulateMode || !IsConnected) return 0;
			Int32 bits = 0;
			ZAux_Direct_GetInMulti(_handle, startPort, endPort, out bits);
			return bits;
		}

		/// <summary>
		/// 批量设置输出端口 — 一次API调用设置多路输出
		/// 用于CameraTriggerManager同时触发多个相机的光源
		/// </summary>
		/// <param name="startPort">起始端口号</param>
		/// <param name="endPort">结束端口号</param>
		/// <param name="states">状态数组(位掩码)</param>
		public void SetOutMulti(int startPort, int endPort, uint[] states)
		{
			if (_simulateMode || !IsConnected) return;
			ZAux_Direct_SetOutMulti(_handle, (ushort)startPort, (ushort)endPort, states);
		}

		/// <summary>
		/// 安全锁检查 — 直接读取硬件IO端口状态
		/// 这是双层安全防护的软件层，每5ms轮询
		/// ★ 硬件层(ZMC ALM_IN)保证CPU 100%时也能急停，软件层负责恢复逻辑
		/// </summary>
		/// <param name="port">输入端口号(IN8=安全锁)，port≤0则跳过检查返回true</param>
		/// <param name="activeHigh">true=高电平有效(IN8=1→安全)，false=低电平有效</param>
		/// <returns>true=安全可运动，false=不安全(门开)</returns>
		public bool CheckSafetyLock(int port, bool activeHigh)
		{
			if (port <= 0 || !IsConnected) return true;
			try
			{
				uint val = 0;
				ZAux_Direct_GetIn(_handle, port, ref val);
				bool safe = activeHigh ? (val == 1) : (val == 0);
				return safe;
			}
			catch (Exception ex)
			{
				Logger.Error($"[安全锁] IN{port}读取失败: {ex.Message}");
				return false;  // 读取失败视为不安全，禁止运动
			}
		}

		// ── 心跳 ──
		private CancellationTokenSource _heartbeatCts;

		/// <summary>
		/// 启动心跳 — 每150ms写一次ZMC用户变量HeartBeat_Flag
		/// ZMC BASIC程序监控此标志检测PC是否存活(超时未更新→触发安全逻辑)
		/// </summary>
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

		/// <summary>停止心跳</summary>
		public void StopHeartbeat()
		{
			_heartbeatCts?.Cancel();
			_heartbeatCts?.Dispose();
			_heartbeatCts = null;
		}

		/// <summary>所有轴回零</summary>
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

		/// <summary>设置绿灯</summary>
		public void SetGreenLight()
		{
			if (_simulateMode) return;
			if (!IsConnected) return;
			_zmc.SetGreenLight(_handle);
		}

		/// <summary>设置红灯(可选蜂鸣器)</summary>
		public void SetRedLight(bool buzzer = false)
		{
			if (_simulateMode) return;
			if (!IsConnected) return;
			_zmc.SetRedLight(_handle, buzzer);
		}

		/// <summary>设置黄灯</summary>
		public void SetYellowLight()
		{
			if (_simulateMode) return;
			if (!IsConnected) return;
			_zmc.SetYellowLight(_handle);
		}

		/// <summary>复位控制器告警</summary>
		public void ResetAlarm()
		{
			if (_simulateMode) return;
			if (!IsConnected) return;
			_zmc.ResetAlarm(_handle);
		}

		/// <summary>断开运动控制卡连接(停止心跳+关闭连接)</summary>
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