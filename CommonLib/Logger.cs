using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CommonLib
{
	public enum LogLevel { Debug, Info, Warning, Error, Time }

	public static class Logger
	{
		private const int MAX_QUEUE_SIZE = 20000;  // 队列上限, 超过则Add阻塞(背压防内存爆炸)
		private static readonly BlockingCollection<LogEntry> _logQueue = new BlockingCollection<LogEntry>(MAX_QUEUE_SIZE);
		public static bool EnableDebugLog { get; set; } = true;
		/// <summary>全流程追踪日志开关（默认关闭，调试时打开）</summary>
		public static bool EnableTraceLog { get; set; } = false;
		/// <summary>控制台输出开关（默认关闭，高频触发时控制台IO会阻塞线程）</summary>
		public static bool EnableConsoleOutput { get; set; } = false;
		private static readonly string _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
		private const long MAX_FILE_SIZE = 10 * 1024 * 1024; // 10MB
		private static long _lastFileSizeCheck = 0;  // 文件大小缓存, 避免每次写日志都查磁盘
		private static long _lastFlushTicks = DateTime.Now.Ticks;
		private const long FLUSH_INTERVAL_TICKS = 3 * 10000000L;  // 每3秒强制Flush
		private static long _droppedLogCount = 0;  // 因队列满丢弃的日志数

		private struct LogEntry
		{
			public LogLevel Level;
			public string Message;
			public DateTime Timestamp;
		}

		static Logger()
		{
			if (!Directory.Exists(_logPath)) Directory.CreateDirectory(_logPath);
			Task.Factory.StartNew(ProcessQueue, TaskCreationOptions.LongRunning);
		}

		private static void ProcessQueue()
		{
			StreamWriter writer = null;
			string currentPath = "";
			long bytesWritten = 0;  // 跟踪已写入字节数, 避免频繁查磁盘

			try
			{
				foreach (var entry in _logQueue.GetConsumingEnumerable())
				{
					string logLine = string.Format("[{0:HH:mm:ss.fff}] [{1}] {2}", entry.Timestamp, entry.Level, entry.Message);

					// 文件管理: 只在必要时检查(每1MB或首次)
					if (writer == null || bytesWritten > 1048576)
					{
						if (writer != null && new FileInfo(currentPath).Length >= MAX_FILE_SIZE)
						{
							writer.Dispose();
							writer = null;
						}
						bytesWritten = 0;
					}
					if (writer == null)
					{
						currentPath = GetNextAvailableFilePath(entry.Timestamp);
						writer = new StreamWriter(currentPath, true, Encoding.UTF8) { AutoFlush = false };
					}

					writer.WriteLine(logLine);
					bytesWritten += Encoding.UTF8.GetByteCount(logLine) + 2;

					// 每3秒强制Flush(防止高频时永远不Flush导致崩溃丢数据)
					long nowTicks = DateTime.Now.Ticks;
					if (nowTicks - _lastFlushTicks > FLUSH_INTERVAL_TICKS)
					{
						writer.Flush();
						_lastFlushTicks = nowTicks;
					}
					// 队列空时也Flush
					if (_logQueue.Count == 0) { writer.Flush(); _lastFlushTicks = nowTicks; }

					if (EnableConsoleOutput) WriteToConsole(entry.Level, logLine);

					// 队列积压告警(>15000条)
					if (_logQueue.Count > 15000 && _logQueue.Count % 5000 == 0)
						System.Diagnostics.Debug.WriteLine("[Logger] queue depth=" + _logQueue.Count);
				}
			}
			finally
			{
				if (writer != null) { writer.Flush(); writer.Dispose(); }
			}
		}

		private static bool IsFileValid(string path)
		{
			if (string.IsNullOrEmpty(path) || !File.Exists(path)) return false;
			return new FileInfo(path).Length < MAX_FILE_SIZE;
		}

		private static string GetNextAvailableFilePath(DateTime date)
		{
			int index = 0;
			string baseName = date.ToString("yyyyMMdd");
			string path;
			do
			{
				path = Path.Combine(_logPath, index == 0 ? $"{baseName}.log" : $"{baseName}_{index}.log");
				index++;
			} while (File.Exists(path) && new FileInfo(path).Length >= MAX_FILE_SIZE);
			return path;
		}

		private static void WriteToConsole(LogLevel level, string msg)
		{
			var originalColor = Console.ForegroundColor;
			switch (level)
			{
				case LogLevel.Error: Console.ForegroundColor = ConsoleColor.Red; break;
				case LogLevel.Warning: Console.ForegroundColor = ConsoleColor.Yellow; break;
				case LogLevel.Time: Console.ForegroundColor = ConsoleColor.Cyan; break;
				case LogLevel.Debug: Console.ForegroundColor = ConsoleColor.DarkGray; break;
				default: Console.ForegroundColor = ConsoleColor.White; break;
			}
			Console.WriteLine(msg);
			Console.ForegroundColor = originalColor;
		}

		/// <summary>入队日志: Debug队列满时丢弃(不阻塞), Info/Warning/Error等待最多100ms(避免丢失关键日志)</summary>
		private static void Log(LogLevel level, string msg)
		{
			var entry = new LogEntry { Level = level, Message = msg, Timestamp = DateTime.Now };
			if (level == LogLevel.Debug)
			{
				// Debug日志: 队列满则丢弃(避免阻塞高频调用)
				if (!_logQueue.TryAdd(entry))
					Interlocked.Increment(ref _droppedLogCount);
			}
			else
			{
				// Info/Warning/Error: 尝试Add, 超时100ms仍失败则丢弃
				if (!_logQueue.TryAdd(entry, 100))
				{
					Interlocked.Increment(ref _droppedLogCount);
					System.Diagnostics.Debug.WriteLine("[Logger] DROPPED " + level + ": " + msg.Substring(0, Math.Min(100, msg.Length)));
				}
			}
		}
		public static void Info(string msg) => Log(LogLevel.Info, msg);
		public static void Error(string msg) => Log(LogLevel.Error, "!!! Error !!! " + msg);
		public static void TimeLog(string module, double ms) => Log(LogLevel.Time, string.Format("[{0}] 耗时: {1:F2} ms", module, ms));

		public static void Debug(string msg, object arg2 = null)
		{
			if (!EnableDebugLog) return;
			string finalMsg = (arg2 == null) ? msg : (msg + " | " + arg2);
			Log(LogLevel.Debug, finalMsg);
		}

		/// <summary>全流程追踪日志（仅在EnableTraceLog=true时输出）</summary>
		public static void Trace(string msg)
		{
			if (!EnableTraceLog) return;
			Log(LogLevel.Debug, "[TRACE] " + msg);
		}

		public static void Warning(string msg, object arg2 = null)
		{
			string finalMsg = (arg2 == null) ? msg : (msg + " | " + arg2);
			Log(LogLevel.Warning, ">>> WARNING >>> " + finalMsg);
		}

		/// <summary>
		/// 写入独立错误日志文件(Logs/Error_{moduleName}_{date}.log)
		/// 用于存储关键异常告警(连续缺少、相机掉线、运动异常等), 与主日志分开便于快速排查
		/// 文件按天滚动, 单文件最大10MB
		/// </summary>
		public static void LogErrorToFile(string moduleName, string message)
		{
			try
			{
				string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
				if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
				string dateStr = DateTime.Now.ToString("yyyyMMdd");
				string filePath = Path.Combine(dir, $"Error_{moduleName}_{dateStr}.log");
				// 文件超过10MB则轮转
				if (File.Exists(filePath) && new FileInfo(filePath).Length > 10 * 1024 * 1024)
				{
					int idx = 1;
					string altPath;
					do { altPath = Path.Combine(dir, $"Error_{moduleName}_{dateStr}_{idx}.log"); idx++; }
					while (File.Exists(altPath) && new FileInfo(altPath).Length > 10 * 1024 * 1024);
					filePath = altPath;
				}
				File.AppendAllText(filePath,
					$"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}",
					Encoding.UTF8);
			}
			catch { /* 错误日志写入失败不应影响主流程 */ }
		}

		/// <summary>输出诊断日志(仅EnableDiagnosticLog=true时), 用于性能分析和问题定位</summary>
		public static bool EnableDiagnosticLog { get; set; } = false;
		public static void Diagnostic(string msg)
		{
			if (!EnableDiagnosticLog) return;
			Log(LogLevel.Debug, "[DIAG] " + msg);
		}

		public static void Shutdown() => _logQueue.CompleteAdding();
	}
}