using Config;
using Models;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VisionMeasure.Utils;
using CommonLib;
using SmartMore.ViMo;
using YoloInference;
using YoloSegmentationEnd2End;
using ZXing;
using ZXing.Common;
using AI;
using CvRect = OpenCvSharp.Rect;
using CvPoint = OpenCvSharp.Point;
using CvSize = OpenCvSharp.Size;
using CvScalar = OpenCvSharp.Scalar;
using Rect = System.Drawing.Rectangle;
using static CommonLib.Class_Config;

namespace Stations
{
	public class BackStationProcessor : IDisposable
	{
		private readonly AiModelManager _models;
		private readonly string _savePath;
		private SkuData _sku;
		private readonly HighSpeedImageSaver _imageSaver;
		private readonly PerformanceMonitor _perfMonitor;
		private Mat _leftBuffer, _rightBuffer;
		private readonly object _syncLock = new object();
		private long _totalCount, _okCount, _ngCount;
		private bool _lastIsOk = true;
		private bool _disposed;
		private Config.ModelParams _barcodeParams;

		public event Action<ProductResult> OnResultReady;
		public event Action<List<string>, int> OnStatusUpdate;
		public float ConfThreshold = 0.5f, IouThreshold = 0.2f;
		public float HookThicknessThreshold = 30f;
		public int BlueAreaClassId = 0, HangHoleClassId = 1;
		public bool ReverseBoxOrder = false;
		public bool EnableDateCodeCheck = false;

		public BackStationProcessor(AiModelManager models, string savePath, SkuData sku,
			HighSpeedImageSaver imageSaver, PerformanceMonitor perfMonitor)
		{ _models = models; _savePath = savePath; _sku = sku; _imageSaver = imageSaver; _perfMonitor = perfMonitor; _barcodeParams = Config.ModelParams.Load("barcode"); }

		public void UpdateSku(SkuData sku) { _sku = sku; }
		public long TotalCount => _totalCount;
		public long OkCount => _okCount;
		public long NgCount => _ngCount;

		public void OnCam3(Bitmap bmp, long pid)
		{
			if (bmp == null) return;
			Logger.Debug("[Back] OnCam3(左) " + bmp.Width + "x" + bmp.Height);
			lock (_syncLock) { _leftBuffer?.Dispose(); _leftBuffer = bmp.ToMat(); }
			CheckAndProcess();
		}

		public void OnCam4(Bitmap bmp, long pid)
		{
			if (bmp == null) return;
			Logger.Debug("[Back] OnCam4(右) " + bmp.Width + "x" + bmp.Height);
			lock (_syncLock) { _rightBuffer?.Dispose(); _rightBuffer = bmp.ToMat(); }
			CheckAndProcess();
		}

		private async void CheckAndProcess()
		{
			Mat l = null, r = null;
			lock (_syncLock) { if (_leftBuffer != null && _rightBuffer != null) { l = _leftBuffer; r = _rightBuffer; _leftBuffer = null; _rightBuffer = null; } }
			if (l == null || r == null) return;
			Logger.Debug("[Back] 配对成功");
			var sw = System.Diagnostics.Stopwatch.StartNew();
			try { await Task.Run(() => Process(l, r)); Logger.Info("[Back] 完成 总耗时=" + sw.Elapsed.TotalMilliseconds.ToString("F1") + "ms"); }
			catch (Exception ex) { Logger.Error("[Back] 异常: " + ex.Message); }
			finally { l?.Dispose(); r?.Dispose(); }
		}

		public void Start() { Logger.Info("背面工位已启动"); }
		public void Stop() { }

