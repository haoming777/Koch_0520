using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Text;
using Models;
using VisionMeasure.Utils;using CommonLib;

namespace CommonLib
{
	/// <summary>
	/// SQLite数据库辅助类 - 用于生产记录存储
	/// </summary>
	public class SQLiteHelper
	{
		private string _databasePath;
		private string _connectionString;
		private readonly object _lock = new object();

		public SQLiteHelper(string databasePath = null)
		{
			if (string.IsNullOrEmpty(databasePath))
			{
				string dataDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data");
				if (!Directory.Exists(dataDir))
					Directory.CreateDirectory(dataDir);
				_databasePath = Path.Combine(dataDir, "ProductionRecord.db");
			}
			else
			{
				_databasePath = databasePath;
			}

			_connectionString = $"Data Source={_databasePath};Version=3;";

			InitializeDatabase();
		}

		/// <summary>
		/// 初始化数据库表结构
		/// </summary>
		private void InitializeDatabase()
		{
			lock (_lock)
			{
				using (var conn = new SQLiteConnection(_connectionString))
				{
					conn.Open();

					// 生产记录表
					string createProductionTable = @"
                        CREATE TABLE IF NOT EXISTS ProductionRecords (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            ProductId INTEGER NOT NULL,
                            CreateTime DATETIME NOT NULL,
                            Shift TEXT NOT NULL,
                            ShiftStartTime DATETIME,
                            ShiftEndTime DATETIME,
                            FrontResult INTEGER,
                            BackResult INTEGER,
                            EndFaceResult INTEGER,
                            SideResult INTEGER,
                            FinalResult INTEGER NOT NULL,
                            FrontDefects TEXT,
                            BackDefects TEXT,
                            EndFaceDefects TEXT,
                            SideDefects TEXT,
                            ProcessingTimeMs INTEGER,
                            UNIQUE(ProductId)
                        )";

					// 缺陷统计表
					string createDefectStatsTable = @"
                        CREATE TABLE IF NOT EXISTS DefectStatistics (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            Shift TEXT NOT NULL,
                            ShiftStartTime DATETIME NOT NULL,
                            ShiftEndTime DATETIME,
                            TotalCount INTEGER DEFAULT 0,
                            OkCount INTEGER DEFAULT 0,
                            NgCount INTEGER DEFAULT 0,
                            FrontOkCount INTEGER DEFAULT 0,
                            FrontNgCount INTEGER DEFAULT 0,
                            BackOkCount INTEGER DEFAULT 0,
                            BackNgCount INTEGER DEFAULT 0,
                            EndFaceOkCount INTEGER DEFAULT 0,
                            EndFaceNgCount INTEGER DEFAULT 0,
                            SideOkCount INTEGER DEFAULT 0,
                            SideNgCount INTEGER DEFAULT 0,
                            FrontPCodeErrorCount INTEGER DEFAULT 0,
                            FrontBoxBreakCount INTEGER DEFAULT 0,
                            FrontFilmBreakCount INTEGER DEFAULT 0,
                            BackBarcodeErrorCount INTEGER DEFAULT 0,
                            BackDateCodeErrorCount INTEGER DEFAULT 0,
                            BackHookErrorCount INTEGER DEFAULT 0,
                            BackCutCharErrorCount INTEGER DEFAULT 0,
                            EndFaceDamageCount INTEGER DEFAULT 0,
                            EndFaceFlapCount INTEGER DEFAULT 0,
                            EndFaceEdgeCount INTEGER DEFAULT 0,
                            SideWrinkleCount INTEGER DEFAULT 0,
                            SideDamageCount INTEGER DEFAULT 0,
                            SideBurstCount INTEGER DEFAULT 0,
                            CreateTime DATETIME DEFAULT CURRENT_TIMESTAMP
                        )";

					// 班次统计表
					string createShiftStatsTable = @"
                        CREATE TABLE IF NOT EXISTS ShiftStatistics (
                            Id INTEGER PRIMARY KEY AUTOINCREMENT,
                            Shift TEXT NOT NULL,
                            Date TEXT NOT NULL,
                            TotalCount INTEGER DEFAULT 0,
                            OkCount INTEGER DEFAULT 0,
                            NgCount INTEGER DEFAULT 0,
                            YieldRate REAL DEFAULT 0,
                            StartTime DATETIME,
                            EndTime DATETIME,
                            CreateTime DATETIME DEFAULT CURRENT_TIMESTAMP,
                            UNIQUE(Shift, Date)
                        )";

					using (var cmd = new SQLiteCommand(createProductionTable, conn))
						cmd.ExecuteNonQuery();

					using (var cmd = new SQLiteCommand(createDefectStatsTable, conn))
						cmd.ExecuteNonQuery();

					using (var cmd = new SQLiteCommand(createShiftStatsTable, conn))
						cmd.ExecuteNonQuery();

					conn.Close();
				}
			}

			Logger.Info("SQLite数据库初始化完成");
		}

