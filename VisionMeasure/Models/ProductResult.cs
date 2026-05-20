using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;

namespace Models
{
	public static class DictionaryExtensions
	{
		public static TValue GetValueOrDefault<TKey, TValue>(this Dictionary<TKey, TValue> dict, TKey key, TValue defaultValue = default(TValue))
		{
			return dict.TryGetValue(key, out var value) ? value : defaultValue;
		}
	}
	public class ProductResult
	{
		public long ProductId { get; set; }
		public DateTime CreateTime { get; set; } = DateTime.Now;

		// 各工位检测结果 (null表示未检测或缺失)
		public bool? FrontResult { get; set; }
		public bool? BackResult { get; set; }
		public bool? EndFaceResult { get; set; }
		public bool? SideResult { get; set; }

		// 各工位缺陷列表
		public List<string> FrontDefects { get; set; } = new List<string>();
		public List<string> BackDefects { get; set; } = new List<string>();
		public List<string> EndFaceDefects { get; set; } = new List<string>();
		public List<string> SideDefects { get; set; } = new List<string>();

		// 详细缺陷统计
		public Dictionary<string, int> FrontDefectCounts { get; set; } = new Dictionary<string, int>();
		public Dictionary<string, int> BackDefectCounts { get; set; } = new Dictionary<string, int>();
		public Dictionary<string, int> EndFaceDefectCounts { get; set; } = new Dictionary<string, int>();
		public Dictionary<string, int> SideDefectCounts { get; set; } = new Dictionary<string, int>();

		// 渲染图像
		public Bitmap FrontRenderImage { get; set; }
		public Bitmap BackRenderImage { get; set; }
		public Bitmap EndFaceRenderImage { get; set; }
		public Bitmap SideRenderImage { get; set; }

		// 性能耗时
		public double TotalTimeMs { get; set; }
		public double FrontTimeMs { get; set; }
		public double BackTimeMs { get; set; }
		public double EndFaceTimeMs { get; set; }
		public double SideTimeMs { get; set; }

		// 状态
		public bool IsComplete => FrontResult.HasValue && BackResult.HasValue
							   && EndFaceResult.HasValue && SideResult.HasValue;

		public bool FinalResult => (FrontResult ?? true) && (BackResult ?? true)
								&& (EndFaceResult ?? true) && (SideResult ?? true);

		/// <summary>
		/// 获取所有缺陷类型
		/// </summary>
		public List<string> GetAllDefects()
		{
			var all = new List<string>();
			all.AddRange(FrontDefects);
			all.AddRange(BackDefects);
			all.AddRange(EndFaceDefects);
			all.AddRange(SideDefects);
			return all;
		}

		/// <summary>
		/// 获取缺陷统计字典
		/// </summary>
		public Dictionary<string, int> GetAllDefectCounts()
		{
			var result = new Dictionary<string, int>();

			foreach (var kv in FrontDefectCounts)
				result[kv.Key] = result.GetValueOrDefault(kv.Key) + kv.Value;
			foreach (var kv in BackDefectCounts)
				result[kv.Key] = result.GetValueOrDefault(kv.Key) + kv.Value;
			foreach (var kv in EndFaceDefectCounts)
				result[kv.Key] = result.GetValueOrDefault(kv.Key) + kv.Value;
			foreach (var kv in SideDefectCounts)
				result[kv.Key] = result.GetValueOrDefault(kv.Key) + kv.Value;

			return result;
		}

		/// <summary>
		/// 获取NG类型字符串（用于文件名）
		/// </summary>
		public string GetNgTypeString()
		{
			var ngTypes = GetAllDefects().Distinct().ToList();
			if (ngTypes.Count == 0) return "OK";
			return string.Join("_", ngTypes);
		}

		public override string ToString()
		{
			string f = FrontResult.HasValue ? (FrontResult.Value ? "O" : "X") : "-";
			string b = BackResult.HasValue ? (BackResult.Value ? "O" : "X") : "-";
			string e = EndFaceResult.HasValue ? (EndFaceResult.Value ? "O" : "X") : "-";
			string s = SideResult.HasValue ? (SideResult.Value ? "O" : "X") : "-";
			return $"ProductId={ProductId} [{f}{b}{e}{s}] {(FinalResult ? "OK" : "NG")}";
		}
	}
}