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
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using VisionMeasure.Utils;
using CommonLib;
using SmartMore.ViMo;
using YoloInference;
using YoloSegmentationEnd2End;
using AI;
using CvRect = OpenCvSharp.Rect;
using CvPoint = OpenCvSharp.Point;
using CvSize = OpenCvSharp.Size;
using CvScalar = OpenCvSharp.Scalar;
using Rect = System.Drawing.Rectangle;
using static CommonLib.Class_Config;

namespace Stations
{
	/// <summary>
/// 背面工位处理器, 左右图配对后3路并行推理.
/// 1.条码识别: BarcodeCore.dll + OpenCV预处理管线 -> 逐盒ROI解码 -> 与参考条码比对.
/// 2.日期码识别: C1(ViMo分割) -> C2(ViMo分类重影) -> C3(ViMo OCR校验), 支持MFG/LOT/双排.
/// 3.挂钩错位: YOLO检测(classId=1明显) + 分割厚度计算(classId=0轻微).
/// 汇总: 3路结果合并 -> 逐盒status -> OK/NG计数 -> 渲染+保存.
/// </summary>
	public class BackStationProcessor : IDisposable
	{
		private readonly AiModelManager _models;
		private readonly string _savePath;
		private SkuData _sku;
		private readonly HighSpeedImageSaver _imageSaver;
		private readonly PerformanceMonitor _perfMonitor;
		private Mat _leftBuffer, _rightBuffer;
		private readonly object _syncLock = new object();
		private int _isProcessing = 0;  // 防重入: 0=空闲, 1=处理中
		private long _totalCount, _okCount, _ngCount;
		private long _imgCount = 0;
		private bool _lastIsOk = true;
		private bool _disposed;
		private Config.ModelParams _barcodeParams;
		private Config.ModelParams _datecodeParams;
		private Config.ModelParams _backBoxParams;

		public event Action<ProductResult> OnResultReady;
		public event Action<List<string>, int> OnStatusUpdate;
		public float ConfThreshold = 0.5f, IouThreshold = 0.2f;
		/// <summary>背面盒子破损独立置信度/IoU阈值(与挂钩阈值解耦, 来自back_box.json, 可像正面一样在检测参数界面调节)</summary>
		public float BackBoxConfThreshold = 0.5f, BackBoxIouThreshold = 0.2f;
		public float HookThicknessThreshold = 30f;
		public int BlueAreaClassId = 0, HangHoleClassId = 1;
		public bool ReverseBoxOrder = false;
		public bool EnableDateCodeCheck = false;
		public bool EnableBarcodeCheck = true;
		public bool EnableHookCheck = true;
		public bool EnableBoxBreakCheck = true;
		public bool SkipCrop = false;

		private static readonly string _bkp = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Logs", "Back_Error.log");
		private static void WBK(string m) { try { var d = System.IO.Path.GetDirectoryName(_bkp); if (!System.IO.Directory.Exists(d)) System.IO.Directory.CreateDirectory(d); System.IO.File.AppendAllText(_bkp, m + Environment.NewLine, System.Text.Encoding.UTF8); } catch { } }

		public BackStationProcessor(AiModelManager models, string savePath, SkuData sku,
			HighSpeedImageSaver imageSaver, PerformanceMonitor perfMonitor)
		{
			_models = models; _savePath = savePath; _sku = sku; _imageSaver = imageSaver; _perfMonitor = perfMonitor;
			_barcodeParams = Config.ModelParams.Load("barcode"); _datecodeParams = Config.ModelParams.Load("datecode");
			_backBoxParams = Config.ModelParams.Load("back_box");
			BackBoxConfThreshold = _backBoxParams.Confidence;
			BackBoxIouThreshold = _backBoxParams.Iou;
			var hookParams = Config.ModelParams.Load("hook");
			HookThicknessThreshold = hookParams.HookThickness;
			BlueAreaClassId = hookParams.HookBlueClassId;
			EnableBarcodeCheck = DetectionParameters.Instance.Back.EnableBarcodeCheck;
			EnableHookCheck = DetectionParameters.Instance.Back.EnableHookCheck;
			HangHoleClassId = hookParams.HookHoleClassId;
			EnableBoxBreakCheck = DetectionParameters.Instance.Back.EnableBoxBreakCheck;
			EnableDateCodeCheck = DetectionParameters.Instance.Back.EnableDateCodeCheck;
			// 条码引擎(BarcodeCore.dll)初始化: 含依赖文件/VC++运行库/加密狗环境自检, 结果写入 Logs/Barcode_*.log
			if (EnableBarcodeCheck) VisionMeasure.Utils.BarcodeCoreEngine.EnsureInitialized();
		}

		/// <summary>重新加载ModelParams，无需重启软件</summary>
		public void ReloadModelParams()
		{
			_barcodeParams = Config.ModelParams.Load("barcode");
			_datecodeParams = Config.ModelParams.Load("datecode");
			_backBoxParams = Config.ModelParams.Load("back_box");
			BackBoxConfThreshold = _backBoxParams.Confidence;
			BackBoxIouThreshold = _backBoxParams.Iou;
			var hookParams = Config.ModelParams.Load("hook");
			HookThicknessThreshold = hookParams.HookThickness;
			BlueAreaClassId = hookParams.HookBlueClassId;
			EnableBarcodeCheck = DetectionParameters.Instance.Back.EnableBarcodeCheck;
			EnableHookCheck = DetectionParameters.Instance.Back.EnableHookCheck;
			HangHoleClassId = hookParams.HookHoleClassId;
			if (hookParams.Confidence > 0) ConfThreshold = hookParams.Confidence;
			if (hookParams.Iou > 0) IouThreshold = hookParams.Iou;
			EnableBoxBreakCheck = DetectionParameters.Instance.Back.EnableBoxBreakCheck;
			EnableDateCodeCheck = DetectionParameters.Instance.Back.EnableDateCodeCheck;
			// 参数保存后热更新: 重新加载 barcode.config.json 并重建扫描器(与在途解码互不阻塞)
			if (EnableBarcodeCheck) VisionMeasure.Utils.BarcodeCoreEngine.ReInitialize();
			Logger.Info($"[Back] ModelParams已重新加载 挂钩Conf={ConfThreshold:F2} Iou={IouThreshold:F2} 盒子破Conf={BackBoxConfThreshold:F2} Iou={BackBoxIouThreshold:F2}");
		}

		/// <summary>更新当前SKU数据(含条码基准、日期码格式等)</summary>
		public void UpdateSku(SkuData sku) { _sku = sku; }
		/// <summary>OK累计数</summary>
		public long TotalCount => _totalCount;
		/// <summary>OK累计数</summary>
		public long OkCount => _okCount;
		/// <summary>NG累计数</summary>
		public long NgCount => _ngCount;
		/// <summary>收图累计数</summary>
		public long ImgCount => _imgCount;
		/// <summary>最近一次逐盒状态列表(供PLC读取)</summary>
		public List<string> StatusList { get; private set; } = new List<string>();

		/// <summary>相机5(背面左)图像回调</summary>
		/// <summary>相机5(背面左)图像回调 — 图像→Mat→配对缓冲→CheckAndProcess触发处理</summary>
		public void OnCam3(Bitmap bmp, long pid)
		{
			if (bmp == null) return;
			Interlocked.Increment(ref _imgCount);
			Logger.Debug("[Back] OnCam3(左) " + bmp.Width + "x" + bmp.Height);
			lock (_syncLock)
			{
				_leftBuffer?.Dispose();
				_leftBuffer = bmp.ToMat();
			}
			CheckAndProcess();
		}

		/// <summary>相机6(背面右)图像回调 — 图像→Mat→配对缓冲→CheckAndProcess触发处理</summary>
		public void OnCam4(Bitmap bmp, long pid)
		{
			if (bmp == null) return;
			Interlocked.Increment(ref _imgCount);
			Logger.Debug("[Back] OnCam4(右) " + bmp.Width + "x" + bmp.Height);
			lock (_syncLock)
			{
				_rightBuffer?.Dispose();
				_rightBuffer = bmp.ToMat();
			}
			CheckAndProcess();
		}

		/// <summary>配对检查+异步处理: 左右图就绪→取图→Task.Run(Process)后台处理, 不阻塞相机回调</summary>
		private async void CheckAndProcess()
		{
			Mat l = null, r = null;
			lock (_syncLock)
			{
				if (_leftBuffer != null && _rightBuffer != null)
				{
					l = _leftBuffer;
					r = _rightBuffer;
					_leftBuffer = null;
					_rightBuffer = null;
				}
			}
			if (l == null || r == null) return;

			// 防重入: 上一批未处理完则丢弃当前这组
			if (Interlocked.CompareExchange(ref _isProcessing, 1, 0) != 0)
			{
				Logger.Warning("[Back] 上一批处理未完成, 跳过当前配对(防止并发处理导致数据混乱)");
				l?.Dispose(); r?.Dispose();
				return;
			}

			Logger.Debug("[Back] 配对成功");
			var sw = System.Diagnostics.Stopwatch.StartNew();
			try { await Task.Run(() => Process(l, r)); Logger.Info("[Back] 完成 总耗时=" + sw.Elapsed.TotalMilliseconds.ToString("F1") + "ms"); }
			catch (Exception ex) { Logger.Error("[Back] 异常: " + ex.Message); }
			finally { Interlocked.Exchange(ref _isProcessing, 0); l?.Dispose(); r?.Dispose(); }
		}

		// 从模型best.json加载阈值
		public void InitThresholdsFromModel()
		{
			if (_models.BackHookModel != null)
			{
				ConfThreshold = _models.BackHookModel.DefaultConfThres;
				IouThreshold = _models.BackHookModel.DefaultIouThres;
			}
			if (_models.BackBoxBreakModel != null)
			{
				BackBoxConfThreshold = _models.BackBoxBreakModel.DefaultConfThres;
				BackBoxIouThreshold = _models.BackBoxBreakModel.DefaultIouThres;
			}
			Logger.Info($"[Back] 阈值从模型: 挂钩Conf={ConfThreshold:F2} Iou={IouThreshold:F2} 盒子破Conf={BackBoxConfThreshold:F2} Iou={BackBoxIouThreshold:F2}");
		}

		public void Start() { Logger.Info("背面工位已启动"); }
		public void Stop() { }

