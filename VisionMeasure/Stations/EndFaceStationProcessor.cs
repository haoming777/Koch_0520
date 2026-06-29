using Config;
using Models;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using VisionMeasure.Utils;
using CommonLib;
using YoloInference;
using AI;
using CvRect = OpenCvSharp.Rect;
using Rect = System.Drawing.Rectangle;
using static CommonLib.Class_Config;

namespace Stations
{
/// <summary>
/// 端面工位处理器, 批次模式(收集P张图后批量处理).
/// 入队: EnqueueImage -> ConcurrentQueue -> BlockingCollection.
/// 防呆: 超时未满P张则强制处理现有图.
/// 处理: ProcessLoop后台线程 -> ProcessBatch(裁图 -> 并行YOLO -> 汇总).
/// </summary>
	public class EndFaceStationProcessor : IDisposable
	{
		private readonly AiModelManager _models;
		private readonly string _savePath;
		private int _pCount;
		private SkuData _sku;
		private readonly HighSpeedImageSaver _imageSaver;
		private readonly PerformanceMonitor _perfMonitor;

		private readonly ConcurrentQueue<ImageContext> _upperQueue = new ConcurrentQueue<ImageContext>();
		private readonly ConcurrentQueue<ImageContext> _lowerQueue = new ConcurrentQueue<ImageContext>();
		private int _upperCount = 0;
		private int _lowerCount = 0;
		private readonly object _countLock = new object();
			private DateTime _lastEnqueueTime = DateTime.MinValue;
			private const int QueueTimeoutMs = 2000;  // 降为2秒(新批次ProductId检测是主力, 超时兜底)
			private long _firstBatchProductId = -1;  // 本批次第一个ProductId, 用于检测新批次到达

		private readonly BlockingCollection<(List<ImageContext> upper, List<ImageContext> lower)> _batchQueue;

		private readonly List<Mat> _currentDisplayImages = new List<Mat>();
		private readonly List<Mat> _upperDisplayImages = new List<Mat>();
		private readonly List<Mat> _lowerDisplayImages = new List<Mat>();
		// Bitmap缓存：DrawDefectOnImage返回Bitmap，直接缓存避免Mat→Bitmap往返转换
		private readonly List<Bitmap> _currentDisplayBitmaps = new List<Bitmap>();
		private readonly List<Bitmap> _upperDisplayBitmaps = new List<Bitmap>();
		private readonly List<Bitmap> _lowerDisplayBitmaps = new List<Bitmap>();
		private int _currentDisplayIndex = 0;
		private int _upperDisplayIndex = 0;  // 上端面独立索引
		private int _lowerDisplayIndex = 0;  // 下端面独立索引
		private readonly object _resultLock = new object();

		private long _totalCount = 0;
		private long _okCount = 0;
		private long _ngCount = 0;
		private long _imgUpperCount = 0;
		private long _imgLowerCount = 0;
		private Config.ModelParams _displayParams;

		private Thread _processThread;
		private CancellationTokenSource _cts;
		private bool _disposed;

		public event Action<ProductResult> OnResultReady;
		public event Action<List<string>, List<string>, List<string>, int> OnStatusUpdate;

		public float ConfThreshold { get; set; } = 0.5f;
		public float IouThreshold { get; set; } = 0.2f;
		public bool ReverseBoxOrder = false;
		public bool SkipCrop = false;
		public bool EnableUpperDefectCheck = true;
		public int ExposureMs { get; set; } = 20;

		public long TotalCount => _totalCount;
		public long OkCount => _okCount;
		public long NgCount => _ngCount;
		public long ImgUpperCount => _imgUpperCount;
		public long ImgLowerCount => _imgLowerCount;
		public int CurrentIndex => _currentDisplayIndex;
		public int ImageCount => _currentDisplayImages.Count;

		public EndFaceStationProcessor(AiModelManager models, string savePath, int pCount,
			HighSpeedImageSaver imageSaver, PerformanceMonitor perfMonitor)
		{
			_models = models;
			_savePath = savePath;
			_pCount = pCount;
			_imageSaver = imageSaver;
			_perfMonitor = perfMonitor;
				EnableUpperDefectCheck = DetectionParameters.Instance.EndFace.EnableUpperDefectCheck;
			_displayParams = Config.ModelParams.Load("endface_upper");
			_batchQueue = new BlockingCollection<(List<ImageContext>, List<ImageContext>)>(50);
			_cts = new CancellationTokenSource();
		}

		public void UpdatePCount(int pCount) => _pCount = pCount;
		public void UpdateSku(SkuData sku) { _sku = sku; }

		/// <summary>重新加载ModelParams，无需重启软件</summary>
		public void ReloadModelParams()
		{
			_displayParams = Config.ModelParams.Load("endface_upper");
			var euParams = Config.ModelParams.Load("endface_upper");
			if (euParams.EndFaceUpperConf > 0) ConfThreshold = euParams.EndFaceUpperConf;
			if (euParams.EndFaceUpperIou > 0) IouThreshold = euParams.EndFaceUpperIou;
			EnableUpperDefectCheck = DetectionParameters.Instance.EndFace.EnableUpperDefectCheck;
			Logger.Info($"[EndFace] ModelParams已重新加载 Conf={ConfThreshold:F2} Iou={IouThreshold:F2}");
		}

