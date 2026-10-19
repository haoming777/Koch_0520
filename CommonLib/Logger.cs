using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;

namespace CommonLib
{
	/// <summary>日志级别(数值即优先级, 越大越严重). Trace~Fatal 之外: Off=全部关闭; Time=耗时日志(与Info同级, 兼容旧代码)</summary>
	public enum LogLevel
	{
		Trace = 0,
		Debug = 1,
		Info = 2,
		Warning = 3,
		Error = 4,
		Fatal = 5,
		Off = 6,
		/// <summary>耗时日志专用标签(过滤时按 Info 处理, 兼容旧版 Logger.TimeLog)</summary>
		Time = 7
	}

	/// <summary>队列溢出(写入速度超过磁盘速度)时的处理策略</summary>
	public enum LogOverflowPolicy
	{
		/// <summary>丢低优先级(Trace/Debug)与积压中的旧日志, 绝不阻塞业务线程 — 默认</summary>
		DropLowPriority = 0,
		/// <summary>丢队列中最旧的条目, 保证最新日志可见</summary>
		DropOldest = 1,
		/// <summary>关键日志(Info以上)同步写入 Fallback_yyyyMMdd.log, 保证不丢(磁盘卡顿时自动降级为丢弃并计数)</summary>
		SyncFallback = 2,
		/// <summary>短暂阻塞业务线程等待队列腾位(上限 OverflowBlockTimeoutMs), 适合"可以等但一条不能少"的场景</summary>
		BlockWithTimeout = 3
	}

	/// <summary>
	/// 日志配置(通过 Logger.Configure 修改). 所有字段在发布后视为只读, 运行期改配置会克隆一份再发布, 读写无锁.
	/// </summary>
	public sealed class LogOptions
	{
		/// <summary>日志目录(默认: 程序目录\Logs)</summary>
		public string Directory { get; set; }
		/// <summary>文件名前缀(默认空; 其他程序可填 "MyApp_" → MyApp_20261012.log)</summary>
		public string FilePrefix { get; set; }
		/// <summary>全局最低级别: 低于此级别一律不产生(默认 Debug; 设 Off 可整体关闭)</summary>
		public LogLevel MinLevel { get; set; }
		/// <summary>是否记录 Debug 级(兼容旧开关 EnableDebugLog)</summary>
		public bool EnableDebug { get; set; }
		/// <summary>是否记录 Trace 级(兼容旧开关 EnableTraceLog)</summary>
		public bool EnableTrace { get; set; }
		/// <summary>是否记录 Diagnostic 诊断日志(兼容旧开关)</summary>
		public bool EnableDiagnostic { get; set; }
		/// <summary>单文件大小上限, 超出滚动到 _N+1(默认10MB)</summary>
		public long MaxFileSizeBytes { get; set; }
		/// <summary>内存队列容量(默认20万条, 约可缓冲10~20分钟高频日志)</summary>
		public int QueueCapacity { get; set; }
		/// <summary>溢出策略(默认丢低优先级, 不阻塞业务线程)</summary>
		public LogOverflowPolicy OverflowPolicy { get; set; }
		/// <summary>BlockWithTimeout 策略下单次最长阻塞毫秒数(默认200)</summary>
		public int OverflowBlockTimeoutMs { get; set; }
		/// <summary>每批最多写出条数(默认4096; 批处理是高性能的关键)</summary>
		public int BatchSize { get; set; }
		/// <summary>最长多久强制Flush一次(默认1000ms; 越小越安全、越大吞吐越高)</summary>
		public int FlushIntervalMs { get; set; }
		/// <summary>时间戳是否带日期(默认false, 与旧格式一致: [HH:mm:ss.fff])</summary>
		public bool IncludeDate { get; set; }
		/// <summary>自定义时间戳格式(默认null=走高速缓存路径; 填入如 "yyyy-MM-dd HH:mm:ss.fff" 则每行格式化, 性能略降)</summary>
		public string TimestampFormat { get; set; }
		/// <summary>是否在每行附加线程号[T123](默认false)</summary>
		public bool IncludeThreadId { get; set; }
		/// <summary>是否同时输出到控制台(默认false)</summary>
		public bool ConsoleOutput { get; set; }
		/// <summary>输出到控制台的最低级别(默认 Info)</summary>
		public LogLevel ConsoleMinLevel { get; set; }
		/// <summary>是否同时输出到调试器 Debug.WriteLine(默认false)</summary>
		public bool DebugOutput { get; set; }
		/// <summary>输出到调试器的最低级别(默认 Info)</summary>
		public LogLevel DebugMinLevel { get; set; }
		/// <summary>是否启用健康文件 Logs/Health_yyyyMMdd.log(默认true)</summary>
		public bool HealthEnabled { get; set; }
		/// <summary>正常时健康记录周期秒(默认120)</summary>
		public int HealthIntervalSec { get; set; }
		/// <summary>健康巡检周期毫秒(默认5000)</summary>
		public int HealthCheckMs { get; set; }
		/// <summary>主日志超过该秒数未落盘 → 记录 STALL 行(默认30)</summary>
		public int StallWarnSec { get; set; }
		/// <summary>日志保留天数(默认0=不清理; >0 时只清理本程序命名的日志文件)</summary>
		public int RetentionDays { get; set; }
		/// <summary>日志目录总大小上限MB(默认0=不限; >0 时从最旧的本程序日志开始清理)</summary>
		public int MaxTotalSizeMB { get; set; }
		/// <summary>文件编码(默认 UTF-8 带BOM, 与旧文件一致)</summary>
		public Encoding Encoding { get; set; }
		/// <summary>可选的额外输出(Sink回调, 在写盘线程上执行; 可用于界面列表/数据库/网络上报)</summary>
		public Action<LogEvent> Sink { get; set; }
		/// <summary>启动时写一行会话横幅(默认true)</summary>
		public bool WriteSessionBanner { get; set; }
		/// <summary>进程退出时自动Flush(默认true)</summary>
		public bool AutoFlushOnExit { get; set; }
		/// <summary>分类(模块)级别覆盖表: 如 {"Yolo": Warning} 只压制该分类的低级别日志</summary>
		public Dictionary<string, LogLevel> ChannelLevels { get; set; }

