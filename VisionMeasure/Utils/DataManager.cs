using System;
using System.Text.RegularExpressions;

namespace VisionMeasure.Utils
{
	public class SkuInfo
	{
		public string SkuCode { get; set; }
		public int P { get; set; }
		public int Z { get; set; }
		public int MM { get; set; }
		public string CodingFormat { get; set; }
		public string StandardPNumber { get; set; }
		public string StandardBarcode { get; set; }

		public static SkuInfo Parse(string skuStr, string format, string pNum, string barcode)
		{
			var info = new SkuInfo
			{
				SkuCode = skuStr,
				CodingFormat = format,
				StandardPNumber = pNum,
				StandardBarcode = barcode
			};

			// 解析 3P2Z70mm 格式
			var match = Regex.Match(skuStr, @"(\d+)P(\d+)Z(\d+)mm", RegexOptions.IgnoreCase);
			if (match.Success)
			{
				info.P = int.Parse(match.Groups[1].Value);
				info.Z = int.Parse(match.Groups[2].Value);
				info.MM = int.Parse(match.Groups[3].Value);
			}
			return info;
		}
	}

	public class ShiftManager
	{
		public enum ShiftType { Night, Morning, Evening }

		public static ShiftType GetCurrentShift()
		{
			TimeSpan now = DateTime.Now.TimeOfDay;
			if (now >= TimeSpan.Parse("08:00:00") && now <= TimeSpan.Parse("15:59:59"))
				return ShiftType.Morning;
			if (now >= TimeSpan.Parse("16:00:00") && now <= TimeSpan.Parse("23:59:59"))
				return ShiftType.Evening;
			return ShiftType.Night;
		}

		public static string GetShiftDateString()
		{
			DateTime now = DateTime.Now;
			// 夜班(00:00 - 08:00)归属前一天
			if (now.TimeOfDay < TimeSpan.Parse("08:00:00"))
				now = now.AddDays(-1);

			return $"{now:yyyyMMdd}_{GetCurrentShift()}";
		}
	}
}