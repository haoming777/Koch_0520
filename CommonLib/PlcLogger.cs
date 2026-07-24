using System;
using System.IO;
using System.Text;

namespace CommonLib
{
    /// <summary>
    /// PLC通讯专用日志 — 独立文件，与主日志分离，便于排查PLC相关问题
    /// 文件路径: Logs/PLC_yyyyMMdd.log，按天轮转
    /// </summary>
    public static class PlcLogger
    {
        private static readonly object _lock = new object();
        private static string _date = "";

        private static string GetLogPath()
        {
            string today = DateTime.Now.ToString("yyyyMMdd");
            string dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs");
            if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
            return Path.Combine(dir, $"PLC_{today}.log");
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
