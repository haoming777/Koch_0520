using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using YoloInference;

namespace Detection
{
    /// <summary>正面破损检测 — YOLO单图推理→按中心X坐标分盒→输出每盒缺陷列表</summary>
    public class FrontDamageInspection
    {
        // 模拟 Python 中 check_front_model.names 字典结构
        public static readonly Dictionary<int, string> ModelClassNames = new Dictionary<int, string>
        {
            { 0, "damage" }
            // { 1, "scratch" } ...
        };

        /// <summary>
        /// 使用 C# 基础数学库向量化执行非极大值抑制 (NMS)
        /// </summary>
        public static List<float[]> ApplyNms(List<float[]> boxesWithScores, float iouThreshold = 0.45f)
        {
            if (boxesWithScores == null || boxesWithScores.Count == 0)
                return new List<float[]>();

            // 按置信度降序排序
            var sortedBoxes = boxesWithScores.OrderByDescending(b => b[4]).ToList();
            var keep = new List<float[]>();
            bool[] isRemoved = new bool[sortedBoxes.Count];

            for (int i = 0; i < sortedBoxes.Count; i++)
            {
                if (isRemoved[i]) continue;

                var current = sortedBoxes[i];
                keep.Add(new float[] { current[0], current[1], current[2], current[3] });

                float areaI = (current[2] - current[0]) * (current[3] - current[1]);

                for (int j = i + 1; j < sortedBoxes.Count; j++)
                {
                    if (isRemoved[j]) continue;

                    var compare = sortedBoxes[j];
                    float xx1 = Math.Max(current[0], compare[0]);
                    float yy1 = Math.Max(current[1], compare[1]);
                    float xx2 = Math.Min(current[2], compare[2]);
                    float yy2 = Math.Min(current[3], compare[3]);

                    float w = Math.Max(0.0f, xx2 - xx1);
                    float h = Math.Max(0.0f, yy2 - yy1);
                    float inter = w * h;

                    float areaJ = (compare[2] - compare[0]) * (compare[3] - compare[1]);
                    float iou = inter / (areaI + areaJ - inter);

                    if (iou > iouThreshold)
                    {
                        isRemoved[j] = true;
                    }
                }
            }

            return keep;
        }

        /// <summary>
        /// 对图像进行切分，并返回子图及其在原图中的左上角偏移坐标
        /// </summary>
        private static (List<Mat> Patches, List<Point> Offsets) GetCropPatchesAndOffsets(Mat image, int P)
        {
            int h = image.Height;
            int w = image.Width;
            var xBoundaries = new List<(int start, int end)>();

            if (P / 2 == 5)
            {
                xBoundaries.Add((0, (int)(w * 0.4)));
                xBoundaries.Add(((int)(w * 0.4), (int)(w * 0.8)));
                xBoundaries.Add(((int)(w * 0.8), w));
            }
            else
            {
                int wThird = w / 3;
                xBoundaries.Add((0, wThird));
                xBoundaries.Add((wThird, wThird * 2));
                xBoundaries.Add((wThird * 2, w));
            }

            var yBoundaries = new List<(int start, int end)>
            {
                (0, (int)(h * 0.55)),
                ((int)(h * 0.45), h)
            };

            var croppedImages = new List<Mat>();
            var offsets = new List<Point>();

            foreach (var xb in xBoundaries)
            {
                foreach (var yb in yBoundaries)
                {
                    int patchW = xb.end - xb.start;
                    int patchH = yb.end - yb.start;
                    Rect roi = new Rect(xb.start, yb.start, patchW, patchH);

                    // 使用 Clone 分配独立的连续内存，避免指针越界
                    croppedImages.Add(new Mat(image, roi).Clone());
                    offsets.Add(new Point(xb.start, yb.start));
                }
            }

            return (croppedImages, offsets);
        }

