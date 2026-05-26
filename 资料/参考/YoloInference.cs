using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using OpenCvSharp;
using OpenCvSharp.Dnn;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;
using Newtonsoft.Json; // 引入 JSON 解析库

namespace YoloInference
{
	// ==========================================
	// 0. 模型元数据映射类 (对接 meta.json)
	// ==========================================
	public class YoloMetadata
	{
		[JsonProperty("dynamic_axes")]
		public bool DynamicAxes { get; set; }

		[JsonProperty("train_imgsz")]
		public int[] TrainImgSz { get; set; }

		[JsonProperty("conf_thres")]
		public float ConfThres { get; set; }

		[JsonProperty("iou_thres")]
		public float IouThres { get; set; }

		[JsonProperty("task")]
		public string Task { get; set; }

		[JsonProperty("base_model")]
		public string BaseModel { get; set; }
	}

	// ==========================================
	// 1. 推理结果封装类
	// ==========================================
	public class YoloResult
	{
		public Rect[] Boxes { get; set; }        // 绝对坐标框 (x, y, width, height)
		public Rect2f[] BoxesN { get; set; }     // 归一化坐标框 (0.0 ~ 1.0)
		public float[] Scores { get; set; }      // 置信度
		public int[] ClassIds { get; set; }      // 类别 ID
		public Mat OrigImg { get; set; }         // 原始图像引用 (供后续绘图)

		// 性能耗时统计 (单位：毫秒 ms)
		public double PreprocessTimeMs { get; set; }
		public double InferenceTimeMs { get; set; }
		public double PostprocessTimeMs { get; set; }
		public double TotalTimeMs => PreprocessTimeMs + InferenceTimeMs + PostprocessTimeMs;
	}

	// ==========================================
	// 2. YOLO 核心推理类 (工业级生产版)
	// ==========================================
	public class YoloOnnx : IDisposable
	{
		private InferenceSession _session;
		private string _inputName;
		private int _inputH;
		private int _inputW;

		// 记录当前实例绑定的预期 Batch 大小
		private readonly int _expectedBatchSize;

		// [新增] 架构路由标志：是否为 YOLO26 (端到端输出格式)
		private readonly bool _isYolo26;

		// 实例绑定的默认阈值（从 json 获取）
		public float DefaultConfThres { get; private set; }
		public float DefaultIouThres { get; private set; }

		/// <summary>
		/// 构造函数
		/// </summary>
		public YoloOnnx(string modelPath, string metaJsonPath, int expectedBatchSize = 1)
		{
			if (!File.Exists(metaJsonPath))
				throw new FileNotFoundException($"找不到配置文件: {metaJsonPath}");

			// 1. 解析 Metadata 配置
			string jsonContent = File.ReadAllText(metaJsonPath);
			YoloMetadata meta = JsonConvert.DeserializeObject<YoloMetadata>(jsonContent);

			// [新增] 防御 JSON 文件为空或格式极度错误的情况
			if (meta == null)
			{
				throw new ArgumentException($"JSON 配置文件解析失败或内容为空！请检查文件内容: {metaJsonPath}");
			}

			if (meta.TrainImgSz == null || meta.TrainImgSz.Length < 2)
			{
				throw new ArgumentException("JSON 配置文件中的 train_imgsz 格式不合法 (缺失或长度不足)！");
			}

			DefaultConfThres = meta.ConfThres;
			DefaultIouThres = meta.IouThres;

			// [新增] 诊断是否为 yolo26 架构
			_isYolo26 = !string.IsNullOrEmpty(meta.BaseModel) && meta.BaseModel.IndexOf("26", StringComparison.OrdinalIgnoreCase) >= 0;

			// 2. 初始化 ONNX Session
			InitSession(modelPath);

			// 3. 解析并校验输入张量
			var inputMeta = _session.InputMetadata.First();
			_inputName = inputMeta.Key;
			var inputShape = inputMeta.Value.Dimensions;

			if (inputShape.Length < 4)
				throw new NotSupportedException("不支持的输入张量格式，期望维度应为 4 (Batch, C, H, W)。");

			// 4. 根据 dynamic_axes 执行不同的尺寸治理策略
			if (meta.DynamicAxes)
			{
				_inputH = meta.TrainImgSz[0];
				_inputW = meta.TrainImgSz[1];

				if (expectedBatchSize <= 0)
					throw new ArgumentException("动态模式下 BatchSize 必须大于 0");

				_expectedBatchSize = expectedBatchSize;
				Console.WriteLine($"[INFO] 动态模型检测: 采用配置尺寸 {_inputW}x{_inputH}, 授权用户 BatchSize={_expectedBatchSize}");
			}
			else
			{
				_inputH = inputShape[2];
				_inputW = inputShape[3];

				if (_inputH != meta.TrainImgSz[0] || _inputW != meta.TrainImgSz[1])
				{
					throw new ArgumentException($"[致命错误] 静态 ONNX 模型的实际尺寸 ({_inputW}x{_inputH}) " +
												$"与训练尺寸 ({meta.TrainImgSz[1]}x{meta.TrainImgSz[0]}) 发生冲突！");
				}

				_expectedBatchSize = 1;
				Console.WriteLine($"[INFO] 静态模型验证通过: 尺寸 {_inputW}x{_inputH}, 强制 BatchSize=1");
			}

			Console.WriteLine($"[INFO] 架构路由识别: {(_isYolo26 ? "YOLO26 (端到端免NMS模式 [Batch, 300, 6])" : "标准YOLO (密集锚框模式)")}");

			// 5. 显存预热
			Warmup(iterations: 3, warmupBatchSize: _expectedBatchSize);
		}

