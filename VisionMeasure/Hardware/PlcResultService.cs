using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommonLib;
using PLC调试.Class;
using VisionMeasure.Utils;

namespace Hardware
{
    /// <summary>
    /// 工位类型枚举
    /// </summary>
    public enum StationType
    {
        Front = 0,   // 1# 正面
        EndFace = 1, // 2# 端面
        Side = 2,    // 3# 侧面
        Back = 3     // 4# 背面
    }

    /// <summary>
    /// 统一PLC结果发送服务 — 纯 S7-1500 DB47
    /// 发送流程: 各工位处理完成 → SendStationResult(rejectBits + stopLevel) → SendStationComplete(Bool)
    /// DB47 地址:
    ///   1# 正面: DBW0/DBB8/DBX12.0
    ///   2# 端面: DBW2/DBB9/DBX12.1
    ///   3# 侧面: DBW4/DBB10/DBX12.2
    ///   4# 背面: DBW6/DBB11/DBX12.3
    ///   DBX12.4 = CameraReady Bool
    ///   DBX12.5 = CameraOnline 心跳
    ///   剔除信号: bit=0→OK, bit=1→NG剔除
    /// 日志: 主Logger记录结果摘要(便于关联推断日志)，PlcLogger记录详细地址级写入(便于排查PLC通讯问题)
    /// </summary>
    public class PlcResultService : IDisposable
    {
        private readonly S7_1500Class _s7;
        private readonly bool _simulateMode;
        private bool _disposed;

        // ── DB47 地址常量 ──
        private static readonly string[] FeedbackWordAddrs = { "DB47.DBW0", "DB47.DBW2", "DB47.DBW4", "DB47.DBW6" };
        private static readonly string[] FailureByteAddrs = { "DB47.DBB8", "DB47.DBB9", "DB47.DBB10", "DB47.DBB11" };
        private static readonly string[] WorkDoneBoolAddrs = { "DB47.DBX12.0", "DB47.DBX12.1", "DB47.DBX12.2", "DB47.DBX12.3" };
        private const string CAMERA_READY_ADDR = "DB47.DBX12.4";

        public PlcResultService(S7_1500Class s7 = null, bool simulateMode = true)
        {
            _s7 = s7;
            _simulateMode = simulateMode;

            PlcLogger.Info($"[PlcResultService] 初始化 S7-1500 DB47, 模拟={_simulateMode}");
            Logger.Info($"[PlcResultService] 初始化完成, 协议=S7-1500, 模拟={_simulateMode}");
        }

        /// <summary>发送单工位结果: 剔除位(Word) + 停机标识(Byte)</summary>
        public bool SendStationResult(StationType station, ushort rejectBits, int stopLevel, int pCount)
        {
            int idx = (int)station;
            string wordAddr = FeedbackWordAddrs[idx];
            string byteAddr = FailureByteAddrs[idx];

            byte stopByte = (byte)Math.Min(stopLevel, 4);

            try
            {
                if (_simulateMode)
                {
                    PlcLogger.Info($"[PLC-{station}] [模拟] {wordAddr}=0x{rejectBits:X4}(bit0~{pCount-1}) {byteAddr}={stopByte} P={pCount}");
                    return true;
                }

                if (_s7 == null)
                {
                    PlcLogger.Warn($"[PLC-{station}] S7实例为null, 跳过发送");
                    Logger.Warning($"[PLC-{station}] S7实例为null, 无法发送PLC结果");
                    return false;
                }

                // 1. 写剔除位 Word
                PlcLogger.Info($"[PLC-{station}] → {wordAddr}=0x{rejectBits:X4} (剔除位, P={pCount})");
                _s7.WriteStationResult(wordAddr, new[] { (int)(short)rejectBits });

                // 2. 写停机标识 Byte
                PlcLogger.Info($"[PLC-{station}] → {byteAddr}={stopByte} (停机标识)");
                _s7.WriteByte(byteAddr, stopByte);

                return true;
            }
            catch (Exception ex)
            {
                PlcLogger.Error($"[PLC-{station}] 发送失败: {ex.Message}");
                ModelPerfTracker.Count("PLC", station + "失败");
                Logger.Error($"[PLC-{station}] 发送PLC结果异常: wordAddr={wordAddr} byteAddr={byteAddr} rejectBits=0x{rejectBits:X4} stopLevel={stopByte} err={ex.Message}");
                return false;
            }
        }

        /// <summary>发送工位拍照完成信号 (写 true, PLC端清除)</summary>
        public bool SendStationComplete(StationType station)
        {
            string addr = WorkDoneBoolAddrs[(int)station];

            try
            {
                if (_simulateMode)
                {
                    PlcLogger.Info($"[PLC-{station}] [模拟] {addr}=true (拍照完成)");
                    return true;
                }

                if (_s7 == null)
                {
                    PlcLogger.Warn($"[PLC-{station}] S7实例为null, 跳过完成信号");
                    return false;
                }

                PlcLogger.Info($"[PLC-{station}] → {addr}=true (拍照完成)");
                _s7.WriteBool(addr, true);
                return true;
            }
            catch (Exception ex)
            {
                PlcLogger.Error($"[PLC-{station}] 完成信号发送失败: {ex.Message}");
                Logger.Error($"[PLC-{station}] 发送完成信号异常: addr={addr} err={ex.Message}");
                return false;
            }
        }

        /// <summary>发送全部相机就绪信号(ready=true置位 / ready=false复位)</summary>
        public bool SendCameraReady(bool ready)
        {
            try
            {
                if (_simulateMode)
                {
                    PlcLogger.Info($"[PlcResultService] [模拟] {CAMERA_READY_ADDR}={ready} (CameraReady)");
                    Logger.Info($"[PlcResultService] [模拟] CameraReady={ready} 已发送");
                    return true;
                }

                if (_s7 == null)
                {
                    PlcLogger.Warn("[PlcResultService] S7实例为null, 跳过CameraReady");
                    return false;
                }

                PlcLogger.Info($"[PlcResultService] → {CAMERA_READY_ADDR}={ready} (CameraReady)");
                Logger.Info($"[PlcResultService] CameraReady={ready} 已发送 → " + CAMERA_READY_ADDR);
                _s7.WriteBool(CAMERA_READY_ADDR, ready);
                return true;
            }
            catch (Exception ex)
            {
                PlcLogger.Error($"[PlcResultService] CameraReady发送失败: {ex.Message}");
                Logger.Error($"[PlcResultService] CameraReady发送异常: {ex.Message}");
                return false;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Logger.Info("[PlcResultService] 已释放");
        }
    }
}
