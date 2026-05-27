using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using OpenCvSharp; // 假设使用 OpenCvSharp4 对应 C# OpenCV 绑定

namespace VisionInspection
{
    /// <summary>
    /// 用于封装复杂的返回结果，避免使用过长的 Tuple 导致可读性下降
    /// </summary>
    public class PNumberCheckResult
    {
        public List<string> Statuses { get; set; } = new List<string>();
        public Dictionary<string, List<List<List<float>>>> LeftDict { get; set; } = new Dictionary<string, List<List<List<float>>>>();
        public Dictionary<string, List<List<List<float>>>> RightDict { get; set; } = new Dictionary<string, List<List<List<float>>>>();
    }

    public class PNumberValidator
    {
        // 预编译正则表达式以提升循环中的匹配性能，忽略大小写
        private static readonly Regex PNumberRegex = new Regex(@"P\d+", RegexOptions.IgnoreCase | RegexOptions.Compiled);

        // 假设你的 OcrEngine 类名为 CustomOcrEngine
        private readonly CustomOcrEngine _ocrEngine;

        public PNumberValidator(CustomOcrEngine ocrEngine)
        {
            _ocrEngine = ocrEngine ?? throw new ArgumentNullException(nameof(ocrEngine));
        }

        /// <summary>
        /// 使用指定的 OCR 引擎检测背面 P 号码是否正确 (单线程安全版)
        /// </summary>
        public PNumberCheckResult CheckBackPNumberOcr(
            Mat leftImage,
            Mat rightImage,
            string referencePNumber,
            int pCount)
        {
            if (pCount <= 0 || pCount % 2 != 0)
            {
                throw new ArgumentException("盒子总数 P 必须是大于0的偶数。");
            }

            int halfP = pCount / 2;
            var result = new PNumberCheckResult();
           
            // 初始化状态列表
            for (int i = 0; i < pCount; i++) result.Statuses.Add(string.Empty);

            // 图像转灰度 (使用 using 确保内部生成的 Mat 被及时回收)
            using (Mat leftGray = EnsureGrayScale(leftImage))
            using (Mat rightGray = EnsureGrayScale(rightImage))
            {
                int hLeft = leftGray.Rows;
                int wLeft = leftGray.Cols;
                int hRight = rightGray.Rows;
                int wRight = rightGray.Cols;

                int boxWLeft = wLeft / halfP;
                int boxWRight = wRight / halfP;

                int startYLeft = (hLeft * 2) / 3;
                int startYRight = (hRight * 2) / 3;

                // 1. 顺序处理左图
                for (int i = 0; i < halfP; i++)
                {
                    ProcessSingleBox(
                        leftGray, i, boxWLeft, startYLeft, hLeft, wLeft, i, referencePNumber,
                        halfP, out string status, out var items);

                    result.Statuses[i] = status;
                    PopulateDict(result.LeftDict, items);
                }

                // 2. 顺序处理右图
                for (int j = 0; j < halfP; j++)
                {
                    int globalIdx = halfP + j;
                    ProcessSingleBox(
                        rightGray, j, boxWRight, startYRight, hRight, wRight, globalIdx, referencePNumber,
                        halfP, out string status, out var items);

                    result.Statuses[globalIdx] = status;
                    PopulateDict(result.RightDict, items);
                }
            }

            return result;
        }

        /// <summary>
        /// 处理单个 ROI 区域并执行 OCR
        /// </summary>
        private void ProcessSingleBox(
            Mat imgGray, int boxIdxInHalf, int boxW, int startY, int imgH, int imgW, int globalIdx, string referencePNumber, int halfP,
            out string status, out List<Tuple<string, List<List<float>>>> detectedItems)
        {
            detectedItems = new List<Tuple<string, List<List<float>>>>();
           
            int startX = boxIdxInHalf * boxW;
            int endX = (boxIdxInHalf < halfP - 1) ? (boxIdxInHalf + 1) * boxW : imgW;
            int roiWidth = endX - startX;
            int roiHeight = imgH - startY;

            // 划定 ROI 区域 (注意：OpenCvSharp 中使用 Rect 裁切图像不会直接拷贝内存，而是建立视图)
            using (Mat roiGray = new Mat(imgGray, new Rect(startX, startY, roiWidth, roiHeight)))
            using (Mat roiBgr = new Mat())
            {
                // OCR 通常需要三通道 BGR 图像
                Cv2.CvtColor(roiGray, roiBgr, ColorConversionCodes.GRAY2BGR);

                // 调用替换后的 SDK 方法
                int sdkRet = _ocrEngine.Run(roiBgr, out ResponseList<OcrResponse> results);

                if (sdkRet != 0 /* ERROR_OK */ || results == null || results.Count == 0)
                {
                    status = "缺少";
                    return;
                }

                status = "P号码错误";
                bool foundAnyPNumber = false;

                // results 是 List<Tuple<Rect, OcrResponse>>
                foreach (var resTuple in results)
                {
                    OcrResponse ocrResponse = resTuple.Item2;
                    if (ocrResponse.Blocks == null) continue;

                    foreach (var block in ocrResponse.Blocks)
                    {
                        if (string.IsNullOrWhiteSpace(block.Label)) continue;

                        Match match = PNumberRegex.Match(block.Label);
                        if (!match.Success) continue;

                        foundAnyPNumber = true;
                        string pNumber = match.Value.ToUpper(); // 获取匹配到的 Pxxxx 文本并转大写

                        // 转换坐标体系：从 ROI 局部坐标系 -> 全局坐标系 -> 相对全局的比例坐标系
                        var relativePointsList = new List<List<float>>();
                        foreach (Point2f pt in block.Polygon)
                        {
                            // 映射到全局像素坐标
                            float globalX = pt.X + startX;
                            float globalY = pt.Y + startY;

                            // 映射为比例 (0~1) 并保留 4 位小数
                            float relX = (float)Math.Round(globalX / imgW, 4);
                            float relY = (float)Math.Round(globalY / imgH, 4);

                            relativePointsList.Add(new List<float> { relX, relY });
                        }

                        detectedItems.Add(new Tuple<string, List<List<float>>>(pNumber, relativePointsList));

                        if (pNumber == referencePNumber)
                        {
                            status = "OK";
                        }
                    }
                }

                if (!foundAnyPNumber)
                {
                    status = "缺少";
                }
            }
        }

        /// <summary>
        /// 确保输入的 Mat 是单通道灰度图，如果不是则执行转换。
        /// 注意：返回的 Mat 资源由调用方负责 Dispose。
        /// </summary>
        private Mat EnsureGrayScale(Mat src)
        {
            if (src.Channels() == 3)
            {
                Mat gray = new Mat();
                Cv2.CvtColor(src, gray, ColorConversionCodes.BGR2GRAY);
                return gray;
            }
            return src.Clone(); // Clone 一份保持生命周期一致性，方便统一使用 using 释放
        }

        /// <summary>
        /// 辅助方法：将当前 Box 检测到的所有结果追加到对应的总字典中
        /// </summary>
        private void PopulateDict(Dictionary<string, List<List<List<float>>>> targetDict, List<Tuple<string, List<List<float>>>> items)
        {
            foreach (var item in items)
            {
                string pNum = item.Item1;
                List<List<float>> ptsList = item.Item2;

                if (!targetDict.ContainsKey(pNum))
                {
                    targetDict[pNum] = new List<List<List<float>>>();
                }
                targetDict[pNum].Add(ptsList);
            }
        }
    }
}