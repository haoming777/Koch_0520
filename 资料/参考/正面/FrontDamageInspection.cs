using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using YoloInference;

public class FrontDamageInspection
{
	// 模拟 Python 中 check_front_model.names 字典结构
	// 在实际应用中，您可以通过解析 ONNX metadata 或硬编码配置注入类别映射
	public static readonly Dictionary<int, string> ModelClassNames = new Dictionary<int, string>
	{
	  { 0, "damage" }
            // { 1, "scratch" } ...
        };

	/// <summary>
	        /// 使用 C# 基础数学库向量化执行非极大值抑制 (NMS)
	        /// </summary>
	        /// <param name="boxesWithScores">包含置信度的边界框列表 [x1, y1, x2, y2, score]</param>
	        /// <param name="iouThreshold">交并比阈值</param>
	        /// <returns>过滤后的边界框列表 [x1, y1, x2, y2]</returns>
	public static List<float[]> ApplyNms(List<float[]> boxesWithScores, float iouThreshold = 0.45f)
	{
		if (boxesWithScores == null || boxesWithScores.Count == 0)
			return new List<float[]>();

		// 按置信度降序排序
		var sortedBoxes = boxesWithScores.OrderByDescending(b => b[4]).ToList();
		var keep = new List<float[]>();
		bool[] isRemoved = new bool[sortedBoxes.Count];

		for (int i = 0; i < sortedBoxes.Count; i++)
		{
			if (isRemoved[i]) continue;

			var current = sortedBoxes[i];
			// 保留当前最高置信度框的坐标（去除得分）
			keep.Add(new float[] { current[0], current[1], current[2], current[3] });

			float areaI = (current[2] - current[0]) * (current[3] - current[1]);

			for (int j = i + 1; j < sortedBoxes.Count; j++)
			{
				if (isRemoved[j]) continue;

				var compare = sortedBoxes[j];
				float xx1 = Math.Max(current[0], compare[0]);
				float yy1 = Math.Max(current[1], compare[1]);
				float xx2 = Math.Min(current[2], compare[2]);
				float yy2 = Math.Min(current[3], compare[3]);

				float w = Math.Max(0.0f, xx2 - xx1);
				float h = Math.Max(0.0f, yy2 - yy1);
				float inter = w * h;

				float areaJ = (compare[2] - compare[0]) * (compare[3] - compare[1]);
				float iou = inter / (areaI + areaJ - inter);

				if (iou > iouThreshold)
				{
					isRemoved[j] = true;
				}
			}
		}

		return keep;
	}

	/// <summary>
	        /// 对图像进行切分，并返回子图及其在原图中的左上角偏移坐标
	        /// </summary>
	private static (List<Mat> Patches, List<Point> Offsets) GetCropPatchesAndOffsets(Mat image, int P)
	{
		int h = image.Height;
		int w = image.Width;
		var xBoundaries = new List<(int start, int end)>();

		if (P / 2 == 5)
		{
			xBoundaries.Add((0, (int)(w * 0.4)));
			xBoundaries.Add(((int)(w * 0.4), (int)(w * 0.8)));
			xBoundaries.Add(((int)(w * 0.8), w));
		}
		else
		{
			int wThird = w / 3;
			xBoundaries.Add((0, wThird));
			xBoundaries.Add((wThird, wThird * 2));
			xBoundaries.Add((wThird * 2, w));
		}

		var yBoundaries = new List<(int start, int end)>
	  {
		(0, (int)(h * 0.55)),
		((int)(h * 0.45), h)
	  };

		var croppedImages = new List<Mat>();
		var offsets = new List<Point>();

		foreach (var xb in xBoundaries)
		{
			foreach (var yb in yBoundaries)
			{
				int patchW = xb.end - xb.start;
				int patchH = yb.end - yb.start;
				Rect roi = new Rect(xb.start, yb.start, patchW, patchH);

				// 使用 Clone 分配独立的连续内存，避免指针越界
				croppedImages.Add(new Mat(image, roi).Clone());
				offsets.Add(new Point(xb.start, yb.start));
			}
		}

		return (croppedImages, offsets);
	}

