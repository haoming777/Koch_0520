using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using SmartMore.ViMo;

namespace VisionMeasure.Utils
{
	public class ResultDrawer
	{
		private static readonly Dictionary<int, Scalar> ClassColors = new Dictionary<int, Scalar>
		{
			{ 0, new Scalar(0, 255, 0) },     // 绿色
			{ 1, new Scalar(0, 0, 255) },     // 蓝色
			{ 2, new Scalar(0, 165, 255) },   // 橙色
			{ 3, new Scalar(255, 0, 255) },   // 紫色
			{ 4, new Scalar(255, 255, 0) },   // 青色
			{ 5, new Scalar(255, 0, 0) },     // 红色
		};

		public static Mat DrawYoloResult(Mat image, YoloInference.YoloResult result, string[] classNames = null)
		{
			if (image == null || image.Empty())
				return null;

			Mat resultImage = image.Clone();

			if (result == null || result.Boxes == null || result.Boxes.Length == 0)
			{
				Cv2.PutText(resultImage, "No detections", new OpenCvSharp.Point(10, 30),
					HersheyFonts.HersheySimplex, 1.0, new Scalar(0, 255, 0), 2);
				return resultImage;
			}

			for (int i = 0; i < result.Boxes.Length; i++)
			{
				Rect box = result.Boxes[i];
				int classId = result.ClassIds[i];
				float score = result.Scores[i];

				Scalar color = GetClassColor(classId);
				string label = classNames != null && classId < classNames.Length
					? $"{classNames[classId]}: {score:F2}"
					: $"Class{classId}: {score:F2}";

				Cv2.Rectangle(resultImage, box, color, 2);
				Cv2.PutText(resultImage, label, new OpenCvSharp.Point(box.X, box.Y - 10),
					HersheyFonts.HersheySimplex, 0.6, color, 2);
			}

			Cv2.PutText(resultImage, $"Detected: {result.Boxes.Length} objects", new OpenCvSharp.Point(10, 30),
				HersheyFonts.HersheySimplex, 1.0, new Scalar(0, 255, 0), 2);

			return resultImage;
		}

		public static Mat DrawOcrResult(Mat image, OcrResponse ocrResult, string expectedText = null)
		{
			if (image == null || image.Empty())
				return null;

			Mat resultImage = image.Clone();

			if (ocrResult == null || ocrResult.Blocks == null || !ocrResult.Blocks.Any())
			{
				Cv2.PutText(resultImage, "No OCR result", new OpenCvSharp.Point(10, 30),
					HersheyFonts.HersheySimplex, 1.0, new Scalar(0, 0, 255), 2);
				return resultImage;
			}

			string recognizedText = string.Join("", ocrResult.Blocks.Select(b => b.Label));
			float avgConfidence = ocrResult.Blocks.Average(b => b.Score);

			Scalar textColor = new Scalar(0, 255, 0);
			string status = "OK";

			if (!string.IsNullOrEmpty(expectedText) && recognizedText != expectedText)
			{
				textColor = new Scalar(0, 0, 255);
				status = "NG";
			}

			int y = 30;
			Cv2.PutText(resultImage, $"Status: {status}", new OpenCvSharp.Point(10, y),
				HersheyFonts.HersheySimplex, 1.0, textColor, 2);
			y += 35;
			Cv2.PutText(resultImage, $"Text: {recognizedText}", new OpenCvSharp.Point(10, y),
				HersheyFonts.HersheySimplex, 0.8, new Scalar(255, 255, 255), 2);
			y += 30;
			Cv2.PutText(resultImage, $"Confidence: {avgConfidence:F4}", new OpenCvSharp.Point(10, y),
				HersheyFonts.HersheySimplex, 0.8, new Scalar(255, 255, 255), 2);

			if (!string.IsNullOrEmpty(expectedText))
			{
				y += 30;
				Cv2.PutText(resultImage, $"Expected: {expectedText}", new OpenCvSharp.Point(10, y),
					HersheyFonts.HersheySimplex, 0.8, new Scalar(255, 255, 0), 2);
			}

			return resultImage;
		}

