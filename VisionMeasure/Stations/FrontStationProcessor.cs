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
using static CommonLib.Class_Config;
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
		private static readonly Regex PNumberRegex = new Regex(@"P\d{8}", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		private const int PNumberMinLength = 9;

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
		/// <summary>PLC结果发送事件: defectCodes数组 + pCount + okCount + ngCount</summary>
		public event Action<int[], int, long, long> OnPlcResult;
		/// <summary>最近一次的逐盒状态列表(供外部读取)</summary>
		public List<string> StatusList { get; private set; } = new List<string>();

		public float ConfThreshold { get; set; } = 0.5f;
		public float IouThreshold { get; set; } = 0.45f;
		public bool ReverseBoxOrder = false;
		public bool EnablePNumberCheck = false;
		public bool EnableBoxBreakCheck = true;
		public bool SkipCrop = false;

		private static readonly string _frp = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "Front_Error.log");
		private static void WFR(string m) { try { var d = System.IO.Path.GetDirectoryName(_frp); if (!System.IO.Directory.Exists(d)) System.IO.Directory.CreateDirectory(d); System.IO.File.AppendAllText(_frp, m + Environment.NewLine, System.Text.Encoding.UTF8); } catch { } }

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
		public void InitThresholdsFromModel()
		{
			if (_models.FrontBoxBreakModel != null)
			{
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

		/// <summary>相机1(正面左)图像回调 — 图像→Mat→配对缓冲→CheckAndProcessAsync</summary>
		public void OnCam1(Bitmap leftImg, object extraArg = null)
		{
			if (leftImg == null) return;
			Interlocked.Increment(ref _imgCount);
			Logger.Debug($"[Front] OnCam1 收到图像 {leftImg.Width}x{leftImg.Height}");
			lock (_syncLock)
			{
				_leftBuffer?.Dispose();
				_leftBuffer = leftImg.ToMat();
			}
			CheckAndProcessAsync();
		}

		/// <summary>相机2(正面右)图像回调 — 图像→Mat→配对缓冲→CheckAndProcessAsync</summary>
		public void OnCam2(Bitmap rightImg, object extraArg = null)
		{
			if (rightImg == null) return;
			Interlocked.Increment(ref _imgCount);
			Logger.Debug($"[Front] OnCam2 收到图像 {rightImg.Width}x{rightImg.Height}");
			lock (_syncLock)
			{
				_rightBuffer?.Dispose();
				_rightBuffer = rightImg.ToMat();
			}
			CheckAndProcessAsync();
		}

		/// <summary>		
		/// 配对+异步处理: 左右图都到达→裁图→2路并行推理→汇总→绘制→保存
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

				// 步骤0: 裁图 (与背面一致: LeftPx=左边界, RightPx=右边界, 直传不做翻转换算)
				leftProc = leftToProcess; rightProc = rightToProcess;
				if (_currentSku != null && !SkipCrop)
				{
					try
					{
						if (_currentSku.FrontLeft_LeftPx > 0 || _currentSku.FrontLeft_RightPx > 0)
						{
							int lPx = _currentSku.FrontLeft_LeftPx, rPx = _currentSku.FrontLeft_RightPx;
							leftProc = ImageHelper.CropImageHorizontallyCv2(leftToProcess, lPx, leftToProcess.Width - rPx);
							Logger.Info($"[Front] Camera1(正面左) SN={_Config.Camera1SN} 裁图: LeftPx={lPx} RightPx={rPx} 原图={leftToProcess.Width}x{leftToProcess.Height} → 裁后={leftProc.Width}x{leftProc.Height}");
						}
						else
						{
							Logger.Info($"[Front] Camera1(正面左) SN={_Config.Camera1SN} 裁图: 无需裁图 原图={leftToProcess.Width}x{leftToProcess.Height}");
						}
						if (_currentSku.FrontRight_LeftPx > 0 || _currentSku.FrontRight_RightPx > 0)
						{
							int lPx = _currentSku.FrontRight_LeftPx, rPx = _currentSku.FrontRight_RightPx;
							rightProc = ImageHelper.CropImageHorizontallyCv2(rightToProcess, lPx, rightToProcess.Width - rPx);
							Logger.Info($"[Front] Camera2(正面右) SN={_Config.Camera2SN} 裁图: LeftPx={lPx} RightPx={rPx} 原图={rightToProcess.Width}x{rightToProcess.Height} → 裁后={rightProc.Width}x{rightProc.Height}");
						}
						else
						{
							Logger.Info($"[Front] Camera2(正面右) SN={_Config.Camera2SN} 裁图: 无需裁图 原图={rightToProcess.Width}x{rightToProcess.Height}");
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
				StatusList = new List<string>(statusList); // 保存副本供PLC读取
				Logger.Info("[Front] 逐盒: [" + string.Join("] [", statusList) + "]");

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
				ModelPerfTracker.RecordPipeline("Front", 0, sw1.Elapsed.TotalMilliseconds, sw3.Elapsed.TotalMilliseconds, sw4.Elapsed.TotalMilliseconds, elapsed);

				// 步骤5: 发射结果事件(更新UI + PLC)
				OnResultReady?.Invoke(merged, ngArray, _okCount, _ngCount);
				OnPlcResult?.Invoke(null, pCount, _okCount, _ngCount); // OnPlcResult订阅方会从StatusList读取
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
		/// P号码OCR识别: 逐盒ROI裁剪 -> 传入单盒处理逻辑
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

				// 左半部分
				for (int i = 0; i < halfP; i++)
				{
					int sx = i * boxWL;
					int rw = (i < halfP - 1) ? boxWL : (wL - sx);
					int rh = hL - startYL;
					if (rw > 0 && rh > 0)
					{
						using (var roi = new Mat(left, new CvRect(sx, startYL, rw, rh)).Clone())
						{
							// 变更：将 pCount 传入 ProcessPNumberRoi
							ProcessPNumberRoi(roi, i, refPNumber, hasRef, wL, hL, sx, startYL, pCount, results);
						}
					}
				}

				// 右半部分
				for (int j = 0; j < halfP; j++)
				{
					int gi = halfP + j;
					int sx = j * boxWR;
					int rw = (j < halfP - 1) ? boxWR : (wR - sx);
					int rh = hR - startYR;
					if (rw > 0 && rh > 0)
					{
						using (var roi = new Mat(right, new CvRect(sx, startYR, rw, rh)).Clone())
						{
							// 变更：将 pCount 传入 ProcessPNumberRoi
							ProcessPNumberRoi(roi, gi, refPNumber, hasRef, wR, hR, sx, startYR, pCount, results);
						}
					}
				}
			}
			catch (Exception ex) { Logger.Error($"P号码识别异常: {ex.Message}"); }
			return results;
		}

		/// <summary>处理单盒P号ROI: ViMo OCR→遍历Blocks→PNumberRegex匹配→过滤碎片(长度<PNumberMinLength)→与参考比对→画框(OK绿/NG橙)</summary>
		/// <summary>
		/// 处理单盒P号ROI: 旋转90度 -> 动态截取底部百分比 -> ViMo OCR -> 正则匹配
		/// </summary>
		private void ProcessPNumberRoi(Mat roi, int boxIdx, string refPNumber, bool hasRef,
			int fullW, int fullH, int offsetX, int offsetY, int pCount, Dictionary<int, List<BoxDefect>> results)
		{
			ResponseList<OcrResponse> ocrResults;
			int ret = -1;

			// 1. 根据 pCount 决定裁剪比例 (取旋转后从下到上的比例)
			double cropRatio = 1.0; // 默认全取
			if (pCount == 8) cropRatio = 0.80;
			else if (pCount == 6) cropRatio = 0.55;
			else if (pCount == 4) cropRatio = 0.45;

			// 2. 图像预处理链：旋转 -> 裁剪
			using (Mat rotatedRoi = new Mat())
			{
				// 向右（顺时针）旋转 90 度以匹配 OCR 训练特征
				Cv2.Rotate(roi, rotatedRoi, RotateFlags.Rotate90Clockwise);

				// 计算裁剪区域
				int rotatedW = rotatedRoi.Width;
				int rotatedH = rotatedRoi.Height;
				int cropH = (int)(rotatedH * cropRatio);
				int startY = rotatedH - cropH; // 从下到上截取，计算起始Y坐标

				// 截取目标区域并送入 OCR
				using (Mat finalRoi = new Mat(rotatedRoi, new CvRect(0, startY, rotatedW, cropH)).Clone())
				{
					ret = _models.FrontOcrModel.Run(finalRoi, out ocrResults);
				}
			}

			// 3. 处理 OCR 推理失败或空结果的情况
			if (ret != 0 || ocrResults == null || ocrResults.Count == 0)
			{
				if (hasRef && EnablePNumberCheck)
				{
					// "P号缺少"框覆盖整个ROI区域（原图归一化坐标）
					AddDefect(results, boxIdx, "P号缺少", new float[] {
						(float)offsetX / fullW,
						(float)offsetY / fullH,
						(float)(offsetX + roi.Width) / fullW,
						(float)(offsetY + roi.Height) / fullH });
				}
				return;
			}

			bool foundAny = false;
			int roiW = roi.Width, roiH = roi.Height;

			// 4. 解析结果
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

					// 完整逆变换: finalRoi → 去裁剪 → 逆时针90° → 去ROI偏移 → 原图归一化坐标
					float[] normBox = ComputeNormBBox(block, fullW, fullH, offsetX, offsetY, roiW, roiH, cropRatio);
					bool isMatch = (pNum == refPNumber);

					if (EnablePNumberCheck && hasRef && !isMatch)
					{
						AddDefect(results, boxIdx, $"P号错误:{pNum}", normBox);
					}
					else
					{
						AddDefect(results, boxIdx, pNum, normBox);
						Logger.Debug($"[Front] P号盒{boxIdx + 1}: 识别={pNum}" + (isMatch ? " OK" : ""));
					}
				}
			}

			if (!foundAny && hasRef && EnablePNumberCheck)
			{
				AddDefect(results, boxIdx, "P号缺少", new float[] {
					(float)offsetX / fullW,
					(float)offsetY / fullH,
					(float)(offsetX + roiW) / fullW,
					(float)(offsetY + roiH) / fullH });
			}
		}

		/// <summary>
		/// 计算归一化包围框: OCR返回的finalRoi坐标 → 逆变换(去裁剪+逆时针90°+去ROI偏移) → 原图归一化坐标
		/// 变换链: 原图 → ROI(竖条) → 顺时针90° → 底部裁剪 → OCR
		/// 逆变换: OCR(finalRoi) → 去裁剪 → 逆时针90° → 去ROI偏移 → 原图
		/// </summary>
		private float[] ComputeNormBBox(TextBlock block, int fullW, int fullH, int offsetX, int offsetY,
			int roiW, int roiH, double cropRatio)
		{
			if (block.Polygon == null || !block.Polygon.Any()) return new float[] { 0, 0, 0.1f, 0.1f };

			int cropH = (int)(roiW * cropRatio); // 裁剪高度 = 旋转后高度(即roi宽度) × 裁剪比例

			float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
			foreach (var pt in block.Polygon)
			{
				// pt 在 finalRoi 坐标系中 (fx, fy)
				float fx = pt.X, fy = pt.Y;

				// 逆变换:
				// 1. 去裁剪 + 逆时针90°：ROI坐标 rx = fy + roiW - cropH, ry = roiH - fx
				// 2. 去ROI偏移：原图 gx = offsetX + rx, gy = offsetY + ry
				float gx = offsetX + fy + (roiW - cropH);
				float gy = offsetY + roiH - fx;

				if (gx < minX) minX = gx;
				if (gy < minY) minY = gy;
				if (gx > maxX) maxX = gx;
				if (gy > maxY) maxY = gy;
			}
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
							var bbSw = System.Diagnostics.Stopwatch.StartNew();
							var result = _models.FrontBoxBreakModel.Predict(patch, ConfThreshold, IouThreshold);
							ModelPerfTracker.Record("Front", "盒子破损", bbSw.Elapsed.TotalMilliseconds);

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
				Logger.Info($"[Front BatchLog] ▶ 盒子破推理: batch=1 逐张Predict, 左3×2={3 * 2}patch 右3×2={3 * 2}patch (P={pCount})");
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

		/// <summary>绘制+合并: 左右图并行渲染→MergeImages(水平拼接+OK/NG大字)</summary>
		private Bitmap DrawAndMergeResults(Mat left, Mat right,
			Dictionary<int, List<BoxDefect>> pNumberResults, Dictionary<int, List<BoxDefect>> damageResults,
			List<string> statusList, int halfP, bool isOk)
		{
			var lb = left.ToBitmap(); var rb = right.ToBitmap();
			int p = _currentSku?.P ?? 8;
			// 左右图独立Bitmap, Graphics无共享, GDI+安全并行
			System.Threading.Tasks.Parallel.Invoke(
				() => { using (var g = Graphics.FromImage(lb)) { g.SmoothingMode = SmoothingMode.AntiAlias; DrawDefects(g, pNumberResults, damageResults, statusList, 0, halfP, lb.Width, lb.Height); } },
				() => { using (var g = Graphics.FromImage(rb)) { g.SmoothingMode = SmoothingMode.AntiAlias; DrawDefects(g, pNumberResults, damageResults, statusList, halfP, p, rb.Width, rb.Height); } }
			);
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
							Color c = isPng ? Color.Orange : Color.Lime;
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
					int maxLen = Math.Min(s.Length, 4);
					string disp = s == "OK" ? "OK" : s.Substring(0, maxLen);
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

		/// <summary>绘制单个缺陷框(半透明填充+实线边框+分数标签) — 支持P号(绿/橙)和破损(红)两种颜色，坐标自动裁剪到图像边界内</summary>
		private void DrawDefectBox(Graphics g, BoxDefect defect, int imgWidth, int imgHeight, Color baseColor)
		{
			if (defect.BoundingBox == null || defect.BoundingBox.Length < 4) return;

			// 计算像素坐标并裁剪到图像边界内
			int x1 = Math.Max(0, Math.Min((int)(defect.BoundingBox[0] * imgWidth), imgWidth - 1));
			int y1 = Math.Max(0, Math.Min((int)(defect.BoundingBox[1] * imgHeight), imgHeight - 1));
			int x2 = Math.Max(0, Math.Min((int)(defect.BoundingBox[2] * imgWidth), imgWidth - 1));
			int y2 = Math.Max(0, Math.Min((int)(defect.BoundingBox[3] * imgHeight), imgHeight - 1));
			if (x2 <= x1 || y2 <= y1) return;
			var rc = new Rectangle(x1, y1, x2 - x1, y2 - y1);

			using (var fill = new SolidBrush(Color.FromArgb(30, baseColor))) g.FillRectangle(fill, rc);
			using (var pn = new Pen(baseColor, 3)) g.DrawRectangle(pn, rc);

			string label = defect.DefectType;
			if (defect.Score > 0 && defect.Score < 1.0f && !label.StartsWith("P号") && !label.StartsWith("P"))
				label = label + " " + defect.Score.ToString("F2");
			if (label.StartsWith("P号错误")) { /* shown as-is */ }
			if (label.Length > 20) label = label.Substring(0, 20);
			using (var f = new Font("微软雅黑", 14, FontStyle.Bold))
			{
				var sz = g.MeasureString(label, f);

				// Y: 框上方优先，不够则放框下方
				int ly = y1 - (int)sz.Height - 8;
				if (ly < 4) ly = y2 + 4;

				// X: 左对齐框，右侧溢出则左移
				int lx = x1;
				int textW = (int)sz.Width + 8;
				if (lx + textW > imgWidth)
					lx = imgWidth - textW - 2;
				if (lx < 0) lx = 2;

				using (var bg = new SolidBrush(baseColor)) g.FillRectangle(bg, lx, ly - 2, textW, sz.Height + 6);
				g.DrawString(label, f, Brushes.White, lx + 2, ly + 1);
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
