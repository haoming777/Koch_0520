using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using DetResult = YoloInference.YoloResult;
using DetYolo = YoloInference.YoloOnnx;
using SegResult = YoloSegmentationEnd2End.YoloResult;
using SegYolo = YoloSegmentationEnd2End.YoloOnnxSegmentation;
using Size = OpenCvSharp.Size;
using Point = OpenCvSharp.Point;
using Rect = OpenCvSharp.Rect;

namespace Detection
{
	public class HookInspectionOutput
	{
		public List<string> HookStatus { get; set; }
		public Dictionary<string, List<object>> LeftNgCoordinates { get; set; }
		public Dictionary<string, List<object>> RightNgCoordinates { get; set; }
	}

	public class HookDamageDetector
	{
		/// <summary>
		/// 通过距离变换求最大内切圆，计算实心框架的最大厚度
		/// </summary>
		private static (double MaxThickness, Point MaxLoc) CalculateThicknessByInscribedCircle(
			Size imageShape, Point[] innerCoords, Point[] outerCoords)
		{
			using (Mat mask = Mat.Zeros(imageShape, MatType.CV_8UC1))
			{
				// 填充外圈为白色
				Cv2.FillPoly(mask, new[] { outerCoords }, new Scalar(255));
				// 挖掉内圈为黑色
				Cv2.FillPoly(mask, new[] { innerCoords }, new Scalar(0));

				using (Mat distTransform = new Mat())
				{
					Cv2.DistanceTransform(mask, distTransform, DistanceTypes.L2, DistanceTransformMasks.Precise);
					Cv2.MinMaxLoc(distTransform, out _, out double maxVal, out _, out Point maxLoc);
					return (maxVal * 2.0, maxLoc);
				}
			}
		}

		/// <summary>
		/// 识别背面盒子挂钩的完整状态
		/// </summary>
		public static HookInspectionOutput CheckAllHookDamages(
			Mat leftImage,
			Mat rightImage,
			int p,
			DetYolo hookObviousDefectModel,
			SegYolo hookSlightDefectModel,
			double thicknessThreshold = 30.0,
			int blueAreaClassId = 0,
			int hangHoleClassId = 1)
		{
			var hookStatus = Enumerable.Repeat("缺少", p).ToList();
			var leftNgDict = new Dictionary<string, List<object>>
			{
				{ "挂钩明显错位", new List<object>() },
				{ "轻微挂钩错位", new List<object>() }
			};
			var rightNgDict = new Dictionary<string, List<object>>
			{
				{ "挂钩明显错位", new List<object>() },
				{ "轻微挂钩错位", new List<object>() }
			};

			var images = new List<Mat> { leftImage, rightImage };
			double[] offsets = { 0.0, p / 2.0 };

			// 批量进行全局目标检测推理
			List<DetResult> batchResults = hookObviousDefectModel.PredictBatch(images, confThres: 0.5f, iouThres: 0.2f);

			for (int i = 0; i < batchResults.Count; i++)
			{
				var objDetResults = batchResults[i];
				if (objDetResults?.Boxes == null || objDetResults.Boxes.Length == 0)
					continue;

				var currentNgDict = (i == 0) ? leftNgDict : rightNgDict;
				Mat currentImage = images[i];
				int imgH = currentImage.Height;
				int imgW = currentImage.Width;

				for (int j = 0; j < objDetResults.Boxes.Length; j++)
				{
					int cls = objDetResults.ClassIds[j];
					Rect2f bboxN = objDetResults.BoxesN[j];
					Rect bbox = objDetResults.Boxes[j];

					double centerXN = bboxN.X + (bboxN.Width / 2.0);
					int globalIndex = (int)Math.Floor(centerXN * (p / 2.0) + offsets[i]);
					globalIndex = Math.Max(0, Math.Min(globalIndex, p - 1));

					if (cls == 1) // 明显挂钩错位
					{
						hookStatus[globalIndex] = "挂钩明显错位";
						currentNgDict["挂钩明显错位"].Add(new List<double>
						{
							bboxN.X, bboxN.Y,
							bboxN.X + bboxN.Width, bboxN.Y + bboxN.Height
						});
					}
					else if (cls == 0) // 挂钩区域，需要进行轻微错位检测
					{
						if (hookStatus[globalIndex] != "挂钩明显错位")
						{
							int x1 = Math.Max(0, bbox.X);
							int y1 = Math.Max(0, bbox.Y);
							int x2 = Math.Min(imgW, bbox.X + bbox.Width);
							int y2 = Math.Min(imgH, bbox.Y + bbox.Height);

							if (x2 <= x1 || y2 <= y1)
							{
								hookStatus[globalIndex] = "OK";
								continue;
							}

							using (Mat cropImg = new Mat(currentImage, new Rect(x1, y1, x2 - x1, y2 - y1)))
							{
								if (cropImg.Empty())
								{
									hookStatus[globalIndex] = "OK";
									continue;
								}

								SegResult segResult = hookSlightDefectModel.Predict(cropImg);

								Point[] innerCoords = null;
								Point[] outerCoords = null;

								if (segResult?.Masks != null)
								{
									for (int m = 0; m < segResult.ClassIds.Length; m++)
									{
										int maskClsId = segResult.ClassIds[m];
										var maskPts = segResult.Masks[m]
											.Select(pt => new Point((int)Math.Round(pt.X), (int)Math.Round(pt.Y)))
											.ToArray();

										if (maskClsId == blueAreaClassId)
											innerCoords = maskPts;
										else if (maskClsId == hangHoleClassId)
											outerCoords = maskPts;
									}
								}

								if (innerCoords != null && outerCoords != null && innerCoords.Length > 0 && outerCoords.Length > 0)
								{
									var (maxWidth, localCenterPt) = CalculateThicknessByInscribedCircle(
										cropImg.Size(), innerCoords, outerCoords);

									if (maxWidth > thicknessThreshold)
									{
										hookStatus[globalIndex] = "轻微挂钩错位";

										double globalCenterX = localCenterPt.X + x1;
										double globalCenterY = localCenterPt.Y + y1;

										double ratioX = Math.Max(0.0, Math.Min(1.0, globalCenterX / imgW));
										double ratioY = Math.Max(0.0, Math.Min(1.0, globalCenterY / imgH));

										int diameter = (int)Math.Round(maxWidth);

										currentNgDict["轻微挂钩错位"].Add(new object[]
										{
											diameter,
											new double[] { ratioX, ratioY }
										});
									}
									else
									{
										hookStatus[globalIndex] = "OK";
									}
								}
								else
								{
									hookStatus[globalIndex] = "OK";
								}
							}
						}
					}
				}
			}

			var cleanLeftNgDict = leftNgDict.Where(kv => kv.Value.Count > 0).ToDictionary(kv => kv.Key, kv => kv.Value);
			var cleanRightNgDict = rightNgDict.Where(kv => kv.Value.Count > 0).ToDictionary(kv => kv.Key, kv => kv.Value);

			return new HookInspectionOutput
			{
				HookStatus = hookStatus,
				LeftNgCoordinates = cleanLeftNgDict,
				RightNgCoordinates = cleanRightNgDict
			};
		}
	}
}