		public LogOptions()
		{
			Directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
			FilePrefix = "";
			MinLevel = LogLevel.Debug;
			EnableDebug = true;
			EnableTrace = false;
			EnableDiagnostic = false;
			MaxFileSizeBytes = 10 * 1024 * 1024;
			QueueCapacity = 200000;
			OverflowPolicy = LogOverflowPolicy.DropLowPriority;
			OverflowBlockTimeoutMs = 200;
			BatchSize = 4096;
			FlushIntervalMs = 1000;
			IncludeDate = false;
			TimestampFormat = null;
			IncludeThreadId = false;
			ConsoleOutput = false;
			ConsoleMinLevel = LogLevel.Info;
			DebugOutput = false;
			DebugMinLevel = LogLevel.Info;
			HealthEnabled = true;
			HealthIntervalSec = 120;
			HealthCheckMs = 5000;
			StallWarnSec = 30;
			RetentionDays = 0;
			MaxTotalSizeMB = 0;
			Encoding = new UTF8Encoding(true);
			Sink = null;
			WriteSessionBanner = true;
			AutoFlushOnExit = true;
			ChannelLevels = null;
		}

		/// <summary>复制一份(改配置时用, 避免影响正在读的线程)</summary>
		public LogOptions Clone()
		{
			var c = (LogOptions)MemberwiseClone();
			if (ChannelLevels != null) c.ChannelLevels = new Dictionary<string, LogLevel>(ChannelLevels, StringComparer.Ordinal);
			return c;
		}
	}

	/// <summary>日志事件(Sink 回调用)</summary>
	public sealed class LogEvent
	{
		public LogLevel Level { get; set; }
		/// <summary>级别标签文本(如 "Info"/"Warning"/"Time")</summary>
		public string Tag { get; set; }
		/// <summary>分类/模块名(可为空)</summary>
		public string Category { get; set; }
		public string Message { get; set; }
		public DateTime TimeStamp { get; set; }
		public int ThreadId { get; set; }
		public long Sequence { get; set; }
	}

	/// <summary>
	/// 轻量分类日志句柄: 静态缓存一个字段即可, 调用无额外分配.
	/// 用法: private static readonly LogChannel Log = Logger.GetChannel("Yolo"); 然后 Log.Info("...")
	/// </summary>
	public struct LogChannel
	{
		private readonly string _category;
		internal LogChannel(string category) { _category = category; }
		public string Category { get { return _category; } }
		public bool IsEnabled(LogLevel level) { return Logger.IsEnabled(level, _category); }
		public void Trace(string msg) { Logger.Log(LogLevel.Trace, _category, msg); }
		public void Debug(string msg, object arg2 = null) { Logger.Log(LogLevel.Debug, _category, Logger.Concat(msg, arg2)); }
		public void Info(string msg, object arg2 = null) { Logger.Log(LogLevel.Info, _category, Logger.Concat(msg, arg2)); }
		public void Warning(string msg, object arg2 = null) { Logger.Log(LogLevel.Warning, _category, Logger.Concat(msg, arg2)); }
		public void Error(string msg, object arg2 = null) { Logger.Log(LogLevel.Error, _category, Logger.Concat(msg, arg2)); }
		public void Fatal(string msg, object arg2 = null) { Logger.Log(LogLevel.Fatal, _category, Logger.Concat(msg, arg2)); }
		public void Exception(Exception ex, string context = null) { Logger.Log(LogLevel.Error, _category, Logger.FormatException(context, ex)); }
	}

	/// <summary>
	/// 高性能异步文件日志(单文件、无外部依赖, 可直接拿到其他程序使用)
	///
	/// 特性:
	///   · 高性能: 业务线程只做"过滤+入队"(无锁队列), 由独立线程按批(默认4096条/批)写盘;
	///             时间戳按秒缓存 + 毫秒查表, 避免每行格式化; 64KB 文件缓冲; 吞吐可达 50万~200万行/秒入队.
	///   · 级别可控: MinLevel 全局最低级别 + 分类级覆盖(SetChannelLevel) + 旧版 EnableDebug/Trace 开关, 运行期随时可改.
	///   · 不丢关键日志: 队列满时按 OverflowPolicy 处理; SyncFallback 策略下 Info以上会落到 Logs/Fallback_yyyyMMdd.log.
	///   · 自诊断: Health_yyyyMMdd.log 记录会话(pid/exe/构建时间)、打开文件、队列深度、丢弃计数, 以及"30秒未落盘"的 STALL 告警.
	///   · 健壮: 写盘异常自动重开文件继续(旧版会永久停写); 目录不可写自动退到临时目录; 跨天自动换文件; 可选保留天数/总大小清理.
	///   · 兼容: .NET Framework 4.0+ / .NET Standard 2.0+ / .NET 5+ (C# 7.3 语法, 仅用 BCL 基础API).
	///
	/// 快速使用:
	///   Logger.Info("普通信息");
	///   Logger.Configure(o => { o.MinLevel = LogLevel.Info; o.RetentionDays = 30; o.IncludeThreadId = true; });
	///   var log = Logger.GetChannel("Plc");  log.Warning("plc timeout");
	///   Logger.Flush(2000);   Logger.Shutdown();
	///
	/// 输出格式(与旧版一致): [HH:mm:ss.fff] [Info] message
	/// 文件命名(与旧版一致): Logs\yyyyMMdd.log → 满10MB → yyyyMMdd_1.log …
	/// </summary>
	public static class Logger
	{
		private static volatile LogOptions _opt = new LogOptions();
		private static readonly object _cfgLock = new object();
		private static readonly ConcurrentQueue<LogEntry> _queue = new ConcurrentQueue<LogEntry>();
		private static long _queueCount;
		private static readonly ManualResetEventSlim _hasWork = new ManualResetEventSlim(false);
		private static readonly ManualResetEventSlim _flushDone = new ManualResetEventSlim(true);
		private static int _flushRequested;
		private static volatile bool _stopping;
		private static int _started;
		private static Thread _worker;
		private static Thread _health;