		/// <summary>测试用：直接传入上/下端面图片对进行推理（跳过裁图）</summary>
		public void TestProcessPair(Bitmap upperBmp, Bitmap lowerBmp)
		{
			long pid = DateTime.Now.Ticks;
			var uCtx = new Models.ImageContext { ProductId = pid, OriginalBitmap = upperBmp, ReceiveTime = DateTime.Now };
			var lCtx = new Models.ImageContext { ProductId = pid, OriginalBitmap = lowerBmp, ReceiveTime = DateTime.Now };
			Task.Run(() => {
				bool savedSkip = SkipCrop;
				SkipCrop = true;
				try { ProcessBatch(new List<Models.ImageContext> { uCtx }, new List<Models.ImageContext> { lCtx }); }
				finally { SkipCrop = savedSkip; }
			});
		}
		/// <summary>相机3(上端面)图像回调 — 入队到_upperQueue, 计数到P→触发批次</summary>
		public void OnCam5(Bitmap bitmap, long productId) { Interlocked.Increment(ref _imgUpperCount); EnqueueImage(_upperQueue, ref _upperCount, bitmap, productId, "Upper"); }
		/// <summary>相机4(下端面)图像回调 — 入队到_lowerQueue, 计数到P→触发批次</summary>
		public void OnCam6(Bitmap bitmap, long productId) { Interlocked.Increment(ref _imgLowerCount); EnqueueImage(_lowerQueue, ref _lowerCount, bitmap, productId, "Lower"); }

	/// <summary>入队图像: ConcurrentQueue原子入队→计数累加→上下各P张触发批次→DequeueBatch取P张→BlockingCollection.Add入处理队列
	/// 新批次检测: 当ProductId跳变超过P*3(即新一批产品到达), 立即强制结束当前不完整批次→清理残留→开始新批次</summary>
		private void EnqueueImage(ConcurrentQueue<ImageContext> queue, ref int count, Bitmap bitmap, long productId, string name)
		{
			var ctx = new ImageContext { ProductId = productId, OriginalBitmap = bitmap, ReceiveTime = DateTime.Now };
			lock (_countLock)
			{
				// 新批次检测: 第一次入队记录firstProductId, 后续ProductId跳变>P*3视为新批次
				if (_firstBatchProductId < 0) _firstBatchProductId = productId;
				if (productId - _firstBatchProductId > _pCount * 3)
				{
					Logger.Info($"[EndFace] 新批次到达 ProductId={productId}(首={_firstBatchProductId}), 强制结束当前批次 Upper={_upperCount} Lower={_lowerCount}");
					// 取出当前不完整批次并送入处理队列
					int partialP = Math.Min(_upperCount, _lowerCount);
					if (partialP > 0)
					{
						var upperList = DequeueBatchN(_upperQueue, ref _upperCount, partialP);
						var lowerList = DequeueBatchN(_lowerQueue, ref _lowerCount, partialP);
						if (upperList.Count > 0 && lowerList.Count > 0)
							_batchQueue.Add((upperList, lowerList));
					}
					// 清空残留(新旧批次混杂的图像全部丢弃, 下一张开始新批次)
					while (_upperQueue.TryDequeue(out var _)) _upperCount--;
					while (_lowerQueue.TryDequeue(out var _)) _lowerCount--;
					_firstBatchProductId = productId;  // 重置为新批次首ID
				}

				queue.Enqueue(ctx);
					_lastEnqueueTime = DateTime.Now;
				count++;
				Logger.Debug($"[EndFace] {name}入队 ProductId={productId}, Upper={_upperCount}/{_pCount}, Lower={_lowerCount}/{_pCount}");
				if (_upperCount >= _pCount && _lowerCount >= _pCount)
				{
					Logger.Info($"[EndFace] 批次触发: P={_pCount}, Upper={_upperCount}, Lower={_lowerCount}");
					var upperList = DequeueBatch(_upperQueue, ref _upperCount);
					var lowerList = DequeueBatch(_lowerQueue, ref _lowerCount);
					_batchQueue.Add((upperList, lowerList));
					_firstBatchProductId = -1;  // 正常批次完成后重置
				}
			}
		}

		/// <summary>出队P张图像: 从ConcurrentQueue TryDequeue到满P张或队列空</summary>
		private List<ImageContext> DequeueBatch(ConcurrentQueue<ImageContext> queue, ref int count)
		{
			var list = new List<ImageContext>();
			while (list.Count < _pCount && queue.TryDequeue(out var ctx))
			{
				list.Add(ctx);
				count--;
			}
			return list;
		}

		/// <summary>出队N张图像: TryDequeue指定数量(防呆超时时使用)</summary>
			private List<ImageContext> DequeueBatchN(ConcurrentQueue<ImageContext> queue, ref int count, int n)
		{
			var list = new List<ImageContext>();
			while (list.Count < n && queue.TryDequeue(out var ctx))
			{
				list.Add(ctx);
				count--;
			}
			return list;
		}

		// 从模型best.json加载阈值
		/// <summary>从模型best.json加载端面缺陷检测的Conf/Iou阈值</summary>
		public void InitThresholdsFromModel() {
			// 上端面模型阈值优先
			if (_models.EndFaceUpperModel != null) { ConfThreshold = _models.EndFaceUpperModel.DefaultConfThres; IouThreshold = _models.EndFaceUpperModel.DefaultIouThres; }
			Logger.Info($"[EndFace] 阈值从模型: Conf={ConfThreshold:F2} Iou={IouThreshold:F2}");
		}

		/// <summary>启动后台处理线程: new Thread(ProcessLoop){AboveNormal,IsBackground}→Start</summary>
		public void Start()
		{
			_processThread = new Thread(ProcessLoop) { Name = "EndFaceStationProcessor", IsBackground = true, Priority = ThreadPriority.AboveNormal };
			_processThread.Start();
			Logger.Info("端面工位处理器已启动");
		}

		public void Stop() { _cts.Cancel(); _processThread?.Join(3000); }

