using System;
using System.Collections.Concurrent;
using System.IO;
using System.Threading;
using Utils;

namespace Utils
{
	public class PerformanceMonitor : IDisposable
	{
		private readonly BlockingCollection<PerformanceRecord> _recordQueue;
		private readonly Thread _writeThread;
		private readonly string _logPath;
		private bool _isRunning = true;

		// 使用 long 类型存储微秒
		private long _totalImages = 0;
		private long _totalCropTimeUs = 0;
		private long _totalInferenceTimeUs = 0;
		private long _totalPostprocessTimeUs = 0;
		private long _totalDrawTimeUs = 0;
		private long _totalSaveTimeUs = 0;
		private long _totalPlcTimeUs = 0;

		public class PerformanceRecord
		{
			public DateTime Timestamp { get; set; }
			public string Station { get; set; }
			public long ProductId { get; set; }
			public double CropTimeMs { get; set; }
			public double InferenceTimeMs { get; set; }
			public double PostprocessTimeMs { get; set; }
			public double DrawTimeMs { get; set; }
			public double SaveTimeMs { get; set; }
			public double PlcTimeMs { get; set; }
			public double TotalTimeMs { get; set; }
			public bool Result { get; set; }
		}

		public PerformanceMonitor()
		{
			string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "Performance");
			if (!Directory.Exists(logDir))
				Directory.CreateDirectory(logDir);

			_logPath = Path.Combine(logDir, $"Performance_{DateTime.Now:yyyyMMdd}.csv");

			if (!File.Exists(_logPath))
			{
				File.WriteAllText(_logPath, "时间,工位,产品ID,裁剪耗时ms,推理耗时ms,后处理耗时ms,绘制耗时ms,存图耗时ms,PLC耗时ms,总耗时ms,结果\n");
			}

			_recordQueue = new BlockingCollection<PerformanceRecord>(new ConcurrentQueue<PerformanceRecord>(), 1000);

			_writeThread = new Thread(WriteLoop)
			{
				Name = "PerfMonitor",
				IsBackground = true
			};
			_writeThread.Start();
		}

		private void WriteLoop()
		{
			while (_isRunning)
			{
				try
				{
					if (_recordQueue.TryTake(out var record, 100))
					{
						string line = $"{record.Timestamp:HH:mm:ss.fff},{record.Station},{record.ProductId}," +
									 $"{record.CropTimeMs:F2},{record.InferenceTimeMs:F2},{record.PostprocessTimeMs:F2}," +
									 $"{record.DrawTimeMs:F2},{record.SaveTimeMs:F2},{record.PlcTimeMs:F2}," +
									 $"{record.TotalTimeMs:F2},{(record.Result ? "OK" : "NG")}";

						File.AppendAllText(_logPath, line + Environment.NewLine);

						// 累加统计 - 转换为微秒存储
						Interlocked.Add(ref _totalCropTimeUs, (long)(record.CropTimeMs * 1000));
						Interlocked.Add(ref _totalInferenceTimeUs, (long)(record.InferenceTimeMs * 1000));
						Interlocked.Add(ref _totalPostprocessTimeUs, (long)(record.PostprocessTimeMs * 1000));
						Interlocked.Add(ref _totalDrawTimeUs, (long)(record.DrawTimeMs * 1000));
						Interlocked.Add(ref _totalSaveTimeUs, (long)(record.SaveTimeMs * 1000));
						Interlocked.Add(ref _totalPlcTimeUs, (long)(record.PlcTimeMs * 1000));
						Interlocked.Increment(ref _totalImages);
					}
				}
				catch (Exception ex)
				{
					Logger.Error($"性能记录写入异常: {ex.Message}");
				}
			}
		}

		public void Record(PerformanceRecord record)
		{
			if (!_recordQueue.TryAdd(record))
			{
				Logger.Warning("性能记录队列已满");
			}
		}

		public void PrintSummary()
		{
			long images = Interlocked.Read(ref _totalImages);
			if (images == 0)
			{
				Logger.Info("暂无性能统计数据");
				return;
			}

			Logger.Info("========== 性能统计摘要 ==========");
			Logger.Info($"总处理数量: {images}");
			Logger.Info($"平均裁剪耗时: {_totalCropTimeUs / (double)images / 1000:F2}ms");
			Logger.Info($"平均推理耗时: {_totalInferenceTimeUs / (double)images / 1000:F2}ms");
			Logger.Info($"平均后处理耗时: {_totalPostprocessTimeUs / (double)images / 1000:F2}ms");
			Logger.Info($"平均绘制耗时: {_totalDrawTimeUs / (double)images / 1000:F2}ms");
			Logger.Info($"平均存图耗时: {_totalSaveTimeUs / (double)images / 1000:F2}ms");
			Logger.Info($"平均PLC耗时: {_totalPlcTimeUs / (double)images / 1000:F2}ms");
			Logger.Info("===================================");
		}

		public void Dispose()
		{
			_isRunning = false;
			_recordQueue.CompleteAdding();
			_writeThread?.Join(3000);
			PrintSummary();
		}
	}
}