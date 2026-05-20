using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Diagnostics;

namespace Utils
{
	/// <summary>
	/// 性能日志专用记录器
	/// </summary>
	public static class PerformanceLogger
	{
		private static string _currentLogPath;
		private static readonly object _lock = new object();
		private static bool _initialized = false;

		// 性能统计
		private static long _totalImages = 0;
		private static double _totalCropTime = 0;
		private static double _totalInferenceTime = 0;
		private static double _totalPostprocessTime = 0;
		private static double _totalDrawTime = 0;
		private static double _totalSaveTime = 0;
		private static double _totalPlcTime = 0;
		private static readonly object _statLock = new object();

		private static void EnsureInitialized()
		{
			if (_initialized) return;

			lock (_lock)
			{
				if (_initialized) return;

				try
				{
					string logDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "Performance");
					if (!Directory.Exists(logDir))
						Directory.CreateDirectory(logDir);

					_currentLogPath = Path.Combine(logDir, $"Performance_{DateTime.Now:yyyyMMdd}.csv");

					if (!File.Exists(_currentLogPath))
					{
						string header = "时间,工位,产品ID,裁剪耗时ms,推理耗时ms,后处理耗时ms,绘制耗时ms,存图耗时ms,PLC耗时ms,总耗时ms,结果";
						File.WriteAllText(_currentLogPath, header + Environment.NewLine, Encoding.UTF8);
					}

					_initialized = true;
					Logger.Info($"性能日志初始化完成: {_currentLogPath}");
				}
				catch (Exception ex)
				{
					Logger.Error($"性能日志初始化失败: {ex.Message}");
				}
			}
		}

		public static void Record(string station, long productId,
			double cropMs, double inferenceMs, double postMs,
			double drawMs, double saveMs, double plcMs,
			double totalMs, bool result)
		{
			EnsureInitialized();

			if (string.IsNullOrEmpty(_currentLogPath)) return;

			try
			{
				string line = $"{DateTime.Now:HH:mm:ss.fff},{station},{productId}," +
							 $"{cropMs:F2},{inferenceMs:F2},{postMs:F2}," +
							 $"{drawMs:F2},{saveMs:F2},{plcMs:F2}," +
							 $"{totalMs:F2},{(result ? "OK" : "NG")}";

				lock (_lock)
				{
					File.AppendAllText(_currentLogPath, line + Environment.NewLine, Encoding.UTF8);
				}

				// 更新统计
				lock (_statLock)
				{
					_totalImages++;
					_totalCropTime += cropMs;
					_totalInferenceTime += inferenceMs;
					_totalPostprocessTime += postMs;
					_totalDrawTime += drawMs;
					_totalSaveTime += saveMs;
					_totalPlcTime += plcMs;
				}

				Logger.Debug($"性能记录: {station} 产品={productId} 总耗时={totalMs:F2}ms");
			}
			catch (Exception ex)
			{
				Logger.Error($"写入性能日志失败: {ex.Message}");
			}
		}

		public static void PrintSummary()
		{
			lock (_statLock)
			{
				if (_totalImages == 0)
				{
					Logger.Info("暂无性能统计数据");
					return;
				}

				var sb = new StringBuilder();
				sb.AppendLine("========== 性能统计摘要 ==========");
				sb.AppendLine($"总处理数量: {_totalImages}");
				sb.AppendLine($"平均裁剪耗时: {_totalCropTime / _totalImages:F2}ms");
				sb.AppendLine($"平均推理耗时: {_totalInferenceTime / _totalImages:F2}ms");
				sb.AppendLine($"平均后处理耗时: {_totalPostprocessTime / _totalImages:F2}ms");
				sb.AppendLine($"平均绘制耗时: {_totalDrawTime / _totalImages:F2}ms");
				sb.AppendLine($"平均存图耗时: {_totalSaveTime / _totalImages:F2}ms");
				sb.AppendLine($"平均PLC耗时: {_totalPlcTime / _totalImages:F2}ms");
				sb.AppendLine($"平均总耗时: {(_totalCropTime + _totalInferenceTime + _totalPostprocessTime + _totalDrawTime + _totalSaveTime + _totalPlcTime) / _totalImages:F2}ms");
				sb.AppendLine("===================================");

				Logger.Info(sb.ToString());
			}
		}

		public static void Reset()
		{
			lock (_statLock)
			{
				_totalImages = 0;
				_totalCropTime = 0;
				_totalInferenceTime = 0;
				_totalPostprocessTime = 0;
				_totalDrawTime = 0;
				_totalSaveTime = 0;
				_totalPlcTime = 0;
			}
		}
	}
}