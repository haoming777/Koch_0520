using System;
using System.Diagnostics;
using System.Threading;
using static cszmcaux.zmcaux;

namespace CommonLib
{
	/// <summary>轴回零(初始化)结果码</summary>
	public enum AxisHomingCode
	{
		Success,        // 回零成功, MODBUS[100]已置1
		NotConnected,   // 控制卡未连接/句柄无效/连接中途断开
		LimitMismatch,  // 限位IO与AxisParams.json不一致
		NotEnabled,     // 伺服未使能
		SafetyNotReady, // 安全锁未释放(不等待模式)
		SafetyAborted,  // 等待安全锁期间连接断开
		SafetyDoorOpen, // 回零过程中安全门打开, 已软件急停中止
		AlarmBefore,    // 回零前已ALM告警
		DatumFailed,    // Single_Datum返回非0
		AlarmDuring,    // 回零过程中ALM
		Timeout,        // 30s未完成
		Aborted,        // 按[停止回零]中止
		Exception       // 其他异常
	}

	/// <summary>轴回零(初始化)结果</summary>
	public class AxisHomingResult
	{
		public AxisHomingCode Code;
		public string Message = "";
		public int Axis;
		public int DatumMode;
		public int DatumRet;
		public int ModbusReadback = -1;
		public long ElapsedMs;
		public bool IsSuccess { get { return Code == AxisHomingCode.Success; } }
	}

	/// <summary>
	/// 轴回零(初始化)统一实现 — PLC监控界面[开始回零]与主程序启动检查共用
	/// 流程: 限位检查 → 使能检查 → 安全锁 → ALM检查 → MODBUS[100]=0 → 写回零参数(含模式)
	///       → ZAux_Direct_Single_Datum → 等待完成(30s, 每20ms监测安全门/ALM/中止/断线) → MPOS/DPOS清零 → MODBUS[100]=1 → 回读验证
	/// 回零模式(DatumMode)、限位、安全锁等参数取自 AxisParamConfig(Config/AxisParams.json)
	/// </summary>
	public static class AxisHomingService
	{
		/// <summary>初始化完成标志寄存器 MODBUS[100]: 1=轴已初始化</summary>
		public const ushort InitFlagReg = 100;

		/// <summary>读取初始化标志: 仅当 MODBUS[100]==1 返回true(读失败返回false)</summary>
		public static bool IsInitialized(IntPtr handle)
		{
			try
			{
				if (handle == IntPtr.Zero) return false;
				ushort[] mbArr = { 0 };
				ZAux_Modbus_Get4x(handle, InitFlagReg, 1, mbArr);
				return mbArr[0] == 1;
			}
			catch { return false; }
		}