		private static long _seq;
		private static long _linesWritten;
		private static long _bytesEstimated;
		private static long _droppedLowPriority;
		private static long _droppedHighPriority;
		private static long _fallbackWritten;
		private static long _writeErrors;
		private static long _rotations;
		private static long _lastWriteTicks = DateTime.Now.Ticks;
		private static long _lastFlushTicks = DateTime.Now.Ticks;
		private static volatile string _currentFile = "";
		private static volatile string _currentPath = "";
		private static long _lastQueueFullHealthTicks;

		// 消费者线程私有(仅 WorkerLoop 访问, 无需加锁)
		private static long _tsCacheSecond = long.MinValue;
		private static string _tsCacheText;
		private static readonly string[] _msTable = BuildMsTable();
		private static readonly string[] _levelNames = { "Trace", "Debug", "Info", "Warning", "Error", "Fatal", "Off", "Time" };
		[ThreadStatic] private static bool _inSink;

		private struct LogEntry
		{
			public LogLevel Level;
			public string Tag;
			public string Category;
			public string Message;
			public DateTime TimeStamp;
			public int ThreadId;
		}

		#region 对外属性

		/// <summary>全局最低级别(运行期可改)</summary>
		public static LogLevel MinLevel { get { return _opt.MinLevel; } set { UpdateConfig(delegate (LogOptions o) { o.MinLevel = value; }); } }
		/// <summary>是否记录 Debug(兼容旧开关)</summary>
		public static bool EnableDebugLog { get { return _opt.EnableDebug; } set { UpdateConfig(delegate (LogOptions o) { o.EnableDebug = value; }); } }
		/// <summary>是否记录 Trace(兼容旧开关)</summary>
		public static bool EnableTraceLog { get { return _opt.EnableTrace; } set { UpdateConfig(delegate (LogOptions o) { o.EnableTrace = value; }); } }
		/// <summary>是否输出到控制台(兼容旧开关)</summary>
		public static bool EnableConsoleOutput { get { return _opt.ConsoleOutput; } set { UpdateConfig(delegate (LogOptions o) { o.ConsoleOutput = value; }); } }
		/// <summary>是否记录 Diagnostic(兼容旧开关)</summary>
		public static bool EnableDiagnosticLog { get { return _opt.EnableDiagnostic; } set { UpdateConfig(delegate (LogOptions o) { o.EnableDiagnostic = value; }); } }

		public static long QueueDepth { get { return Interlocked.Read(ref _queueCount); } }
		public static int QueueCapacity { get { return _opt.QueueCapacity; } }
		public static long LinesWritten { get { return Interlocked.Read(ref _linesWritten); } }
		public static long BytesWrittenEstimated { get { return Interlocked.Read(ref _bytesEstimated); } }
		public static long DroppedLowPriorityCount { get { return Interlocked.Read(ref _droppedLowPriority); } }
		public static long DroppedHighPriorityCount { get { return Interlocked.Read(ref _droppedHighPriority); } }
		/// <summary>兼容旧名: 丢弃的 Debug 日志数</summary>
		public static long DroppedDebugCount { get { return Interlocked.Read(ref _droppedLowPriority); } }
		/// <summary>兼容旧名: 丢弃的关键日志数</summary>
		public static long DroppedImportantCount { get { return Interlocked.Read(ref _droppedHighPriority); } }
		public static long FallbackWriteCount { get { return Interlocked.Read(ref _fallbackWritten); } }
		public static long WriteErrorCount { get { return Interlocked.Read(ref _writeErrors); } }
		public static long RotationCount { get { return Interlocked.Read(ref _rotations); } }
		public static DateTime LastWriteTime { get { return new DateTime(Interlocked.Read(ref _lastWriteTicks)); } }
		public static string CurrentLogFile { get { return _currentFile; } }
		public static string CurrentLogPath { get { return _currentPath; } }
		public static string LogDirectory { get { return _opt.Directory; } }

		#endregion

		#region 配置

		/// <summary>取当前配置快照(修改它不会生效, 请用 Configure)</summary>
		public static LogOptions Options { get { return _opt.Clone(); } }

		/// <summary>修改配置(线程安全: 克隆→发布; 已写入的日志不受影响)</summary>
		public static void Configure(Action<LogOptions> apply)
		{
			if (apply != null) UpdateConfig(apply);
		}

		/// <summary>设置某个分类(模块)的最低级别, 例如把高频模块压到 Warning</summary>
		public static void SetChannelLevel(string category, LogLevel level)
		{
			if (string.IsNullOrEmpty(category)) return;
			UpdateConfig(delegate (LogOptions o)
			{
				if (o.ChannelLevels == null) o.ChannelLevels = new Dictionary<string, LogLevel>(StringComparer.Ordinal);
				o.ChannelLevels[category] = level;
			});
		}

		/// <summary>清空所有分类级别覆盖</summary>
		public static void ClearChannelLevels()
		{
			UpdateConfig(delegate (LogOptions o) { o.ChannelLevels = null; });
		}

