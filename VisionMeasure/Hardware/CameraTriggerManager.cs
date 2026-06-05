using Config;
using Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using VisionMeasure.Utils;
using CommonLib;

namespace Hardware
{
	/// <summary>
	/// 相机触发管理器 — 工业视觉系统的实时信号中枢
	/// 核心职责:
	///   1. 高频扫描(2000Hz) IN4~IN13 输入端口，检测传感器边沿信号
	///   2. 根据边沿信号触发对应相机(通过OUT端口脉冲)
	///   3. 统计各端口触发次数和图片接收量
	/// 线程架构(3个线程):
	///   - MonitorLoop: Highest优先级, 绑定最后CPU核心, 2000Hz边沿扫描
	///   - PulseOutputLoop: AboveNormal优先级, 消费脉冲队列, 精确延时输出
	///   - StatsReportLoop: 每15秒输出触发统计日志
	/// 外部触发模式(ExternalTriggerEnabled=true): 只监听IN13启动侧面工位, 不输出脉冲
	/// </summary>
	public class CameraTriggerManager : IDisposable
	{
		// 核心绑定：把MonitorLoop锁在独立CPU核心上，防止被AI推理抢占
		// SetThreadAffinityMask 将线程绑定到指定CPU核心, 1<<lastCore 表示最后一个核心
		[System.Runtime.InteropServices.DllImport("kernel32.dll")]
		private static extern IntPtr GetCurrentThread();
		[System.Runtime.InteropServices.DllImport("kernel32.dll")]
		private static extern IntPtr SetThreadAffinityMask(IntPtr hThread, IntPtr dwThreadAffinityMask);

		private readonly MotionControlManager _motion;
		/// <summary>上一周期的输入状态(用于边沿检测)</summary>
		private readonly Dictionary<int, bool> _lastStates = new Dictionary<int, bool>();
		/// <summary>各相机触发计数</summary>
		private readonly Dictionary<int, long> _triggerCounts = new Dictionary<int, long>();
		private readonly object _countLock = new object();

		/// <summary>脉冲输出队列(容量100), 生产者=MonitorLoop, 消费者=PulseOutputLoop</summary>
		private readonly BlockingCollection<PulseTask> _pulseQueue = new BlockingCollection<PulseTask>(100);
		private volatile bool _isRunning;
		private CancellationTokenSource _cts;
		private Thread _monitorThread;
		private Thread _pulseThread;
		private Thread _statsThread;
		private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
		private bool _simulateMode;

		// 统计计数器：按输入端口统计边沿检测次数
		private readonly Dictionary<int, long> _inputEdgeCounts = new Dictionary<int, long>();
		private readonly object _statsLock = new object();

		/// <summary>触发事件: 参数为被触发的相机ID(1~8)</summary>
		public event Action<int> OnTriggered;

		public CameraTriggerManager(MotionControlManager motion, bool simulateMode = true)
		{
			_motion = motion;
			_simulateMode = simulateMode;

			foreach (var kvp in CameraTriggerConfig.TriggerConfigs)
				_triggerCounts[kvp.Key] = 0;
		}

	/// <summary>启动触发管理器: 提升进程优先级到High→记录初始IO状态→启动3个线程(TrigMonitor=Highest+CPU绑定 | TrigPulseOut=AboveNormal | TrigStats=Normal)→外部触发模式下跳过PulseOut</summary>
		public void Start()
		{
			_isRunning = true;
			_cts = new CancellationTokenSource();
			_stopwatch.Restart();

			// 提升进程优先级，减少OS抢占MonitorLoop
			try { System.Diagnostics.Process.GetCurrentProcess().PriorityClass = System.Diagnostics.ProcessPriorityClass.High; }
			catch { }

			foreach (var config in CameraTriggerConfig.TriggerConfigs.Values)
			{
				if (config.InputPort >= 0 && _motion.GetInput(config.InputPort, out bool state))
					_lastStates[config.InputPort] = state;
			}

			_monitorThread = new Thread(MonitorLoop)
			{
				Name = "TrigMonitor",
				IsBackground = true,
				Priority = ThreadPriority.Highest
			};
			_monitorThread.Start();

			if (!ExternalTriggerEnabled)
			{
				_pulseThread = new Thread(PulseOutputLoop)
				{
					Name = "TrigPulseOut",
					IsBackground = true,
					Priority = ThreadPriority.AboveNormal
				};
				_pulseThread.Start();
			}

			// 统计报告线程：每15秒输出端口触发统计
			_statsThread = new Thread(StatsReportLoop)
			{
				Name = "TrigStats",
				IsBackground = true
			};
			_statsThread.Start();

			Logger.Info($"相机触发管理器启动 {(_simulateMode ? "(模拟模式)" : "")}");
		}

