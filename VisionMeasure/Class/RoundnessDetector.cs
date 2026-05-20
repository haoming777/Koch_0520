using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using OpenCvSharp;

namespace RoundnessDetectionV3
{
	public class DetectionResultV3
	{
		public double Roundness { get; set; }
		public Rect BoundingRect { get; set; }
		public int LongEdge { get; set; }
		public int ShortEdge { get; set; }
		public double Area { get; set; }
		public double Perimeter { get; set; }
		public int Threshold { get; set; }
		public Point[] Contour { get; set; }
	}

	public static class RoundnessDetectorV3
	{
		private static int Clamp(int value, int min, int max)
		{
			if (value < min) return min;
			if (value > max) return max;
			return value;
		}

		private static int PercentileU8(Mat gray, double percentile)
		{
			percentile = Math.Max(0, Math.Min(100, percentile));
			int total = gray.Rows * gray.Cols;
			if (total <= 0) return 0;

			int[] hist = new int[256];
			for (int y = 0; y < gray.Rows; y++)
				for (int x = 0; x < gray.Cols; x++)
					hist[gray.At<byte>(y, x)]++;

			int target = (int)Math.Ceiling(total * (percentile / 100.0));
			int cumulative = 0;
			for (int i = 0; i < 256; i++)
			{
				cumulative += hist[i];
				if (cumulative >= target) return i;
			}
			return 255;
		}

		public static Mat ImReadUnicode(string imagePath, ImreadModes mode = ImreadModes.Color)
		{
			try
			{
				byte[] bytes = File.ReadAllBytes(imagePath);
				using (Mat data = Mat.FromImageData(bytes, mode))
				{
					return data?.Clone();
				}
			}
			catch (Exception e)
			{
				Console.WriteLine($"无法读取图像(Unicode读取): {imagePath}\n异常: {e.Message}");
				return null;
			}
		}

		public static bool ImWriteUnicode(string imagePath, Mat image)
		{
			try
			{
				string ext = Path.GetExtension(imagePath)?.ToLowerInvariant();
				if (string.IsNullOrWhiteSpace(ext)) ext = ".png";
				else if (ext == ".jpeg") ext = ".jpg";

				Cv2.ImEncode(ext, image, out byte[] encoded);
				File.WriteAllBytes(imagePath, encoded);
				return true;
			}
			catch (Exception e)
			{
				Console.WriteLine($"无法保存图像(Unicode写入): {imagePath}\n异常: {e.Message}");
				return false;
			}
		}

