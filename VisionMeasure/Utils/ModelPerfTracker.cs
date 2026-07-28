using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using CommonLib;

namespace VisionMeasure.Utils
{
    /// <summary>
    /// 周期性统计(默认5分钟): 模型耗时/管道耗时/PLC结果/缺陷分布/事件计数
    /// 只在有采样时输出(机器空闲时静默)
    /// </summary>
    public static class ModelPerfTracker
    {
        private static int _reportIntervalMs = 300000;
        private static Timer _timer;
        private static readonly object _lock = new object();

        // 模型耗时: key = "Station/ModelName"
        private static readonly Dictionary<string, Slot> _modelSlots = new Dictionary<string, Slot>();
        // 管道耗时: key = "Station"
        private static readonly Dictionary<string, PipelineSlot> _pipelineSlots = new Dictionary<string, PipelineSlot>();
        // 缺陷统计: key = "Station/DefectType"
        private static readonly Dictionary<string, long> _defectCounts = new Dictionary<string, long>();
        // PLC结果: key = "Station" → (ok, ng)
        private static readonly Dictionary<string, (long ok, long ng)> _plcResults = new Dictionary<string, (long, long)>();
        // 一般计数: key = "Category/Event"
        private static readonly Dictionary<string, long> _counters = new Dictionary<string, long>();

        private class Slot
        {
            public long Count;
            public double TotalMs;
            public double MinMs = double.MaxValue;
            public double MaxMs;
        }

        private class PipelineSlot
        {
            public long Count;
            public double CropMs, InferMs, DrawMs, SaveMs, TotalMs;
            public double MinTotal = double.MaxValue, MaxTotal;
        }

        /// <summary>记录模型推理耗时</summary>
        public static void Record(string station, string model, double elapsedMs)
        {
            if (elapsedMs <= 0) return;
            string key = station + "/" + model;
            lock (_lock)
            {
                if (!_modelSlots.TryGetValue(key, out var s))
                    _modelSlots[key] = s = new Slot();
                s.Count++;
                s.TotalMs += elapsedMs;
                if (elapsedMs < s.MinMs) s.MinMs = elapsedMs;
                if (elapsedMs > s.MaxMs) s.MaxMs = elapsedMs;
            }
        }

        /// <summary>记录管道耗时</summary>
        public static void RecordPipeline(string station, double cropMs, double inferMs,
            double drawMs, double saveMs, double totalMs)
        {
            lock (_lock)
            {
                if (!_pipelineSlots.TryGetValue(station, out var s))
                    _pipelineSlots[station] = s = new PipelineSlot();
                s.Count++;
                s.CropMs += cropMs;
                s.InferMs += inferMs;
                s.DrawMs += drawMs;
                s.SaveMs += saveMs;
                s.TotalMs += totalMs;
                if (totalMs < s.MinTotal) s.MinTotal = totalMs;
                if (totalMs > s.MaxTotal) s.MaxTotal = totalMs;
            }
        }

        /// <summary>记录缺陷(逐盒NG原因)</summary>
        public static void RecordDefects(string station, List<string> statusList)
        {
            if (statusList == null) return;
            lock (_lock)
            {
                foreach (var s in statusList)
                {
                    if (s == "OK" || string.IsNullOrWhiteSpace(s)) continue;
                    foreach (var def in s.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries))
                    {
                        string d = def.Trim();
                        if (d.Length == 0) continue;
                        string key = station + "/" + d;
                        _defectCounts.TryGetValue(key, out long v);
                        _defectCounts[key] = v + 1;
                    }
                }
            }
        }

        /// <summary>记录PLC发送结果(每工位OK/NG盒数)</summary>
        public static void RecordPlcResult(string station, long okCount, long ngCount)
        {
            lock (_lock)
            {
                if (!_plcResults.TryGetValue(station, out var t))
                    _plcResults[station] = (okCount, ngCount);
                else
                    _plcResults[station] = (t.ok + okCount, t.ng + ngCount);
            }
        }

        /// <summary>累加计数</summary>
        public static void Count(string category, string eventName, long delta = 1)
        {
            string key = category + "/" + eventName;
            lock (_lock)
            {
                _counters.TryGetValue(key, out long v);
                _counters[key] = v + delta;
            }
        }

        public static void Start(int intervalMs = 300000)
        {
            _reportIntervalMs = intervalMs;
            _timer = new Timer(_ => DumpAndReset(), null, _reportIntervalMs, _reportIntervalMs);
            Logger.Info($"[ModelPerf] 启动, 周期={_reportIntervalMs / 60000}min");
        }

        public static void Stop()
        {
            _timer?.Dispose();
            DumpAndReset();
            Logger.Info("[ModelPerf] 已停止");
        }

