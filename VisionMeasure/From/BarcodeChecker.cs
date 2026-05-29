using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading.Tasks;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using ZXing;
using ZXing.Common;

namespace VisionMeasure.From
{
    public class BarcodeChecker
    {
        public enum ThresholdModeEnum { None, Adaptive, Otsu, Fixed }

        public class PreprocessConfig
        {
            public bool EnablePreprocess = true;
            public float ContrastAlpha = 1.0f;
            public int BrightnessBeta = 0;
            public bool EnableGaussianBlur = true;
            public bool EnableMedianBlur = false;
            public bool EnableEqualizeHist = false;
            public ThresholdModeEnum ThresholdMode = ThresholdModeEnum.Adaptive;
            public int AdaptiveBlockSize = 11;
            public double AdaptiveC = 2.0;
            public int FixedThreshold = 128;
            public bool EnableInvert = false;
            public bool EnableMorphClose = true;
            public bool EnableMorphOpen = false;
            public bool EnableMorphDilate = false;
            public bool EnableMorphErode = false;
            /// <summary>裁剪起始高度比例 (0.0~1.0), 0.667 表示从图像高度 2/3 处开始，仅处理底部 1/3</summary>
            public double StartHeightRatio = 2.0 / 3.0;
            /// <summary>启用智能过滤：每个区域只保留与基准条码最相似的1个结果</summary>
            public bool EnableFilterBestMatch = true;
            /// <summary>有效条码最小长度（字符数），短于此值的视为幻读</summary>
            public int MinBarcodeLength = 3;
            /// <summary>有效条码最大长度（字符数），长于此值的视为幻读</summary>
            public int MaxBarcodeLength = 50;
            /// <summary>ZXing TryHarder 深度搜索模式，关闭可提速 2~5 倍</summary>
            public bool TryHarder = false;
            /// <summary>首次失败后尝试 90° 旋转重试，关闭可显著提速</summary>
            public bool EnableRotationRetry = false;
        }

