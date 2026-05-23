// ZmcUniversalController.cs - 通用运动控制库（兼容脉冲和总线）
using System;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using cszmcaux;

namespace ZmcUniversalLib
{
	/// <summary>
	/// 控制器类型
	/// </summary>
	public enum ControllerType
	{
		Pulse,      // 脉冲型控制器（本地脉冲轴）
		BusEtherCAT, // EtherCAT总线
		BusRTEX     // RTEX总线
	}

	/// <summary>
	/// 轴控制模式
	/// </summary>
	public enum AxisControlMode
	{
		Position = 0,  // 位置模式
		Speed = 1,     // 速度模式
		Torque = 2     // 力矩模式
	}

	/// <summary>
	/// 轴配置
	/// </summary>
	public class AxisConfig
	{
		public int AxisNum { get; set; }
		public int AxisType { get; set; }          // 轴类型
		public bool IsBusAxis { get; set; }      // 是否为总线轴
		public int BusNodeNum { get; set; }      // 总线节点号（总线轴时使用）
		public float Units { get; set; } = 1;     // 脉冲当量
		public float Speed { get; set; } = 100;   // 速度
		public float Accel { get; set; } = 1000;  // 加速度
		public float Decel { get; set; } = 1000;  // 减速度
		public float FastDec { get; set; } = 10000;// 快减速
		public float Sramp { get; set; } = 0;     // S曲线时间
		public int HomeMode { get; set; } = 3;    // 回零模式
		public float HomeSpeed { get; set; } = 50;     // 回零高速
		public float HomeCreep { get; set; } = 10;     // 回零低速
		public float HomeOffset { get; set; } = 0;     // 回零偏移
		public int DatumIn { get; set; } = -1;    // 原点信号IO（-1=无效）
		public int FwdIn { get; set; } = -1;      // 正限位IO
		public int RevIn { get; set; } = -1;      // 负限位IO
	}

	/// <summary>
	/// 通用运动控制器
	/// 支持脉冲轴和总线轴（EtherCAT/RTEX）的混合控制
	/// </summary>
	public class ZmcUniversalController : IDisposable
	{
		#region 字段
		private IntPtr _handle = IntPtr.Zero;
		private ControllerType _ctrlType;
		private bool _isConnected = false;
		private bool _busInitialized = false;
		private AxisConfig[] _axisConfigs;
		private int _maxAxes = 0;
		#endregion

		#region 属性
		public IntPtr Handle => _handle;
		public bool IsConnected => _isConnected && _handle != IntPtr.Zero;
		public bool IsBusInitialized => _busInitialized;
		public ControllerType CtrlType => _ctrlType;
		#endregion

		#region 连接
		/// <summary>
		/// 通用连接（自动判断类型）
		/// </summary>
		public int Connect(string connectStr, int type = 2)
		{
			if (_handle != IntPtr.Zero) Disconnect();

			int ret = zmcaux.ZAux_FastOpen(type, connectStr, 1000, out _handle);
			if (ret == 0)
			{
				_isConnected = true;

				// 读取控制器信息判断类型
				var info = GetControllerInfo();
				if (info.SoftType.Contains("ECAT") || info.SoftType.Contains("EtherCAT"))
					_ctrlType = ControllerType.BusEtherCAT;
				else if (info.SoftType.Contains("RTEX"))
					_ctrlType = ControllerType.BusRTEX;
				else
					_ctrlType = ControllerType.Pulse;

				// 获取最大轴数
				RefreshMaxAxes();
			}
			return ret;
		}

		/// <summary>
		/// 断开连接
		/// </summary>
		public void Disconnect()
		{
			if (_handle != IntPtr.Zero)
			{
				zmcaux.ZAux_Close(_handle);
				_handle = IntPtr.Zero;
			}
			_isConnected = false;
			_busInitialized = false;
		}
		#endregion