		/// <summary>
		/// 保存生产记录
		/// </summary>
		public bool SaveProductionRecord(ProductResult result)
		{
			lock (_lock)
			{
				try
				{
					using (var conn = new SQLiteConnection(_connectionString))
					{
						conn.Open();

						string sql = @"
                            INSERT OR REPLACE INTO ProductionRecords 
                            (ProductId, CreateTime, Shift, FrontResult, BackResult, EndFaceResult, SideResult, 
                             FinalResult, FrontDefects, BackDefects, EndFaceDefects, SideDefects)
                            VALUES (@ProductId, @CreateTime, @Shift, @FrontResult, @BackResult, @EndFaceResult, @SideResult,
                                    @FinalResult, @FrontDefects, @BackDefects, @EndFaceDefects, @SideDefects)";

						using (var cmd = new SQLiteCommand(sql, conn))
						{
							cmd.Parameters.AddWithValue("@ProductId", result.ProductId);
							cmd.Parameters.AddWithValue("@CreateTime", result.CreateTime.ToString("yyyy-MM-dd HH:mm:ss.fff"));
							cmd.Parameters.AddWithValue("@Shift", GetShiftName(result.CreateTime));
							cmd.Parameters.AddWithValue("@FrontResult", result.FrontResult.HasValue ? (result.FrontResult.Value ? 1 : 0) : (object)DBNull.Value);
							cmd.Parameters.AddWithValue("@BackResult", result.BackResult.HasValue ? (result.BackResult.Value ? 1 : 0) : (object)DBNull.Value);
							cmd.Parameters.AddWithValue("@EndFaceResult", result.EndFaceResult.HasValue ? (result.EndFaceResult.Value ? 1 : 0) : (object)DBNull.Value);
							cmd.Parameters.AddWithValue("@SideResult", result.SideResult.HasValue ? (result.SideResult.Value ? 1 : 0) : (object)DBNull.Value);
							cmd.Parameters.AddWithValue("@FinalResult", result.FinalResult ? 1 : 0);
							cmd.Parameters.AddWithValue("@FrontDefects", string.Join(",", result.FrontDefects));
							cmd.Parameters.AddWithValue("@BackDefects", string.Join(",", result.BackDefects));
							cmd.Parameters.AddWithValue("@EndFaceDefects", string.Join(",", result.EndFaceDefects));
							cmd.Parameters.AddWithValue("@SideDefects", string.Join(",", result.SideDefects));

							cmd.ExecuteNonQuery();
						}

						conn.Close();
					}

					return true;
				}
				catch (Exception ex)
				{
					Logger.Error($"保存生产记录失败: {ex.Message}");
					return false;
				}
			}
		}

