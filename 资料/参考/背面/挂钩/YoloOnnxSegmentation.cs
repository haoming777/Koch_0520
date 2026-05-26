using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using OpenCvSharp;
using Microsoft.ML.OnnxRuntime;
using Microsoft.ML.OnnxRuntime.Tensors;

namespace YoloSegmentationEnd2End // 按照要求修改了专属命名空间
{
	// ==========================================
	// 1. 推理结果封装类 (支持边界框与多边形掩码)
	// ==========================================
	public class YoloResult
	{
		public Rect[] Boxes { get; set; }        // 绝对坐标边界框 (x, y, width, height)
		public Rect2f[] BoxesN { get; set; }     // 归一化比例边界框 (0.0 ~ 1.0)
		public Point2f[][] Masks { get; set; }   // 掩码多边形轮廓坐标 (绝对像素，等同于 masks.xy)
		public Point2f[][] MasksN { get; set; }  // 掩码多边形轮廓坐标 (归一化比例，等同于 masks.xyn)
		public float[] Scores { get; set; }      // 置信度
		public int[] ClassIds { get; set; }      // 类别 ID
		public Mat OrigImg { get; set; }         // 原始图像引用 (供后续绘图或二次裁剪)

		// 性能耗时统计 (单位：毫秒 ms)
		public double PreprocessTimeMs { get; set; }
		public double InferenceTimeMs { get; set; }
		public double PostprocessTimeMs { get; set; }
		public double TotalTimeMs => PreprocessTimeMs + InferenceTimeMs + PostprocessTimeMs;
	}

	// ==========================================
	// 2. YOLO 分割核心推理类 (End-to-End 生产版)
	// ==========================================
	public class YoloOnnxSegmentation : IDisposable
	{
		private InferenceSession _session;
		private string _inputName;
		private string[] _outputNames;
		private int _inputH;
		private int _inputW;

		// 记录当前实例绑定的预期 Batch 大小，用于显存分配策略
		private readonly int _expectedBatchSize;

		/// <summary>
		/// 模型输入高度（公开属性）
		/// </summary>
		public int InputHeight => _inputH;

		/// <summary>
		/// 模型输入宽度（公开属性）
		/// </summary>
		public int InputWidth => _inputW;

		/// <summary>
		/// 构造函数，初始化 ONNX Runtime 环境
		/// </summary>
		public YoloOnnxSegmentation(string modelPath, int expectedBatchSize = 1, int fallbackH = 640, int fallbackW = 640)
		{
			if (expectedBatchSize <= 0) throw new ArgumentException("BatchSize 必须大于 0");
			_expectedBatchSize = expectedBatchSize;

			try
			{
				SessionOptions options = new SessionOptions();
				options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;

				// 配置 CUDA 提供程序选项
				var cudaProviderOptions = new OrtCUDAProviderOptions();
				cudaProviderOptions.UpdateOptions(new Dictionary<string, string>()
				{
					{ "cudnn_conv_algo_search", "HEURISTIC" }, // 启发式搜索算法，避免 BatchSize 变化时的卡顿
                    { "arena_extend_strategy", "kNextPowerOfTwo" }, // 显存扩容策略
                    { "do_copy_in_default_stream", "1" }
				});

				options.AppendExecutionProvider_CUDA(cudaProviderOptions);
				_session = new InferenceSession(modelPath, options);
				Console.WriteLine($"[INFO] 模型加载成功: CUDA 加速 (绑定 BatchSize={_expectedBatchSize})");
			}
			catch (Exception ex)
			{
				// 如果当前机器没有 N 卡或缺少环境，自动优雅降级为 CPU 模式
				Console.WriteLine($"[WARNING] CUDA 初始化失败，自动降级为 CPU 模式。原因: {ex.Message}\r\n{ex.StackTrace}");
				SessionOptions options = new SessionOptions();
				options.GraphOptimizationLevel = GraphOptimizationLevel.ORT_ENABLE_ALL;
				options.AppendExecutionProvider_CPU(0);
				// CPU 多线程优化
				options.InterOpNumThreads = 2;
				options.IntraOpNumThreads = Math.Max(1, Environment.ProcessorCount / 2);
				_session = new InferenceSession(modelPath, options);
			}

			// 解析输入张量元数据
			var inputMeta = _session.InputMetadata.First();
			_inputName = inputMeta.Key;
			_outputNames = _session.OutputMetadata.Keys.ToArray();
			var inputShape = inputMeta.Value.Dimensions;

			// 针对动态分辨率模型的兼容（如果模型导出时指定了动态轴 -1）
			_inputH = inputShape[2] > 0 ? inputShape[2] : fallbackH;
			_inputW = inputShape[3] > 0 ? inputShape[3] : fallbackW;

			// 【注意这里】：调用预热方法，强制分配显存
			Warmup(iterations: 3, warmupBatchSize: _expectedBatchSize);
		}

