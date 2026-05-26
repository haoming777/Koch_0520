using System;
using System.Threading.Tasks;
using Models;
using VisionMeasure.Utils;using CommonLib;

namespace Hardware
{
	public class PlcCommunication
	{
		private readonly string _ip;
		private readonly int _port;
		private readonly object _lock = new object();
		private bool _connected;
		private bool _simulateMode;

		public int AddressHeartbeat { get; set; } = 100;
		public int AddressResult { get; set; } = 200;
		public int AddressReady { get; set; } = 201;

		public event Action<bool, string> OnStateChanged;
		public event Action<int> OnResultRequest;

		public PlcCommunication(string ip, int port = 502, bool simulateMode = true)
		{
			_ip = ip;
			_port = port;
			_simulateMode = simulateMode;
		}

		public bool Connect()
		{
			var sw = System.Diagnostics.Stopwatch.StartNew();
			if (_simulateMode)
			{
				Logger.Info($"[PLC] 模拟模式连接成功 IP={_ip}:{_port} 耗时={sw.ElapsedMilliseconds}ms");
				_connected = true;
				return true;
			}

			try
			{
				// TODO: 实现实际的PLC连接
				_connected = true;
				Logger.Info($"[PLC] 连接成功 IP={_ip}:{_port} 耗时={sw.ElapsedMilliseconds}ms");

				// 启动心跳检测
				Task.Run(HeartbeatLoop);

				return true;
			}
			catch (Exception ex)
			{
				Logger.Error($"[PLC] 连接异常: {ex.Message} 耗时={sw.ElapsedMilliseconds}ms");
				_connected = false;
				return false;
			}
		}

		private async Task HeartbeatLoop()
		{
			while (_connected)
			{
				await Task.Delay(1000);
				// TODO: 发送心跳信号
			}
		}

		public bool SendResult(ProductResult product)
		{
			if (_simulateMode)
			{
				Logger.Debug($"[PLC] 模拟发送 ProductId={product.ProductId} Front={product.FrontResult} Back={product.BackResult} EndFace={product.EndFaceResult} Side={product.SideResult}");
				return true;
			}

			if (!_connected || product == null) return false;

			lock (_lock)
			{
				try
				{
					Logger.Info($"[PLC] 发送结果 ProductId={product.ProductId} FinalResult={(product.FinalResult ? "OK" : "NG")} Defects={string.Join(",", product.GetAllDefects())}");
					return true;
				}
				catch (Exception ex)
				{
					Logger.Error($"[PLC] 发送失败: {ex.Message}");
					return false;
				}
			}
		}

		public bool SendBatchResult(BatchResultRecord batch)
		{
			if (_simulateMode)
			{
				Logger.Debug($"模拟模式：PLC发送批次 BatchId={batch.BatchId}");
				return true;
			}

			if (!_connected || batch == null) return false;

			lock (_lock)
			{
				try
				{
					Logger.Info($"PLC发送批次 BatchId={batch.BatchId}, OK={batch.GetOkCount()}, NG={batch.GetNgCount()}");
					return true;
				}
				catch (Exception ex)
				{
					Logger.Error($"PLC批量发送失败: {ex.Message}");
					return false;
				}
			}
		}

		public bool ReadReadySignal()
		{
			if (_simulateMode) return true;
			if (!_connected) return false;
			return true;
		}

		public void Disconnect()
		{
			if (_simulateMode)
			{
				_connected = false;
				Logger.Info("模拟模式：PLC已断开");
				return;
			}

			_connected = false;
			Logger.Info("PLC已断开");
		}

		public bool IsConnected => _connected;
	}
}