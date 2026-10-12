using System;
using System.IO;
using System.Text;

namespace CommonLib
{
    /// <summary>
    /// SKU工单TCP专用日志 — 独立文件, 记录SKU切换完整轨迹(TCP自动/手动触发、新旧SKU对比、失败原因)
    /// 文件路径: Logs/SkuRecord_yyyyMMdd.log，按天轮转
    /// </summary>
    public static class SkuTcpLogger
    {
        private static readonly object _lock = new object();

        private static string GetLogPath()
        {
            string today = DateTime.Now.ToString("yyyyMMdd");
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return Path.Combine(dir, $"SkuRecord_{today}.log");
        }

        private static void Write(string level, string message)
        {
            lock (_lock)
            {
                try
                {
                    string now = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
                    string line = $"{now} [{level}] {message}{Environment.NewLine}";
                    File.AppendAllText(GetLogPath(), line, Encoding.UTF8);
                }
                catch { /* 日志故障不影响主流程 */ }
            }
        }

        public static void Info(string message) => Write("INFO", message);
        public static void Warn(string message) => Write("WARN", message);
        public static void Error(string message) => Write("ERROR", message);
    }
}
