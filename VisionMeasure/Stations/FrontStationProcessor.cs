using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CommonLib;
using Config;
using Models;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using SmartMore.ViMo;
using VisionMeasure.Utils;
using YoloInference;
using AI;
using CvRect = OpenCvSharp.Rect;
using CvPoint = OpenCvSharp.Point;
using CvSize = OpenCvSharp.Size;

namespace VisionMeasure.Stations
{
	///<summary>	
	/// 正面工位处理器 — 左右图配对后并行推理(P号OCR+盒子破损YOLO)	
	/// 触发: Camera1/Camera2的OnImage回调 → OnCam1/OnCam2 → 配对缓冲 → 异步处理	
	/// 推理: Task.Run并行: RecognizePNumber(ViMo OCR逐盒ROI) + DetectBoxDamage(YOLO分盒映射)	
	/// 汇总: 逐盒合并P号结果+破损结果 → statusList(OK/NG) → 按盒粒度计数	
	/// 显示: 合并渲染图 → OnResultReady → MainFrm.OnStationResult → xlPictureBox1	
	/// 存图: 渲染图+左原图+右原图 → JPEG(yyyyMMdd_HHmmss_fff_渲染_OK/NG类型.jpg)	
	/// </summary>
	public class FrontStationProcessor : IDisposable
	{
		private static readonly Regex PNumberRegex = new Regex(@"P\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		private const int PNumberMinLength = 6;

		private HighSpeedImageSaver _imageSaver;
		private readonly AiModelManager _models;
		private readonly DetectionParameters _detectionParams;
		private Config.ModelParams _pcodeParams;

		private Mat _leftBuffer = null;
		private Mat _rightBuffer = null;
		private readonly object _syncLock = new object();
		private int _isProcessing = 0;  // 防重入: 0=空闲, 1=处理中

		private long _okCount = 0;
		private long _ngCount = 0;
		private long _imgCount = 0;
		private SkuData _currentSku = null;
		private bool _lastIsOk = true;

		/// <summary>OK计数（公开只读，供统计显示使用）</summary>
		public long OkCount => _okCount;
		/// <summary>NG计数（公开只读，供统计显示使用）</summary>
		public long NgCount => _ngCount;
		/// <summary>收图计数</summary>
		public long ImgCount => _imgCount;

		public event Action<Bitmap, bool[], long, long> OnResultReady;
		public event Action<List<string>, int> OnStatusUpdate;

		public float ConfThreshold { get; set; } = 0.5f;
		public float IouThreshold { get; set; } = 0.45f;
		public bool ReverseBoxOrder = false;
		public bool EnablePNumberCheck = false;
		public bool EnableBoxBreakCheck = true;
		public bool SkipCrop = false;

		public FrontStationProcessor(AiModelManager modelManager, DetectionParameters detectionParams)
		{
			_models = modelManager;
			_detectionParams = detectionParams;
			_pcodeParams = Config.ModelParams.Load("front_pcode");
			EnablePNumberCheck = detectionParams.Front.EnablePNumberCheck;
			EnableBoxBreakCheck = detectionParams.Front.EnableBoxBreakCheck;
			_imageSaver = new HighSpeedImageSaver();
		}

		/// <summary>重新加载ModelParams，无需重启软件</summary>
		public void ReloadModelParams()
		{
			_pcodeParams = Config.ModelParams.Load("front_pcode");
			var fbParams = Config.ModelParams.Load("front_box");
			if (fbParams.Confidence > 0) ConfThreshold = fbParams.Confidence;
			if (fbParams.Iou > 0) IouThreshold = fbParams.Iou;
			Logger.Info($"[Front] ModelParams已重新加载 Conf={ConfThreshold:F2} Iou={IouThreshold:F2}");
		}

		// 从模型best.json加载阈值
		/// <summary>初始化模型阈值: 从FrontBoxBreakModel加载Conf/Iou阈值覆盖默认值</summary>
		public void InitThresholdsFromModel() {
			if (_models.FrontBoxBreakModel != null) {
				if (_models.FrontBoxBreakModel != null)
			{
				ConfThreshold = _models.FrontBoxBreakModel.DefaultConfThres;
				IouThreshold = _models.FrontBoxBreakModel.DefaultIouThres;
			}
				Logger.Info($"[Front] 阈值从模型: Conf={ConfThreshold:F2} Iou={IouThreshold:F2}");
			}
		}

		public void Start() { ClearCounters(); Logger.Info("FrontStationProcessor Started."); }
		public void UpdateSku(SkuData newSku) { _currentSku = newSku; }
		public void RestoreCounts(long ok, long ng) { _okCount = ok; _ngCount = ng; }
		public void ClearCounters() { _okCount = 0; _ngCount = 0; }

		/// <summary>相机1(正面左)图像回调 — 图像→Mat→Flip(XY翻转)→配对缓冲→CheckAndProcessAsync</summary>
		public void OnCam1(Bitmap leftImg, object extraArg = null)
		{
			if (leftImg == null) return;
			Interlocked.Increment(ref _imgCount);
			Logger.Debug($"[Front] OnCam1 收到图像 {leftImg.Width}x{leftImg.Height}");
			lock (_syncLock)
			{
				_leftBuffer?.Dispose();
				_leftBuffer = leftImg.ToMat();
				if (!SkipCrop) Cv2.Flip(_leftBuffer, _leftBuffer, FlipMode.XY);
			}
			CheckAndProcessAsync();
		}

		/// <summary>相机2(正面右)图像回调 — 图像→Mat→Flip(XY翻转)→配对缓冲→CheckAndProcessAsync</summary>
		public void OnCam2(Bitmap rightImg, object extraArg = null)
		{
			if (rightImg == null) return;
			Interlocked.Increment(ref _imgCount);
			Logger.Debug($"[Front] OnCam2 收到图像 {rightImg.Width}x{rightImg.Height}");
			lock (_syncLock)
			{
				_rightBuffer?.Dispose();
				_rightBuffer = rightImg.ToMat();
				if (!SkipCrop) Cv2.Flip(_rightBuffer, _rightBuffer, FlipMode.XY);
			}
			CheckAndProcessAsync();
		}

		/// <summary>		
		/// 配对+异步处理: 左右图都到达→Flip(XY翻转)→裁图→2路并行推理→汇总→绘制→保存		
		/// 并行: Task.Run(P号OCR) + Task.Run(盒子破损YOLO) → await Task.WhenAll		
		/// P号: 逐盒ROI→ViMo OCR→正则匹配Pd+→与参考P号比对(EnablePNumberCheck开关)		
		/// 破损: YOLO → ProcessYoloResults(按中心X坐标分配盒索引)		
		/// 统计: OK/NG按单盒粒度计数(非按排)		
		/// </summary>
		private async void CheckAndProcessAsync()
		{
			Mat leftToProcess = null, rightToProcess = null;
			lock (_syncLock)
			{
				if (_leftBuffer != null && _rightBuffer != null)
				{
					leftToProcess = _leftBuffer; rightToProcess = _rightBuffer;
					_leftBuffer = null; _rightBuffer = null;
					Logger.Trace("[Front] ▶ 左右配对成功 图=" + leftToProcess.Width + "x" + leftToProcess.Height);
					Logger.Debug("[Front] 左右图像配对成功，开始处理");
				}
			}
			if (leftToProcess == null || rightToProcess == null) return;

			// 防重入: 上一批未处理完则丢弃当前这组(保留最新buffer已被清空, 新图会填充)
			if (Interlocked.CompareExchange(ref _isProcessing, 1, 0) != 0)
			{
				Logger.Warning("[Front] 上一批处理未完成, 跳过当前配对(防止并发处理导致数据混乱)");
				leftToProcess?.Dispose(); rightToProcess?.Dispose();
				return;
			}

			var swTotal = System.Diagnostics.Stopwatch.StartNew();
			Mat leftProc = null, rightProc = null;
			try
			{
				int pCount = _currentSku?.P ?? 8;
				int halfP = pCount / 2;

				// 步骤0: 裁图
				leftProc = leftToProcess; rightProc = rightToProcess;
				if (_currentSku != null && !SkipCrop)
				{
					try
					{
						int w = leftToProcess.Width;
						if (_currentSku.FrontLeft_LeftPx > 0 || _currentSku.FrontLeft_RightPx > 0)
						{
							int rawL = _currentSku.FrontLeft_LeftPx, rawR = _currentSku.FrontLeft_RightPx;
							leftProc = ImageHelper.CropImageHorizontallyCv2(leftToProcess, w - rawR, leftToProcess.Width - (w - rawL));
							Logger.Debug($"[Front] 左图裁图: 原始{rawL}~{rawR} -> {leftProc.Width}x{leftProc.Height}");
						}
						if (_currentSku.FrontRight_LeftPx > 0 || _currentSku.FrontRight_RightPx > 0)
						{
							int rawL = _currentSku.FrontRight_LeftPx, rawR = _currentSku.FrontRight_RightPx;
							rightProc = ImageHelper.CropImageHorizontallyCv2(rightToProcess, w - rawR, rightToProcess.Width - (w - rawL));
							Logger.Debug($"[Front] 右图裁图: 原始{rawL}~{rawR} -> {rightProc.Width}x{rightProc.Height}");
						}
					}
					catch (Exception ex) { Logger.Warning($"[Front] 裁图失败({ex.Message}), 使用原图"); }
				}

				Logger.Trace("[Front] ▷ 推理开始 P=" + pCount + " 并行P号+破损");
				// 步骤1: 并行推理
				var sw1 = System.Diagnostics.Stopwatch.StartNew();
				var pNumberTask = Task.Run(() => RecognizePNumber(leftProc, rightProc, pCount, halfP));
				var damageTask = Task.Run(() => DetectBoxDamage(leftProc, rightProc));
				await Task.WhenAll(pNumberTask, damageTask);
				var pNumberResults = pNumberTask.Result;
				var damageResults = damageTask.Result;

				// 分离P号码的"仅显示"和"判NG"结果
				var pNumberNg = new Dictionary<int, List<BoxDefect>>();
				foreach (var kv in pNumberResults)
				{
					var ngList = kv.Value.Where(d => d.DefectType.Contains("错误") || d.DefectType == "P号缺少").ToList();
					if (ngList.Count > 0) pNumberNg[kv.Key] = ngList;
				}

				Logger.Trace("[Front] ✓ 推理完成 " + sw1.Elapsed.TotalMilliseconds.ToString("F0") + "ms");
				Logger.Info($"[Front] 步骤1完成: 推理={sw1.Elapsed.TotalMilliseconds:F1}ms P号={pNumberResults.Values.Sum(v => v.Count)} 破损={damageResults.Values.Sum(v => v.Count)}");

				// 步骤2: 汇总结果(P号仅EnablePNumberCheck时判NG)
				var statusList = new List<string>();
				var ngArray = new bool[pCount];
				for (int i = 0; i < pCount; i++)
				{
					var defects = new List<string>();
					if (pNumberNg.ContainsKey(i)) defects.AddRange(pNumberNg[i].Select(d => d.DefectType));
					if (damageResults.ContainsKey(i)) defects.AddRange(damageResults[i].Select(d => d.DefectType));
					ngArray[i] = defects.Count > 0;
					statusList.Add(defects.Count > 0 ? string.Join(",", defects) : "OK");
				}
				Logger.Info("[Front]  " + string.Join(" ", Enumerable.Range(1,statusList.Count).Select(i => i.ToString().PadLeft(2))));
				Logger.Info("[Front]  " + string.Join("  ", statusList.Select(s => s == "OK" ? "O" : "X")));

				int currentNgCount = ngArray.Count(n => n);
				bool isOk = (currentNgCount == 0);
				if (isOk) _okCount += pCount; else { _okCount += (pCount - currentNgCount); _ngCount += currentNgCount; }
				_lastIsOk = isOk;

				// 缺陷统计
				var defStats = new Dictionary<string, int>();
				foreach (var s in statusList)
				{
					if (s != "OK")
						foreach (var d in s.Split(','))
						{
							var k = d.Trim();
							if (!string.IsNullOrEmpty(k))
							{
								if (defStats.ContainsKey(k)) defStats[k]++;
								else defStats[k] = 1;
							}
						}
				}
				string defStr = defStats.Count > 0 ? string.Join(" ", defStats.Select(kv => kv.Key + ":" + kv.Value))
					: "盒子破损:0 P号错误:0";
				defStr = " | " + defStr;
				double elapsed = swTotal.Elapsed.TotalMilliseconds;
				Logger.Info($"[Front] 完成 P={pCount} OK={pCount - currentNgCount} NG={currentNgCount}{defStr} | 耗时={elapsed:F0}ms");

				// 步骤3: 绘制+合并渲染图
				Logger.Debug("[Front] 步骤3: 绘制+合并...");
				var sw3 = System.Diagnostics.Stopwatch.StartNew();
				var merged = DrawAndMergeResults(leftProc, rightProc, pNumberResults, damageResults, statusList, halfP, isOk);
				Logger.Info($"[Front] 步骤3完成: {sw3.Elapsed.TotalMilliseconds:F1}ms {merged.Width}x{merged.Height}");

				// 步骤4: 保存图片
				Logger.Debug("[Front] 步骤4: 保存...");
				var sw4 = System.Diagnostics.Stopwatch.StartNew();
				SaveImages(leftProc, rightProc, merged, ngArray);
				Logger.Info($"[Front] 步骤4完成: 保存={sw4.Elapsed.TotalMilliseconds:F1}ms");

				// 步骤5: 发射结果事件(更新UI)
				OnResultReady?.Invoke(merged, ngArray, _okCount, _ngCount);
			}
			catch (Exception ex) { Logger.Error($"[Front] 处理异常: {ex.Message}\r\n{ex.StackTrace}"); }
			finally
			{
				Interlocked.Exchange(ref _isProcessing, 0);  // 防重入锁释放
				leftToProcess?.Dispose(); rightToProcess?.Dispose();
				if (leftProc != null && leftProc != leftToProcess) leftProc.Dispose();
				if (rightProc != null && rightProc != rightToProcess) rightProc.Dispose();
			}
		}

		/// <summary>
/// P号码OCR识别: 逐盒ROI裁剪(左halfP+右halfP, 取下方1/3),
/// ViMo OCR -> 正则匹配 -> 与参考P号比对, 返回缺陷列表.
/// </summary>
		private Dictionary<int, List<BoxDefect>> RecognizePNumber(Mat left, Mat right, int pCount, int halfP)
		{
			var results = new Dictionary<int, List<BoxDefect>>();
			if (_models.FrontOcrModel == null) return results;
			try
			{
				int hL = left.Height, wL = left.Width, hR = right.Height, wR = right.Width;
				int boxWL = wL / halfP, boxWR = wR / halfP;
				double pcRatio = (_pcodeParams != null) ? _pcodeParams.StartHeightRatioPCode : (2.0 / 3.0);
			int startYL = (int)(hL * pcRatio), startYR = (int)(hR * pcRatio);
				string refPNumber = _currentSku?.FrontPCode;
				bool hasRef = !string.IsNullOrEmpty(refPNumber);

				for (int i = 0; i < halfP; i++) { int sx = i * boxWL; int rw = (i < halfP - 1) ? boxWL : (wL - sx); int rh = hL - startYL; if (rw > 0 && rh > 0) using (var roi = new Mat(left, new CvRect(sx, startYL, rw, rh)).Clone()) ProcessPNumberRoi(roi, i, refPNumber, hasRef, wL, hL, sx, startYL, results); }
				for (int j = 0; j < halfP; j++) { int gi = halfP + j; int sx = j * boxWR; int rw = (j < halfP - 1) ? boxWR : (wR - sx); int rh = hR - startYR; if (rw > 0 && rh > 0) using (var roi = new Mat(right, new CvRect(sx, startYR, rw, rh)).Clone()) ProcessPNumberRoi(roi, gi, refPNumber, hasRef, wR, hR, sx, startYR, results); }
			}
			catch (Exception ex) { Logger.Error($"P号码识别异常: {ex.Message}"); }
			return results;
		}

		/// <summary>处理单盒P号ROI: ViMo OCR→遍历Blocks→PNumberRegex匹配→过滤碎片(长度<PNumberMinLength)→与参考比对→画框(OK绿/NG橙)</summary>
		private void ProcessPNumberRoi(Mat roi, int boxIdx, string refPNumber, bool hasRef,
			int fullW, int fullH, int offsetX, int offsetY, Dictionary<int, List<BoxDefect>> results)
		{
			ResponseList<OcrResponse> ocrResults;
			int ret = _models.FrontOcrModel.Run(roi, out ocrResults);
			if (ret != 0 || ocrResults == null || ocrResults.Count == 0)
			{
				if (hasRef && EnablePNumberCheck)
					AddDefect(results, boxIdx, "P号缺少", new float[] { 0, (float)offsetY / fullH, 0.1f, (float)(offsetY + roi.Height) / fullH });
				return;
			}

			bool foundAny = false;
			foreach (var resTuple in ocrResults)
			{
				OcrResponse ocrResp = resTuple.Item2;
				if (ocrResp.Blocks == null) continue;
				foreach (var block in ocrResp.Blocks)
				{
					if (string.IsNullOrWhiteSpace(block.Label)) continue;
					Match match = PNumberRegex.Match(block.Label);
					if (!match.Success) continue;
					string pNum = match.Value.ToUpper();
					if (pNum.Length < PNumberMinLength) continue; // 过滤碎片

					foundAny = true;
					float[] normBox = ComputeNormBBox(block, fullW, fullH, offsetX, offsetY);
					bool isMatch = (pNum == refPNumber);

					if (EnablePNumberCheck && hasRef && !isMatch)
					{
						AddDefect(results, boxIdx, $"P号错误(识:{pNum}/标:{refPNumber})", normBox);
					}
					else
					{
						// 始终画框: OK用绿色显示识别结果
						AddDefect(results, boxIdx, $"P号:{pNum}", normBox);
						Logger.Debug($"[Front] P号盒{boxIdx + 1}: 识别={pNum}" + (isMatch ? " OK" : ""));
					}
				}
			}
			if (!foundAny && hasRef && EnablePNumberCheck)
				AddDefect(results, boxIdx, "P号缺少", new float[] { 0, (float)offsetY / fullH, 0.1f, (float)(offsetY + roi.Height) / fullH });
		}

		/// <summary>计算归一化包围框: TextBlock.Polygon→min/max→(x/fullW, y/fullH)归一化→[x1,y1,x2,y2]</summary>
		private float[] ComputeNormBBox(TextBlock block, int fullW, int fullH, int offsetX, int offsetY)
		{
			if (block.Polygon == null || !block.Polygon.Any()) return new float[] { 0, 0, 0.1f, 0.1f };
			float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
			foreach (var pt in block.Polygon) { float gx = pt.X + offsetX, gy = pt.Y + offsetY; if (gx < minX) minX = gx; if (gy < minY) minY = gy; if (gx > maxX) maxX = gx; if (gy > maxY) maxY = gy; }
			return new float[] { minX / fullW, minY / fullH, maxX / fullW, maxY / fullH };
		}

		/// <summary>添加缺陷到字典: 若key不存在则创建List→添加BoxDefect(盒索引+缺陷类型+归一化坐标)</summary>
		private void AddDefect(Dictionary<int, List<BoxDefect>> dict, int idx, string type, float[] box)
		{
			if (!dict.ContainsKey(idx)) dict[idx] = new List<BoxDefect>();
			dict[idx].Add(new BoxDefect(idx, type, box));
		}

		/// <summary>盒子破损检测: 3×2网格裁图(重叠覆盖)→逐张Predict(batch=1)→centerX分盒→盒内NMS去重→返回缺陷(盒子破损+Score)</summary>
		private Dictionary<int, List<BoxDefect>> DetectBoxDamage(Mat left, Mat right)
		{
			var results = new Dictionary<int, List<BoxDefect>>();
			if (!EnableBoxBreakCheck || _models.FrontBoxBreakModel == null) return results;

			int pCount = _currentSku?.P ?? 8;
			int halfP = pCount / 2;

			try
			{
				// 本地函数: 处理单侧图像 (3×2网格裁图 → 逐张Predict → 坐标映射 → 分盒)
				void ProcessSide(Mat sourceImage, bool isLeft)
				{
					int currentW = sourceImage.Width;
					int currentH = sourceImage.Height;
					int baseIdx = isLeft ? 0 : halfP;

					var (patches, offsets) = GetCropPatchesAndOffsets(sourceImage, pCount);

					for (int i = 0; i < patches.Count; i++)
					{
						Mat patch = patches[i];
						CvPoint offset = offsets[i];

						try
						{
							var result = _models.FrontBoxBreakModel.Predict(patch, ConfThreshold, IouThreshold);

							for (int j = 0; j < result.Boxes.Length; j++)
							{
								var box = result.Boxes[j];
								float score = result.Scores[j];

								// 映射回原图绝对坐标
								float origX1 = box.Left + offset.X;
								float origY1 = box.Top + offset.Y;
								float origX2 = box.Right + offset.X;
								float origY2 = box.Bottom + offset.Y;

								// 归一化到整图坐标
								float nx1 = origX1 / currentW, ny1 = origY1 / currentH;
								float nx2 = origX2 / currentW, ny2 = origY2 / currentH;

								// centerX 确定盒子索引
								float centerX = (origX1 + origX2) / 2f;
								int boxLocal = (int)(centerX / currentW * halfP);
								boxLocal = Math.Max(0, Math.Min(boxLocal, halfP - 1));
								int globalIdx = baseIdx + boxLocal;

								if (!results.ContainsKey(globalIdx))
									results[globalIdx] = new List<BoxDefect>();
								results[globalIdx].Add(new BoxDefect(globalIdx, "盒子破损",
									new float[] { nx1, ny1, nx2, ny2 }, score));
							}
						}
						finally { patch?.Dispose(); }  // 每张用完即释放
					}
				}

				// 处理左右两侧
				Logger.Info($"[Front BatchLog] ▶ 盒子破推理: batch=1 逐张Predict, 左3×2={3*2}patch 右3×2={3*2}patch (P={pCount})");
				ProcessSide(left, isLeft: true);
				ProcessSide(right, isLeft: false);

				// 盒内NMS去重 (重叠patch导致同一缺陷被多次检出)
				int totalBeforeNms = results.Values.Sum(v => v.Count);
				ApplyNmsPerBox(results, IouThreshold);
				int totalAfterNms = results.Values.Sum(v => v.Count);
				Logger.Info($"[Front BatchLog] ◀ 盒子破推理完成: P={pCount}, 检出框={totalBeforeNms}→{totalAfterNms}(NMS后)");
			}
			catch (Exception ex) { Logger.Error($"盒子破损检测异常: {ex.Message}"); }
			return results;
		}

		/// <summary>3×2网格裁图: 水平3段+垂直2段(带10%重叠), 返回patch列表+偏移量</summary>
		private static (List<Mat> Patches, List<CvPoint> Offsets) GetCropPatchesAndOffsets(Mat image, int P)
		{
			int h = image.Height, w = image.Width;
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

			var patches = new List<Mat>();
			var offsets = new List<CvPoint>();

			foreach (var xb in xBoundaries)
				foreach (var yb in yBoundaries)
				{
					int pw = xb.end - xb.start, ph = yb.end - yb.start;
					CvRect roi = new CvRect(xb.start, yb.start, pw, ph);
					patches.Add(new Mat(image, roi).Clone());
					offsets.Add(new CvPoint(xb.start, yb.start));
				}

			return (patches, offsets);
		}

		/// <summary>盒内NMS去重: 重叠patch可能让同一缺陷被多次检出, 每盒独立做NMS</summary>
		private static void ApplyNmsPerBox(Dictionary<int, List<BoxDefect>> results, float iouThreshold)
		{
			foreach (var kvp in results.ToList())
			{
				var defects = kvp.Value;
				if (defects.Count <= 1) continue;

				var boxesWithScore = defects.Select(d => new float[] {
					d.BoundingBox[0], d.BoundingBox[1], d.BoundingBox[2], d.BoundingBox[3], d.Score
				}).ToList();

				var sorted = boxesWithScore
					.Select((b, i) => (box: b, idx: i))
					.OrderByDescending(x => x.box[4]).ToList();
				var removed = new bool[sorted.Count];
				var keep = new List<int>();

				for (int i = 0; i < sorted.Count; i++)
				{
					if (removed[i]) continue;
					keep.Add(sorted[i].idx);
					float ax1 = sorted[i].box[0], ay1 = sorted[i].box[1];
					float ax2 = sorted[i].box[2], ay2 = sorted[i].box[3];
					float areaA = (ax2 - ax1) * (ay2 - ay1);

					for (int j = i + 1; j < sorted.Count; j++)
					{
						if (removed[j]) continue;
						float bx1 = sorted[j].box[0], by1 = sorted[j].box[1];
						float bx2 = sorted[j].box[2], by2 = sorted[j].box[3];
						float xx1 = Math.Max(ax1, bx1), yy1 = Math.Max(ay1, by1);
						float xx2 = Math.Min(ax2, bx2), yy2 = Math.Min(ay2, by2);
						float iw = Math.Max(0, xx2 - xx1), ih = Math.Max(0, yy2 - yy1);
						float inter = iw * ih;
						float areaB = (bx2 - bx1) * (by2 - by1);
						float iou = inter / (areaA + areaB - inter);
						if (iou > iouThreshold) removed[j] = true;
					}
				}

				results[kvp.Key] = keep.Select(k => defects[k]).ToList();
			}
		}

		/// <summary>YOLO结果→分盒映射: BoxesN归一化→centerX*n确定盒索引(startIdx~endIdx-1)→构建BoxDefect(归一化坐标+缺陷类型+置信度)</summary>
		private void ProcessYoloResults(YoloResult result, Dictionary<int, List<BoxDefect>> results, int startIdx, int endIdx, string defectType)
		{
			if (result == null || result.BoxesN == null) return;
			int n = endIdx - startIdx; if (n <= 0) return;
			for (int j = 0; j < result.BoxesN.Length; j++)
			{
				var box = result.BoxesN[j];
				float score = (result.Scores != null && j < result.Scores.Length) ? result.Scores[j] : 1.0f;
				float cx = box.X + box.Width / 2f;
				int idx = startIdx + (int)(cx * n);
				if (idx >= startIdx && idx < endIdx)
				{
					if (!results.ContainsKey(idx)) results[idx] = new List<BoxDefect>();
					results[idx].Add(new BoxDefect(idx, defectType, new float[] { box.X, box.Y, box.X + box.Width, box.Y + box.Height }, score));
				}
			}
		}

		/// <summary>绘制+合并: 左图DrawDefects(P号框/破损框/状态/序号)+右图DrawDefects→MergeImages(水平拼接+OK/NG大字)</summary>
		private Bitmap DrawAndMergeResults(Mat left, Mat right,
			Dictionary<int, List<BoxDefect>> pNumberResults, Dictionary<int, List<BoxDefect>> damageResults,
			List<string> statusList, int halfP, bool isOk)
		{
			var lb = left.ToBitmap(); var rb = right.ToBitmap();
			int p = _currentSku?.P ?? 8;
			using (var g = Graphics.FromImage(lb))
			{
				g.SmoothingMode = SmoothingMode.AntiAlias;
				DrawDefects(g, pNumberResults, damageResults, statusList, 0, halfP, lb.Width, lb.Height);
			}
			using (var g = Graphics.FromImage(rb))
			{
				g.SmoothingMode = SmoothingMode.AntiAlias;
				DrawDefects(g, pNumberResults, damageResults, statusList, halfP, p, rb.Width, rb.Height);
			}
			return MergeImages(lb, rb, isOk);
		}

		/// <summary>绘制缺陷: 分区虚线→P号框(匹配绿/不匹配橙)→破损框(红)→每盒OK/NG状态标签(绿/红)→盒序号(黄色, 支持ReverseBoxOrder反转)</summary>
		private void DrawDefects(Graphics g,
			Dictionary<int, List<BoxDefect>> pNumberResults, Dictionary<int, List<BoxDefect>> damageResults,
			List<string> statusList, int startIdx, int endIdx, int imgWidth, int imgHeight)
		{
			int n = endIdx - startIdx, p = _currentSku?.P ?? 8;

			// 分区虚线
			if (n > 1) using (var dp = new Pen(Color.FromArgb(100, 100, 100), 3) { DashStyle = DashStyle.Dash })
				for (int i = 1; i < n; i++) g.DrawLine(dp, i * imgWidth / n, 0, i * imgWidth / n, imgHeight);

			// P号码框: 全部画出, OK绿色(仅显示)/NG橙色
			for (int i = startIdx; i < endIdx; i++)
			{
				if (pNumberResults.ContainsKey(i))
					foreach (var d in pNumberResults[i])
					{
						bool isPng = d.DefectType.Contains("错误") || d.DefectType == "P号缺少";
						bool isPonly = d.DefectType.StartsWith("P号:");
						Color c = isPng ? Color.Orange : (isPonly ? Color.Lime : Color.Orange);
						DrawDefectBox(g, d, imgWidth, imgHeight, c);
					}
			}

			// 破损框: 红色
			for (int i = startIdx; i < endIdx; i++)
			{
				if (damageResults.ContainsKey(i))
					foreach (var d in damageResults[i])
						DrawDefectBox(g, d, imgWidth, imgHeight, Color.Red);
			}

			// 每盒状态标签
			using (var sf = new Font("微软雅黑", 48, FontStyle.Bold))
				for (int i = 0; i < n && startIdx + i < statusList.Count; i++)
				{
					string s = statusList[startIdx + i];
					string disp = s == "OK" ? "OK" : (s.Length > 4 ? s.Substring(0, 4) : s);
					Color c = s == "OK" ? Color.Green : Color.Red;
					float cx = (i + 0.5f) * imgWidth / n;
					var sz = g.MeasureString(disp, sf);
					using (var br = new SolidBrush(c)) g.DrawString(disp, sf, br, cx - sz.Width / 2, 60);
				}

			// 盒序号
			using (var nf = new Font("微软雅黑", 28, FontStyle.Bold))
				for (int i = 0; i < n && startIdx + i < p; i++)
				{
					int boxNum = ReverseBoxOrder ? (p - (startIdx + i)) : (startIdx + i + 1);
					float cx = (i + 0.5f) * imgWidth / n;
					var sz = g.MeasureString("盒" + boxNum, nf);
					using (var br = new SolidBrush(Color.Yellow)) g.DrawString("盒" + boxNum, nf, br, cx - sz.Width / 2, 120);
				}
		}

		/// <summary>绘制单个缺陷框(半透明填充+实线边框+分数标签) — 支持P号(绿/橙)和破损(红)两种颜色</summary>
		private void DrawDefectBox(Graphics g, BoxDefect defect, int imgWidth, int imgHeight, Color baseColor)
		{
			if (defect.BoundingBox == null || defect.BoundingBox.Length < 4) return;
			int x1 = (int)(defect.BoundingBox[0] * imgWidth), y1 = (int)(defect.BoundingBox[1] * imgHeight);
			int x2 = (int)(defect.BoundingBox[2] * imgWidth), y2 = (int)(defect.BoundingBox[3] * imgHeight);
			if (x2 <= x1 || y2 <= y1) return;
			var rc = new Rectangle(x1, y1, x2 - x1, y2 - y1);

			using (var fill = new SolidBrush(Color.FromArgb(30, baseColor))) g.FillRectangle(fill, rc);
			using (var pn = new Pen(baseColor, 3)) g.DrawRectangle(pn, rc);

			string label = defect.DefectType;
			if (defect.Score > 0 && defect.Score < 1.0f && !label.StartsWith("P号"))
				label = label + " " + defect.Score.ToString("F2");
			if (label.StartsWith("P号:") || label.StartsWith("P号错误")) { /* shown as-is */ }
			if (label.Length > 20) label = label.Substring(0, 20);
			using (var f = new Font("微软雅黑", 14, FontStyle.Bold))
			{
				var sz = g.MeasureString(label, f);
				int ly = y1 - (int)sz.Height - 8; if (ly < 8) ly = y1 + 8;
				using (var bg = new SolidBrush(baseColor)) g.FillRectangle(bg, x1 - 2, ly - 2, sz.Width + 8, sz.Height + 6);
				g.DrawString(label, f, Brushes.White, x1 + 2, ly + 1);
			}
		}

		/// <summary>合并左右渲染图: 黑底+白色分隔线+右上角OK/NG大字标签</summary>
		private Bitmap MergeImages(Bitmap left, Bitmap right, bool isOk)
		{
			Bitmap merged = new Bitmap(left.Width + right.Width, Math.Max(left.Height, right.Height), PixelFormat.Format24bppRgb);
			using (Graphics g = Graphics.FromImage(merged))
			{
				g.Clear(Color.Black);
				g.DrawImage(left, 0, (merged.Height - left.Height) / 2);
				g.DrawImage(right, left.Width, (merged.Height - right.Height) / 2);
				using (var pn = new Pen(Color.White, 4)) g.DrawLine(pn, left.Width, 0, left.Width, merged.Height);

				string txt = isOk ? "OK" : "NG";
				Color tc = isOk ? Color.Lime : Color.Red;
				using (var f = new Font("微软雅黑", 120, FontStyle.Bold))
				{
					var sz = g.MeasureString(txt, f);
					int rx = merged.Width - (int)sz.Width - 60, ry = 30;
					using (var bg = new SolidBrush(Color.FromArgb(180, Color.Black))) g.FillRectangle(bg, rx - 20, ry - 10, sz.Width + 40, sz.Height + 20);
					using (var br = new SolidBrush(tc)) g.DrawString(txt, f, br, rx, ry);
				}
			}
			left.Dispose(); right.Dispose();
			return merged;
		}

		/// <summary>保存正面图片: 渲染图+左原图+右原图→JPEG→Images/{日期}/{班次}/正面工位/{OK|NG}/, 文件名含时间戳+NG类型</summary>
		private void SaveImages(Mat left, Mat right, Bitmap merged, bool[] ngArray)
		{
			try
			{
				bool hasNg = ngArray.Any(n => n);
				bool so = _detectionParams.Save.SaveOkImage && !hasNg, sn = _detectionParams.Save.SaveNgImage && hasNg;
				bool sor = _detectionParams.Save.SaveOkRawImage && !hasNg, snr = _detectionParams.Save.SaveNgRawImage && hasNg;
				if (!so && !sn && !sor && !snr) return;

				string shift = GetShift(), dd = DateTime.Now.ToString("yyMMdd");
				string nt = hasNg ? string.Join("_", ngArray.Select((n, i) => n ? $"NG{i + 1}" : "").Where(s => !string.IsNullOrEmpty(s))) : "OK";
				string dir = System.IO.Path.Combine(_detectionParams.Save.ImageSavePath, dd, shift, "正面工位", hasNg ? "NG" : "OK");
				System.IO.Directory.CreateDirectory(dir);

				long pid = DateTime.Now.Ticks;
				string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
				if (so || sn) _imageSaver.Enqueue(merged, System.IO.Path.Combine(dir, $"{ts}_渲染_{nt}.jpg"), ImageFormat.Jpeg);
				if (sor || snr)
				{
					_imageSaver.Enqueue(left.ToBitmap(), System.IO.Path.Combine(dir, $"{ts}_左原图_{nt}.jpg"), ImageFormat.Jpeg);
					_imageSaver.Enqueue(right.ToBitmap(), System.IO.Path.Combine(dir, $"{ts}_右原图_{nt}.jpg"), ImageFormat.Jpeg);
				}
			}
			catch (Exception ex) { Logger.Error($"正面工位存图异常: {ex.Message}"); }
		}

		/// <summary>获取当前班次: 00~08=晚班, 08~16=早班, 16~24=中班</summary>
		private string GetShift()
		{
			var n = DateTime.Now.TimeOfDay;
			if (n >= System.TimeSpan.Parse("00:00") && n <= System.TimeSpan.Parse("07:59")) return "晚班";
			if (n >= System.TimeSpan.Parse("08:00") && n <= System.TimeSpan.Parse("15:59")) return "早班";
			return "中班";
		}

		public void Dispose()
		{
			_imageSaver?.Dispose();
			_leftBuffer?.Dispose();
			_rightBuffer?.Dispose();
		}
	}
}