		/// <summary>
		/// 批量保存生产记录
		/// </summary>
		public bool SaveProductionRecordsBatch(List<ProductResult> results)
		{
			lock (_lock)
			{
				try
				{
					using (var conn = new SQLiteConnection(_connectionString))
					{
						conn.Open();

						using (var transaction = conn.BeginTransaction())
						{
							string sql = @"
                                INSERT OR REPLACE INTO ProductionRecords 
                                (ProductId, CreateTime, Shift, FrontResult, BackResult, EndFaceResult, SideResult, 
                                 FinalResult, FrontDefects, BackDefects, EndFaceDefects, SideDefects)
                                VALUES (@ProductId, @CreateTime, @Shift, @FrontResult, @BackResult, @EndFaceResult, @SideResult,
                                        @FinalResult, @FrontDefects, @BackDefects, @EndFaceDefects, @SideDefects)";

							foreach (var result in results)
							{
								using (var cmd = new SQLiteCommand(sql, conn))
								{
									cmd.Parameters.AddWithValue("@ProductId", result.ProductId);
									cmd.Parameters.AddWithValue("@CreateTime", result.CreateTime.ToString("yyyy-MM-dd HH:mm:ss.fff"));
									cmd.Parameters.AddWithValue("@Shift", GetShiftName(result.CreateTime));
									cmd.Parameters.AddWithValue("@FrontResult", result.FrontResult.HasValue ? (result.FrontResult.Value ? 1 : 0) : (object)DBNull.Value);
									cmd.Parameters.AddWithValue("@BackResult", result.BackResult.HasValue ? (result.BackResult.Value ? 1 : 0) : (object)DBNull.Value);
									cmd.Parameters.AddWithValue("@EndFaceResult", result.EndFaceResult.HasValue ? (result.EndFaceResult.Value ? 1 : 0) : (object)DBNull.Value);
									cmd.Parameters.AddWithValue("@SideResult", result.SideResult.HasValue ? (result.SideResult.Value ? 1 : 0) : (object)DBNull.Value);
									cmd.Parameters.AddWithValue("@FinalResult", result.FinalResult ? 1 : 0);
									cmd.Parameters.AddWithValue("@FrontDefects", string.Join(",", result.FrontDefects));
									cmd.Parameters.AddWithValue("@BackDefects", string.Join(",", result.BackDefects));
									cmd.Parameters.AddWithValue("@EndFaceDefects", string.Join(",", result.EndFaceDefects));
									cmd.Parameters.AddWithValue("@SideDefects", string.Join(",", result.SideDefects));

									cmd.ExecuteNonQuery();
								}
							}

							transaction.Commit();
						}

						conn.Close();
					}

					return true;
				}
				catch (Exception ex)
				{
					Logger.Error($"批量保存生产记录失败: {ex.Message}");
					return false;
				}
			}
		}