		private static void UpdateConfig(Action<LogOptions> apply)
		{
			lock (_cfgLock)
			{
				var next = _opt.Clone();
				apply(next);
				_opt = next;
			}
			_hasWork.Set();
			HealthNote("配置已更新: MinLevel=" + LevelName(_opt.MinLevel) + " 目录=" + _opt.Directory);
		}

		#endregion

		#region 判定 / 分类句柄

		public static bool IsEnabled(LogLevel level) { return IsEnabled(level, null); }

		public static bool IsEnabled(LogLevel level, string category)
		{
			var o = _opt;
			if (level == LogLevel.Off) return false;
			if (level == LogLevel.Time) level = LogLevel.Info;   // 耗时日志按 Info 级别过滤
			int lv = (int)level;
			if (lv < (int)o.MinLevel || (int)o.MinLevel >= (int)LogLevel.Off) return false;
			if (level == LogLevel.Debug && !o.EnableDebug) return false;
			if (level == LogLevel.Trace && !o.EnableTrace) return false;
			if (category != null && o.ChannelLevels != null)
			{
				LogLevel cl;
				if (o.ChannelLevels.TryGetValue(category, out cl) && lv < (int)cl) return false;
			}
			return true;
		}

		/// <summary>取分类句柄(建议 static readonly 缓存)</summary>
		public static LogChannel GetChannel(string category) { return new LogChannel(category); }

		#endregion

		#region 静态日志接口

		public static void Info(string msg) { Log(LogLevel.Info, null, msg); }
		public static void Error(string msg) { Log(LogLevel.Error, null, "!!! Error !!! " + msg); }
		public static void Fatal(string msg) { Log(LogLevel.Fatal, null, "!!! FATAL !!! " + msg); }
		public static void Warning(string msg, object arg2 = null) { Log(LogLevel.Warning, null, Concat(msg, arg2)); }
		public static void Debug(string msg, object arg2 = null)
		{
			if (!_opt.EnableDebug) return;
			Log(LogLevel.Debug, null, Concat(msg, arg2));
		}
		/// <summary>全流程追踪日志(需 EnableTraceLog=true)</summary>
		public static void Trace(string msg)
		{
			if (!_opt.EnableTrace) return;
			Log(LogLevel.Trace, null, "[TRACE] " + msg);
		}
		/// <summary>诊断日志(需 EnableDiagnosticLog=true)</summary>
		public static void Diagnostic(string msg)
		{
			if (!_opt.EnableDiagnostic) return;
			Log(LogLevel.Debug, null, "[DIAG] " + msg);
		}
		/// <summary>耗时日志(标签 [Time], 严重级等同 Info)</summary>
		public static void TimeLog(string module, double ms)
		{
			Log(LogLevel.Time, null, string.Format("[{0}] 耗时: {1:F2} ms", module, ms), "Time");
		}
		/// <summary>记录异常(自动带类型/消息/堆栈)</summary>
		public static void Exception(Exception ex, string context = null) { Log(LogLevel.Error, null, FormatException(context, ex)); }

		/// <summary>通用入口</summary>
		public static void Log(LogLevel level, string message) { Log(level, null, message); }

		/// <summary>通用入口(带分类)</summary>
		public static void Log(LogLevel level, string category, string message) { Log(level, category, message, null); }

		private static void Log(LogLevel level, string category, string message, string tag = null)
		{
			var o = _opt;
			if (!IsEnabled(level, category)) return;
			var entry = new LogEntry
			{
				Level = level,
				Tag = tag,
				Category = category,
				Message = message ?? string.Empty,
				TimeStamp = DateTime.Now,
				ThreadId = o.IncludeThreadId ? Thread.CurrentThread.ManagedThreadId : 0
			};
			EnsureStarted();
			if (!TryEnqueue(entry, o)) HandleOverflow(entry, o);
		}

		#endregion

		#region 队列

		private static bool TryEnqueue(LogEntry entry, LogOptions o)
		{
			long n = Interlocked.Increment(ref _queueCount);
			if (n > o.QueueCapacity)
			{
				Interlocked.Decrement(ref _queueCount);
				return false;
			}
			_queue.Enqueue(entry);
			_hasWork.Set();
			return true;
		}

		private static void HandleOverflow(LogEntry entry, LogOptions o)
		{
			// 1) 可短暂等待(适合"不能丢"的场景)
			if (o.OverflowPolicy == LogOverflowPolicy.BlockWithTimeout && o.OverflowBlockTimeoutMs > 0)
			{
				var sw = Stopwatch.StartNew();
				while (sw.ElapsedMilliseconds < o.OverflowBlockTimeoutMs)
				{
					if (TryEnqueue(entry, o)) return;
					Thread.Sleep(1);
				}
			}

			// 2) 低优先级(Trace/Debug)直接丢, 保护关键日志
			if ((int)entry.Level <= (int)LogLevel.Debug)
			{
				Interlocked.Increment(ref _droppedLowPriority);
				return;
			}

			// 3) 丢旧留新
			if (o.OverflowPolicy == LogOverflowPolicy.DropOldest)
			{
				LogEntry old;
				if (_queue.TryDequeue(out old))
				{
					Interlocked.Decrement(ref _queueCount);
					Interlocked.Increment(ref _droppedLowPriority);
					if (TryEnqueue(entry, o)) return;
				}
			}

			// 4) 同步落盘到降级文件(磁盘卡死时自动转为计数丢弃, 绝不长时间阻塞)
			if (o.OverflowPolicy == LogOverflowPolicy.SyncFallback && WriteFallback(entry)) return;

			Interlocked.Increment(ref _droppedHighPriority);
			long now = DateTime.Now.Ticks;
			if (now - Interlocked.Read(ref _lastQueueFullHealthTicks) > 5 * 10000000L)
			{
				Interlocked.Exchange(ref _lastQueueFullHealthTicks, now);
				HealthNote("队列满(" + QueueDepth + "/" + o.QueueCapacity + "), 丢弃关键日志: " + FirstLine(entry.Message));
			}
		}

