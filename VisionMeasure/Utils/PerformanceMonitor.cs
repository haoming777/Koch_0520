using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using VisionMeasure.Utils;using CommonLib;

namespace VisionMeasure.Utils
{
/// <summary>性能监控器 — 记录各工位检测耗时(Crop/Inference/Draw/Save/Total)</summary>
	public class PerformanceMonitor : IDisposable
	{
		private readonly BlockingCollection<PerformanceRecord> _recordQueue;
		private readonly Thread _writeThread;
		private readonly string _logPath;
		private bool _isRunning = true;

		// 全局统计
		private long _totalImages = 0;
		private long _totalCropTimeUs = 0;
		private long _totalInferenceTimeUs = 0;
		private long _totalPostprocessTimeUs = 0;
		private long _totalDrawTimeUs = 0;
		private long _totalSaveTimeUs = 0;
		private long _totalPlcTimeUs = 0;

		// 分站统计
		private readonly ConcurrentDictionary<string, StationStats> _stationStats = new ConcurrentDictionary<string, StationStats>();

		private class StationStats
		{
			public long Count;
			public long TotalCropUs, TotalInferUs, TotalDrawUs, TotalSaveUs, TotalPlcUs;
			public double MinCropMs = double.MaxValue, MaxCropMs;
			public double MinInferMs = double.MaxValue, MaxInferMs;
			public double MinDrawMs = double.MaxValue, MaxDrawMs;
			public double MinSaveMs = double.MaxValue, MaxSaveMs;
			public double MinTotalMs = double.MaxValue, MaxTotalMs;

			public void Update(PerformanceRecord r)
			{
				Count++;
				TotalCropUs += (long)(r.CropTimeMs * 1000);
				TotalInferUs += (long)(r.InferenceTimeMs * 1000);
				TotalDrawUs += (long)(r.DrawTimeMs * 1000);
				TotalSaveUs += (long)(r.SaveTimeMs * 1000);
				TotalPlcUs += (long)(r.PlcTimeMs * 1000);
				if (r.CropTimeMs < MinCropMs) MinCropMs = r.CropTimeMs;
				if (r.CropTimeMs > MaxCropMs) MaxCropMs = r.CropTimeMs;
				if (r.InferenceTimeMs < MinInferMs) MinInferMs = r.InferenceTimeMs;
				if (r.InferenceTimeMs > MaxInferMs) MaxInferMs = r.InferenceTimeMs;
				if (r.DrawTimeMs < MinDrawMs) MinDrawMs = r.DrawTimeMs;
				if (r.DrawTimeMs > MaxDrawMs) MaxDrawMs = r.DrawTimeMs;
				if (r.SaveTimeMs < MinSaveMs) MinSaveMs = r.SaveTimeMs;
				if (r.SaveTimeMs > MaxSaveMs) MaxSaveMs = r.SaveTimeMs;
				if (r.TotalTimeMs < MinTotalMs) MinTotalMs = r.TotalTimeMs;
				if (r.TotalTimeMs > MaxTotalMs) MaxTotalMs = r.TotalTimeMs;
			}
		}

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

						// 累加统计
						Interlocked.Add(ref _totalCropTimeUs, (long)(record.CropTimeMs * 1000));
						Interlocked.Add(ref _totalInferenceTimeUs, (long)(record.InferenceTimeMs * 1000));
						Interlocked.Add(ref _totalPostprocessTimeUs, (long)(record.PostprocessTimeMs * 1000));
						Interlocked.Add(ref _totalDrawTimeUs, (long)(record.DrawTimeMs * 1000));
						Interlocked.Add(ref _totalSaveTimeUs, (long)(record.SaveTimeMs * 1000));
						Interlocked.Add(ref _totalPlcTimeUs, (long)(record.PlcTimeMs * 1000));
						Interlocked.Increment(ref _totalImages);
						// 分站统计
						_stationStats.AddOrUpdate(record.Station,
							k => { var s = new StationStats(); s.Update(record); return s; },
							(k, s) => { s.Update(record); return s; });
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

			// 分站详细统计
			if (_stationStats.Count > 0)
			{
				Logger.Info("── 分站统计 ──");
				foreach (var kv in _stationStats.OrderBy(k => k.Key))
				{
					var s = kv.Value;
					if (s.Count == 0) continue;
					double avgTotal = (s.TotalCropUs + s.TotalInferUs + s.TotalDrawUs + s.TotalSaveUs + s.TotalPlcUs) / (double)s.Count / 1000;
					double avgInfer = s.TotalInferUs / (double)s.Count / 1000;
					double avgCrop = s.TotalCropUs / (double)s.Count / 1000;
					double avgDraw = s.TotalDrawUs / (double)s.Count / 1000;
					Logger.Info($"┌─ {kv.Key} x{s.Count} ─────────────────────┐");
					Logger.Info($"│ 推理  avg={avgInfer:F1}  min={s.MinInferMs:F1}  max={s.MaxInferMs:F1}ms");
					Logger.Info($"│ 裁剪  avg={avgCrop:F1}  min={s.MinCropMs:F1}  max={s.MaxCropMs:F1}ms");
					Logger.Info($"│ 绘制  avg={avgDraw:F1}  min={s.MinDrawMs:F1}  max={s.MaxDrawMs:F1}ms");
					Logger.Info($"│ 存图  avg={s.TotalSaveUs / (double)s.Count / 1000:F1}  min={s.MinSaveMs:F1}  max={s.MaxSaveMs:F1}ms");
					Logger.Info($"│ 总耗时 avg={avgTotal:F1}  min={s.MinTotalMs:F1}  max={s.MaxTotalMs:F1}ms");
					Logger.Info($"└─────────────────────────────────────┘");
				}
			}

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