using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Text.RegularExpressions;
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
	public class FrontStationProcessor : IDisposable
	{
		private static readonly Regex PNumberRegex = new Regex(@"P\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled);
		private const int PNumberMinLength = 6;

		private HighSpeedImageSaver _imageSaver;
		private readonly AiModelManager _models;
		private readonly DetectionParameters _detectionParams;

		private Mat _leftBuffer = null;
		private Mat _rightBuffer = null;
		private readonly object _syncLock = new object();

		private int _okCount = 0;
		private int _ngCount = 0;
		private SkuData _currentSku = null;
		private bool _lastIsOk = true;

		public event Action<Bitmap, bool[], int, int> OnResultReady;
		public event Action<List<string>, int> OnStatusUpdate;

		public float ConfThreshold { get; set; } = 0.5f;
		public float IouThreshold { get; set; } = 0.45f;
		public bool ReverseBoxOrder = false;
		public bool EnablePNumberCheck = false;

		public FrontStationProcessor(AiModelManager modelManager, DetectionParameters detectionParams)
		{
			_models = modelManager;
			_detectionParams = detectionParams;
			_imageSaver = new HighSpeedImageSaver();
		}

		public void Start() { ClearCounters(); Logger.Info("FrontStationProcessor Started."); }
		public void UpdateSku(SkuData newSku) { _currentSku = newSku; }
		public void ClearCounters() { _okCount = 0; _ngCount = 0; }

		public void OnCam1(Bitmap leftImg, object extraArg = null)
		{
			if (leftImg == null) return;
			Logger.Debug($"[Front] OnCam1 收到图像 {leftImg.Width}x{leftImg.Height}");
			lock (_syncLock) { _leftBuffer?.Dispose(); _leftBuffer = leftImg.ToMat(); Cv2.Flip(_leftBuffer, _leftBuffer, FlipMode.XY); }
			CheckAndProcessAsync();
		}

		public void OnCam2(Bitmap rightImg, object extraArg = null)
		{
			if (rightImg == null) return;
			Logger.Debug($"[Front] OnCam2 收到图像 {rightImg.Width}x{rightImg.Height}");
			lock (_syncLock) { _rightBuffer?.Dispose(); _rightBuffer = rightImg.ToMat(); Cv2.Flip(_rightBuffer, _rightBuffer, FlipMode.XY); }
			CheckAndProcessAsync();
		}

		private async void CheckAndProcessAsync()
		{
			Mat leftToProcess = null, rightToProcess = null;
			lock (_syncLock)
			{
				if (_leftBuffer != null && _rightBuffer != null)
				{
					leftToProcess = _leftBuffer; rightToProcess = _rightBuffer;
					_leftBuffer = null; _rightBuffer = null;
					Logger.Debug("[Front] 左右图像配对成功，开始处理");
				}
			}
			if (leftToProcess == null || rightToProcess == null) return;

			var swTotal = System.Diagnostics.Stopwatch.StartNew();
			Mat leftProc = null, rightProc = null;
			try
			{
				int pCount = _currentSku?.P ?? 8;
				int halfP = pCount / 2;

				// 步骤0: 裁图
				leftProc = leftToProcess; rightProc = rightToProcess;
				if (_currentSku != null)
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

				// 步骤1: 并行推理
				var sw1 = System.Diagnostics.Stopwatch.StartNew();
				var pNumberTask = Task.Run(() => RecognizePNumber(leftProc, rightProc, pCount, halfP));
				var damageTask = Task.Run(() => DetectBoxDamage(leftProc, rightProc, halfP));
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

				Logger.Info($"[Front] 步骤1完成: 推理={sw1.Elapsed.TotalMilliseconds:F1}ms P号={pNumberResults.Values.Sum(v=>v.Count)} 破损={damageResults.Values.Sum(v=>v.Count)}");

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
				for (int i = 0; i < statusList.Count; i++) Logger.Info($"[Front]   盒{i + 1}: {statusList[i]}");

				int currentNgCount = ngArray.Count(n => n);
				bool isOk = (currentNgCount == 0);
				if (isOk) _okCount += pCount; else { _okCount += (pCount - currentNgCount); _ngCount += currentNgCount; }
				_lastIsOk = isOk;

				// 步骤3: 绘制(P号码全部画框, 绿色OK/橙色NG)
				Bitmap mergedImage = DrawAndMergeResults(leftProc, rightProc, pNumberResults, damageResults, statusList, halfP, isOk);

				// 步骤4: 存图
				SaveImages(leftProc, rightProc, mergedImage, ngArray);
				OnResultReady?.Invoke(mergedImage, ngArray, _okCount, _ngCount);
				OnStatusUpdate?.Invoke(statusList, pCount);
				Logger.Info($"[Front] 处理完成 总耗时={swTotal.Elapsed.TotalMilliseconds:F2}ms P={pCount} OK={pCount - currentNgCount} NG={currentNgCount}");
			}
			catch (Exception ex) { Logger.Error($"[Front] 处理异常: {ex.Message}\r\n{ex.StackTrace}"); }
			finally
			{
				leftToProcess?.Dispose(); rightToProcess?.Dispose();
				if (leftProc != null && leftProc != leftToProcess) leftProc.Dispose();
				if (rightProc != null && rightProc != rightToProcess) rightProc.Dispose();
			}
		}

		/// <summary>
		/// P号码识别: 逐盒ROI运行Vimo OCR, 返回所有识别结果(含OK的用于显示框)
		/// </summary>
		private Dictionary<int, List<BoxDefect>> RecognizePNumber(Mat left, Mat right, int pCount, int halfP)
		{
			var results = new Dictionary<int, List<BoxDefect>>();
			if (_models.FrontOcrModel == null) return results;
			try
			{
				int hL = left.Height, wL = left.Width, hR = right.Height, wR = right.Width;
				int boxWL = wL / halfP, boxWR = wR / halfP;
				int startYL = hL * 2 / 3, startYR = hR * 2 / 3;
				string refPNumber = _currentSku?.FrontPCode;
				bool hasRef = !string.IsNullOrEmpty(refPNumber);

				for (int i = 0; i < halfP; i++) { int sx = i * boxWL; int rw = (i < halfP - 1) ? boxWL : (wL - sx); int rh = hL - startYL; if (rw > 0 && rh > 0) using (var roi = new Mat(left, new CvRect(sx, startYL, rw, rh)).Clone()) ProcessPNumberRoi(roi, i, refPNumber, hasRef, wL, hL, sx, startYL, results); }
				for (int j = 0; j < halfP; j++) { int gi = halfP + j; int sx = j * boxWR; int rw = (j < halfP - 1) ? boxWR : (wR - sx); int rh = hR - startYR; if (rw > 0 && rh > 0) using (var roi = new Mat(right, new CvRect(sx, startYR, rw, rh)).Clone()) ProcessPNumberRoi(roi, gi, refPNumber, hasRef, wR, hR, sx, startYR, results); }
			}
			catch (Exception ex) { Logger.Error($"P号码识别异常: {ex.Message}"); }
			return results;
		}

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

		private float[] ComputeNormBBox(TextBlock block, int fullW, int fullH, int offsetX, int offsetY)
		{
			if (block.Polygon == null || !block.Polygon.Any()) return new float[] { 0, 0, 0.1f, 0.1f };
			float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
			foreach (var pt in block.Polygon) { float gx = pt.X + offsetX, gy = pt.Y + offsetY; if (gx < minX) minX = gx; if (gy < minY) minY = gy; if (gx > maxX) maxX = gx; if (gy > maxY) maxY = gy; }
			return new float[] { minX / fullW, minY / fullH, maxX / fullW, maxY / fullH };
		}

		private void AddDefect(Dictionary<int, List<BoxDefect>> dict, int idx, string type, float[] box)
		{
			if (!dict.ContainsKey(idx)) dict[idx] = new List<BoxDefect>();
			dict[idx].Add(new BoxDefect(idx, type, box));
		}

		private Dictionary<int, List<BoxDefect>> DetectBoxDamage(Mat left, Mat right, int halfP)
		{
			var results = new Dictionary<int, List<BoxDefect>>();
			if (_models.FrontBoxBreakModel == null) return results;
			try
			{
				var lr = _models.FrontBoxBreakModel.Predict(left, ConfThreshold, IouThreshold);
				var rr = _models.FrontBoxBreakModel.Predict(right, ConfThreshold, IouThreshold);
				ProcessYoloResults(lr, results, 0, halfP, "盒子破损");
				ProcessYoloResults(rr, results, halfP, _currentSku?.P ?? 8, "盒子破损");
			}
			catch (Exception ex) { Logger.Error($"盒子破损检测异常: {ex.Message}"); }
			return results;
		}

		private void ProcessYoloResults(YoloResult result, Dictionary<int, List<BoxDefect>> results, int startIdx, int endIdx, string defectType)
		{
			if (result == null || result.BoxesN == null) return;
			int n = endIdx - startIdx; if (n <= 0) return;
			foreach (var box in result.BoxesN)
			{
				float cx = box.X + box.Width / 2f;
				int idx = startIdx + (int)(cx * n);
				if (idx >= startIdx && idx < endIdx)
				{
					if (!results.ContainsKey(idx)) results[idx] = new List<BoxDefect>();
					results[idx].Add(new BoxDefect(idx, defectType, new float[] { box.X, box.Y, box.X + box.Width, box.Y + box.Height }));
				}
			}
		}

		private Bitmap DrawAndMergeResults(Mat left, Mat right,
			Dictionary<int, List<BoxDefect>> pNumberResults, Dictionary<int, List<BoxDefect>> damageResults,
			List<string> statusList, int halfP, bool isOk)
		{
			var lb = left.ToBitmap(); var rb = right.ToBitmap();
			int p = _currentSku?.P ?? 8;
			using (var g = Graphics.FromImage(lb)) { g.SmoothingMode = SmoothingMode.AntiAlias; DrawDefects(g, pNumberResults, damageResults, statusList, 0, halfP, lb.Width, lb.Height); }
			using (var g = Graphics.FromImage(rb)) { g.SmoothingMode = SmoothingMode.AntiAlias; DrawDefects(g, pNumberResults, damageResults, statusList, halfP, p, rb.Width, rb.Height); }
			return MergeImages(lb, rb, isOk);
		}

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
				if (so || sn) _imageSaver.Enqueue(merged, System.IO.Path.Combine(dir, $"{pid}_渲染_{nt}.jpg"), ImageFormat.Jpeg);
				if (sor || snr)
				{
					_imageSaver.Enqueue(left.ToBitmap(), System.IO.Path.Combine(dir, $"{pid}_左原图_{nt}.bmp"), ImageFormat.Bmp);
					_imageSaver.Enqueue(right.ToBitmap(), System.IO.Path.Combine(dir, $"{pid}_右原图_{nt}.bmp"), ImageFormat.Bmp);
				}
			}
			catch (Exception ex) { Logger.Error($"正面工位存图异常: {ex.Message}"); }
		}

		private string GetShift()
		{
			var n = DateTime.Now.TimeOfDay;
			if (n >= System.TimeSpan.Parse("00:00") && n <= System.TimeSpan.Parse("07:59")) return "晚班";
			if (n >= System.TimeSpan.Parse("08:00") && n <= System.TimeSpan.Parse("15:59")) return "早班";
			return "中班";
		}

		public void Dispose() { _imageSaver?.Dispose(); _leftBuffer?.Dispose(); _rightBuffer?.Dispose(); }
	}
}
