using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Diagnostics;

namespace Utils
{
	/// <summary>
	/// 高性能独立日志系统 - 支持日志级别控制
	/// </summary>
	public static class Logger
	{
		private static readonly string _logDirectory;
		private static readonly BlockingCollection<LogEntry> _logQueue;
		private static readonly Thread _workerThread;
		private static bool _isRunning = true;
		private static readonly object _fileLock = new object();

		// 日志级别枚举
		public enum LogLevel
		{
			Debug = 0,      // 调试信息（最详细）
			Info = 1,       // 一般信息
			Warning = 2,    // 警告
			Error = 3,      // 错误
			None = 4        // 不输出任何日志
		}

		// 当前日志级别 - 可以动态调整
		public static LogLevel CurrentLogLevel { get; set; } = LogLevel.Info;

		// 每秒最大日志条数限制
		private static int _logCountThisSecond = 0;
		private static DateTime _lastResetTime = DateTime.Now;
		private static readonly object _rateLimitLock = new object();
		private const int MAX_LOG_PER_SECOND = 100;

		private class LogEntry
		{
			public DateTime Timestamp { get; set; }
			public LogLevel Level { get; set; }
			public string Message { get; set; }
			public string Source { get; set; }
		}

		static Logger()
		{
			try
			{
				_logDirectory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
				if (!Directory.Exists(_logDirectory))
					Directory.CreateDirectory(_logDirectory);

				_logQueue = new BlockingCollection<LogEntry>(new ConcurrentQueue<LogEntry>(), 5000);

				_workerThread = new Thread(ProcessLogQueue)
				{
					Name = "LoggerWorker",
					IsBackground = true
				};
				_workerThread.Start();

				WriteInfo("日志系统初始化完成", "Logger");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"日志系统初始化失败: {ex.Message}");
			}
		}

		private static bool ShouldLog(LogLevel level)
		{
			// 检查日志级别
			if (level < CurrentLogLevel) return false;

			// 速率限制
			lock (_rateLimitLock)
			{
				var now = DateTime.Now;
				if ((now - _lastResetTime).TotalSeconds >= 1)
				{
					_logCountThisSecond = 0;
					_lastResetTime = now;
				}

				if (_logCountThisSecond >= MAX_LOG_PER_SECOND)
				{
					if (_logCountThisSecond == MAX_LOG_PER_SECOND)
					{
						_logCountThisSecond++;
						Console.WriteLine($"[WARN] 日志输出过多，已启用限流");
					}
					return false;
				}

				_logCountThisSecond++;
				return true;
			}
		}

		private static void ProcessLogQueue()
		{
			while (_isRunning)
			{
				try
				{
					if (_logQueue.TryTake(out var entry, 100))
					{
						WriteToFileDirect(entry);
					}
				}
				catch (Exception ex)
				{
					Console.WriteLine($"日志写入异常: {ex.Message}");
				}
			}

			while (_logQueue.TryTake(out var entry))
			{
				WriteToFileDirect(entry);
			}
		}

		private static void WriteToFileDirect(LogEntry entry)
		{
			try
			{
				string logFileName = $"Koch_{entry.Timestamp:yyyyMMdd}.log";
				string logPath = Path.Combine(_logDirectory, logFileName);

				string levelStr = GetLevelString(entry.Level);
				string logLine = $"{entry.Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{levelStr}] {entry.Message}";

				if (!string.IsNullOrEmpty(entry.Source))
					logLine += $" [{entry.Source}]";

				lock (_fileLock)
				{
					File.AppendAllText(logPath, logLine + Environment.NewLine, Encoding.UTF8);
				}

				// 使用 System.Diagnostics.Debug.WriteLine 而不是 Debug（避免与方法名冲突）
				System.Diagnostics.Debug.WriteLine(logLine);
			}
			catch (Exception ex)
			{
				Console.WriteLine($"写入日志文件失败: {ex.Message}");
			}
		}

		private static string GetLevelString(LogLevel level)
		{
			switch (level)
			{
				case LogLevel.Debug: return "DBG";
				case LogLevel.Info: return "INF";
				case LogLevel.Warning: return "WRN";
				case LogLevel.Error: return "ERR";
				default: return "UNK";
			}
		}