		private void InitSession(string modelPath)
		{
			try
			{
				SessionOptions options = new SessionOptions();
				options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;

				var cudaProviderOptions = new OrtCUDAProviderOptions();
				cudaProviderOptions.UpdateOptions(new Dictionary<string, string>()
				{
					{ "cudnn_conv_algo_search", "HEURISTIC" },
					{ "arena_extend_strategy", "kNextPowerOfTwo" },
					{ "do_copy_in_default_stream", "1" }
				});

				options.AppendExecutionProvider_CUDA(cudaProviderOptions);
				_session = new InferenceSession(modelPath, options);
				Console.WriteLine($"[INFO] 模型加载成功: CUDA 硬件加速引擎启动");
			}
			catch (Exception ex)
			{
				Console.WriteLine($"[WARNING] CUDA 初始化失败，尝试降级 CPU。原因: {ex.Message}\r\n{ex.StackTrace}");
				SessionOptions options = new SessionOptions();
				options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
				options.AppendExecutionProvider_CPU(0);
				options.InterOpNumThreads = 2;
				options.IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2);
				_session = new InferenceSession(modelPath, options);
				Console.WriteLine("[INFO] 模型加载成功: 纯 CPU 模式");
			}
		}

		private void Warmup(int iterations, int warmupBatchSize)
		{
			Console.WriteLine($"[INFO] 开始显存预热 ({iterations} 次, 张量=[{warmupBatchSize}, 3, {_inputH}, {_inputW}])...");
			float[] dummyData = new float[warmupBatchSize * 3 * _inputH * _inputW];
			var dummyTensor = new DenseTensor<float>(dummyData, new int[] { warmupBatchSize, 3, _inputH, _inputW });
			var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_inputName, dummyTensor) };

			for (int i = 0; i < iterations; i++)
			{
				using (var results = _session.Run(inputs)) { }
			}
			Console.WriteLine("[INFO] 模型预热完毕，硬件已就绪！\n");
		}

		public YoloResult Predict(Mat origImg, float? confThres = null, float? iouThres = null)
		{
			var results = PredictBatch(new List<Mat> { origImg }, confThres, iouThres);
			return results.FirstOrDefault();
		}

		public List<YoloResult> PredictBatch(List<Mat> origImgs, float? confThres = null, float? iouThres = null)
		{
			int batchSize = origImgs.Count;
			if (batchSize == 0) return new List<YoloResult>();

			float finalConf = confThres ?? DefaultConfThres;
			float finalIou = iouThres ?? DefaultIouThres;

			var sw = new Stopwatch();
			float[] ratios = new float[batchSize];
			float[] padWs = new float[batchSize];
			float[] padHs = new float[batchSize];

			// 1. 批量预处理
			sw.Restart();
			var tensor = PreprocessBatchOptimized(origImgs, ratios, padWs, padHs);
			var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_inputName, tensor) };
			sw.Stop();
			double preTimeMs = sw.Elapsed.TotalMilliseconds;

			// 2. 模型推理
			sw.Restart();
			using (var results = _session.Run(inputs))
			{
				var output = results.First().AsTensor<float>();
				sw.Stop();
				double inferTimeMs = sw.Elapsed.TotalMilliseconds;

				// 3. 批量后处理 (利用多态路由决定解析策略)
				sw.Restart();
				List<YoloResult> finalResults;
				if (_isYolo26)
				{
					// YOLO26 端到端解析
					finalResults = PostprocessYolo26(output, origImgs, ratios, padWs, padHs, finalConf);
				}
				else
				{
					// 标准 YOLO 解析
					finalResults = PostprocessStandard(output, origImgs, ratios, padWs, padHs, finalConf, finalIou);
				}
				sw.Stop();
				double postTimeMs = sw.Elapsed.TotalMilliseconds;

				foreach (var res in finalResults)
				{
					res.PreprocessTimeMs = preTimeMs / batchSize;
					res.InferenceTimeMs = inferTimeMs / batchSize;
					res.PostprocessTimeMs = postTimeMs / batchSize;
				}

				return finalResults;
			}
		}

		private DenseTensor<float> PreprocessBatchOptimized(List<Mat> origImgs, float[] ratios, float[] padWs, float[] padHs)
		{
			int batchSize = origImgs.Count;
			int planeSize = _inputH * _inputW;
			int singleImgTensorSize = 3 * planeSize;
			float[] tensorData = new float[batchSize * singleImgTensorSize];
			float padValue = 114f / 255.0f;

			for (int i = 0; i < tensorData.Length; i++) tensorData[i] = padValue;

			Parallel.For(0, batchSize, b =>
			{
				Mat origImg = origImgs[b];
				int w = origImg.Width, h = origImg.Height;

				float ratio = Math.Min((float)_inputH / h, (float)_inputW / w);
				ratios[b] = ratio;

				int newW = (int)Math.Round(w * ratio);
				int newH = (int)Math.Round(h * ratio);

				float padW = (_inputW - newW) / 2f;
				float padH = (_inputH - newH) / 2f;
				padWs[b] = padW;
				padHs[b] = padH;

				int padWInt = (int)Math.Round(padW - 0.1);
				int padHInt = (int)Math.Round(padH - 0.1);

				using (Mat resized = new Mat())
				{
					if (w != newW || h != newH)
						Cv2.Resize(origImg, resized, new Size(newW, newH), 0, 0, InterpolationFlags.Linear);
					else
						origImg.CopyTo(resized);

					int stride = (int)resized.Step();
					int batchOffset = b * singleImgTensorSize;

					unsafe
					{
						byte* pSrcBase = (byte*)resized.DataPointer;
						fixed (float* pDstBase = tensorData)
						{
							float* pDstBatch = pDstBase + batchOffset;
							for (int y = 0; y < newH; y++)
							{
								byte* row = pSrcBase + y * stride;
								int targetY = y + padHInt;

								float* rDst = pDstBatch + (targetY * _inputW) + padWInt;
								float* gDst = pDstBatch + planeSize + (targetY * _inputW) + padWInt;
								float* bDst = pDstBatch + (2 * planeSize) + (targetY * _inputW) + padWInt;

								for (int x = 0; x < newW; x++)
								{
									rDst[x] = row[x * 3 + 2] / 255.0f;
									gDst[x] = row[x * 3 + 1] / 255.0f;
									bDst[x] = row[x * 3 + 0] / 255.0f;
								}
							}
						}
					}
				}
			});

			return new DenseTensor<float>(tensorData, new int[] { batchSize, 3, _inputH, _inputW });
		}

		// =========================================================
		// [新增] YOLO26 解析逻辑 (针对 [batch, 300, 6] 端到端输出)
		// =========================================================
		private List<YoloResult> PostprocessYolo26(Tensor<float> output, List<Mat> origImgs, float[] ratios, float[] padWs, float[] padHs, float confThres)
		{
			int batchSize = origImgs.Count;
			// 预期维度：Dimensions[1] = 300 (最大检测数), Dimensions[2] = 6 (x1, y1, x2, y2, score, classId)
			int numBoxes = output.Dimensions[1];
			int elementsPerBox = output.Dimensions[2];

			if (elementsPerBox != 6)
				throw new InvalidDataException($"YOLO26 期望最后一维为 6，但得到了 {elementsPerBox}");

			int singleBatchOutputSize = numBoxes * elementsPerBox;
			float[] outputArray = output.ToArray();
			List<YoloResult> results = new List<YoloResult>(batchSize);

			unsafe
			{
				fixed (float* pOutputBase = outputArray)
				{
					for (int b = 0; b < batchSize; b++)
					{
						float* pOutput = pOutputBase + b * singleBatchOutputSize;

						List<Rect> boxes = new List<Rect>();
						List<float> scores = new List<float>();
						List<int> classIds = new List<int>();

						int origW = origImgs[b].Width;
						int origH = origImgs[b].Height;
						float ratio = ratios[b];
						float padW = padWs[b];
						float padH = padHs[b];

						for (int i = 0; i < numBoxes; i++)
						{
							// 指针偏移，每次跳 6 个浮点数
							float* pBox = pOutput + i * elementsPerBox;

							float score = pBox[4]; // 第 5 个元素是置信度
							if (score < confThres) continue; // 模型内部已做NMS，只需做阈值截断

							float x1 = pBox[0];
							float y1 = pBox[1];
							float x2 = pBox[2];
							float y2 = pBox[3];
							int classId = (int)Math.Round(pBox[5]); // 第 6 个元素是类别ID

							// 坐标还原：减去 Padding，除以缩放比例
							float origX1 = (x1 - padW) / ratio;
							float origY1 = (y1 - padH) / ratio;
							float origX2 = (x2 - padW) / ratio;
							float origY2 = (y2 - padH) / ratio;

							// 边界安全裁剪 (Clamp)
							origX1 = Math.Max(0, Math.Min(origX1, origW));
							origY1 = Math.Max(0, Math.Min(origY1, origH));
							origX2 = Math.Max(0, Math.Min(origX2, origW));
							origY2 = Math.Max(0, Math.Min(origY2, origH));

							boxes.Add(new Rect((int)origX1, (int)origY1, (int)(origX2 - origX1), (int)(origY2 - origY1)));
							scores.Add(score);
							classIds.Add(classId);
						}

						// 组装最终结果（由于免NMS，直接填充即可）
						var finalResult = new YoloResult
						{
							Boxes = boxes.ToArray(),
							Scores = scores.ToArray(),
							ClassIds = classIds.ToArray(),
							BoxesN = new Rect2f[boxes.Count],
							OrigImg = origImgs[b]
						};

						for (int i = 0; i < boxes.Count; i++)
						{
							Rect box = boxes[i];
							finalResult.BoxesN[i] = new Rect2f((float)box.X / origW, (float)box.Y / origH, (float)box.Width / origW, (float)box.Height / origH);
						}

						results.Add(finalResult);
					}
				}
			}
			return results;
		}

		// =========================================================
		// 原有逻辑：标准 YOLO 解析 (密集锚框 + NMS)
		// =========================================================
		private List<YoloResult> PostprocessStandard(Tensor<float> output, List<Mat> origImgs, float[] ratios, float[] padWs, float[] padHs, float confThres, float iouThres)
		{
			int batchSize = origImgs.Count;
			int numClassesAndCoords = output.Dimensions[1];
			int numAnchors = output.Dimensions[2];
			int numClasses = numClassesAndCoords - 4;
			int singleBatchOutputSize = numClassesAndCoords * numAnchors;

			float[] outputArray = output.ToArray();
			List<YoloResult> results = new List<YoloResult>(batchSize);

			unsafe
			{
				fixed (float* pOutputBase = outputArray)
				{
					for (int b = 0; b < batchSize; b++)
					{
						float* pOutput = pOutputBase + b * singleBatchOutputSize;
						List<Rect> candidates = new List<Rect>();
						List<float> confidences = new List<float>();
						List<int> classIds = new List<int>();

						float* pCx = pOutput;
						float* pCy = pOutput + numAnchors;
						float* pW = pOutput + 2 * numAnchors;
						float* pH = pOutput + 3 * numAnchors;
						float* pClasses = pOutput + 4 * numAnchors;

						int origW = origImgs[b].Width;
						int origH = origImgs[b].Height;
						float ratio = ratios[b];
						float padW = padWs[b];
						float padH = padHs[b];

						for (int a = 0; a < numAnchors; a++)
						{
							float maxScore = 0;
							int classId = 0;

							for (int c = 0; c < numClasses; c++)
							{
								float score = pClasses[c * numAnchors + a];
								if (score > maxScore)
								{
									maxScore = score;
									classId = c;
								}
							}

							if (maxScore > confThres)
							{
								float cx = pCx[a], cy = pCy[a], w = pW[a], h = pH[a];
								float x1 = (cx - w / 2f - padW) / ratio;
								float y1 = (cy - h / 2f - padH) / ratio;
								float x2 = (cx + w / 2f - padW) / ratio;
								float y2 = (cy + h / 2f - padH) / ratio;

								x1 = Math.Max(0, Math.Min(x1, origW));
								y1 = Math.Max(0, Math.Min(y1, origH));
								x2 = Math.Max(0, Math.Min(x2, origW));
								y2 = Math.Max(0, Math.Min(y2, origH));

								candidates.Add(new Rect((int)x1, (int)y1, (int)(x2 - x1), (int)(y2 - y1)));
								confidences.Add(maxScore);
								classIds.Add(classId);
							}
						}

						// 标准模型需要 NMS 过滤
						CvDnn.NMSBoxes(candidates, confidences, confThres, iouThres, out int[] indices);

						var finalResult = new YoloResult
						{
							Boxes = new Rect[indices.Length],
							BoxesN = new Rect2f[indices.Length],
							Scores = new float[indices.Length],
							ClassIds = new int[indices.Length],
							OrigImg = origImgs[b]
						};

						for (int i = 0; i < indices.Length; i++)
						{
							int idx = indices[i];
							Rect box = candidates[idx];
							finalResult.Boxes[i] = box;
							finalResult.Scores[i] = confidences[idx];
							finalResult.ClassIds[i] = classIds[idx];
							finalResult.BoxesN[i] = new Rect2f((float)box.X / origW, (float)box.Y / origH, (float)box.Width / origW, (float)box.Height / origH);
						}
						results.Add(finalResult);
					}
				}
			}
			return results;
		}

		public void Dispose()
		{
			_session?.Dispose();
		}
	}

	// ==========================================
	// 3. 入口程序 (包含绘图与导出演示)
	// ==========================================
	//class Program
	//{
	//	static void Main(string[] args)
	//	{
	//		string modelPath = "E:\\koch纸盒\\模型\\下端面\\下端面缺陷目标检测\\best.onnx";
	//		string metaJsonPath = "E:\\koch纸盒\\模型\\下端面\\下端面缺陷目标检测\\meta.json";
	//		string imagePath = "E:\\koch纸盒\\模型\\下端面\\下端面缺陷目标检测\\Pic_2026_04_09_102904_12.jpeg";

	//		// 假设推理输出的图片保存目录
	//		string outputDir = "E:\\koch纸盒\\模型\\下端面\\检测结果";

	//		int currentScenarioBatchSize = 2;

	//		try
	//		{
	//			if (!Directory.Exists(outputDir))
	//				Directory.CreateDirectory(outputDir);

	//			using (var yolo = new YoloOnnx(modelPath, metaJsonPath, expectedBatchSize: currentScenarioBatchSize))
	//			using (var img = Cv2.ImRead(imagePath))
	//			{
	//				if (img.Empty())
	//				{
	//					Console.WriteLine("❌ 图片读取失败！请检查路径。");
	//					return;
	//				}

	//				var batchImages = new List<Mat>();
	//				for (int i = 0; i < currentScenarioBatchSize; i++)
	//				{
	//					batchImages.Add(img.Clone());
	//				}

	//				Console.WriteLine($"\n[INFO] 开始执行正式推理 (BatchSize = {batchImages.Count})...");

	//				var results = yolo.PredictBatch(batchImages, confThres: yolo.DefaultConfThres, iouThres: yolo.DefaultIouThres);

	//				for (int batchIdx = 0; batchIdx < results.Count; batchIdx++)
	//				{
	//					var result = results[batchIdx];
	//					Console.WriteLine($"\n====== ⚡ Batch[{batchIdx}] 性能报告 ======");
	//					Console.WriteLine($"预处理耗时:   {result.PreprocessTimeMs:F2} ms");
	//					Console.WriteLine($"模型推理耗时: {result.InferenceTimeMs:F2} ms");
	//					Console.WriteLine($"后处理耗时:   {result.PostprocessTimeMs:F2} ms");
	//					Console.WriteLine($"均摊总耗时:   {result.TotalTimeMs:F2} ms");
	//					Console.WriteLine($"检测到目标数: {result.Boxes.Length} 个");

	//					// --- 绘制检测结果图 ---
	//					using (Mat drawImg = result.OrigImg.Clone())
	//					{
	//						for (int i = 0; i < result.Boxes.Length; i++)
	//						{
	//							Rect box = result.Boxes[i];
	//							float score = result.Scores[i];
	//							int classId = result.ClassIds[i];

	//							// 绘制红色边界框 (BGR: 0, 0, 255)，线宽为2
	//							Cv2.Rectangle(drawImg, box, new Scalar(0, 0, 255), 2);

	//							// 拼接标签文本 (例如: ID:1 95%)
	//							string label = $"ID:{classId} {(score * 100):F0}%";

	//							// 计算文字大小并绘制黑色背景以便看清文字
	//							var textSize = Cv2.GetTextSize(label, HersheyFonts.HersheySimplex, 0.6, 1, out int baseline);
	//							Point textOrg = new Point(box.X, Math.Max(box.Y - 5, textSize.Height));
	//							Rect textBgRect = new Rect(textOrg.X, textOrg.Y - textSize.Height, textSize.Width, textSize.Height + baseline);
	//							Cv2.Rectangle(drawImg, textBgRect, new Scalar(0, 0, 255), -1); // 实心矩形作为底色

	//							// 绘制白色文本
	//							Cv2.PutText(drawImg, label, textOrg, HersheyFonts.HersheySimplex, 0.6, Scalar.White, 1);
	//						}

	//						// 保存结果图到磁盘
	//						string outFilePath = Path.Combine(outputDir, $"Result_Batch{batchIdx}.jpg");
	//						Cv2.ImWrite(outFilePath, drawImg);
	//						Console.WriteLine($"[INFO] 检测结果图已保存至: {outFilePath}");
	//					}
	//				}

	//				// 释放克隆的图像资源
	//				foreach (var m in batchImages) m.Dispose();
	//			}
	//		}
	//		catch (Exception ex)
	//		{
	//			Console.WriteLine($"\n❌ 发生异常: {ex.Message}\r\n{ex.StackTrace}\n{ex.StackTrace}");
	//		}

	//		Console.WriteLine("\n按任意键退出...");
	//		Console.ReadKey();
	//	}
	//}
}