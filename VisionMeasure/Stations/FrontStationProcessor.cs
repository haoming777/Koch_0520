using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Linq;
using System.Threading.Tasks;
using CommonLib;
using Config;
using Models;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using VisionMeasure.Utils;
using YoloInference;
using AI;
using CvRect = OpenCvSharp.Rect;

namespace VisionMeasure.Stations
{
	public class FrontStationProcessor : IDisposable
	{
		private HighSpeedImageSaver _imageSaver;
		private readonly AiModelManager _models;
		private readonly DetectionParameters _detectionParams;

		private Mat _leftBuffer = null;
		private Mat _rightBuffer = null;
		private readonly object _syncLock = new object();

		private int _okCount = 0;
		private int _ngCount = 0;
		private SkuData _currentSku = null;

		public event Action<Bitmap, bool[], int, int> OnResultReady;
		public event Action<List<string>, int> OnStatusUpdate;

		public float ConfThreshold { get; set; } = 0.5f;
		public float IouThreshold { get; set; } = 0.45f;

		public FrontStationProcessor(AiModelManager modelManager, DetectionParameters detectionParams)
		{
			_models = modelManager;
			_detectionParams = detectionParams;
			_imageSaver = new HighSpeedImageSaver();
		}

		public void Start()
		{
			ClearCounters();
			Logger.Info("FrontStationProcessor Started.");
		}

		public void UpdateSku(SkuData newSku)
		{
			_currentSku = newSku;
		}

		public void ClearCounters()
		{
			_okCount = 0;
			_ngCount = 0;
		}

		public void OnCam1(Bitmap leftImg, object extraArg = null)
		{
			if (leftImg == null) return;
			Logger.Debug($"[Front] OnCam1 收到图像 {leftImg.Width}x{leftImg.Height}");
			lock (_syncLock)
			{
				_leftBuffer?.Dispose();
				_leftBuffer = leftImg.ToMat();
				Cv2.Flip(_leftBuffer, _leftBuffer, FlipMode.XY); // 旋转180°
			}
			CheckAndProcessAsync();
		}

		public void OnCam2(Bitmap rightImg, object extraArg = null)
		{
			if (rightImg == null) return;
			Logger.Debug($"[Front] OnCam2 收到图像 {rightImg.Width}x{rightImg.Height}");
			lock (_syncLock)
			{
				_rightBuffer?.Dispose();
				_rightBuffer = rightImg.ToMat();
				Cv2.Flip(_rightBuffer, _rightBuffer, FlipMode.XY); // 旋转180°
			}
			CheckAndProcessAsync();
		}

		private async void CheckAndProcessAsync()
		{
			Mat leftToProcess = null;
			Mat rightToProcess = null;

			lock (_syncLock)
			{
				if (_leftBuffer != null && _rightBuffer != null)
				{
					leftToProcess = _leftBuffer;
					rightToProcess = _rightBuffer;
					_leftBuffer = null;
					_rightBuffer = null;
					Logger.Debug("[Front] 左右图像配对成功，开始处理");
				}
			}

			if (leftToProcess == null || rightToProcess == null) return;

			var swTotal = System.Diagnostics.Stopwatch.StartNew();
			try
			{
				int pCount = _currentSku?.P ?? 8;
				int halfP = pCount / 2;

				// 并行执行模型推理
				var pNumberTask = Task.Run(() => RecognizePNumber(leftToProcess, rightToProcess, halfP));
				var damageTask = Task.Run(() => DetectBoxDamage(leftToProcess, rightToProcess, halfP));

				await Task.WhenAll(pNumberTask, damageTask);

				var pNumberResults = pNumberTask.Result;
				var damageResults = damageTask.Result;

				// 汇总结果
				var statusList = new List<string>();
				var ngArray = new bool[pCount];

				for (int i = 0; i < pCount; i++)
				{
					bool isNg = false;
					var defects = new List<string>();

					if (pNumberResults.ContainsKey(i))
					{
						isNg = true;
						defects.AddRange(pNumberResults[i].Select(d => d.DefectType));
					}
					if (damageResults.ContainsKey(i))
					{
						isNg = true;
						defects.AddRange(damageResults[i].Select(d => d.DefectType));
					}

					ngArray[i] = isNg;
					statusList.Add(isNg ? string.Join(",", defects) : "OK");
				}

				// 更新统计
				int currentNgCount = ngArray.Count(n => n);
				if (currentNgCount == 0)
					_okCount += pCount;
				else
				{
					_okCount += (pCount - currentNgCount);
					_ngCount += currentNgCount;
				}

				// 绘制结果
				Bitmap mergedImage = DrawAndMergeResults(leftToProcess, rightToProcess, pNumberResults, damageResults, statusList, halfP);

				// 存图
				SaveImages(leftToProcess, rightToProcess, mergedImage, ngArray);

				// 触发主界面更新
				OnResultReady?.Invoke(mergedImage, ngArray, _okCount, _ngCount);
				OnStatusUpdate?.Invoke(statusList, pCount);

				Logger.Info($"[Front] 处理完成 总耗时={swTotal.Elapsed.TotalMilliseconds:F2}ms P={pCount} OK={pCount - currentNgCount} NG={currentNgCount}");
			}
			catch (Exception ex)
			{
				Logger.Error($"[Front] 处理异常: {ex.Message}\r\n{ex.StackTrace}");
			}
			finally
			{
				leftToProcess?.Dispose();
				rightToProcess?.Dispose();
			}
		}

