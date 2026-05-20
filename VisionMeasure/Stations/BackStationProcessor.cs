using AI;
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
using Utils;
using YoloInference;
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

		private readonly ConcurrentDictionary<long, ImageContext> _leftDict = new ConcurrentDictionary<long, ImageContext>();
		private readonly ConcurrentDictionary<long, ImageContext> _rightDict = new ConcurrentDictionary<long, ImageContext>();
		private readonly BlockingCollection<(ImageContext left, ImageContext right)> _pairQueue;

		private long _totalCount = 0;
		private long _okCount = 0;
		private long _ngCount = 0;

		private Thread _processThread;
		private CancellationTokenSource _cts;
		private bool _disposed;

		public event Action<ProductResult> OnResultReady;
		public event Action<List<string>, int> OnStatusUpdate;

		public float ConfThreshold { get; set; } = 0.5f;
		public float IouThreshold { get; set; } = 0.2f;
		public float HookThicknessThreshold { get; set; } = 30.0f;
		public int HookBlueAreaClassId { get; set; } = 0;
		public int HookHangHoleClassId { get; set; } = 1;

		public BackStationProcessor(AiModelManager models, string savePath, SkuData sku,
			HighSpeedImageSaver imageSaver, PerformanceMonitor perfMonitor)
		{
			_models = models;
			_savePath = savePath;
			_sku = sku;
			_imageSaver = imageSaver;
			_perfMonitor = perfMonitor;
			_pairQueue = new BlockingCollection<(ImageContext, ImageContext)>(100);
			_cts = new CancellationTokenSource();
		}

		public void UpdateSku(SkuData sku) => _sku = sku;
		public void OnCam3(Bitmap bitmap, long productId) => AddImage(_leftDict, bitmap, productId, true);
		public void OnCam4(Bitmap bitmap, long productId) => AddImage(_rightDict, bitmap, productId, false);

		private void AddImage(ConcurrentDictionary<long, ImageContext> dict, Bitmap bitmap, long productId, bool isLeft)
		{
			var ctx = new ImageContext { ProductId = productId, OriginalBitmap = bitmap, ReceiveTime = DateTime.Now };
			var otherDict = isLeft ? _rightDict : _leftDict;
			if (otherDict.TryRemove(productId, out var other))
				_pairQueue.Add(isLeft ? (ctx, other) : (other, ctx));
			else
				dict[productId] = ctx;
		}

		public void Start()
		{
			_processThread = new Thread(ProcessLoop) { Name = "BackStationProcessor", IsBackground = true, Priority = ThreadPriority.AboveNormal };
			_processThread.Start();
			Logger.Info("背面工位处理器已启动");
		}

		public void Stop() { _cts.Cancel(); _processThread?.Join(3000); }

		private void ProcessLoop()
		{
			while (!_cts.Token.IsCancellationRequested)
			{
				try
				{
					if (_pairQueue.TryTake(out var pair, 100, _cts.Token))
						ProcessImages(pair.left, pair.right);
				}
				catch (OperationCanceledException) { break; }
				catch (Exception ex) { Logger.Error($"背面工位处理异常: {ex.Message}"); }
			}
		}

		private async void ProcessImages(ImageContext left, ImageContext right)
		{
			long productId = left.ProductId;
			var sw = System.Diagnostics.Stopwatch.StartNew();
			var finalResult = new ProductResult { ProductId = productId, CreateTime = DateTime.Now };
			var statusList = new List<string>(_sku.P);
			for (int i = 0; i < _sku.P; i++) statusList.Add("OK");

			double cropTime = 0, inferenceTime = 0;

			try
			{
				Logger.Debug($"背面处理开始 ProductId={productId}, P={_sku.P}");

				Mat leftMat = null, rightMat = null;
				using (var cropScope = new StopwatchScope(t => cropTime = t))
				{
					leftMat = BitmapConverter.ToMat(left.OriginalBitmap);
					rightMat = BitmapConverter.ToMat(right.OriginalBitmap);
					CropImages(leftMat, rightMat, out var leftCropped, out var rightCropped);
					leftMat?.Dispose(); rightMat?.Dispose();
					leftMat = leftCropped; rightMat = rightCropped;
				}

				int halfP = _sku.P / 2;

				Dictionary<int, List<BoxDefect>> barcodeDict = null;
				Dictionary<int, List<BoxDefect>> dateCodeDict = null;
				Dictionary<int, List<BoxDefect>> hookDict = null;

				using (var inferScope = new StopwatchScope(t => inferenceTime = t))
				{
					var barcodeTask = Task.Run(() => RecognizeBarcodes(leftMat, rightMat, halfP));
					var dateCodeTask = Task.Run(() => RecognizeDateCodes(leftMat, rightMat, halfP));
					var hookTask = Task.Run(() => DetectHookDamage(leftMat, rightMat, halfP));

					await Task.WhenAll(barcodeTask, dateCodeTask, hookTask);

					barcodeDict = barcodeTask.Result;
					dateCodeDict = dateCodeTask.Result;
					hookDict = hookTask.Result;
				}

				var allDefects = new List<BoxDefect>();
				if (barcodeDict != null) allDefects.AddRange(barcodeDict.Values.SelectMany(v => v));
				if (dateCodeDict != null) allDefects.AddRange(dateCodeDict.Values.SelectMany(v => v));
				if (hookDict != null) allDefects.AddRange(hookDict.Values.SelectMany(v => v));

				foreach (var defect in allDefects)
				{
					if (defect.BoxIndex >= 0 && defect.BoxIndex < statusList.Count)
						statusList[defect.BoxIndex] = defect.DefectType;
				}

				bool isOk = statusList.All(s => s == "OK");
				finalResult.BackResult = isOk;
				finalResult.BackDefects = statusList.Where(s => s != "OK").Distinct().ToList();

				Interlocked.Increment(ref _totalCount);
				if (isOk) Interlocked.Increment(ref _okCount);
				else Interlocked.Increment(ref _ngCount);

				double drawTime = 0;
				Bitmap mergedRender = null;
				using (var drawScope = new StopwatchScope(t => drawTime = t))
				{
					var leftRender = DrawDetectionResult(leftMat, allDefects.Where(d => d.BoxIndex < halfP).ToList(), statusList, 0, halfP);
					var rightRender = DrawDetectionResult(rightMat, allDefects.Where(d => d.BoxIndex >= halfP).ToList(), statusList, halfP, _sku.P);
					mergedRender = MergeImages(leftRender, rightRender);
				}

				double saveTime = 0;
				using (var saveScope = new StopwatchScope(t => saveTime = t))
				{
					SaveImages(left.OriginalBitmap, right.OriginalBitmap, mergedRender, productId, isOk, statusList);
				}

				double totalTime = sw.Elapsed.TotalMilliseconds;
				_perfMonitor?.Record(new PerformanceMonitor.PerformanceRecord
				{
					Timestamp = DateTime.Now,
					Station = "Back",
					ProductId = productId,
					CropTimeMs = cropTime,
					InferenceTimeMs = inferenceTime,
					PostprocessTimeMs = 0,
					DrawTimeMs = drawTime,
					SaveTimeMs = saveTime,
					PlcTimeMs = 0,
					TotalTimeMs = totalTime,
					Result = isOk
				});

				Logger.Info($"背面处理完成 ProductId={productId} 耗时={totalTime:F2}ms 结果={(isOk ? "OK" : "NG")}");
				OnResultReady?.Invoke(finalResult);
				OnStatusUpdate?.Invoke(statusList, _sku.P);
			}
			catch (Exception ex)
			{
				Logger.Error($"背面处理异常 ProductId={productId}: {ex.Message}");
				finalResult.BackResult = false;
				OnResultReady?.Invoke(finalResult);
			}
			finally
			{
				left.Dispose(); right.Dispose();
			}
		}

		private void CropImages(Mat left, Mat right, out Mat leftCropped, out Mat rightCropped)
		{
			int h = left.Height;
			int w = left.Width;
			int cropStartY = h / 2;
			leftCropped = new Mat(left, new CvRect(0, cropStartY, w, h - cropStartY)).Clone();
			rightCropped = new Mat(right, new CvRect(0, cropStartY, w, h - cropStartY)).Clone();
		}

		private Dictionary<int, List<BoxDefect>> RecognizeBarcodes(Mat left, Mat right, int halfP)
		{
			var results = new Dictionary<int, List<BoxDefect>>();
			return results;
		}

		private Dictionary<int, List<BoxDefect>> RecognizeDateCodes(Mat left, Mat right, int halfP)
		{
			var results = new Dictionary<int, List<BoxDefect>>();
			if (_models.BackDateCodeModel == null) return results;

			// 简化实现
			return results;
		}

		private Dictionary<int, List<BoxDefect>> DetectHookDamage(Mat left, Mat right, int halfP)
		{
			var results = new Dictionary<int, List<BoxDefect>>();
			if (_models.BackHookModel == null) return results;
			return results;
		}

		private Bitmap DrawDetectionResult(Mat image, List<BoxDefect> defects, List<string> statusList, int startIdx, int endIdx)
		{
			var bitmap = image.ToBitmap();
			using (var g = Graphics.FromImage(bitmap))
			{
				g.SmoothingMode = SmoothingMode.AntiAlias;
				int w = bitmap.Width, h = bitmap.Height;
				int totalBoxes = endIdx - startIdx;

				var colorMap = new Dictionary<string, Color>
				{
					{ "条形码错误", Color.Red },
					{ "日期码错误", Color.Orange },
					{ "挂钩明显错位", Color.DarkRed },
					{ "轻微挂钩错位", Color.OrangeRed },
					{ "OK", Color.Green }
				};

				foreach (var defect in defects)
				{
					var box = defect.BoundingBox;
					if (box.Length < 4) continue;
					int x1 = (int)(box[0] * w), y1 = (int)(box[1] * h);
					int x2 = (int)(box[2] * w), y2 = (int)(box[3] * h);
					var rect = new Rect(x1, y1, x2 - x1, y2 - y1);
					var color = colorMap.ContainsKey(defect.DefectType) ? colorMap[defect.DefectType] : Color.Red;

					using (var fill = new SolidBrush(Color.FromArgb(80, color)))
						g.FillRectangle(fill, rect);
					using (var pen = new Pen(color, 3))
						g.DrawRectangle(pen, rect);

					using (var font = new Font("微软雅黑", 10, FontStyle.Bold))
					{
						string label = defect.DefectType;
						var labelSize = g.MeasureString(label, font);
						int lx = x1, ly = y1 - (int)labelSize.Height - 4;
						if (ly < 4) ly = y1 + 4;
						using (var bgBrush = new SolidBrush(color))
							g.FillRectangle(bgBrush, lx - 2, ly - 2, labelSize.Width + 8, labelSize.Height + 6);
						g.DrawString(label, font, Brushes.White, lx + 2, ly + 1);
					}
				}

				if (totalBoxes > 1)
				{
					using (var dashPen = new Pen(Color.FromArgb(100, 100, 100), 1) { DashStyle = DashStyle.Dash })
						for (int i = 1; i < totalBoxes; i++)
							g.DrawLine(dashPen, i * w / totalBoxes, 0, i * w / totalBoxes, h);
				}

				using (var font = new Font("微软雅黑", 12, FontStyle.Bold))
				{
					for (int i = 0; i < totalBoxes && startIdx + i < statusList.Count; i++)
					{
						string status = statusList[startIdx + i];
						string display = status == "OK" ? "OK" : status.Length > 4 ? status.Substring(0, 4) : status;
						var color = colorMap.ContainsKey(status) ? colorMap[status] : Color.Gray;
						float cx = (i + 0.5f) / totalBoxes * w;
						var ts = g.MeasureString(display, font);
						using (var brush = new SolidBrush(color))
							g.DrawString(display, font, brush, cx - ts.Width / 2, 5);
					}
				}
			}
			return bitmap;
		}

		private Bitmap MergeImages(Bitmap left, Bitmap right)
		{
			int totalWidth = left.Width + right.Width;
			int maxHeight = Math.Max(left.Height, right.Height);
			var merged = new Bitmap(totalWidth, maxHeight, PixelFormat.Format24bppRgb);
			using (var g = Graphics.FromImage(merged))
			{
				g.Clear(Color.Black);
				g.DrawImage(left, 0, (maxHeight - left.Height) / 2);
				g.DrawImage(right, left.Width, (maxHeight - right.Height) / 2);
				using (var pen = new Pen(Color.White, 2))
					g.DrawLine(pen, left.Width, 0, left.Width, maxHeight);
			}
			left.Dispose(); right.Dispose();
			return merged;
		}

		private void SaveImages(Bitmap leftRaw, Bitmap rightRaw, Bitmap mergedRender, long productId, bool isOk, List<string> statusList)
		{
			bool saveOkImage = _Config.IsSaveOkImage;
			bool saveNgImage = _Config.IsSaveNgImage;
			bool saveOkRawImage = _Config.IsSaveOkRawImage;
			bool saveNgRawImage = _Config.IsSaveNgRawImage;

			if (!saveOkImage && !saveNgImage && !saveOkRawImage && !saveNgRawImage)
				return;

			string shift = GetCurrentShift();
			string dateDir = DateTime.Now.ToString("yyMMdd");
			string ngTypes = GetNgTypesString(statusList);

			if ((isOk && saveOkImage) || (!isOk && saveNgImage))
			{
				string dir = Path.Combine(_savePath, dateDir, shift, "Back", "Render");
				Directory.CreateDirectory(dir);
				string fileName = $"{productId}_{ngTypes}.jpg";
				string filePath = Path.Combine(dir, fileName);
				var jpegData = mergedRender.ToJpegBytesFast(85);
				_imageSaver.AddSaveTask(filePath, jpegData, true, 85);
			}

			if ((isOk && saveOkRawImage) || (!isOk && saveNgRawImage))
			{
				string dir = Path.Combine(_savePath, dateDir, shift, "Back", "Raw");
				Directory.CreateDirectory(dir);

				string leftFileName = $"{productId}_left_{ngTypes}.bmp";
				string leftPath = Path.Combine(dir, leftFileName);
				var leftData = leftRaw.ToBmpBytesFast();
				_imageSaver.AddSaveTask(leftPath, leftData, false);

				string rightFileName = $"{productId}_right_{ngTypes}.bmp";
				string rightPath = Path.Combine(dir, rightFileName);
				var rightData = rightRaw.ToBmpBytesFast();
				_imageSaver.AddSaveTask(rightPath, rightData, false);
			}
		}

		private string GetCurrentShift()
		{
			var now = DateTime.Now.TimeOfDay;
			if (now >= TimeSpan.Parse("00:00:00") && now <= TimeSpan.Parse("07:59:59")) return "Night";
			if (now >= TimeSpan.Parse("08:00:00") && now <= TimeSpan.Parse("15:59:59")) return "Morning";
			return "Afternoon";
		}

		private string GetNgTypesString(List<string> statusList)
		{
			var ngTypes = statusList.Where(s => s != "OK").Distinct().ToList();
			if (ngTypes.Count == 0) return "OK";
			return string.Join("_", ngTypes);
		}

		public void ClearCounters()
		{
			Interlocked.Exchange(ref _totalCount, 0);
			Interlocked.Exchange(ref _okCount, 0);
			Interlocked.Exchange(ref _ngCount, 0);
		}

		public void Dispose()
		{
			if (_disposed) return;
			_disposed = true;
			_cts.Cancel();
			_processThread?.Join(3000);
			_cts.Dispose();
			_pairQueue.Dispose();
		}
	}
}
