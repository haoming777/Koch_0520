using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using static System.Windows.Forms.AxHost;
using static CommonLib.Class_Config;
using XL.Tool;

using HslCommunication.Profinet.Siemens;
using HslCommunication;
using System.Xml.Serialization;
using HslCommunication.Profinet.Siemens.S7PlusHelper;

namespace PLC调试.Class
{
	public class S7_1500Class
	{
		Thread doKeepAlive;        // 心跳

		Thread doState;

		Thread doReadT1;

		Stopwatch timeOut;

		public S7_1500Class()
		{
			timeOut = new Stopwatch();

			doReadT1 = new Thread(new ThreadStart(ReadGetTrigger));
			doReadT1.IsBackground = true;


			doState = new Thread(new ThreadStart(DoStateMethod));
			doState.IsBackground = true;

			doKeepAlive = new Thread(new ThreadStart(WriteKeepAlive));
			doKeepAlive.IsBackground = true;

			doReadT1.Start();
			doKeepAlive.Start();
			doState.Start();
		}

		public delegate void DelegateConnectState(bool state, string error);
		public event DelegateConnectState EventConnectState;

		public delegate void DelegateTriggerGet();
		public event DelegateTriggerGet EventTriggerGet;

		SiemensS7Net plc = new SiemensS7Net(SiemensPLCS.S1500);

		XLToolClass toolClass = new XLToolClass();
		bool plcState = false;

		public bool ConnectModbus()
		{
			try
			{
				plc.IpAddress = _Config.PlcIP;
				plc.Port = _Config.PlcPort;

				CommonLib.PlcLogger.Info($"[S7-1500] 正在连接 {_Config.PlcIP}:{_Config.PlcPort} ...");
				CommonLib.Logger.Info($"[S7-1500] 连接PLC: {_Config.PlcIP}:{_Config.PlcPort}");

				plc?.ConnectClose();
				OperateResult connectState = plc.ConnectServer();
				plcState = connectState.IsSuccess;

				if (connectState.IsSuccess)
				{
					timeOut.Restart();
					CommonLib.PlcLogger.Info($"[S7-1500] 连接成功 {_Config.PlcIP}:{_Config.PlcPort}");
					CommonLib.Logger.Info($"[S7-1500] PLC连接成功");
					EventConnectState(true, "PLC连接成功");
					return true;
				}
				else
				{
					CommonLib.PlcLogger.Error($"[S7-1500] 连接失败: {connectState.Message}");
					CommonLib.Logger.Error($"[S7-1500] PLC连接失败: {connectState.Message}");
					EventConnectState(false, "PLC连接失败");
					return false;
				}
			}
			catch (Exception ex)
			{
				plcState = false;
				CommonLib.PlcLogger.Error($"[S7-1500] 连接异常: {ex.Message}");
				CommonLib.Logger.Error($"[S7-1500] PLC连接异常: {ex.Message}");
				EventConnectState(false, $"连接PLC错误...\r\n {ex.Message} \r\n {ex.StackTrace}");
				return false;
			}
		}

		public void CloseModbus()
		{
			try
			{
				CommonLib.PlcLogger.Info("[S7-1500] 断开PLC连接");
				CommonLib.Logger.Info("[S7-1500] 断开PLC连接");
				plc.ConnectClose();
				plcState = false;
			}
			catch (Exception ex)
			{
				CommonLib.PlcLogger.Error($"[S7-1500] 断开连接异常: {ex.Message}");
				CommonLib.Logger.Error($"[S7-1500] 断开PLC连接异常: {ex.Message}");
			}
		}

	
		public float[] GetCylindricityData()
		{

			try
			{
				return plc.ReadFloat($"DB1000.DBD494", 6).Content;
			}
			catch (Exception ex)
			{
				plcState = false;
				EventConnectState(false, $"读取数据时出现异常...\r\n {ex.Message} \r\n {ex.StackTrace}");

				return null;
			}
		}


