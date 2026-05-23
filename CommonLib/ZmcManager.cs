using System;
using System.Threading;
using System.Threading.Tasks;
using ZmcUniversalLib;

namespace ZmcUniversalLib
{
	public class ZmcManager
	{
		public static ZmcUniversalController ZmcCtrl { get; private set; }
		private static bool _isRunning = false;

		// 定义事件，供业务层订阅
		public static event Action OnFrontStationTriggered;   // IN9 上升沿
		public static event Action OnEndFaceStationTriggered; // IN10 上升沿
		public static event Action OnBackStationTriggered;    // IN11 上升沿
		public static event Action<bool> OnSideCameraTriggered; // IN12 true上升沿, false下降沿

		public static void InitAndConnect(string ip, int type)
		{
			ZmcCtrl = new ZmcUniversalController();
			ZmcCtrl.Connect(ip, type);

			if (ZmcCtrl.IsConnected)
			{
				_isRunning = true;
				StartIoPolling();
			}
		}
	
		private static void StartIoPolling()
		{
			Task.Run(() =>
			{
				bool lastIn9 = false, lastIn10 = false, lastIn11 = false, lastIn12 = false;

				while (_isRunning)
				{
					if (!ZmcCtrl.IsConnected)
					{
						Thread.Sleep(500);
						continue;
					}

					// 批量读取状态以提升性能 (假设有对应API，此处用ReadIn示意)
					bool in9 = ZmcCtrl.ReadIn(9) == 1;
					bool in10 = ZmcCtrl.ReadIn(10) == 1;
					bool in11 = ZmcCtrl.ReadIn(11) == 1;
					bool in12 = ZmcCtrl.ReadIn(12) == 1;

					// IN9 正面上升沿
					if (in9 && !lastIn9)
					{
						TriggerOut(8); TriggerOut(9); // 触发相机
						OnFrontStationTriggered?.Invoke();
					}

					// IN10 端面上升沿 (飞拍)
					if (in10 && !lastIn10)
					{
						TriggerOut(10); TriggerOut(11);
						OnEndFaceStationTriggered?.Invoke();
					}

					// IN11 背面上升沿
					if (in11 && !lastIn11)
					{
						TriggerOut(12); TriggerOut(13);
						OnBackStationTriggered?.Invoke();
					}

					// IN12 侧面运动轴上相机 (支持上升沿和下降沿分别拍两侧)
					if (in12 && !lastIn12) OnSideCameraTriggered?.Invoke(true);  // 上升沿
					if (!in12 && lastIn12) OnSideCameraTriggered?.Invoke(false); // 下降沿

					lastIn9 = in9; lastIn10 = in10; lastIn11 = in11; lastIn12 = in12;
					Thread.Sleep(1); // 释放CPU，防止死循环占满单核
				}
			});
		}

		// 飞拍触发：给出指定毫秒的高电平后关闭
		private static void TriggerOut(int outIndex, int durationMs = 50)
		{
			Task.Run(() =>
			{
				ZmcCtrl.WriteOut(outIndex, 1);
				Thread.Sleep(durationMs);
				ZmcCtrl.WriteOut(outIndex, 0);
			});
		}

		public static void Disconnect()
		{
			_isRunning = false;
			ZmcCtrl?.Disconnect();
		}
	}
}