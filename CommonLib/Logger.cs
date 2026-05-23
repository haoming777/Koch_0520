using System;
using System.Collections.Concurrent;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace CommonLib
{
	public enum LogLevel { Debug, Info, Warning, Error, Time }

	public static class Logger
	{
		private static readonly BlockingCollection<LogEntry> _logQueue = new BlockingCollection<LogEntry>();
		private static readonly string _logPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
		private const long MAX_FILE_SIZE = 10 * 1024 * 1024; // 10MB

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

			try
			{
				foreach (var entry in _logQueue.GetConsumingEnumerable())
				{
					string logLine = string.Format("[{0:HH:mm:ss.fff}] [{1}] {2}", entry.Timestamp, entry.Level, entry.Message);

					if (writer == null || !IsFileValid(currentPath))
					{
						if (writer != null) writer.Dispose();
						currentPath = GetNextAvailableFilePath(entry.Timestamp);
						writer = new StreamWriter(currentPath, true, Encoding.UTF8);
						writer.AutoFlush = false;
					}

					writer.WriteLine(logLine);
					if (_logQueue.Count == 0) writer.Flush();

					WriteToConsole(entry.Level, logLine);
				}
			}
			finally
			{
				if (writer != null) writer.Dispose();
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

		private static void Log(LogLevel level, string msg) => _logQueue.Add(new LogEntry { Level = level, Message = msg, Timestamp = DateTime.Now });
		public static void Info(string msg) => Log(LogLevel.Info, msg);
		public static void Error(string msg) => Log(LogLevel.Error, "!!! Error !!! " + msg);
		public static void TimeLog(string module, double ms) => Log(LogLevel.Time, string.Format("[{0}] 耗时: {1:F2} ms", module, ms));

		public static void Debug(string msg, object arg2 = null)
		{
			string finalMsg = (arg2 == null) ? msg : (msg + " | " + arg2);
			Log(LogLevel.Debug, finalMsg);
		}

		public static void Warning(string msg, object arg2 = null)
		{
			string finalMsg = (arg2 == null) ? msg : (msg + " | " + arg2);
			Log(LogLevel.Warning, ">>> WARNING >>> " + finalMsg);
		}

		public static void Shutdown() => _logQueue.CompleteAdding();
	}
}