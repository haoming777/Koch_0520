using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommonLib;
using Newtonsoft.Json;

namespace Config
{
    /// <summary>
    /// 工位缺陷→PLC信号配置（替代旧 DefectPriorityConfig）
    /// 存储于 Config/StationDefectConfig.json
    /// 逐盒判定: IsReject(bool) + StopLevel(int)
    ///   停机标识优先级: 4 > 3 > 2 > 1 > 0
    ///   剔除优先级: 剔除 > 不剔除
    /// 匹配规则: 按配置顺序遍历，先命中先生效（具体项放前，通用项放后）
    /// </summary>
    public class StationDefectConfig
    {
        #region Singleton
        private static readonly Lazy<StationDefectConfig> _instance =
            new Lazy<StationDefectConfig>(() => Load(), true);

        public static StationDefectConfig Instance => _instance.Value;
        #endregion

        private static readonly string ConfigPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "StationDefectConfig.json");

        public Dictionary<string, StationDefectRule> Stations { get; set; }
            = new Dictionary<string, StationDefectRule>();

        public DefectRuleItem DefaultDefect { get; set; } = new DefectRuleItem
        {
            Name = "*",
            IsReject = false,
            StopLevel = 0
        };

        /// <summary>
        /// 核心方法：将工位逐盒状态列表 → PLC 信号
        /// </summary>
        /// <param name="stationKey">工位键: Front/Back/EndFace/Side/Global</param>
        /// <param name="boxStatusList">P个盒子的状态字符串列表</param>
        /// <param name="rejectBits">按位打包的剔除信号</param>
        /// <param name="stopLevel">最高停机标识 0/1/2/3</param>
        /// <param name="stopReason">停机原因: "盒N:缺陷名(StopLevel=N)" 或 ""</param>
        public void Resolve(string stationKey, List<string> boxStatusList,
            out ushort rejectBits, out int stopLevel, out string stopReason)
        {
            stopReason = "";
            if (boxStatusList == null || boxStatusList.Count == 0)
            {
                rejectBits = 0; stopLevel = 0;
                return;
            }

            if (Stations == null)
            {
                Logger.Error($"[StationDefectConfig] Stations字典为null, 全部返回 OK");
                rejectBits = 0; stopLevel = 0;
                return;
            }
            if (!Stations.TryGetValue(stationKey, out var rule) || rule?.Defects == null || rule.Defects.Count == 0)
            {
                Logger.Warning($"[StationDefectConfig] 工位 '{stationKey}' 无配置, 全部返回 OK");
                rejectBits = 0; stopLevel = 0;
                return;
            }

            var entries = rule.Defects;
            rejectBits = 0;
            stopLevel = 0;

            for (int i = 0; i < boxStatusList.Count && i < 16; i++)
            {
                string status = boxStatusList[i];
                if (string.IsNullOrWhiteSpace(status) || status == "OK")
                    continue;

                // 支持逗号分隔的多缺陷
                var defectNames = status.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries)
                                        .Select(d => d.Trim())
                                        .Where(d => d.Length > 0);

                bool boxReject = false;
                int boxStop = 0;

                foreach (var def in defectNames)
                {
                    var matched = MatchDefect(def, entries);
                    if (matched == null) continue;

                    if (matched.IsReject) boxReject = true;
                    if (matched.StopLevel > boxStop) boxStop = matched.StopLevel;
                }

                if (boxReject) rejectBits |= (ushort)(1 << i);
                if (boxStop > stopLevel)
                {
                    stopLevel = boxStop;
                    stopReason = $"盒{i + 1}:{status}(StopLevel={boxStop})";
                }
            }
        }

        /// <summary>
        /// 按配置顺序匹配单个缺陷名
        /// 1. 精确匹配 (==)
        /// 2. 子串匹配 (Contains) — 兼容动态内容
        /// 3. 都不匹配 → null (记录未匹配日志便于排查)
        /// </summary>
        private DefectRuleItem MatchDefect(string defectName, List<DefectRuleItem> entries)
        {
            // 精确匹配
            foreach (var entry in entries)
            {
                if (string.Equals(defectName, entry.Name, StringComparison.OrdinalIgnoreCase))
                    return entry;
            }

            // 子串匹配（按配置顺序，先命中先生效）
            foreach (var entry in entries)
            {
                if (!string.IsNullOrEmpty(entry.Name) &&
                    defectName.IndexOf(entry.Name, StringComparison.OrdinalIgnoreCase) >= 0)
                    return entry;
            }

            // 未匹配: 记录日志便于排查配置遗漏(每次进程只告警一次, 避免刷屏)
            Logger.Warning($"[StationDefectConfig] 缺陷 '{defectName}' 未匹配任何规则, 默认不剔除不停机 (请检查 StationDefectConfig.json)");
            return null;
        }

        /// <summary>获取某工位所有规则（供 UI 展示）</summary>
        public List<DefectRuleItem> GetDefectEntries(string stationKey)
        {
            if (Stations.TryGetValue(stationKey, out var rule) && rule?.Defects != null)
                return rule.Defects;
            return new List<DefectRuleItem>();
        }

        #region Load / Save
        public static StationDefectConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath, System.Text.Encoding.UTF8);
                    var config = JsonConvert.DeserializeObject<StationDefectConfig>(json);
                    if (config != null)
                    {
                        // 确保 DefaultDefect 不为 null
                        if (config.DefaultDefect == null)
                            config.DefaultDefect = new DefectRuleItem { Name = "*", IsReject = false, StopLevel = 0 };

                        Logger.Info($"[StationDefectConfig] 从 {ConfigPath} 加载成功, 工位数={config.Stations.Count}");
                        return config;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[StationDefectConfig] 加载失败: {ex.Message}, 使用默认配置");
            }

            var defaultConfig = CreateDefault();
            defaultConfig.Save();
            return defaultConfig;
        }

        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(ConfigPath, json, System.Text.Encoding.UTF8);
                Logger.Info($"[StationDefectConfig] 已保存到 {ConfigPath}");
            }
            catch (Exception ex)
            {
                Logger.Error($"[StationDefectConfig] 保存失败: {ex.Message}");
            }
        }

        /// <summary>生成默认配置</summary>
        public static StationDefectConfig CreateDefault()
        {
            var config = new StationDefectConfig();

            config.Stations["Front"] = new StationDefectRule
            {
                StationKey = "Front",
                Defects = new List<DefectRuleItem>
                {
                    new DefectRuleItem { Name = "P号错误", IsReject = true,  StopLevel = 3 },
                    new DefectRuleItem { Name = "P号缺少", IsReject = true,  StopLevel = 3 },
                    new DefectRuleItem { Name = "盒子破损", IsReject = true,  StopLevel = 2 },
                }
            };

            config.Stations["EndFace"] = new StationDefectRule
            {
                StationKey = "EndFace",
                Defects = new List<DefectRuleItem>
                {
                    new DefectRuleItem { Name = "搭舌缺陷", IsReject = true,  StopLevel = 2 },
                    new DefectRuleItem { Name = "破损",     IsReject = true,  StopLevel = 2 },
                    new DefectRuleItem { Name = "边缘问题", IsReject = false, StopLevel = 1 },
                    new DefectRuleItem { Name = "缺少",     IsReject = true,  StopLevel = 2 },
                }
            };

            config.Stations["Side"] = new StationDefectRule
            {
                StationKey = "Side",
                Defects = new List<DefectRuleItem>
                {
                    new DefectRuleItem { Name = "缺陷", IsReject = true, StopLevel = 2 },
                }
            };

            // Back: 具体项在前，通用项在后 — "日期码不完全正确"优先于"日期码"兜底
            config.Stations["Back"] = new StationDefectRule
            {
                StationKey = "Back",
                Defects = new List<DefectRuleItem>
                {
                    new DefectRuleItem { Name = "日期码不完全正确", IsReject = false, StopLevel = 1 },
                    new DefectRuleItem { Name = "日期码",           IsReject = true,  StopLevel = 2 },
                    new DefectRuleItem { Name = "条码缺少",         IsReject = true,  StopLevel = 2 },
                    new DefectRuleItem { Name = "条码错",           IsReject = false, StopLevel = 1 },
                    new DefectRuleItem { Name = "挂钩明显错位",     IsReject = true,  StopLevel = 2 },
                    new DefectRuleItem { Name = "轻微挂钩错位",     IsReject = true,  StopLevel = 2 },
                    new DefectRuleItem { Name = "盒子破损",         IsReject = true,  StopLevel = 2 },
                }
            };

            config.Stations["Global"] = new StationDefectRule
            {
                StationKey = "Global",
                Defects = new List<DefectRuleItem>
                {
                    new DefectRuleItem { Name = "缺料", IsReject = true, StopLevel = 1 },
                }
            };

            return config;
        }
        #endregion
    }

    /// <summary>工位缺陷规则</summary>
    public class StationDefectRule
    {
        public string StationKey { get; set; }
        public List<DefectRuleItem> Defects { get; set; } = new List<DefectRuleItem>();
    }

    /// <summary>单个缺陷规则项</summary>
    public class DefectRuleItem
    {
        /// <summary>匹配名（子串匹配，按序命中）</summary>
        public string Name { get; set; }

        /// <summary>是否剔除</summary>
        public bool IsReject { get; set; }

        /// <summary>停机标识 0/1/2/3/4</summary>
        public int StopLevel { get; set; }
    }
}
