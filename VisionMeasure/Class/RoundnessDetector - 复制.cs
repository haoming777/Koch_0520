using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using OpenCvSharp;
using XL.Tool;
using static CommonLib.Class_Config;
using VisionMeasure.Utils;using CommonLib;
using VisionMeasure.Utils;using CommonLib;

namespace RoundnessDetection1
{
    
    /// <summary>
    /// 检测结果类
    /// </summary>
    public class DetectionResult
    {

        /// <summary>
        /// 圆度值 (0-1, 1表示完美圆形)
        /// </summary>
        public double Roundness { get; set; }

		/// <summary>
		/// 长宽比
		/// </summary>
		public double LengthWidthRatio { get; set; }


		/// <summary>
		/// 外接矩形信息 (x, y, width, height)
		/// </summary>
		public Rect BoundingRect { get; set; }

        /// <summary>
        /// 外接矩形的长边
        /// </summary>
        public int LongEdge { get; set; }

        /// <summary>
        /// 外接矩形的短边
        /// </summary>
        public int ShortEdge { get; set; }

        /// <summary>
        /// 区域面积
        /// </summary>
        public double Area { get; set; }

        /// <summary>
        /// 区域周长
        /// </summary>
        public double Perimeter { get; set; }
    }

    /// <summary>
    /// 圆度检测类
    /// </summary>
    public class RoundnessDetector
    {
       static XLToolClass toolClass = new XLToolClass();
		/// <summary>
		/// 检测图像中白色圆环内部不规则区域的圆度和外接矩形
		/// </summary>
		/// <param name="img">OpenCV图像对象（Mat），可以是灰度图或彩色图</param>
		/// <returns>检测结果，如果未找到内部区域，返回null</returns>
		public static DetectionResult DetectRoundnessAndRect(Mat img)
        {
            Stopwatch stopwatch = new Stopwatch();
            stopwatch.Start();
			// 检查输入
			if (img == null || img.Empty())
            {
                Console.WriteLine("输入图像无效");
                return null;
            }

            // 如果是彩色图，转换为灰度图
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

			Logger.Info($"找圆 前处理用时：{stopwatch.ElapsedMilliseconds}ms");
            stopwatch.Restart();
			// 二值化处理，白色区域为255
			Mat binary = new Mat();
            Cv2.Threshold(imgGray, binary, _Config.Camera3Thresh, _Config.Camera3Maxval, ThresholdTypes.Binary);
			Logger.Info($"找圆 二值化处理用时：{stopwatch.ElapsedMilliseconds}ms");
			stopwatch.Restart();

			// 查找所有轮廓，使用RETR_TREE获取层级关系
			OpenCvSharp.Point[][] contours;
            HierarchyIndex[] hierarchy;
            Cv2.FindContours(binary, out contours, out hierarchy, RetrievalModes.Tree, ContourApproximationModes.ApproxSimple);

            if (contours.Length == 0)
            {
                Console.WriteLine("未找到任何轮廓");
                imgGray?.Dispose();
                binary?.Dispose();
                return null;
            }
			Logger.Info($"找圆 查找所有轮廓用时：{stopwatch.ElapsedMilliseconds}ms");
			stopwatch.Restart();
			// 按面积排序，找到最大的轮廓（外部白色圆环的外轮廓）
			var contoursSorted = contours.OrderByDescending(c => Cv2.ContourArea(c)).ToArray();

            if (contoursSorted.Length < 2)
            {
                Console.WriteLine("轮廓数量不足，无法找到内部区域");
                imgGray?.Dispose();
                binary?.Dispose();
                return null;
            }

			// 外部圆环的外轮廓（最大的）
			OpenCvSharp.Point[] outerRingOuter = contoursSorted[0];
            double outerRingArea = Cv2.ContourArea(outerRingOuter);

			// 找到外环的内轮廓（第二大轮廓，应该是外环内部的空洞边界）
			OpenCvSharp.Point[] ringInnerContour = null;
            double ringInnerArea = 0;

            for (int i = 1; i < contoursSorted.Length; i++)
            {
				OpenCvSharp.Point[] contour = contoursSorted[i];
                double area1 = Cv2.ContourArea(contour);

                // 外环内轮廓的面积应该接近外环外轮廓（因为环的宽度相对较小）
                // 但应该明显小于外轮廓
                if (area1 < outerRingArea * 0.3)  // 如果太小，可能是内部不规则区域
                {
                    break;
                }

                // 检查轮廓中心是否在外环外轮廓内部
                Moments M = Cv2.Moments(contour);
                if (M.M00 != 0)
                {
                    int cx = (int)(M.M10 / M.M00);
                    int cy = (int)(M.M01 / M.M00);

                    // 检查点是否在外环外轮廓内部
                    double pointTest = Cv2.PointPolygonTest(outerRingOuter, new Point2f(cx, cy), false);
                    if (pointTest > 0)
                    {
                        // 这应该是外环的内轮廓
                        if (area1 > ringInnerArea)
                        {
                            ringInnerContour = contour;
                            ringInnerArea = area1;
                        }
                    }
                }
            }

            if (ringInnerContour == null)
            {
                Console.WriteLine("未找到外环的内轮廓");
                imgGray?.Dispose();
                binary?.Dispose();
                return null;
            }

            // 现在在外环内轮廓内部找白色不规则区域
            // 创建一个mask，只保留外环内轮廓内部的区域（填充内部）
            Mat mask = Mat.Zeros(binary.Size(), MatType.CV_8UC1);
            Cv2.FillPoly(mask, new OpenCvSharp.Point[][] { ringInnerContour }, Scalar.White);

            // 在mask区域内，找到白色区域（内部不规则区域）
            // 使用bitwise_and来获取mask内的白色区域
            Mat maskedBinary = new Mat();
            Cv2.BitwiseAnd(binary, mask, maskedBinary);

            // 使用形态学操作，先消除内轮廓边界的影响
            // 创建一个内轮廓的mask（只画轮廓线，不填充）
            Mat contourMask = Mat.Zeros(binary.Size(), MatType.CV_8UC1);
            Cv2.DrawContours(contourMask, new OpenCvSharp.Point[][] { ringInnerContour }, -1, Scalar.White, 2);  // 2像素宽的轮廓

            // 从masked_binary中减去内轮廓边界
            Mat contourMaskNot = new Mat();
            Cv2.BitwiseNot(contourMask, contourMaskNot);
            Cv2.BitwiseAnd(maskedBinary, contourMaskNot, maskedBinary);

            // 使用连通组件分析，找到所有白色连通区域
            Mat labels = new Mat();
            Mat stats = new Mat();
            Mat centroids = new Mat();
            int numLabels = Cv2.ConnectedComponentsWithStats(maskedBinary, labels, stats, centroids, PixelConnectivity.Connectivity8);

            if (numLabels < 2)  // 至少要有1个连通区域（背景+至少1个前景）
            {
                Console.WriteLine("在外环内部未找到白色连通区域");
                imgGray?.Dispose();
                binary?.Dispose();
                mask?.Dispose();
                maskedBinary?.Dispose();
                contourMask?.Dispose();
                contourMaskNot?.Dispose();
                labels?.Dispose();
                stats?.Dispose();
                centroids?.Dispose();
                return null;
            }

			// 找到最大的连通区域（排除背景，背景标签是0）
			// 同时要确保这个区域在外环内轮廓内部
			OpenCvSharp.Point[] innerContour = null;
            double innerArea = 0;

            for (int i = 1; i < numLabels; i++)  // 跳过背景（标签0）
            {
                // 获取连通组件的统计信息
                int area2 = stats.At<int>(i, 4);  // CC_STAT_AREA = 4
                if (area2 < ringInnerArea * 0.05)  // 太小，跳过
                {
                    continue;
                }

                // 获取这个连通区域的mask（标签值等于i的区域）
                Mat componentMask = new Mat();
                Cv2.Compare(labels, new Scalar(i), componentMask, CmpType.EQ);
                componentMask.ConvertTo(componentMask, MatType.CV_8UC1, 255.0);

				// 找到这个连通区域的轮廓
				OpenCvSharp.Point[][] componentContours;
                HierarchyIndex[] componentHierarchy;
                Cv2.FindContours(componentMask, out componentContours, out componentHierarchy, RetrievalModes.External, ContourApproximationModes.ApproxSimple);

                if (componentContours.Length == 0)
                {
                    componentMask?.Dispose();
                    continue;
                }

				OpenCvSharp.Point[] componentContour = componentContours.OrderByDescending(c => Cv2.ContourArea(c)).First();

                // 检查这个区域的中心是否在外环内轮廓内部
                Moments M = Cv2.Moments(componentContour);
                if (M.M00 != 0)
                {
                    int cx = (int)(M.M10 / M.M00);
                    int cy = (int)(M.M01 / M.M00);

                    // 确保中心在内轮廓内部，且面积小于内轮廓（是填充区域，不是边界）
                    double pointTest = Cv2.PointPolygonTest(ringInnerContour, new Point2f(cx, cy), false);
                    if (pointTest > 0 && area2 < ringInnerArea * 0.95 && area2 > innerArea)
                    {
                        innerContour = componentContour;
                        innerArea = area2;
                    }
                }

                componentMask?.Dispose();
            }

            // 清理资源
            imgGray?.Dispose();
            binary?.Dispose();
            mask?.Dispose();
            maskedBinary?.Dispose();
            contourMask?.Dispose();
            contourMaskNot?.Dispose();
            labels?.Dispose();
            stats?.Dispose();
            centroids?.Dispose();

            if (innerContour == null)
            {
                Console.WriteLine("未找到有效的内部不规则区域");
                return null;
            }

            // 计算内部区域的圆度
            // 圆度 = 4π * 面积 / 周长²
            double area = Cv2.ContourArea(innerContour);
            double perimeter = Cv2.ArcLength(innerContour, true);

            double roundness = 0;
			if (perimeter != 0)
            {
                roundness = (4 * Math.PI * area) / (perimeter * perimeter);
            }

         

            // 计算外接矩形
            Rect boundingRect = Cv2.BoundingRect(innerContour);
            int longEdge = Math.Max(boundingRect.Width, boundingRect.Height);
            int shortEdge = Math.Min(boundingRect.Width, boundingRect.Height);
            double ratio = (Convert.ToDouble(longEdge) / Convert.ToDouble(shortEdge));

			Logger.Info($"找圆 处理结果用时：{stopwatch.ElapsedMilliseconds}ms"); 

			stopwatch.Restart();
			return new DetectionResult
            {
                Roundness = roundness,
				LengthWidthRatio = ratio,
                BoundingRect = boundingRect,
				LongEdge = longEdge,
                ShortEdge = shortEdge,
                Area = area,
                Perimeter = perimeter
            };
        }

        /// <summary>
        /// 可视化检测结果，在原图上标注内部区域和外接矩形
        /// </summary>
        /// <param name="img">原始图像（可以是灰度图或彩色图）</param>
        /// <param name="result">检测结果</param>
        /// <param name="savePath">保存路径（可选，如果为null则不保存）</param>
        /// <returns>绘制了检测结果的图像</returns>
        public static Mat VisualizeDetection(Mat img, DetectionResult result, string savePath = null)
        {
            Stopwatch stopwatch = Stopwatch.StartNew();
            stopwatch.Start();

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
            Cv2.Rectangle(resultImg, rect, new Scalar(0, 255, 0), 10);

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
			Logger.Info($"找圆 画图用时：{stopwatch.ElapsedMilliseconds}ms");
			stopwatch.Stop();
			return resultImg;
        }
    }
}

