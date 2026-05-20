using Config;
using Models;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Utils;

namespace Hardware
{
	public class CameraTriggerManager : IDisposable
	{
		private readonly MotionControlManager _motion;
		private readonly Dictionary<int, bool> _lastStates = new Dictionary<int, bool>();
		private readonly Dictionary<int, long> _triggerCounts = new Dictionary<int, long>();
		private readonly object _countLock = new object();

		private readonly BlockingCollection<PulseTask> _pulseQueue = new BlockingCollection<PulseTask>(100);
		private volatile bool _isRunning;
		private CancellationTokenSource _cts;
		private Thread _monitorThread;
		private Thread _pulseThread;
		private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
		private bool _simulateMode;

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

			_pulseThread = new Thread(PulseOutputLoop)
			{
				Name = "TrigPulseOut",
				IsBackground = true,
				Priority = ThreadPriority.AboveNormal
			};
			_pulseThread.Start();

			Logger.Info($"相机触发管理器启动 {(_simulateMode ? "(模拟模式)" : "")}");
		}

		public void Stop()
		{
			_isRunning = false;
			_cts?.Cancel();
			_pulseQueue.CompleteAdding();
			_monitorThread?.Join(3000);
			_pulseThread?.Join(3000);
			Logger.Info($"触发管理器停止 总脉冲数={_totalPulses}");
		}

		private long _totalPulses;

		private void MonitorLoop()
		{
			Logger.Info("信号监听线程启动");
			var spinWait = new SpinWait();

			while (!_cts.Token.IsCancellationRequested && _isRunning)
			{
				try
				{
					bool anyTriggered = false;

					foreach (var config in CameraTriggerConfig.TriggerConfigs.Values)
					{
						if (config.InputPort < 0 || config.OutputPort < 0) continue;

						if (!_motion.GetInput(config.InputPort, out bool cur)) continue;

						if (!_lastStates.TryGetValue(config.InputPort, out bool last))
						{
							_lastStates[config.InputPort] = cur;
							continue;
						}

						bool trigger = false;
						if (config.EdgeMode == CameraTriggerConfig.TriggerEdgeMode.RisingEdge)
							trigger = !last && cur;
						else
							trigger = last && !cur;

						if (trigger)
						{
							long timestamp = _stopwatch.ElapsedTicks;

							if (_pulseQueue.TryAdd(new PulseTask
							{
								CameraId = config.CameraId,
								OutputPort = config.OutputPort,
								PulseWidthMs = config.PulseWidthMs,
								Timestamp = timestamp
							}))
							{
								anyTriggered = true;
								Interlocked.Increment(ref _totalPulses);
								lock (_countLock)
								{
									_triggerCounts[config.CameraId] = _triggerCounts.GetValueOrDefault(config.CameraId) + 1;
								}
								OnTriggered?.Invoke(config.CameraId);
							}
						}

						_lastStates[config.InputPort] = cur;
					}

					if (!anyTriggered)
						spinWait.SpinOnce();
					else
						spinWait.Reset();
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
					if (_pulseQueue.TryTake(out var task, 10, _cts.Token))
					{
						SendPulse(task);
					}
				}
				catch (OperationCanceledException) { break; }
				catch (Exception ex)
				{
					Logger.Error($"脉冲输出异常: {ex.Message}");
				}
			}
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