		#region 总线初始化
		/// <summary>
		/// 总线初始化（仅总线控制器需要）
		/// 脉冲控制器调用此方法不会有任何影响
		/// </summary>
		public async Task<bool> BusInitAsync(string basFilePath = null, int timeoutMs = 30000)
		{
			EnsureConnected();

			// 如果没有总线轴配置，跳过
			bool hasBusAxis = false;
			if (_axisConfigs != null)
			{
				foreach (var cfg in _axisConfigs)
					if (cfg.IsBusAxis) { hasBusAxis = true; break; }
			}

			if (!hasBusAxis && _ctrlType == ControllerType.Pulse)
			{
				_busInitialized = true;
				return true;
			}

			try
			{
				if (!string.IsNullOrEmpty(basFilePath))
				{
					// 方式1：使用BAS文件初始化
					int ret = zmcaux.ZAux_BasDown(_handle, basFilePath, 0);
					if (ret != 0) return false;
				}
				else
				{
					// 方式2：使用Zmotion Tools配置好的参数
					int ret = zmcaux.ZAux_BusCmd_InitBus(_handle);
					if (ret != 0) return false;
				}

				// 等待初始化完成
				int waited = 0;
				while (waited < timeoutMs)
				{
					int status = -1;
					zmcaux.ZAux_BusCmd_GetInitStatus(_handle, ref status);
					if (status == 1)
					{
						_busInitialized = true;
						return true;
					}
					await Task.Delay(200);
					waited += 200;
				}
				return false;
			}
			catch
			{
				return false;
			}
		}
		#endregion
		/// <summary>
		/// 获取控制卡回零状态
		/// </summary>
		public uint GetHomeStatus(int axis)
		{
			EnsureConnected();
			uint status = 0;
			zmcaux.ZAux_Direct_GetHomeStatus(_handle, axis, ref status);
			return status;
		}

		/// <summary>
		/// 获取总线驱动器回零状态
		/// </summary>
		public uint GetBusHomeStatus(int axis)
		{
			EnsureConnected();
			uint status = 0;
			zmcaux.ZAux_BusCmd_GetHomeStatus(_handle, (uint)axis, ref status);
			return status;
		}
		#region 轴配置（通用）
		/// <summary>
		/// 配置轴参数（自动判断脉冲/总线）
		/// </summary>
		public int ConfigureAxis(AxisConfig config)
		{
			EnsureConnected();
			int ret = 0;

			if (config.IsBusAxis || config.AxisType >= 65)
			{
				// 总线轴：先设置地址，再设置类型
				int address = config.BusNodeNum + 1;
				ret += zmcaux.ZAux_Direct_SetAxisAddress(_handle, config.AxisNum, address);
				System.Threading.Thread.Sleep(10);
				ret += zmcaux.ZAux_Direct_SetAtype(_handle, config.AxisNum, config.AxisType);
			}
			else
			{
				// 脉冲轴：直接设置类型
				ret += zmcaux.ZAux_Direct_SetAtype(_handle, config.AxisNum, config.AxisType);
			}

			// 其他参数
			ret += zmcaux.ZAux_Direct_SetUnits(_handle, config.AxisNum, config.Units);
			ret += zmcaux.ZAux_Direct_SetSpeed(_handle, config.AxisNum, config.Speed);
			ret += zmcaux.ZAux_Direct_SetAccel(_handle, config.AxisNum, config.Accel);
			ret += zmcaux.ZAux_Direct_SetDecel(_handle, config.AxisNum, config.Decel);
			ret += zmcaux.ZAux_Direct_SetFastDec(_handle, config.AxisNum, config.FastDec);
			ret += zmcaux.ZAux_Direct_SetSramp(_handle, config.AxisNum, config.Sramp);

			// 限位参数（-1表示不设置）
			if (config.FwdIn >= 0)
				ret += zmcaux.ZAux_Direct_SetFwdIn(_handle, config.AxisNum, config.FwdIn);
			if (config.RevIn >= 0)
				ret += zmcaux.ZAux_Direct_SetRevIn(_handle, config.AxisNum, config.RevIn);
			if (config.DatumIn >= 0)
				ret += zmcaux.ZAux_Direct_SetDatumIn(_handle, config.AxisNum, config.DatumIn);

			return ret;
		}

		// 在 ZmcUniversalController.cs 中添加
		/// <summary>
		/// 读取输出口状态（通用）
		/// </summary>
		public uint ReadOut(int ioNum)
		{
			EnsureConnected();
			uint value = 0;
			zmcaux.ZAux_Direct_GetOp(_handle, ioNum, ref value);
			return value;
		}

		/// <summary>
		/// 批量配置多轴
		/// </summary>
		public int ConfigureAxes(AxisConfig[] configs)
		{
			_axisConfigs = configs;
			int ret = 0;
			foreach (var cfg in configs)
				ret += ConfigureAxis(cfg);
			return ret;
		}
		#endregion

		#region 通用参数设置
		public int SetUnits(int axis, float value)
		{
			EnsureConnected();
			return zmcaux.ZAux_Direct_SetUnits(_handle, axis, value);
		}

		public int SetSpeed(int axis, float value)
		{
			EnsureConnected();
			return zmcaux.ZAux_Direct_SetSpeed(_handle, axis, value);
		}

		public int SetAccel(int axis, float value)
		{
			EnsureConnected();
			return zmcaux.ZAux_Direct_SetAccel(_handle, axis, value);
		}

