using System;
using System.Collections.Generic;
using OpenCvSharp;
using YoloInference; // 引用你提供的命名空间

namespace Detection
{
/// <summary>侧面缺陷处理器 — 头尾裁剪YOLO推理→坐标映射→输出缺陷</summary>
	public class SideDefectProcessor
	{
	/// <summary>侧面缺陷检测: 头尾裁剪YOLO推理->坐标映射回原图归一化坐标.</summary>
		public static Tuple<List<string>, List<float[]>> DetectSideDefects(Mat image, float cropRatio, YoloOnnx model)
		{
			int height = image.Height;
			int width = image.Width;

			// 1. 计算裁剪宽度
			int cropWidth = (int)(height * cropRatio);
			if (cropWidth > width)
			{
				cropWidth = width;
			}

			List<string> defectsList = new List<string>();
			List<float[]> boxesList = new List<float[]>();

			// 2. 图像裁剪 (利用 ROI 提取，通过 using 释放非托管资源)
			using (Mat headCrop = new Mat(image, new Rect(0, 0, cropWidth, height)))
			using (Mat tailCrop = new Mat(image, new Rect(width - cropWidth, 0, cropWidth, height)))
			{
				// 3. Batch 批量推理
				List<Mat> batchImages = new List<Mat> { headCrop, tailCrop };
				List<YoloResult> results = model.PredictBatch(batchImages);

				if (results == null || results.Count < 2)
				{
					return new Tuple<List<string>, List<float[]>>(defectsList, boxesList);
				}

				// 4. 解析头部检测结果
				YoloResult headResults = results[0];
				for (int i = 0; i < headResults.Boxes.Length; i++)
				{
					// 对应 Python 中的: if int(box.cls[0].item()) == 0
					if (headResults.ClassIds[i] == 0)
					{
						defectsList.Add("defects");
						Rect box = headResults.Boxes[i];

						// 计算相对坐标并保留 6 位小数
						float[] relBox = new float[]
			{
			  (float)Math.Round((double)box.Left / width, 6),
			  (float)Math.Round((double)box.Top / height, 6),
			  (float)Math.Round((double)box.Right / width, 6),
			  (float)Math.Round((double)box.Bottom / height, 6)
			};
						boxesList.Add(relBox);
					}
				}

				// 5. 解析尾部检测结果
				YoloResult tailResults = results[1];
				int xOffset = width - cropWidth;

				for (int i = 0; i < tailResults.Boxes.Length; i++)
				{
					if (tailResults.ClassIds[i] == 0)
					{
						defectsList.Add("defects");
						Rect box = tailResults.Boxes[i];

						// 加上偏移量还原为原图绝对坐标，然后再除以原图宽高计算相对坐标
						float[] relBox = new float[]
			{
			  (float)Math.Round((double)(box.Left + xOffset) / width, 6),
			  (float)Math.Round((double)box.Top / height, 6),
			  (float)Math.Round((double)(box.Right + xOffset) / width, 6),
			  (float)Math.Round((double)box.Bottom / height, 6)
			};
						boxesList.Add(relBox);
					}
				}
			}

			return new Tuple<List<string>, List<float[]>>(defectsList, boxesList);
		}
	}

	
}