		public static Mat DrawHookDamageResult(Mat leftImage, Mat rightImage, Detection.HookInspectionOutput result)
		{
			if (leftImage == null || leftImage.Empty())
				return null;

			int newWidth = leftImage.Width + rightImage.Width;
			int newHeight = Math.Max(leftImage.Height, rightImage.Height);
			Mat resultImage = new Mat(newHeight, newWidth, leftImage.Type(), new Scalar(0, 0, 0));

			leftImage.CopyTo(new Mat(resultImage, new Rect(0, 0, leftImage.Width, leftImage.Height)));
			rightImage.CopyTo(new Mat(resultImage, new Rect(leftImage.Width, 0, rightImage.Width, rightImage.Height)));

			if (result != null && result.HookStatus != null)
			{
				int boxWidth = leftImage.Width / result.HookStatus.Count;
				for (int i = 0; i < result.HookStatus.Count; i++)
				{
					string status = result.HookStatus[i];
					Scalar color = new Scalar(0, 255, 0);

					if (status != "OK" && status != "缺少")
					{
						color = new Scalar(0, 0, 255);
					}

					int x = i * boxWidth;
					Cv2.Rectangle(resultImage, new Rect(x, 0, boxWidth, 30), color, -1);
					Cv2.PutText(resultImage, $"#{i + 1}: {status}", new OpenCvSharp.Point(x + 5, 20),
						HersheyFonts.HersheySimplex, 0.6, new Scalar(255, 255, 255), 2);
				}
			}

			return resultImage;
		}

		public static Mat DrawEndFaceResult(Mat image, YoloInference.YoloResult result)
		{
			if (image == null || image.Empty())
				return null;

			Mat resultImage = image.Clone();

			if (result == null || result.Boxes == null || result.Boxes.Length == 0)
			{
				Cv2.PutText(resultImage, "No defects detected", new OpenCvSharp.Point(10, 30),
					HersheyFonts.HersheySimplex, 1.0, new Scalar(0, 255, 0), 2);
				return resultImage;
			}

			string[] defectNames = { "搭舌缺陷", "边缘问题", "破损" };

			for (int i = 0; i < result.Boxes.Length; i++)
			{
				Rect box = result.Boxes[i];
				int classId = result.ClassIds[i];
				float score = result.Scores[i];

				Scalar color = new Scalar(0, 0, 255);
				string defectName = classId < defectNames.Length ? defectNames[classId] : $"未知{classId}";
				string label = $"{defectName}: {score:F2}";

				Cv2.Rectangle(resultImage, box, color, 2);
				Cv2.PutText(resultImage, label, new OpenCvSharp.Point(box.X, box.Y - 10),
					HersheyFonts.HersheySimplex, 0.6, color, 2);
			}

			Cv2.PutText(resultImage, $"Defects: {result.Boxes.Length}", new OpenCvSharp.Point(10, 30),
				HersheyFonts.HersheySimplex, 1.0, new Scalar(0, 0, 255), 2);

			return resultImage;
		}

		public static Mat DrawSideDefectResult(Mat image, YoloInference.YoloResult result)
		{
			if (image == null || image.Empty())
				return null;

			Mat resultImage = image.Clone();

			if (result == null || result.Boxes == null || result.Boxes.Length == 0)
			{
				Cv2.PutText(resultImage, "No side defects detected", new OpenCvSharp.Point(10, 30),
					HersheyFonts.HersheySimplex, 1.0, new Scalar(0, 255, 0), 2);
				return resultImage;
			}

			for (int i = 0; i < result.Boxes.Length; i++)
			{
				Rect box = result.Boxes[i];
				int classId = result.ClassIds[i];
				float score = result.Scores[i];

				Scalar color = new Scalar(255, 0, 0);
				string label = $"Defect{classId}: {score:F2}";

				Cv2.Rectangle(resultImage, box, color, 2);
				Cv2.PutText(resultImage, label, new OpenCvSharp.Point(box.X, box.Y - 10),
					HersheyFonts.HersheySimplex, 0.6, color, 2);
			}

			Cv2.PutText(resultImage, $"Side Defects: {result.Boxes.Length}", new OpenCvSharp.Point(10, 30),
				HersheyFonts.HersheySimplex, 1.0, new Scalar(255, 0, 0), 2);

			return resultImage;
		}

		private static Scalar GetClassColor(int classId)
		{
			if (ClassColors.ContainsKey(classId))
				return ClassColors[classId];

			int index = classId % ClassColors.Count;
			return ClassColors.Values.ElementAt(index);
		}
	}
}