		public int SetDecel(int axis, float value)
		{
			EnsureConnected();
			return zmcaux.ZAux_Direct_SetDecel(_handle, axis, value);
		}

		public int SetFastDec(int axis, float value)
		{
			EnsureConnected();
			return zmcaux.ZAux_Direct_SetFastDec(_handle, axis, value);
		}

		public int SetSramp(int axis, float value)
		{
			EnsureConnected();
			return zmcaux.ZAux_Direct_SetSramp(_handle, axis, value);
		}

		public float GetSpeed(int axis)
		{
			EnsureConnected();
			float val = 0;
			zmcaux.ZAux_Direct_GetSpeed(_handle, axis, ref val);
			return val;
		}
		#endregion



		#region 通用位置操作
		public int SetDpos(int axis, float value)
		{
			EnsureConnected();
			return zmcaux.ZAux_Direct_SetDpos(_handle, axis, value);
		}

		public float GetDpos(int axis)
		{
			EnsureConnected();
			float val = 0;
			zmcaux.ZAux_Direct_GetDpos(_handle, axis, ref val);
			return val;
		}



		public float GetMpos(int axis)
		{
			EnsureConnected();
			float val = 0;
			zmcaux.ZAux_Direct_GetMpos(_handle, axis, ref val);
			return val;
		}

		public int ClearPosition(int axis)
		{
			EnsureConnected();
			int ret = SetDpos(axis, 0);
			ret += SetMpos(axis, 0);
			return ret;
		}
		/// <summary>
		/// 设置 MPOS（编码器反馈位置）
		/// </summary>
		public int SetMpos(int axis, float value)
		{
			EnsureConnected();
			return zmcaux.ZAux_Direct_SetMpos(_handle, axis, value);
		}

		/// <summary>
		/// 使用 SetParam 方式设置参数
		/// </summary>
		public int SetParam(string paramName, int axis, float value)
		{
			EnsureConnected();
			return zmcaux.ZAux_Direct_SetParam(_handle, paramName, axis, value);
		}

		/// <summary>
		/// 清除编码器反馈位置
		/// </summary>
		public int ClearMpos(int axis)
		{
			EnsureConnected();
			return zmcaux.ZAux_Direct_SetParam(_handle, "MPOS", axis, 0);
		}

		/// <summary>
		/// 清除命令位置
		/// </summary>
		public int ClearDpos(int axis)
		{
			EnsureConnected();
			return zmcaux.ZAux_Direct_SetParam(_handle, "DPOS", axis, 0);
		}

		/// <summary>
		/// 清零所有轴位置
		/// </summary>
		public int ClearAllPositions(int maxAxes = 4)
		{
			EnsureConnected();
			int ret = 0;
			for (int i = 0; i < maxAxes; i++)
			{
				ret += zmcaux.ZAux_Direct_SetParam(_handle, "DPOS", i, 0);
				ret += zmcaux.ZAux_Direct_SetParam(_handle, "MPOS", i, 0);
			}
			return ret;
		}
		#endregion

		#region 通用运动控制

		/// <summary>
		/// 相对运动（脉冲和总线通用）
		/// </summary>
		public int MoveRelative(int axis, float distance)
		{
			EnsureConnected();
			return zmcaux.ZAux_Direct_Single_Move(_handle, axis, distance);
		}

		/// <summary>
		/// 绝对运动（脉冲和总线通用）
		/// </summary>
		public int MoveAbsolute(int axis, float position)
		{
			EnsureConnected();
			return zmcaux.ZAux_Direct_Single_MoveAbs(_handle, axis, position);
		}

		/// <summary>
		/// 持续运动（脉冲和总线通用）
		/// </summary>
		public int MoveContinuous(int axis, int direction)
		{
			EnsureConnected();
			return zmcaux.ZAux_Direct_Single_Vmove(_handle, axis, direction);
		}

		/// <summary>
		/// 停止轴（脉冲和总线通用）
		/// </summary>
		public int StopAxis(int axis, int mode = 2)
		{
			EnsureConnected();
			return zmcaux.ZAux_Direct_Single_Cancel(_handle, axis, mode);
		}

		/// <summary>
		/// 停止所有轴
		/// </summary>
		public int StopAll(int mode = 2)
		{
			EnsureConnected();
			return zmcaux.ZAux_Direct_Rapidstop(_handle, mode);
		}
		#endregion

		#region 通用回零
		///// <summary>
		///// 回零（自动判断脉冲/总线方式）
		///// </summary>
		//public int Home(int axis, AxisConfig config = null)
		//{
		//	EnsureConnected();