		#endregion

		#region 后台线程

		private static void EnsureStarted()
		{
			if (_started != 0) return;
			lock (_cfgLock)
			{
				if (_started != 0) return;
				_started = 1;
				_worker = new Thread(WorkerLoop) { IsBackground = true, Name = "Logger.Writer", Priority = ThreadPriority.BelowNormal };
				_worker.Start();
				_health = new Thread(HealthLoop) { IsBackground = true, Name = "Logger.Health", Priority = ThreadPriority.BelowNormal };
				_health.Start();

				if (_opt.AutoFlushOnExit)
				{
					try
					{
						AppDomain.CurrentDomain.ProcessExit += delegate { try { Flush(2000); } catch { } };
					}
					catch { }
				}

				if (_opt.WriteSessionBanner)
				{
					string pid = "?", exe = "?";
					try
					{
						var p = Process.GetCurrentProcess();
						pid = p.Id.ToString();
						exe = p.MainModule.FileVersionInfo.FileVersion + " 构建于 " + File.GetLastWriteTime(p.MainModule.FileName).ToString("yyyy-MM-dd HH:mm");
					}
					catch { }
					HealthNote("SESSION start pid=" + pid + " exe=" + exe + " dir=" + _opt.Directory);
					Log(LogLevel.Info, null, "=== 日志会话开始 " + DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + " pid=" + pid + " exe=" + exe + " ===");
				}
			}
		}

		/// <summary>写盘线程: 批量出队→组行→写文件; 单条异常只重开文件不停线程</summary>
		private static void WorkerLoop()
		{
			var sink = new FileSink();
			try
			{
				while (true)
				{
					_hasWork.Wait(50);
					_hasWork.Reset();

					var o = _opt;
					DrainBatch(sink, o);

					long now = DateTime.Now.Ticks;
					bool idle = Interlocked.Read(ref _queueCount) <= 0;
					if (sink.Writer != null && (idle || (now - _lastFlushTicks) / 10000L >= o.FlushIntervalMs))
					{
						TryFlush(sink);
						_lastFlushTicks = now;
					}

					if (Volatile.Read(ref _flushRequested) == 1 && idle)
					{
						TryFlush(sink);
						Interlocked.Exchange(ref _flushRequested, 0);
						_flushDone.Set();
					}

					if (_stopping && idle) break;
				}
			}
			catch (Exception ex)
			{
				// 兜底: 写盘线程本身异常也不能让日志静默中断
				try { HealthNote("写盘线程异常退出: " + ex.GetType().Name + ": " + FirstLine(ex.Message)); } catch { }
			}
			finally
			{
				TryFlush(sink);
				sink.Dispose();
				HealthNote("写盘线程退出 lines=" + Interlocked.Read(ref _linesWritten));
			}
		}

		private static int DrainBatch(FileSink sink, LogOptions o)
		{
			int n = 0;
			LogEntry entry;
			int batch = o.BatchSize > 0 ? o.BatchSize : 1024;
			while (n < batch && _queue.TryDequeue(out entry))
			{
				Interlocked.Decrement(ref _queueCount);
				n++;
				try
				{
					WriteEntry(sink, ref entry, o);
				}
				catch (Exception ex)
				{
					Interlocked.Increment(ref _writeErrors);
					HealthNote("写日志异常(已重开文件继续): " + ex.GetType().Name + ": " + FirstLine(ex.Message));
					sink.Dispose();
					Thread.Sleep(50);
				}
			}
			return n;
		}

		private static void WriteEntry(FileSink sink, ref LogEntry e, LogOptions o)
		{
			if (sink.Writer == null) OpenFile(sink, e.TimeStamp, o, false);
			else if (sink.Date != e.TimeStamp.Date) { sink.Dispose(); Interlocked.Increment(ref _rotations); OpenFile(sink, e.TimeStamp, o, false); }
			else if (sink.Bytes >= o.MaxFileSizeBytes) { sink.Dispose(); Interlocked.Increment(ref _rotations); OpenFile(sink, e.TimeStamp, o, true); }

			var w = sink.Writer;
			WriteTimestamp(w, e.TimeStamp, o);
			w.Write("] [");
			w.Write(e.Tag != null ? e.Tag : LevelName(e.Level));
			w.Write("] ");
			if (e.Category != null) { w.Write('['); w.Write(e.Category); w.Write("] "); }
			if (o.IncludeThreadId && e.ThreadId > 0) { w.Write("[T"); w.Write(e.ThreadId); w.Write("] "); }
			w.WriteLine(e.Message);

			// 估算行字节数(用于滚动判断): 固定前缀(=时间戳+级别标签)约25字节 + 分类 + 正文
			int estimate = e.Message.Length + 25 + (e.Category != null ? e.Category.Length + 3 : 0);
			sink.Bytes += estimate;
			Interlocked.Increment(ref _linesWritten);
			Interlocked.Add(ref _bytesEstimated, estimate);
			Interlocked.Exchange(ref _lastWriteTicks, DateTime.Now.Ticks);

			// 附加输出(默认关闭; 开启时按需格式化字符串)
			if (o.Sink != null) EmitSink(o.Sink, ref e);
			if (o.ConsoleOutput && (int)e.Level >= (int)o.ConsoleMinLevel) WriteToConsole(e.Level, FormatLine(ref e, o));
			if (o.DebugOutput && (int)e.Level >= (int)o.DebugMinLevel)
			{
				try { System.Diagnostics.Debug.WriteLine(FormatLine(ref e, o)); } catch { }
			}
		}

