using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using CommonLib;

namespace VisionMeasure.Utils
{
    /// <summary>
    /// System resource monitor: periodically samples CPU/memory and logs reports.
    /// Tracks process-level CPU%, private memory, GC counts, thread count.
    /// Reports every N seconds with avg/peak values.
    /// </summary>
    public class SystemResourceMonitor : IDisposable
    {
        private readonly Timer _sampleTimer;
        private readonly Timer _reportTimer;
        private readonly Process _proc;
        private readonly int _processorCount;
        private TimeSpan _lastTotalProcessorTime;
        private DateTime _lastSampleTime;

        // Accumulators since last report
        private readonly object _lock = new object();
        private readonly List<float> _cpuSamples = new List<float>();
        private readonly List<long> _memSamples = new List<long>();
        private float _cpuPeak;
        private long _memPeak;
        private int _gc0Count, _gc1Count, _gc2Count;

        // Running totals for overall stats
        private float _cpuGrandPeak;
        private long _memGrandPeak;
        private long _sampleCount;
        private double _cpuSum;

        public SystemResourceMonitor(int sampleIntervalMs = 2000, int reportIntervalSec = 30)
        {
            _proc = Process.GetCurrentProcess();
            _processorCount = Environment.ProcessorCount;
            _lastTotalProcessorTime = _proc.TotalProcessorTime;
            _lastSampleTime = DateTime.Now;

            _sampleTimer = new Timer(_ => Sample(), null, sampleIntervalMs, sampleIntervalMs);
            _reportTimer = new Timer(_ => Report(), null, reportIntervalSec * 1000, reportIntervalSec * 1000);

            Logger.Info($"[SysRes] Monitor started: CPU cores={_processorCount}, sample={sampleIntervalMs}ms, report={reportIntervalSec}s");
        }

        private void Sample()
        {
            try
            {
                var now = DateTime.Now;
                var newCpuTime = _proc.TotalProcessorTime;
                var elapsed = (now - _lastSampleTime).TotalMilliseconds;
                if (elapsed < 100) return; // skip first sample

                double cpuUsed = (newCpuTime - _lastTotalProcessorTime).TotalMilliseconds;
                float cpuPct = (float)(cpuUsed / (elapsed * _processorCount) * 100);

                long memBytes = _proc.WorkingSet64;
                long memMB = memBytes / (1024 * 1024);

                lock (_lock)
                {
                    _cpuSamples.Add(cpuPct);
                    _memSamples.Add(memMB);
                    if (cpuPct > _cpuPeak) _cpuPeak = cpuPct;
                    if (memMB > _memPeak) _memPeak = memMB;

                    // Grand stats
                    _sampleCount++;
                    _cpuSum += cpuPct;
                    if (cpuPct > _cpuGrandPeak) _cpuGrandPeak = cpuPct;
                    if (memMB > _memGrandPeak) _memGrandPeak = memMB;
                }

                _lastTotalProcessorTime = newCpuTime;
                _lastSampleTime = now;

                // GC tracking
                var gc0 = GC.CollectionCount(0);
                var gc1 = GC.CollectionCount(1);
                var gc2 = GC.CollectionCount(2);
                Interlocked.Exchange(ref _gc0Count, gc0);
                Interlocked.Exchange(ref _gc1Count, gc1);
                Interlocked.Exchange(ref _gc2Count, gc2);
            }
            catch { /* sampling failure should not crash */ }
        }

        private int _reportSeq;
        private void Report()
        {
            float avgCpu;
            long avgMem;
            float peakCpu;
            long peakMem;
            int count;
            int gc0, gc1, gc2;

            lock (_lock)
            {
                count = _cpuSamples.Count;
                if (count == 0) return;
                avgCpu = _cpuSamples.Count > 0 ? _cpuSamples.Average() : 0;
                avgMem = _memSamples.Count > 0 ? (long)_memSamples.Average() : 0;
                peakCpu = _cpuPeak;
                peakMem = _memPeak;
                _cpuSamples.Clear();
                _memSamples.Clear();
                _cpuPeak = 0;
                _memPeak = 0;
            }

            gc0 = Interlocked.CompareExchange(ref _gc0Count, 0, 0);
            gc1 = Interlocked.CompareExchange(ref _gc1Count, 0, 0);
            gc2 = Interlocked.CompareExchange(ref _gc2Count, 0, 0);
            int threadCount = _proc.Threads.Count;

            _reportSeq++;
            Logger.Info($"[SysRes #{_reportSeq}] CPU avg={avgCpu:F1}% peak={peakCpu:F1}% | " +
                        $"Mem avg={avgMem}MB peak={peakMem}MB | " +
                        $"Threads={threadCount} | GC:0={gc0} 1={gc1} 2={gc2} | " +
                        $"GrandPeak CPU={_cpuGrandPeak:F1}% Mem={_memGrandPeak}MB samples={_sampleCount}");
        }

        public void Dispose()
        {
            _sampleTimer?.Dispose();
            _reportTimer?.Dispose();
            // Final summary
            long grandPeakMem = Interlocked.Read(ref _memGrandPeak);
            Logger.Info($"[SysRes] Shutdown summary: GrandPeak CPU={_cpuGrandPeak:F1}% Mem={grandPeakMem}MB Samples={_sampleCount}");
        }
    }
}
