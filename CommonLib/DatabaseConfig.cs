using System;
using System.IO;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace CommonLib
{
    /// <summary>
    /// SQL Server ODBC 数据库连接配置
    /// 存储于 Config/DatabaseConfig.json（带注释，首次运行自动生成）
    /// 连接字符串拼接逻辑：OdbcConnectionString 非空则直接用，否则逐字段拼接
    /// </summary>
    public class DatabaseConfig
    {
        private static readonly string ConfigPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "DatabaseConfig.json");

        // ═══════════════════════════════════════════════════════════
        // 逐字段配置
        // ═══════════════════════════════════════════════════════════

        /// <summary>SQL Server 服务器地址（IP 或主机名）</summary>
        public string OdbcServer { get; set; } = "10.86.126.242";

        /// <summary>数据库名称</summary>
        public string OdbcDatabase { get; set; } = "M600";

        /// <summary>数据库登录用户名</summary>
        public string OdbcUsername { get; set; } = "CSX_AIVision";

        /// <summary>数据库登录密码</summary>
        public string OdbcPassword { get; set; } = "CSX_AIVision";

        /// <summary>连接超时时间（秒），默认 15</summary>
        public int OdbcTimeout { get; set; } = 15;

        /// <summary>
        /// 原始 ODBC 连接字符串（优先级最高）
        /// 如果不为空，则忽略上面的逐字段配置，直接使用此字符串
        /// 格式示例: Driver={ODBC Driver 17 for SQL Server};Server=tcp:10.86.126.242;Database=M600;Uid=sa;Pwd=123;Connect Timeout=15;
        /// </summary>
        public string OdbcConnectionString { get; set; } = "";

        /// <summary>机台号 — 用于调用 F_AI_Running_SKU_Get 存储过程，默认 P1KC0002</summary>
        public string MachineID { get; set; } = "P1KC0002";

        /// <summary>构建最终的 ODBC 连接字符串</summary>
        public string BuildConnectionString()
        {
            // 原始连接字符串优先
            if (!string.IsNullOrWhiteSpace(OdbcConnectionString))
                return OdbcConnectionString;

            int timeout = OdbcTimeout > 0 ? OdbcTimeout : 15;

            return $"Driver={{ODBC Driver 17 for SQL Server}};" +
                   $"Server=tcp:{OdbcServer};" +
                   $"Database={OdbcDatabase};" +
                   $"Uid={OdbcUsername};" +
                   $"Pwd={OdbcPassword};" +
                   $"Connect Timeout={timeout};";
        }

        /// <summary>生成带注释的默认 JSON 文件</summary>
        private static string GetDefaultJsonWithComments()
        {
            return @"{
  // ═══════════════════════════════════════════════════════════
  // SQL Server ODBC 逐字段配置
  // 当 OdbcConnectionString 为空时，使用以下字段拼接连接字符串
  // ═══════════════════════════════════════════════════════════

  // SQL Server 服务器地址（IP 或主机名）
  ""OdbcServer"": ""10.86.126.242"",

  // 数据库名称
  ""OdbcDatabase"": ""M600"",

  // 数据库登录用户名
  ""OdbcUsername"": ""CSX_AIVision"",

  // 数据库登录密码
  ""OdbcPassword"": ""CSX_AIVision"",

  // 连接超时时间（秒），默认 15
  ""OdbcTimeout"": 15,

  // ═══════════════════════════════════════════════════════════
  // 原始 ODBC 连接字符串（优先级最高）
  // 如果不为空，则忽略上面的逐字段配置，直接使用此字符串
  // 格式示例: Driver={ODBC Driver 17 for SQL Server};Server=tcp:10.86.126.242;Database=M600;Uid=sa;Pwd=123;Connect Timeout=15;
  // ═══════════════════════════════════════════════════════════
  ""OdbcConnectionString"": """",

  // ═══════════════════════════════════════════════════════════
  // 机台号 — 用于调用 F_AI_Running_SKU_Get 存储过程
  // 默认 P1KC0002，现场根据实际机台号修改
  // ═══════════════════════════════════════════════════════════
  ""MachineID"": ""P1KC0002""
}";
        }

        /// <summary>从 JSON 文件加载配置，文件不存在则自动生成带注释的默认文件</summary>
        public static DatabaseConfig Load()
        {
            try
            {
                if (File.Exists(ConfigPath))
                {
                    // 使用 JObject 读取带注释的 JSON（Newtonsoft.Json 默认忽略注释）
                    var json = File.ReadAllText(ConfigPath, Encoding.UTF8);
                    var config = JsonConvert.DeserializeObject<DatabaseConfig>(json);

                    if (config != null)
                    {
                        Logger.Info($"[DatabaseConfig] 从 {ConfigPath} 加载成功");
                        return config;
                    }
                }
                else
                {
                    // 首次运行：生成带注释的默认文件
                    var dir = Path.GetDirectoryName(ConfigPath);
                    if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                    File.WriteAllText(ConfigPath, GetDefaultJsonWithComments(), Encoding.UTF8);
                    Logger.Info($"[DatabaseConfig] 已生成默认配置文件: {ConfigPath}");
                    return new DatabaseConfig();
                }
            }
            catch (Exception ex)
            {
                Logger.Error($"[DatabaseConfig] 加载失败: {ex.Message}，使用默认配置");
            }

            return new DatabaseConfig();
        }

        /// <summary>保存配置到 JSON 文件（保留注释：先读取原文件，仅替换字段值）</summary>
        public void Save()
        {
            try
            {
                var dir = Path.GetDirectoryName(ConfigPath);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                // 如果原文件存在且有注释，尝试保留注释
                if (File.Exists(ConfigPath))
                {
                    var original = File.ReadAllText(ConfigPath, Encoding.UTF8);
                    var jObj = JObject.Parse(original);

                    jObj["OdbcServer"] = OdbcServer;
                    jObj["OdbcDatabase"] = OdbcDatabase;
                    jObj["OdbcUsername"] = OdbcUsername;
                    jObj["OdbcPassword"] = OdbcPassword;
                    jObj["OdbcTimeout"] = OdbcTimeout;
                    jObj["OdbcConnectionString"] = OdbcConnectionString;
                    jObj["MachineID"] = MachineID;

                    File.WriteAllText(ConfigPath, jObj.ToString(Formatting.Indented), Encoding.UTF8);
                }
                else
                {
                    // 纯 JSON（无注释），直接序列化
                    var json = JsonConvert.SerializeObject(this, Formatting.Indented);
                    File.WriteAllText(ConfigPath, json, Encoding.UTF8);
                }

                Logger.Info($"[DatabaseConfig] 已保存到 {ConfigPath}");
            }
            catch (Exception ex)
            {
                Logger.Error($"[DatabaseConfig] 保存失败: {ex.Message}");
            }
        }
    }
}