		private Dictionary<int, List<BoxDefect>> RecognizePNumber(Mat left, Mat right, int halfP)
		{
			var results = new Dictionary<int, List<BoxDefect>>();
			if (_models.FrontOcrModel == null) return results;

			try
			{
				// 预留Vimo模型调用位置
				// var leftResult = _models.FrontPNumberModel.Run(left, out var ocrResults);
				// 对比识别结果与标准P号码

				if (_currentSku != null && !string.IsNullOrEmpty(_currentSku.FrontPCode))
				{
					// 实际应用中这里应该调用Vimo的OCR方法
					Logger.Debug($"正面P号码识别: Vimo模型预留位置, 标准P号码={_currentSku.FrontPCode}");
				}
			}
			catch (Exception ex)
			{
				Logger.Error($"P号码识别异常: {ex.Message}");
			}

			return results;
		}

		private Dictionary<int, List<BoxDefect>> DetectBoxDamage(Mat left, Mat right, int halfP)
		{
			var results = new Dictionary<int, List<BoxDefect>>();
			if (_models.FrontBoxBreakModel == null) return results;

			try
			{
				var leftResult = _models.FrontBoxBreakModel.Predict(left, ConfThreshold, IouThreshold);
				var rightResult = _models.FrontBoxBreakModel.Predict(right, ConfThreshold, IouThreshold);

				ProcessYoloResults(leftResult, results, 0, halfP, "盒子破损");
				ProcessYoloResults(rightResult, results, halfP, _currentSku?.P ?? 8, "盒子破损");
			}
			catch (Exception ex)
			{
				Logger.Error($"盒子破损检测异常: {ex.Message}");
			}

			return results;
		}

		private void ProcessYoloResults(YoloInference.YoloResult result, Dictionary<int, List<BoxDefect>> results, int startIdx, int endIdx, string defectType)
		{
			if (result == null || result.Boxes == null) return;

			int totalBoxes = endIdx - startIdx;
			if (totalBoxes <= 0) return;

			foreach (var box in result.Boxes)
			{
				float centerX = (box.X + box.Width / 2f) / result.OrigImg.Width;
				int boxIndex = startIdx + (int)(centerX * totalBoxes);

				if (boxIndex >= startIdx && boxIndex < endIdx)
				{
					if (!results.ContainsKey(boxIndex))
						results[boxIndex] = new List<BoxDefect>();

					results[boxIndex].Add(new BoxDefect(boxIndex, defectType,
						new float[] { box.X, box.Y, box.X + box.Width, box.Y + box.Height }));
				}
			}
		}

		private Bitmap DrawAndMergeResults(Mat left, Mat right, Dictionary<int, List<BoxDefect>> pNumberResults, 
			Dictionary<int, List<BoxDefect>> damageResults, List<string> statusList, int halfP)
		{
			var leftBitmap = left.ToBitmap();
			var rightBitmap = right.ToBitmap();

			using (var g = Graphics.FromImage(leftBitmap))
			{
				g.SmoothingMode = SmoothingMode.AntiAlias;
				DrawDefects(g, pNumberResults, damageResults, 0, halfP, left.Width, left.Height);
			}

			using (var g = Graphics.FromImage(rightBitmap))
			{
				g.SmoothingMode = SmoothingMode.AntiAlias;
				DrawDefects(g, pNumberResults, damageResults, halfP, _currentSku?.P ?? 8, right.Width, right.Height);
			}

			return MergeImages(leftBitmap, rightBitmap);
		}