		private void Process(Mat leftMat, Mat rightMat)
		{
			// 防呆: SKU未加载时使用默认值, 避免NPE
			if (_sku == null)
			{
				Logger.Error("[Back] SKU未设置, 无法处理");
				return;
			}
			long pid = DateTime.Now.Ticks;
			long tickProcess = DateTime.Now.Ticks;
			var sw = System.Diagnostics.Stopwatch.StartNew();
			var result = new ProductResult { ProductId = pid, CreateTime = DateTime.Now };
			int p = _sku.P, hp = p / 2;
			var status = new List<string>(p); for (int i = 0; i < p; i++) status.Add("OK");
			Mat leftProc = null, rightProc = null;

			try
			{
				Logger.Debug($"[Back] ⏱ Process入口 P={p} 图={leftMat.Width}x{leftMat.Height}");
				Logger.Info("[Back] ====== 开始 P=" + p + " " + leftMat.Width + "x" + leftMat.Height + " ======");
				Logger.Trace("[Back] ▶ ====== 开始推理 P=" + p + " 图=" + leftMat.Width + "x" + leftMat.Height);

				// 步骤0: 裁图
				leftProc = leftMat; rightProc = rightMat;
				if (!SkipCrop) try
					{
						if (_sku.BackLeft_LeftPx > 0 || _sku.BackLeft_RightPx > 0)
						{
							int lPx = _sku.BackLeft_LeftPx, rPx = _sku.BackLeft_RightPx;
							leftProc = ImageHelper.CropImageHorizontallyCv2(leftMat, lPx, leftMat.Width - rPx);
							Logger.Info($"[Back] Camera5(背面左) SN={_Config.Camera5SN} 裁图: LeftPx={lPx} RightPx={rPx} 原图={leftMat.Width}x{leftMat.Height} → 裁后={leftProc.Width}x{leftProc.Height}");
						}
						else
						{
							Logger.Info($"[Back] Camera5(背面左) SN={_Config.Camera5SN} 裁图: 无需裁图 原图={leftMat.Width}x{leftMat.Height}");
						}
						if (_sku.BackRight_LeftPx > 0 || _sku.BackRight_RightPx > 0)
						{
							int lPx = _sku.BackRight_LeftPx, rPx = _sku.BackRight_RightPx;
							rightProc = ImageHelper.CropImageHorizontallyCv2(rightMat, lPx, rightMat.Width - rPx);
							Logger.Info($"[Back] Camera6(背面右) SN={_Config.Camera6SN} 裁图: LeftPx={lPx} RightPx={rPx} 原图={rightMat.Width}x{rightMat.Height} → 裁后={rightProc.Width}x{rightProc.Height}");
						}
						else
						{
							Logger.Info($"[Back] Camera6(背面右) SN={_Config.Camera6SN} 裁图: 无需裁图 原图={rightMat.Width}x{rightMat.Height}");
						}
					}
					catch (Exception ex) { Logger.Warning("[Back] 裁图失败(" + ex.Message + "), 使用原图"); }

				// 步骤1: 并行推理
				Logger.Debug("[Back] 步骤1: 推理...");
				Logger.Trace("[Back] ▷ 推理中 条码+日期+挂钩 并行");
				var sw1 = System.Diagnostics.Stopwatch.StartNew();
				Dictionary<int, List<BoxDefect>> barcodeDict = null, dateCodeDict = null, hookDict = null;
				var tasks = new List<Task>();
				tasks.Add(Task.Run(() => { barcodeDict = RecognizeBarcodes(leftProc, rightProc, hp); }));
				if (EnableDateCodeCheck && _models.BackDateCodeSegModel != null && _models.BackDateCodeClsModel != null && _models.BackDateCodeOcrModel != null)
					tasks.Add(Task.Run(() => { dateCodeDict = RecognizeDateCodes(leftProc, rightProc, hp); }));
				tasks.Add(Task.Run(() => { hookDict = DetectHookDamage(leftProc, rightProc, p); }));
				Dictionary<int, List<BoxDefect>> boxBreakDict = null;
				if (EnableBoxBreakCheck && _models.BackBoxBreakModel != null)
				{
					Logger.Debug($"[Back] 盒子破损任务已添加 P={p} Conf={BackBoxConfThreshold:F2} Iou={BackBoxIouThreshold:F2}");
					tasks.Add(Task.Run(() => { boxBreakDict = DetectBoxBreak(leftProc, rightProc, p); }));
				}
				else
				{
					Logger.Warning($"[Back] 盒子破损跳过: EnableBoxBreakCheck={EnableBoxBreakCheck} BackBoxBreakModel={(_models.BackBoxBreakModel != null ? "已加载" : "NULL! 检查setup.ini BackBoxBreakModel路径和best.json")}");
				}
				Task.WaitAll(tasks.ToArray());
				var inferMs = sw1.Elapsed.TotalMilliseconds;
				Logger.Info("[Back] 步骤1完成: 推理=" + inferMs.ToString("F1") + "ms");
				Logger.Trace("[Back] ✓ 推理完成 " + inferMs.ToString("F0") + "ms");

				// 步骤2: 汇总
				var all = new List<BoxDefect>();
				int bc = 0, ho = 0, hs = 0, dc = 0, bb = 0;
				if (barcodeDict != null)
				{
					var its = barcodeDict.Values.SelectMany(v => v).ToList();
					all.AddRange(its);
					bc = its.Count(d => !d.DefectType.StartsWith("条码:"));
				}
				if (dateCodeDict != null)
				{
					var its = dateCodeDict.Values.SelectMany(v => v).ToList();
					all.AddRange(its);
					dc = its.Count(d => !d.DefectType.StartsWith("日期:") && !d.DefectType.StartsWith("双排:"));
				}
				if (hookDict != null)
				{
					var its = hookDict.Values.SelectMany(v => v).ToList();
					all.AddRange(its);
					ho = its.Count(d => d.DefectType == "挂钩明显错位");
					hs = its.Count(d => d.DefectType.Contains("轻微挂钩错位"));
				}
				if (boxBreakDict != null)
				{
					var its = boxBreakDict.Values.SelectMany(v => v).ToList();
					all.AddRange(its);
					bb = its.Count;
				}
				Logger.Info("[Back] 步骤2汇总: 条形码=" + bc + " 日期码=" + dc + " 明显=" + ho + " 轻微=" + hs + " 盒子破=" + bb + " 总计=" + all.Count);
				// 只把真正的NG缺陷写入状态，"条码:xxx"和"日期:xxx"等仅显示标签不覆盖状态
				// Bug修复: 同一盒子多缺陷改为追加拼接，避免后一个覆盖前一个
				foreach (var d in all)
				{
					if (d.BoxIndex < 0 || d.BoxIndex >= status.Count) continue;
					bool isDisplayOnly = d.DefectType.StartsWith("条码:") || d.DefectType.StartsWith("日期:") || d.DefectType.StartsWith("双排:");
					if (isDisplayOnly) continue;
					status[d.BoxIndex] = status[d.BoxIndex] == "OK"
						? d.DefectType
						: status[d.BoxIndex] + "," + d.DefectType;
				}
				Logger.Info("[Back]   " + string.Join(" ", Enumerable.Range(1, status.Count).Select(i => i.ToString().PadLeft(2))));
				Logger.Info("[Back] 逐盒: [" + string.Join("] [", status) + "]");
				StatusList = new List<string>(status);  // 保存副本供PLC读取
				bool isOk = status.All(s => s == "OK");
				result.BackResult = isOk;
				result.BackDefects = status.Where(s => s != "OK").Distinct().ToList();
				int boxOk = status.Count(s => s == "OK");
				Interlocked.Add(ref _totalCount, status.Count);
				Interlocked.Add(ref _okCount, boxOk);
				Interlocked.Add(ref _ngCount, status.Count - boxOk);
				_lastIsOk = isOk;

				// 步骤3: 绘制+合并
				Logger.Debug("[Back] 步骤3: 绘制+合并...");
				Logger.Trace("[Back] ▷ 绘制中");
				var sw3 = System.Diagnostics.Stopwatch.StartNew();
				var lr = DrawResult(leftProc, all.Where(d => d.BoxIndex < hp).ToList(), status, 0, hp);
				var rr = DrawResult(rightProc, all.Where(d => d.BoxIndex >= hp).ToList(), status, hp, p);
				var merged = MergeImages(lr, rr);
				result.BackRenderImage = merged;
				var drawMs = sw3.Elapsed.TotalMilliseconds;
				Logger.Info("[Back] 步骤3完成: " + drawMs.ToString("F1") + "ms " + merged.Width + "x" + merged.Height);

				// 步骤4: 保存
				var sw4 = System.Diagnostics.Stopwatch.StartNew();
				SaveImages(leftProc.ToBitmap(), rightProc.ToBitmap(), merged, pid, isOk, status);
				var saveMs = sw4.Elapsed.TotalMilliseconds;
				Logger.Info("[Back] 步骤4完成: 保存=" + saveMs.ToString("F1") + "ms");

				var total = sw.Elapsed.TotalMilliseconds;
				_perfMonitor?.Record(new PerformanceMonitor.PerformanceRecord
				{
					Timestamp = DateTime.Now,
					Station = "Back",
					ProductId = pid,
					InferenceTimeMs = inferMs,
					DrawTimeMs = drawMs,
					SaveTimeMs = saveMs,
					TotalTimeMs = total,
					Result = isOk
				});
				var defStats = new Dictionary<string, int>();
				foreach (var s in status)
				{
					if (s != "OK")
					{
						if (defStats.ContainsKey(s)) defStats[s]++;
						else defStats[s] = 1;
					}
				}
				string defStr = defStats.Count > 0 ? string.Join(" ", defStats.Select(kv => kv.Key + ":" + kv.Value))
					: "条码:0 日期码:0 明显挂钩:0 轻微挂钩:0";
				defStr = " | " + defStr;
				Logger.Info($"[Back] 完成 P={p} OK={boxOk} NG={status.Count - boxOk}{defStr} | 耗时={total:F0}ms");
					ModelPerfTracker.RecordPipeline("Back", 0, inferMs, drawMs, saveMs, total);
				Logger.Trace("[Back] ✓ 全流程完成 结果=" + (isOk ? "OK" : "NG") + " 总=" + total.ToString("F0") + "ms");
				OnResultReady?.Invoke(result);
				OnStatusUpdate?.Invoke(status, p);
			}
			catch (Exception ex)
			{
				Logger.Error("[Back] 异常 Pid=" + pid + ": " + ex.Message);
				result.BackResult = false;
				OnResultReady?.Invoke(result);
			}
			finally
			{
				// 释放裁图产生的 Mat (与原始 leftMat/rightMat 不同时才需释放, 原始由调用方释放)
				if (leftProc != null && leftProc != leftMat) leftProc.Dispose();
				if (rightProc != null && rightProc != rightMat) rightProc.Dispose();
			}
		}