		private void ReadGetTrigger()
		{
			try
			{

				string path = "DB1000.DBW0";
				short val = 0;
				//string path = _Config.gt_DataValid.ToString();
				//toolClass.SaveLog($"触发地址：{path}");
				while (true)
				{

					Thread.Sleep(50);
					//toolClass.SaveLog(plcState + "状态");
					if (!plcState) continue;

					short test = Convert.ToInt16(plc.ReadInt16("DB1000.DBW0").Content);
					if (test == 1)
					{
						EventTriggerGet();
						Thread.Sleep(50);
						plc.Write("DB1000.DBW0", val);
						toolClass.SaveLog($"写零后读取{plc.ReadInt16(path).Content}");
					}
				}
			}
			catch (Exception ex)
			{
				plcState = false;
				EventConnectState(false, $"读触发信号时出现异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}


		// 心跳地址: DB47.DBX12.5 (CameraOnline)
		private const string HEARTBEAT_ADDR = "DB47.DBX12.5";

		private void WriteKeepAlive()
		{
			try
			{
				bool toggle = false;
				while (true)
				{
					Thread.Sleep(200);  // 200ms 交替 1/0

					if (plcState)
					{
						toggle = !toggle;
						plc.Write(HEARTBEAT_ADDR, toggle);
					}
				}
			}
			catch (Exception ex)
			{
				plcState = false;
				EventConnectState(false, $"向PLC写心跳时发生错误...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}

		private void DoStateMethod()
		{
			timeOut.Start();
			bool oldVal = false;
			try
			{
				while (true)
				{
					Thread.Sleep(50);
					if (plcState)
					{
						bool newVal = plc.ReadBool(HEARTBEAT_ADDR).Content;
						if (oldVal != newVal)
						{
							oldVal = newVal;
							timeOut.Restart();
						}

						if (timeOut.ElapsedMilliseconds > 10000)
						{
							plcState = false;
							CommonLib.PlcLogger.Error($"[S7-1500] 心跳超时! {timeOut.ElapsedMilliseconds}ms未变化, 判定通讯断开");
							CommonLib.Logger.Error($"[S7-1500] 心跳超时 {timeOut.ElapsedMilliseconds}ms, PLC通讯断开");
							EventConnectState(false, $"心跳状态超十秒未更新，判定为通讯断开");
						}
					}
				}
			}
			catch (Exception ex)
			{
				plcState = false;
				CommonLib.PlcLogger.Error($"[S7-1500] 心跳监测异常: {ex.Message}");
				CommonLib.Logger.Error($"[S7-1500] 心跳监测异常: {ex.Message}");
				EventConnectState(false, $"心跳监测异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}


		/// <summary>批量写Int16到S7-1500 DB (用于发送逐盒缺陷码)</summary>
		public void WriteStationResult(string dbAddr, int[] values)
		{
			try
			{
				if (!plcState)
				{
					CommonLib.PlcLogger.Warn($"[S7-1500] 写入跳过(未连接) addr={dbAddr} values=[{string.Join(",", values)}]");
					return;
				}
				short[] shorts = Array.ConvertAll(values, v => (short)v);
				plc.Write(dbAddr, shorts);
				CommonLib.PlcLogger.Info($"[S7-1500] 写入成功 addr={dbAddr} count={values.Length} values=[{string.Join(",", values)}]");
			}
			catch (Exception ex)
			{
				plcState = false;
				CommonLib.PlcLogger.Error($"[S7-1500] 写入失败 addr={dbAddr}: {ex.Message}");
				EventConnectState(false, "S7-1500批量写异常: " + ex.Message);
			}
		}

		/// <summary>写单个Bool到S7-1500 DB (用于发送完成信号)</summary>
		public void WriteBool(string dbAddr, bool value)
		{
			try
			{
				if (!plcState)
				{
					CommonLib.PlcLogger.Warn($"[S7-1500] 写Bool跳过(未连接) addr={dbAddr} value={value}");
					return;
				}
				plc.Write(dbAddr, value);
				CommonLib.PlcLogger.Info($"[S7-1500] 写Bool成功 addr={dbAddr} value={value}");
			}
			catch (Exception ex)
			{
				plcState = false;
				CommonLib.PlcLogger.Error($"[S7-1500] 写Bool失败 addr={dbAddr}: {ex.Message}");
				EventConnectState(false, "S7-1500写Bool异常: " + ex.Message);
			}
		}

		/// <summary>写单个Byte到S7-1500 DB (用于发送停机标识)</summary>
		public void WriteByte(string dbAddr, byte value)
		{
			try
			{
				if (!plcState)
				{
					CommonLib.PlcLogger.Warn($"[S7-1500] 写Byte跳过(未连接) addr={dbAddr} value={value}");
					return;
				}
				plc.Write(dbAddr, value);
				CommonLib.PlcLogger.Info($"[S7-1500] 写Byte成功 addr={dbAddr} value={value}");
			}
			catch (Exception ex)
			{
				plcState = false;
				CommonLib.PlcLogger.Error($"[S7-1500] 写Byte失败 addr={dbAddr}: {ex.Message}");
				EventConnectState(false, "S7-1500写Byte异常: " + ex.Message);
			}
		}

		bool bRunning = false;
		int ReconnectCount = 0;


		public void Reconnect()
		{
			if (bRunning) return;
			CommonLib.PlcLogger.Warn("[S7-1500] 开始重连PLC...");
			CommonLib.Logger.Warning("[S7-1500] 开始PLC重连");
			Task.Run(() =>
			{
				bRunning = true;
				while (!plcState)
				{
					ReconnectCount++;
					CommonLib.PlcLogger.Info($"[S7-1500] 重连第{ReconnectCount}次...");
					ConnectModbus();
					Thread.Sleep(1000);
				}
				bRunning = false;
				CommonLib.PlcLogger.Info($"[S7-1500] 重连成功! 共尝试{ReconnectCount}次");
				CommonLib.Logger.Info($"[S7-1500] PLC重连成功, 共尝试{ReconnectCount}次");
				ReconnectCount = 0;
			});
		}
	}
}