	/// <summary>停止触发管理器: 取消令牌→CompleteAdding脉冲队列→Join等待3线程退出(各3s超时)→输出总脉冲数</summary>
		public void Stop()
		{
			_isRunning = false;
			_cts?.Cancel();
			_pulseQueue.CompleteAdding();
			_monitorThread?.Join(3000);
			_pulseThread?.Join(3000);
			_statsThread?.Join(3000);
			Logger.Info($"触发管理器停止 总脉冲数={_totalPulses}");
		}

		private long _totalPulses;
		private int _monitorLoopCount;
		// 各相机收到的图片数量 [cameraId]
		public static long[] ImageReceivedCount = new long[9];
		// 最新GetInMulti快照（供SideStation直接读取，避免单独GetInput调用竞争ZMC）
		public static volatile int LastInBits;
		/// <summary>外部触发模式：true=所有OUT脉冲由外部硬件控制，程序只检测IN13启动侧面</summary>
		public static bool ExternalTriggerEnabled = false;

		/// <summary>
		/// 信号监听线程 — 系统实时信号中枢 (Highest优先级, 绑定最后CPU核心)
		/// ★ 2000Hz扫描: GetInMulti(4,13)批量读取10个端口 → 位运算边沿检测 → 入队脉冲
		/// ★ 外部触发模式(ExternalTriggerEnabled=true): 只监听IN13, 不输出脉冲
		/// </summary>
		private void MonitorLoop()
		{
			// 锁定到最后一个CPU核心，AI推理线程用其他核心，MonitorLoop不被抢占
			try
			{
				int lastCore = Environment.ProcessorCount - 1;
				SetThreadAffinityMask(GetCurrentThread(), (IntPtr)(1 << lastCore));
			}
			catch { }
			Logger.Info("信号监听线程启动");
			// 预分配，避免GC
			var newStates = new Dictionary<int, bool>(4);

			while (!_cts.Token.IsCancellationRequested && _isRunning)
			{
				try
				{
					long cycleTs = 0;
					_monitorLoopCount++;

					// 批量读取IN4~IN13（纯忙循环，无SpinWait/Sleep，最大化扫描速率）
					int inBits = _motion.GetInMulti(4, 13);
					LastInBits = inBits;

					newStates.Clear();
					foreach (var config in CameraTriggerConfig.TriggerConfigs.Values)
					{
						if (config.InputPort < 4 || config.InputPort > 13 || config.OutputPort < 0) continue;
						// GetInMulti bit0=IN4, bit6=IN10, bit9=IN13 (相对于起始端口的偏移)
						bool cur = (inBits & (1 << (config.InputPort - 4))) != 0;
						newStates[config.InputPort] = cur;

						if (!_lastStates.TryGetValue(config.InputPort, out bool last))
							continue;

						bool trigger = false;
						if (config.EdgeMode == CameraTriggerConfig.TriggerEdgeMode.RisingEdge)
							trigger = !last && cur;
						else
							trigger = last && !cur;

						if (trigger)
						{
							lock (_statsLock) { _inputEdgeCounts[config.InputPort] = _inputEdgeCounts.GetValueOrDefault(config.InputPort) + 1; }

							bool stationEnabled = true;
							if (config.CameraId <= 2) stationEnabled = VisionMeasure.MainFrm.FrontEnabled;
							else if (config.CameraId <= 4) stationEnabled = VisionMeasure.MainFrm.EndFaceEnabled;
							else if (config.CameraId <= 6) stationEnabled = VisionMeasure.MainFrm.BackEnabled;
							else stationEnabled = VisionMeasure.MainFrm.SideEnabled;
							if (!stationEnabled) continue;

							bool isSideCam = config.CameraId >= 7 && config.CameraId <= 8 && VisionMeasure.MainFrm.SideEnabled;
							if (isSideCam) { OnTriggered?.Invoke(config.CameraId); continue; }

						// 外部触发模式：跳过脉冲队列（硬件负责OUT脉冲）
						if (ExternalTriggerEnabled) { continue; }

							if (cycleTs == 0) cycleTs = _stopwatch.ElapsedTicks;
							long timestamp = cycleTs;
							if (_pulseQueue.TryAdd(new PulseTask
							{
								CameraId = config.CameraId,
								OutputPort = config.OutputPort,
								PulseWidthMs = config.PulseWidthMs,
								Timestamp = timestamp
							}))
							{
								_ = Interlocked.Increment(ref _totalPulses);
								lock (_countLock)
								{
									_triggerCounts[config.CameraId] = _triggerCounts.GetValueOrDefault(config.CameraId) + 1;
								}
								OnTriggered?.Invoke(config.CameraId);
							}
						}
					}
					foreach (var kv in newStates)
						_lastStates[kv.Key] = kv.Value;

					// 控制扫描速率 ≈2000Hz，防止纯忙循环把ZMC打崩
					Thread.SpinWait(2000);
				}
				catch (Exception ex)
				{
					Logger.Error($"信号监听异常: {ex.Message}");
					Thread.Sleep(1);
				}
			}
		}