		// ====== 条形码识别 (BarcodeCore.dll, 逐盒ROI, 裁图+OpenCV预处理保留) ======
		/// <summary>条码识别: 逐盒ROI裁剪→ApplyBarcodePreprocess(对比度/直方图/高斯/中值/阈值/形态学)→BarcodeCore.dll解码→与参考条码比对</summary>
		private Dictionary<int, List<BoxDefect>> RecognizeBarcodes(Mat left, Mat right, int hp)
		{
			var r = new Dictionary<int, List<BoxDefect>>();
			if (!EnableBarcodeCheck) { Logger.Debug("[Back] 条码检测已停用，跳过"); return r; }
			string refBarcode = _sku?.BackBarcode;
			// 去除参考条码首位0(防止识别/配置的首位0不匹配)
			refBarcode = StripLeadingZero(refBarcode);
			// 即使无参考条码也继续解码（仅显示识别结果，不做比对）
			try
			{
				int hL = left.Height, wL = left.Width, hR = right.Height, wR = right.Width;
				double syRatio = (_barcodeParams != null) ? _barcodeParams.BcStartHeightRatio : (2.0 / 3.0);
				int bwL = wL / hp, bwR = wR / hp, syL = (int)(hL * syRatio), syR = (int)(hR * syRatio);
				Logger.Debug("[Back] 条码BarcodeCore: 左" + wL + "x" + hL + " boxW=" + bwL);

				for (int i = 0; i < hp; i++)
				{
					int sx = i * bwL, rw = (i < hp - 1) ? bwL : (wL - sx), rh = hL - syL;
					if (rw <= 0 || rh <= 0) continue;
					using (var roi = new Mat(left, new CvRect(sx, syL, rw, rh)).Clone())
					{
						var defs = DecodeBarcodeCore(roi, refBarcode, sx, syL, wL, hL, i);
						if (defs != null)
					{
						if (!r.ContainsKey(i))
							r[i] = new List<BoxDefect>();
						r[i].AddRange(defs);
					}
					}
				}

				for (int j = 0; j < hp; j++)
				{
					int gi = hp + j, sx = j * bwR, rw = (j < hp - 1) ? bwR : (wR - sx), rh = hR - syR;
					if (rw <= 0 || rh <= 0) continue;
					using (var roi = new Mat(right, new CvRect(sx, syR, rw, rh)).Clone())
					{
						var defs = DecodeBarcodeCore(roi, refBarcode, sx, syR, wR, hR, gi);
						if (defs != null)
						{
							if (!r.ContainsKey(gi))
								r[gi] = new List<BoxDefect>();
							r[gi].AddRange(defs);
						}
					}
				}
				Logger.Debug("[Back] 条码: " + r.Count + "盒识别");
			}
			catch (Exception ex) { Logger.Error("条码异常: " + ex.Message); }
			return r;
		}

		/// <summary>单盒条码解码: 预处理管线→BarcodeCore.dll解码→多结果选优(参考条码匹配/编辑距离)→返回缺陷列表.
		/// 第一项=判定项(条码:xxx=OK显示 / 条码错:xxx=NG), 其余项=同区域附加码("条码:"前缀仅显示).
		/// 每项携带各自四点框(QuadPoints), 同区域多码时每个码画出自己的位置框</summary>
		private List<BoxDefect> DecodeBarcodeCore(Mat roi, string refBarcode, int ox, int oy, int fw, int fh, int boxIdx)
		{
			try
			{
				var p = _barcodeParams ?? Config.ModelParams.Load("barcode");
				Mat proc = ApplyBarcodePreprocess(roi, p);
				using (proc)
				{
					// BarcodeCore.dll 解码 (预处理图失败→原图灰度重试, 与旧ZXing流程一致)
					var swDecode = System.Diagnostics.Stopwatch.StartNew();
					var items = DecodeWithRetry(roi, proc, boxIdx);
					double decodeMs = swDecode.Elapsed.TotalMilliseconds;
					float pad = roi.Width * 0.03f; // 盒区间隙3%
					float[] defBox = new float[] { (float)(ox + pad) / fw, (float)oy / fh, (float)(ox + roi.Width - pad) / fw, (float)(oy + roi.Height) / fh };

					if (items == null || items.Count == 0)
					{
						// 漏识别是最常见问题: 未识别到的盒子同样写入独立条码日志(耗时+过程细节), 便于统计漏检率/排查
						BarcodeLogger.Warn($"[Back] 盒{boxIdx + 1}: 未识别到 耗时={decodeMs:F0}ms");
						return new List<BoxDefect> { new BoxDefect(boxIdx, "条码缺少", defBox) };
					}

					// ── 选优: 判定项 = 参考条码精确匹配 > 编辑距离最优 (与旧逻辑一致) ──
					string bestText = null;
					BarcodeTextResult bestItem = null;
					if (items.Count == 1)
						{
							bestText = StripLeadingZero(items[0].Text);
							bestItem = items[0];
						}
					else if (!string.IsNullOrEmpty(refBarcode))
					{
						if (items.Any(res => StripLeadingZero(res.Text) == refBarcode))
						{ bestText = refBarcode; bestItem = items.First(res => StripLeadingZero(res.Text) == refBarcode); }
						else
						{
							int bestDist = int.MaxValue;
							foreach (var res in items)
							{
								if (string.IsNullOrEmpty(res.Text)) continue;
								int dist = LevenshteinDistance(StripLeadingZero(res.Text), refBarcode);
								if (dist < bestDist)
						{
							bestDist = dist;
							bestText = StripLeadingZero(res.Text);
							bestItem = res;
						}
							}
						}
					}
					else { bestText = StripLeadingZero(items[0].Text); bestItem = items[0]; }

					bool hasRef = !string.IsNullOrEmpty(refBarcode);
					Logger.Debug("[Back] 条码盒" + (boxIdx + 1) + ": 识=" + (bestText ?? "(空)") + " 标=" + (hasRef ? refBarcode : "(无)") + " " + (hasRef && bestText == refBarcode ? "OK" : hasRef ? "NG" : ""));

					var defs = new List<BoxDefect>();
					// 判定项(列表第一项): 决定盒状态
					string judgeType;
					if (hasRef && bestText == refBarcode) judgeType = "条码:" + bestText;
					else if (!hasRef) judgeType = "条码:" + (bestText ?? items[0].Text);
					else judgeType = "条码错:" + bestText; // 不匹配 → NG+橙色
					defs.Add(new BoxDefect(boxIdx, judgeType, defBox) { QuadPoints = NormalizeQuad(bestItem, ox, oy, fw, fh) });

					// 附加码(同区域其余识别结果): 仅显示+各自四点框, 不影响判定状态
					foreach (var it in items)
					{
						if (ReferenceEquals(it, bestItem)) continue;
						defs.Add(new BoxDefect(boxIdx, "条码:" + StripLeadingZero(it.Text), defBox) { QuadPoints = NormalizeQuad(it, ox, oy, fw, fh) });
						BarcodeLogger.Info($"[Back] 盒{boxIdx + 1}: 附加码 识={StripLeadingZero(it.Text)} 类型={it.Format ?? "-"} 四点=({it.X1},{it.Y1})-({it.X2},{it.Y2})-({it.X3},{it.Y3})-({it.X4},{it.Y4})");
					}

					// 条码独立日志(Logs/Barcode_*.log): 判定项解码详情, 与主日志分离便于单独排查
					BarcodeLogger.Info($"[Back] 盒{boxIdx + 1}: 识={bestText ?? "(空)"} 类型={bestItem?.Format ?? "-"} 四点=({bestItem?.X1},{bestItem?.Y1})-({bestItem?.X2},{bestItem?.Y2})-({bestItem?.X3},{bestItem?.Y3})-({bestItem?.X4},{bestItem?.Y4}) 标={(hasRef ? refBarcode : "(无)")} {(hasRef && bestText == refBarcode ? "OK" : hasRef ? "NG" : "仅显示")} 耗时={decodeMs:F0}ms");
					return defs;
				}
			}
			catch (Exception ex)
			{
				Logger.Debug("[Back] 条码异常盒" + (boxIdx + 1) + ": " + ex.Message);
				float pad2 = roi.Width * 0.03f;
				return new List<BoxDefect> { new BoxDefect(boxIdx, "条码缺少",
					new float[] {
						(float)(ox + pad2) / fw,
						(float)oy / fh,
						(float)(ox + roi.Width - pad2) / fw,
						(float)(oy + roi.Height) / fh
					}) };
			}
		}

		/// <summary>ROI四点坐标 → 整图归一化四点 [x1,y1,x2,y2,x3,y3,x4,y4] (左上→右上→右下→左下)</summary>
		private static float[] NormalizeQuad(BarcodeTextResult it, int ox, int oy, int fw, int fh)
		{
			if (it == null) return null;
			return new float[] {
				(float)(ox + it.X1) / fw, (float)(oy + it.Y1) / fh,
				(float)(ox + it.X2) / fw, (float)(oy + it.Y2) / fh,
				(float)(ox + it.X3) / fw, (float)(oy + it.Y3) / fh,
				(float)(ox + it.X4) / fw, (float)(oy + it.Y4) / fh
			};
		}

		/// <summary>BarcodeCore解码+灰度重试: 预处理图未识别→原图灰度重试(与旧ZXing流程一致). 引擎级错误码(负值)不重试直接返回null→条码缺少.
		/// 注: 旧ZXing的TryHarder/旋转重试选项已由 barcode.config.json 的 EnableZXingFallback/AutoRotationCorrection 接管</summary>
		private static List<BarcodeTextResult> DecodeWithRetry(Mat roi, Mat proc, int boxIdx)
		{
			int status = BarcodeCoreEngine.Decode(proc, out var items);
			if (status < 0)
			{
				Logger.Debug("[Back] 盒" + (boxIdx + 1) + " BarcodeCore错误码=" + status);
				BarcodeLogger.Error($"[Back] 盒{boxIdx + 1}: 预处理图解码引擎错误码={status}");
				return null;
			}
			if (items != null && items.Count > 0) return items;

			// 预处理图未识别到 → 原图灰度重试
			BarcodeLogger.Info($"[Back] 盒{boxIdx + 1}: 预处理图未识别到, 转原图灰度重试");
			using (var gray = new Mat())
			{
				Cv2.CvtColor(roi, gray, roi.Channels() == 3 ? ColorConversionCodes.BGR2GRAY : ColorConversionCodes.BGRA2GRAY);
				int s2 = BarcodeCoreEngine.Decode(gray, out var items2);
				if (s2 < 0)
				{
					Logger.Debug("[Back] 盒" + (boxIdx + 1) + " 灰度重试BarcodeCore错误码=" + s2);
					BarcodeLogger.Error($"[Back] 盒{boxIdx + 1}: 灰度重试引擎错误码={s2}");
					return null;
				}
				Logger.Debug("[Back] 盒" + (boxIdx + 1) + " 灰度重试=" + (items2 != null ? items2.Count : 0) + "个");
				BarcodeLogger.Info($"[Back] 盒{boxIdx + 1}: 灰度重试结果={(items2 != null ? items2.Count : 0)}个");
				return items2;
			}
		}