		/// <summary>
		/// 执行轴回零. log=日志回调(可为null); isConnected=连接检测回调;
		/// waitSafety=true: 安全锁未释放时阻塞等待(调试界面); false: 直接失败返回(启动自动初始化, 不卡界面)
		/// abortRequested=中止检测回调(停止回零按钮), 返回true时中止, 不清零坐标也不写完成标志
		/// </summary>
		public static AxisHomingResult Home(IntPtr handle, AxisParamConfig cfg, int axis,
			Action<string> log = null, Func<bool> isConnected = null, bool waitSafety = true,
			Func<bool> abortRequested = null)
		{
			var result = new AxisHomingResult { Axis = axis, DatumMode = cfg != null ? cfg.DatumMode : 14 };
			Action<string> L = log;
			if (L == null) L = m => { };
			Func<bool> alive = isConnected;
			if (alive == null) alive = () => true;
			var sw = Stopwatch.StartNew();

			if (handle == IntPtr.Zero)
				return Fail(result, AxisHomingCode.NotConnected, "控制卡未连接");
			if (cfg == null)
				return Fail(result, AxisHomingCode.NotConnected, "轴参数为空(AxisParamConfig)");

			int mode = cfg.DatumMode >= 0 ? cfg.DatumMode : 14;
			result.DatumMode = mode;

			try
			{
				// 1. 读取限位IO并核对
				int fwdIn = 0, revIn = 0, datumIn = 0;
				ZAux_Direct_GetFwdIn(handle, axis, ref fwdIn);
				ZAux_Direct_GetRevIn(handle, axis, ref revIn);
				ZAux_Direct_GetDatumIn(handle, axis, ref datumIn);
				L($"[回零-步骤1] 读取限位: FwdIn={fwdIn} RevIn={revIn} DatumIn={datumIn} 期望={cfg.FwdIn}/{cfg.RevIn}/{cfg.DatumIn}");
				if (fwdIn != cfg.FwdIn || revIn != cfg.RevIn || datumIn != cfg.DatumIn)
					return Fail(result, AxisHomingCode.LimitMismatch,
						$"限位不一致! FwdIn={fwdIn}(期望{cfg.FwdIn}) RevIn={revIn}(期望{cfg.RevIn}) DatumIn={datumIn}(期望{cfg.DatumIn})\n请先点击[参数下发]设置限位后再回零");
				L("[回零-步骤1] 限位检查通过 ✓");

				// 2. 使能检查
				int enable = 0;
				ZAux_Direct_GetAxisEnable(handle, axis, ref enable);
				if (enable != 1)
					return Fail(result, AxisHomingCode.NotEnabled, $"轴{axis} 未使能(伺服OFF), 请先点击[伺服ON]");
				L("[回零-步骤2] 使能检查通过 ✓");

				// 3. 安全锁
				int port = cfg.SafetyLockPort;
				if (port > 0)
				{
					bool activeHigh = cfg.SafetyLockActiveHigh;
					uint val = 0;
					ZAux_Direct_GetIn(handle, port, ref val);
					bool safe = activeHigh ? (val == 1) : (val == 0);
					if (!safe && !waitSafety)
						return Fail(result, AxisHomingCode.SafetyNotReady, $"安全锁未释放(IN{port}), 初始化中止");
					if (!safe)
					{
						L($"[回零] 安全锁触发 IN{port}, 等待释放...");
						while (!safe)
						{
							if (!alive()) return Fail(result, AxisHomingCode.SafetyAborted, "安全锁未释放或连接断开, 初始化中止");
							Thread.Sleep(20);
							ZAux_Direct_GetIn(handle, port, ref val);
							safe = activeHigh ? (val == 1) : (val == 0);
						}
						L($"[回零] 安全锁已释放 IN{port}, 继续回零");
					}
					L("[回零-步骤3] 安全锁检查通过 ✓");
				}

				// 3.5 复核硬件告警绑定(防呆): 门输入→轴ALM硬件急停映射被改动时, 回零期间仍保留硬件级保护
				if (port > 0)
				{
					int retAlm = ZAux_Direct_SetAlmIn(handle, axis, port);
					int retInv = ZAux_Direct_SetInvertIn(handle, port, 1);
					L($"[回零-步骤3] 硬件告警复核: 轴{axis}←IN{port}(反相) SetAlmIn={retAlm} SetInvertIn={retInv}");
				}

				// 4. 回零前状态(调试用) + ALM检查
				float dposB4 = 0, mposB4 = 0;
				int idleB4 = 0, enableB4 = 0, almSts = 0;
				ZAux_Direct_GetDpos(handle, axis, ref dposB4);
				ZAux_Direct_GetMpos(handle, axis, ref mposB4);
				ZAux_Direct_GetIfIdle(handle, axis, ref idleB4);
				ZAux_Direct_GetAxisEnable(handle, axis, ref enableB4);
				ZAux_Direct_GetAxisStatus(handle, axis, ref almSts);
				if ((almSts & 1) != 0)
					return Fail(result, AxisHomingCode.AlarmBefore, "轴处于ALM告警状态! 请先点击[报警清除]按钮");
				L($"[回零-步骤4] 回零前状态: DPOS={dposB4:F2} MPOS={mposB4:F2} Idle={idleB4} Enable={enableB4}");

				// 5. 重置初始化标志(开始前先清零, 防止上次异常残留)
				ZAux_Modbus_Set4x(handle, InitFlagReg, (ushort)1, new ushort[] { 0 });
				L("[回零-步骤5] 重置 MODBUS[100]=0 (开始初始化)");

				// 6. 写回零参数(与旧版一致: Atype65/Units13107.2/速度40/爬行1; 回零模式取自AxisParams.json)
				ZAux_Direct_SetAtype(handle, axis, 65);
				ZAux_Direct_SetUnits(handle, axis, 13107.2f);
				ZAux_Direct_SetSpeed(handle, axis, 40f);
				ZAux_Direct_SetAccel(handle, axis, 1000f);
				ZAux_Direct_SetDecel(handle, axis, 1000f);
				ZAux_Direct_SetLspeed(handle, axis, 10f);
				ZAux_Direct_SetSramp(handle, axis, 0f);
				float creep = 1f;
				int retCreep = ZAux_Direct_SetCreep(handle, axis, creep);
				L($"[回零-步骤6] 设置参数: mode={mode} creep={creep} SetCreep ret={retCreep}");

				// 7. 执行回零
				int retDatum = (int)ZAux_Direct_Single_Datum(handle, axis, mode);
				result.DatumRet = retDatum;
				L($"[回零-步骤7] ZAux_Direct_Single_Datum(handle,{axis},{mode}) ret={retDatum}");
				if (retDatum != 0)
					return Fail(result, AxisHomingCode.DatumFailed, $"轴{axis} 回零失败 ret={retDatum}");

				// 8. 轮询等待回零完成(最多30秒, 每20ms监测安全门/ALM/中止/断线)
				L("[回零-步骤8] 等待回零完成...");
				var swWait = Stopwatch.StartNew();
				bool homeOk = false;
				bool doorActiveHigh = cfg.SafetyLockActiveHigh;
				while (swWait.ElapsedMilliseconds < 30000)
				{
					if (abortRequested != null && abortRequested())
						return Fail(result, AxisHomingCode.Aborted, "回零已中止(停止回零)");
					if (!alive())
						return Fail(result, AxisHomingCode.NotConnected, "连接断开, 回零中止");

					// 安全门轮询(软件兜底): 门开 → 立即取消回零运动 + 中止(硬件AlmIn映射失效时也不会带门运行)
					if (port > 0)
					{
						uint dv = 0;
						ZAux_Direct_GetIn(handle, port, ref dv);
						bool doorSafe = doorActiveHigh ? (dv == 1) : (dv == 0);
						if (!doorSafe)
						{
							ZAux_Direct_Single_Cancel(handle, axis, 2);  // mode2=取消当前+缓冲运动(与[停止回零]一致)
							float stopPos = 0;
							ZAux_Direct_GetDpos(handle, axis, ref stopPos);
							L($"[回零-步骤8] 安全门打开(IN{port}={dv})! 已急停轴{axis} 位置={stopPos:F2}, 回零中止");
							return Fail(result, AxisHomingCode.SafetyDoorOpen,
								$"安全门打开(IN{port}), 回零已急停中止; 请关门并清除报警(PLC监控页)后重新回零");
						}
					}

					int idle = 0, sts = 0;
					ZAux_Direct_GetIfIdle(handle, axis, ref idle);
					ZAux_Direct_GetAxisStatus(handle, axis, ref sts);
					if ((sts & 1) != 0)
					{
						L($"[回零-步骤8] 回零中检测到ALM(0x{sts:X4}), 中止!");
						return Fail(result, AxisHomingCode.AlarmDuring, $"轴{axis} 回零过程中ALM告警! 请先清除报警后重试");
					}
					if (idle == -1) { homeOk = true; break; }
					Thread.Sleep(20);
				}
				if (!homeOk)
					return Fail(result, AxisHomingCode.Timeout, $"轴{axis} 回零超时, 请检查轴状态");
				L($"[回零-步骤8] 回零完成 ✓ 耗时={swWait.ElapsedMilliseconds}ms");

				// 9. 清零MPOS/DPOS
				int mposRet = ZAux_Direct_SetMpos(handle, axis, 0f);
				int dposRet = ZAux_Direct_SetDpos(handle, axis, 0f);
				L($"[回零-步骤9] SetMpos(0) ret={mposRet}, SetDpos(0) ret={dposRet}");

				// 10. 写入初始化完成标志
				int setRet = (int)ZAux_Modbus_Set4x(handle, InitFlagReg, (ushort)1, new ushort[] { 1 });
				Thread.Sleep(100); // 等待写入生效
				L($"[回零-步骤10] MODBUS[100]=1 ret={setRet}");

				// 11. 回读验证
				try { ushort[] mbVrf = { 0 }; ZAux_Modbus_Get4x(handle, InitFlagReg, 1, mbVrf); result.ModbusReadback = mbVrf[0]; } catch { }
				float dposAfter = 0, mposAfter = 0;
				ZAux_Direct_GetDpos(handle, axis, ref dposAfter);
				ZAux_Direct_GetMpos(handle, axis, ref mposAfter);
				L($"[回零-步骤11] 验证: MODBUS[100]={result.ModbusReadback} DPOS={dposAfter:F2} MPOS={mposAfter:F2}");

				result.Code = AxisHomingCode.Success;
				result.Message = $"轴{axis} 初始化完成 (MODBUS[100]={result.ModbusReadback}, 模式{mode})";
				result.ElapsedMs = sw.ElapsedMilliseconds;
				return result;
			}
			catch (Exception ex)
			{
				L($"[回零-异常] {ex.Message}\n{ex.StackTrace}");
				return Fail(result, AxisHomingCode.Exception, $"回零异常: {ex.Message}");
			}
		}

		private static AxisHomingResult Fail(AxisHomingResult r, AxisHomingCode code, string msg)
		{
			r.Code = code;
			r.Message = msg;
			return r;
		}
	}
}
