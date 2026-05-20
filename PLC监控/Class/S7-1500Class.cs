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

				plc?.ConnectClose();
				OperateResult connectState = plc.ConnectServer();
				plcState = connectState.IsSuccess;

				if (connectState.IsSuccess)
				{

					timeOut.Restart();

					EventConnectState(true, "PLC连接成功");
					return true;
				}
				else
				{
					EventConnectState(false, "PLC连接失败");
					return false;
				}
			}
			catch (Exception ex)
			{
				plcState = false;
				EventConnectState(false, $"连接PLC错误...\r\n {ex.Message} \r\n {ex.StackTrace}");
				return false;
			}

		}

		public void CloseModbus()
		{
			try
			{
				plc.ConnectClose();
				plcState = false;
				toolClass.SaveLog($"关闭PLC连接...");
			}
			catch (Exception ex)
			{
				toolClass.SaveLog($"关闭PLC时错误...\r\n {ex.Message} \r\n {ex.StackTrace}");
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


		private void WriteKeepAlive()
		{
			try
			{
				short val = 1;
				while (true)
				{
					Thread.Sleep(500);

					if (plcState)
					{
						plc.Write("DB1000.DBD48", val);
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
			short oldVal = 0;
			try
			{
				while (true)
				{
					Thread.Sleep(50);
					if (plcState)
					{
						short newVal = plc.ReadInt16("DB1000.DBD48").Content;
						if (oldVal != newVal)
						{
							Console.WriteLine($"状态变了 之前{oldVal} 现在{newVal}");
							oldVal = newVal;
							//Console.WriteLine(timeOut.ElapsedMilliseconds);
							timeOut.Restart();

							Console.WriteLine($"状态更新后 时间清空了{timeOut.ElapsedMilliseconds}");
						}

						if (timeOut.ElapsedMilliseconds > 10000)
						{
							Console.WriteLine($"超出十秒状态没有更新了 时间：{timeOut.ElapsedMilliseconds}ms");
							plcState = false;
							EventConnectState(false, $"心跳状态超十秒未更新，判定为通讯断开状态，最后一次为[{newVal}]");
						}
					}
				}
			}
			catch (Exception ex)
			{
				plcState = false;
				EventConnectState(false, $"向PLC写心跳时发生错误...\r\n {ex.Message} \r\n {ex.StackTrace}");
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
				while (!plcState)
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
