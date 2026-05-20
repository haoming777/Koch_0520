using System;
using System.Collections.Generic;
using System.Linq;

namespace Models
{
	/// <summary>
	/// 批次结果记录（多个产品一组）
	/// </summary>
	public class BatchResultRecord
	{
		public DateTime Timestamp { get; set; } = DateTime.Now;
		public long BatchId { get; set; }
		public long[] ProductIds { get; set; } = new long[0];
		public bool[] Results { get; set; } = new bool[0];
		public bool FinalResult { get; set; }

		// 各工位结果
		public bool?[] FrontResults { get; set; }
		public bool?[] BackResults { get; set; }
		public bool?[] EndFaceResults { get; set; }
		public bool?[] SideResults { get; set; }

		// 匹配状态
		public string MatchStatus { get; set; } = "Success";
		public string MissingCameras { get; set; }
		public long MatchTimeMs { get; set; }

		/// <summary>
		/// 获取OK数量
		/// </summary>
		public int GetOkCount() => Results.Count(r => r);

		/// <summary>
		/// 获取NG数量
		/// </summary>
		public int GetNgCount() => Results.Count(r => !r);

		public override string ToString()
		{
			return $"[{Timestamp:HH:mm:ss.fff}] 批次={BatchId}, 产品ID=[{string.Join(",", ProductIds)}], " +
				   $"结果=[{string.Join(",", Results.Select(r => r ? "OK" : "NG"))}], " +
				   $"最终={(FinalResult ? "OK" : "NG")}, 状态={MatchStatus}";
		}
	}

	/// <summary>
	/// 班次统计结果
	/// </summary>
	public class ShiftStatistics
	{
		public string Shift { get; set; }          // Morning/Afternoon/Night
		public DateTime ShiftStartTime { get; set; }
		public DateTime ShiftEndTime { get; set; }

		public long TotalCount { get; set; }
		public long OkCount { get; set; }
		public long NgCount { get; set; }
		public double YieldRate => TotalCount > 0 ? (double)OkCount / TotalCount * 100 : 0;

		// 各工位统计
		public long FrontOkCount { get; set; }
		public long FrontNgCount { get; set; }
		public long BackOkCount { get; set; }
		public long BackNgCount { get; set; }
		public long EndFaceOkCount { get; set; }
		public long EndFaceNgCount { get; set; }
		public long SideOkCount { get; set; }
		public long SideNgCount { get; set; }

		// 缺陷详细统计
		public Dictionary<string, int> DefectCounts { get; set; } = new Dictionary<string, int>();

		public override string ToString()
		{
			return $"{Shift}班次: 总数={TotalCount}, OK={OkCount}, NG={NgCount}, 良率={YieldRate:F2}%";
		}
	}
}