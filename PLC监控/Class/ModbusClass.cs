using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using HslCommunication;
using HslCommunication.ModBus;
using static CommonLib.Class_Config;
using XL.Tool;
using System.Text.RegularExpressions;

using System.Diagnostics;

namespace PLC调试.Class
{
	public class ModbusClass
	{

		Thread doKeepAlive;        // 心跳

		Thread doState;

		Thread doReadT1;

		Stopwatch timeOut;

		public ModbusClass()
		{
			timeOut = new Stopwatch();

			doState = new Thread(new ThreadStart(DoStateMethod));
			doState.IsBackground = true;

			doKeepAlive = new Thread(new ThreadStart(WriteKeepAlive));
			doKeepAlive.IsBackground = true;

			doReadT1.Start();
			doKeepAlive.Start();
			doState.Start();
		}



		XLToolClass toolClass = new XLToolClass();

		public ModbusTcpNet modbusTcp = new ModbusTcpNet();
		//OperateResult connectState;
		bool modbusState = false;

		public delegate void DelegateConnectState(bool state, string error);
		public event DelegateConnectState EventConnectState;

		/// <summary>
		/// 相机一触发信号
		/// </summary>
		public delegate void DelegateTriggerGet();
		public event DelegateTriggerGet EventTriggerGet;
		public bool ConnectModbus()
		{
			try
			{
				modbusTcp?.ConnectClose();
				modbusTcp = new ModbusTcpNet(_Config.ModbusIP, _Config.ModbusPort, 1);
				modbusTcp.AddressStartWithZero = true;
				modbusTcp.DataFormat = HslCommunication.Core.DataFormat.CDAB;
				modbusTcp.IsStringReverse = true;
				modbusTcp.ConnectTimeOut = 5000;

				OperateResult connectState = modbusTcp.ConnectServer();
				modbusState = connectState.IsSuccess;
				if (connectState.IsSuccess)
				{

					timeOut.Restart();

					EventConnectState(true, "Modbus连接成功");
					return true;
				}
				else
				{
					EventConnectState(false, "Modbus连接失败");
					return false;
				}

			}
			catch (Exception ex)
			{
				modbusState = false;
				EventConnectState(false, $"连接Modbus错误...\r\n {ex.Message} \r\n {ex.StackTrace}");
				return false;
			}
		}

		public void CloseModbus()
		{
			try
			{
				modbusTcp.ConnectClose();
				modbusState = false;
				toolClass.SaveLog($"关闭Modbus连接...");
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"关闭Modbus时错误...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}


		private void DoStateMethod()
		{
			timeOut.Start();
			uint oldVal = 0;
			try
			{
				while (true)
				{
					Thread.Sleep(50);
					if (modbusState)
					{
						uint newVal = modbusTcp.ReadUInt16($"{_Config.keepAlive}").Content;
						if (oldVal != newVal)
						{
							Console.WriteLine($"状态变了 之前{oldVal} 现在{newVal}");
							oldVal = newVal;
							//Console.WriteLine(timeOut.ElapsedMilliseconds);
							timeOut.Restart();

							Console.WriteLine($"状态更新后 时间清空了{timeOut.ElapsedMilliseconds}");
						}

						//Console.WriteLine($"111: {timeOut.ElapsedMilliseconds}");

						if (timeOut.ElapsedMilliseconds > 10000)
						{
							Console.WriteLine($"超出十秒状态没有更新了 时间：{timeOut.ElapsedMilliseconds}ms");
							modbusState = false;
							EventConnectState(false, $"心跳状态超十秒未更新，判定为通讯断开状态，最后一次为[{newVal}]");
						}
					}
				}
			}
			catch (Exception ex)
			{
				modbusState = false;
				EventConnectState(false, $"向Modbus写心跳时发生错误...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}

		}

		private void WriteKeepAlive()
		{
			try
			{
				while (true)
				{
					Thread.Sleep(500);
					//toolClass.SaveLog($"modbusState: {modbusState}");
					if (modbusState)
					{
						//toolClass.SaveLog($"进来了: {modbusState}");
						//心跳
						modbusTcp.Write($"{_Config.keepAlive}", (short)1);
						int text = modbusTcp.ReadInt16($"{_Config.keepAlive}").Content;
						//toolClass.SaveLog($"写入后读取: {text}");
					}
				}
			}
			catch (Exception ex)
			{
				modbusState = false;
				EventConnectState(false, $"向Modbus写心跳时发生错误...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}




		/// <summary>批量写Int16寄存器 (用于发送逐盒缺陷码)</summary>
		public void WriteStationResult(string startAddr, int[] values)
		{
			try
			{
				if (!modbusState)
				{
					CommonLib.PlcLogger.Warn($"[Modbus] 写入跳过(未连接) addr={startAddr} values=[{string.Join(",", values)}]");
					return;
				}
				short[] shorts = Array.ConvertAll(values, v => (short)v);
				modbusTcp.Write(startAddr, shorts);
				CommonLib.PlcLogger.Info($"[Modbus] 写入成功 addr={startAddr} count={values.Length} values=[{string.Join(",", values)}]");
			}
			catch (Exception ex)
			{
				modbusState = false;
				CommonLib.PlcLogger.Error($"[Modbus] 写入失败 addr={startAddr}: {ex.Message}");
				EventConnectState(false, "批量写寄存器异常: " + ex.Message);
			}
		}

		/// <summary>写单个线圈 (用于发送完成信号)</summary>
		public void WriteBool(string addr, bool value)
		{
			try
			{
				if (!modbusState)
				{
					CommonLib.PlcLogger.Warn($"[Modbus] 写线圈跳过(未连接) addr={addr} value={value}");
					return;
				}
				modbusTcp.Write(addr, value);
				CommonLib.PlcLogger.Info($"[Modbus] 写线圈成功 addr={addr} value={value}");
			}
			catch (Exception ex)
			{
				modbusState = false;
				CommonLib.PlcLogger.Error($"[Modbus] 写线圈失败 addr={addr}: {ex.Message}");
				EventConnectState(false, "写线圈异常: " + ex.Message);
			}
		}

		bool bRunning = false;
		int ReconnectCount = 0;


		public void Reconnect()
		{
			if (bRunning) return;
			toolClass.SaveLog("尝试重新连接PLC");
			Task.Run(() =>
			{
				bRunning = true;
				while (!modbusState)
				{
					ReconnectCount++;
					toolClass.SaveLog($"正在尝试第 {ReconnectCount} 次重连");
					ConnectModbus();
					Thread.Sleep(1000);
				}
				bRunning = false;
				toolClass.SaveLog($"在第 {ReconnectCount} 次时重连成功");
				ReconnectCount = 0;
			});
		}

	}
}
