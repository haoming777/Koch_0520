using System;
using System.Data;
using System.Data.Odbc;
using System.Diagnostics;
using CommonLib;

namespace VisionMeasure.Services
{
    /// <summary>
    /// ODBC 数据库访问服务 — 所有方法均为同步（由调用方用 Task.Run 包裹）
    /// 原因：System.Data.Odbc 的 Async 方法底层是同步阻塞实现，必须放后台线程
    /// 严格参照测试SQLServer参考项目的 OdbcDataService 实现
    /// </summary>
    public class OdbcDatabaseService
    {
        /// <summary>
        /// 测试数据库连接（同步方法，请在 Task.Run 中调用）
        /// </summary>
        public bool TestConnection(string connectionString)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                // 输出连接字符串（隐藏密码），方便排查问题
                Logger.Info($"[OdbcDB] 测试连接 → {MaskPassword(connectionString)}");

                using (var connection = new OdbcConnection(connectionString))
                {
                    connection.Open();
                }

                sw.Stop();
                Logger.Info($"[OdbcDB] 连接成功，耗时 {sw.ElapsedMilliseconds}ms");
                return true;
            }
            catch (OdbcException ex)
            {
                sw.Stop();
                var messages = new System.Collections.Generic.List<string>();
                foreach (OdbcError err in ex.Errors)
                    messages.Add($"[{err.SQLState}] {err.Message}");
                Logger.Error($"[OdbcDB] 连接失败({sw.ElapsedMilliseconds}ms): {string.Join(" | ", messages)}");
                return false;
            }
            catch (Exception ex)
            {
                sw.Stop();
                Logger.Error($"[OdbcDB] 连接异常({sw.ElapsedMilliseconds}ms): {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// 执行查询并返回结果集（同步方法，请在 Task.Run 中调用）
        /// 使用参数化查询防 SQL 注入
        /// </summary>
        /// <param name="connectionString">ODBC 连接字符串</param>
        /// <param name="query">SQL 查询语句，使用 ? 作为参数占位符</param>
        /// <param name="parameters">参数值数组，按顺序对应 ? 占位符</param>
        /// <returns>查询结果 DataTable，失败返回 null</returns>
        public DataTable ExecuteQuery(string connectionString, string query, params string[] parameters)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                // 输出查询信息（隐藏密码），方便排查问题
                Logger.Info($"[OdbcDB] 执行查询 → {MaskPassword(connectionString)}");
                Logger.Info($"[OdbcDB] SQL: {query}");
                Logger.Info($"[OdbcDB] 参数: [{(parameters.Length > 0 ? string.Join(", ", parameters) : "无")}]");

                using (var connection = new OdbcConnection(connectionString))
                {
                    connection.Open();

                    using (var command = new OdbcCommand(query, connection))
                    {
                        command.CommandTimeout = 60;

                        // 添加参数防注入
                        for (int i = 0; i < parameters.Length; i++)
                        {
                            command.Parameters.AddWithValue($"@p{i}", parameters[i] ?? (object)DBNull.Value);
                        }

                        var dataTable = new DataTable();
                        using (var reader = command.ExecuteReader())
                        {
                            dataTable.Load(reader);
                        }

                        sw.Stop();
                        Logger.Info($"[OdbcDB] 查询成功：{dataTable.Rows.Count} 行，耗时 {sw.ElapsedMilliseconds}ms");
                        return dataTable;
                    }
                }
            }
            catch (OdbcException ex)
            {
                sw.Stop();
                var messages = new System.Collections.Generic.List<string>();
                foreach (OdbcError err in ex.Errors)
                    messages.Add($"[{err.SQLState}] {err.Message}");
                Logger.Error($"[OdbcDB] 查询失败({sw.ElapsedMilliseconds}ms): {string.Join(" | ", messages)}");
                return null;
            }
            catch (Exception ex)
            {
                sw.Stop();
                Logger.Error($"[OdbcDB] 查询异常({sw.ElapsedMilliseconds}ms): {ex.Message}");
                return null;
            }
        }

        /// <summary>隐藏连接字符串中的密码字段，用于日志输出</summary>
        private static string MaskPassword(string connectionString)
        {
            if (string.IsNullOrEmpty(connectionString)) return connectionString;

            // 隐藏 Pwd= 后面的内容
            var idx = connectionString.IndexOf("Pwd=", StringComparison.OrdinalIgnoreCase);
            if (idx < 0) return connectionString;

            var start = idx + 4;
            var end = connectionString.IndexOf(';', start);
            if (end < 0) end = connectionString.Length;

            return connectionString.Substring(0, start) + "****" + connectionString.Substring(end);
        }
    }
}
