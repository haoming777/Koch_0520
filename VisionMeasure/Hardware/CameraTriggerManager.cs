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
	public class CameraTriggerManager : IDisposable
	{
		// 核心绑定：把MonitorLoop锁在独立CPU核心上，防止被AI推理抢占
		[System.Runtime.InteropServices.DllImport("kernel32.dll")]
		private static extern IntPtr GetCurrentThread();
		[System.Runtime.InteropServices.DllImport("kernel32.dll")]
		private static extern IntPtr SetThreadAffinityMask(IntPtr hThread, IntPtr dwThreadAffinityMask);

		private readonly MotionControlManager _motion;
		private readonly Dictionary<int, bool> _lastStates = new Dictionary<int, bool>();
		private readonly Dictionary<int, long> _triggerCounts = new Dictionary<int, long>();
		private readonly object _countLock = new object();

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

		public event Action<int> OnTriggered;

		public CameraTriggerManager(MotionControlManager motion, bool simulateMode = true)
		{
			_motion = motion;
			_simulateMode = simulateMode;

			foreach (var kvp in CameraTriggerConfig.TriggerConfigs)
				_triggerCounts[kvp.Key] = 0;
		}

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

		private void PreciseDelay(int milliseconds)
		{
			if (milliseconds <= 0) return;
			long targetTicks = _stopwatch.ElapsedTicks + (milliseconds * Stopwatch.Frequency / 1000);
			var spinWait = new SpinWait();
			while (_stopwatch.ElapsedTicks < targetTicks)
				spinWait.SpinOnce();
		}

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

		public Dictionary<int, long> GetCounts()
		{
			lock (_countLock) return new Dictionary<int, long>(_triggerCounts);
		}

		public void ResetCounts()
		{
			lock (_countLock) _triggerCounts.Clear();
		}

		public (long totalPulses, long maxDelayMs) GetStats()
		{
			return (Interlocked.Read(ref _totalPulses), 0);
		}

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
							rows.Add($"│ IN{inPort,-2} │ {edges,8} │ Cam{kv.Key} │ {outCount,8} │ {imgCount,8} │");
						}
					}
				}
				Logger.Info("┌──────┬──────────┬──────┬──────────┬──────────┐");
				Logger.Info("│ 端口 │ 触发边沿 │ 相机 │ OUT脉冲  │ 收到图片 │");
				Logger.Info("├──────┼──────────┼──────┼──────────┼──────────┤");
				foreach (var row in rows) Logger.Info(row);
				Logger.Info("└──────┴──────────┴──────┴──────────┴──────────┘");
				Logger.Info($"[触发统计] 总脉冲={totalPulses}  扫描次数={loopCount}");
			}
		}

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