	/// <summary>后台处理循环(AboveNormal优先级): 消费BlockingCollection批次. 超时未满P张则强制处理.</summary>
		private void ProcessLoop()
		{
			while (!_cts.Token.IsCancellationRequested)
			{
				// 防呆超时检查: 超过QueueTimeoutMs未收到足够图片则清空残留队列
				lock (_countLock)
				{
					if ((_upperCount > 0 || _lowerCount > 0) && _lastEnqueueTime != DateTime.MinValue
						&& (DateTime.Now - _lastEnqueueTime).TotalMilliseconds > QueueTimeoutMs)
						{
							int actualP = Math.Min(_upperCount, _lowerCount);
							if (actualP > 0)
							{
								Logger.Warning($"[EndFace] 队列超时({QueueTimeoutMs}ms), 强制处理现有图 Upper={_upperCount} Lower={_lowerCount} -> P={actualP}");
								var upperList = DequeueBatchN(_upperQueue, ref _upperCount, actualP);
								var lowerList = DequeueBatchN(_lowerQueue, ref _lowerCount, actualP);
								while (_upperQueue.TryDequeue(out var _)) _upperCount--;
								while (_lowerQueue.TryDequeue(out var _)) _lowerCount--;
								_batchQueue.Add((upperList, lowerList));
							}
							else
							{
								Logger.Warning($"[EndFace] 队列超时且无配对图, 清空残留 Upper={_upperCount} Lower={_lowerCount}");
								while (_upperQueue.TryDequeue(out var _)) _upperCount--;
								while (_lowerQueue.TryDequeue(out var _)) _lowerCount--;
							}
							_firstBatchProductId = -1;  // 超时清空后重置, 下一批重新开始
						}
				}
				{
					try
					{
						if (_batchQueue.TryTake(out var batch, 100, _cts.Token))
						{
							ProcessBatch(batch.upper, batch.lower);
						}
					}
					catch (OperationCanceledException) { break; }
					catch (Exception ex) { Logger.Error($"端面工位处理异常: {ex.Message}"); }
				}
			}
		}