		private void Process(Mat leftMat, Mat rightMat)
		{
			long pid = DateTime.Now.Ticks;
			var sw = System.Diagnostics.Stopwatch.StartNew();
			var result = new ProductResult { ProductId = pid, CreateTime = DateTime.Now };
			int p = _sku.P, hp = p / 2;
			var status = new List<string>(p); for (int i = 0; i < p; i++) status.Add("OK");

			try
			{
				Logger.Info("[Back] ====== 开始 P=" + p + " " + leftMat.Width + "x" + leftMat.Height + " ======");

				// 步骤0: 裁图
				Mat leftProc = leftMat, rightProc = rightMat;
				if (_sku.BackLeft_LeftPx > 0 || _sku.BackLeft_RightPx > 0)
				{
					leftProc = ImageHelper.CropImageHorizontallyCv2(leftMat, _sku.BackLeft_LeftPx, leftMat.Width - _sku.BackLeft_RightPx);
					Logger.Debug("[Back] 左图裁图: 保留" + _sku.BackLeft_LeftPx + "~" + _sku.BackLeft_RightPx + " -> " + leftProc.Width + "x" + leftProc.Height);
				}
				if (_sku.BackRight_LeftPx > 0 || _sku.BackRight_RightPx > 0)
				{
					rightProc = ImageHelper.CropImageHorizontallyCv2(rightMat, _sku.BackRight_LeftPx, rightMat.Width - _sku.BackRight_RightPx);
					Logger.Debug("[Back] 右图裁图: 保留" + _sku.BackRight_LeftPx + "~" + _sku.BackRight_RightPx + " -> " + rightProc.Width + "x" + rightProc.Height);
				}

				// 步骤1: 并行推理
				Logger.Debug("[Back] 步骤1: 推理...");
				var sw1 = System.Diagnostics.Stopwatch.StartNew();
				Dictionary<int, List<BoxDefect>> barcodeDict = null, dateCodeDict = null, hookDict = null;
				var tasks = new List<Task>();
				tasks.Add(Task.Run(() => { barcodeDict = RecognizeBarcodes(leftProc, rightProc, hp); }));
				if (EnableDateCodeCheck && _models.BackDateCodeSegModel != null && _models.BackDateCodeClsModel != null && _models.BackDateCodeOcrModel != null)
					tasks.Add(Task.Run(() => { dateCodeDict = RecognizeDateCodes(leftProc, rightProc, hp); }));
				tasks.Add(Task.Run(() => { hookDict = DetectHookDamage(leftProc, rightProc, p); }));
				Task.WaitAll(tasks.ToArray());
				var inferMs = sw1.Elapsed.TotalMilliseconds;
				Logger.Info("[Back] 步骤1完成: 推理=" + inferMs.ToString("F1") + "ms");

				// 步骤2: 汇总
				var all = new List<BoxDefect>();
				int bc = 0, ho = 0, hs = 0, dc = 0;
				if (barcodeDict != null) { var its = barcodeDict.Values.SelectMany(v => v).ToList(); all.AddRange(its); bc = its.Count(d => !d.DefectType.StartsWith("条码:")); }
				if (dateCodeDict != null) { var its = dateCodeDict.Values.SelectMany(v => v).ToList(); all.AddRange(its); dc = its.Count(d => !d.DefectType.StartsWith("日期:") && !d.DefectType.StartsWith("双排:")); }
				if (hookDict != null) { var its = hookDict.Values.SelectMany(v => v).ToList(); all.AddRange(its); ho = its.Count(d => d.DefectType == "挂钩明显错位"); hs = its.Count(d => d.DefectType == "轻微挂钩错位"); }
				Logger.Info("[Back] 步骤2汇总: 条形码=" + bc + " 日期码=" + dc + " 明显=" + ho + " 轻微=" + hs + " 总计=" + all.Count);
				foreach (var d in all) if (d.BoxIndex >= 0 && d.BoxIndex < status.Count) status[d.BoxIndex] = d.DefectType;
				for (int i = 0; i < status.Count; i++) Logger.Info("[Back]   盒" + (i + 1) + ": " + status[i]);
				bool isOk = status.All(s => s == "OK");
				result.BackResult = isOk;
				result.BackDefects = status.Where(s => s != "OK").Distinct().ToList();
				Interlocked.Increment(ref _totalCount);
				if (isOk) Interlocked.Increment(ref _okCount); else Interlocked.Increment(ref _ngCount);
				_lastIsOk = isOk;

				// 步骤3: 绘制+合并
				Logger.Debug("[Back] 步骤3: 绘制+合并...");
				var sw3 = System.Diagnostics.Stopwatch.StartNew();
				var lr = DrawResult(leftProc, all.Where(d => d.BoxIndex < hp).ToList(), status, 0, hp);
				var rr = DrawResult(rightProc, all.Where(d => d.BoxIndex >= hp).ToList(), status, hp, p);
				var merged = MergeImages(lr, rr);
				result.BackRenderImage = merged;
				var drawMs = sw3.Elapsed.TotalMilliseconds;
				Logger.Info("[Back] 步骤3完成: " + drawMs.ToString("F1") + "ms " + merged.Width + "x" + merged.Height);

				// 步骤4: 保存
				var sw4 = System.Diagnostics.Stopwatch.StartNew();
				SaveImages(leftProc.ToBitmap(), rightProc.ToBitmap(), merged, pid, isOk, status);
				var saveMs = sw4.Elapsed.TotalMilliseconds;
				Logger.Info("[Back] 步骤4完成: 保存=" + saveMs.ToString("F1") + "ms");

				var total = sw.Elapsed.TotalMilliseconds;
				_perfMonitor?.Record(new PerformanceMonitor.PerformanceRecord
				{
					Timestamp = DateTime.Now, Station = "Back", ProductId = pid,
					InferenceTimeMs = inferMs, DrawTimeMs = drawMs, SaveTimeMs = saveMs, TotalTimeMs = total, Result = isOk
				});
				Logger.Info("[Back] ====== 完成: 总=" + total.ToString("F1") + "ms 结果=" + (isOk ? "OK" : "NG") + " ======");
				OnResultReady?.Invoke(result);
				OnStatusUpdate?.Invoke(status, p);
			}
			catch (Exception ex)
			{
				Logger.Error("[Back] 异常 Pid=" + pid + ": " + ex.Message);
				result.BackResult = false;
				OnResultReady?.Invoke(result);
			}
		}