		/// <summary>
		/// 更新缺陷统计（按班次）
		/// </summary>
		public bool UpdateDefectStatistics(string shift, DateTime shiftStartTime,
			int frontOkCount, int frontNgCount, int backOkCount, int backNgCount,
			int endFaceOkCount, int endFaceNgCount, int sideOkCount, int sideNgCount,
			Dictionary<string, int> defectCounts)
		{
			lock (_lock)
			{
				try
				{
					using (var conn = new SQLiteConnection(_connectionString))
					{
						conn.Open();

						// 检查是否存在记录
						string checkSql = "SELECT Id FROM DefectStatistics WHERE Shift = @Shift AND ShiftStartTime = @ShiftStartTime";
						bool exists = false;
						using (var checkCmd = new SQLiteCommand(checkSql, conn))
						{
							checkCmd.Parameters.AddWithValue("@Shift", shift);
							checkCmd.Parameters.AddWithValue("@ShiftStartTime", shiftStartTime.ToString("yyyy-MM-dd HH:mm:ss"));
							var result = checkCmd.ExecuteScalar();
							exists = result != null;
						}

						string sql;
						if (exists)
						{
							sql = @"
                                UPDATE DefectStatistics SET
                                    FrontOkCount = FrontOkCount + @FrontOkCount,
                                    FrontNgCount = FrontNgCount + @FrontNgCount,
                                    BackOkCount = BackOkCount + @BackOkCount,
                                    BackNgCount = BackNgCount + @BackNgCount,
                                    EndFaceOkCount = EndFaceOkCount + @EndFaceOkCount,
                                    EndFaceNgCount = EndFaceNgCount + @EndFaceNgCount,
                                    SideOkCount = SideOkCount + @SideOkCount,
                                    SideNgCount = SideNgCount + @SideNgCount,
                                    FrontPCodeErrorCount = FrontPCodeErrorCount + @FrontPCodeErrorCount,
                                    FrontBoxBreakCount = FrontBoxBreakCount + @FrontBoxBreakCount,
                                    FrontFilmBreakCount = FrontFilmBreakCount + @FrontFilmBreakCount,
                                    BackBarcodeErrorCount = BackBarcodeErrorCount + @BackBarcodeErrorCount,
                                    BackDateCodeErrorCount = BackDateCodeErrorCount + @BackDateCodeErrorCount,
                                    BackHookErrorCount = BackHookErrorCount + @BackHookErrorCount,
                                    BackCutCharErrorCount = BackCutCharErrorCount + @BackCutCharErrorCount,
                                    EndFaceDamageCount = EndFaceDamageCount + @EndFaceDamageCount,
                                    EndFaceFlapCount = EndFaceFlapCount + @EndFaceFlapCount,
                                    EndFaceEdgeCount = EndFaceEdgeCount + @EndFaceEdgeCount,
                                    SideWrinkleCount = SideWrinkleCount + @SideWrinkleCount,
                                    SideDamageCount = SideDamageCount + @SideDamageCount,
                                    SideBurstCount = SideBurstCount + @SideBurstCount
                                WHERE Shift = @Shift AND ShiftStartTime = @ShiftStartTime";
						}
						else
						{
							sql = @"
                                INSERT INTO DefectStatistics 
                                (Shift, ShiftStartTime, TotalCount, OkCount, NgCount,
                                 FrontOkCount, FrontNgCount, BackOkCount, BackNgCount,
                                 EndFaceOkCount, EndFaceNgCount, SideOkCount, SideNgCount,
                                 FrontPCodeErrorCount, FrontBoxBreakCount, FrontFilmBreakCount,
                                 BackBarcodeErrorCount, BackDateCodeErrorCount, BackHookErrorCount, BackCutCharErrorCount,
                                 EndFaceDamageCount, EndFaceFlapCount, EndFaceEdgeCount,
                                 SideWrinkleCount, SideDamageCount, SideBurstCount)
                                VALUES (@Shift, @ShiftStartTime, 0, 0, 0,
                                        @FrontOkCount, @FrontNgCount, @BackOkCount, @BackNgCount,
                                        @EndFaceOkCount, @EndFaceNgCount, @SideOkCount, @SideNgCount,
                                        @FrontPCodeErrorCount, @FrontBoxBreakCount, @FrontFilmBreakCount,
                                        @BackBarcodeErrorCount, @BackDateCodeErrorCount, @BackHookErrorCount, @BackCutCharErrorCount,
                                        @EndFaceDamageCount, @EndFaceFlapCount, @EndFaceEdgeCount,
                                        @SideWrinkleCount, @SideDamageCount, @SideBurstCount)";
						}

						using (var cmd = new SQLiteCommand(sql, conn))
						{
							cmd.Parameters.AddWithValue("@Shift", shift);
							cmd.Parameters.AddWithValue("@ShiftStartTime", shiftStartTime.ToString("yyyy-MM-dd HH:mm:ss"));
							cmd.Parameters.AddWithValue("@FrontOkCount", frontOkCount);
							cmd.Parameters.AddWithValue("@FrontNgCount", frontNgCount);
							cmd.Parameters.AddWithValue("@BackOkCount", backOkCount);
							cmd.Parameters.AddWithValue("@BackNgCount", backNgCount);
							cmd.Parameters.AddWithValue("@EndFaceOkCount", endFaceOkCount);
							cmd.Parameters.AddWithValue("@EndFaceNgCount", endFaceNgCount);
							cmd.Parameters.AddWithValue("@SideOkCount", sideOkCount);
							cmd.Parameters.AddWithValue("@SideNgCount", sideNgCount);
							cmd.Parameters.AddWithValue("@FrontPCodeErrorCount", defectCounts.GetValueOrDefault("P号码错误", 0));
							cmd.Parameters.AddWithValue("@FrontBoxBreakCount", defectCounts.GetValueOrDefault("盒子破", 0));
							cmd.Parameters.AddWithValue("@FrontFilmBreakCount", defectCounts.GetValueOrDefault("薄膜破", 0));
							cmd.Parameters.AddWithValue("@BackBarcodeErrorCount", defectCounts.GetValueOrDefault("条形码错误", 0));
							cmd.Parameters.AddWithValue("@BackDateCodeErrorCount", defectCounts.GetValueOrDefault("日期码错误", 0));
							cmd.Parameters.AddWithValue("@BackHookErrorCount", defectCounts.GetValueOrDefault("挂钩错位", 0));
							cmd.Parameters.AddWithValue("@BackCutCharErrorCount", defectCounts.GetValueOrDefault("切字错误", 0));
							cmd.Parameters.AddWithValue("@EndFaceDamageCount", defectCounts.GetValueOrDefault("破损", 0));
							cmd.Parameters.AddWithValue("@EndFaceFlapCount", defectCounts.GetValueOrDefault("搭舌缺陷", 0));
							cmd.Parameters.AddWithValue("@EndFaceEdgeCount", defectCounts.GetValueOrDefault("边缘问题", 0));
							cmd.Parameters.AddWithValue("@SideWrinkleCount", defectCounts.GetValueOrDefault("褶皱", 0));
							cmd.Parameters.AddWithValue("@SideDamageCount", defectCounts.GetValueOrDefault("破损", 0));
							cmd.Parameters.AddWithValue("@SideBurstCount", defectCounts.GetValueOrDefault("爆口", 0));

							cmd.ExecuteNonQuery();
						}

						conn.Close();
					}

					return true;
				}
				catch (Exception ex)
				{
					Logger.Error($"更新缺陷统计失败: {ex.Message}");
					return false;
				}
			}
		}