	/// <summary>脉冲输出循环: 从BlockingCollection取脉冲任务→收集同一时间戳的脉冲批量输出(SetOutMulti位掩码)→PreciseDelay精确延时→关闭所有端口</summary>
		private void PulseOutputLoop()
		{
			Logger.Info("脉冲输出线程启动");
			while (!_cts.Token.IsCancellationRequested)
			{
				try
				{
					if (_pulseQueue.TryTake(out var first, 0))
					{
						// 收集同一时间戳的所有脉冲，并行输出
						var batch = new List<PulseTask> { first };
						long ts = first.Timestamp;
						var putBack = new List<PulseTask>();
						int maxBatch = 10;
						while (maxBatch-- > 0)
						{
							if (!_pulseQueue.TryTake(out var next, 0)) break;
							if (next.Timestamp == ts) batch.Add(next);
							else putBack.Add(next);
						}
						foreach (var t in putBack) _pulseQueue.TryAdd(t);
						if (batch.Count > 1)
							SendPulseBatch(batch);
						else
							SendPulse(first);
					}
					else Thread.SpinWait(30);
				}
				catch (OperationCanceledException) { break; }
				catch (Exception ex) { Logger.Error($"脉冲输出异常: {ex.Message}"); }
			}
		}

	/// <summary>批量脉冲输出: 计算端口范围→构建位掩码数组→SetOutMulti一次性设置多个端口→同时延时→同时关闭, 减少API调用次数</summary>
		private void SendPulseBatch(List<PulseTask> tasks)
		{
			try
			{
				if (_simulateMode) return;
				// 批量输出：位掩码方式，一次API调用设置所有端口
				// ZAux_Direct_SetOutMulti(handle, iofirst, ioend, uint32* istate)
				// istate是位掩码数组，istate[0].bit0=OUT(iofirst+0), bit1=OUT(iofirst+1)...
				int minPort = int.MaxValue, maxPort = 0;
				foreach (var t in tasks)
				{
					if (t.OutputPort < minPort) minPort = t.OutputPort;
					if (t.OutputPort > maxPort) maxPort = t.OutputPort;
				}
				int n = (maxPort - minPort) / 32 + 1;
				uint[] mask = new uint[n];
				foreach (var t in tasks)
				{
					int offset = t.OutputPort - minPort;
					mask[offset / 32] |= (uint)(1 << (offset % 32));
				}
				_motion.SetOutMulti(minPort, maxPort, mask);
				PreciseDelay(tasks[0].PulseWidthMs);
				Array.Clear(mask, 0, mask.Length);
				_motion.SetOutMulti(minPort, maxPort, mask);
			}
			catch (Exception ex) { Logger.Error($"批量脉冲输出失败: {ex.Message}"); try { foreach (var t in tasks) _motion.SetOutput(t.OutputPort, false); } catch { } }
		}

	/// <summary>单脉冲输出: SetOutput(true)→PreciseDelay(pulseWidthMs)→SetOutput(false), 异常时确保输出关闭</summary>
		private void SendPulse(PulseTask task)
		{
			try
			{
				if (_simulateMode)
				{
					Logger.Debug($"模拟模式：Camera{task.CameraId} 脉冲输出");
					return;
				}
				_motion.SetOutput(task.OutputPort, true);
				PreciseDelay(task.PulseWidthMs);
				_motion.SetOutput(task.OutputPort, false);
			}
			catch (Exception ex)
			{
				Logger.Error($"脉冲输出失败 Camera{task.CameraId}: {ex.Message}");
				try { _motion.SetOutput(task.OutputPort, false); } catch { }
			}
		}