		/// <summary>条码OpenCV预处理管线: 1.对比度亮度调整 2.灰度化 3.直方图均衡 4.高斯/中值滤波 5.自适应/Otsu/固定阈值 6.反转 7.形态学(闭/开/膨胀/腐蚀)</summary>
		/// <summary>
		/// 条码图像预处理管线: 灰度→对比度/亮度→直方图均衡→高斯模糊→中值滤波→阈值化→反色→形态学
		/// 每步创建新Mat后释放旧Mat，防止内存泄漏
		/// </summary>
		private static Mat ApplyBarcodePreprocess(Mat src, Config.ModelParams p)
		{
			// 预处理关闭: 仅转灰度
			if (!p.BcEnablePreprocess)
			{
				var g2 = new Mat();
				Cv2.CvtColor(src, g2, ColorConversionCodes.BGR2GRAY);
				return g2;
			}

			Mat m = src.Clone();

			// 1. 对比度/亮度调整
			if (Math.Abs(p.BcContrastAlpha - 1.0f) > 0.001f || p.BcBrightnessBeta != 0)
			{
				var t = new Mat();
				m.ConvertTo(t, -1, p.BcContrastAlpha, p.BcBrightnessBeta);
				m.Dispose();
				m = t;
			}

			// 2. 转灰度（如非单通道）
			if (m.Channels() != 1)
			{
				var g2 = new Mat();
				var cc = m.Channels() == 3
					? ColorConversionCodes.BGR2GRAY
					: ColorConversionCodes.BGRA2GRAY;
				Cv2.CvtColor(m, g2, cc);
				m.Dispose();
				m = g2;
			}

			// 3. 直方图均衡
			if (p.BcEnableEqualizeHist)
			{
				var e = new Mat();
				Cv2.EqualizeHist(m, e);
				m.Dispose();
				m = e;
			}

			// 4. 高斯模糊
			if (p.BcEnableGaussianBlur)
			{
				var b = new Mat();
				Cv2.GaussianBlur(m, b, new OpenCvSharp.Size(5, 5), 0);
				m.Dispose();
				m = b;
			}

			// 5. 中值滤波
			if (p.BcEnableMedianBlur)
			{
				var b = new Mat();
				Cv2.MedianBlur(m, b, 5);
				m.Dispose();
				m = b;
			}

			// 6. 阈值化
			int tm = p.BcThresholdMode;
			if (tm == 1) // 自适应阈值
			{
				int bs = p.BcAdaptiveBlockSize;
				if (bs % 2 == 0) bs++;
				var t = new Mat();
				Cv2.AdaptiveThreshold(m, t, 255,
					AdaptiveThresholdTypes.MeanC,
					ThresholdTypes.Binary, bs, p.BcAdaptiveC);
				m.Dispose();
				m = t;
			}
			else if (tm == 2) // OTSU
			{
				var t = new Mat();
				Cv2.Threshold(m, t, 0, 255,
					ThresholdTypes.Otsu | ThresholdTypes.Binary);
				m.Dispose();
				m = t;
			}
			else if (tm == 3) // 固定阈值
			{
				var t = new Mat();
				Cv2.Threshold(m, t, p.BcFixedThreshold, 255,
					ThresholdTypes.Binary);
				m.Dispose();
				m = t;
			}

			// 7. 反色
			if (p.BcEnableInvert)
			{
				var t = new Mat();
				Cv2.BitwiseNot(m, t);
				m.Dispose();
				m = t;
			}

			// 8. 形态学操作
			var k = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(3, 3));
			try
			{
				if (p.BcEnableMorphClose)
				{
					var t = new Mat();
					Cv2.MorphologyEx(m, t, MorphTypes.Close, k);
					m.Dispose();
					m = t;
				}
				if (p.BcEnableMorphOpen)
				{
					var t = new Mat();
					Cv2.MorphologyEx(m, t, MorphTypes.Open, k);
					m.Dispose();
					m = t;
				}
				if (p.BcEnableMorphDilate)
				{
					var t = new Mat();
					Cv2.MorphologyEx(m, t, MorphTypes.Dilate, k);
					m.Dispose();
					m = t;
				}
				if (p.BcEnableMorphErode)
				{
					var t = new Mat();
					Cv2.MorphologyEx(m, t, MorphTypes.Erode, k);
					m.Dispose();
					m = t;
				}
			}
			finally { k.Dispose(); }

			return m;
		}

		/// <summary>计算编辑距离(Levenshtein) — 用于条码模糊匹配, 在多个解码结果中选最优</summary>
		/// <summary>去除条码首位0: 第一位是'0'则Substring(1)移除, 单字符判断O(1)无GC压力</summary>
		private static int LevenshteinDistance(string a, string b)
		{
			if (string.IsNullOrEmpty(a)) return b == null ? 0 : b.Length;
			if (string.IsNullOrEmpty(b)) return a.Length;
			int la = a.Length, lb = b.Length;
			int[,] dp = new int[la + 1, lb + 1];
			for (int i = 0; i <= la; i++) dp[i, 0] = i;
			for (int j = 0; j <= lb; j++) dp[0, j] = j;
			for (int i = 1; i <= la; i++)
				for (int j = 1; j <= lb; j++)
					dp[i, j] = Math.Min(
					Math.Min(dp[i - 1, j] + 1, dp[i, j - 1] + 1),
					dp[i - 1, j - 1] + (a[i - 1] == b[j - 1] ? 0 : 1));
			return dp[la, lb];
		}

		/// <summary>去除条码首位0: 第一位是'0'则Substring(1)移除, 单字符判断O(1)无GC压力</summary>
		private static string StripLeadingZero(string s)
		{
			if (!string.IsNullOrEmpty(s) && s[0] == '0') return s.Substring(1);
			return s ?? "";
		}

		// ====== 日期码识别 (C1分割+C2分类+C3 OCR, 合并左右图后推理) ======
		private static readonly Regex MFG_RX = new Regex(@"MFG\s*(\d{2}/\d{2}/\d{4})", RegexOptions.IgnoreCase);
		private static readonly Regex LOT_RX = new Regex(@"L[0O]T\s*(\d{4}/\d{2}/\d{2})", RegexOptions.IgnoreCase);
		private static readonly Regex EXP_RX = new Regex(@"EXP\s*(\d{2}/\d{2}/\d{4})", RegexOptions.IgnoreCase);

	/// <summary>日期码识别: 合并左右图(Cv2.HConcat)→crop下1/3区域→ProcessDateCodeFull(C1分割+C2分类+C3 OCR三步流水线)</summary>
		private Dictionary<int, List<BoxDefect>> RecognizeDateCodes(Mat left, Mat right, int hp)
		{
			var r = new Dictionary<int, List<BoxDefect>>();
			string codingFormat = _sku?.CodingFormat ?? "";
			try
			{
				int p = hp * 2;
				using (Mat merged = new Mat())
				{
					Cv2.HConcat(left, right, merged);
					double dcTopRatio = (_datecodeParams != null) ? _datecodeParams.StartHeightRatioDateCode : (2.0 / 3.0);
					double dcBottomRatio = (_datecodeParams != null) ? _datecodeParams.DateCodeCropBottomRatio : 0.0;
					// 防呆: 裁顶+裁底 ≥ 95% 则自动限制
					if (dcTopRatio + dcBottomRatio >= 0.95)
					{
						double excess = dcTopRatio + dcBottomRatio - 0.90;
						dcTopRatio = Math.Max(0, dcTopRatio - excess / 2.0);
						dcBottomRatio = Math.Max(0, dcBottomRatio - excess / 2.0);
						Logger.Warning("[Back] 日期码裁图比例异常(顶" + dcTopRatio.ToString("F2") + "+底" + dcBottomRatio.ToString("F2") + "≥0.95), 已自动修正为顶" + dcTopRatio.ToString("F2") + "+底" + dcBottomRatio.ToString("F2"));
					}
					int fullH = merged.Height, fullW = merged.Width;
					int cropY = (int)(fullH * dcTopRatio);
					int cropBottomPx = (int)(fullH * dcBottomRatio);
					int cropH = fullH - cropY - cropBottomPx;
					if (cropH < fullH * 0.05) cropH = (int)(fullH * 0.05); // 至少保留5%高度
					Logger.Debug("[Back] 日期码裁图: 全图=" + fullW + "x" + fullH + " 裁顶=" + cropY + "px(" + dcTopRatio.ToString("F2") + ") 裁底=" + cropBottomPx + "px(" + dcBottomRatio.ToString("F2") + ") 有效=" + fullW + "x" + cropH);
					using (var crop = new Mat(merged, new CvRect(0, cropY, fullW, cropH)).Clone())
					{
						r = ProcessDateCodeFull(crop, codingFormat, p, cropY, fullH);
					}
				}
				Logger.Debug("[Back] 日期码: " + r.Count + "盒识别");
			}
			catch (Exception ex) { Logger.Error("日期码异常: " + ex.Message); }
			return r;
		}

