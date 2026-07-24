using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CommonLib;
using Newtonsoft.Json;

namespace Config
{
    /// <summary>
    /// 缺陷剔除等级配置（三级分类）
    /// 存储于 Config/DefectPriority.json
    /// 逐盒判定: 取该盒所有缺陷中剔除等级最高的值
    ///   1 = OK（合格，无缺陷）
    ///   2 = NG 但不剔除（有缺陷但可接受，如轻微瑕疵）
    ///   3 = NG 需剔除（严重缺陷，必须踢出）
    /// </summary>
    public class DefectPriorityConfig
    {
        public Dictionary<string, List<DefectEntry>> StationDefects { get; set; }
            = new Dictionary<string, List<DefectEntry>>();

        private static readonly string ConfigPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "DefectPriority.json");

        /// <summary>
        /// 根据工位 + 逐盒状态列表 → 逐盒剔除等级(int[])
        /// 每盒取所有缺陷中 RejectLevel 最大值：OK=1, NG不剔除=2, NG需剔除=3
        /// </summary>
        public int[] ResolveCodes(string stationKey, List<string> boxStatusList)
        {
            if (!StationDefects.TryGetValue(stationKey, out var entries) || entries == null)
            {
                Logger.Warning($"[DefectPriority] 工位 '{stationKey}' 无配置, 全部返回1(OK)");
                return boxStatusList.Select(s => 1).ToArray();
            }

            var levelMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var e in entries)
                if (!string.IsNullOrEmpty(e.DefectName))
                    levelMap[e.DefectName] = e.RejectLevel;

            var codes = new int[boxStatusList.Count];
            for (int i = 0; i < boxStatusList.Count; i++)
            {
                string status = boxStatusList[i] ?? "OK";
                codes[i] = ResolveLevel(status, levelMap);
            }
            return codes;
        }

        private int ResolveLevel(string status, Dictionary<string, int> levelMap)
        {
            if (status == "OK" || string.IsNullOrWhiteSpace(status))
                return 1;

            // status 可能是多个缺陷逗号分隔, 如 "条码错误,日期码重影"
            // 取所有缺陷中 RejectLevel 最大值（最严重）
            var defects = status.Split(new[] { ',', '，' }, StringSplitOptions.RemoveEmptyEntries)
                               .Select(d => d.Trim())
                               .Where(d => d.Length > 0);

            int worstLevel = 1;

            foreach (var def in defects)
            {
                // 精确匹配优先, 否则前缀包含匹配
                if (levelMap.TryGetValue(def, out int level))
                {
                    if (level > worstLevel) worstLevel = level;
                }
                else
                {
                    var match = levelMap.FirstOrDefault(e => def.Contains(e.Key));
                    if (!string.IsNullOrEmpty(match.Key) && match.Value > worstLevel)
                        worstLevel = match.Value;
                }
            }

            return worstLevel;
        }

        /// <summary>加载配置, 不存在则生成默认</summary>
        public static DefectPriorityConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    var json = File.ReadAllText(ConfigPath, System.Text.Encoding.UTF8);
                    var config = JsonConvert.DeserializeObject<DefectPriorityConfig>(json);
                    if (config != null)
                    {
                        Logger.Info($"[DefectPriority] 从 {ConfigPath} 加载成功");
                        return config;
                    }
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[DefectPriority] 加载失败: {ex.Message}, 使用默认配置");
            }

            // 生成默认配置（全部NG默认=3需剔除, 后续手动改为2）
            var defaultConfig = CreateDefault();
            defaultConfig.Save();
            return defaultConfig;
        }

        /// <summary>保存到JSON</summary>
        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                File.WriteAllText(ConfigPath, json, System.Text.Encoding.UTF8);
                Logger.Info($"[DefectPriority] 已保存到 {ConfigPath}");
            }
            catch (Exception ex)
            {
                Logger.Error($"[DefectPriority] 保存失败: {ex.Message}");
            }
        }

        /// <summary>生成默认配置：全部NG默认RejectLevel=3(需剔除), OK=1</summary>
        public static DefectPriorityConfig CreateDefault()
        {
            var config = new DefectPriorityConfig();

            config.StationDefects["Front"] = new List<DefectEntry>
            {
                new DefectEntry { DefectName = "OK",     RejectLevel = 1 },
                new DefectEntry { DefectName = "P号错误", RejectLevel = 3 },
                new DefectEntry { DefectName = "P号缺少", RejectLevel = 3 },
                new DefectEntry { DefectName = "盒子破损", RejectLevel = 3 },
            };

            config.StationDefects["Back"] = new List<DefectEntry>
            {
                new DefectEntry { DefectName = "OK",               RejectLevel = 1 },
                new DefectEntry { DefectName = "盒子破损",          RejectLevel = 3 },
                new DefectEntry { DefectName = "条码错误",          RejectLevel = 3 },
                new DefectEntry { DefectName = "条码缺少",          RejectLevel = 3 },
                new DefectEntry { DefectName = "日期码错误",        RejectLevel = 3 },
                new DefectEntry { DefectName = "日期码重影",        RejectLevel = 3 },
                new DefectEntry { DefectName = "日期码不完全正确",  RejectLevel = 3 },
                new DefectEntry { DefectName = "挂钩明显错位",      RejectLevel = 3 },
                new DefectEntry { DefectName = "轻微挂钩错位",      RejectLevel = 3 },
            };

            config.StationDefects["EndFace"] = new List<DefectEntry>
            {
                new DefectEntry { DefectName = "OK",       RejectLevel = 1 },
                new DefectEntry { DefectName = "搭舌缺陷",  RejectLevel = 3 },
                new DefectEntry { DefectName = "边缘问题",  RejectLevel = 3 },
                new DefectEntry { DefectName = "破损",      RejectLevel = 3 },
            };

            config.StationDefects["Side"] = new List<DefectEntry>
            {
                new DefectEntry { DefectName = "OK",   RejectLevel = 1 },
                new DefectEntry { DefectName = "缺陷",  RejectLevel = 3 },
            };

            return config;
        }
    }

    /// <summary>单个缺陷条目 — 映射缺陷名称到剔除等级</summary>
    public class DefectEntry
    {
        /// <summary>缺陷名称（如 "盒子破损", "条码错误"）</summary>
        public string DefectName { get; set; }

        /// <summary>
        /// 剔除等级
        ///   1 = OK（合格，无缺陷）
        ///   2 = NG 但不剔除（有缺陷但可接受）
        ///   3 = NG 需剔除（严重缺陷，必须踢出）
        /// </summary>
        public int RejectLevel { get; set; }
    }
}
