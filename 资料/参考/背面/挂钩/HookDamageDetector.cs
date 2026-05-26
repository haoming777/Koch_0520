using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Diagnostics.Eventing.Reader;
using System.Drawing;
using System.Linq;
using DetResult = YoloInference.YoloResult;
// 引入您提供的两个命名空间，并对存在命名冲突的 YoloResult 采用别名区分
using DetYolo = YoloInference.YoloOnnx;
using SegResult = YoloSegmentationEnd2End.YoloResult;
using SegYolo = YoloSegmentationEnd2End.YoloOnnxSegmentation;
using Size = OpenCvSharp.Size;
using Point =  OpenCvSharp.Point;
using PointF = System.Drawing.PointF;

namespace HookInspectionSystem
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
		        /// 通过距离变换求最大内切圆，计算实心框架的最大厚度。
		        /// </summary>
		        /// <param name="imageShape">局部图像的尺寸</param>
		        /// <param name="innerCoords">内圈多边形坐标</param>
		        /// <param name="outerCoords">外圈多边形坐标</param>
		        /// <returns>最大厚度（直径），以及最大内切圆的圆心坐标</returns>
		private static (double MaxThickness, Point MaxLoc) CalculateThicknessByInscribedCircle(
	  Size imageShape, Point[] innerCoords, Point[] outerCoords)
		{
			// 1. 创建与原图尺寸相同的全黑空白掩膜 (Mask)
			using (Mat mask = Mat.Zeros(imageShape, MatType.CV_8UC1))
			{
				// 2. 将外圈多边形填充为白色 (255)，代表整个物体的初始实心轮廓
				Cv2.FillPoly(mask, new[] { outerCoords }, new Scalar(255));

				// 3. 将内圈多边形填充为黑色 (0)，相当于在物体中间“挖洞”，留下纯粹的壁厚区域
				Cv2.FillPoly(mask, new[] { innerCoords }, new Scalar(0));

				// 4. 执行距离变换 (Distance Transform)
				using (Mat distTransform = new Mat())
				{
					Cv2.DistanceTransform(mask, distTransform, DistanceTypes.L2, DistanceTransformMasks.Precise);

					// 5. 寻找距离变换矩阵中的最大值及其像素坐标
					Cv2.MinMaxLoc(distTransform, out _, out double maxVal, out _, out Point maxLoc);

					// 局部最大框架厚度即为内切圆的直径
					return (maxVal * 2.0, maxLoc);
				}
			}
		}

		/// <summary>
		        /// 识别背面盒子挂钩的完整状态，包括全局明显错位检测以及局部轻微错位检测。
		        /// </summary>
		        /// <param name="leftImage">左侧相机拍摄的图像</param>
		        /// <param name="rightImage">右侧相机拍摄的图像</param>
		        /// <param name="p">整个检测面上盒子的总数</param>
		        /// <param name="hookObviousDefectModel">YOLO 全局目标检测模型实例</param>
		        /// <param name="hookSlightDefectModel">YOLO 局部实例分割模型实例</param>
		        /// <param name="thicknessThreshold">判断轻微错位的像素宽度阈值</param>
		        /// <param name="blueAreaClassId">分割模型中代表蓝底(内圈)的类别ID</param>
		        /// <param name="hangHoleClassId">分割模型中代表挂孔(外圈)的类别ID</param>
		public static HookInspectionOutput CheckAllHookDamages(
	  Mat leftImage,
	  Mat rightImage,
	  int p,
	  DetYolo hookObviousDefectModel,
	  SegYolo hookSlightDefectModel,
	  double thicknessThreshold = 30.0,
	  int blueAreaClassId = 0,    // 需替换为模型实际的内圈 ID
			int hangHoleClassId = 1)    // 需替换为模型实际的外圈 ID
		{
			// 初始化状态列表与缺陷记录字典
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

				// 动态指定当前结果字典与原图
				var currentNgDict = (i == 0) ? leftNgDict : rightNgDict;
				Mat currentImage = images[i];
				int imgH = currentImage.Height;
				int imgW = currentImage.Width;

				for (int j = 0; j < objDetResults.Boxes.Length; j++)
				{
					int cls = objDetResults.ClassIds[j];
					Rect2f bboxN = objDetResults.BoxesN[j]; // 归一化坐标 [x, y, w, h]
					Rect bbox = objDetResults.Boxes[j];     // 绝对像素坐标 [x, y, w, h]

					// 计算归一化的 X 中心点
					double centerXN = bboxN.X + (bboxN.Width / 2.0);

					// 计算全局索引
					int globalIndex = (int)Math.Floor(centerXN * (p / 2.0) + offsets[i]);
					globalIndex = Math.Max(0, Math.Min(globalIndex, p - 1));

					if (cls == 1)
					{
						// 处理全局判定为 "挂钩明显错位" 的情况
						hookStatus[globalIndex] = "挂钩明显错位";
						// Python 代码的 xyxyn 是 [x1, y1, x2, y2]，因此需转换
						currentNgDict["挂钩明显错位"].Add(new List<double>
			{
			  bboxN.X,
			  bboxN.Y,
			  bboxN.X + bboxN.Width,
			  bboxN.Y + bboxN.Height
			});
					}
					else if (cls == 0)
					{
						// 如果当前位置还没被标记为严重缺陷，则进行轻微缺陷复测
						if (hookStatus[globalIndex] != "挂钩明显错位")
						{
							// 1. 解析绝对坐标并进行边界保护
							int x1 = Math.Max(0, bbox.X);
							int y1 = Math.Max(0, bbox.Y);
							int x2 = Math.Min(imgW, bbox.X + bbox.Width);
							int y2 = Math.Min(imgH, bbox.Y + bbox.Height);

							if (x2 <= x1 || y2 <= y1)
							{
								hookStatus[globalIndex] = "OK";
								continue;
							}

							// 2. 裁剪出当前挂钩的局部图像 (利用 ROI 特性避免深拷贝，节省内存)
							using (Mat cropImg = new Mat(currentImage, new Rect(x1, y1, x2 - x1, y2 - y1)))
							{
								if (cropImg.Empty())
								{
									hookStatus[globalIndex] = "OK";
									continue;
								}

								// 3. 将局部图像送入分割模型推理
								SegResult segResult = hookSlightDefectModel.Predict(cropImg);

								Point[] innerCoords = null;
								Point[] outerCoords = null;

								if (segResult?.Masks != null)
								{
									for (int m = 0; m < segResult.ClassIds.Length; m++)
									{
										int maskClsId = segResult.ClassIds[m];
										// OpenCvSharp 的 FillPoly 需要 Point 整型数组
										var maskPts = segResult.Masks[m]
					  .Select(pt => new Point((int)Math.Round(pt.X), (int)Math.Round(pt.Y)))
					  .ToArray();

										if (maskClsId == blueAreaClassId)
											innerCoords = maskPts;
										else if (maskClsId == hangHoleClassId)
											outerCoords = maskPts;
									}
								}

								// 4. 如果成功提取到内外轮廓，进行厚度计算
								if (innerCoords != null && outerCoords != null && innerCoords.Length > 0 && outerCoords.Length > 0)
								{
									var (maxWidth, localCenterPt) = CalculateThicknessByInscribedCircle(cropImg.Size(), innerCoords, outerCoords);

									// 5. 依据阈值判定轻微错位
									if (maxWidth > thicknessThreshold)
									{
										hookStatus[globalIndex] = "轻微挂钩错位";

										// 第一步：将局部裁剪图中的圆心坐标还原到全局原图的绝对像素坐标
										double globalCenterX = localCenterPt.X + x1;
										double globalCenterY = localCenterPt.Y + y1;

										// 第二步：将绝对像素坐标转换为比例坐标 (归一化坐标)
										double ratioX = Math.Max(0.0, Math.Min(1.0, globalCenterX / imgW));
										double ratioY = Math.Max(0.0, Math.Min(1.0, globalCenterY / imgH));

										// 第三步：将直径四舍五入取整
										int diameter = (int)Math.Round(maxWidth);

										// 第四步：追加嵌套列表数据 [直径, [x比例, y比例]]
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

			// 清理字典中空的 List 以严格对齐 Python 中的 defaultdict 输出表现
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