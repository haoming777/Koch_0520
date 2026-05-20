using CommonLib;
using System;
using System.Threading;
using System.Threading.Tasks;
using Utils;
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

			try
			{
				return _zmc.StopMove(_handle, axis);
			}
			catch (Exception ex)
			{
				Logger.Error($"StopAxis失败 轴{axis}: {ex.Message}");
				return false;
			}
		}

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