	/// <summary>批处理核心: 裁图->并行YOLO(上下端面各自推理)->解析缺陷->汇总状态->绘制->保存. 上端面裁右边, 下端面裁左边.</summary>
		private void ProcessBatch(List<ImageContext> upperImages, List<ImageContext> lowerImages)
		{
			int actualP = Math.Min(upperImages.Count, lowerImages.Count);
			if (actualP == 0) { Logger.Error("端面图片为空"); return; }
			if (actualP < _pCount)
				Logger.Warning($"[EndFace] 部分批次: Upper={upperImages.Count}, Lower={lowerImages.Count}, 预期P={_pCount}, 实际={actualP}");
			int p = Math.Max(actualP, _pCount);  // 显示总是用_pCount, 缺失位填"缺少"

			var sw = System.Diagnostics.Stopwatch.StartNew();
			long firstProductId = upperImages.FirstOrDefault()?.ProductId ?? 0;
			Logger.Debug($"[EndFace] ⏱ ProcessBatch开始 Upper={upperImages.Count}张 Lower={lowerImages.Count}张 P={p}");

			try
			{
				double cropTime = 0, inferenceTime = 0;

				List<Mat> upperMats = null, lowerMats = null;
				using (var cropScope = new StopwatchScope(t => cropTime = t))
				{
					// 上端面: 左裁边(裁掉左边像素) + 右裁边(裁掉右边像素)
					int upperCropLeftPx = _sku?.UpperEndFace_LeftPx ?? 0;
					int upperCropRightPx = _sku?.UpperEndFace_RightPx ?? 0;
					// 下端面: 左裁边 + 右裁边
					int lowerCropLeftPx = _sku?.LowerEndFace_LeftPx ?? 0;
					int lowerCropRightPx = _sku?.LowerEndFace_RightPx ?? 0;
					Logger.Debug($"[EndFace] 裁图参数: 上端面 左{upperCropLeftPx}px 右{upperCropRightPx}px | 下端面 左{lowerCropLeftPx}px 右{lowerCropRightPx}px");
					upperMats = CropImagesBatch(upperImages, upperCropLeftPx, upperCropRightPx);
					lowerMats = CropImagesBatch(lowerImages, lowerCropLeftPx, lowerCropRightPx);
					if (upperMats.Count > 0) Logger.Debug($"[EndFace] 裁图后 上端面尺寸={upperMats[0].Width}x{upperMats[0].Height} (共{upperMats.Count}张)");
					if (lowerMats.Count > 0) Logger.Debug($"[EndFace] 裁图后 下端面尺寸={lowerMats[0].Width}x{lowerMats[0].Height} (共{lowerMats.Count}张)");
					Logger.Debug($"[EndFace] ⏱ 裁图耗时={cropTime:F1}ms");
				}

				List<YoloInference.YoloResult> upperResults = null, lowerResults = null;
				using (var inferScope = new StopwatchScope(t => inferenceTime = t))
				{
					Logger.Debug($"[EndFace] ⏱ 推理开始: 上端面 batch={upperMats.Count} 下端面 batch={lowerMats.Count} 并行");
					var upperTask = Task.Run(() => EnableUpperDefectCheck ? RunInference(upperMats, _models.EndFaceUpperModel) : new List<YoloInference.YoloResult>());
					var lowerTask = Task.Run(() => RunInference(lowerMats, _models.EndFaceLowerModel));
					Task.WaitAll(upperTask, lowerTask);
					upperResults = upperTask.Result;
					lowerResults = lowerTask.Result;
					Logger.Debug($"[EndFace] ⏱ 推理完成 耗时={inferenceTime:F1}ms 上检出={upperResults?.Sum(r => r?.BoxesN?.Length ?? 0) ?? 0}框 下检出={lowerResults?.Sum(r => r?.BoxesN?.Length ?? 0) ?? 0}框");
				}

				var upperDefects = ParseResults(upperResults);
				var lowerDefects = ParseResults(lowerResults);

				var upperStatus = new List<string>();
				var lowerStatus = new List<string>();
				var mergedStatus = new List<string>();

				for (int i = 0; i < actualP; i++)
				{
					string uStatus = upperDefects.ContainsKey(i) ? string.Join(",", upperDefects[i].Select(d => d.DefectType)) : "OK";
					string lStatus = lowerDefects.ContainsKey(i) ? string.Join(",", lowerDefects[i].Select(d => d.DefectType)) : "OK";
					upperStatus.Add(uStatus);
					lowerStatus.Add(lStatus);
					mergedStatus.Add((uStatus == "OK" && lStatus == "OK") ? "OK" : (uStatus != "OK" ? uStatus : lStatus));
				}
				// 不完整批次: 缺失位置标记"缺少"→NG, 在汇总中显式标识
				int missingCount = _pCount - actualP;
				for (int i = 0; i < missingCount; i++)
				{
					upperStatus.Add("缺少");
					lowerStatus.Add("缺少");
					mergedStatus.Add("缺少");
				}
				if (missingCount > 0)
					Logger.Warning($"[EndFace] 缺失{missingCount}个工件位 → 标记为缺少");

				bool isOk = mergedStatus.All(s => s == "OK");
				var result = new ProductResult
				{
					ProductId = firstProductId,
					CreateTime = DateTime.Now,
					EndFaceResult = isOk,
					EndFaceDefects = mergedStatus.Where(s => s != "OK").Distinct().ToList()
				};

				int boxOk = mergedStatus.Count(s => s == "OK");
				Logger.Info("[EndFace]    " + string.Join(" ", Enumerable.Range(1, _pCount).Select(i => i.ToString().PadLeft(3))));
				// alignment done
				Logger.Info("[EndFace]上 " + string.Join(" ", upperStatus.Select(s => (s == "OK" ? "  O" : (s == "缺少" ? "  M" : "  X")))));
				Logger.Info("[EndFace]下 " + string.Join(" ", lowerStatus.Select(s => (s == "OK" ? "  O" : (s == "缺少" ? "  M" : "  X")))));
				Logger.Info("[EndFace]总 " + string.Join(" ", mergedStatus.Select(s => (s == "OK" ? "  O" : (s == "缺少" ? "  M" : "  X")))));
				Interlocked.Add(ref _totalCount, mergedStatus.Count);
				Interlocked.Add(ref _okCount, boxOk);
				Interlocked.Add(ref _ngCount, mergedStatus.Count - boxOk);

				double drawTime = 0;
				using (var drawScope = new StopwatchScope(t => drawTime = t))
				{
					DrawResultsBatch(upperImages, upperMats, upperDefects, upperStatus);
					DrawResultsBatch(lowerImages, lowerMats, lowerDefects, lowerStatus);
					UpdateDisplayImages(upperMats, lowerMats, upperDefects, lowerDefects, upperStatus, lowerStatus);
				}

				// 显示逻辑: 上下端面独立——各自有NG时显示第一张NG，全OK时显示最后一张
				lock (_resultLock)
				{
					int upperIdx = FindFirstNgInList(upperStatus, _upperDisplayImages.Count);
					int lowerIdx = FindFirstNgInList(lowerStatus, _lowerDisplayImages.Count);
					// 直接从Bitmap缓存取，省掉Mat→Bitmap转换
					if (upperIdx < _upperDisplayBitmaps.Count && _upperDisplayBitmaps[upperIdx] != null)
						result.EndFaceRenderImage = (Bitmap)_upperDisplayBitmaps[upperIdx].Clone();
					if (lowerIdx < _lowerDisplayBitmaps.Count && _lowerDisplayBitmaps[lowerIdx] != null)
						result.EndFaceLowerRenderImage = (Bitmap)_lowerDisplayBitmaps[lowerIdx].Clone();
				}

				double saveTime = 0;
				using (var saveScope = new StopwatchScope(t => saveTime = t))
				{
					SaveImagesBatch(upperImages, lowerImages, upperMats, lowerMats, upperStatus, lowerStatus, mergedStatus, firstProductId, isOk);
				}

				double totalTime = sw.Elapsed.TotalMilliseconds;
				_perfMonitor?.Record(new PerformanceMonitor.PerformanceRecord
				{
					Timestamp = DateTime.Now,
					Station = "EndFace",
					ProductId = firstProductId,
					CropTimeMs = cropTime,
					InferenceTimeMs = inferenceTime,
					PostprocessTimeMs = 0,
					DrawTimeMs = drawTime,
					SaveTimeMs = saveTime,
					PlcTimeMs = 0,
					TotalTimeMs = totalTime,
					Result = isOk
				});

				OnResultReady?.Invoke(result);
				OnStatusUpdate?.Invoke(upperStatus, lowerStatus, mergedStatus, actualP);
				var upStats = new Dictionary<string, int>();
				foreach (var s in upperStatus)
				{
					if (s != "OK")
						foreach (var d in s.Split(','))
						{
							var k = d.Trim();
							if (!string.IsNullOrEmpty(k))
							{
								if (upStats.ContainsKey(k)) upStats[k]++;
								else upStats[k] = 1;
							}
						}
				}
				var loStats = new Dictionary<string, int>();
				foreach (var s in lowerStatus)
				{
					if (s != "OK")
						foreach (var d in s.Split(','))
						{
							var k = d.Trim();
							if (!string.IsNullOrEmpty(k))
							{
								if (loStats.ContainsKey(k)) loStats[k]++;
								else loStats[k] = 1;
							}
						}
				}
				string defStr = " | 上端面:" + (upStats.Count > 0 ? string.Join(" ", upStats.Select(kv => kv.Key + kv.Value)) : "0");
				defStr += " 下端面:" + (loStats.Count > 0 ? string.Join(" ", loStats.Select(kv => kv.Key + kv.Value)) : "0");
				Logger.Info($"[EndFace] 完成 P={actualP} OK={boxOk} NG={mergedStatus.Count - boxOk}{defStr} | 耗时={totalTime:F0}ms");
			}
			catch (Exception ex)
			{
				Logger.Error($"端面批处理异常: {ex.Message}");
			}
			finally
			{
				foreach (var img in upperImages) img.Dispose();
				foreach (var img in lowerImages) img.Dispose();
			}
		}