		public static DetectionResultV3 DetectRoundnessAndRect(Mat img)
		{
			if (img == null || img.Empty())
			{
				Console.WriteLine("输入图像无效");
				return null;
			}

			using (var imgGray = img.Channels() == 3 ? img.CvtColor(ColorConversionCodes.BGR2GRAY) : img.Clone())
			using (var blur = new Mat())
			using (var binary = new Mat())
			using (var kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new Size(3, 3)))
			{
				int h = imgGray.Rows, w = imgGray.Cols;
				double imgArea = h * w;
				double cx0 = w / 2.0, cy0 = h / 2.0;

				Cv2.GaussianBlur(imgGray, blur, new Size(5, 5), 0);
				int p92 = PercentileU8(blur, 92);
				int threshVal = Clamp(p92, 170, 245);

				Cv2.Threshold(blur, binary, threshVal, 255, ThresholdTypes.Binary);
				Cv2.MorphologyEx(binary, binary, MorphTypes.Open, kernel, iterations: 1);
				Cv2.MorphologyEx(binary, binary, MorphTypes.Close, kernel, iterations: 2);

				// 关键修复：此时 binary 已经具有正确的尺寸和类型，再创建 centerMask
				using (Mat centerMask = new Mat(binary.Size(), binary.Type()))
				{
					centerMask.SetTo(0); // 清零
					int radius = (int)(Math.Min(w, h) * 0.33);
					Cv2.Circle(centerMask, new Point((int)cx0, (int)cy0), radius, new Scalar(255, 255, 255), -1);
					Cv2.BitwiseAnd(binary, centerMask, binary);

					Cv2.FindContours(binary, out Point[][] contours, out _, RetrievalModes.External, ContourApproximationModes.ApproxSimple);
					if (contours == null || contours.Length == 0)
					{
						Console.WriteLine("未找到候选亮区域");
						return null;
					}

					Point[] bestContour = null;
					double bestDist = double.MaxValue;
					double minArea = imgArea * 0.001, maxArea = imgArea * 0.30;

					foreach (Point[] contour in contours)
					{
						double area = Cv2.ContourArea(contour);
						if (area < minArea || area > maxArea) continue;

						Moments m = Cv2.Moments(contour);
						if (Math.Abs(m.M00) < 1e-9) continue;

						double cx = m.M10 / m.M00, cy = m.M01 / m.M00;
						double dist = Math.Sqrt((cx - cx0) * (cx - cx0) + (cy - cy0) * (cy - cy0));
						if (dist < bestDist)
						{
							bestDist = dist;
							bestContour = contour;
						}
					}

					if (bestContour == null)
					{
						Console.WriteLine("未找到有效的中心亮区域");
						return null;
					}

					double bestArea = Cv2.ContourArea(bestContour);
					double perimeter = Cv2.ArcLength(bestContour, true);
					double roundness = perimeter == 0 ? 0 : (4 * Math.PI * bestArea) / (perimeter * perimeter);
					Rect rect = Cv2.BoundingRect(bestContour);

					return new DetectionResultV3
					{
						Roundness = roundness,
						BoundingRect = rect,
						LongEdge = Math.Max(rect.Width, rect.Height),
						ShortEdge = Math.Min(rect.Width, rect.Height),
						Area = bestArea,
						Perimeter = perimeter,
						Threshold = threshVal,
						Contour = bestContour
					};
				}
			}
		}

		public static bool VisualizeAndSave(string imagePath, string savePath, DetectionResultV3 result)
		{
			using (Mat color = ImReadUnicode(imagePath, ImreadModes.Color))
			{
				if (color == null || color.Empty())
				{
					Console.WriteLine($"无法读取图像: {imagePath}");
					return false;
				}

				if (result?.Contour != null)
				{
					Cv2.DrawContours(color, new[] { result.Contour }, -1, new Scalar(0, 255, 0), 2);
					Cv2.Rectangle(color, result.BoundingRect, new Scalar(255, 0, 0), 2);

					string[] info =
					{
						$"Roundness: {result.Roundness:F3}",
						$"Long Edge: {result.LongEdge}",
						$"Short Edge: {result.ShortEdge}",
						$"Area: {result.Area:F0}",
						$"Thresh: {result.Threshold}"
					};
					for (int i = 0; i < info.Length; i++)
						Cv2.PutText(color, info[i], new Point(10, 30 + i * 25), HersheyFonts.HersheySimplex, 0.6, new Scalar(0, 0, 255), 2);
				}
				else
				{
					Cv2.PutText(color, "FAIL", new Point(20, 50), HersheyFonts.HersheySimplex, 1.4, new Scalar(0, 0, 255), 3);
				}

				bool ok = ImWriteUnicode(savePath, color);
				Console.WriteLine(ok ? $"结果已保存到: {savePath}" : $"结果保存失败: {savePath}");
				return ok;
			}
		}

		public static string MakeResultPath(string inputPath)
		{
			string dir = Path.GetDirectoryName(inputPath) ?? "";
			string name = Path.GetFileNameWithoutExtension(inputPath);
			string ext = Path.GetExtension(inputPath);
			return Path.Combine(dir, $"{name}_result{ext}");
		}