		/// <summary>
		/// 保存班次统计
		/// </summary>
		public bool SaveShiftStatistics(string shift, DateTime date, int totalCount, int okCount, int ngCount)
		{
			lock (_lock)
			{
				try
				{
					using (var conn = new SQLiteConnection(_connectionString))
					{
						conn.Open();

						string dateStr = date.ToString("yyyy-MM-dd");
						double yieldRate = totalCount > 0 ? (double)okCount / totalCount * 100 : 0;

						string sql = @"
                            INSERT OR REPLACE INTO ShiftStatistics 
                            (Shift, Date, TotalCount, OkCount, NgCount, YieldRate, EndTime)
                            VALUES (@Shift, @Date, @TotalCount, @OkCount, @NgCount, @YieldRate, @EndTime)";

						using (var cmd = new SQLiteCommand(sql, conn))
						{
							cmd.Parameters.AddWithValue("@Shift", shift);
							cmd.Parameters.AddWithValue("@Date", dateStr);
							cmd.Parameters.AddWithValue("@TotalCount", totalCount);
							cmd.Parameters.AddWithValue("@OkCount", okCount);
							cmd.Parameters.AddWithValue("@NgCount", ngCount);
							cmd.Parameters.AddWithValue("@YieldRate", yieldRate);
							cmd.Parameters.AddWithValue("@EndTime", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));

							cmd.ExecuteNonQuery();
						}

						conn.Close();
					}

					return true;
				}
				catch (Exception ex)
				{
					Logger.Error($"保存班次统计失败: {ex.Message}");
					return false;
				}
			}
		}

		/// <summary>
		/// 查询班次统计
		/// </summary>
		public DataTable GetShiftStatistics(string shift, DateTime startDate, DateTime endDate)
		{
			lock (_lock)
			{
				try
				{
					using (var conn = new SQLiteConnection(_connectionString))
					{
						conn.Open();

						string sql = @"
                            SELECT * FROM ShiftStatistics 
                            WHERE Shift = @Shift AND Date BETWEEN @StartDate AND @EndDate
                            ORDER BY Date DESC";

						using (var adapter = new SQLiteDataAdapter(sql, conn))
						{
							adapter.SelectCommand.Parameters.AddWithValue("@Shift", shift);
							adapter.SelectCommand.Parameters.AddWithValue("@StartDate", startDate.ToString("yyyy-MM-dd"));
							adapter.SelectCommand.Parameters.AddWithValue("@EndDate", endDate.ToString("yyyy-MM-dd"));

							var dt = new DataTable();
							adapter.Fill(dt);
							return dt;
						}
					}
				}
				catch (Exception ex)
				{
					Logger.Error($"查询班次统计失败: {ex.Message}");
					return new DataTable();
				}
			}
		}

		/// <summary>
		/// 清除指定班次的数据
		/// </summary>
		public bool ClearShiftData(string shift, DateTime date)
		{
			lock (_lock)
			{
				try
				{
					using (var conn = new SQLiteConnection(_connectionString))
					{
						conn.Open();

						string dateStr = date.ToString("yyyy-MM-dd");
						string startTime = $"{dateStr} 00:00:00";
						string endTime = $"{dateStr} 23:59:59";

						string sql = "DELETE FROM ProductionRecords WHERE Shift = @Shift AND CreateTime BETWEEN @StartTime AND @EndTime";

						using (var cmd = new SQLiteCommand(sql, conn))
						{
							cmd.Parameters.AddWithValue("@Shift", shift);
							cmd.Parameters.AddWithValue("@StartTime", startTime);
							cmd.Parameters.AddWithValue("@EndTime", endTime);
							cmd.ExecuteNonQuery();
						}

						conn.Close();
					}

					return true;
				}
				catch (Exception ex)
				{
					Logger.Error($"清除班次数据失败: {ex.Message}");
					return false;
				}
			}
		}

		/// <summary>
		/// 获取班次名称
		/// </summary>
		private string GetShiftName(DateTime time)
		{
			var now = time.TimeOfDay;
			if (now >= TimeSpan.Parse("00:00:00") && now <= TimeSpan.Parse("07:59:59"))
				return "Night";
			if (now >= TimeSpan.Parse("08:00:00") && now <= TimeSpan.Parse("15:59:59"))
				return "Morning";
			return "Afternoon";
		}

		/// <summary>
		/// 获取数据库连接字符串
		/// </summary>
		public string GetConnectionString() => _connectionString;
	}
}