	/// <summary>
	        /// 处理左右两图，执行批量推理并坐标映射，最后应用NMS去除重叠框
	        /// </summary>
	public static (List<string> StatusList, Dictionary<string, List<float[]>> FinalLeftDict, Dictionary<string, List<float[]>> FinalRightDict)
  CheckFrontDamage(Mat leftImage, Mat rightImage, int P, YoloOnnx yoloModel)
	{
		if (leftImage == null || leftImage.Empty() || rightImage == null || rightImage.Empty())
			throw new ArgumentException("输入的图像不能为空。");

		int halfP = P / 2;
		int h = leftImage.Height;
		int w = leftImage.Width;

		var statusList = Enumerable.Repeat("OK", P).ToList();
		var tempLeftDict = new Dictionary<string, List<float[]>>();
		var tempRightDict = new Dictionary<string, List<float[]>>();

		// 1. 获取切片数据
		var (leftPatches, leftOffsets) = GetCropPatchesAndOffsets(leftImage, P);
		var (rightPatches, rightOffsets) = GetCropPatchesAndOffsets(rightImage, P);

		var allPatches = new List<Mat>(leftPatches.Count + rightPatches.Count);
		allPatches.AddRange(leftPatches);
		allPatches.AddRange(rightPatches);

		// 建立模型输出标签的翻译映射表
		var labelTranslationMap = new Dictionary<string, string>
	  {
		{ "damage", "破损" }
	  };

		try
		{
			// 2. 核心批量推理
			var results = yoloModel.PredictBatch(allPatches, confThres: 0.25f, iouThres: 0.45f);

			// 3. 结果解析与坐标重映射
			for (int i = 0; i < results.Count; i++)
			{
				var result = results[i];
				bool isLeft = i < leftPatches.Count;
				var offsets = isLeft ? leftOffsets : rightOffsets;
				int offsetIdx = isLeft ? i : i - leftPatches.Count;

				int xStart = offsets[offsetIdx].X;
				int yStart = offsets[offsetIdx].Y;
				var targetDict = isLeft ? tempLeftDict : tempRightDict;
				int baseIdx = isLeft ? 0 : halfP;

				for (int j = 0; j < result.Boxes.Length; j++)
				{
					int classId = result.ClassIds[j];
					string rawClassName = ModelClassNames.ContainsKey(classId) ? ModelClassNames[classId] : classId.ToString();
					string className = labelTranslationMap.ContainsKey(rawClassName) ? labelTranslationMap[rawClassName] : rawClassName;

					var box = result.Boxes[j];
					float score = result.Scores[j];

					// 转换回原图绝对坐标系
					float origX1 = box.Left + xStart;
					float origY1 = box.Top + yStart;
					float origX2 = box.Right + xStart;
					float origY2 = box.Bottom + yStart;

					// 归一化并附加置信度
					float[] normBoxWithScore = {
			  origX1 / w, origY1 / h, origX2 / w, origY2 / h, score
			};

					if (!targetDict.ContainsKey(className))
					{
						targetDict[className] = new List<float[]>();
					}
					targetDict[className].Add(normBoxWithScore);

					// 状态阵列计算
					float centerX = (origX1 + origX2) / 2.0f;
					int brushLocalIdx = (int)((centerX / w) * halfP);
					brushLocalIdx = Math.Max(0, Math.Min(brushLocalIdx, halfP - 1));

					int globalIdx = baseIdx + brushLocalIdx;
					statusList[globalIdx] = className;
				}
			}
		}
		finally
		{
			// 4. 清理克隆的临时图像内存
			foreach (var patch in allPatches)
			{
				patch?.Dispose();
			}
		}

		// 5. 全局 NMS 后处理
		var finalLeftDict = new Dictionary<string, List<float[]>>();
		foreach (var kvp in tempLeftDict)
		{
			finalLeftDict[kvp.Key] = ApplyNms(kvp.Value, iouThreshold: 0.45f);
		}

		var finalRightDict = new Dictionary<string, List<float[]>>();
		foreach (var kvp in tempRightDict)
		{
			finalRightDict[kvp.Key] = ApplyNms(kvp.Value, iouThreshold: 0.45f);
		}

		return (statusList, finalLeftDict, finalRightDict);
	}
}