		/// <summary>批量裁图: 遍历ImageContext→ToMat→同时支持左右双侧裁图(leftPx裁左边, rightPx裁右边)→返回Mat列表</summary>
		private List<Mat> CropImagesBatch(List<ImageContext> images, int leftPx, int rightPx)
		{
			var mats = new List<Mat>();
			foreach (var img in images)
			{
				var mat = BitmapConverter.ToMat(img.OriginalBitmap);
				// 水平裁图: 同时支持左右双侧裁图
				if ((leftPx > 0 || rightPx > 0) && !SkipCrop)
				{
					int? l = leftPx > 0 ? (int?)leftPx : null;
					int? r = rightPx > 0 ? (int?)rightPx : null;
					Mat croppedH = ImageHelper.CropImageHorizontallyCv2(mat, l, r);
					mat.Dispose();
					mat = croppedH;
				}
				mats.Add(mat);
			}
			return mats;
		}

	/// <summary>批量YOLO推理: model.PredictBatch(所有Mat图片一次性送入GPU), 模型为null返回空列表</summary>
		private List<YoloInference.YoloResult> RunInference(List<Mat> images, YoloOnnx model)
		{
			if (model == null) return new List<YoloInference.YoloResult>();
			return model.PredictBatch(images, ConfThreshold, IouThreshold);
		}

	/// <summary>解析YOLO结果: 遍历每张图的Boxes→BoxesN(归一化坐标)→GetDefectType(classId映射缺陷名)→构建BoxDefect字典</summary>
		private Dictionary<int, List<BoxDefect>> ParseResults(List<YoloInference.YoloResult> results)
		{
			var defects = new Dictionary<int, List<BoxDefect>>();
			for (int i = 0; i < results.Count; i++)
			{
				var result = results[i];
				for (int j = 0; j < result.Boxes.Length; j++)
				{
					string defectType = GetDefectType(result.ClassIds[j]);
					var box = result.BoxesN[j];
					var defect = new BoxDefect(i, defectType,
						new float[] { box.X, box.Y, box.X + box.Width, box.Y + box.Height },
						result.Scores[j]);
					if (!defects.ContainsKey(i)) defects[i] = new List<BoxDefect>();
					defects[i].Add(defect);
				}
			}
			return defects;
		}

	/// <summary>批量绘制: 遍历每张图像→DrawDefectOnImage(缺陷框+状态标签+序号)→存入ImageContext.RenderBitmap</summary>
		private void DrawResultsBatch(List<ImageContext> images, List<Mat> mats, Dictionary<int, List<BoxDefect>> defects, List<string> status)
		{
			for (int i = 0; i < images.Count; i++)
			{
				var drawn = DrawDefectOnImage(mats[i], defects.ContainsKey(i) ? defects[i] : new List<BoxDefect>(), status[i], i, images.Count);
				images[i].RenderBitmap = drawn;
			}
		}

	/// <summary>绘制单张端面缺陷图: 填充半透明框+实线边框(破损红/搭舌橙/边缘紫)+缺陷类型标签+OK/NG状态(右上角)+序号(右下角)</summary>
		private Bitmap DrawDefectOnImage(Mat image, List<BoxDefect> defects, string status, int index, int total)
		{
			var bitmap = image.ToBitmap();
			using (var g = Graphics.FromImage(bitmap))
			{
				g.SmoothingMode = SmoothingMode.AntiAlias;
				int w = bitmap.Width, h = bitmap.Height;

				var colorMap = new Dictionary<string, Color>
				{
					{ "破损", Color.FromArgb(231, 76, 60) },
					{ "搭舌缺陷", Color.FromArgb(230, 126, 34) },
					{ "边缘问题", Color.FromArgb(155, 89, 182) },
					{ "OK", Color.FromArgb(39, 174, 96) }
				};

				foreach (var defect in defects)
				{
					var box = defect.BoundingBox;
					int x1 = (int)(box[0] * w), y1 = (int)(box[1] * h);
					int x2 = (int)(box[2] * w), y2 = (int)(box[3] * h);
					var rect = new Rect(x1, y1, x2 - x1, y2 - y1);
					var color = colorMap.ContainsKey(defect.DefectType) ? colorMap[defect.DefectType] : Color.Red;

					using (var fill = new SolidBrush(Color.FromArgb(80, color)))
						g.FillRectangle(fill, rect);
					using (var pen = new Pen(color, 6))
						g.DrawRectangle(pen, rect);

					int dfFont = (_displayParams != null && _displayParams.DrawFontDefect > 0) ? _displayParams.DrawFontDefect : 18; using (var font = new Font("微软雅黑", dfFont, FontStyle.Bold))
					{
						string label = defect.DefectType;
						if (defect.Score > 0 && defect.Score < 1.0f)
							label = label + " " + defect.Score.ToString("F2");
						var labelSize = g.MeasureString(label, font);
						int lx = x1, ly = y1 - (int)labelSize.Height - 4;
						if (ly < 4) ly = y1 + 4;
						using (var bgBrush = new SolidBrush(color))
							g.FillRectangle(bgBrush, lx - 2, ly - 2, labelSize.Width + 8, labelSize.Height + 6);
						g.DrawString(label, font, Brushes.White, lx + 2, ly + 1);
					}
				}

				var statusColor = status == "OK" ? Color.Green : Color.Red;
				int stFont = (_displayParams != null && _displayParams.DrawFontStatus > 0) ? _displayParams.DrawFontStatus : 48; using (var font = new Font("微软雅黑", stFont, FontStyle.Bold))
				using (var brush = new SolidBrush(statusColor))
				{
					string display = status == "OK" ? "OK" : "NG";
					var ssz = g.MeasureString(display, font);
					using (var sbg = new SolidBrush(Color.FromArgb(180, Color.Black)))
						g.FillRectangle(sbg, w - (int)ssz.Width - 16, 2, ssz.Width + 12, ssz.Height + 10);
					g.DrawString(display, font, brush, w - (int)ssz.Width - 10, 5);
				}

				// 右下角：第几张/总数
				string idxLabel = (index + 1) + "/" + total;
				using (var f2 = new Font("微软雅黑", 22, FontStyle.Bold))
				{
					var isz = g.MeasureString(idxLabel, f2);
					using (var bg2 = new SolidBrush(Color.FromArgb(180, Color.Black)))
						g.FillRectangle(bg2, w - (int)isz.Width - 16, h - (int)isz.Height - 12, isz.Width + 12, isz.Height + 10);
					g.DrawString(idxLabel, f2, Brushes.Cyan, w - (int)isz.Width - 10, h - (int)isz.Height - 8);
				}
			}
			return bitmap;
		}