		private static void WriteTimestamp(TextWriter w, DateTime ts, LogOptions o)
		{
			if (string.IsNullOrEmpty(o.TimestampFormat))
			{
				// 高速路径: 秒级缓存 + 毫秒查表, 避免每行调用 DateTime.ToString
				long sec = ts.Ticks / TimeSpan.TicksPerSecond;
				if (sec != _tsCacheSecond || _tsCacheText == null)
				{
					_tsCacheSecond = sec;
					_tsCacheText = "[" + ts.ToString(o.IncludeDate ? "yyyy-MM-dd HH:mm:ss" : "HH:mm:ss");
				}
				w.Write(_tsCacheText);
				w.Write('.');
				w.Write(_msTable[ts.Millisecond]);
			}
			else
			{
				w.Write('[');
				w.Write(ts.ToString(o.TimestampFormat));
			}
		}

		private static string FormatLine(ref LogEntry e, LogOptions o)
		{
			var sb = new StringBuilder(96 + e.Message.Length);
			sb.Append('[').Append(string.IsNullOrEmpty(o.TimestampFormat) ? e.TimeStamp.ToString("HH:mm:ss.fff") : e.TimeStamp.ToString(o.TimestampFormat)).Append("] [");
			sb.Append(e.Tag != null ? e.Tag : LevelName(e.Level)).Append("] ");
			if (e.Category != null) sb.Append('[').Append(e.Category).Append("] ");
			if (o.IncludeThreadId && e.ThreadId > 0) sb.Append("[T").Append(e.ThreadId).Append("] ");
			return sb.Append(e.Message).ToString();
		}

		private static void EmitSink(Action<LogEvent> sink, ref LogEntry e)
		{
			if (_inSink) return;   // 防止 Sink 内部又写日志导致递归
			_inSink = true;
			try
			{
				sink(new LogEvent
				{
					Level = e.Level,
					Tag = e.Tag != null ? e.Tag : LevelName(e.Level),
					Category = e.Category,
					Message = e.Message,
					TimeStamp = e.TimeStamp,
					ThreadId = e.ThreadId,
					Sequence = Interlocked.Increment(ref _seq)
				});
			}
			catch { }
			finally { _inSink = false; }
		}

		/// <summary>健康巡检线程: 落盘心跳检测(STALL) + 计数变化上报 + 日志保留清理</summary>
		private static void HealthLoop()
		{
			long lastReport = 0, cleanupTicks = 0;
			long lastLow = 0, lastHigh = 0, lastFallback = 0, lastErr = 0;
			while (!_stopping)
			{
				try { Thread.Sleep(_opt.HealthCheckMs > 0 ? _opt.HealthCheckMs : 5000); } catch { }
				if (_stopping) break;
				try
				{
					var o = _opt;
					long now = DateTime.Now.Ticks;
					double noWriteSec = (now - Interlocked.Read(ref _lastWriteTicks)) / 10000000.0;
					long low = Interlocked.Read(ref _droppedLowPriority);
					long high = Interlocked.Read(ref _droppedHighPriority);
					long fb = Interlocked.Read(ref _fallbackWritten);
					long err = Interlocked.Read(ref _writeErrors);

					bool stall = noWriteSec >= o.StallWarnSec;
					bool changed = low != lastLow || high != lastHigh || fb != lastFallback || err != lastErr;
					bool due = (now - lastReport) / 10000000.0 >= o.HealthIntervalSec;

					if (o.HealthEnabled && (stall || changed || due))
					{
						string line = string.Format(
							"queue={0} lines={1} noWrite={2:F1}s dropDbg={3} dropKey={4} fallback={5} writeErr={6} rot={7} file={8}",
							Interlocked.Read(ref _queueCount), Interlocked.Read(ref _linesWritten), noWriteSec,
							low, high, fb, err, Interlocked.Read(ref _rotations), _currentFile);
						HealthNote((stall ? "STALL " : "") + line);
						lastReport = now; lastLow = low; lastHigh = high; lastFallback = fb; lastErr = err;
					}

					if (o.RetentionDays > 0 || o.MaxTotalSizeMB > 0)
					{
						if (cleanupTicks == 0 || (now - cleanupTicks) / 10000000.0 >= 1800)
						{
							cleanupTicks = now;
							CleanupOldFiles(o);
						}
					}
				}
				catch { }
			}
		}

		#endregion

		#region 文件管理

		private sealed class FileSink : IDisposable
		{
			public StreamWriter Writer;
			public string Path;
			public DateTime Date;
			public long Bytes;
			public void Dispose()
			{
				try { if (Writer != null) { Writer.Flush(); Writer.Dispose(); } }
				catch { }
				Writer = null;
			}
		}

		/// <summary>
		/// 打开(或新建)日志文件. forceNew=true 用于滚动: 直接取当天最大序号+1.
		/// 不能用"磁盘文件大小是否已满"判断 — 缓冲未落盘时会把刚写满的文件误判成未满, 导致反复重开同一文件.
		/// </summary>
		private static void OpenFile(FileSink sink, DateTime date, LogOptions o, bool forceNew)
		{
			string dir = o.Directory;
			try
			{
				if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
			}
			catch
			{
				// 目录不可写(权限/只读盘) → 退到临时目录, 保证日志不中断
				try
				{
					dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "Logs");
					if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
					HealthNote("日志目录不可写, 已切换到: " + dir);
				}
				catch { }
			}