	/// <summary>
	/// 日期码三步流水线(C1/C2/C3), 对齐参考AIRunThread.cs.
	/// C1=ViMo全图分割->ConnectedComponents提取区域->数量门控.
	/// C2=ViMo分类判断重影, 重影则跳过C3.
	/// C3=ViMo OCR识别->条数校验->DateComparison校验(MFG/LOT/双排).
	/// 三态输出: 0=OK, 1=NG剔除(数量/重影/前缀错), 2=NG不剔除(日期不对/不打码区域多).
	/// </summary>
	private Dictionary<int, List<BoxDefect>> ProcessDateCodeFull(Mat img, string codingFormat, int p, int cropY, int fullH)
		{
			var r = new Dictionary<int, List<BoxDefect>>();
			int fw = img.Width, fh = img.Height, halfW = fw / 2, boxW = fw / p;
			int hp2 = p / 2;
			bool isNoCode = codingFormat.Contains("不打码") || codingFormat.Contains("内销码");

			try
			{
				// C1: 分割模型全图推理 → 从Mask提取连通域
				var swC1 = System.Diagnostics.Stopwatch.StartNew();
				ResponseList<SegmentationResponse> segRsp;
				var dcSegSw = System.Diagnostics.Stopwatch.StartNew();
			int segRet = _models.BackDateCodeSegModel.Run(img, out segRsp);
			ModelPerfTracker.Record("Back", "日期码C1分割", dcSegSw.Elapsed.TotalMilliseconds);
				int rspCount = segRsp?.Count ?? 0;
				Logger.Debug("[Back] C1分割: ret=" + segRet + " rsp=" + rspCount + " " + swC1.Elapsed.TotalMilliseconds.ToString("F0") + "ms");
				if (segRet != 0 || rspCount == 0) return r;

				var regions = new List<CvRect>();
				foreach (var item in segRsp)
				{
					var mask = item.Item2.Mask;
					if (mask == null || mask.Empty()) continue;
					int nz = Cv2.CountNonZero(mask);
					Logger.Debug("[Back] Mask: " + mask.Width + "x" + mask.Height + " nz=" + nz);
					using (Mat mc = mask.Clone())
					{
						Mat labels = new Mat(), stats = new Mat(), centroids = new Mat();
						int nLabels = Cv2.ConnectedComponentsWithStats(mc, labels, stats, centroids, PixelConnectivity.Connectivity8);
						for (int k = 1; k < nLabels; k++)
						{
							int sx = stats.At<int>(k, 0), sy = stats.At<int>(k, 1);
							int sw2 = stats.At<int>(k, 2), sh = stats.At<int>(k, 3);
							if (sw2 > 5 && sh > 5) regions.Add(new CvRect(sx, sy, sw2, sh));
						}
						labels.Dispose(); stats.Dispose(); centroids.Dispose();
					}
				}
				int numRegions = regions.Count;
				Logger.Debug("[Back] C1区域数: " + numRegions);

				// ── C1数量门控(逐盒, 对齐参考AIRunThread单产品→适配多盒拼接) ──
				// 参考为单产品单图, Num==1; 本系统多盒拼接, 需逐盒检查区域数
				if (!isNoCode)
				{
					// MFG / LOT / 双排: 统计每盒区域数, 逐盒门控
					var boxRegionCounts = new Dictionary<int, int>();
					foreach (var rect in regions)
					{
						int cx = rect.X + rect.Width / 2;
						int bi = cx / boxW;
						if (bi < 0) bi = 0; if (bi >= p) bi = p - 1;
						if (!boxRegionCounts.ContainsKey(bi)) boxRegionCounts[bi] = 0;
						boxRegionCounts[bi]++;
					}
					// 有盒子的区域数≠1 → 该盒标记数量错误, 并为缺失盒子补充错误
					for (int bi = 0; bi < p; bi++)
					{
						int count = boxRegionCounts.ContainsKey(bi) ? boxRegionCounts[bi] : 0;
						if (count == 0)
						{
							// 该盒无日期码区域 → NG剔除
							float[] nbx = new float[] {
								(float)(bi < hp2 ? bi * boxW : (bi - hp2) * boxW) / halfW,
								(float)cropY / fullH,
								(float)(bi < hp2 ? (bi + 1) * boxW : (bi - hp2 + 1) * boxW) / halfW,
								1f
							};
							if (!r.ContainsKey(bi)) r[bi] = new List<BoxDefect>();
							r[bi].Add(new BoxDefect(bi, "日期码缺少", nbx));
							Logger.Debug("[Back] 盒" + (bi + 1) + " 日期码缺少 → NG剔除");
						}
						else if (count > 1)
						{
							// 该盒有多个日期码区域 → NG剔除
							Logger.Debug("[Back] 盒" + (bi + 1) + " 日期码数量错误: " + count + "/1 → NG剔除");
						}
					}
				}
				else
				{
					// 不打码 / 内销码: 整体区域数 < RemoveNum → OK, >= RemoveNum → NG不剔除
					int removeNum = (_datecodeParams != null) ? _datecodeParams.DateCodeRemoveNum : 3;
					if (numRegions >= removeNum)
					{
						Logger.Debug("[Back] 不打码模式, 区域数=" + numRegions + " >= RemoveNum=" + removeNum + " → NG不剔除");
						int fallbackBox = 0;
						if (regions.Count > 0)
						{
							int cx = regions[0].X + regions[0].Width / 2;
							fallbackBox = cx / boxW;
							if (fallbackBox < 0) fallbackBox = 0; if (fallbackBox >= p) fallbackBox = p - 1;
						}
						if (!r.ContainsKey(fallbackBox))
							r[fallbackBox] = new List<BoxDefect>();
						r[fallbackBox].Add(new BoxDefect(fallbackBox,
							"不打码异常(" + numRegions + ")", new float[] { 0.05f, (float)cropY / fullH, 0.95f, 1f }));
					}
					return r; // 不打码模式不执行C2/C3
				}

				// ── C2+C3: 逐区域处理, 每盒内单区域正常、多区域标记错误 ──
				var boxRegionList = new Dictionary<int, List<CvRect>>();
				foreach (var rect in regions)
				{
					int cx = rect.X + rect.Width / 2;
					int bi = cx / boxW;
					if (bi < 0) bi = 0; if (bi >= p) bi = p - 1;
					if (!boxRegionList.ContainsKey(bi)) boxRegionList[bi] = new List<CvRect>();
					boxRegionList[bi].Add(rect);
				}
				foreach (var kv in boxRegionList)
				{
					int boxIdx = kv.Key;
					var boxRegions = kv.Value;
					if (boxRegions.Count > 1)
					{
						// 该盒多个区域 → 全部标记错误, 跳过C2/C3
						foreach (var rect in boxRegions)
						{
							int mx2 = Math.Max(0, rect.X - 5), myRaw2 = Math.Max(0, rect.Y - 5);
							int mw2 = Math.Min(fw - mx2, rect.Width + 10), mh2 = Math.Min(fh - myRaw2, rect.Height + 10);
							int my2 = myRaw2 + cropY;
							float[] nb = new float[] {
								(float)(mx2 - (boxIdx < hp2 ? 0 : halfW)) / halfW,
								(float)my2 / fullH,
								(float)(mx2 + mw2 - (boxIdx < hp2 ? 0 : halfW)) / halfW,
								(float)(my2 + mh2) / fullH
							};
							if (!r.ContainsKey(boxIdx)) r[boxIdx] = new List<BoxDefect>();
							r[boxIdx].Add(new BoxDefect(boxIdx, "日期码数量错误(" + boxRegions.Count + "/1)", nb));
						}
						continue; // 跳过C2/C3
					}
					// 恰好1个区域 → C2+C3
					var rect2 = boxRegions[0];
				{
					// boxIdx 已在上方确定，直接用
					int mx = Math.Max(0, rect2.X - 5), myRaw = Math.Max(0, rect2.Y - 5);
					int mw = Math.Min(fw - mx, rect2.Width + 10), mh = Math.Min(fh - myRaw, rect2.Height + 10);
					int my = myRaw + cropY;

					float[] normBox = new float[] {
						(float)(mx - (boxIdx < hp2 ? 0 : halfW)) / halfW,
						(float)my / fullH,
						(float)(mx + mw - (boxIdx < hp2 ? 0 : halfW)) / halfW,
						(float)(my + mh) / fullH
					};

					// C2: 重影分类
					bool c2Shadow = false;
					using (var cropC2 = new Mat(img, new CvRect(mx, myRaw, mw, mh)).Clone())
					{
						ResponseList<ClassificationResponse> clsRsp;
						var dcClsSw = System.Diagnostics.Stopwatch.StartNew();
					int clsRet = _models.BackDateCodeClsModel.Run(cropC2, out clsRsp);
					ModelPerfTracker.Record("Back", "日期码C2分类", dcClsSw.Elapsed.TotalMilliseconds);
						Logger.Debug("[Back] C2 clsRet=" + clsRet + " count=" + (clsRsp?.Count ?? 0));
						if (clsRet == 0 && clsRsp != null && clsRsp.Count > 0)
						{
							foreach (var ci in clsRsp)
							{
								var labels = ci.Item2.Labels;
								if (labels == null || !labels.Any()) continue;
								foreach (var lbl in labels)
								{
									float s = 0;
									try { s = Convert.ToSingle(lbl.GetType().GetProperty("Score")?.GetValue(lbl) ?? 0f); } catch { }
									Logger.Debug("[Back] C2 Label=" + lbl.Label + " Score=" + s.ToString("F4"));
									if (lbl.Label == "NG" || lbl.Label == "重影") c2Shadow = true;
								}
							}
						}
					}
					Logger.Debug("[Back] C2最终: c2Shadow=" + c2Shadow);

					if (c2Shadow)
					{
						// 重影 → NG剔除, 跳过C3 (对齐 AIRunThread.cs:191-196)
						Logger.Debug("[Back] C2重影 → NG剔除, 跳过C3");
						if (!r.ContainsKey(boxIdx))
							r[boxIdx] = new List<BoxDefect>();
						r[boxIdx].Add(new BoxDefect(boxIdx, "日期码重影", normBox));
						continue; // 关键: 跳过C3 OCR
					}

					// C3: OCR (仅C2通过时执行, 对齐 AIRunThread.cs:153)
					using (var cropC3 = new Mat(img, new CvRect(mx, myRaw, mw, mh)).Clone())
					{
						ResponseList<OcrResponse> ocrRsp;
						var dcOcrSw = System.Diagnostics.Stopwatch.StartNew();
				int ocrRet = _models.BackDateCodeOcrModel.Run(cropC3, out ocrRsp);
				ModelPerfTracker.Record("Back", "日期码C3 OCR", dcOcrSw.Elapsed.TotalMilliseconds);
						if (ocrRet != 0 || ocrRsp == null || ocrRsp.Count == 0)
						{
							// OCR无结果 → NG剔除
							Logger.Debug("[Back] C3 OCR无结果 → NG剔除");
							if (!r.ContainsKey(boxIdx))
								r[boxIdx] = new List<BoxDefect>();
							r[boxIdx].Add(new BoxDefect(boxIdx, "日期码错误(OCR无结果)", normBox));
							continue;
						}

						var texts = new List<string>();
						foreach (var rt in ocrRsp)
						{
							if (rt.Item2.Blocks == null) continue;
							foreach (var blk in rt.Item2.Blocks)
								if (!string.IsNullOrWhiteSpace(blk.Label)) texts.Add(blk.Label);
						}
						Logger.Debug("[Back] C3 OCR texts=" + texts.Count + ": " + string.Join(" | ", texts));

						if (texts.Count == 0)
						{
							if (!r.ContainsKey(boxIdx))
								r[boxIdx] = new List<BoxDefect>();
							r[boxIdx].Add(new BoxDefect(boxIdx, "日期码错误(OCR无文本)", normBox));
							continue;
						}

						// ── C3文本条数校验 (对齐 AIRunThread.cs:465-472) ──
						int c3Result;
						if (codingFormat.Contains("双排"))
						{
							// 双排: 必须恰好2条
							if (texts.Count != 2)
							{
								if (!r.ContainsKey(boxIdx))
									r[boxIdx] = new List<BoxDefect>();
								r[boxIdx].Add(new BoxDefect(boxIdx,
									"日期码错误(双排需2条,实为" + texts.Count + ")", normBox));
								continue;
							}
							c3Result = CheckDoubleRow(texts);
						}
						else if (codingFormat.Contains("MFG"))
						{
							// MFG: 必须恰好1条
							if (texts.Count != 1)
							{
								if (!r.ContainsKey(boxIdx))
									r[boxIdx] = new List<BoxDefect>();
								r[boxIdx].Add(new BoxDefect(boxIdx,
									"日期码错误(MFG需1条,实为" + texts.Count + ")", normBox));
								continue;
							}
							c3Result = CheckMFG(texts[0]);
						}
						else if (codingFormat.Contains("LOT"))
						{
							// LOT: 必须恰好1条
							if (texts.Count != 1)
							{
								if (!r.ContainsKey(boxIdx))
									r[boxIdx] = new List<BoxDefect>();
								r[boxIdx].Add(new BoxDefect(boxIdx,
									"日期码错误(LOT需1条,实为" + texts.Count + ")", normBox));
								continue;
							}
							c3Result = CheckLOT(texts[0]);
						}
						else
						{
							c3Result = 0; // 未知格式, 仅显示
						}

						string allText = string.Join(" ", texts);
						Logger.Debug("[Back] 日期码盒" + (boxIdx + 1) + ": " + allText + " c3Result=" + c3Result);

						if (!r.ContainsKey(boxIdx))
							r[boxIdx] = new List<BoxDefect>();

						string label;
						if (c3Result == 0)
							// OK — 仅显示, 不设NG
							label = codingFormat.Contains("双排") ? "双排:" + allText : "日期:" + allText;
						else if (c3Result == 1)
							// NG剔除 — 前缀/格式不匹配
							label = "日期码错误(" + allText + ")";
						else // c3Result == 2
							// NG不剔除 — 前缀匹配但日期不对
							label = "日期码不完全正确(" + allText + ")";

						r[boxIdx].Add(new BoxDefect(boxIdx, label, normBox));
					}
				}
			}
			}  // foreach boxRegionList
			catch (Exception ex) { Logger.Error("日期码处理异常: " + ex.Message); }
			return r;
		}