        /// <summary>
        /// 使用 ZXing.Net 检测背面条码是否正确，支持高并发与 90 度旋转降级重试。
        /// </summary>
        /// <param name="leftImage">左侧完整图像</param>
        /// <param name="rightImage">右侧完整图像</param>
        /// <param name="referenceBarcode">基准条码文本</param>
        /// <param name="P">盒子总数，必须为大于0的偶数</param>
        /// <param name="config">OpenCV 预处理参数配置，为 null 时使用默认配置</param>
        /// <returns>包含状态数组、左图字典、右图字典的元组</returns>
        public static async Task<(
            string[] backBarcodeStatuses,
            Dictionary<string, List<List<double[]>>> leftBarcodeDict,
            Dictionary<string, List<List<double[]>>> rightBarcodeDict)>
            CheckBackBarcodeCv2Async(Bitmap leftImage, Bitmap rightImage, string referenceBarcode, int P, PreprocessConfig config = null)
        {
            if (P <= 0 || P % 2 != 0)
            {
                throw new ArgumentException("盒子总数 P 必须是大于0的偶数。");
            }

            if (config == null)
            {
                config = new PreprocessConfig();
            }

            int halfP = P / 2;

            int hLeft = leftImage.Height;
            int wLeft = leftImage.Width;
            int hRight = rightImage.Height;
            int wRight = rightImage.Width;

            int boxWLeft = wLeft / halfP;
            int boxWRight = wRight / halfP;

            // 根据配置的起始高度比例截取
            int startYLeft = (int)(hLeft * config.StartHeightRatio);
            int startYRight = (int)(hRight * config.StartHeightRatio);

            string[] backBarcodeStatuses = new string[P];

            var leftBarcodeDict = new Dictionary<string, List<List<double[]>>>();
            var rightBarcodeDict = new Dictionary<string, List<List<double[]>>>();

            // 预先提取所有 ROI（主线程顺序执行，无需锁），消除多线程 Clone 竞争瓶颈
            int totalBoxes = P;
            var roiData = new (Bitmap roi, int startX, int startY, int imgW, int imgH, int globalIdx)[totalBoxes];

            for (int i = 0; i < halfP; i++)
            {
                int startX = i * boxWLeft;
                int endX = (i < (wLeft / boxWLeft) - 1) ? (i + 1) * boxWLeft : wLeft;
                int roiW = endX - startX;
                int roiH = hLeft - startYLeft;
                roiData[i] = (leftImage.Clone(new Rectangle(startX, startYLeft, roiW, roiH), leftImage.PixelFormat),
                    startX, startYLeft, wLeft, hLeft, i);
            }
            for (int j = 0; j < halfP; j++)
            {
                int startX = j * boxWRight;
                int endX = (j < (wRight / boxWRight) - 1) ? (j + 1) * boxWRight : wRight;
                int roiW = endX - startX;
                int roiH = hRight - startYRight;
                int idx = halfP + j;
                roiData[idx] = (rightImage.Clone(new Rectangle(startX, startYRight, roiW, roiH), rightImage.PixelFormat),
                    startX, startYRight, wRight, hRight, idx);
            }

            // 提交并发任务（每个任务持有独立 Bitmap，无需锁）
            var tasks = new List<Task<(int GlobalIdx, string Status, List<(string Text, List<double[]> Points)> Items)>>();
            for (int i = 0; i < totalBoxes; i++)
            {
                var data = roiData[i];
                tasks.Add(Task.Run(() =>
                    ProcessSingleBox(data.roi, data.startX, data.startY, data.imgW, data.imgH, data.globalIdx, referenceBarcode, config)));
            }

            // 3. 等待所有并发任务完成
            var results = await Task.WhenAll(tasks);

            // 4. 收集结果并分别构建字典（在主线程单线程归并，避免了并发字典的锁开销与性能损耗）
            foreach (var result in results)
            {
                backBarcodeStatuses[result.GlobalIdx] = result.Status;

                // 根据全局索引决定数据应当归属哪个字典
                var targetDict = result.GlobalIdx < halfP ? leftBarcodeDict : rightBarcodeDict;

                foreach (var item in result.Items)
                {
                    if (!targetDict.ContainsKey(item.Text))
                    {
                        targetDict[item.Text] = new List<List<double[]>>();
                    }
                    targetDict[item.Text].Add(item.Points);
                }
            }

            return (backBarcodeStatuses, leftBarcodeDict, rightBarcodeDict);
        }