		/// <summary>
		/// 模型预热：通过灌入假数据让 cuDNN 提前完成底层显存分配和算法寻优
		/// </summary>
		private void Warmup(int iterations, int warmupBatchSize)
		{
			Console.WriteLine($"[INFO] 开始模型预热 ({iterations} 次, 预热张量尺寸=[{warmupBatchSize}, 3, {_inputH}, {_inputW}])...");

			float[] dummyData = new float[warmupBatchSize * 3 * _inputH * _inputW];
			var dummyTensor = new DenseTensor<float>(dummyData, new int[] { warmupBatchSize, 3, _inputH, _inputW });
			var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_inputName, dummyTensor) };

			for (int i = 0; i < iterations; i++)
			{
				using (var results = _session.Run(inputs)) { }
			}
			Console.WriteLine("[INFO] 模型预热完毕，GPU 已就绪！");
		}

		/// <summary>
		/// 单图推理快捷接口
		/// </summary>
		public YoloResult Predict(Mat origImg, float confThres = 0.25f)
		{
			var results = PredictBatch(new List<Mat> { origImg }, confThres);
			return results.FirstOrDefault();
		}

		/// <summary>
		/// 批量推理核心接口
		/// </summary>
		public List<YoloResult> PredictBatch(List<Mat> origImgs, float confThres = 0.25f)
		{
			int batchSize = origImgs.Count;
			if (batchSize == 0) return new List<YoloResult>();

			var sw = new Stopwatch();
			float[] ratios = new float[batchSize];
			float[] padWs = new float[batchSize];
			float[] padHs = new float[batchSize];

			// 1. 图像预处理
			sw.Restart();
			var tensor = PreprocessBatchOptimized(origImgs, ratios, padWs, padHs);
			var inputs = new List<NamedOnnxValue> { NamedOnnxValue.CreateFromTensor(_inputName, tensor) };
			sw.Stop();
			double preTimeMs = sw.Elapsed.TotalMilliseconds;

			// 2. 模型前向推理
			sw.Restart();
			using (var results = _session.Run(inputs))
			{
				sw.Stop();
				double inferTimeMs = sw.Elapsed.TotalMilliseconds;

				// 3. 分割后处理
				sw.Restart();
				var finalResults = PostprocessEnd2EndSegmentation(results, origImgs, ratios, padWs, padHs, confThres);
				sw.Stop();
				double postTimeMs = sw.Elapsed.TotalMilliseconds;

				// 均摊时间统计
				foreach (var res in finalResults)
				{
					res.PreprocessTimeMs = preTimeMs / batchSize;
					res.InferenceTimeMs = inferTimeMs / batchSize;
					res.PostprocessTimeMs = postTimeMs / batchSize;
				}

				return finalResults;
			}
		}

		/// <summary>
		/// 并发图像预处理 (LetterBox + 通道转置 + 归一化)
		/// 100% 完整无省略的极致指针级加速实现
		/// </summary>
		private DenseTensor<float> PreprocessBatchOptimized(List<Mat> origImgs, float[] ratios, float[] padWs, float[] padHs)
		{
			int batchSize = origImgs.Count;
			int planeSize = _inputH * _inputW;
			int singleImgTensorSize = 3 * planeSize;
			int totalTensorSize = batchSize * singleImgTensorSize;

			float[] tensorData = new float[totalTensorSize];
			float padValue = 114f / 255.0f; // YOLO 默认的灰边填充值

			// 预先用灰边颜色填充满整个一维数组
			for (int i = 0; i < tensorData.Length; i++)
			{
				tensorData[i] = padValue;
			}

			// 多线程并行处理每张图片
			Parallel.For(0, batchSize, b =>
			{
				Mat origImg = origImgs[b];
				int w = origImg.Width;
				int h = origImg.Height;

				// 计算缩放比例，保持长宽比
				float ratio = Math.Min((float)_inputH / h, (float)_inputW / w);
				ratios[b] = ratio;

				int newW = (int)Math.Round(w * ratio);
				int newH = (int)Math.Round(h * ratio);

				// 计算居中填充的留白尺寸
				float padW = (_inputW - newW) / 2f;
				float padH = (_inputH - newH) / 2f;
				padWs[b] = padW;
				padHs[b] = padH;

				int padWInt = (int)Math.Round(padW - 0.1);
				int padHInt = (int)Math.Round(padH - 0.1);

				using (Mat resized = new Mat())
				{
					// 仅当尺寸需要改变时才进行 Resize
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

							// BGR 到 RGB 通道转换，同时进行 CHW 内存排列及 /255 归一化
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

		/// <summary>
		/// End-to-End 分割后处理流水线
		/// </summary>
		private List<YoloResult> PostprocessEnd2EndSegmentation(IDisposableReadOnlyCollection<DisposableNamedOnnxValue> outputs,
			List<Mat> origImgs, float[] ratios, float[] padWs, float[] padHs, float confThres)
		{
			int batchSize = origImgs.Count;
			List<YoloResult> results = new List<YoloResult>(batchSize);

			// 1. 动态区分模型双输出：预测结果张量 (preds) 和 原型掩码张量 (protos)
			var outList = outputs.ToList();
			Tensor<float> predsTensor = null;
			Tensor<float> protosTensor = null;

			foreach (var opt in outList)
			{
				var t = opt.AsTensor<float>();
				if (t.Dimensions.Length == 4) protosTensor = t;      // 维度形如 [Batch, 32, H/4, W/4] 的必定是 protos
				else if (t.Dimensions.Length == 3) predsTensor = t;  // 维度形如 [Batch, MaxDet, Features] 的必定是 preds
			}

			if (predsTensor == null || protosTensor == null)
				throw new InvalidOperationException("后处理失败：模型输出格式不符合端到端分割模型 (需包含 preds 和 protos 双路输出)。如果这是纯检测模型，请使用检测专用的处理类。");

			int maxDet = predsTensor.Dimensions[1];   // NMS 后的最大保留框数 (如 300)
			int features = predsTensor.Dimensions[2]; // 特征维数 (比如 38，包含 4坐标 + 1置信度 + 1类别ID + 32Mask权重)

			int numMasks = protosTensor.Dimensions[1]; // 掩码通道数 (通常是 32)
			int mh = protosTensor.Dimensions[2];       // 掩码特征图高
			int mw = protosTensor.Dimensions[3];       // 掩码特征图宽

			float[] predsArray = predsTensor.ToArray();
			float[] protosArray = protosTensor.ToArray();

			unsafe
			{
				fixed (float* pPredsBase = predsArray)
				fixed (float* pProtosBase = protosArray)
				{
					for (int b = 0; b < batchSize; b++)
					{
						float* pPreds = pPredsBase + b * maxDet * features;
						float* pProtos = pProtosBase + b * numMasks * mh * mw;

						int origW = origImgs[b].Width;
						int origH = origImgs[b].Height;
						float ratio = ratios[b];
						float padW = padWs[b];
						float padH = padHs[b];

						// 第一步：基于置信度阈值过滤有效目标
						List<Rect> validBoxesPadded = new List<Rect>();
						List<float> confidences = new List<float>();
						List<int> classIds = new List<int>();
						List<float[]> maskWeightsList = new List<float[]>();

						for (int i = 0; i < maxDet; i++)
						{
							float* row = pPreds + i * features;
							float conf = row[4];

							if (conf > confThres)
							{
								float cx = row[0];
								float cy = row[1];
								float w = row[2];
								float h = row[3];
								int clsId = (int)row[5];

								// 换算得到在 640x640 输入张量尺寸下的边界框 (方便裁剪 Mask)
								int bx = Math.Max(0, (int)(cx - w / 2f));
								int by = Math.Max(0, (int)(cy - h / 2f));
								int bw = Math.Min(_inputW - bx, (int)w);
								int bh = Math.Min(_inputH - by, (int)h);
								validBoxesPadded.Add(new Rect(bx, by, bw, bh));

								confidences.Add(conf);
								classIds.Add(clsId);

								// 提取对应目标的 Mask 权重向量 (长度通常为 32)
								float[] weights = new float[numMasks];
								for (int m = 0; m < numMasks; m++)
								{
									weights[m] = row[features - numMasks + m];
								}
								maskWeightsList.Add(weights);
							}
						}

						int validCount = confidences.Count;
						var result = new YoloResult
						{
							Boxes = new Rect[validCount],
							BoxesN = new Rect2f[validCount],
							Masks = new Point2f[validCount][],
							MasksN = new Point2f[validCount][],
							Scores = confidences.ToArray(),
							ClassIds = classIds.ToArray(),
							OrigImg = origImgs[b] // 挂载原始图片方便外部使用
						};

						if (validCount > 0)
						{
							// 构造一维数组供 OpenCV 的 Mat 使用
							float[] flatMaskWeights = new float[validCount * numMasks];
							for (int i = 0; i < validCount; i++)
							{
								Array.Copy(maskWeightsList[i], 0, flatMaskWeights, i * numMasks, numMasks);
							}

							// 锁定权重数组内存，防止 GC 移动
							fixed (float* pWeights = flatMaskWeights)
							{
								// 第二步核心加速点：解决 CS0619 错误，使用官方推荐的 Mat.FromPixelData 替代过时的构造函数
								using (Mat weightsMat = Mat.FromPixelData(validCount, numMasks, MatType.CV_32FC1, (IntPtr)pWeights))
								using (Mat protosMat = Mat.FromPixelData(numMasks, mh * mw, MatType.CV_32FC1, (IntPtr)pProtos))
								using (Mat masksMat = weightsMat * protosMat) // OpenCV C++ 级别的高效 BLAS 乘法
								{
									// Sigmoid 计算 (并行化处理，将值压缩到 0~1 之间)
									float* pMaskData = (float*)masksMat.DataPointer;
									int totalElements = validCount * mh * mw;
									Parallel.For(0, totalElements, idx => {
										pMaskData[idx] = 1.0f / (1.0f + (float)Math.Exp(-pMaskData[idx]));
									});

									// 第三步：逐个处理每个目标的掩码轮廓
									for (int i = 0; i < validCount; i++)
									{
										Rect bboxPadded = validBoxesPadded[i];

										// 1. 将边界框还原到原始图像尺度
										float x1 = (bboxPadded.X - padW) / ratio;
										float y1 = (bboxPadded.Y - padH) / ratio;
										float x2 = (bboxPadded.Right - padW) / ratio;
										float y2 = (bboxPadded.Bottom - padH) / ratio;

										x1 = Math.Max(0, Math.Min(x1, origW));
										y1 = Math.Max(0, Math.Min(y1, origH));
										x2 = Math.Max(0, Math.Min(x2, origW));
										y2 = Math.Max(0, Math.Min(y2, origH));

										result.Boxes[i] = new Rect((int)x1, (int)y1, (int)(x2 - x1), (int)(y2 - y1));
										result.BoxesN[i] = new Rect2f(x1 / origW, y1 / origH, (x2 - x1) / origW, (y2 - y1) / origH);

										// 2. 掩码上采样与裁剪还原
										using (Mat maskReshaped = masksMat.Row(i).Reshape(1, mh))
										using (Mat maskResized = new Mat())
										{
											// 上采样到网络输入尺寸 (如 640x640)
											Cv2.Resize(maskReshaped, maskResized, new Size(_inputW, _inputH), 0, 0, InterpolationFlags.Linear);

											// 创建一个全黑画布，对应 python 的 crop_mask：只保留边界框区域内的掩码，抑制干扰区域
											using (Mat zeroMask = Mat.Zeros(_inputH, _inputW, MatType.CV_32FC1))
											{
												if (bboxPadded.Width > 0 && bboxPadded.Height > 0)
												{
													using (Mat roiSrc = new Mat(maskResized, bboxPadded))
													using (Mat roiDst = new Mat(zeroMask, bboxPadded))
													{
														roiSrc.CopyTo(roiDst);
													}
												}

												// 裁剪掉 LetterBox 造成的 Padding，并还原到原图大小
												Rect imgRoi = new Rect((int)Math.Round(padW), (int)Math.Round(padH),
																	   _inputW - (int)Math.Round(2 * padW), _inputH - (int)Math.Round(2 * padH));

												using (Mat croppedToAspect = new Mat(zeroMask, imgRoi))
												using (Mat finalMask = new Mat())
												using (Mat binMask = new Mat())
												{
													// 放大到原始分辨率
													Cv2.Resize(croppedToAspect, finalMask, new Size(origW, origH), 0, 0, InterpolationFlags.Linear);

													// 掩码二值化 (阈值 > 0.5)
													Cv2.Threshold(finalMask, binMask, 0.5, 255, ThresholdTypes.Binary);
													binMask.ConvertTo(binMask, MatType.CV_8UC1);

													// 提取多边形轮廓
													Cv2.FindContours(binMask, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

													if (contours.Length > 0)
													{
														// 采用与 Python 中 strategy="largest" 一致的逻辑，选取最大面积的轮廓
														var largestContour = contours.OrderByDescending(c => Cv2.ContourArea(c)).First();
														Point2f[] pts = new Point2f[largestContour.Length];
														Point2f[] ptsN = new Point2f[largestContour.Length];

														for (int ptIdx = 0; ptIdx < largestContour.Length; ptIdx++)
														{
															float px = largestContour[ptIdx].X;
															float py = largestContour[ptIdx].Y;
															pts[ptIdx] = new Point2f(px, py);
															ptsN[ptIdx] = new Point2f(px / origW, py / origH); // 归一化比例坐标
														}
														result.Masks[i] = pts;
														result.MasksN[i] = ptsN;
													}
													else
													{
														result.Masks[i] = new Point2f[0];
														result.MasksN[i] = new Point2f[0];
													}
												}
											}
										}
									}
								}
							}
						}
						results.Add(result);
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
	// 3. 入口测试
	// ==========================================
	//class Program
	//{
	//	static void Main(string[] args)
	//	{
	//		//Console.WriteLine("=== 🚀 企业级 YOLO End2End 分割推理服务启动 ===");

	//		// 请在此处替换为你的实际路径
	//		string modelPath = "F:\\best.onnx";
	//		string imagePath = "F:\\Pic_2026_04_28_132017_3_ok_hook_4.png";

	//		try
	//		{
	//			// 初始化模型，expectedBatchSize 用于预热分配固定显存
	//			using (var yolo = new YoloOnnxSegmentation(modelPath, expectedBatchSize: 1))
	//			using (var img = Cv2.ImRead(imagePath))
	//			{
	//				if (img.Empty()) throw new Exception($"图片读取失败，请检查路径: {imagePath}");

	//				// 执行单图推理
	//				var result = yolo.Predict(img, confThres: 0.3f);

	//				Console.WriteLine("\n====== ⚡ 性能与结果报告 ======");
	//				Console.WriteLine($"[耗时] 预处理:   {result.PreprocessTimeMs:F2} ms");
	//				Console.WriteLine($"[耗时] 模型推理: {result.InferenceTimeMs:F2} ms");
	//				Console.WriteLine($"[耗时] 后处理:   {result.PostprocessTimeMs:F2} ms");
	//				Console.WriteLine($"[统计] 成功检测到目标: {result.Boxes?.Length ?? 0} 个");
	//				Console.WriteLine(new string('-', 50));

	//				if (result.Boxes != null)
	//				{
	//					for (int i = 0; i < result.Boxes.Length; i++)
	//					{
	//						Console.WriteLine($"目标 {i + 1}: 类别ID = {result.ClassIds[i]}, 置信度 = {result.Scores[i]:F3}");
	//						Console.WriteLine($"  - Box(像素): X:{result.Boxes[i].X}, Y:{result.Boxes[i].Y}, W:{result.Boxes[i].Width}, H:{result.Boxes[i].Height}");

	//						var mask = result.Masks[i];
	//						string examplePoint = mask.Length > 0 ? $"({mask[0].X:F1}, {mask[0].Y:F1})" : "无";
	//						Console.WriteLine($"  - Masks 轮廓顶点数: {mask.Length}, 示例坐标点: {examplePoint}");
	//					}
	//				}
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