	/// <summary>校验MFG格式日期(对齐DateComparison.MFGExtractAndCompareDate): "MFG dd/MM/yyyy"→提取日期→比对当天, 0=正确 1=NG剔除(前缀/格式错) 2=NG不剔除(日期不匹配)</summary>
		private int CheckMFG(string text)
		{
			// 前缀校验: 必须包含M/F/G任一字符且长度≥13 (对齐参考)
			if (!Regex.IsMatch(text, "[MFG]") || text.Length < 13) return 1;
			var m = MFG_RX.Match(text);
			if (!m.Success) return 2;  // 前缀对但正则不匹配 → NG不剔除
			if (DateTime.TryParseExact(m.Groups[1].Value, "dd/MM/yyyy", null,
				System.Globalization.DateTimeStyles.None, out DateTime dt))
				return dt.Date == DateTime.Now.Date ? 0 : 2;
			return 2; // 日期解析失败 → NG不剔除
		}
	/// <summary>校验LOT格式日期(对齐DateComparison.LOTExtractAndCompareDate): "LOT/L0T yyyy/MM/dd"→提取日期→比对当天, 0=正确 1=NG剔除 2=NG不剔除</summary>
		private int CheckLOT(string text)
		{
			// 前缀校验: 前3字符必须匹配L/0/O/T且长度≥13 (对齐参考)
			string prefix = text.Length >= 3 ? text.Substring(0, 3) : text;
			if (!Regex.IsMatch(prefix, "[L0OT]") || text.Length < 13) return 1;
			var m = LOT_RX.Match(text);
			if (!m.Success) return 2;
			if (DateTime.TryParseExact(m.Groups[1].Value, "yyyy/MM/dd", null,
				System.Globalization.DateTimeStyles.None, out DateTime dt))
				return dt.Date == DateTime.Now.Date ? 0 : 2;
			return 2;
		}
		/// <summary>
/// 双排日期码校验(对齐AIRunThread参考实现).
/// OCR已保证lines.Count==2, 按顺序line0/line1确定MFG+EXP顺序后逐行校验.
/// </summary>
/// </summary>
		private int CheckDoubleRow(List<string> lines)
		{
			if (lines.Count != 2) return 1;
			string line0 = lines[0].Length >= 3 ? lines[0].Substring(0, 3) : lines[0];
			string line1 = lines[1].Length >= 3 ? lines[1].Substring(0, 3) : lines[1];

			if (Regex.IsMatch(line0, "[MFG]"))
			{
				int mfgR = CheckMFG(lines[0]);
				if (mfgR != 0) return mfgR;
				if (Regex.IsMatch(line1, "[EXP]"))
					return CheckEXP(lines[1]);
				else return 1;
			}
			else if (Regex.IsMatch(line0, "[EXP]"))
			{
				int expR = CheckEXP(lines[0]);
				if (expR != 0) return expR;
				if (Regex.IsMatch(line1, "[MFG]"))
					return CheckMFG(lines[1]);
				else return 1;
			}
			else return 1;
		}
		private int CheckEXP(string text)
		{
			if (!Regex.IsMatch(text, "[EXP]") || text.Length < 13) return 1;
			var m = EXP_RX.Match(text);
			if (!m.Success) return 2;
			if (DateTime.TryParseExact(m.Groups[1].Value, "dd/MM/yyyy", null,
				System.Globalization.DateTimeStyles.None, out DateTime dt))
				return dt.Date == DateTime.Now.AddYears(10).Date ? 0 : 2;
			return 2;
		}

		// ====== 盒子破损检测 (BackBoxBreakModel YOLO) ======
		/// <summary>
		/// 背面盒子破损检测: YOLO 推理 -> "盒子破损".
		/// 左/右半图独立推理，centerX映射到全局盒号.
		/// </summary>
		/// <summary>
		/// 背面盒子破损检测: 3×2网格裁图→逐张Predict→坐标映射→分盒→盒内NMS去重 (与正面一致)
		/// </summary>
		private Dictionary<int, List<BoxDefect>> DetectBoxBreak(Mat left, Mat right, int p)
		{
			var results = new Dictionary<int, List<BoxDefect>>();
			if (_models.BackBoxBreakModel == null)
			{
				Logger.Warning("[Back] 盒子破损模型为null, 跳过检测(检查setup.ini [AI_Models] BackBoxBreakModel路径和best.json是否存在)");
				return results;
			}
			try
			{
				int halfP = p / 2;
				Logger.Debug($"[Back] 盒子破损检测开始 P={p} 图={left.Width}x{left.Height} Conf={BackBoxConfThreshold:F2} Iou={BackBoxIouThreshold:F2}");

				// 本地函数: 处理单侧图像 (3×2网格裁图 → 逐张Predict → 坐标映射 → 分盒)
				void ProcessSide(Mat sourceImage, bool isLeft)
				{
					int currentW = sourceImage.Width;
					int currentH = sourceImage.Height;
					int baseIdx = isLeft ? 0 : halfP;

					var (patches, offsets) = GetCropPatchesAndOffsets(sourceImage, p);

					for (int i = 0; i < patches.Count; i++)
					{
						Mat patch = patches[i];
						CvPoint offset = offsets[i];

						try
						{
							var bbSw2 = System.Diagnostics.Stopwatch.StartNew();
					var detResult = _models.BackBoxBreakModel.Predict(patch, BackBoxConfThreshold, BackBoxIouThreshold);
					ModelPerfTracker.Record("Back", "盒子破损", bbSw2.Elapsed.TotalMilliseconds);
							if (detResult?.Boxes == null) continue;

							for (int j = 0; j < detResult.Boxes.Length; j++)
							{
								var box = detResult.Boxes[j];
								float score = (detResult.Scores != null && j < detResult.Scores.Length)
									? detResult.Scores[j] : 1.0f;

								// 映射回原图绝对坐标
								float origX1 = box.Left + offset.X;
								float origY1 = box.Top + offset.Y;
								float origX2 = box.Right + offset.X;
								float origY2 = box.Bottom + offset.Y;

								// 归一化到整图坐标
								float nx1 = origX1 / currentW, ny1 = origY1 / currentH;
								float nx2 = origX2 / currentW, ny2 = origY2 / currentH;

								// centerX 确定盒子索引
								float centerX = (origX1 + origX2) / 2f;
								int boxLocal = (int)(centerX / currentW * halfP);
								boxLocal = Math.Max(0, Math.Min(boxLocal, halfP - 1));
								int globalIdx = baseIdx + boxLocal;

								if (!results.ContainsKey(globalIdx))
									results[globalIdx] = new List<BoxDefect>();
								results[globalIdx].Add(new BoxDefect(globalIdx, "盒子破损",
									new float[] { nx1, ny1, nx2, ny2 }, score));
							}
						}
						finally { patch?.Dispose(); }
					}
				}

				// 处理左右两侧
				Logger.Info($"[Back BatchLog] ▶ 盒子破推理: batch=1 逐张Predict, 左3x2={3 * 2}patch 右3x2={3 * 2}patch (P={p})");
				ProcessSide(left, isLeft: true);
				ProcessSide(right, isLeft: false);

				// 盒内NMS去重 (重叠patch导致同一缺陷被多次检出)
				int totalBeforeNms = results.Values.Sum(v => v.Count);
				ApplyNmsPerBox(results, BackBoxIouThreshold);
				int totalAfterNms = results.Values.Sum(v => v.Count);
				Logger.Info($"[Back BatchLog] ◀ 盒子破推理完成: P={p}, 检出框={totalBeforeNms}→{totalAfterNms}(NMS后)");
			}
			catch (Exception ex) { Logger.Error("背面盒子破损异常: " + ex.Message); }
			return results;
		}