	/// <summary>更新显示缓存: 释放旧Bitmap→逐张重新绘制上下端面→上下拼接combined→存入_displayBitmaps/Images→重置索引(NG优先)</summary>
		private void UpdateDisplayImages(List<Mat> upperMats, List<Mat> lowerMats,
			Dictionary<int, List<BoxDefect>> upperDefects, Dictionary<int, List<BoxDefect>> lowerDefects,
			List<string> upperStatus, List<string> lowerStatus)
		{
			lock (_resultLock)
			{
				// 释放旧Bitmap，避免内存泄漏
				foreach (var b in _currentDisplayBitmaps) b?.Dispose();
				foreach (var b in _upperDisplayBitmaps) b?.Dispose();
				foreach (var b in _lowerDisplayBitmaps) b?.Dispose();
				_currentDisplayImages.Clear();
				_currentDisplayBitmaps.Clear();
				_upperDisplayImages.Clear();
				_upperDisplayBitmaps.Clear();
				_lowerDisplayImages.Clear();
				_lowerDisplayBitmaps.Clear();
				for (int i = 0; i < upperMats.Count; i++)
				{
					var upperBmp = DrawDefectOnImage(upperMats[i], upperDefects.ContainsKey(i) ? upperDefects[i] : new List<BoxDefect>(), upperStatus[i], i, upperMats.Count);
					_upperDisplayBitmaps.Add(upperBmp);  // 缓存Bitmap，由_displayBitmaps管理生命周期
					_upperDisplayImages.Add(BitmapConverter.ToMat(upperBmp));
					var lowerBmp = DrawDefectOnImage(lowerMats[i], lowerDefects.ContainsKey(i) ? lowerDefects[i] : new List<BoxDefect>(), lowerStatus[i], i, lowerMats.Count);
					_lowerDisplayBitmaps.Add(lowerBmp);
					_lowerDisplayImages.Add(BitmapConverter.ToMat(lowerBmp));

					int w = upperMats[i].Width;
					int h = upperMats[i].Height + lowerMats[i].Height;
					var combined = new Mat(new OpenCvSharp.Size(w, h), MatType.CV_8UC3);
					using (var upperRoi = new Mat(combined, new CvRect(0, 0, w, upperMats[i].Height)))
						upperMats[i].CopyTo(upperRoi);
					using (var lowerRoi = new Mat(combined, new CvRect(0, upperMats[i].Height, w, lowerMats[i].Height)))
						lowerMats[i].CopyTo(lowerRoi);
					var combinedBmp = DrawDefectOnCombined(combined, upperDefects.ContainsKey(i) ? upperDefects[i] : new List<BoxDefect>(),
						lowerDefects.ContainsKey(i) ? lowerDefects[i] : new List<BoxDefect>(), upperStatus[i], lowerStatus[i]);
					_currentDisplayBitmaps.Add(combinedBmp);  // 缓存Bitmap
					_currentDisplayImages.Add(BitmapConverter.ToMat(combinedBmp));
					combined.Dispose();
				}
				_currentDisplayIndex = FindFirstNgIndex(upperStatus, lowerStatus);
				_upperDisplayIndex = FindFirstNgInList(upperStatus, _upperDisplayImages.Count);
				_lowerDisplayIndex = FindFirstNgInList(lowerStatus, _lowerDisplayImages.Count);
			}
		}