		private static void AddLog(LogLevel level, string message, string source = null)
		{
			if (!_isRunning) return;
			if (!ShouldLog(level)) return;

			try
			{
				var entry = new LogEntry
				{
					Timestamp = DateTime.Now,
					Level = level,
					Message = message,
					Source = source
				};

				if (!_logQueue.TryAdd(entry))
				{
					if (_logQueue.Count > 4900)
					{
						Console.WriteLine($"日志队列即将溢出，当前数量: {_logQueue.Count}");
					}
				}
			}
			catch (Exception ex)
			{
				Console.WriteLine($"添加日志失败: {ex.Message}");
			}
		}

		#region 公共方法 - 注意不要与 System.Diagnostics.Debug 混淆

		public static void LogDebug(string message, string source = null)
		{
			AddLog(LogLevel.Debug, message, source);
		}

		public static void LogInfo(string message, string source = null)
		{
			AddLog(LogLevel.Info, message, source);
		}

		public static void LogWarning(string message, string source = null)
		{
			AddLog(LogLevel.Warning, message, source);
		}

		public static void LogError(string message, string source = null)
		{
			AddLog(LogLevel.Error, message, source);
		}

		public static void LogException(Exception ex, string message = null, string source = null)
		{
			string msg = message ?? "发生异常";
			msg += $"\n类型: {ex.GetType().Name}\n消息: {ex.Message}\n堆栈: {ex.StackTrace}";
			AddLog(LogLevel.Error, msg, source);
		}

		// 为了兼容性，保留原来的方法名（但使用不同名称避免冲突）
		public static void Debug(string message, string source = null)
		{
			AddLog(LogLevel.Debug, message, source);
		}

		public static void Info(string message, string source = null)
		{
			AddLog(LogLevel.Info, message, source);
		}

		public static void Warning(string message, string source = null)
		{
			AddLog(LogLevel.Warning, message, source);
		}

		public static void Error(string message, string source = null)
		{
			AddLog(LogLevel.Error, message, source);
		}

		public static void Exception(Exception ex, string message = null, string source = null)
		{
			string msg = message ?? "发生异常";
			msg += $"\n类型: {ex.GetType().Name}\n消息: {ex.Message}\n堆栈: {ex.StackTrace}";
			AddLog(LogLevel.Error, msg, source);
		}

		#endregion

		#region 内部写入方法（避免递归）

		private static void WriteInfo(string message, string source = null)
		{
			try
			{
				string logFileName = $"Koch_{DateTime.Now:yyyyMMdd}.log";
				string logPath = Path.Combine(_logDirectory, logFileName);
				string logLine = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [INF] {message}";
				if (!string.IsNullOrEmpty(source)) logLine += $" [{source}]";

				lock (_fileLock)
				{
					File.AppendAllText(logPath, logLine + Environment.NewLine, Encoding.UTF8);
				}
				System.Diagnostics.Debug.WriteLine(logLine);
			}
			catch { }
		}

		private static void WriteError(string message, string source = null)
		{
			try
			{
				string logFileName = $"Koch_{DateTime.Now:yyyyMMdd}.log";
				string logPath = Path.Combine(_logDirectory, logFileName);
				string logLine = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff} [ERR] {message}";
				if (!string.IsNullOrEmpty(source)) logLine += $" [{source}]";

				lock (_fileLock)
				{
					File.AppendAllText(logPath, logLine + Environment.NewLine, Encoding.UTF8);
				}
				System.Diagnostics.Debug.WriteLine(logLine);
			}
			catch { }
		}

		#endregion

		public static void Shutdown()
		{
			WriteInfo("日志系统正在关闭...");
			_isRunning = false;
			_workerThread?.Join(3000);
			WriteInfo("日志系统已关闭");
		}

		/// <summary>
		/// 临时降低日志级别（用于高频操作）
		/// </summary>
		public static IDisposable TemporarilyReduceLogLevel()
		{
			return new LogLevelScope(LogLevel.Error);
		}

		private class LogLevelScope : IDisposable
		{
			private LogLevel _previousLevel;

			public LogLevelScope(LogLevel tempLevel)
			{
				_previousLevel = CurrentLogLevel;
				CurrentLogLevel = tempLevel;
			}

			public void Dispose()
			{
				CurrentLogLevel = _previousLevel;
			}
		}
	}
}