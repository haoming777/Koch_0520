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
	public class EndFaceStationProcessor : IDisposable
	{
		private readonly AiModelManager _models;
		private readonly string _savePath;
		private int _pCount;
		private readonly HighSpeedImageSaver _imageSaver;
		private readonly PerformanceMonitor _perfMonitor;

		private readonly ConcurrentQueue<ImageContext> _upperQueue = new ConcurrentQueue<ImageContext>();
		private readonly ConcurrentQueue<ImageContext> _lowerQueue = new ConcurrentQueue<ImageContext>();
		private int _upperCount = 0;
		private int _lowerCount = 0;
		private readonly object _countLock = new object();

		private readonly BlockingCollection<(List<ImageContext> upper, List<ImageContext> lower)> _batchQueue;

		private readonly List<Mat> _currentDisplayImages = new List<Mat>();
		private int _currentDisplayIndex = 0;
		private readonly object _resultLock = new object();

		private long _totalCount = 0;
		private long _okCount = 0;
		private long _ngCount = 0;

		private Thread _processThread;
		private CancellationTokenSource _cts;
		private bool _disposed;

		public event Action<ProductResult> OnResultReady;
		public event Action<List<string>, List<string>, List<string>, int> OnStatusUpdate;

		public float ConfThreshold { get; set; } = 0.5f;
		public float IouThreshold { get; set; } = 0.2f;
		public int ExposureMs { get; set; } = 20;

		public long TotalCount => _totalCount;
		public long OkCount => _okCount;
		public long NgCount => _ngCount;
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
			_batchQueue = new BlockingCollection<(List<ImageContext>, List<ImageContext>)>(50);
			_cts = new CancellationTokenSource();
		}

		public void UpdatePCount(int pCount) => _pCount = pCount;
		public void OnCam5(Bitmap bitmap, long productId) => EnqueueImage(_upperQueue, ref _upperCount, bitmap, productId, "Upper");
		public void OnCam6(Bitmap bitmap, long productId) => EnqueueImage(_lowerQueue, ref _lowerCount, bitmap, productId, "Lower");

		private void EnqueueImage(ConcurrentQueue<ImageContext> queue, ref int count, Bitmap bitmap, long productId, string name)
		{
			var ctx = new ImageContext { ProductId = productId, OriginalBitmap = bitmap, ReceiveTime = DateTime.Now };
			lock (_countLock)
			{
				queue.Enqueue(ctx);
				count++;
				if (_upperCount >= _pCount && _lowerCount >= _pCount)
				{
					var upperList = DequeueBatch(_upperQueue, ref _upperCount);
					var lowerList = DequeueBatch(_lowerQueue, ref _lowerCount);
					_batchQueue.Add((upperList, lowerList));
				}
			}
		}

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

		public void Start()
		{
			_processThread = new Thread(ProcessLoop) { Name = "EndFaceStationProcessor", IsBackground = true, Priority = ThreadPriority.AboveNormal };
			_processThread.Start();
			Logger.Info("端面工位处理器已启动");
		}

		public void Stop() { _cts.Cancel(); _processThread?.Join(3000); }

		private void ProcessLoop()
		{
			while (!_cts.Token.IsCancellationRequested)
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

		private void ProcessBatch(List<ImageContext> upperImages, List<ImageContext> lowerImages)
		{
			if (upperImages.Count != _pCount || lowerImages.Count != _pCount)
			{
				Logger.Error($"端面图片数量不匹配: Upper={upperImages.Count}, Lower={lowerImages.Count}, P={_pCount}");
				return;
			}

			var sw = System.Diagnostics.Stopwatch.StartNew();
			long firstProductId = upperImages.FirstOrDefault()?.ProductId ?? 0;

			try
			{
				double cropTime = 0, inferenceTime = 0;

				List<Mat> upperMats = null, lowerMats = null;
				using (var cropScope = new StopwatchScope(t => cropTime = t))
				{
					upperMats = CropImagesBatch(upperImages);
					lowerMats = CropImagesBatch(lowerImages);
				}

				List<YoloResult> upperResults = null, lowerResults = null;
				using (var inferScope = new StopwatchScope(t => inferenceTime = t))
				{
					var upperTask = Task.Run(() => RunInference(upperMats, _models.EndFaceUpperModel));
					var lowerTask = Task.Run(() => RunInference(lowerMats, _models.EndFaceLowerModel));
					Task.WaitAll(upperTask, lowerTask);
					upperResults = upperTask.Result;
					lowerResults = lowerTask.Result;
				}

				var upperDefects = ParseResults(upperResults);
				var lowerDefects = ParseResults(lowerResults);

				var upperStatus = new List<string>();
				var lowerStatus = new List<string>();
				var mergedStatus = new List<string>();

				for (int i = 0; i < _pCount; i++)
				{
					string uStatus = upperDefects.ContainsKey(i) ? upperDefects[i].First().DefectType : "OK";
					string lStatus = lowerDefects.ContainsKey(i) ? lowerDefects[i].First().DefectType : "OK";
					upperStatus.Add(uStatus);
					lowerStatus.Add(lStatus);
					mergedStatus.Add((uStatus == "OK" && lStatus == "OK") ? "OK" : (uStatus != "OK" ? uStatus : lStatus));
				}

				bool isOk = mergedStatus.All(s => s == "OK");
				var result = new ProductResult
				{
					ProductId = firstProductId,
					CreateTime = DateTime.Now,
					EndFaceResult = isOk,
					EndFaceDefects = mergedStatus.Where(s => s != "OK").Distinct().ToList()
				};

				Interlocked.Increment(ref _totalCount);
				if (isOk) Interlocked.Increment(ref _okCount);
				else Interlocked.Increment(ref _ngCount);

				double drawTime = 0;
				using (var drawScope = new StopwatchScope(t => drawTime = t))
				{
					DrawResultsBatch(upperImages, upperMats, upperDefects, upperStatus);
					DrawResultsBatch(lowerImages, lowerMats, lowerDefects, lowerStatus);
					UpdateDisplayImages(upperMats, lowerMats, upperDefects, lowerDefects, upperStatus, lowerStatus);
				}

				double saveTime = 0;
				using (var saveScope = new StopwatchScope(t => saveTime = t))
				{
					SaveImagesBatch(upperImages, lowerImages, mergedStatus, firstProductId, isOk);
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
				OnStatusUpdate?.Invoke(upperStatus, lowerStatus, mergedStatus, _pCount);
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

		private List<Mat> CropImagesBatch(List<ImageContext> images)
		{
			var mats = new List<Mat>();
			foreach (var img in images)
			{
				var mat = BitmapConverter.ToMat(img.OriginalBitmap);
				int h = mat.Height, w = mat.Width;
				int cropH = h * 2 / 3;
				var cropped = new Mat(mat, new CvRect(0, 0, w, cropH)).Clone();
				mats.Add(cropped);
				mat.Dispose();
			}
			return mats;
		}

		private List<YoloResult> RunInference(List<Mat> images, YoloOnnx model)
		{
			if (model == null) return new List<YoloResult>();
			return model.PredictBatch(images, ConfThreshold, IouThreshold);
		}

		private Dictionary<int, List<BoxDefect>> ParseResults(List<YoloResult> results)
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

		private void DrawResultsBatch(List<ImageContext> images, List<Mat> mats, Dictionary<int, List<BoxDefect>> defects, List<string> status)
		{
			for (int i = 0; i < images.Count; i++)
			{
				var drawn = DrawDefectOnImage(mats[i], defects.ContainsKey(i) ? defects[i] : new List<BoxDefect>(), status[i], i, _pCount);
				images[i].RenderBitmap = drawn;
			}
		}

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
					using (var pen = new Pen(color, 3))
						g.DrawRectangle(pen, rect);

					using (var font = new Font("微软雅黑", 9, FontStyle.Bold))
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

				var statusColor = status == "OK" ? Color.Green : Color.Red;
				using (var font = new Font("微软雅黑", 14, FontStyle.Bold))
				using (var brush = new SolidBrush(statusColor))
				{
					string display = status == "OK" ? "OK" : "NG";
					g.DrawString(display, font, brush, w - 60, 5);
				}
			}
			return bitmap;
		}

		private void UpdateDisplayImages(List<Mat> upperMats, List<Mat> lowerMats,
			Dictionary<int, List<BoxDefect>> upperDefects, Dictionary<int, List<BoxDefect>> lowerDefects,
			List<string> upperStatus, List<string> lowerStatus)
		{
			lock (_resultLock)
			{
				_currentDisplayImages.Clear();
				for (int i = 0; i < _pCount; i++)
				{
					int w = upperMats[i].Width;
					int h = upperMats[i].Height + lowerMats[i].Height;
					var combined = new Mat(new OpenCvSharp.Size(w, h), MatType.CV_8UC3);
					using (var upperRoi = new Mat(combined, new CvRect(0, 0, w, upperMats[i].Height)))
						upperMats[i].CopyTo(upperRoi);
					using (var lowerRoi = new Mat(combined, new CvRect(0, upperMats[i].Height, w, lowerMats[i].Height)))
						lowerMats[i].CopyTo(lowerRoi);

					var bitmap = DrawDefectOnCombined(combined, upperDefects.ContainsKey(i) ? upperDefects[i] : new List<BoxDefect>(),
						lowerDefects.ContainsKey(i) ? lowerDefects[i] : new List<BoxDefect>(), upperStatus[i], lowerStatus[i]);
					_currentDisplayImages.Add(BitmapConverter.ToMat(bitmap));
					bitmap.Dispose();
					combined.Dispose();
				}
				_currentDisplayIndex = FindFirstNgIndex(upperStatus, lowerStatus);
			}
		}

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
					using (var pen = new Pen(color, 3))
						g.DrawRectangle(pen, rect);
				}

				foreach (var defect in lowerDefects)
				{
					var box = defect.BoundingBox;
					int x1 = (int)(box[0] * w), y1 = midY + (int)(box[1] * midY);
					int x2 = (int)(box[2] * w), y2 = midY + (int)(box[3] * midY);
					var rect = new Rect(x1, y1, x2 - x1, y2 - y1);
					var color = colorMap.ContainsKey(defect.DefectType) ? colorMap[defect.DefectType] : Color.Red;
					using (var pen = new Pen(color, 3))
						g.DrawRectangle(pen, rect);
				}

				using (var font = new Font("微软雅黑", 12, FontStyle.Bold))
				{
					var upperColor = upperStatus == "OK" ? Color.Green : Color.Red;
					var lowerColor = lowerStatus == "OK" ? Color.Green : Color.Red;
					using (var brush = new SolidBrush(upperColor))
						g.DrawString(upperStatus == "OK" ? "OK" : "NG", font, brush, w - 50, 5);
					using (var brush = new SolidBrush(lowerColor))
						g.DrawString(lowerStatus == "OK" ? "OK" : "NG", font, brush, w - 50, midY + 5);
				}
			}
			return bitmap;
		}

		private int FindFirstNgIndex(List<string> upperStatus, List<string> lowerStatus)
		{
			for (int i = 0; i < _pCount; i++)
				if (upperStatus[i] != "OK" || lowerStatus[i] != "OK")
					return i;
			return 0;
		}

		public Mat GetCurrentDisplayImage()
		{
			lock (_resultLock)
			{
				if (_currentDisplayImages.Count > 0 && _currentDisplayIndex >= 0 && _currentDisplayIndex < _currentDisplayImages.Count)
					return _currentDisplayImages[_currentDisplayIndex].Clone();
				return null;
			}
		}

		public void NavigatePrev()
		{
			lock (_resultLock)
			{
				if (_currentDisplayImages.Count > 0)
				{
					_currentDisplayIndex = (_currentDisplayIndex - 1 + _currentDisplayImages.Count) % _currentDisplayImages.Count;
					OnStatusUpdate?.Invoke(new List<string>(), new List<string>(), new List<string>(), _pCount);
				}
			}
		}

		public void NavigateNext()
		{
			lock (_resultLock)
			{
				if (_currentDisplayImages.Count > 0)
				{
					_currentDisplayIndex = (_currentDisplayIndex + 1) % _currentDisplayImages.Count;
					OnStatusUpdate?.Invoke(new List<string>(), new List<string>(), new List<string>(), _pCount);
				}
			}
		}

		private void SaveImagesBatch(List<ImageContext> upperImages, List<ImageContext> lowerImages, List<string> mergedStatus, long productId, bool isOk)
		{
			bool saveOkImage = _Config.IsSaveOkImage;
			bool saveNgImage = _Config.IsSaveNgImage;
			bool saveOkRawImage = _Config.IsSaveOkRawImage;
			bool saveNgRawImage = _Config.IsSaveNgRawImage;

			if (!saveOkImage && !saveNgImage && !saveOkRawImage && !saveNgRawImage)
				return;

			string shift = GetCurrentShift();
			string dateDir = DateTime.Now.ToString("yyMMdd");
			string ngTypes = GetNgTypesString(mergedStatus);

			if ((isOk && saveOkImage) || (!isOk && saveNgImage))
			{
				string dir = Path.Combine(_savePath, dateDir, shift, "EndFace", "Render");
				Directory.CreateDirectory(dir);
				for (int i = 0; i < _pCount; i++)
				{
					if (upperImages[i].RenderBitmap != null)
					{
						string fileName = $"{productId}_{i + 1}_{ngTypes}.jpg";
						string filePath = Path.Combine(dir, fileName);
						var jpegData = upperImages[i].RenderBitmap.ToJpegBytesFast(85);
						_imageSaver.AddSaveTask(filePath, jpegData, true, 85);
					}
				}
			}

			if ((isOk && saveOkRawImage) || (!isOk && saveNgRawImage))
			{
				string dir = Path.Combine(_savePath, dateDir, shift, "EndFace", "Raw");
				Directory.CreateDirectory(dir);
				for (int i = 0; i < _pCount; i++)
				{
					string fileName = $"{productId}_{i + 1}_upper_{ngTypes}.bmp";
					string filePath = Path.Combine(dir, fileName);
					var upperData = upperImages[i].OriginalBitmap.ToBmpBytesFast();
					_imageSaver.AddSaveTask(filePath, upperData, false);

					fileName = $"{productId}_{i + 1}_lower_{ngTypes}.bmp";
					filePath = Path.Combine(dir, fileName);
					var lowerData = lowerImages[i].OriginalBitmap.ToBmpBytesFast();
					_imageSaver.AddSaveTask(filePath, lowerData, false);
				}
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

		private string GetDefectType(int classId)
		{
			var classMap = new Dictionary<int, string> { { 0, "搭舌缺陷" }, { 1, "边缘问题" }, { 2, "破损" } };
			return classMap.ContainsKey(classId) ? classMap[classId] : $"缺陷{classId}";
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
			_batchQueue.Dispose();
		}
	}
}