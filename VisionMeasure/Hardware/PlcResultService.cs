using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CommonLib;
using PLC调试.Class;

namespace Hardware
{
    /// <summary>
    /// 工位类型枚举
    /// </summary>
    public enum StationType
    {
        Front = 0,
        Back = 1,
        EndFace = 2,
        Side = 3
    }

    /// <summary>
    /// 统一PLC结果发送服务 — 内部双通道(Modbus TCP / S7-1500)
    /// 根据 setup.ini [plc] ProtocolType 决定当前使用哪个协议。
    /// 发送流程: 各工位处理完成 → SendStationResult(逐盒缺陷码) → SendStationComplete(脉冲Bool)
    /// </summary>
    public class PlcResultService : IDisposable
    {
        private readonly ModbusClass _modbus;
        private readonly S7_1500Class _s7;
        private readonly string _protocolType;
        private readonly bool _simulateMode;
        private bool _disposed;

        // ── 地址配置 ──
        public string FrontCompleteAddr { get; set; }
        public string BackCompleteAddr { get; set; }
        public string EndFaceCompleteAddr { get; set; }
        public string SideCompleteAddr { get; set; }
        public string FrontDefectStartAddr { get; set; }
        public string BackDefectStartAddr { get; set; }
        public string EndFaceDefectStartAddr { get; set; }
        public string SideDefectStartAddr { get; set; }

        /// <summary>完成信号脉冲宽度(ms)</summary>
        public int CompletePulseMs { get; set; } = 50;

        public PlcResultService(ModbusClass modbus = null, S7_1500Class s7 = null, bool simulateMode = true)
        {
            _modbus = modbus;
            _s7 = s7;
            _simulateMode = simulateMode;

            _protocolType = Class_Config._Config.PlcProtocolType ?? "Modbus";

            // 从 setup.ini 读取地址
            FrontCompleteAddr = Class_Config._Config.FrontCompleteAddr ?? "D10000";
            BackCompleteAddr = Class_Config._Config.BackCompleteAddr ?? "D10001";
            EndFaceCompleteAddr = Class_Config._Config.EndFaceCompleteAddr ?? "D10002";
            SideCompleteAddr = Class_Config._Config.SideCompleteAddr ?? "D10003";
            FrontDefectStartAddr = Class_Config._Config.FrontDefectStartAddr ?? "D10100";
            BackDefectStartAddr = Class_Config._Config.BackDefectStartAddr ?? "D10124";
            EndFaceDefectStartAddr = Class_Config._Config.EndFaceDefectStartAddr ?? "D10148";
            SideDefectStartAddr = Class_Config._Config.SideDefectStartAddr ?? "D10172";

            CommonLib.PlcLogger.Info($"[PlcResultService] 初始化, 协议={_protocolType}, 模拟={_simulateMode}");
        }

        /// <summary>发送单工位逐盒缺陷码 (P个Int16)</summary>
        public bool SendStationResult(StationType station, int[] defectCodes, int pCount)
        {
            if (defectCodes == null || defectCodes.Length == 0)
            {
                CommonLib.PlcLogger.Warn($"[PlcResultService] {station}缺陷码为空, 跳过发送");
                return false;
            }

            string startAddr = GetDefectStartAddr(station);
            if (string.IsNullOrEmpty(startAddr))
            {
                CommonLib.PlcLogger.Warn($"[PlcResultService] {station}缺陷码起始地址未配置");
                return false;
            }

            // 截断或补零到P个
            var codes = new int[pCount];
            for (int i = 0; i < pCount; i++)
                codes[i] = (i < defectCodes.Length) ? defectCodes[i] : 0;

            try
            {
                if (_simulateMode)
                {
                    CommonLib.PlcLogger.Info($"[PlcResultService] [模拟] {station} 发送缺陷码: [{string.Join(",", codes)}] P={pCount} → {startAddr}");
                    return true;
                }

                if (_protocolType == "S7-1500" && _s7 != null)
                {
                    // TODO: S7-1500 DB地址格式需后续配置
                    CommonLib.PlcLogger.Info($"[PlcResultService] [S7-1500] {station} 发送缺陷码: [{string.Join(",", codes)}] P={pCount}");
                    return true;
                }
                else if (_modbus != null)
                {
                    _modbus.WriteStationResult(startAddr, codes);
                    CommonLib.PlcLogger.Info($"[PlcResultService] [Modbus] {station} 发送缺陷码: [{string.Join(",", codes)}] P={pCount} → {startAddr}");
                    return true;
                }
                else
                {
                    CommonLib.PlcLogger.Warn($"[PlcResultService] 协议={_protocolType} 但对应实例为null");
                    return false;
                }
            }
            catch (Exception ex)
            {
                CommonLib.PlcLogger.Error($"[PlcResultService] {station}发送缺陷码失败: {ex.Message}");
                return false;  // 防御性: PLC故障不影响检测
            }
        }

        /// <summary>发送工位拍照完成信号 (bool脉冲: true→延时→false)</summary>
        public bool SendStationComplete(StationType station)
        {
            string addr = GetCompleteAddr(station);
            if (string.IsNullOrEmpty(addr))
            {
                CommonLib.PlcLogger.Warn($"[PlcResultService] {station}完成信号地址未配置");
                return false;
            }

            try
            {
                if (_simulateMode)
                {
                    CommonLib.PlcLogger.Info($"[PlcResultService] [模拟] {station} 拍照完成 → {addr}");
                    return true;
                }

                // 脉冲发送: true
                WriteBoolInternal(addr, true);
                // 短暂延时后恢复
                Task.Run(async () =>
                {
                    await Task.Delay(CompletePulseMs);
                    try { WriteBoolInternal(addr, false); }
                    catch (Exception ex) { CommonLib.PlcLogger.Error($"[PlcResultService] 完成信号复位失败: {ex.Message}"); }
                });

                CommonLib.PlcLogger.Info($"[PlcResultService] {station} 拍照完成信号已发送 → {addr}");
                return true;
            }
            catch (Exception ex)
            {
                CommonLib.PlcLogger.Error($"[PlcResultService] {station}发送完成信号失败: {ex.Message}");
                return false;
            }
        }

        private void WriteBoolInternal(string addr, bool value)
        {
            if (_protocolType == "S7-1500" && _s7 != null)
                _s7.WriteBool(addr, value);
            else if (_modbus != null)
                _modbus.WriteBool(addr, value);
        }

        private string GetCompleteAddr(StationType station) => station switch
        {
            StationType.Front => FrontCompleteAddr,
            StationType.Back => BackCompleteAddr,
            StationType.EndFace => EndFaceCompleteAddr,
            StationType.Side => SideCompleteAddr,
            _ => null
        };

        private string GetDefectStartAddr(StationType station) => station switch
        {
            StationType.Front => FrontDefectStartAddr,
            StationType.Back => BackDefectStartAddr,
            StationType.EndFace => EndFaceDefectStartAddr,
            StationType.Side => SideDefectStartAddr,
            _ => null
        };

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            Logger.Info("[PlcResultService] 已释放");
        }
    }
}