		/// <summary>3×2网格裁图: 水平3段+垂直2段(带10%重叠), 返回patch列表+偏移量</summary>
		private static (List<Mat> Patches, List<CvPoint> Offsets) GetCropPatchesAndOffsets(Mat image, int P)
		{
			int h = image.Height, w = image.Width;
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

			var patches = new List<Mat>();
			var offsets = new List<CvPoint>();

			foreach (var xb in xBoundaries)
				foreach (var yb in yBoundaries)
				{
					int pw = xb.end - xb.start, ph = yb.end - yb.start;
					CvRect roi = new CvRect(xb.start, yb.start, pw, ph);
					patches.Add(new Mat(image, roi).Clone());
					offsets.Add(new CvPoint(xb.start, yb.start));
				}

			return (patches, offsets);
		}

		/// <summary>盒内NMS去重: 重叠patch可能让同一缺陷被多次检出, 每盒独立做NMS</summary>
		private static void ApplyNmsPerBox(Dictionary<int, List<BoxDefect>> results, float iouThreshold)
		{
			foreach (var kvp in results.ToList())
			{
				var defects = kvp.Value;
				if (defects.Count <= 1) continue;

				var boxesWithScore = defects.Select(d => new float[] {
					d.BoundingBox[0], d.BoundingBox[1], d.BoundingBox[2], d.BoundingBox[3], d.Score
				}).ToList();

				var sorted = boxesWithScore
					.Select((b, i) => (box: b, idx: i))
					.OrderByDescending(x => x.box[4]).ToList();
				var removed = new bool[sorted.Count];
				var keep = new List<int>();

				for (int i = 0; i < sorted.Count; i++)
				{
					if (removed[i]) continue;
					keep.Add(sorted[i].idx);
					float ax1 = sorted[i].box[0], ay1 = sorted[i].box[1];
					float ax2 = sorted[i].box[2], ay2 = sorted[i].box[3];
					float areaA = (ax2 - ax1) * (ay2 - ay1);

					for (int j = i + 1; j < sorted.Count; j++)
					{
						if (removed[j]) continue;
						float bx1 = sorted[j].box[0], by1 = sorted[j].box[1];
						float bx2 = sorted[j].box[2], by2 = sorted[j].box[3];
						float xx1 = Math.Max(ax1, bx1), yy1 = Math.Max(ay1, by1);
						float xx2 = Math.Min(ax2, bx2), yy2 = Math.Min(ay2, by2);
						float iw = Math.Max(0, xx2 - xx1), ih = Math.Max(0, yy2 - yy1);
						float inter = iw * ih;
						float areaB = (bx2 - bx1) * (by2 - by1);
						float iou = inter / (areaA + areaB - inter);
						if (iou > iouThreshold) removed[j] = true;
					}
				}

				results[kvp.Key] = keep.Select(k => defects[k]).ToList();
			}
		}

		// ====== 挂钩缺陷检测 (原有代码不变) ======
	/// <summary>
	/// 挂钩缺陷检测: YOLO + 分割厚度计算.
	/// 明显错位(classId=1): 直接映射, DarkRed框.
	/// 轻微错位(classId=0): 分割 -> DistanceTransform -> 厚度>阈值 -> OrangeRed框.
	/// </summary>
	private Dictionary<int, List<BoxDefect>> DetectHookDamage(Mat left, Mat right, int p)
		{
			var results = new Dictionary<int, List<BoxDefect>>();
			if (!EnableHookCheck || _models.BackHookModel == null) return results;
			try
			{
				var images = new List<Mat> { left, right };
				double[] offsets = { 0.0, p / 2.0 };
				var hookSw = System.Diagnostics.Stopwatch.StartNew();
			var batchResults = _models.BackHookModel.PredictBatch(images, ConfThreshold, IouThreshold);
			ModelPerfTracker.Record("Back", "挂钩明显", hookSw.Elapsed.TotalMilliseconds);
				Logger.Debug("[Back] 挂钩批量推理: " + (batchResults?.Count ?? 0) + "张");

				for (int i = 0; i < (batchResults?.Count ?? 0); i++)
				{
					var detResult = batchResults[i];
					if (detResult?.Boxes == null || detResult.Boxes.Length == 0) continue;
					Mat curImg = images[i];
					int imgH = curImg.Height, imgW = curImg.Width;

					for (int j = 0; j < detResult.Boxes.Length; j++)
					{
						int cls = detResult.ClassIds[j];
						var bboxN = detResult.BoxesN[j];
						var bbox = detResult.Boxes[j];
						double cxN = bboxN.X + bboxN.Width / 2.0;
						int gi = (int)(cxN * (p / 2.0) + offsets[i]);
						gi = Math.Max(0, Math.Min(gi, p - 1));

						float score = (detResult.Scores != null && j < detResult.Scores.Length) ? detResult.Scores[j] : 1.0f;
						if (cls == 1)
						{
							AddDefect(results, gi, "挂钩明显错位",
								new float[] { (float)bboxN.X, (float)bboxN.Y, (float)(bboxN.X + bboxN.Width), (float)(bboxN.Y + bboxN.Height) }, score);
						}
						else if (cls == 0)
						{
							if (results.ContainsKey(gi) && results[gi].Any(d => d.DefectType == "挂钩明显错位"))
								continue;
							int x1 = Math.Max(0, bbox.X), y1 = Math.Max(0, bbox.Y);
							int x2 = Math.Min(imgW, bbox.X + bbox.Width), y2 = Math.Min(imgH, bbox.Y + bbox.Height);
							if (x2 <= x1 || y2 <= y1) continue;
							using (Mat cropImg = new Mat(curImg, new CvRect(x1, y1, x2 - x1, y2 - y1)))
							{
								if (cropImg.Empty()) continue;
								var segR = _models.HookSlightModel.Predict(cropImg, ConfThreshold);
								if (segR?.Masks == null) continue;
								CvPoint[] inner = null, outer = null;
								for (int m = 0; m < segR.ClassIds.Length; m++)
								{
									var pts = segR.Masks[m].Select(pt => new CvPoint((int)Math.Round(pt.X), (int)Math.Round(pt.Y))).ToArray();
									if (segR.ClassIds[m] == BlueAreaClassId) inner = pts;
									else if (segR.ClassIds[m] == HangHoleClassId) outer = pts;
								}
								if (inner != null && outer != null && inner.Length > 0 && outer.Length > 0)
								{
									var thick = CalcThickness(cropImg.Size(), inner, outer);
									if (thick.Item1 > HookThicknessThreshold)
									{
										// 圆心坐标: 裁剪区域内的局部坐标 + 裁剪原点 → 全局坐标 → 归一化
										float circCxN = (float)(x1 + thick.Item2.X) / imgW;
										float circCyN = (float)(y1 + thick.Item2.Y) / imgH;
										float circRN = (float)(thick.Item1 / 2.0) / imgW;  // 半径归一化
										var def = new BoxDefect(gi, $"轻微挂钩错位 {thick.Item1:F1}px",
											new float[] { (float)bboxN.X, (float)bboxN.Y, (float)(bboxN.X + bboxN.Width), (float)(bboxN.Y + bboxN.Height) },
											score);
										def.CircleInfo = new float[] { circCxN, circCyN, circRN };
										if (!results.ContainsKey(gi)) results[gi] = new List<BoxDefect>();
										results[gi].Add(def);
									}
									else
										Logger.Debug($"[Back] 盒{gi+1}厚度={thick.Item1:F1}px ≤ 阈值{HookThicknessThreshold:F0}, 未判NG");
								}
							}
						}
					}
				}
				int oc = results.Values.SelectMany(v => v).Count(d => d.DefectType == "挂钩明显错位");
				int sc = results.Values.SelectMany(v => v).Count(d => d.DefectType.Contains("轻微挂钩错位"));
				Logger.Info("[Back] 挂钩结果: 明显=" + oc + " 轻微=" + sc);
			}
			catch (Exception ex) { Logger.Error("挂钩异常: " + ex.Message); }
			return results;
		}

		/// <summary>计算挂钩厚度: FillPoly(内外圈)→DistanceTransform→maxVal*2=最大厚度(px)</summary>
		private (double MaxThickness, CvPoint MaxLoc) CalcThickness(CvSize sz, CvPoint[] inner, CvPoint[] outer)
		{
			using (Mat mask = Mat.Zeros(sz, MatType.CV_8UC1))
			{
				Cv2.FillPoly(mask, new[] { outer }, new CvScalar(255));
				Cv2.FillPoly(mask, new[] { inner }, new CvScalar(0));
				using (Mat dist = new Mat())
				{
					Cv2.DistanceTransform(mask, dist, DistanceTypes.L2, DistanceTransformMasks.Precise);
					Cv2.MinMaxLoc(dist, out _, out double maxVal, out _, out CvPoint maxLoc);
					return (maxVal * 2.0, maxLoc);
				}
			}
		}

		/// <summary>添加缺陷到字典: 若key不存在创建List→Add BoxDefect</summary>
		private void AddDefect(Dictionary<int, List<BoxDefect>> dict, int idx, string type, float[] box, float score = 1.0f)
		{
			if (!dict.ContainsKey(idx)) dict[idx] = new List<BoxDefect>();
			dict[idx].Add(new BoxDefect(idx, type, box, score));
		}

		/// <summary>YOLO结果→分盒映射: Boxes→centerX→分盒索引→构建BoxDefect字典</summary>
		private void MapBoxes(YoloInference.YoloResult res, Dictionary<int, List<BoxDefect>> dict, int start, int end, string type)
		{
			if (res == null || res.Boxes == null) return;
			int n = end - start; if (n <= 0) return;
			for (int j = 0; j < res.Boxes.Length; j++)
			{
				var b = res.Boxes[j];
				float score = (res.Scores != null && j < res.Scores.Length) ? res.Scores[j] : 1.0f;
				float cx = (b.X + b.Width / 2f) / res.OrigImg.Width;
				int idx = start + (int)(cx * n);
				if (idx < start || idx >= end) continue;
				if (!dict.ContainsKey(idx)) dict[idx] = new List<BoxDefect>();
				dict[idx].Add(new BoxDefect(idx, type, new float[] { b.X, b.Y, b.X + b.Width, b.Y + b.Height }, score));
			}
		}

