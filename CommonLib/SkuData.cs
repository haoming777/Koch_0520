using System;

namespace CommonLib
{
	public class SkuData
	{
		public string SkuNumber { get; set; }
		public int P { get; set; }
		public int Z { get; set; }
		public int MM { get; set; }
		public string PZInfo { get; set; }

		// 裁图像素参数 (传给 ImageHelper.CropImageHorizontallyCv2)
		public int FrontLeft_LeftPx { get; set; }
		public int FrontLeft_RightPx { get; set; }
		public int FrontRight_LeftPx { get; set; }
		public int FrontRight_RightPx { get; set; }
		public int UpperEndFace_LeftPx { get; set; }
		public int LowerEndFace_LeftPx { get; set; }
		public int BackLeft_LeftPx { get; set; }
		public int BackLeft_RightPx { get; set; }
		public int BackRight_LeftPx { get; set; }
		public int BackRight_RightPx { get; set; }

		public string FrontPCode { get; set; }
		public string BackBarcode { get; set; }
		public string CodingFormat { get; set; }

		public override string ToString() => $"{P}P{Z}Z{MM}mm";
	}
}