		public static Mat VisualizeDetection(Mat img, DetectionResultV3 result, string savePath = null)
		{
			if (img == null || img.Empty() || result == null)
			{
				return null;
			}

			// 如果是灰度图，转换为彩色图以便绘制彩色标注
			Mat resultImg;
			if (img.Channels() == 1)
			{
				resultImg = new Mat();
				Cv2.CvtColor(img, resultImg, ColorConversionCodes.GRAY2BGR);
			}
			else
			{
				resultImg = img.Clone();
			}

			// 重新执行检测以获取轮廓（用于绘制）
			Mat imgGray;
			if (img.Channels() == 3)
			{
				imgGray = new Mat();
				Cv2.CvtColor(img, imgGray, ColorConversionCodes.BGR2GRAY);
			}
			else
			{
				imgGray = img.Clone();
			}

			Mat binary = new Mat();
			Cv2.Threshold(imgGray, binary, 200, 255, ThresholdTypes.Binary);

			OpenCvSharp.Point[][] contours;
			HierarchyIndex[] hierarchy;
			Cv2.FindContours(binary, out contours, out hierarchy, RetrievalModes.Tree, ContourApproximationModes.ApproxSimple);

			if (contours.Length >= 2)
			{
				var contoursSorted = contours.OrderByDescending(c => Cv2.ContourArea(c)).ToArray();
				OpenCvSharp.Point[] outerRingOuter = contoursSorted[0];
				double outerRingArea = Cv2.ContourArea(outerRingOuter);

				OpenCvSharp.Point[] ringInnerContour = null;
				double ringInnerArea = 0;

				for (int i = 1; i < contoursSorted.Length; i++)
				{
					OpenCvSharp.Point[] contour = contoursSorted[i];
					double area1 = Cv2.ContourArea(contour);

					if (area1 < outerRingArea * 0.3)
					{
						break;
					}

					Moments M = Cv2.Moments(contour);
					if (M.M00 != 0)
					{
						int cx = (int)(M.M10 / M.M00);
						int cy = (int)(M.M01 / M.M00);

						double pointTest = Cv2.PointPolygonTest(outerRingOuter, new Point2f(cx, cy), false);
						if (pointTest > 0)
						{
							if (area1 > ringInnerArea)
							{
								ringInnerContour = contour;
								ringInnerArea = area1;
							}
						}
					}
				}

				if (ringInnerContour != null)
				{
					Mat mask = Mat.Zeros(binary.Size(), MatType.CV_8UC1);
					Cv2.FillPoly(mask, new OpenCvSharp.Point[][] { ringInnerContour }, Scalar.White);

					Mat maskedBinary = new Mat();
					Cv2.BitwiseAnd(binary, mask, maskedBinary);

					Mat contourMask = Mat.Zeros(binary.Size(), MatType.CV_8UC1);
					Cv2.DrawContours(contourMask, new OpenCvSharp.Point[][] { ringInnerContour }, -1, Scalar.White, 2);

					Mat contourMaskNot = new Mat();
					Cv2.BitwiseNot(contourMask, contourMaskNot);
					Cv2.BitwiseAnd(maskedBinary, contourMaskNot, maskedBinary);

					Mat labels = new Mat();
					Mat stats = new Mat();
					Mat centroids = new Mat();
					int numLabels = Cv2.ConnectedComponentsWithStats(maskedBinary, labels, stats, centroids, PixelConnectivity.Connectivity8);

					OpenCvSharp.Point[] innerContour = null;
					double innerArea = 0;

					for (int i = 1; i < numLabels; i++)
					{
						int area2 = stats.At<int>(i, 4);
						if (area2 < ringInnerArea * 0.05)
						{
							continue;
						}

						Mat componentMask = new Mat();
						Cv2.Compare(labels, new Scalar(i), componentMask, CmpType.EQ);
						componentMask.ConvertTo(componentMask, MatType.CV_8UC1, 255.0);

						OpenCvSharp.Point[][] componentContours;
						HierarchyIndex[] componentHierarchy;
						Cv2.FindContours(componentMask, out componentContours, out componentHierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

						if (componentContours.Length == 0)
						{
							componentMask?.Dispose();
							continue;
						}

						OpenCvSharp.Point[] componentContour = componentContours.OrderByDescending(c => Cv2.ContourArea(c)).First();

						Moments M = Cv2.Moments(componentContour);
						if (M.M00 != 0)
						{
							int cx = (int)(M.M10 / M.M00);
							int cy = (int)(M.M01 / M.M00);

							double pointTest = Cv2.PointPolygonTest(ringInnerContour, new Point2f(cx, cy), false);
							if (pointTest > 0 && area2 < ringInnerArea * 0.95 && area2 > innerArea)
							{
								innerContour = componentContour;
								innerArea = area2;
							}
						}

						componentMask?.Dispose();
					}

					// 绘制内部轮廓（蓝色）
					if (innerContour != null)
					{
						Cv2.DrawContours(resultImg, new OpenCvSharp.Point[][] { innerContour }, -1, new Scalar(255, 0, 0), 2);
					}

					// 清理资源
					mask?.Dispose();
					maskedBinary?.Dispose();
					contourMask?.Dispose();
					contourMaskNot?.Dispose();
					labels?.Dispose();
					stats?.Dispose();
					centroids?.Dispose();
				}

				// 清理资源
				imgGray?.Dispose();
				binary?.Dispose();
			}

			// 绘制外接矩形（绿色）
			Rect rect = result.BoundingRect;
			Cv2.Rectangle(resultImg, rect, new Scalar(0, 255, 0), 5);

			//// 在图像上显示检测信息（红色文字）
			//string[] infoText = new string[]
			//{
			//    $"Roundness: {result.Roundness:F3}",
			//    $"Long Edge: {result.LongEdge}",
			//    $"Short Edge: {result.ShortEdge}",
			//    $"Area: {result.Area:F0}"
			//};

			//int yOffset = 30;
			//for (int i = 0; i < infoText.Length; i++)
			//{
			//    Cv2.PutText(resultImg, infoText[i], new OpenCvSharp. Point(10, yOffset + i * 25),
			//               HersheyFonts.HersheySimplex, 0.6, new Scalar(0, 0, 255), 2);
			//}

			//// 保存图像
			//if (!string.IsNullOrEmpty(savePath))
			//{
			//    Cv2.ImWrite(savePath, resultImg);
			//}

			return resultImg;
		}

		public static bool ProcessSingleImage(string imagePath)
		{
			using (Mat gray = ImReadUnicode(imagePath, ImreadModes.Grayscale))
			{
				if (gray == null || gray.Empty())
				{
					Console.WriteLine($"无法读取图像: {imagePath}");
					return false;
				}

				DetectionResultV3 result = DetectRoundnessAndRect(gray);
				if (result != null)
				{
					Console.WriteLine("检测结果:");
					Console.WriteLine($"圆度: {result.Roundness:F4}");
					Console.WriteLine($"外接矩形: x={result.BoundingRect.X}, y={result.BoundingRect.Y}, w={result.BoundingRect.Width}, h={result.BoundingRect.Height}");
					Console.WriteLine($"长边: {result.LongEdge} 像素");
					Console.WriteLine($"短边: {result.ShortEdge} 像素");
					Console.WriteLine($"面积: {result.Area:F2} 平方像素");
					Console.WriteLine($"周长: {result.Perimeter:F2} 像素");
				}
				else
				{
					Console.WriteLine("检测失败");
				}

				string resultPath = MakeResultPath(imagePath);
				VisualizeAndSave(imagePath, resultPath, result);
				return result != null;
			}
		}

		public static void ProcessFolderImages(string folderPath)
		{
			if (!Directory.Exists(folderPath))
			{
				Console.WriteLine($"文件夹不存在: {folderPath}");
				return;
			}

			var exts = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ".jpg", ".jpeg", ".png", ".bmp", ".tif", ".tiff" };
			var files = Directory.EnumerateFiles(folderPath, "*", SearchOption.AllDirectories)
								 .Where(p => exts.Contains(Path.GetExtension(p)))
								 .OrderBy(p => p)
								 .ToList();

			if (files.Count == 0)
			{
				Console.WriteLine($"未找到图片: {folderPath}");
				return;
			}

			int success = 0;
			Console.WriteLine($"开始批量处理，共 {files.Count} 张图片...");
			for (int i = 0; i < files.Count; i++)
			{
				string file = files[i];
				Console.WriteLine($"\n[{i + 1}/{files.Count}] 处理: {file}");
				try
				{
					if (ProcessSingleImage(file)) success++;
				}
				catch (Exception e)
				{
					Console.WriteLine($"处理异常: {file}\n{e.Message}");
				}
			}
			Console.WriteLine($"\n批量处理完成\n总数: {files.Count}\n成功: {success}\n失败: {files.Count - success}");
		}
	}
}