		/// <summary>绘制上下端面合并图: 上半=上端面缺陷框, 下半=下端面缺陷框, 右上角上下OK/NG状态标签</summary>
		private Bitmap DrawDefectOnCombined(Mat combined, List<BoxDefect> upperDefects, List<BoxDefect> lowerDefects, string upperStatus, string lowerStatus)
		{
			var bitmap = combined.ToBitmap();
			using (var g = Graphics.FromImage(bitmap))
			{
				g.SmoothingMode = SmoothingMode.AntiAlias;
				int w = bitmap.Width, h = bitmap.Height;
				int midY = h / 2;

				var colorMap = new Dictionary<string, Color>
				{
					{ "破损", Color.FromArgb(231, 76, 60) },
					{ "搭舌缺陷", Color.FromArgb(230, 126, 34) },
					{ "边缘问题", Color.FromArgb(155, 89, 182) }
				};

				foreach (var defect in upperDefects)
				{
					var box = defect.BoundingBox;
					int x1 = (int)(box[0] * w), y1 = (int)(box[1] * midY);
					int x2 = (int)(box[2] * w), y2 = (int)(box[3] * midY);
					var rect = new Rect(x1, y1, x2 - x1, y2 - y1);
					var color = colorMap.ContainsKey(defect.DefectType) ? colorMap[defect.DefectType] : Color.Red;
					using (var pen = new Pen(color, 6))
						g.DrawRectangle(pen, rect);
				}

				foreach (var defect in lowerDefects)
				{
					var box = defect.BoundingBox;
					int x1 = (int)(box[0] * w), y1 = midY + (int)(box[1] * midY);
					int x2 = (int)(box[2] * w), y2 = midY + (int)(box[3] * midY);
					var rect = new Rect(x1, y1, x2 - x1, y2 - y1);
					var color = colorMap.ContainsKey(defect.DefectType) ? colorMap[defect.DefectType] : Color.Red;
					using (var pen = new Pen(color, 6))
						g.DrawRectangle(pen, rect);
				}

				int stFont2 = (_displayParams != null && _displayParams.DrawFontStatus > 0) ? _displayParams.DrawFontStatus : 28; using (var font = new Font("微软雅黑", stFont2, FontStyle.Bold))
				{
					var upperColor = upperStatus == "OK" ? Color.Green : Color.Red;
					var lowerColor = lowerStatus == "OK" ? Color.Green : Color.Red;
					var uSz = g.MeasureString(upperStatus == "OK" ? "OK" : "NG", font);
					var lSz = g.MeasureString(lowerStatus == "OK" ? "OK" : "NG", font);
					using (var sbg = new SolidBrush(Color.FromArgb(180, Color.Black)))
					{
						g.FillRectangle(sbg, w - (int)uSz.Width - 16, 2, uSz.Width + 12, uSz.Height + 10);
						g.FillRectangle(sbg, w - (int)lSz.Width - 16, midY + 2, lSz.Width + 12, lSz.Height + 10);
					}
					using (var brush = new SolidBrush(upperColor))
						g.DrawString(upperStatus == "OK" ? "OK" : "NG", font, brush, w - (int)uSz.Width - 10, 5);
					using (var brush = new SolidBrush(lowerColor))
						g.DrawString(lowerStatus == "OK" ? "OK" : "NG", font, brush, w - (int)lSz.Width - 10, midY + 5);
				}
			}
			return bitmap;
		}

	/// <summary>查找第一个NG索引: 遍历上/下端面状态, 任一NG即返回该索引, 全OK返回最后一张</summary>
		private int FindFirstNgIndex(List<string> upperStatus, List<string> lowerStatus)
		{
			for (int i = 0; i < _upperDisplayImages.Count; i++)
				if (i < upperStatus.Count && upperStatus[i] != "OK" || i < lowerStatus.Count && lowerStatus[i] != "OK")
					return i;
			return Math.Max(0, _upperDisplayImages.Count - 1);  // 全OK显示最后一张
		}

		/// <summary>在单侧状态列表中找第一个NG，全OK返回最后一张</summary>
		/// <summary>在单侧状态列表中找第一个NG, 全OK返回最后一张索引</summary>
		private int FindFirstNgInList(List<string> statusList, int count)
		{
			for (int i = 0; i < count && i < statusList.Count; i++)
				if (statusList[i] != "OK")
					return i;
			return Math.Max(0, count - 1);
		}

		/// <summary>获取当前显示的合并图像Mat(Clone副本, 线程安全)</summary>
		public Mat GetCurrentDisplayImage()
		{
			lock (_resultLock)
			{
				if (_currentDisplayImages.Count > 0 && _currentDisplayIndex >= 0 && _currentDisplayIndex < _currentDisplayImages.Count)
					return _currentDisplayImages[_currentDisplayIndex].Clone();
				return null;
			}
		}

		/// <summary>获取当前上端面渲染图Mat(Clone副本, 线程安全)</summary>
		public Mat GetCurrentUpperImage()
		{
			lock (_resultLock)
			{
				if (_upperDisplayImages.Count > 0 && _upperDisplayIndex >= 0 && _upperDisplayIndex < _upperDisplayImages.Count)
					return _upperDisplayImages[_upperDisplayIndex].Clone();  // 上端面独立索引
				return null;
			}
		}

		/// <summary>获取当前下端面渲染图Mat(Clone副本, 线程安全)</summary>
		public Mat GetCurrentLowerImage()
		{
			lock (_resultLock)
			{
				if (_lowerDisplayImages.Count > 0 && _lowerDisplayIndex >= 0 && _lowerDisplayIndex < _lowerDisplayImages.Count)
					return _lowerDisplayImages[_lowerDisplayIndex].Clone();  // 下端面独立索引
				return null;
			}
		}

		public bool NavigationEnabled { get; set; } = false;

		/// <summary>轮播上一张: _displayIndex循环递减</summary>
		public void NavigatePrev()
		{
			if (!NavigationEnabled) return;
			lock (_resultLock)
			{
				if (_currentDisplayImages.Count > 0)
				{
					_currentDisplayIndex = (_currentDisplayIndex - 1 + _currentDisplayImages.Count) % _currentDisplayImages.Count;
					OnStatusUpdate?.Invoke(new List<string>(), new List<string>(), new List<string>(), _currentDisplayImages.Count);
				}
			}
		}

		/// <summary>轮播下一张: _displayIndex循环递增</summary>
		public void NavigateNext()
		{
			if (!NavigationEnabled) return;
			lock (_resultLock)
			{
				if (_currentDisplayImages.Count > 0)
				{
					_currentDisplayIndex = (_currentDisplayIndex + 1) % _currentDisplayImages.Count;
					OnStatusUpdate?.Invoke(new List<string>(), new List<string>(), new List<string>(), _currentDisplayImages.Count);
				}
			}
		}

