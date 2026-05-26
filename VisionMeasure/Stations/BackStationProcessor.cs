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
using System.Threading;
using System.Threading.Tasks;
using VisionMeasure.Utils;
using CommonLib;
using YoloInference;
using YoloSegmentationEnd2End;
using AI;
using CvRect = OpenCvSharp.Rect;
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

		public event Action<ProductResult> OnResultReady;
		public event Action<List<string>, int> OnStatusUpdate;
		public float ConfThreshold = 0.5f, IouThreshold = 0.2f;

		public BackStationProcessor(AiModelManager models, string savePath, SkuData sku,
			HighSpeedImageSaver imageSaver, PerformanceMonitor perfMonitor)
		{ _models = models; _savePath = savePath; _sku = sku; _imageSaver = imageSaver; _perfMonitor = perfMonitor; }

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
					Logger.Debug("[Back] 左图裁图: " + "保留" + _sku.BackLeft_LeftPx + "~" + _sku.BackLeft_RightPx + " -> " + leftProc.Width + "x" + leftProc.Height);
				}
				else Logger.Debug("[Back] 左图无需裁图");

				if (_sku.BackRight_LeftPx > 0 || _sku.BackRight_RightPx > 0)
				{
					rightProc = ImageHelper.CropImageHorizontallyCv2(rightMat, _sku.BackRight_LeftPx, rightMat.Width - _sku.BackRight_RightPx);
					Logger.Debug("[Back] 右图裁图: " + _sku.BackRight_LeftPx + "," + _sku.BackRight_RightPx + " -> " + rightProc.Width + "x" + rightProc.Height);
				}
				else Logger.Debug("[Back] 右图无需裁图");

				// 步骤1: 推理
				Logger.Debug("[Back] 步骤1: 推理(条形码裁下1/3+挂钩全图)...");
				var sw1 = System.Diagnostics.Stopwatch.StartNew();
				Dictionary<int, List<BoxDefect>> barcodeDict = null, hookDict = null;
				var bt = Task.Run(() => RecognizeBarcodes(leftProc, rightProc, hp));
				var ht = Task.Run(() => DetectHookDamage(leftProc, rightProc, hp));
				Task.WaitAll(bt, ht);
				barcodeDict = bt.Result; hookDict = ht.Result;
				var inferMs = sw1.Elapsed.TotalMilliseconds;
				Logger.Info("[Back] 步骤1完成: 推理=" + inferMs.ToString("F1") + "ms");

				// 步骤2: 汇总
				var all = new List<BoxDefect>();
				int bc = 0, ho = 0, hs = 0;
				if (barcodeDict != null) { var its = barcodeDict.Values.SelectMany(v => v).ToList(); all.AddRange(its); bc = its.Count; }
				if (hookDict != null) { var its = hookDict.Values.SelectMany(v => v).ToList(); all.AddRange(its); ho = its.Count(d => d.DefectType == "挂钩明显错位"); hs = its.Count(d => d.DefectType == "轻微挂钩错位"); }
				Logger.Info("[Back] 步骤2汇总: 条形码=" + bc + " 明显=" + ho + " 轻微=" + hs + " 总计=" + all.Count);
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
				Logger.Info("[Back] 步骤3完成: 绘制+合并=" + drawMs.ToString("F1") + "ms " + merged.Width + "x" + merged.Height);

				// 步骤4: 保存
				Logger.Debug("[Back] 步骤4: 保存...");
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

		private Dictionary<int, List<BoxDefect>> RecognizeBarcodes(Mat left, Mat right, int hp)
		{
			var r = new Dictionary<int, List<BoxDefect>>();
			if (_models.BackBarcodeModel == null) return r;
			try
			{
				int h = left.Height, w = left.Width, cy = h * 2 / 3;
				Logger.Debug("[Back] 条形码裁图: " + w + "x" + h + ", 取底部1/3: y=" + cy + " h=" + (h - cy));
				using (var lc = new Mat(left, new CvRect(0, cy, w, h - cy)).Clone())
				using (var rc = new Mat(right, new CvRect(0, cy, w, h - cy)).Clone())
				{
					var lr = _models.BackBarcodeModel.Predict(lc, ConfThreshold, IouThreshold);
					var rr = _models.BackBarcodeModel.Predict(rc, ConfThreshold, IouThreshold);
					Logger.Debug("[Back] 条形码结果: 左=" + (lr?.Boxes?.Length ?? 0) + "框 右=" + (rr?.Boxes?.Length ?? 0) + "框");
					MapBoxes(lr, r, 0, hp, "条形码错误");
					MapBoxes(rr, r, hp, _sku.P, "条形码错误");
				}
			}
			catch (Exception ex) { Logger.Error("条形码异常: " + ex.Message); }
			return r;
		}

		private Dictionary<int, List<BoxDefect>> DetectHookDamage(Mat left, Mat right, int hp)
		{
			var r = new Dictionary<int, List<BoxDefect>>();
			if (_models.BackHookModel == null) return r;
			try
			{
				var lr = _models.BackHookModel.Predict(left, ConfThreshold, IouThreshold);
				var rr = _models.BackHookModel.Predict(right, ConfThreshold, IouThreshold);
				MapBoxes(lr, r, 0, hp, "挂钩明显错位");
				MapBoxes(rr, r, hp, _sku.P, "挂钩明显错位");
				if (_models.HookSlightModel != null)
				{
					var sl = _models.HookSlightModel.Predict(left, ConfThreshold);
					var sr = _models.HookSlightModel.Predict(right, ConfThreshold);
					MapBoxesSeg(sl, r, 0, hp, "轻微挂钩错位");
					MapBoxesSeg(sr, r, hp, _sku.P, "轻微挂钩错位");
				}
			}
			catch (Exception ex) { Logger.Error("挂钩异常: " + ex.Message); }
			return r;
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

		private void MapBoxesSeg(YoloSegmentationEnd2End.YoloResult res, Dictionary<int, List<BoxDefect>> dict, int start, int end, string type)
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

		private Bitmap DrawResult(Mat img, List<BoxDefect> defects, List<string> status, int start, int end)
		{
			var bmp = img.ToBitmap();
			using (var g = Graphics.FromImage(bmp))
			{
				g.SmoothingMode = SmoothingMode.AntiAlias;
				int w = bmp.Width, h = bmp.Height, n = end - start;
				var cmap = new Dictionary<string, Color> { { "条形码错误", Color.Red }, { "挂钩明显错位", Color.DarkRed }, { "轻微挂钩错位", Color.OrangeRed }, { "OK", Color.Green } };
				foreach (var d in defects)
				{
					var bb = d.BoundingBox; if (bb.Length < 4) continue;
					int x1 = (int)(bb[0] * w), y1 = (int)(bb[1] * h), x2 = (int)(bb[2] * w), y2 = (int)(bb[3] * h);
					var rc = new Rect(x1, y1, x2 - x1, y2 - y1);
					Color c = cmap.ContainsKey(d.DefectType) ? cmap[d.DefectType] : Color.Red;
					using (var fl = new SolidBrush(Color.FromArgb(80, c))) g.FillRectangle(fl, rc);
					using (var pn = new Pen(c, 8)) g.DrawRectangle(pn, rc);
					using (var f = new Font("微软雅黑", 72, FontStyle.Bold))
					{
						var sz = g.MeasureString(d.DefectType, f);
						int ly = y1 - (int)sz.Height - 8; if (ly < 8) ly = y1 + 8;
						using (var bg = new SolidBrush(c)) g.FillRectangle(bg, x1 - 4, ly - 4, sz.Width + 16, sz.Height + 12);
						g.DrawString(d.DefectType, f, Brushes.White, x1 + 4, ly + 2);
					}
				}
				if (n > 1) using (var dp = new Pen(Color.FromArgb(100, 100, 100), 3) { DashStyle = DashStyle.Dash })
						for (int i = 1; i < n; i++) g.DrawLine(dp, i * w / n, 0, i * w / n, h);
				using (var f = new Font("微软雅黑", 96, FontStyle.Bold))
					for (int i = 0; i < n && start + i < status.Count; i++)
					{
						string s = status[start + i], disp = s == "OK" ? "OK" : (s.Length > 4 ? s.Substring(0, 4) : s);
						Color c = cmap.ContainsKey(s) ? cmap[s] : Color.Red;
						float cx = (i + 0.5f) * w / n;
						var sz = g.MeasureString(disp, f);
						using (var br = new SolidBrush(c)) g.DrawString(disp, f, br, cx - sz.Width / 2, 5);
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
			if ((isOk && so) || (!isOk && sn))
			{
				string dir = Path.Combine(_savePath, dd, shift, "Back", "Render"); Directory.CreateDirectory(dir);
				_imageSaver.AddSaveTask(Path.Combine(dir, pid + "_" + nt + ".jpg"), merged.ToJpegBytesFast(85), true, 85);
			}
			if ((isOk && sor) || (!isOk && snr))
			{
				string dir = Path.Combine(_savePath, dd, shift, "Back", "Raw"); Directory.CreateDirectory(dir);
				_imageSaver.AddSaveTask(Path.Combine(dir, pid + "_left_" + nt + ".bmp"), leftRaw.ToBmpBytesFast(), false);
				_imageSaver.AddSaveTask(Path.Combine(dir, pid + "_right_" + nt + ".bmp"), rightRaw.ToBmpBytesFast(), false);
			}
		}

		private string GetShift()
		{
			var n = DateTime.Now.TimeOfDay;
			if (n >= TimeSpan.Parse("00:00") && n <= TimeSpan.Parse("07:59")) return "Night";
			if (n >= TimeSpan.Parse("08:00") && n <= TimeSpan.Parse("15:59")) return "Morning";
			return "Afternoon";
		}

		public void ClearCounters() { Interlocked.Exchange(ref _totalCount, 0); Interlocked.Exchange(ref _okCount, 0); Interlocked.Exchange(ref _ngCount, 0); }
		public void Dispose() { if (_disposed) return; _disposed = true; lock (_syncLock) { _leftBuffer?.Dispose(); _rightBuffer?.Dispose(); } }
	}
}