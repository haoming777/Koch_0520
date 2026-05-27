using Config;
using Hardware;
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
	public class SideStationProcessor : IDisposable
	{
		private readonly AiModelManager _models;
		private readonly string _savePath;
		private SkuData _sku;
		private readonly MotionControlManager _motionMgr;
		private readonly HighSpeedImageSaver _imageSaver;
		private readonly PerformanceMonitor _perfMonitor;

		private readonly ConcurrentQueue<ImageContext> _leftQueue = new ConcurrentQueue<ImageContext>();
		private readonly ConcurrentQueue<ImageContext> _rightQueue = new ConcurrentQueue<ImageContext>();
		private readonly BlockingCollection<(string side, ImageContext ctx)> _processQueue;

		private readonly List<SideDetectionResult> _leftResults = new List<SideDetectionResult>();
		private readonly List<SideDetectionResult> _rightResults = new List<SideDetectionResult>();
		private readonly List<Bitmap> _currentDisplayImages = new List<Bitmap>();
		private int _currentDisplayIndex = 0;
		private readonly object _resultLock = new object();

		private long _currentBatchProductId = 0;
		private int _currentBatchIndex = 0;
		private bool _batchCollecting = false;
		private readonly object _batchLock = new object();

		private float _startPosition = 0;
		private float _endPosition = 100;
		private bool _isMoving = false;

		private long _totalCount = 0;
		private long _okCount = 0;
		private long _ngCount = 0;

		private Thread _processThread;
		private CancellationTokenSource _cts;
		private bool _disposed;

		public event Action<ProductResult> OnResultReady;
		public event Action<List<string>, List<string>, List<string>, int> OnStatusUpdate;

		public float ConfThreshold { get; set; } = 0.5f;
		public float IouThreshold { get; set; } = 0.45f;
	public bool ReverseBoxOrder = false;
		public float CropRatio { get; set; } = 2.0f;
		public int MoveSpeed { get; set; } = 20;
		public int MoveAccel { get; set; } = 10000;
		public bool MissingAsNg { get; set; } = true;
		public bool UseContinuousMode { get; set; } = false;

		public enum TriggerEdgeMode { RisingLeftFallingRight, RisingRightFallingLeft }
		public TriggerEdgeMode EdgeMode { get; set; } = TriggerEdgeMode.RisingLeftFallingRight;

		public long TotalCount => _totalCount;
		public long OkCount => _okCount;
		public long NgCount => _ngCount;
		public int CurrentIndex => _currentDisplayIndex;
		public bool IsMoving => _isMoving;

		public SideStationProcessor(AiModelManager models, string savePath, SkuData sku,
			MotionControlManager motionMgr, HighSpeedImageSaver imageSaver, PerformanceMonitor perfMonitor)
		{
			_models = models;
			_savePath = savePath;
			_sku = sku;
			_motionMgr = motionMgr;
			_imageSaver = imageSaver;
			_perfMonitor = perfMonitor;
			_processQueue = new BlockingCollection<(string, ImageContext)>(200);
			_cts = new CancellationTokenSource();
		}

		public void UpdateSku(SkuData sku) { lock (_batchLock) _sku = sku; }
		public void OnCam7(Bitmap bitmap, long productId) => AddImage(_leftQueue, bitmap, productId, "Left");
		public void OnCam8(Bitmap bitmap, long productId) => AddImage(_rightQueue, bitmap, productId, "Right");

		private void AddImage(ConcurrentQueue<ImageContext> queue, Bitmap bitmap, long productId, string side)
		{
			var ctx = new ImageContext { ProductId = productId, OriginalBitmap = bitmap, ReceiveTime = DateTime.Now };
			queue.Enqueue(ctx);
			_processQueue.Add((side, ctx));
			Logger.Debug($"[Side] 图像入队 side={side}, ProductId={productId}, LeftQ={_leftQueue.Count}, RightQ={_rightQueue.Count}, ProcessQ={_processQueue.Count}");
		}

		public void Start()
		{
			_processThread = new Thread(ProcessLoop) { Name = "SideStationProcessor", IsBackground = true, Priority = ThreadPriority.AboveNormal };
			_processThread.Start();
		}

		public void Stop() { _cts.Cancel(); _processThread?.Join(3000); }

		public void StartDetection()
		{
			lock (_batchLock)
			{
				if (_batchCollecting) return;
				_batchCollecting = true;
				_currentBatchProductId = DateTime.Now.Ticks;
				_currentBatchIndex = 0;
				_currentDisplayImages.Clear();
				StartMotionControl();
			}
		}

		private void StartMotionControl()
		{
			if (_motionMgr == null || !_motionMgr.IsConnected)
			{
				Logger.Warning("[Side] 运动控制卡未连接，跳过侧面运动控制");
				_batchCollecting = false;
				return;
			}

			Logger.Info($"[Side] 运动控制开始: 起点={_startPosition}, 终点={_endPosition}, P={_sku.P}, 模式={(UseContinuousMode ? "连续" : "步进")}, EdgeMode={EdgeMode}");
			_isMoving = true;
			try
			{
				_motionMgr.MoveAbs(2, _startPosition);
				Thread.Sleep(100);

				if (UseContinuousMode)
				{
					_motionMgr.MoveAbs(2, _endPosition);
					while (_currentBatchIndex < _sku.P && _motionMgr.IsMoving(2))
						Thread.Sleep(10);
				}
				else
				{
					float currentPos = _startPosition;
					float step = (_endPosition - _startPosition) / _sku.P;

					for (int i = 0; i < _sku.P && _currentBatchIndex < _sku.P; i++)
					{
						currentPos = i == 0 ? _startPosition + step / 2 : currentPos + step;
						_motionMgr.GoPosition(2, currentPos);
						while (_motionMgr.IsMoving(2)) Thread.Sleep(10);

						TriggerCameraByPosition();

						int timeout = 5000;
						while (_currentBatchIndex <= i && timeout > 0) { Thread.Sleep(10); timeout -= 10; }
					}
				}

				if (_currentBatchIndex < _sku.P && MissingAsNg) FillMissingResults();
			}
			catch (Exception ex) { Logger.Error($"侧面运动控制异常: {ex.Message}"); }
			finally
			{
				_motionMgr.MoveAbs(2, _startPosition);
				_isMoving = false;
				_batchCollecting = false;
			}
		}

		private void TriggerCameraByPosition()
		{
			if (_motionMgr.GetInput(12, out bool sensorState))
			{
				int cameraId = EdgeMode == TriggerEdgeMode.RisingLeftFallingRight ?
					(sensorState ? 7 : 8) : (sensorState ? 8 : 7);
				Logger.Debug($"[Side] IN12={(sensorState ? "高" : "低")}, EdgeMode={EdgeMode}, 触发Camera{cameraId}");
				_motionMgr.SetOutput(cameraId + 7, true);
				Thread.Sleep(50);
				_motionMgr.SetOutput(cameraId + 7, false);
			}
			else
			{
				Logger.Warning("[Side] 无法读取IN12传感器状态");
			}
		}

		private void FillMissingResults()
		{
			while (_currentBatchIndex < _sku.P)
			{
				var result = new SideDetectionResult { BoxStatus = new List<string> { MissingAsNg ? "缺少" : "OK" } };
				_leftResults.Add(result);
				_rightResults.Add(result);
				_currentBatchIndex++;
			}
			ProcessBatch();
		}

		private void ProcessLoop()
		{
			while (!_cts.Token.IsCancellationRequested)
			{
				try
				{
					if (_processQueue.TryTake(out var item, 100, _cts.Token))
						ProcessSingleImage(item.side, item.ctx);
				}
				catch (OperationCanceledException) { break; }
				catch (Exception ex) { Logger.Error($"侧面工位处理异常: {ex.Message}"); }
			}
		}

		private void ProcessSingleImage(string side, ImageContext ctx)
		{
			var sw = System.Diagnostics.Stopwatch.StartNew();

			try
			{
				var mat = BitmapConverter.ToMat(ctx.OriginalBitmap);
				Mat cropped = null;
				using (var cropScope = new StopwatchScope(t => { }))
				{
					int h = mat.Height, w = mat.Width;
					int cropW = (int)(h * CropRatio);
					if (cropW > w) cropW = w;
					int startX = (w - cropW) / 2;
					cropped = new Mat(mat, new CvRect(startX, 0, cropW, h)).Clone();
				}

			YoloInference.YoloResult yoloResult = null;
				double inferTime = 0;
				using (var inferScope = new StopwatchScope(t => inferTime = t))
				{
					if (_models.SideDefectModel != null)
						yoloResult = _models.SideDefectModel.Predict(cropped, ConfThreshold, IouThreshold);
				}
				var defects = new List<BoxDefect>();
				if (yoloResult != null && yoloResult.Boxes != null)
				{
					for (int i = 0; i < yoloResult.Boxes.Length; i++)
					{
						var box = yoloResult.BoxesN[i];
						defects.Add(new BoxDefect(i, "缺陷" + yoloResult.ClassIds[i],
							new float[] { box.X, box.Y, box.X + box.Width, box.Y + box.Height }, yoloResult.Scores[i]));
					}
				}
				bool isOk = defects.Count == 0;

				double drawTime = 0;
				Bitmap renderBitmap = null;
				using (var drawScope = new StopwatchScope(t => drawTime = t))
					renderBitmap = DrawDetectionResult(cropped, defects, isOk);

				lock (_resultLock)
				{
					var result = new SideDetectionResult { BoxStatus = isOk ? new List<string> { "OK" } : defects.Select(d => d.DefectType).ToList() };
					if (side == "Left") { _leftResults.Add(result); _currentDisplayImages.Add(renderBitmap); }
					else _rightResults.Add(result);
					_currentBatchIndex++;
				}

				if (_currentBatchIndex >= _sku.P) ProcessBatch();
			}
			catch (Exception ex) { Logger.Error($"侧面单图处理异常 side={side}: {ex.Message}"); }
			finally { ctx.Dispose(); }
		}

		private Bitmap DrawDetectionResult(Mat image, List<BoxDefect> defects, bool isOk)
		{
			var bitmap = image.ToBitmap();
			using (var g = Graphics.FromImage(bitmap))
			{
				g.SmoothingMode = SmoothingMode.AntiAlias;
				int w = bitmap.Width, h = bitmap.Height;

				var colorMap = new Dictionary<string, Color>
				{
					{ "褶皱", Color.FromArgb(230, 126, 34) },
					{ "破损", Color.FromArgb(231, 76, 60) },
					{ "爆口", Color.FromArgb(155, 89, 182) }
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

				using (var font = new Font("微软雅黑", 14, FontStyle.Bold))
				using (var brush = new SolidBrush(isOk ? Color.Green : Color.Red))
					g.DrawString(isOk ? "OK" : "NG", font, brush, w - 50, 5);
			}
			return bitmap;
		}

		private void ProcessBatch()
		{
			lock (_resultLock)
			{
				int halfP = _sku.P / 2;
				var leftStatus = new List<string>();
				var rightStatus = new List<string>();
				var mergedStatus = new List<string>();

				for (int i = 0; i < halfP && i < _leftResults.Count; i++)
					leftStatus.Add(_leftResults[i].BoxStatus.FirstOrDefault() ?? "OK");
				for (int i = 0; i < halfP && i < _rightResults.Count; i++)
					rightStatus.Add(_rightResults[i].BoxStatus.FirstOrDefault() ?? "OK");

				while (leftStatus.Count < halfP) leftStatus.Add(MissingAsNg ? "缺少" : "OK");
				while (rightStatus.Count < halfP) rightStatus.Add(MissingAsNg ? "缺少" : "OK");

				for (int i = 0; i < _sku.P; i++)
				{
					string lStatus = i < leftStatus.Count ? leftStatus[i] : "OK";
					string rStatus = i < rightStatus.Count ? rightStatus[i] : "OK";
					mergedStatus.Add((lStatus == "OK" && rStatus == "OK") ? "OK" : (lStatus != "OK" ? lStatus : rStatus));
				}

				bool isOk = mergedStatus.All(s => s == "OK");
				var result = new ProductResult
				{
					ProductId = _currentBatchProductId,
					CreateTime = DateTime.Now,
					SideResult = isOk,
					SideDefects = mergedStatus.Where(s => s != "OK").Distinct().ToList()
				};

				Interlocked.Increment(ref _totalCount);
				if (isOk) Interlocked.Increment(ref _okCount);
				else Interlocked.Increment(ref _ngCount);

				SaveBatchImages(mergedStatus, _currentBatchProductId, isOk);
				OnResultReady?.Invoke(result);
				OnStatusUpdate?.Invoke(leftStatus, rightStatus, mergedStatus, _sku.P);

				_currentDisplayIndex = FindFirstNgIndex(mergedStatus);
				_leftResults.Clear(); _rightResults.Clear();
			}
		}

		private int FindFirstNgIndex(List<string> statusList)
		{
			for (int i = 0; i < statusList.Count; i++)
				if (statusList[i] != "OK") return i;
			return 0;
		}

		public Bitmap GetCurrentDisplayImage()
		{
			lock (_resultLock)
			{
				if (_currentDisplayIndex < _currentDisplayImages.Count)
					return (Bitmap)_currentDisplayImages[_currentDisplayIndex].Clone();
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
					OnStatusUpdate?.Invoke(new List<string>(), new List<string>(), new List<string>(), _sku.P);
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
					OnStatusUpdate?.Invoke(new List<string>(), new List<string>(), new List<string>(), _sku.P);
				}
			}
		}

		private void SaveBatchImages(List<string> mergedStatus, long productId, bool isOk)
		{
			bool saveOkImage = _Config.IsSaveOkImage;
			bool saveNgImage = _Config.IsSaveNgImage;

			if (!saveOkImage && !saveNgImage) return;

			string shift = GetCurrentShift();
			string dateDir = DateTime.Now.ToString("yyMMdd");
		string resultDir = isOk ? "OK" : "NG";
			string ngTypes = GetNgTypesString(mergedStatus);

			if ((isOk && saveOkImage) || (!isOk && saveNgImage))
			{
				string dir = Path.Combine(_savePath, dateDir, shift, "侧面工位", resultDir);
				Directory.CreateDirectory(dir);
				for (int i = 0; i < _currentDisplayImages.Count; i++)
				{
					string fileName = $"{productId}_{i + 1}_{ngTypes}.jpg";
					string filePath = Path.Combine(dir, fileName);
					var jpegData = _currentDisplayImages[i].ToJpegBytesFast(85);
					_imageSaver.AddSaveTask(filePath, jpegData, true, 85);
				}
			}
		}

		private string GetCurrentShift()
		{
			var now = DateTime.Now.TimeOfDay;
			if (now >= TimeSpan.Parse("00:00:00") && now <= TimeSpan.Parse("07:59:59")) return "晚班";
			if (now >= TimeSpan.Parse("08:00:00") && now <= TimeSpan.Parse("15:59:59")) return "早班";
			return "中班";
		}

		private string GetNgTypesString(List<string> statusList)
		{
			var ngTypes = statusList.Where(s => s != "OK").Distinct().ToList();
			if (ngTypes.Count == 0) return "OK";
			return string.Join("_", ngTypes);
		}

		public void SetMotionPositions(float startPos, float endPos)
		{
			_startPosition = startPos;
			_endPosition = endPos;
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
			_processQueue.Dispose();
		}
	}

	public class SideDetectionResult
	{
		public List<string> BoxStatus { get; set; } = new List<string>();
		public double InferenceTimeMs { get; set; }
		public double TotalTimeMs { get; set; }
	}
}