        private static void DumpAndReset()
        {
            List<(string key, Slot s)> modelSnap;
            List<(string key, PipelineSlot s)> pipeSnap;
            List<(string key, long count)> defectSnap;
            List<(string station, long ok, long ng)> plcSnap;
            List<(string key, long count)> counterSnap;
            lock (_lock)
            {
                modelSnap = _modelSlots
                    .Where(kv => kv.Value.Count > 0)
                    .Select(kv => (kv.Key, new Slot { Count = kv.Value.Count, TotalMs = kv.Value.TotalMs, MinMs = kv.Value.MinMs, MaxMs = kv.Value.MaxMs }))
                    .OrderBy(x => x.Key).ToList();
                pipeSnap = _pipelineSlots
                    .Where(kv => kv.Value.Count > 0)
                    .Select(kv => (kv.Key, new PipelineSlot { Count = kv.Value.Count, CropMs = kv.Value.CropMs, InferMs = kv.Value.InferMs, DrawMs = kv.Value.DrawMs, SaveMs = kv.Value.SaveMs, TotalMs = kv.Value.TotalMs, MinTotal = kv.Value.MinTotal, MaxTotal = kv.Value.MaxTotal }))
                    .OrderBy(x => x.Key).ToList();
                defectSnap = _defectCounts
                    .Where(kv => kv.Value > 0)
                    .Select(kv => (kv.Key, kv.Value))
                    .OrderByDescending(x => x.Value).ThenBy(x => x.Key).ToList();
                plcSnap = _plcResults
                    .Where(kv => kv.Value.ok > 0 || kv.Value.ng > 0)
                    .Select(kv => (kv.Key, kv.Value.ok, kv.Value.ng))
                    .OrderBy(x => x.Key).ToList();
                counterSnap = _counters
                    .Where(kv => kv.Value > 0)
                    .Select(kv => (kv.Key, kv.Value))
                    .OrderBy(x => x.Key).ToList();

                foreach (var kv in _modelSlots.Values) { kv.Count = 0; kv.TotalMs = 0; kv.MinMs = double.MaxValue; kv.MaxMs = 0; }
                foreach (var kv in _pipelineSlots.Values) { kv.Count = 0; kv.CropMs = 0; kv.InferMs = 0; kv.DrawMs = 0; kv.SaveMs = 0; kv.TotalMs = 0; kv.MinTotal = double.MaxValue; kv.MaxTotal = 0; }
                _defectCounts.Clear();
                _plcResults.Clear();
                _counters.Clear();
            }

            bool hasData = modelSnap.Count > 0 || pipeSnap.Count > 0 || defectSnap.Count > 0 || plcSnap.Count > 0 || counterSnap.Count > 0;
            if (!hasData) return;

            Logger.Info("══════════ 周期统计 ══════════");

            // ── 1. PLC 发送结果统计 ──
            if (plcSnap.Count > 0)
            {
                Logger.Info("── PLC 发送统计 ──");
                long totalOk = 0, totalNg = 0;
                foreach (var (st, ok, ng) in plcSnap)
                {
                    Logger.Info($"  {st}: OK={ok}  NG={ng}  良率={(ok + ng > 0 ? (ok * 100.0 / (ok + ng)).ToString("F1") + "%" : "-")}");
                    totalOk += ok; totalNg += ng;
                }
                Logger.Info($"  合计: OK={totalOk}  NG={totalNg}  良率={(totalOk + totalNg > 0 ? (totalOk * 100.0 / (totalOk + totalNg)).ToString("F1") + "%" : "-")}");
            }

            // ── 2. NG 缺陷分布 ──
            if (defectSnap.Count > 0)
            {
                Logger.Info("── NG 缺陷分布 ──");
                foreach (var (key, count) in defectSnap)
                    Logger.Info($"  {key}: {count}");
            }

            // ── 3. 工位管道耗时 ──
            if (pipeSnap.Count > 0)
            {
                Logger.Info("── 工位管道耗时 ──");
                foreach (var (station, s) in pipeSnap)
                {
                    double n = s.Count;
                    Logger.Info($"  [{station}] x{n:F0} | 推理 avg={s.InferMs / n:F1}ms | 总 avg={s.TotalMs / n:F1}ms min={s.MinTotal:F0}ms max={s.MaxTotal:F0}ms");
                }
            }

            // ── 4. 模型推理耗时 ──
            if (modelSnap.Count > 0)
            {
                Logger.Info("── 模型推理耗时 ──");
                Logger.Info($"  {"模型",-28} {"次数",5} {"平均ms",8} {"最小ms",7} {"最大ms",7}");
                foreach (var (key, s) in modelSnap)
                    Logger.Info($"  {key,-28} {s.Count,5} {s.TotalMs / s.Count,8:F1} {s.MinMs,7:F1} {s.MaxMs,7:F1}");
            }

            // ── 5. 事件计数 ──
            if (counterSnap.Count > 0)
            {
                Logger.Info("── 事件计数 ──");
                foreach (var (key, count) in counterSnap)
                    Logger.Info($"  {key}: {count}");
            }

            Logger.Info("══════════════════════════════");
        }
    }
}
