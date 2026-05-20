using System;
using System.Collections.Generic;
using System.Linq;  // ← 添加这个
using OpenCvSharp;
using YoloInference;

namespace Detection
{
	public class DefectDetectionService
	{
		// ==========================================
		// 1. 基础配置与分类映射
		// ==========================================
		private static readonly Dictionary<int, string> ClassMap = new Dictionary<int, string>
		{
			{ 0, "搭舌缺陷" },
			{ 1, "边缘问题" },
			{ 2, "破损" }
		};

		/// <summary>
		/// 识别盒子下端面状态：破损、搭舌缺陷、边缘问题。
		/// </summary>
		/// <param name="model">YoloOnnx 推理模型实例</param>
		/// <param name="downImageList">缓存的下端面图像列表</param>
		/// <param name="p">整个检测面上盒子的总数</param>
		/// <returns>包含状态列表和缺陷归一化坐标字典列表的元组</returns>
		public static (List<List<string>> downStatus, List<Dictionary<string, List<Rect2f>>> ngCoordinates)
			CheckDownDefects(YoloOnnx model, List<Mat> downImageList, int p)
		{
			var downStatus = new List<List<string>>(p);
			var ngCoordinates = new List<Dictionary<string, List<Rect2f>>>(p);

			// ==========================================
			// 2. 输入前置校验
			// ==========================================
			if (downImageList.Count != p)
			{
				for (int i = 0; i < p; i++)
				{
					downStatus.Add(new List<string> { "数量错误" });
					ngCoordinates.Add(new Dictionary<string, List<Rect2f>>());
				}
				return (downStatus, ngCoordinates);
			}

			// ==========================================
			// 3. 批量推理与结果解析
			// ==========================================
			var results = model.PredictBatch(downImageList, confThres: 0.5f, iouThres: 0.2f);

			for (int i = 0; i < results.Count; i++)
			{
				var result = results[i];
				var currentStatusSet = new HashSet<string>();
				var currentCoordsDict = new Dictionary<string, List<Rect2f>>();

				// 检查当前图像是否检测到缺陷框
				if (result.Boxes != null && result.Boxes.Length > 0)
				{
					for (int j = 0; j < result.Boxes.Length; j++)
					{
						int clsId = result.ClassIds[j];
						Rect2f normalizedBox = result.BoxesN[j];

						string className = ClassMap.ContainsKey(clsId)
							? ClassMap[clsId]
							: $"未知缺陷_{clsId}";

						currentStatusSet.Add(className);

						if (!currentCoordsDict.ContainsKey(className))
						{
							currentCoordsDict[className] = new List<Rect2f>();
						}
						currentCoordsDict[className].Add(normalizedBox);
					}

					// 使用 new List<string>(hashSet) 替代 ToList()
					downStatus.Add(new List<string>(currentStatusSet));
					ngCoordinates.Add(currentCoordsDict);
				}
				else
				{
					// 未检测到缺陷，判定为 "OK"
					downStatus.Add(new List<string> { "OK" });
					ngCoordinates.Add(new Dictionary<string, List<Rect2f>>());
				}
			}

			// 二次核验输出长度
			if (downStatus.Count != p || ngCoordinates.Count != p)
			{
				throw new InvalidOperationException("输出结果列表长度与 P 不一致！");
			}

			return (downStatus, ngCoordinates);
		}
	}
}