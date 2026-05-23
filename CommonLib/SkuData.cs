using System;

namespace CommonLib
{
	/// <summary>
	/// SKU数据模型
	/// </summary>
	public class SkuData
	{
		public string SkuNumber { get; set; }
		public int P { get; set; }          // 盒子数量
		public int Z { get; set; }          // 层数
		public int MM { get; set; }         // 尺寸mm
		public string PZInfo { get; set; }  // 原始PZ信息，如"3P2Z70mm"

		// 裁剪比例配置
		public double FrontLeftCropRatio { get; set; }   // 正面左图左侧裁图比例
		public double FrontRightCropRatio { get; set; }  // 正面左图右侧裁图比例
		public double BackLeftCropRatio { get; set; }    // 背面左图左侧裁图比例
		public double BackRightCropRatio { get; set; }   // 背面左图右侧裁图比例
		public double UpperCropRatio { get; set; }       // 上端面裁图比例
		public double LowerCropRatio { get; set; }       // 下端面裁图比例

		// 标准信息
		public string FrontPCode { get; set; }           // 正面P号码标准
		public string BackBarcode { get; set; }          // 背面条形码标准
		public string CodingFormat { get; set; }         // 打码格式: MFG/不打码/LOT/双排码/内销码

		// 打码格式枚举
		public enum CodingFormatType
		{
			MFG,
			不打码,
			LOT,
			双排码,
			内销码
		}

		public CodingFormatType GetCodingFormat()
		{
			switch (CodingFormat)
			{
				case "MFG": return CodingFormatType.MFG;
				case "LOT": return CodingFormatType.LOT;
				case "双排码": return CodingFormatType.双排码;
				case "内销码": return CodingFormatType.内销码;
				default: return CodingFormatType.不打码;
			}
		}

		/// <summary>
		/// 从字符串解析P、Z、MM
		/// </summary>
		public static SkuData ParseFromPZ(string pzString)
		{
			var result = new SkuData();
			result.PZInfo = pzString;

			try
			{
				// 格式如 "3P2Z70mm"
				var pIndex = pzString.IndexOf('P');
				var zIndex = pzString.IndexOf('Z');
				var mmIndex = pzString.IndexOf('m');

				if (pIndex > 0)
					result.P = int.Parse(pzString.Substring(0, pIndex));
				if (zIndex > pIndex)
					result.Z = int.Parse(pzString.Substring(pIndex + 1, zIndex - pIndex - 1));
				if (mmIndex > zIndex)
					result.MM = int.Parse(pzString.Substring(zIndex + 1, mmIndex - zIndex - 1));
			}
			catch (Exception ex)
			{
				Logger.Error($"解析PZ字符串失败: {pzString}, {ex.Message}");
			}

			return result;
		}

		public override string ToString() => $"{P}P{Z}Z{MM}mm";
	}
}