		// ====== 条形码识别 (ZXing.Net, 逐盒ROI, 无图像预处理仅灰度) ======
		private Dictionary<int, List<BoxDefect>> RecognizeBarcodes(Mat left, Mat right, int hp)
		{
			var r = new Dictionary<int, List<BoxDefect>>();
			string refBarcode = _sku?.BackBarcode;
			if (string.IsNullOrEmpty(refBarcode)) return r;
			try
			{
				int hL = left.Height, wL = left.Width, hR = right.Height, wR = right.Width;
				double syRatio = (_barcodeParams != null) ? _barcodeParams.BcStartHeightRatio : (2.0 / 3.0);
				int bwL = wL / hp, bwR = wR / hp, syL = (int)(hL * syRatio), syR = (int)(hR * syRatio);
				Logger.Debug("[Back] 条码ZXing: 左" + wL + "x" + hL + " boxW=" + bwL);

				for (int i = 0; i < hp; i++)
				{
					int sx = i * bwL, rw = (i < hp - 1) ? bwL : (wL - sx), rh = hL - syL;
					if (rw <= 0 || rh <= 0) continue;
					using (var roi = new Mat(left, new CvRect(sx, syL, rw, rh)).Clone())
					{
						var def = DecodeBarcodeZxing(roi, refBarcode, sx, syL, wL, hL, i);
						if (def != null) { if (!r.ContainsKey(i)) r[i] = new List<BoxDefect>(); r[i].Add(def); }
					}
				}

				for (int j = 0; j < hp; j++)
				{
					int gi = hp + j, sx = j * bwR, rw = (j < hp - 1) ? bwR : (wR - sx), rh = hR - syR;
					if (rw <= 0 || rh <= 0) continue;
					using (var roi = new Mat(right, new CvRect(sx, syR, rw, rh)).Clone())
					{
						var def = DecodeBarcodeZxing(roi, refBarcode, sx, syR, wR, hR, gi);
						if (def != null) { if (!r.ContainsKey(gi)) r[gi] = new List<BoxDefect>(); r[gi].Add(def); }
					}
				}
				Logger.Debug("[Back] 条码: " + r.Count + "盒识别");
			}
			catch (Exception ex) { Logger.Error("条码异常: " + ex.Message); }
			return r;
		}

		private BoxDefect DecodeBarcodeZxing(Mat roi, string refBarcode, int ox, int oy, int fw, int fh, int boxIdx)
		{
			try
			{
				var p = _barcodeParams ?? Config.ModelParams.Load("barcode");
				Mat proc = ApplyBarcodePreprocess(roi, p);
				using (proc)
				using (var bmp = proc.ToBitmap())
				{
					var reader = new BarcodeReader
					{
						AutoRotate = true,
						Options = new DecodingOptions
						{
							TryHarder = p.BcTryHarder,
							PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.CODE_128, BarcodeFormat.EAN_13 }
						}
					};
					var results = reader.DecodeMultiple(bmp);
					float[] defBox = new float[] { 0.05f, (float)oy / fh, 0.95f, (float)(oy + 0.5f * roi.Height) / fh };

					if (results == null || results.Length == 0)
						return new BoxDefect(boxIdx, "条码缺少", defBox);

					string bestText = null;
					ResultPoint[] bestPts = null;
					if (results.Length == 1) { bestText = results[0].Text; bestPts = results[0].ResultPoints; }
					else
					{
						if (results.Any(res => res.Text == refBarcode))
						{ bestText = refBarcode; bestPts = results.First(res => res.Text == refBarcode).ResultPoints; }
						else
						{
							int bestDist = int.MaxValue;
							foreach (var res in results)
							{
								if (string.IsNullOrEmpty(res.Text)) continue;
								int dist = LevenshteinDistance(res.Text, refBarcode);
								if (dist < bestDist) { bestDist = dist; bestText = res.Text; bestPts = res.ResultPoints; }
							}
						}
					}

					float[] normBox = defBox;
					if (bestPts != null && bestPts.Length >= 2)
					{
						float mx = float.MaxValue, my = float.MaxValue, Mx = float.MinValue, My = float.MinValue;
						foreach (var pt in bestPts) { float gx = pt.X + ox, gy = pt.Y + oy; if (gx < mx) mx = gx; if (gy < my) my = gy; if (gx > Mx) Mx = gx; if (gy > My) My = gy; }
						normBox = new float[] { mx / fw, my / fh, Mx / fw, My / fh };
					}

					Logger.Debug("[Back] 条码盒" + (boxIdx + 1) + ": 识=" + (bestText ?? "(空)") + " 标=" + refBarcode + " " + (bestText == refBarcode ? "OK" : "NG"));
					if (bestText == refBarcode)
						return new BoxDefect(boxIdx, "条码:" + bestText, normBox);
					return new BoxDefect(boxIdx, "条码错误(识:" + bestText + "/标:" + refBarcode + ")", normBox);
				}
			}
			catch { return new BoxDefect(boxIdx, "条码缺少", new float[] { 0.05f, (float)oy / fh, 0.95f, (float)(oy + roi.Height * 0.5f) / fh }); }
		}