		//	if (config == null && _axisConfigs != null)
		//	{
		//		// 从配置中查找
		//		foreach (var cfg in _axisConfigs)
		//			if (cfg.AxisNum == axis) { config = cfg; break; }
		//	}

		//	if (config != null && config.IsBusAxis)
		//	{
		//		// 总线轴：使用驱动器回零
		//		SetSpeed(axis, config.HomeSpeed);
		//		SetCreep(axis, config.HomeCreep);
		//		zmcaux.ZAux_BusCmd_SetDatumOffpos(_handle, (uint)axis, config.HomeOffset);
		//		return zmcaux.ZAux_BusCmd_Datum(_handle, (uint)axis, (uint)config.HomeMode);
		//	}
		//	else
		//	{
		//		// 脉冲轴：使用控制器回零
		//		if (config != null)
		//		{
		//			SetSpeed(axis, config.HomeSpeed);
		//			SetCreep(axis, config.HomeCreep);
		//			return zmcaux.ZAux_Direct_Single_Datum(_handle, axis, config.HomeMode);
		//		}
		//		return zmcaux.ZAux_Direct_Single_Datum(_handle, axis, 3);
		//	}
		//}

		/// <summary>
		/// 控制卡方式回零
		/// </summary>
		public int HomeByController(int axis, int homeMode)
		{
			EnsureConnected();
			return zmcaux.ZAux_Direct_Single_Datum(_handle, axis, homeMode);
		}

		/// <summary>
		/// 总线驱动器回零
		/// </summary>
		public int HomeByDrive(int axis, uint homeMode)
		{
			EnsureConnected();
			return zmcaux.ZAux_BusCmd_Datum(_handle, (uint)axis, homeMode);
		}

		/// <summary>
		/// 设置回零爬行速度
		/// </summary>
		//public int SetCreep(int axis, float value)
		//{
		//	EnsureConnected();
		//	return zmcaux.ZAux_Direct_SetCreep(_handle, axis, value);
		//}

		/// <summary>
		/// 通用回零（自动判断方式）
		/// </summary>
		public int Home(int axis, AxisConfig config)
		{
			EnsureConnected();

			if (config != null && config.IsBusAxis)
			{
				// 总线轴使用驱动器回零
				return HomeByDrive(axis, (uint)config.HomeMode);
			}
			else
			{
				// 脉冲轴使用控制卡回零
				return HomeByController(axis, config.HomeMode);
			}
		}

		public int SetCreep(int axis, float value)
		{
			return zmcaux.ZAux_Direct_SetCreep(_handle, axis, value);
		}
		#endregion

		#region 通用使能控制
		/// <summary>
		/// 轴使能（自动判断方式）
		/// </summary>
		public int SetEnable(int axis, bool enable)
		{
			EnsureConnected();

			AxisConfig config = null;
			if (_axisConfigs != null)
			{
				foreach (var cfg in _axisConfigs)
					if (cfg.AxisNum == axis) { config = cfg; break; }
			}

			if (config != null && config.IsBusAxis)
			{
				// 总线轴使用专用指令
				return zmcaux.ZAux_Direct_SetAxisEnable(_handle, axis, enable ? 1 : 0);
			}
			else
			{
				// 脉冲轴可以通过OP口控制（具体口要根据硬件手册）
				// 或者使用SetDAC（速度模式时）
				return 0;
			}
		}

		public bool GetEnable(int axis)
		{
			EnsureConnected();
			int en = 0;
			zmcaux.ZAux_Direct_GetAxisEnable(_handle, axis, ref en);
			return en == 1;
		}
		#endregion

		#region 通用报警处理
		/// <summary>
		/// 清除报警（自动判断方式）
		/// </summary>
		public int ClearAlarm(int axis)
		{
			EnsureConnected();

			AxisConfig config = null;
			if (_axisConfigs != null)
			{
				foreach (var cfg in _axisConfigs)
					if (cfg.AxisNum == axis) { config = cfg; break; }
			}

			if (config != null && config.IsBusAxis)
			{
				return zmcaux.ZAux_BusCmd_DriveClear(_handle, (uint)axis, 0);
			}
			else
			{
				return zmcaux.ZAux_Direct_Single_Datum(_handle, axis, 0);
			}
		}
		#endregion

		#region 通用状态读取
		public bool IsIdle(int axis)
		{
			EnsureConnected();
			int idle = 0;
			zmcaux.ZAux_Direct_GetIfIdle(_handle, axis, ref idle);
			return idle == -1;
		}