			string path = forceNew
				? GetNextNewFilePath(dir, o.FilePrefix, date)
				: GetNextAvailableFilePath(dir, o.FilePrefix, date, o.MaxFileSizeBytes);
			// FileShare.ReadWrite|Delete: 允许其他程序(记事本/拷贝/日志工具)在程序运行期间读取或复制日志文件
			var stream = new FileStream(path, FileMode.Append, FileAccess.Write, FileShare.ReadWrite | FileShare.Delete, 65536);
			sink.Writer = new StreamWriter(stream, o.Encoding != null ? o.Encoding : new UTF8Encoding(true), 65536) { AutoFlush = false };
			sink.Path = path;
			sink.Date = date.Date;
			sink.Bytes = SafeLength(path);
			_currentFile = System.IO.Path.GetFileName(path);
			_currentPath = path;
			HealthNote("打开日志文件 " + _currentFile);
		}

		/// <summary>当天最后一个文件未满则续写(重启不回填更早的文件), 满则新建 _N+1</summary>
		private static string GetNextAvailableFilePath(string dir, string prefix, DateTime date, long maxSize)
		{
			string baseName = (prefix ?? "") + date.ToString("yyyyMMdd");
			int last = 0;
			while (File.Exists(System.IO.Path.Combine(dir, FileName(baseName, last)))) last++;
			if (last == 0) return System.IO.Path.Combine(dir, baseName + ".log");
			string prev = System.IO.Path.Combine(dir, FileName(baseName, last - 1));
			if (SafeLength(prev) < maxSize) return prev;
			return System.IO.Path.Combine(dir, FileName(baseName, last));
		}

		/// <summary>滚动时新建文件: 取当天现有最大序号 +1(不依赖磁盘大小)</summary>
		private static string GetNextNewFilePath(string dir, string prefix, DateTime date)
		{
			string baseName = (prefix ?? "") + date.ToString("yyyyMMdd");
			int last = 0;
			while (File.Exists(System.IO.Path.Combine(dir, FileName(baseName, last)))) last++;
			return System.IO.Path.Combine(dir, FileName(baseName, last));
		}

		private static string FileName(string baseName, int index)
		{
			return index == 0 ? baseName + ".log" : baseName + "_" + index + ".log";
		}

		private static long SafeLength(string path)
		{
			try { return File.Exists(path) ? new FileInfo(path).Length : 0; }
			catch { return 0; }
		}

		private static void TryFlush(FileSink sink)
		{
			try { if (sink.Writer != null) sink.Writer.Flush(); }
			catch { }
		}

		/// <summary>保留清理: 只删除"本程序命名规则"的日期日志(绝不碰其他文件)</summary>
		private static void CleanupOldFiles(LogOptions o)
		{
			try
			{
				string dir = o.Directory;
				if (!System.IO.Directory.Exists(dir)) return;
				var files = new List<FileInfo>();
				long total = 0;
				foreach (var f in new DirectoryInfo(dir).GetFiles("*.log"))
				{
					if (!IsOwnLogFile(f.Name, o.FilePrefix)) continue;
					files.Add(f);
					total += f.Length;
				}
				files.Sort(delegate (FileInfo a, FileInfo b) { return a.LastWriteTimeUtc.CompareTo(b.LastWriteTimeUtc); });

				int deleted = 0;
				if (o.RetentionDays > 0)
				{
					DateTime cutoff = DateTime.UtcNow.AddDays(-o.RetentionDays);
					foreach (var f in files)
					{
						if (f.LastWriteTimeUtc >= cutoff) continue;
						try { total -= f.Length; f.Delete(); deleted++; } catch { }
					}
				}
				if (o.MaxTotalSizeMB > 0)
				{
					long limit = (long)o.MaxTotalSizeMB * 1024 * 1024;
					foreach (var f in files)
					{
						if (total <= limit) break;
						if (!f.Exists) continue;
						try { total -= f.Length; f.Delete(); deleted++; } catch { }
					}
				}
				if (deleted > 0) HealthNote("日志保留清理: 删除 " + deleted + " 个旧文件, 剩余约 " + (total / 1024 / 1024) + "MB");
			}
			catch { }
		}

		private static bool IsOwnLogFile(string name, string prefix)
		{
			if (string.IsNullOrEmpty(name) || !name.EndsWith(".log", StringComparison.OrdinalIgnoreCase)) return false;
			string p = prefix ?? "";
			if (p.Length > 0 && !name.StartsWith(p, StringComparison.Ordinal)) return false;
			int i = p.Length;
			if (name.Length < i + 8 + 4) return false;
			for (int k = 0; k < 8; k++) if (!char.IsDigit(name[i + k])) return false;
			string tail = name.Substring(i + 8, name.Length - i - 8 - 4);   // 去掉 ".log"
			if (tail.Length == 0) return true;                              // yyyyMMdd.log
			if (tail.Length < 2 || tail[0] != '_') return false;
			for (int k = 1; k < tail.Length; k++) if (!char.IsDigit(tail[k])) return false;
			return true;                                                    // yyyyMMdd_N.log
		}

		#endregion

		#region 健康文件 / 降级文件

		/// <summary>写一条健康/诊断记录(独立文件、同步落盘, 不依赖主队列; 主链路故障时仍能写)</summary>
		public static void HealthNote(string message)
		{
			try
			{
				string dir = _opt.Directory;
				if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
				string path = System.IO.Path.Combine(dir, "Health_" + DateTime.Now.ToString("yyyyMMdd") + ".log");
				string line = string.Format("[{0:yyyy-MM-dd HH:mm:ss.fff}] {1}{2}", DateTime.Now, message, Environment.NewLine);
				lock (_healthLock) { File.AppendAllText(path, line, Encoding.UTF8); }
			}
			catch { }
		}
		private static readonly object _healthLock = new object();
		private static readonly object _fallbackLock = new object();