		private static Mat ApplyBarcodePreprocess(Mat src, Config.ModelParams p)
		{
			if (!p.BcEnablePreprocess) { var g2 = new Mat(); Cv2.CvtColor(src, g2, ColorConversionCodes.BGR2GRAY); return g2; }
			Mat m = src.Clone();
			if (Math.Abs(p.BcContrastAlpha - 1.0f) > 0.001f || p.BcBrightnessBeta != 0) { var t = new Mat(); m.ConvertTo(t, -1, p.BcContrastAlpha, p.BcBrightnessBeta); m.Dispose(); m = t; }
			if (m.Channels() != 1) { var g2 = new Mat(); var cc = m.Channels() == 3 ? ColorConversionCodes.BGR2GRAY : ColorConversionCodes.BGRA2GRAY; Cv2.CvtColor(m, g2, cc); m.Dispose(); m = g2; }
			if (p.BcEnableEqualizeHist) { var e = new Mat(); Cv2.EqualizeHist(m, e); m.Dispose(); m = e; }
			if (p.BcEnableGaussianBlur) { var b = new Mat(); Cv2.GaussianBlur(m, b, new OpenCvSharp.Size(5, 5), 0); m.Dispose(); m = b; }
			if (p.BcEnableMedianBlur) { var b = new Mat(); Cv2.MedianBlur(m, b, 5); m.Dispose(); m = b; }
			int tm = p.BcThresholdMode;
			if (tm == 1) { int bs = p.BcAdaptiveBlockSize; if (bs % 2 == 0) bs++; var t = new Mat(); Cv2.AdaptiveThreshold(m, t, 255, AdaptiveThresholdTypes.MeanC, ThresholdTypes.Binary, bs, p.BcAdaptiveC); m.Dispose(); m = t; }
			else if (tm == 2) { var t = new Mat(); Cv2.Threshold(m, t, 0, 255, ThresholdTypes.Otsu | ThresholdTypes.Binary); m.Dispose(); m = t; }
			else if (tm == 3) { var t = new Mat(); Cv2.Threshold(m, t, p.BcFixedThreshold, 255, ThresholdTypes.Binary); m.Dispose(); m = t; }
			if (p.BcEnableInvert) { var t = new Mat(); Cv2.BitwiseNot(m, t); m.Dispose(); m = t; }
			var k = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(3, 3));
			if (p.BcEnableMorphClose) { var t = new Mat(); Cv2.MorphologyEx(m, t, MorphTypes.Close, k); m.Dispose(); m = t; }
			if (p.BcEnableMorphOpen) { var t = new Mat(); Cv2.MorphologyEx(m, t, MorphTypes.Open, k); m.Dispose(); m = t; }
			if (p.BcEnableMorphDilate) { var t = new Mat(); Cv2.MorphologyEx(m, t, MorphTypes.Dilate, k); m.Dispose(); m = t; }
			if (p.BcEnableMorphErode) { var t = new Mat(); Cv2.MorphologyEx(m, t, MorphTypes.Erode, k); m.Dispose(); m = t; }
			k.Dispose();
			return m;
		}

		private static int LevenshteinDistance(string a, string b)
		{
			if (string.IsNullOrEmpty(a)) return b == null ? 0 : b.Length;
			if (string.IsNullOrEmpty(b)) return a.Length;
			int la = a.Length, lb = b.Length;
			int[,] dp = new int[la + 1, lb + 1];
			for (int i = 0; i <= la; i++) dp[i, 0] = i;
			for (int j = 0; j <= lb; j++) dp[0, j] = j;
			for (int i = 1; i <= la; i++)
				for (int j = 1; j <= lb; j++)
					dp[i, j] = Math.Min(Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1), dp[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
			return dp[la, lb];
		}

		// ====== 日期码识别 (C1分割+C2分类+C3 OCR, 合并左右图后推理) ======
		private static readonly Regex MFG_RX = new Regex(@"MFG\s*(\d{2}/\d{2}/\d{4})", RegexOptions.IgnoreCase);
		private static readonly Regex LOT_RX = new Regex(@"L[0O]T\s*(\d{4}/\d{2}/\d{2})", RegexOptions.IgnoreCase);
		private static readonly Regex EXP_RX = new Regex(@"EXP\s*(\d{2}/\d{2}/\d{4})", RegexOptions.IgnoreCase);

		private Dictionary<int, List<BoxDefect>> RecognizeDateCodes(Mat left, Mat right, int hp)
		{
			var r = new Dictionary<int, List<BoxDefect>>();
			string codingFormat = _sku?.CodingFormat ?? "";
			try
			{
				int p = hp * 2;
				using (Mat merged = new Mat())
				{
					Cv2.HConcat(left, right, merged);
					r = ProcessDateCodeFull(merged, codingFormat, p);
				}
				Logger.Debug("[Back] 日期码: " + r.Count + "盒识别");
			}
			catch (Exception ex) { Logger.Error("日期码异常: " + ex.Message); }
			return r;
		}

		/// <summary>三步流水线: C1全图分割→C2重影分类→C3 OCR (参考AIRunThread.cs)</summary>
		private Dictionary<int, List<BoxDefect>> ProcessDateCodeFull(Mat img, string codingFormat, int p)
		{
			var r = new Dictionary<int, List<BoxDefect>>();
			int fw = img.Width, fh = img.Height, halfW = fw / 2, boxW = fw / p;
		int hp2 = p / 2;

			try
			{
				// C1: 分割模型全图推理 → 从Mask提取连通域
				var swC1 = System.Diagnostics.Stopwatch.StartNew();
				ResponseList<SegmentationResponse> segRsp;
				int segRet = _models.BackDateCodeSegModel.Run(img, out segRsp);
				int rspCount = segRsp?.Count ?? 0;
				Logger.Debug("[Back] C1分割: ret=" + segRet + " rsp=" + rspCount + " " + swC1.Elapsed.TotalMilliseconds.ToString("F0") + "ms");
				if (segRet != 0 || rspCount == 0) return r;

				var regions = new List<CvRect>();
				foreach (var item in segRsp)
				{
					var mask = item.Item2.Mask;
					if (mask == null || mask.Empty()) continue;
					int nz = Cv2.CountNonZero(mask);
					Logger.Debug("[Back] Mask: " + mask.Width + "x" + mask.Height + " nz=" + nz);
					using (Mat mc = mask.Clone())
					{
						Mat labels = new Mat(), stats = new Mat(), centroids = new Mat();
						int nLabels = Cv2.ConnectedComponentsWithStats(mc, labels, stats, centroids, PixelConnectivity.Connectivity8);
						for (int k = 1; k < nLabels; k++)
						{
							int sx = stats.At<int>(k, 0), sy = stats.At<int>(k, 1);
							int sw2 = stats.At<int>(k, 2), sh = stats.At<int>(k, 3);
							if (sw2 > 5 && sh > 5) regions.Add(new CvRect(sx, sy, sw2, sh));
						}
						labels.Dispose(); stats.Dispose(); centroids.Dispose();
					}
				}
				Logger.Debug("[Back] C1区域数: " + regions.Count);

				// C2+C3: 逐区域处理
				foreach (var rect in regions)
				{
					int cx = rect.X + rect.Width / 2;
					int boxIdx = cx / boxW;
					if (boxIdx < 0) boxIdx = 0;
					if (boxIdx >= p) boxIdx = p - 1;

					int mx = Math.Max(0, rect.X - 5), my = Math.Max(0, rect.Y - 5);
					int mw = Math.Min(fw - mx, rect.Width + 10), mh = Math.Min(fh - my, rect.Height + 10);

					// C2: 重影分类
					using (var cropC2 = new Mat(img, new CvRect(mx, my, mw, mh)).Clone())
					{
						ResponseList<ClassificationResponse> clsRsp;
						int clsRet = _models.BackDateCodeClsModel.Run(cropC2, out clsRsp);
						bool c2Shadow = false;
						if (clsRet == 0 && clsRsp != null && clsRsp.Count > 0)
						{
							foreach (var ci in clsRsp)
							{
								if (ci.Item2.Labels.Any())
								{
									var topLabel = ci.Item2.Labels.First();
									if (topLabel.Label == "重影" || topLabel.Score > 0.8f) { c2Shadow = true; break; }
								}
							}
						}

						// C3: OCR
						using (var cropC3 = new Mat(img, new CvRect(mx, my, mw, mh)).Clone())
						{
							ResponseList<OcrResponse> ocrRsp;
							int ocrRet = _models.BackDateCodeOcrModel.Run(cropC3, out ocrRsp);
							if (ocrRet != 0 || ocrRsp == null || ocrRsp.Count == 0) continue;

							var texts = new List<string>();
							foreach (var rt in ocrRsp)
							{
								if (rt.Item2.Blocks == null) continue;
								foreach (var blk in rt.Item2.Blocks)
									if (!string.IsNullOrWhiteSpace(blk.Label)) texts.Add(blk.Label);
							}
							if (c2Shadow) { if (!r.ContainsKey(boxIdx)) r[boxIdx] = new List<BoxDefect>(); r[boxIdx].Add(new BoxDefect(boxIdx, "日期码重影", new float[] { (float)(mx - (boxIdx < hp2 ? 0 : halfW)) / halfW, (float)my / fh, (float)(mx + mw - (boxIdx < hp2 ? 0 : halfW)) / halfW, (float)(my + mh) / fh })); }
							if (texts.Count == 0) continue;

							string allText = string.Join(" ", texts);
							Logger.Debug("[Back] 日期码盒" + (boxIdx + 1) + ": " + allText);
							float[] normBox = new float[] { (float)(mx - (boxIdx < hp2 ? 0 : halfW)) / halfW, (float)my / fh, (float)(mx + mw - (boxIdx < hp2 ? 0 : halfW)) / halfW, (float)(my + mh) / fh };

							int result;
							if (codingFormat.Contains("MFG") && !codingFormat.Contains("双排")) result = CheckMFG(allText);
							else if (codingFormat.Contains("LOT")) result = CheckLOT(allText);
							else if (codingFormat.Contains("双排")) result = CheckDoubleRow(texts);
							else result = 0;

							if (!r.ContainsKey(boxIdx)) r[boxIdx] = new List<BoxDefect>();
							string label;
							if (result == 0)
								label = codingFormat.Contains("双排") ? "双排:" + allText : "日期:" + allText;
							else
								label = result == 1 ? "日期码错误(" + allText + ")" : "日期码不完全正确(" + allText + ")";
							r[boxIdx].Add(new BoxDefect(boxIdx, label, normBox));
						}
					}
				}
			}
			catch (Exception ex) { Logger.Error("日期码处理异常: " + ex.Message); }
			return r;
		}

		private int CheckMFG(string text) { var m = MFG_RX.Match(text); if (!m.Success) return 1; if (DateTime.TryParseExact(m.Groups[1].Value, "dd/MM/yyyy", null, System.Globalization.DateTimeStyles.None, out DateTime dt)) return dt.Date == DateTime.Now.Date ? 0 : 2; return 2; }
		private int CheckLOT(string text) { var m = LOT_RX.Match(text); if (!m.Success) return 1; if (DateTime.TryParseExact(m.Groups[1].Value, "yyyy/MM/dd", null, System.Globalization.DateTimeStyles.None, out DateTime dt)) return dt.Date == DateTime.Now.Date ? 0 : 2; return 2; }
		private int CheckDoubleRow(List<string> lines) { if (lines.Count < 2) return 1; string mfgLine = null, expLine = null; foreach (var line in lines) { string s3 = line.Length >= 3 ? line.Substring(0, 3) : line; if (mfgLine == null && Regex.IsMatch(s3, "[MFG]")) mfgLine = line; if (expLine == null && Regex.IsMatch(s3, "[EXP]")) expLine = line; } if (mfgLine == null || expLine == null) return 1; int mfgR = CheckMFG(mfgLine); return mfgR != 0 ? mfgR : CheckEXP(expLine); }
		private int CheckEXP(string text) { var m = EXP_RX.Match(text); if (!m.Success) return 1; if (DateTime.TryParseExact(m.Groups[1].Value, "dd/MM/yyyy", null, System.Globalization.DateTimeStyles.None, out DateTime dt)) return dt.Date == DateTime.Now.AddYears(10).Date ? 0 : 2; return 2; }

		// ====== 挂钩缺陷检测 (原有代码不变) ======
		private Dictionary<int, List<BoxDefect>> DetectHookDamage(Mat left, Mat right, int p)
		{
			var results = new Dictionary<int, List<BoxDefect>>();
			if (_models.BackHookModel == null) return results;
			try
			{
				var images = new List<Mat> { left, right };
				double[] offsets = { 0.0, p / 2.0 };
				var batchResults = _models.BackHookModel.PredictBatch(images, ConfThreshold, IouThreshold);
				Logger.Debug("[Back] 挂钩批量推理: " + (batchResults?.Count ?? 0) + "张");

				for (int i = 0; i < (batchResults?.Count ?? 0); i++)
				{
					var detResult = batchResults[i];
					if (detResult?.Boxes == null || detResult.Boxes.Length == 0) continue;
					Mat curImg = images[i];
					int imgH = curImg.Height, imgW = curImg.Width;

					for (int j = 0; j < detResult.Boxes.Length; j++)
					{
						int cls = detResult.ClassIds[j];
						var bboxN = detResult.BoxesN[j];
						var bbox = detResult.Boxes[j];
						double cxN = bboxN.X + bboxN.Width / 2.0;
						int gi = (int)(cxN * (p / 2.0) + offsets[i]);
						gi = Math.Max(0, Math.Min(gi, p - 1));

						if (cls == 1)
						{
							AddDefect(results, gi, "挂钩明显错位",
								new float[] { (float)bboxN.X, (float)bboxN.Y, (float)(bboxN.X + bboxN.Width), (float)(bboxN.Y + bboxN.Height) });
						}
						else if (cls == 0)
						{
							if (results.ContainsKey(gi) && results[gi].Any(d => d.DefectType == "挂钩明显错位"))
								continue;
							int x1 = Math.Max(0, bbox.X), y1 = Math.Max(0, bbox.Y);
							int x2 = Math.Min(imgW, bbox.X + bbox.Width), y2 = Math.Min(imgH, bbox.Y + bbox.Height);
							if (x2 <= x1 || y2 <= y1) continue;
							using (Mat cropImg = new Mat(curImg, new CvRect(x1, y1, x2 - x1, y2 - y1)))
							{
								if (cropImg.Empty()) continue;
								var segR = _models.HookSlightModel.Predict(cropImg);
								if (segR?.Masks == null) continue;
								CvPoint[] inner = null, outer = null;
								for (int m = 0; m < segR.ClassIds.Length; m++)
								{
									var pts = segR.Masks[m].Select(pt => new CvPoint((int)Math.Round(pt.X), (int)Math.Round(pt.Y))).ToArray();
									if (segR.ClassIds[m] == BlueAreaClassId) inner = pts;
									else if (segR.ClassIds[m] == HangHoleClassId) outer = pts;
								}
								if (inner != null && outer != null && inner.Length > 0 && outer.Length > 0)
								{
									var thick = CalcThickness(cropImg.Size(), inner, outer);
									if (thick.Item1 > HookThicknessThreshold)
										AddDefect(results, gi, "轻微挂钩错位",
											new float[] { (float)bboxN.X, (float)bboxN.Y, (float)(bboxN.X + bboxN.Width), (float)(bboxN.Y + bboxN.Height) });
								}
							}
						}
					}
				}
				int oc = results.Values.SelectMany(v => v).Count(d => d.DefectType == "挂钩明显错位");
				int sc = results.Values.SelectMany(v => v).Count(d => d.DefectType == "轻微挂钩错位");
				Logger.Info("[Back] 挂钩结果: 明显=" + oc + " 轻微=" + sc);
			}
			catch (Exception ex) { Logger.Error("挂钩异常: " + ex.Message); }
			return results;
		}

		private (double MaxThickness, CvPoint MaxLoc) CalcThickness(CvSize sz, CvPoint[] inner, CvPoint[] outer)
		{
			using (Mat mask = Mat.Zeros(sz, MatType.CV_8UC1))
			{
				Cv2.FillPoly(mask, new[] { outer }, new CvScalar(255));
				Cv2.FillPoly(mask, new[] { inner }, new CvScalar(0));
				using (Mat dist = new Mat())
				{
					Cv2.DistanceTransform(mask, dist, DistanceTypes.L2, DistanceTransformMasks.Precise);
					Cv2.MinMaxLoc(dist, out _, out double maxVal, out _, out CvPoint maxLoc);
					return (maxVal * 2.0, maxLoc);
				}
			}
		}

		private void AddDefect(Dictionary<int, List<BoxDefect>> dict, int idx, string type, float[] box)
		{
			if (!dict.ContainsKey(idx)) dict[idx] = new List<BoxDefect>();
			dict[idx].Add(new BoxDefect(idx, type, box));
		}

		private void MapBoxes(YoloInference.YoloResult res, Dictionary<int, List<BoxDefect>> dict, int start, int end, string type)
		{
			if (res == null || res.Boxes == null) return;
			int n = end - start; if (n <= 0) return;
			foreach (var b in res.Boxes)
			{
				float cx = (b.X + b.Width / 2f) / res.OrigImg.Width;
				int idx = start + (int)(cx * n);
				if (idx < start || idx >= end) continue;
				if (!dict.ContainsKey(idx)) dict[idx] = new List<BoxDefect>();
				dict[idx].Add(new BoxDefect(idx, type, new float[] { b.X, b.Y, b.X + b.Width, b.Y + b.Height }));
			}
		}

		// ====== 绘制 ======
		private Bitmap DrawResult(Mat img, List<BoxDefect> defects, List<string> status, int start, int end)
		{
			var bmp = img.ToBitmap();
			using (var g = Graphics.FromImage(bmp))
			{
				g.SmoothingMode = SmoothingMode.AntiAlias;
				int w = bmp.Width, h = bmp.Height, n = end - start;

				foreach (var d in defects)
				{
					var bb = d.BoundingBox; if (bb.Length < 4) continue;
					int x1 = (int)(bb[0] * w), y1 = (int)(bb[1] * h), x2 = (int)(bb[2] * w), y2 = (int)(bb[3] * h);
					if (x2 <= x1 || y2 <= y1) continue;
					var rc = new Rect(x1, y1, x2 - x1, y2 - y1);
					bool isOk = d.DefectType.StartsWith("条码:") || d.DefectType.StartsWith("日期:") || d.DefectType.StartsWith("双排:");
					Color c = isOk ? Color.Lime : Color.Red;
					if (d.DefectType.Contains("条码错误") || d.DefectType.Contains("条码缺少")) c = Color.Orange;
					if (d.DefectType.Contains("日期码错误") || d.DefectType.Contains("日期码不完全") || d.DefectType.Contains("日期码重影")) c = Color.Orange;
					if (d.DefectType.Contains("明显")) c = Color.DarkRed;
					if (d.DefectType.Contains("轻微")) c = Color.OrangeRed;
					bool borderOnly = d.DefectType.Contains("条码缺少") || d.DefectType.Contains("日期码错误") || d.DefectType.Contains("日期码不完全") || d.DefectType.Contains("日期码重影");
					if (!borderOnly) using (var fl = new SolidBrush(Color.FromArgb(80, c))) g.FillRectangle(fl, rc);
					using (var pn = new Pen(c, borderOnly ? 2 : 4) { DashStyle = borderOnly ? DashStyle.Dash : DashStyle.Solid }) g.DrawRectangle(pn, rc);
					using (var f = new Font("微软雅黑", 36, FontStyle.Bold))
					{
						string label = d.DefectType;
						if (label.Length > 18) label = label.Substring(0, 18);
						var sz = g.MeasureString(label, f);
						int ly = y1 - (int)sz.Height - 8; if (ly < 8) ly = y1 + 8;
						using (var bg = new SolidBrush(c)) g.FillRectangle(bg, x1 - 4, ly - 4, sz.Width + 16, sz.Height + 12);
						g.DrawString(label, f, Brushes.White, x1 + 4, ly + 2);
					}
				}

				if (n > 1)
					using (var dp = new Pen(Color.FromArgb(100, 100, 100), 3) { DashStyle = DashStyle.Dash })
						for (int i = 1; i < n; i++) g.DrawLine(dp, i * w / n, 0, i * w / n, h);

				using (var f = new Font("微软雅黑", 48, FontStyle.Bold))
					for (int i = 0; i < n && start + i < status.Count; i++)
					{
						string s = status[start + i];
						string disp = s == "OK" ? "OK" : (s.Length > 4 ? s.Substring(0, 4) : s);
						Color c = s == "OK" ? Color.Green : Color.Red;
						float cx = (i + 0.5f) * w / n;
						var sz = g.MeasureString(disp, f);
						using (var br = new SolidBrush(c)) g.DrawString(disp, f, br, cx - sz.Width / 2, 60);

						int boxNum = ReverseBoxOrder ? (status.Count - (start + i)) : (start + i + 1);
						using (var fn2 = new Font("微软雅黑", 28, FontStyle.Bold))
						{
							string idxStr = "盒" + boxNum;
							var nsz = g.MeasureString(idxStr, fn2);
							using (var nbr = new SolidBrush(Color.Yellow))
								g.DrawString(idxStr, fn2, nbr, cx - nsz.Width / 2, 120);
						}
					}
			}
			return bmp;
		}

		private Bitmap MergeImages(Bitmap left, Bitmap right)
		{
			var m = new Bitmap(left.Width + right.Width, Math.Max(left.Height, right.Height), PixelFormat.Format24bppRgb);
			using (var g = Graphics.FromImage(m))
			{
				g.Clear(Color.Black);
				g.DrawImage(left, 0, (m.Height - left.Height) / 2);
				g.DrawImage(right, left.Width, (m.Height - right.Height) / 2);
				using (var pn = new Pen(Color.White, 4)) g.DrawLine(pn, left.Width, 0, left.Width, m.Height);
				string txt = _lastIsOk ? "OK" : "NG";
				Color tc = _lastIsOk ? Color.Lime : Color.Red;
				using (var f = new Font("微软雅黑", 120, FontStyle.Bold))
				{
					var sz = g.MeasureString(txt, f);
					int rx = m.Width - (int)sz.Width - 60, ry = 30;
					using (var bg = new SolidBrush(Color.FromArgb(180, Color.Black)))
						g.FillRectangle(bg, rx - 20, ry - 10, sz.Width + 40, sz.Height + 20);
					using (var br = new SolidBrush(tc)) g.DrawString(txt, f, br, rx, ry);
				}
			}
			left.Dispose(); right.Dispose(); return m;
		}

		private void SaveImages(Bitmap leftRaw, Bitmap rightRaw, Bitmap merged, long pid, bool isOk, List<string> st)
		{
			bool so = _Config.IsSaveOkImage, sn = _Config.IsSaveNgImage, sor = _Config.IsSaveOkRawImage, snr = _Config.IsSaveNgRawImage;
			if (!so && !sn && !sor && !snr) return;
			string shift = GetShift(), dd = DateTime.Now.ToString("yyMMdd");
			string nt = string.Join("_", st.Where(s => s != "OK").Distinct().DefaultIfEmpty("OK"));
			foreach (var ch in new char[] { ':', '/', '\\', '<', '>', '"', '|', '?', '*', '(', ')', '（', '）' }) nt = nt.Replace(ch, '_');
			string resultDir = isOk ? "OK" : "NG";
			string dir = Path.Combine(_savePath, dd, shift, "背面工位", resultDir); Directory.CreateDirectory(dir);
			if ((isOk && so) || (!isOk && sn))
				_imageSaver.AddSaveTask(Path.Combine(dir, pid + "_渲染_" + nt + ".jpg"), merged.ToJpegBytesFast(85), true, 85);
			if ((isOk && sor) || (!isOk && snr))
			{
				_imageSaver.AddSaveTask(Path.Combine(dir, pid + "_左原图_" + nt + ".bmp"), leftRaw.ToBmpBytesFast(), false);
				_imageSaver.AddSaveTask(Path.Combine(dir, pid + "_右原图_" + nt + ".bmp"), rightRaw.ToBmpBytesFast(), false);
			}
		}

		private string GetShift()
		{
			var n = DateTime.Now.TimeOfDay;
			if (n >= TimeSpan.Parse("00:00") && n <= TimeSpan.Parse("07:59")) return "晚班";
			if (n >= TimeSpan.Parse("08:00") && n <= TimeSpan.Parse("15:59")) return "早班";
			return "中班";
		}

		public void ClearCounters() { Interlocked.Exchange(ref _totalCount, 0); Interlocked.Exchange(ref _okCount, 0); Interlocked.Exchange(ref _ngCount, 0); }
		public void Dispose() { if (_disposed) return; _disposed = true; lock (_syncLock) { _leftBuffer?.Dispose(); _rightBuffer?.Dispose(); } }
	}
}