		public int GetAxisStatus(int axis)
		{
			EnsureConnected();
			int status = 0;
			zmcaux.ZAux_Direct_GetAxisStatus(_handle, axis, ref status);
			return status;
		}

		/// <summary>
		/// 等待轴停止
		/// </summary>
		public void WaitForStop(int axis, int timeoutMs = 30000)
		{
			int waited = 0;
			while (!IsIdle(axis) && waited < timeoutMs)
			{
				Thread.Sleep(50);
				waited += 50;
			}
		}
		#endregion

		#region IO操作（通用）
		public uint ReadIn(int num)
		{
			EnsureConnected();
			uint val = 0;
			zmcaux.ZAux_Direct_GetIn(_handle, num, ref val);
			return val;
		}

		public int SetOut(int num, uint value)
		{
			EnsureConnected();
			return zmcaux.ZAux_Direct_SetOp(_handle, num, value);
		}
		#endregion

		#region 在线命令
		public string Execute(string cmd)
		{
			EnsureConnected();
			StringBuilder sb = new StringBuilder(2048);
			zmcaux.ZAux_Execute(_handle, cmd, sb, 2048);
			return sb.ToString();
		}
		#endregion

		#region 辅助
		/// <summary>
		/// 获取控制器信息
		/// </summary>
		public (string SoftType, string Version, string ID) GetControllerInfo()
		{
			EnsureConnected();
			StringBuilder type = new StringBuilder(50);
			StringBuilder ver = new StringBuilder(50);
			StringBuilder id = new StringBuilder(50);
			zmcaux.ZAux_GetControllerInfo(_handle, type, ver, id);
			return (type.ToString(), ver.ToString(), id.ToString());
		}

		private void RefreshMaxAxes()
		{
			ushort maxVirt = 0;
			byte[] motor = new byte[1];
			byte[] io = new byte[4];
			zmcaux.ZAux_GetSysSpecification(_handle, ref maxVirt, motor, io);
			_maxAxes = maxVirt;
		}

		private void EnsureConnected()
		{
			if (!IsConnected)
				throw new InvalidOperationException("控制器未连接！请先连接控制器。");
		}

		public void Dispose()
		{
			Disconnect();
		}
		#endregion

		// --- 补丁：补齐运动控制底层接口 ---

		public float GetPosition(int axis)
		{
			if (!IsConnected) return 0f;
			float pos = 0f;
			cszmcaux.zmcaux.ZAux_Direct_GetDpos(_handle, axis, ref pos);
			return pos;
		}

		public void MoveAbs(int axis, float position)
		{
			if (!IsConnected) return;
			cszmcaux.zmcaux.ZAux_Direct_Single_MoveAbs(_handle, axis, position);
		}

		public void JogMove(int axis, int dir)
		{
			if (!IsConnected) return;
			// 假设 dir 1 为正向，-1 为反向
			cszmcaux.zmcaux.ZAux_Direct_Single_Vmove(_handle, axis, dir);
		}

		public void SetSpeed(int axis, float speed, float accel, float decel)
		{
			if (!IsConnected) return;
			cszmcaux.zmcaux.ZAux_Direct_SetSpeed(_handle, axis, speed);
			cszmcaux.zmcaux.ZAux_Direct_SetAccel(_handle, axis, accel);
			cszmcaux.zmcaux.ZAux_Direct_SetDecel(_handle, axis, decel);
		}

		/// <summary>
		/// 设置运动控制卡输出口状态 (WriteOut)
		/// </summary>
		/// <param name="port">输出口编号 (如 8, 9)</param>
		/// <param name="value">状态: 1为开(高电平), 0为关(低电平)</param>
		public void WriteOut(int port, uint value)
		{
			if (!IsConnected) return;
			// 调用 cszmcaux 底层接口写入OUT
			cszmcaux.zmcaux.ZAux_Direct_SetOp(_handle, port, value);
		}
	}

	/// <summary>
	/// 状态解析工具
	/// </summary>
	public static class StatusHelper
	{
		public static string ParseAxisStatus(int status)
		{
			if (status == 0) return "正常";
			var sb = new StringBuilder();
			if ((status & 2) != 0) sb.Append("超限 ");
			if ((status & 4) != 0) sb.Append("通讯错 ");
			if ((status & 8) != 0) sb.Append("驱动器报警 ");
			if ((status & 16) != 0) sb.Append("正限位 ");
			if ((status & 32) != 0) sb.Append("负限位 ");
			if ((status & 64) != 0) sb.Append("回零中 ");
			if ((status & 4194304) != 0) sb.Append("ALM ");
			if (sb.Length == 0) sb.Append($"未知({status})");
			return sb.ToString().Trim();
		}
	}
}