		private void DrawDefects(Graphics g, Dictionary<int, List<BoxDefect>> pNumberResults, 
			Dictionary<int, List<BoxDefect>> damageResults, int startIdx, int endIdx, int imgWidth, int imgHeight)
		{
			for (int i = startIdx; i < endIdx; i++)
			{
				if (pNumberResults.ContainsKey(i))
				{
					foreach (var defect in pNumberResults[i])
					{
						DrawDefect(g, defect, imgWidth, imgHeight);
					}
				}
				if (damageResults.ContainsKey(i))
				{
					foreach (var defect in damageResults[i])
					{
						DrawDefect(g, defect, imgWidth, imgHeight);
					}
				}
			}
		}

		private void DrawDefect(Graphics g, BoxDefect defect, int imgWidth, int imgHeight)
		{
			if (defect.BoundingBox == null || defect.BoundingBox.Length < 4) return;

			Rectangle rect = new Rectangle(
				(int)defect.BoundingBox[0],
				(int)defect.BoundingBox[1],
				(int)(defect.BoundingBox[2] - defect.BoundingBox[0]),
				(int)(defect.BoundingBox[3] - defect.BoundingBox[1])
			);

			using (Pen pen = new Pen(Color.Red, 2))
			{
				g.DrawRectangle(pen, rect);
				g.DrawString(defect.DefectType, new Font("Arial", 10), Brushes.Red, rect.Left, rect.Top - 20);
			}
		}

		private Bitmap MergeImages(Bitmap left, Bitmap right)
		{
			Bitmap merged = new Bitmap(left.Width + right.Width, Math.Max(left.Height, right.Height), PixelFormat.Format32bppArgb);
			using (Graphics g = Graphics.FromImage(merged))
			{
				g.DrawImage(left, 0, 0);
				g.DrawImage(right, left.Width, 0);
			}
			return merged;
		}

		private void SaveImages(Mat left, Mat right, Bitmap merged, bool[] ngArray)
		{
			try
			{
				bool hasNg = ngArray.Any(n => n);
				bool saveOk = _detectionParams.Save.SaveOkImage && !hasNg;
				bool saveNg = _detectionParams.Save.SaveNgImage && hasNg;
				bool saveOkRaw = _detectionParams.Save.SaveOkRawImage && !hasNg;
				bool saveNgRaw = _detectionParams.Save.SaveNgRawImage && hasNg;

				if (!saveOk && !saveNg && !saveOkRaw && !saveNgRaw)
					return;

				string basePath = _detectionParams.Save.ImageSavePath;
				string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
				string defectType = hasNg ? string.Join("_", ngArray.Select((n, i) => n ? $"NG{i + 1}" : "").Where(s => !string.IsNullOrEmpty(s))) : "OK";

				if (saveOk || saveNg)
				{
					string path = System.IO.Path.Combine(basePath, "Render", $"Front_{timestamp}_{defectType}.jpg");
					_imageSaver.Enqueue(merged, path, System.Drawing.Imaging.ImageFormat.Jpeg);
				}

				if (saveOkRaw || saveNgRaw)
				{
					string leftPath = System.IO.Path.Combine(basePath, "Raw", $"Front_Left_{timestamp}.bmp");
					string rightPath = System.IO.Path.Combine(basePath, "Raw", $"Front_Right_{timestamp}.bmp");
					_imageSaver.Enqueue(left.ToBitmap(), leftPath, System.Drawing.Imaging.ImageFormat.Bmp);
					_imageSaver.Enqueue(right.ToBitmap(), rightPath, System.Drawing.Imaging.ImageFormat.Bmp);
				}
			}
			catch (Exception ex)
			{
				Logger.Error($"正面工位存图异常: {ex.Message}");
			}
		}

		public void Dispose()
		{
			_imageSaver?.Dispose();
			_leftBuffer?.Dispose();
			_rightBuffer?.Dispose();
		}
	}
}
