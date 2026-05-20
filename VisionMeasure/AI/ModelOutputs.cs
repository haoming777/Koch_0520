using System.Collections.Generic;
using OpenCvSharp;

namespace AI
{
	/// <summary>
	/// Yolo检测结果
	/// </summary>
	public class YoloDetectionResult
	{
		public Rect[] Boxes { get; set; }        // 绝对坐标框
		public Rect2f[] BoxesN { get; set; }     // 归一化坐标框
		public float[] Scores { get; set; }      // 置信度
		public int[] ClassIds { get; set; }      // 类别ID
		public double InferenceTimeMs { get; set; }
	}

	/// <summary>
	/// OCR识别结果
	/// </summary>
	public class OcrResult
	{
		public string Text { get; set; }
		public float Score { get; set; }
		public Rect BoundingBox { get; set; }
		public double InferenceTimeMs { get; set; }
	}

	/// <summary>
	/// 分割结果
	/// </summary>
	public class SegmentationResult
	{
		public Point2f[][] Masks { get; set; }
		public int[] ClassIds { get; set; }
		public float[] Scores { get; set; }
		public double InferenceTimeMs { get; set; }
	}

	/// <summary>
	/// 条形码识别结果
	/// </summary>
	public class BarcodeResult
	{
		public string Code { get; set; }
		public Rect BoundingBox { get; set; }
		public double InferenceTimeMs { get; set; }
	}

	/// <summary>
	/// 正面检测汇总结果
	/// </summary>
	public class FrontDetectionResult
	{
		public List<BoxDefect> LeftDefects { get; set; } = new List<BoxDefect>();
		public List<BoxDefect> RightDefects { get; set; } = new List<BoxDefect>();
		public List<string> BoxStatus { get; set; } = new List<string>();
		public List<string> PCodeResults { get; set; } = new List<string>();
		public double CropTimeMs { get; set; }
		public double PCodeTimeMs { get; set; }
		public double BoxBreakTimeMs { get; set; }
		public double FilmBreakTimeMs { get; set; }
		public double TotalTimeMs { get; set; }
	}

	/// <summary>
	/// 背面检测汇总结果
	/// </summary>
	public class BackDetectionResult
	{
		public List<BoxDefect> LeftDefects { get; set; } = new List<BoxDefect>();
		public List<BoxDefect> RightDefects { get; set; } = new List<BoxDefect>();
		public List<string> BoxStatus { get; set; } = new List<string>();
		public List<string> BarcodeResults { get; set; } = new List<string>();
		public List<string> DateCodeResults { get; set; } = new List<string>();
		public double BarcodeTimeMs { get; set; }
		public double DateCodeTimeMs { get; set; }
		public double HookTimeMs { get; set; }
		public double TotalTimeMs { get; set; }
	}

	/// <summary>
	/// 端面检测汇总结果
	/// </summary>
	public class EndFaceDetectionResult
	{
		public List<BoxDefect> UpperDefects { get; set; } = new List<BoxDefect>();
		public List<BoxDefect> LowerDefects { get; set; } = new List<BoxDefect>();
		public List<string> BoxStatus { get; set; } = new List<string>();
		public double InferenceTimeMs { get; set; }
		public double TotalTimeMs { get; set; }
	}

	/// <summary>
	/// 侧面检测结果
	/// </summary>
	public class SideDetectionResult
	{
		public List<BoxDefect> LeftDefects { get; set; } = new List<BoxDefect>();
		public List<BoxDefect> RightDefects { get; set; } = new List<BoxDefect>();
		public List<string> BoxStatus { get; set; } = new List<string>();
		public double InferenceTimeMs { get; set; }
		public double TotalTimeMs { get; set; }
	}

	/// <summary>
	/// 单个盒子缺陷
	/// </summary>
	public class BoxDefect
	{
		public int BoxIndex { get; set; }
		public string DefectType { get; set; }
		public float[] BoundingBox { get; set; }  // [x1, y1, x2, y2] 归一化
		public float Score { get; set; }

		public BoxDefect(int index, string type, float[] box, float score = 1.0f)
		{
			BoxIndex = index;
			DefectType = type;
			BoundingBox = box;
			Score = score;
		}
	}
}