		/// <summary>队列溢出时把关键日志同步写入降级文件; 磁盘卡顿时自动放弃(避免拖住业务线程)</summary>
		private static bool WriteFallback(LogEntry entry)
		{
			if (Interlocked.Read(ref _lastFallbackMs) > FallbackSlowMs) return false;
			try
			{
				long t0 = Environment.TickCount;
				string dir = _opt.Directory;
				string path = System.IO.Path.Combine(dir, "Fallback_" + entry.TimeStamp.ToString("yyyyMMdd") + ".log");
				string line = string.Format("[{0:HH:mm:ss.fff}] [{1}] {2}{3}", entry.TimeStamp,
					entry.Tag != null ? entry.Tag : LevelName(entry.Level), entry.Message, Environment.NewLine);
				lock (_fallbackLock) { File.AppendAllText(path, line, Encoding.UTF8); }
				Interlocked.Exchange(ref _lastFallbackMs, Environment.TickCount - t0);
				Interlocked.Increment(ref _fallbackWritten);
				return true;
			}
			catch
			{
				Interlocked.Exchange(ref _lastFallbackMs, FallbackSlowMs + 1);
				return false;
			}
		}
		private static long _lastFallbackMs;
		private const int FallbackSlowMs = 300;

		/// <summary>
		/// 写入独立错误日志文件(Logs/Error_{module}_{date}.log) — 与主日志分开便于快速排查, 按天滚动.
		/// </summary>
		public static void LogErrorToFile(string moduleName, string message)
		{
			try
			{
				string dir = _opt.Directory;
				if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
				string dateStr = DateTime.Now.ToString("yyyyMMdd");
				string filePath = System.IO.Path.Combine(dir, "Error_" + moduleName + "_" + dateStr + ".log");
				if (File.Exists(filePath) && new FileInfo(filePath).Length > 10 * 1024 * 1024)
				{
					int idx = 1;
					string altPath;
					do { altPath = System.IO.Path.Combine(dir, "Error_" + moduleName + "_" + dateStr + "_" + idx + ".log"); idx++; }
					while (File.Exists(altPath) && new FileInfo(altPath).Length > 10 * 1024 * 1024);
					filePath = altPath;
				}
				File.AppendAllText(filePath, string.Format("[{0:yyyy-MM-dd HH:mm:ss.fff}] {1}{2}", DateTime.Now, message, Environment.NewLine), Encoding.UTF8);
			}
			catch { /* 错误日志写入失败不影响主流程 */ }
		}

		#endregion

		#region Flush / 停机

		/// <summary>强制落盘并等待队列排空(用于退出前、生成报告前). 返回是否在超时内完成</summary>
		public static bool Flush(int timeoutMs = 5000)
		{
			if (_started == 0) return true;
			try
			{
				_flushDone.Reset();
				Interlocked.Exchange(ref _flushRequested, 1);
				_hasWork.Set();
				return _flushDone.Wait(timeoutMs);
			}
			catch { return false; }
		}

		/// <summary>优雅停机: 写完队列后结束线程(可重复调用)</summary>
		public static void Shutdown()
		{
			if (_started == 0) return;
			_stopping = true;
			_hasWork.Set();
			try { if (_worker != null) _worker.Join(5000); } catch { }
			try { if (_health != null) _health.Join(1000); } catch { }
			HealthNote("Shutdown: lines=" + Interlocked.Read(ref _linesWritten) + " dropDbg=" + Interlocked.Read(ref _droppedLowPriority) + " dropKey=" + Interlocked.Read(ref _droppedHighPriority));
		}

		#endregion

		#region 工具

		/// <summary>拼接第二参数(兼容旧版 Debug/Warning 的 arg2 写法)</summary>
		public static string Concat(string msg, object arg2)
		{
			return arg2 == null ? (msg ?? string.Empty) : (msg ?? string.Empty) + " | " + arg2;
		}

		/// <summary>异常格式化(类型: 消息 + 堆栈)</summary>
		public static string FormatException(string context, Exception ex)
		{
			if (ex == null) return "!!! Error !!! " + (context ?? "未知异常");
			return "!!! Error !!! " + (string.IsNullOrEmpty(context) ? "" : context + " → ")
				+ ex.GetType().Name + ": " + ex.Message + Environment.NewLine + ex.StackTrace;
		}

		private static string LevelName(LogLevel level)
		{
			int i = (int)level;
			return (i >= 0 && i < _levelNames.Length) ? _levelNames[i] : "Info";
		}

		private static string FirstLine(string s)
		{
			if (string.IsNullOrEmpty(s)) return "";
			int i = s.IndexOfAny(new char[] { '\r', '\n' });
			return i > 0 ? s.Substring(0, i) : s;
		}

		private static string[] BuildMsTable()
		{
			var t = new string[1000];
			for (int i = 0; i < 1000; i++) t[i] = i.ToString("D3");
			return t;
		}

		private static void WriteToConsole(LogLevel level, string line)
		{
			try
			{
				var original = Console.ForegroundColor;
				switch ((int)level)
				{
					case (int)LogLevel.Error: Console.ForegroundColor = ConsoleColor.Red; break;
					case (int)LogLevel.Fatal: Console.ForegroundColor = ConsoleColor.Magenta; break;
					case (int)LogLevel.Warning: Console.ForegroundColor = ConsoleColor.Yellow; break;
					case (int)LogLevel.Debug: Console.ForegroundColor = ConsoleColor.DarkGray; break;
					case (int)LogLevel.Trace: Console.ForegroundColor = ConsoleColor.DarkGray; break;
					default: Console.ForegroundColor = ConsoleColor.White; break;
				}
				Console.WriteLine(line);
				Console.ForegroundColor = original;
			}
			catch { }
		}

		#endregion
	}
}