		// ====== 绘制 ======
	/// <summary>绘制背面检测结果: 缺陷框(条码绿/橙虚线, 日期码橙, 挂钩暗红/橙红)+分区虚线+盒状态标签(OK绿/NG红)+盒序号(黄)</summary>
		private Bitmap DrawResult(Mat img, List<BoxDefect> defects, List<string> status, int start, int end)
		{
			var bmp = img.ToBitmap();
			using (var g = Graphics.FromImage(bmp))
			{
				g.SmoothingMode = SmoothingMode.AntiAlias;
				int w = bmp.Width, h = bmp.Height, n = end - start;

				foreach (var d in defects)
				{
					var bb = d.BoundingBox; if (bb.Length < 4) continue;
					int x1 = (int)(bb[0] * w), y1 = (int)(bb[1] * h), x2 = (int)(bb[2] * w), y2 = (int)(bb[3] * h);
					if (x2 <= x1 || y2 <= y1) continue;
					var rc = new Rect(x1, y1, x2 - x1, y2 - y1);
					bool isOk = d.DefectType.StartsWith("条码:") || d.DefectType.StartsWith("日期:") || d.DefectType.StartsWith("双排:");
					Color c = isOk ? Color.Lime : Color.Red;
					if (d.DefectType.StartsWith("条码错") || d.DefectType.Contains("条码错误") || d.DefectType.Contains("条码缺少")) c = Color.Orange;
					if (d.DefectType.Contains("日期码错误") || d.DefectType.Contains("日期码不完全") || d.DefectType.Contains("日期码重影")) c = Color.Orange;
					if (d.DefectType.Contains("盒子破损")) c = Color.Red;
					if (d.DefectType.Contains("明显")) c = Color.DarkRed;
					if (d.DefectType.Contains("轻微")) c = Color.OrangeRed;
					bool isBcOrDc = d.DefectType.StartsWith("条码") || d.DefectType.StartsWith("日期码");
					bool borderOnly = isBcOrDc || d.DefectType.Contains("缺少");
					if (!borderOnly) using (var fl = new SolidBrush(Color.FromArgb(80, c))) g.FillRectangle(fl, rc);
					if (d.QuadPoints != null && d.QuadPoints.Length >= 8)
					{
						// 条码四点旋转矩形框(左上→右上→右下→左下按序连线), 同区域多码各自画出自己的位置
						var qp = new System.Drawing.Point[4];
						for (int k = 0; k < 4; k++)
						{
							int qx = (int)Math.Round(d.QuadPoints[k * 2] * w);
							int qy = (int)Math.Round(d.QuadPoints[k * 2 + 1] * h);
							qp[k] = new System.Drawing.Point(Math.Max(0, Math.Min(qx, w - 1)), Math.Max(0, Math.Min(qy, h - 1)));
						}
						using (var pn = new Pen(c, borderOnly ? 4 : 8) { DashStyle = borderOnly ? DashStyle.Dash : DashStyle.Solid }) g.DrawPolygon(pn, qp);
					}
					else
					{
						using (var pn = new Pen(c, borderOnly ? 4 : 8) { DashStyle = borderOnly ? DashStyle.Dash : DashStyle.Solid }) g.DrawRectangle(pn, rc);
					}
					// 轻微挂钩错位: 绘制内切圆
				if (d.DefectType.Contains("轻微挂钩错位") && d.CircleInfo != null && d.CircleInfo.Length >= 3)
				{
					int cxPx = (int)(d.CircleInfo[0] * w);
					int cyPx = (int)(d.CircleInfo[1] * h);
					int rPx = (int)(d.CircleInfo[2] * w);
					using (var circlePen = new Pen(Color.Cyan, 3))
						g.DrawEllipse(circlePen, cxPx - rPx, cyPx - rPx, rPx * 2, rPx * 2);
					// 圆心十字标记
					int cs = 8;
					using (var crossPen = new Pen(Color.Cyan, 2))
					{
						g.DrawLine(crossPen, cxPx - cs, cyPx, cxPx + cs, cyPx);
						g.DrawLine(crossPen, cxPx, cyPx - cs, cxPx, cyPx + cs);
					}
				}

				bool isBarcode = d.DefectType.StartsWith("条码") || d.DefectType.Contains("条码");
					int labelFont = isBarcode ? ((_barcodeParams != null && _barcodeParams.DrawFontBarcode > 0) ? _barcodeParams.DrawFontBarcode : 28)
						: ((_barcodeParams != null && _barcodeParams.DrawFontDefect > 0) ? _barcodeParams.DrawFontDefect : 18);
					using (var f = new Font("微软雅黑", labelFont, FontStyle.Bold))
					{
						string label = d.DefectType;
						bool isDisplayOnly = label.StartsWith("条码:") || label.StartsWith("日期:") || label.StartsWith("双排:");
						bool isHook = label.Contains("挂钩");
						// 挂钩类缺陷不显示模型得分(日志中已有)
						if (!isDisplayOnly && !isHook && d.Score > 0 && d.Score < 1.0f)
							label = label + " " + d.Score.ToString("F2");
						if (label.Length > 30) label = label.Substring(0, 30);
						var sz = g.MeasureString(label, f);
						int ly = y1 - (int)sz.Height - 8; if (ly < 8) ly = y1 + 8;
						using (var bg = new SolidBrush(c)) g.FillRectangle(bg, x1 - 4, ly - 4, sz.Width + 16, sz.Height + 12);
						g.DrawString(label, f, Brushes.White, x1 + 4, ly + 2);
					}
				}

				if (n > 1)
					using (var dp = new Pen(Color.FromArgb(100, 100, 100), 3) { DashStyle = DashStyle.Dash })
						for (int i = 1; i < n; i++) g.DrawLine(dp, i * w / n, 0, i * w / n, h);

				int stFont = (_barcodeParams != null && _barcodeParams.DrawFontStatus > 0) ? _barcodeParams.DrawFontStatus : 48; using (var f = new Font("微软雅黑", stFont, FontStyle.Bold))
					for (int i = 0; i < n && start + i < status.Count; i++)
					{
						string s = status[start + i];
						string disp = s == "OK" ? "OK" : (s.Length > 4 ? s.Substring(0, 4) : s);
						Color c = s == "OK" ? Color.Green : Color.Red;
						float cx = (i + 0.5f) * w / n;
						var sz = g.MeasureString(disp, f);
						using (var br = new SolidBrush(c)) g.DrawString(disp, f, br, cx - sz.Width / 2, 60);

						int boxNum = ReverseBoxOrder ? (status.Count - (start + i)) : (start + i + 1);
						int bxFont = (_barcodeParams != null && _barcodeParams.DrawFontBoxNum > 0) ? _barcodeParams.DrawFontBoxNum : 28; using (var fn2 = new Font("微软雅黑", bxFont, FontStyle.Bold))
						{
							string idxStr = "盒" + boxNum;
							var nsz = g.MeasureString(idxStr, fn2);
							using (var nbr = new SolidBrush(Color.Yellow))
								g.DrawString(idxStr, fn2, nbr, cx - nsz.Width / 2, 120);
						}
					}
			}
			return bmp;
		}

	/// <summary>合并左右渲染图为一张: 左右水平拼接, 黑底+白色分隔线+OK/NG大字(右上角, 半透明黑底)</summary>
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

	/// <summary>保存背面工位图片: 渲染图+左原图+右原图 → JPEG 85% → Images/{日期}/{班次}/背面工位/{OK|NG}/</summary>
		private void SaveImages(Bitmap leftRaw, Bitmap rightRaw, Bitmap merged, long pid, bool isOk, List<string> st)
		{
			bool so = _Config.IsSaveOkImage, sn = _Config.IsSaveNgImage, sor = _Config.IsSaveOkRawImage, snr = _Config.IsSaveNgRawImage;
			if (!so && !sn && !sor && !snr) return;
			string shift = GetShift(), dd = DateTime.Now.ToString("yyMMdd");
			string nt = string.Join("_", st.Where(s => s != "OK").Distinct().DefaultIfEmpty("OK"));
			foreach (var ch in new char[] { ':', '/', '\\', '<', '>', '"', '|', '?', '*', '(', ')', '（', '）' }) nt = nt.Replace(ch, '_');
			string resultDir = isOk ? "OK" : "NG";
			string dir = Path.Combine(_savePath, dd, shift, "背面工位", resultDir); Directory.CreateDirectory(dir);
			string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
			if ((isOk && so) || (!isOk && sn))
				_imageSaver.AddSaveTask(Path.Combine(dir, ts + "_渲染_" + nt + ".jpg"), merged.ToJpegBytesFast(85), true, 85);
			if ((isOk && sor) || (!isOk && snr))
			{
				_imageSaver.AddSaveTask(Path.Combine(dir, ts + "_左原图_" + nt + ".jpg"), leftRaw.ToJpegBytesFast(85), false);
				_imageSaver.AddSaveTask(Path.Combine(dir, ts + "_右原图_" + nt + ".jpg"), rightRaw.ToJpegBytesFast(85), false);
			}
		}

		/// <summary>获取当前班次: 00~08=晚班, 08~16=早班, 16~24=中班</summary>
		private string GetShift()
		{
			var n = DateTime.Now.TimeOfDay;
			if (n >= TimeSpan.Parse("00:00") && n <= TimeSpan.Parse("07:59")) return "晚班";
			if (n >= TimeSpan.Parse("08:00") && n <= TimeSpan.Parse("15:59")) return "早班";
			return "中班";
		}

		public void RestoreCounts(long ok, long ng) { _okCount = ok; _ngCount = ng; _totalCount = ok + ng; }
		public void ClearCounters() { Interlocked.Exchange(ref _totalCount, 0); Interlocked.Exchange(ref _okCount, 0); Interlocked.Exchange(ref _ngCount, 0); }
		public void Dispose()
		{
			if (_disposed) return;
			_disposed = true;
			lock (_syncLock)
			{
				_leftBuffer?.Dispose();
				_rightBuffer?.Dispose();
			}
		}
	}
}