        /// <summary>
        /// OpenCV 图像预处理管线：对比度/亮度 → 灰度 → 滤波 → 二值化 → 形态学
        /// </summary>
        public static Bitmap ApplyImageFilters(Bitmap srcBitmap, PreprocessConfig config)
        {
            if (!config.EnablePreprocess) return srcBitmap;

            using (Mat srcMat = BitmapConverter.ToMat(srcBitmap))
            {
                Mat workMat = new Mat();
                float alpha = config.ContrastAlpha;
                int beta = config.BrightnessBeta;
                srcMat.ConvertTo(workMat, -1, alpha, beta);

                if (workMat.Channels() == 3) Cv2.CvtColor(workMat, workMat, ColorConversionCodes.BGR2GRAY);
                if (config.EnableEqualizeHist) Cv2.EqualizeHist(workMat, workMat);

                if (config.EnableGaussianBlur) Cv2.GaussianBlur(workMat, workMat, new OpenCvSharp.Size(5, 5), 0);
                if (config.EnableMedianBlur) Cv2.MedianBlur(workMat, workMat, 5);

                Mat binaryMat;

                switch (config.ThresholdMode)
                {
                    case ThresholdModeEnum.None:
                        binaryMat = workMat.Clone();
                        break;
                    case ThresholdModeEnum.Adaptive:
                        binaryMat = new Mat();
                        int blockSize = config.AdaptiveBlockSize;
                        if (blockSize % 2 == 0) blockSize += 1;
                        Cv2.AdaptiveThreshold(workMat, binaryMat, 255, AdaptiveThresholdTypes.MeanC,
                            ThresholdTypes.Binary, blockSize, config.AdaptiveC);
                        break;
                    case ThresholdModeEnum.Otsu:
                        binaryMat = new Mat();
                        Cv2.Threshold(workMat, binaryMat, 0, 255, ThresholdTypes.Binary | ThresholdTypes.Otsu);
                        break;
                    case ThresholdModeEnum.Fixed:
                        binaryMat = new Mat();
                        Cv2.Threshold(workMat, binaryMat, config.FixedThreshold, 255, ThresholdTypes.Binary);
                        break;
                    default:
                        binaryMat = workMat.Clone();
                        break;
                }

                if (config.EnableInvert) Cv2.BitwiseNot(binaryMat, binaryMat);

                if (config.EnableMorphClose)
                {
                    using (Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(3, 3)))
                        Cv2.MorphologyEx(binaryMat, binaryMat, MorphTypes.Close, kernel);
                }
                if (config.EnableMorphOpen)
                {
                    using (Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(3, 3)))
                        Cv2.MorphologyEx(binaryMat, binaryMat, MorphTypes.Open, kernel);
                }
                if (config.EnableMorphDilate)
                {
                    using (Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(3, 3)))
                        Cv2.Dilate(binaryMat, binaryMat, kernel);
                }
                if (config.EnableMorphErode)
                {
                    using (Mat kernel = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(3, 3)))
                        Cv2.Erode(binaryMat, binaryMat, kernel);
                }

                Bitmap result = BitmapConverter.ToBitmap(binaryMat);
                workMat.Dispose();
                binaryMat.Dispose();
                return result;
            }
        }

        /// <summary>
        /// 处理单个区域的图像，包含 ROI 截取、OpenCV 预处理、ZXing 解码以及 90 度旋转降级重试
        /// </summary>
        private static (int GlobalIdx, string Status, List<(string Text, List<double[]> Points)> Items)
            ProcessSingleBox(Bitmap roiBitmap, int startX, int startY, int imgW, int imgH, int globalIdx,
                string referenceBarcode, PreprocessConfig config)
        {
            int roiWidth = roiBitmap.Width;
            int roiHeight = roiBitmap.Height;

            var detectedItems = new List<(string Text, List<double[]> Points)>();
            string status = "条形码错误";

            using (roiBitmap)
            {
                // OpenCV 预处理阶段
                Bitmap decodeBitmap;
                if (config.EnablePreprocess)
                {
                    decodeBitmap = ApplyImageFilters(roiBitmap, config);
                }
                else
                {
                    decodeBitmap = roiBitmap;
                }

                // 初始化 ZXing Reader (每次 Task 独立实例化，保证绝对的线程安全)
                var reader = new BarcodeReader
                {
                    AutoRotate = true,
                    Options = new DecodingOptions
                    {
                        TryHarder = config.TryHarder,
                        PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.CODE_128, BarcodeFormat.EAN_13 }
                    }
                };

                // 第 1 步：尝试直接解码预处理后的图像
                Result[] results = reader.DecodeMultiple(decodeBitmap);

                // 第 2 步：90 度旋转降级重试 (可配置开关)
                if (config.EnableRotationRetry && (results == null || results.Length == 0) && roiWidth > 0 && roiHeight > 0)
                {
                    // 复制一个新的 Bitmap 用于旋转，避免破坏原图
                    using (Bitmap rotatedRoi = (Bitmap)decodeBitmap.Clone())
                    {
                        // GDI+ 顺时针旋转 90 度
                        rotatedRoi.RotateFlip(RotateFlipType.Rotate90FlipNone);

                        // 对真正旋转后的图像进行第二次解码尝试
                        results = reader.DecodeMultiple(rotatedRoi);

                        // 如果旋转后成功识别，必须将坐标逆向映射回原始的 ROI 坐标系
                        if (results != null && results.Length > 0)
                        {
                            foreach (var res in results)
                            {
                                if (res.ResultPoints != null)
                                {
                                    for (int i = 0; i < res.ResultPoints.Length; i++)
                                    {
                                        float rx = res.ResultPoints[i].X;
                                        float ry = res.ResultPoints[i].Y;

                                        // 坐标逆向映射数学推导：
                                        // 图像顺时针转 90 度后，新坐标与旧坐标的关系推导
                                        // 原x = 新y, 原y = 原图高 - 新x
                                        float originalX = ry;
                                        float originalY = roiHeight - rx;

                                        // 覆盖掉原来在旋转图像中的点坐标，还原为未旋转前 ROI 中的相对坐标
                                        res.ResultPoints[i] = new ResultPoint(originalX, originalY);
                                    }
                                }
                            }
                        }
                    }
                }

                // 第 3 步：结果收集与坐标归一化
                if (results == null || results.Length == 0)
                {
                    if (config.EnablePreprocess) decodeBitmap.Dispose();
                    return (globalIdx, "缺少", detectedItems); // 两种方式均未能识别
                }

                // 先收集所有有效结果（带原始坐标）
                var rawItems = new List<(string Text, ResultPoint[] Points)>();
                foreach (var res in results)
                {
                    if (res == null || string.IsNullOrEmpty(res.Text)) continue;

                    // 长度过滤：剔除明显不合理的幻读结果
                    int textLen = res.Text.Length;
                    if (textLen < config.MinBarcodeLength || textLen > config.MaxBarcodeLength)
                        continue;

                    rawItems.Add((res.Text, res.ResultPoints));
                }

                if (rawItems.Count == 0)
                {
                    if (config.EnablePreprocess) decodeBitmap.Dispose();
                    return (globalIdx, "缺少", detectedItems);
                }

                // 智能过滤：每个区域只保留与基准条码编辑距离最接近的结果
                if (config.EnableFilterBestMatch && !string.IsNullOrEmpty(referenceBarcode))
                {
                    double bestScore = double.MinValue;
                    (string Text, ResultPoint[] Points) bestItem = rawItems[0];

                    foreach (var item in rawItems)
                    {
                        double score = LevenshteinSimilarity(item.Text, referenceBarcode);
                        if (score > bestScore)
                        {
                            bestScore = score;
                            bestItem = item;
                        }
                    }
                    rawItems = new List<(string, ResultPoint[])> { bestItem };
                }

                // 坐标归一化
                foreach (var item in rawItems)
                {
                    var relativePointsList = new List<double[]>();

                    foreach (var point in item.Points)
                    {
                        double globalX = point.X + startX;
                        double globalY = point.Y + startY;
                        double relativeX = globalX / imgW;
                        double relativeY = globalY / imgH;
                        relativePointsList.Add(new double[] {
                            Math.Round(relativeX, 4),
                            Math.Round(relativeY, 4)
                        });
                    }

                    detectedItems.Add((item.Text, relativePointsList));

                    if (item.Text == referenceBarcode)
                        status = "OK";
                }

                if (config.EnablePreprocess) decodeBitmap.Dispose();
            }

            return (globalIdx, status, detectedItems);
        }

        /// <summary>
        /// 计算两个字符串的编辑距离相似度 (0.0~1.0)，1.0 表示完全匹配。
        /// </summary>
        private static double LevenshteinSimilarity(string a, string b)
        {
            if (string.IsNullOrEmpty(a) && string.IsNullOrEmpty(b)) return 1.0;
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0.0;

            int lenA = a.Length, lenB = b.Length;
            int[,] dp = new int[lenA + 1, lenB + 1];

            for (int i = 0; i <= lenA; i++) dp[i, 0] = i;
            for (int j = 0; j <= lenB; j++) dp[0, j] = j;

            for (int i = 1; i <= lenA; i++)
                for (int j = 1; j <= lenB; j++)
                    dp[i, j] = Math.Min(Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
                        dp[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));

            int maxLen = Math.Max(lenA, lenB);
            return 1.0 - (double)dp[lenA, lenB] / maxLen;
        }
    }
}