	/// <summary>精确延时(微秒级): SpinWait忙等待+Stopwatch高精度计时, 用于相机触发脉冲(10ms脉冲宽度需精确控制)</summary>
		private void PreciseDelay(int milliseconds)
		{
			if (milliseconds <= 0) return;
			long targetTicks = _stopwatch.ElapsedTicks + (milliseconds * Stopwatch.Frequency / 1000);
			var spinWait = new SpinWait();
			while (_stopwatch.ElapsedTicks < targetTicks)
				spinWait.SpinOnce();
		}

		/// <summary>手动触发指定相机(测试用) — 构造PulseTask入队, 返回是否成功入队</summary>
		public bool ManualTrigger(int cameraId)
		{
			var config = CameraTriggerConfig.GetConfig(cameraId);
			if (config == null || config.OutputPort < 0) return false;

			return _pulseQueue.TryAdd(new PulseTask
			{
				CameraId = cameraId,
				OutputPort = config.OutputPort,
				PulseWidthMs = config.PulseWidthMs,
				Timestamp = _stopwatch.ElapsedTicks
			});
		}

		/// <summary>获取各相机触发计数快照(线程安全副本)</summary>
		public Dictionary<int, long> GetCounts()
		{
			lock (_countLock) return new Dictionary<int, long>(_triggerCounts);
		}

		/// <summary>重置所有触发计数为0</summary>
		public void ResetCounts()
		{
			lock (_countLock) _triggerCounts.Clear();
		}

		/// <summary>获取触发统计: 总脉冲数+最大延时(当前返回0)</summary>
		public (long totalPulses, long maxDelayMs) GetStats()
		{
			return (Interlocked.Read(ref _totalPulses), 0);
		}

	/// <summary>统计报告循环(每15s): 快照各端口触发边沿/OUT脉冲/收到图片计数→输出格式化统计表→空闲时(全零)跳过不输出日志</summary>
		private void StatsReportLoop()
		{
			while (!_cts.Token.IsCancellationRequested && _isRunning)
			{
				try { _cts.Token.WaitHandle.WaitOne(15000); } catch { break; }
				if (_cts.Token.IsCancellationRequested || !_isRunning) break;

				// 快照数据（锁内只读，不在锁内写日志阻塞MonitorLoop）
				long totalPulses = Interlocked.Read(ref _totalPulses);
				int loopCount = _monitorLoopCount;
				var rows = new List<string>();
				bool allZero = true;
				lock (_statsLock)
				{
					lock (_countLock)
					{
						foreach (var kv in _triggerCounts.OrderBy(k => k.Key))
						{
							var cfg = CameraTriggerConfig.GetConfig(kv.Key);
							int inPort = cfg?.InputPort ?? 0;
							long edges = _inputEdgeCounts.GetValueOrDefault(inPort);
							long outCount = kv.Value;
							long imgCount = Interlocked.Read(ref ImageReceivedCount[kv.Key]);
							if (edges > 0 || outCount > 0 || imgCount > 0) allZero = false;
							rows.Add($"│ IN{inPort,-2} │ {edges,8} │ Cam{kv.Key} │ {outCount,8} │ {imgCount,8} │");
						}
					}
				}
				// 空闲时跳过（无触发+无图片则不输出日志）
				if (allZero && totalPulses == 0)
					continue;

				Logger.Info("┌──────┬──────────┬──────┬──────────┬──────────┐");
				Logger.Info("│ 端口 │ 触发边沿 │ 相机 │ OUT脉冲  │ 收到图片 │");
				Logger.Info("├──────┼──────────┼──────┼──────────┼──────────┤");
				foreach (var row in rows) Logger.Info(row);
				Logger.Info("└──────┴──────────┴──────┴──────────┴──────────┘");
				Logger.Info($"[触发统计] 总脉冲={totalPulses}  扫描次数={loopCount}");
			}
		}

		/// <summary>释放触发管理器: Stop线程→取消令牌→Dispose队列</summary>
		public void Dispose()
		{
			Stop();
			_cts?.Dispose();
			_pulseQueue?.Dispose();
		}

		private struct PulseTask
		{
			public int CameraId;
			public int OutputPort;
			public int PulseWidthMs;
			public long Timestamp;
		}
	}
}