	/// <summary>保存端面批次图片: 渲染图(上/下端面分目录)+原图→JPEG 85%→Images/{日期}/{班次}/端面工位/{上下端面}/{OK|NG}/</summary>
		private void SaveImagesBatch(List<ImageContext> upperImages, List<ImageContext> lowerImages, List<Mat> upperMats, List<Mat> lowerMats, List<string> upperStatus, List<string> lowerStatus, List<string> mergedStatus, long productId, bool isOk)
		{
			bool saveOkImage = _Config.IsSaveOkImage;
			string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
			bool saveNgImage = _Config.IsSaveNgImage;
			bool saveOkRawImage = _Config.IsSaveOkRawImage;
			bool saveNgRawImage = _Config.IsSaveNgRawImage;

			if (!saveOkImage && !saveNgImage && !saveOkRawImage && !saveNgRawImage)
				return;

			string shift = GetCurrentShift();
			string dateDir = DateTime.Now.ToString("yyMMdd");
			string resultDir = isOk ? "OK" : "NG";
			string ngTypes = GetNgTypesString(mergedStatus);

			if ((isOk && saveOkImage) || (!isOk && saveNgImage))
			{
				string upperDir = Path.Combine(_savePath, dateDir, shift, "端面工位", "上端面", resultDir);
				string lowerDir = Path.Combine(_savePath, dateDir, shift, "端面工位", "下端面", resultDir);
				Directory.CreateDirectory(upperDir);
				Directory.CreateDirectory(lowerDir);
				for (int i = 0; i < upperImages.Count; i++)
				{
					// 只存NG图: 上下端面各自独立判断, OK的那个不存
					bool upNg = isOk || (i < upperStatus.Count && upperStatus[i] != "OK");
					bool loNg = isOk || (i < lowerStatus.Count && lowerStatus[i] != "OK");
					if (upNg && upperImages[i].RenderBitmap != null)
					{
						_imageSaver.AddSaveTask(Path.Combine(upperDir, $"{ts}_{i + 1}_上端面_渲染_{ngTypes}.jpg"), upperImages[i].RenderBitmap.ToJpegBytesFast(85), true, 85);
					}
					if (loNg && lowerImages[i].RenderBitmap != null)
					{
						_imageSaver.AddSaveTask(Path.Combine(lowerDir, $"{ts}_{i + 1}_下端面_渲染_{ngTypes}.jpg"), lowerImages[i].RenderBitmap.ToJpegBytesFast(85), true, 85);
					}
				}
			}

			if ((isOk && saveOkRawImage) || (!isOk && saveNgRawImage))
			{
				string upperDir = Path.Combine(_savePath, dateDir, shift, "端面工位", "上端面", resultDir);
				string lowerDir = Path.Combine(_savePath, dateDir, shift, "端面工位", "下端面", resultDir);
				Directory.CreateDirectory(upperDir);
				Directory.CreateDirectory(lowerDir);
				for (int i = 0; i < upperImages.Count; i++)
				{
					// 上下端面各自独立判断, OK那个不存原图
					bool upNg = isOk || (i < upperStatus.Count && upperStatus[i] != "OK");
					bool loNg = isOk || (i < lowerStatus.Count && lowerStatus[i] != "OK");
					if (upNg) { var bmp = upperMats[i].ToBitmap(); if (bmp != null) { _imageSaver.AddSaveTask(Path.Combine(upperDir, $"{ts}_{i + 1}_上端面_原图_{ngTypes}.jpg"), bmp.ToJpegBytesFast(85), false); bmp.Dispose(); } }
					if (loNg) { var bmp = lowerMats[i].ToBitmap(); if (bmp != null) { _imageSaver.AddSaveTask(Path.Combine(lowerDir, $"{ts}_{i + 1}_下端面_原图_{ngTypes}.jpg"), bmp.ToJpegBytesFast(85), false); bmp.Dispose(); } }
				}
			}
		}

		/// <summary>获取当前班次: 00~08=晚班, 08~16=早班, 16~24=中班</summary>
		private string GetCurrentShift()
		{
			var now = DateTime.Now.TimeOfDay;
			if (now >= TimeSpan.Parse("00:00:00") && now <= TimeSpan.Parse("07:59:59")) return "晚班";
			if (now >= TimeSpan.Parse("08:00:00") && now <= TimeSpan.Parse("15:59:59")) return "早班";
			return "中班";
		}

		/// <summary>生成NG类型字符串: 从statusList提取非OK项→去重→_连接, 全OK返回"OK"</summary>
		private string GetNgTypesString(List<string> statusList)
		{
			var ngTypes = statusList.Where(s => s != "OK").Distinct().ToList();
			if (ngTypes.Count == 0) return "OK";
			return string.Join("_", ngTypes);
		}

	/// <summary>缺陷classId→中文名称映射: 0=搭舌缺陷 1=边缘问题 2=破损</summary>
		private string GetDefectType(int classId)
		{
			var classMap = new Dictionary<int, string> { { 0, "搭舌缺陷" }, { 1, "边缘问题" }, { 2, "破损" } };
			return classMap.ContainsKey(classId) ? classMap[classId] : $"缺陷{classId}";
		}

		public void RestoreCounts(long ok, long ng) { _okCount = ok; _ngCount = ng; _totalCount = ok + ng; }
		/// <summary>清零统计计数: Interlocked.Exchange置0(线程安全)</summary>
		public void ClearCounters()
		{
			Interlocked.Exchange(ref _totalCount, 0);
			Interlocked.Exchange(ref _okCount, 0);
			Interlocked.Exchange(ref _ngCount, 0);
		}

		private void CleanupDisplayBitmaps()
		{
			lock (_resultLock)
			{
				foreach (var b in _currentDisplayBitmaps) b?.Dispose();
				foreach (var b in _upperDisplayBitmaps) b?.Dispose();
				foreach (var b in _lowerDisplayBitmaps) b?.Dispose();
			}
		}
		/// <summary>释放资源: 取消令牌→Join线程→Dispose队列→清理显示Bitmap缓存</summary>
		public void Dispose()
		{
			if (_disposed) return;
			_disposed = true;
			_cts.Cancel();
			_processThread?.Join(3000);
			_cts.Dispose();
			_batchQueue.Dispose();
			CleanupDisplayBitmaps();  // 释放缓存的Bitmap
		}
	}
}