        /// <summary>
        /// 处理左右两图，单图顺序推理并映射坐标（适配左右图尺寸不同），最后应用NMS去除重叠框
        /// </summary>
        public static (List<string> StatusList, Dictionary<string, List<float[]>> FinalLeftDict, Dictionary<string, List<float[]>> FinalRightDict)
            CheckFrontDamage(Mat leftImage, Mat rightImage, int P, YoloOnnx yoloModel)
        {
            if (leftImage == null || leftImage.Empty() || rightImage == null || rightImage.Empty())
                throw new ArgumentException("输入的图像不能为空。");

            int halfP = P / 2;
            var statusList = Enumerable.Repeat("OK", P).ToList();
            
            var tempLeftDict = new Dictionary<string, List<float[]>>();
            var tempRightDict = new Dictionary<string, List<float[]>>();

            var labelTranslationMap = new Dictionary<string, string>
            {
                { "damage", "破损" }
            };

            // 使用 C# 本地函数 (Local Function) 封装核心单边处理逻辑
            // 完美解决左右图尺寸可能不一致，以及状态索引错位的问题
            void ProcessSide(Mat sourceImage, bool isLeft)
            {
                int currentW = sourceImage.Width;
                int currentH = sourceImage.Height;
                var targetDict = isLeft ? tempLeftDict : tempRightDict;
                int baseIdx = isLeft ? 0 : halfP;

                var (patches, offsets) = GetCropPatchesAndOffsets(sourceImage, P);

                for (int i = 0; i < patches.Count; i++)
                {
                    Mat patch = patches[i];
                    Point offset = offsets[i];

                    try
                    {
                        // 改为单张图片推理，避免 Batch 处理带来的底层尺寸维度不匹配和显存峰值问题
                        // 注意：这里假定您的 YoloOnnx 库提供 Predict 单图推理方法。
                        // 如果仅有 PredictBatch，可传入 new List<Mat> { patch } 然后取 [0]
                        var result = yoloModel.Predict(patch, confThres: 0.25f, iouThres: 0.45f);

                        for (int j = 0; j < result.Boxes.Length; j++)
                        {
                            int classId = result.ClassIds[j];
                            string rawClassName = ModelClassNames.ContainsKey(classId) ? ModelClassNames[classId] : classId.ToString();
                            string className = labelTranslationMap.ContainsKey(rawClassName) ? labelTranslationMap[rawClassName] : rawClassName;

                            var box = result.Boxes[j];
                            float score = result.Scores[j];

                            // 转换回原图绝对坐标系
                            float origX1 = box.Left + offset.X;
                            float origY1 = box.Top + offset.Y;
                            float origX2 = box.Right + offset.X;
                            float origY2 = box.Bottom + offset.Y;

                            // 归一化并附加置信度（此时使用的是准确的当前图 currentW 和 currentH）
                            float[] normBoxWithScore = {
                                origX1 / currentW, origY1 / currentH, origX2 / currentW, origY2 / currentH, score
                            };

                            if (!targetDict.ContainsKey(className))
                            {
                                targetDict[className] = new List<float[]>();
                            }
                            targetDict[className].Add(normBoxWithScore);

                            // 状态阵列计算（使用正确的 currentW 计算区域索引）
                            float centerX = (origX1 + origX2) / 2.0f;
                            int brushLocalIdx = (int)((centerX / currentW) * halfP);
                            brushLocalIdx = Math.Max(0, Math.Min(brushLocalIdx, halfP - 1));

                            int globalIdx = baseIdx + brushLocalIdx;
                            statusList[globalIdx] = className;
                        }
                    }
                    finally
                    {
                        // 及时释放每一个切片的内存，严控内存水位
                        patch?.Dispose();
                    }
                }
            }

            // 1 & 2. 依次处理左图与右图
            ProcessSide(leftImage, isLeft: true);
            ProcessSide(rightImage, isLeft: false);

            // 3. 全局 NMS 后处理 (利用 LINQ 的 ToDictionary 使代码更加简洁优雅)
            var finalLeftDict = tempLeftDict.ToDictionary(
                kvp => kvp.Key,
                kvp => ApplyNms(kvp.Value, iouThreshold: 0.45f)
            );

            var finalRightDict = tempRightDict.ToDictionary(
                kvp => kvp.Key,
                kvp => ApplyNms(kvp.Value, iouThreshold: 0.45f)
            );

            return (statusList, finalLeftDict, finalRightDict);
        }
    }
}