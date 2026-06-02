using Config;
using Hardware;
using Models;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
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
	/// <summary>拍照模式</summary>
	public enum CaptureMode { FlyCapture = 0, StopCapture = 1 }
	/// <summary>推理模式</summary>
	public enum InferenceMode { PerImage = 0, Batch = 1 }
	/// <summary>IN12边缘→相机映射</summary>
	public enum In12EdgeMap { RisingLeftFallingRight = 0, RisingRightFallingLeft = 1 }

	public class SideStationProcessor : IDisposable
	{
		private readonly AiModelManager _models;
		private readonly string _savePath;
		private SkuData _sku;
		private readonly MotionControlManager _motion;
		private readonly HighSpeedImageSaver _imageSaver;
		private readonly PerformanceMonitor _perfMonitor;

		// 队列
		private readonly ConcurrentQueue<SideImageCtx> _leftQueue = new ConcurrentQueue<SideImageCtx>();
		private readonly ConcurrentQueue<SideImageCtx> _rightQueue = new ConcurrentQueue<SideImageCtx>();
		private int _leftCount, _rightCount;
		private readonly object _countLock = new object();

		// 结果缓存
		private readonly List<SideResult> _leftResults = new List<SideResult>();
		private readonly List<SideResult> _rightResults = new List<SideResult>();
		private readonly List<Mat> _displayImages = new List<Mat>();
		private int _displayIndex;
		private readonly object _resultLock = new object();

		private long _totalCount, _okCount, _ngCount;
		private bool _disposed;
		private CancellationTokenSource _motionCts;

		public event Action<ProductResult> OnResultReady;
		public event Action<List<string>, List<string>, List<string>, int> OnStatusUpdate;
		/// <summary>实时显示事件：每推理一张立即推送渲染图到UI (Side, Bitmap)</summary>
		public event Action<Side, Bitmap> OnRealTimeDisplay;

		// ====== 开放参数 ======
		public float ConfThreshold = 0.5f, IouThreshold = 0.45f;
		public float CropRatio = 2.0f;
		public CaptureMode CaptureMode { get; set; } = CaptureMode.FlyCapture;
		public InferenceMode InferenceMode { get; set; } = InferenceMode.PerImage;
		public In12EdgeMap EdgeMapping { get; set; } = In12EdgeMap.RisingLeftFallingRight;
	// 兼容旧接口
		public TriggerEdgeMode EdgeMode { get { return (TriggerEdgeMode)(int)EdgeMapping; } set { EdgeMapping = (In12EdgeMap)(int)value; } }
		public bool UseContinuousMode { get { return CaptureMode == CaptureMode.StopCapture; } set { CaptureMode = value ? CaptureMode.StopCapture : CaptureMode.FlyCapture; } }
		public int CurrentIndex => _displayIndex;
		public Bitmap GetCurrentDisplayImage() { var m = GetDisplayImage(); if (m == null) return null; var bmp = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(m); m.Dispose(); return bmp; }

		public enum TriggerEdgeMode { RisingLeftFallingRight = 0, RisingRightFallingLeft = 1 }
		public bool MissingAsNg { get; set; } = true;
		public bool ReverseBoxOrder { get; set; }
		public int SideAxis { get; set; } = 0;
		public float StartPosition { get; set; } = 0f;
		public float EndPosition { get; set; } = 100f;
		public float ForwardSpeed { get; set; } = 50f;
		public float ReturnSpeed { get; set; } = 200f;
		public float Accel { get; set; } = 5000f;
		public float Decel { get; set; } = 5000f;
	public int FwdInPort { get; set; } = 14;
		public int RevInPort { get; set; } = 15;
		public int DatumInPort { get; set; } = 16;
		public int In12Port { get; set; } = 12;
		public int Cam7OutPort { get; set; } = 14;
		public int Cam8OutPort { get; set; } = 15;
		public int TriggerPulseMs { get; set; } = 50;

		public long TotalCount => _totalCount; public long OkCount => _okCount; public long NgCount => _ngCount;
		public long TriggerCount, OutLeftCount, OutRightCount, ImgLeftCount, ImgRightCount;
		public int ImageCount => _displayImages.Count;
		public bool IsMoving { get; private set; }

		public SideStationProcessor(AiModelManager models, string savePath, SkuData sku,
			MotionControlManager motion, HighSpeedImageSaver imageSaver, PerformanceMonitor perfMonitor)
		{ _models = models; _savePath = savePath; _sku = sku; _motion = motion; _imageSaver = imageSaver; _perfMonitor = perfMonitor; }

		public void UpdateSku(SkuData sku) { _sku = sku; }

		public void OnCam7(Bitmap bmp, long pid) { if (bmp != null) AddImage(_leftQueue, ref _leftCount, bmp, pid, Side.Left); }
		public void OnCam8(Bitmap bmp, long pid) { if (bmp != null) AddImage(_rightQueue, ref _rightCount, bmp, pid, Side.Right); }

		private void AddImage(ConcurrentQueue<SideImageCtx> q, ref int count, Bitmap bmp, long pid, Side side)
		{
			q.Enqueue(new SideImageCtx { Image = bmp, ProductId = pid, Side = side });
			Interlocked.Increment(ref count);
		}

		public void Start() { Logger.Info("侧面工位已启动"); }
		public void Stop() { _motionCts?.Cancel(); }

		/// <summary>模拟检测：跳过运动轴，直接处理队列图片</summary>
		public void SimulateDetection()
		{
			if (IsMoving) return;
			IsMoving = true;
			int originalP = _sku.P;
			int simP = Math.Max(_leftCount, _rightCount);
			if (simP > 0) _sku.P = simP;
			_motionCts = new CancellationTokenSource();
			Task.Run(() =>
			{
				try
				{
					Logger.Info("[Side] ====== 侧面模拟检测开始 P=" + _sku.P + " L=" + _leftCount + " R=" + _rightCount + " ======");
					ProcessResults();
				}
				catch (Exception ex) { Logger.Error("[Side] 模拟异常: " + ex.Message); }
				finally { _sku.P = originalP; IsMoving = false; }
			});
		}

		/// <summary>IN13下降沿触发 → MainFrm调用此方法</summary>
		public void StartDetection()
		{
			if (_disposed) { Logger.Warning("[Side] 已释放，忽略StartDetection"); return; }
			if (IsMoving) { Logger.Warning("[Side] 上一批未完成，强制中断回起点"); _motionCts?.Cancel(); _motion.StopAxis(SideAxis); _motion.SetSpeed(SideAxis, ReturnSpeed); _motion.MoveAbs(SideAxis, StartPosition); }
			IsMoving = true;
		SetLimitSwitches();
			_motionCts = new CancellationTokenSource();
			var cts = _motionCts; // 捕获当前令牌，防止被后续周期覆盖
			Task.Run(() =>
			{
				try
				{
					Logger.Info("[Side] ====== 侧面检测开始 P=" + _sku.P + " ======");
					ClearBatch();
					lock (_resultLock) { _leftResults.Clear(); _rightResults.Clear(); }
					var streamTask = Task.Run(() => ProcessStream(cts.Token));
					StartMotion();
					// 运动结束，取消ProcessStream线程，防止废弃线程积累导致CPU饿死
					cts.Cancel();
					streamTask.Wait(3000);
					FinalizeResults();
				}
				catch (Exception ex) { Logger.Error("[Side] 异常: " + ex.Message); }
				finally { IsMoving = false; cts.Dispose(); }
			});
		}

		/// <summary>运动控制: 从起始位走到结束位，监听IN12控制拍照</summary>
		private void StartMotion()
		{
			int p = _sku.P;
			Logger.Info("[Side] 运动开始: 轴" + SideAxis + " " + StartPosition + "→" + EndPosition + " 前进速度=" + ForwardSpeed + " 回程速度=" + ReturnSpeed);

			// 先到起始位
			_motion.MoveAbs(SideAxis, StartPosition);
			_motion.WaitForMoveComplete(SideAxis, 10000);

			// 设置前进速度
			SetAxisSpeed(ForwardSpeed);

			// 开始向结束位移动
			_motion.MoveAbs(SideAxis, EndPosition);

			// ── 独立脉冲线程（与CameraTriggerManager设计一致，避免阻塞IN12监听）──
			var pulseQueue = new BlockingCollection<(int port, int ms)>(100);
			var pulseCts = CancellationTokenSource.CreateLinkedTokenSource(_motionCts.Token);
			var pulseTask = Task.Run(() =>
			{
				try
				{
					foreach (var (port, ms) in pulseQueue.GetConsumingEnumerable(pulseCts.Token))
						_motion.HwPulse(port, ms);
				}
				catch (OperationCanceledException) { }
				catch (Exception ex) { Logger.Error("[Side] 脉冲线程异常: " + ex.Message); }
			});

			bool prevIn12 = false;
			bool firstRead = true;
			var sw = Stopwatch.StartNew();
			int posCheckCounter = 0;

			while (!_motionCts.Token.IsCancellationRequested)
			{
				// 检查是否已收够图片
				if (_leftCount >= p && _rightCount >= p) { Logger.Info("[Side] 图片已收够, 提前返回"); break; }

				// 检查是否已到结束位（每20次循环读一次，减少ZMC竞争）
				if (++posCheckCounter >= 20)
				{
					posCheckCounter = 0;
					float curPos = _motion.GetPosition(SideAxis);
					if (Math.Abs(curPos - EndPosition) < 0.5f || sw.ElapsedMilliseconds > 60000)
					{ Logger.Info("[Side] 到达结束位 pos=" + curPos.ToString("F1")); break; }
				}

				// 读取IN12（从MonitorLoop的GetInMulti快照读取，避免ZMC竞争）
				bool curIn12 = (Hardware.CameraTriggerManager.LastInBits & (1 << (In12Port - 4))) != 0;
				// 边沿检测
				if (firstRead) { prevIn12 = curIn12; firstRead = false; continue; }
				if (curIn12 == prevIn12) { Thread.Sleep(2); continue; }

				// 上升沿/下降沿触发拍照
				int trigCam;
				if (curIn12 && !prevIn12) // 上升沿
					trigCam = (EdgeMapping == In12EdgeMap.RisingLeftFallingRight) ? 7 : 8;
				else // 下降沿
					trigCam = (EdgeMapping == In12EdgeMap.RisingLeftFallingRight) ? 8 : 7;

				prevIn12 = curIn12;

				if (CaptureMode == CaptureMode.StopCapture)
				{
					_motion.StopAxis(SideAxis);
					_motion.WaitForMoveComplete(SideAxis, 2000);
				}

				// 发送触发脉冲到独立线程（非阻塞，即刻返回继续监听IN12）
				int outPort = (trigCam == 7) ? Cam7OutPort : Cam8OutPort;
				if (!pulseQueue.TryAdd((outPort, TriggerPulseMs)))
					Logger.Warning("[Side] 脉冲队列已满，丢弃Cam" + trigCam + "触发");
				Logger.Debug("[Side] IN12边沿 触发Cam" + trigCam + " pos=" + curPos.ToString("F1") + " L=" + _leftCount + "/" + p + " R=" + _rightCount + "/" + p);

				if (CaptureMode == CaptureMode.StopCapture)
				{
					_motion.MoveAbs(SideAxis, EndPosition);
					SetAxisSpeed(ForwardSpeed);
				}
			}

			// 脉冲线程收尾
			pulseQueue.CompleteAdding();
			pulseCts.Cancel();
			try { pulseTask.Wait(5000); } catch { }
			pulseCts.Dispose();
			pulseQueue.Dispose();

			// 停止并返回起始位
			_motion.StopAxis(SideAxis);
			_motion.WaitForMoveComplete(SideAxis, 2000);
			Logger.Info("[Side] 返回起始位 L=" + _leftCount + " R=" + _rightCount);
			SetAxisSpeed(ReturnSpeed);
			_motion.MoveAbs(SideAxis, StartPosition);
			_motion.WaitForMoveComplete(SideAxis, 10000);
		}

	private void SetLimitSwitches() { if (_motion.IsConnected) { try { _motion.SetLimitIn(SideAxis, FwdInPort, RevInPort, DatumInPort); Logger.Info("[Side] 限位已设置: FWD=IN" + FwdInPort + " REV=IN" + RevInPort + " DATUM=IN" + DatumInPort); } catch (Exception ex) { Logger.Warning("[Side] 限位设置失败: " + ex.Message); } } }
		private void SetAxisSpeed(float speed) { _motion.SetSpeed(SideAxis, speed); _motion.SetAccel(SideAxis, Accel); _motion.SetDecel(SideAxis, Decel); }

		/// <summary>实时流式处理：运动过程中拍一张推理一张显示一张</summary>
		private List<SideImageCtx> _processedLeftImgs = new List<SideImageCtx>();
		private List<SideImageCtx> _processedRightImgs = new List<SideImageCtx>();

		private void ProcessStream(CancellationToken cancel)
		{
			int p = _sku.P, processed = 0;
			_processedLeftImgs.Clear(); _processedRightImgs.Clear();
			while (!cancel.IsCancellationRequested && processed < p * 2)
			{
				SideImageCtx ctx;
				if (_leftQueue.TryDequeue(out ctx))
				{
					var res = InferSingle(ctx, _leftResults.Count);
					lock (_resultLock) _leftResults.Add(res);
					_processedLeftImgs.Add(ctx);
					EmitPartial(p);
					processed++;
				}
				else if (_rightQueue.TryDequeue(out ctx))
				{
					var res = InferSingle(ctx, _rightResults.Count);
					lock (_resultLock) _rightResults.Add(res);
					_processedRightImgs.Add(ctx);
					EmitPartial(p);
					processed++;
				}
				else Thread.Sleep(1);
			}
		}

		private void EmitPartial(int p)
		{
			lock (_resultLock)
			{
				var st = new List<string>();
				int n = Math.Max(_leftResults.Count, _rightResults.Count);
				for (int i = 0; i < n; i++)
				{
					string ls = i < _leftResults.Count ? _leftResults[i].Status : "?";
					string rs = i < _rightResults.Count ? _rightResults[i].Status : "?";
					st.Add((ls == "OK" && rs == "OK") ? "OK" : (ls != "OK" && ls != "?" ? ls : (rs != "?" ? rs : "OK")));
				}
				OnStatusUpdate?.Invoke(new List<string>(), new List<string>(), st, p);
			}
		}

		private void FinalizeResults()
		{
			int p = _sku.P;
			while (_leftResults.Count < p) _leftResults.Add(new SideResult { Status = MissingAsNg ? "缺少" : "OK", Side = Side.Left, Index = _leftResults.Count });
			while (_rightResults.Count < p) _rightResults.Add(new SideResult { Status = MissingAsNg ? "缺少" : "OK", Side = Side.Right, Index = _rightResults.Count });
			var mergedStatus = new List<string>();
			for (int i = 0; i < p; i++)
			{
				string ls = i < _leftResults.Count ? _leftResults[i].Status : (MissingAsNg ? "缺少" : "OK");
				string rs = i < _rightResults.Count ? _rightResults[i].Status : (MissingAsNg ? "缺少" : "OK");
				mergedStatus.Add((ls == "OK" && rs == "OK") ? "OK" : (ls != "OK" ? ls : rs));
			}
			bool isOk = mergedStatus.All(s => s == "OK");
			Interlocked.Increment(ref _totalCount);
			if (isOk) Interlocked.Add(ref _okCount, p); else { Interlocked.Add(ref _okCount, p - mergedStatus.Count(s => s != "OK")); Interlocked.Add(ref _ngCount, mergedStatus.Count(s => s != "OK")); }
			var result = new ProductResult { ProductId = DateTime.Now.Ticks, CreateTime = DateTime.Now, SideResult = isOk, SideDefects = mergedStatus.Where(s => s != "OK").Distinct().ToList() };
			// 生成轮播显示图
				BuildDisplayImages(_processedLeftImgs, _processedRightImgs, p);
				// 左右分别渲染显示图（xlPictureBox5=左侧面, xlPictureBox6=右侧面）
				Bitmap leftRender = null, rightRender = null;
				lock (_resultLock)
				{
					if (_processedLeftImgs.Count > 0)
						leftRender = RenderSideImage(_processedLeftImgs[0], 0, p, _leftResults);
					if (_processedRightImgs.Count > 0)
						rightRender = RenderSideImage(_processedRightImgs[0], 0, p, _rightResults);
				}
				result.SideRenderImage = leftRender;
				result.SideLeftRenderImage = leftRender;
				result.SideRightRenderImage = rightRender;
				SaveImages(_processedLeftImgs, _processedRightImgs, mergedStatus, isOk);
			Logger.Info("[Side] 完成 P=" + p + " 结果=" + (isOk ? "OK" : "NG"));
			OnResultReady?.Invoke(result);
			OnStatusUpdate?.Invoke(new List<string>(), new List<string>(), mergedStatus, p);
		}

		/// <summary>处理推理结果（模拟检测使用）</summary>
		private void ProcessResults()
		{
			int p = _sku.P;
			var leftImages = new List<SideImageCtx>();
			var rightImages = new List<SideImageCtx>();
			while (leftImages.Count < _leftCount && _leftQueue.TryDequeue(out var ctx)) leftImages.Add(ctx);
			while (rightImages.Count < _rightCount && _rightQueue.TryDequeue(out var ctx)) rightImages.Add(ctx);
			_leftCount = 0; _rightCount = 0;

			Logger.Info("[Side] 推理开始 L=" + leftImages.Count + " R=" + rightImages.Count + " 模式=" + InferenceMode);

			var sw = Stopwatch.StartNew();
			if (InferenceMode == InferenceMode.Batch)
				ProcessBatchInference(leftImages, rightImages, p);
			else
				ProcessPerImageInference(leftImages, rightImages, p);

			var inferMs = sw.Elapsed.TotalMilliseconds;

			// 填充缺失结果
			while (_leftResults.Count < p) _leftResults.Add(new SideResult { Status = MissingAsNg ? "缺少" : "OK", Side = Side.Left, Index = _leftResults.Count });
			while (_rightResults.Count < p) _rightResults.Add(new SideResult { Status = MissingAsNg ? "缺少" : "OK", Side = Side.Right, Index = _rightResults.Count });

			// 汇总
			var mergedStatus = new List<string>();
			for (int i = 0; i < p; i++)
			{
				string ls = i < _leftResults.Count ? _leftResults[i].Status : (MissingAsNg ? "缺少" : "OK");
				string rs = i < _rightResults.Count ? _rightResults[i].Status : (MissingAsNg ? "缺少" : "OK");
				mergedStatus.Add((ls == "OK" && rs == "OK") ? "OK" : (ls != "OK" ? ls : rs));
			}
			bool isOk = mergedStatus.All(s => s == "OK");
			Interlocked.Increment(ref _totalCount);
			if (isOk) Interlocked.Add(ref _okCount, p); else { Interlocked.Add(ref _okCount, p - mergedStatus.Count(s => s != "OK")); Interlocked.Add(ref _ngCount, mergedStatus.Count(s => s != "OK")); }

			// 绘制显示图
			BuildDisplayImages(leftImages, rightImages, p);

			Bitmap leftRender = null, rightRender = null;
				lock (_resultLock) { if (_displayImages.Count > 0) leftRender = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(_displayImages[0]); }
				var result = new ProductResult { ProductId = DateTime.Now.Ticks, CreateTime = DateTime.Now, SideResult = isOk, SideDefects = mergedStatus.Where(s => s != "OK").Distinct().ToList(), SideRenderImage = leftRender, SideLeftRenderImage = leftRender, SideRightRenderImage = rightRender };

			// 存图
			var swSave = Stopwatch.StartNew();
			SaveImages(leftImages, rightImages, mergedStatus, isOk);
			var saveMs = swSave.Elapsed.TotalMilliseconds;

			Logger.Info("[Side] 完成 总耗时=" + sw.Elapsed.TotalMilliseconds.ToString("F0") + "ms 推理=" + inferMs.ToString("F0") + "ms 保存=" + saveMs.ToString("F0") + "ms 结果=" + (isOk ? "OK" : "NG"));
			for (int i = 0; i < mergedStatus.Count; i++) Logger.Info("[Side]   盒" + (i + 1) + ": " + mergedStatus[i]);

			OnResultReady?.Invoke(result);
			OnStatusUpdate?.Invoke(new List<string>(), new List<string>(), mergedStatus, p);
		}

		private void ProcessPerImageInference(List<SideImageCtx> leftImages, List<SideImageCtx> rightImages, int p)
		{
			// 左图逐张推理
			for (int i = 0; i < leftImages.Count; i++)
			{
				var res = InferSingle(leftImages[i], i);
				lock (_resultLock) _leftResults.Add(res);
			}
			// 右图逐张推理
			for (int i = 0; i < rightImages.Count; i++)
			{
				var res = InferSingle(rightImages[i], i);
				lock (_resultLock) _rightResults.Add(res);
			}
		}

		private void ProcessBatchInference(List<SideImageCtx> leftImages, List<SideImageCtx> rightImages, int p)
		{
			var allCrops = new List<Mat>();
			var allMeta = new List<Tuple<Side, int, int>>(); // Side, imgIdx, isTail(0=head,1=tail)
			foreach (var img in leftImages)
			{
				try { var m = img.Image.ToMat(); int h = m.Height, w = m.Width; int cw = (int)(h * CropRatio); if (cw > w) cw = w;
					var hd = new Mat(m, new CvRect(0, 0, cw, h)).Clone(); allCrops.Add(hd); allMeta.Add(Tuple.Create(Side.Left, allCrops.Count - 1, 0));
					var tl = new Mat(m, new CvRect(w - cw, 0, cw, h)).Clone(); allCrops.Add(tl); allMeta.Add(Tuple.Create(Side.Left, allCrops.Count - 1, 1));
					m.Dispose(); } catch { }
			}
			foreach (var img in rightImages)
			{
				try { var m = img.Image.ToMat(); int h = m.Height, w = m.Width; int cw = (int)(h * CropRatio); if (cw > w) cw = w;
					var hd = new Mat(m, new CvRect(0, 0, cw, h)).Clone(); allCrops.Add(hd); allMeta.Add(Tuple.Create(Side.Right, allCrops.Count - 1, 0));
					var tl = new Mat(m, new CvRect(w - cw, 0, cw, h)).Clone(); allCrops.Add(tl); allMeta.Add(Tuple.Create(Side.Right, allCrops.Count - 1, 1));
					m.Dispose(); } catch { }
			}

			if (_models.SideDefectModel != null && allCrops.Count > 0)
			{
				var results = _models.SideDefectModel.PredictBatch(allCrops, ConfThreshold, IouThreshold);
				var imgDefects = new Dictionary<int, List<BoxDefect>>();
				for (int i = 0; i < results.Count; i++)
				{
					var r = results[i]; var meta = allMeta[i];
					int imgIdx = meta.Item2 / 2, isTail = meta.Item3; Side side = meta.Item1;
					if (r?.BoxesN == null) continue;
					int key = (side == Side.Left ? 0 : 100) + imgIdx;
					if (!imgDefects.ContainsKey(key)) imgDefects[key] = new List<BoxDefect>();
					int srcW = 0, srcH = 0; // need original image dimensions for tail offset
					var srcImgs = side == Side.Left ? leftImages : rightImages;
					if (imgIdx < srcImgs.Count) { var tmp = srcImgs[imgIdx]; srcW = tmp.Image.Width; srcH = tmp.Image.Height; }
					int tailOff = isTail == 1 ? (srcW - (int)(srcH * CropRatio)) : 0;
					for (int j = 0; j < r.BoxesN.Length; j++)
					{ var b = r.BoxesN[j]; imgDefects[key].Add(new BoxDefect(j, "缺陷" + r.ClassIds[j], new float[] { (tailOff + b.X) / srcW, b.Y / srcH, (tailOff + b.X + b.Width) / srcW, (b.Y + b.Height) / srcH }, r.Scores[j])); }
				}
				foreach (var kv in imgDefects)
				{
					int srcIdx = kv.Key % 100; Side side = kv.Key >= 100 ? Side.Right : Side.Left;
					var sr = new SideResult { Index = srcIdx, Side = side, Defects = kv.Value, Status = kv.Value.Count > 0 ? "NG" : "OK" };
					if (side == Side.Left) _leftResults.Add(sr); else _rightResults.Add(sr);
				}
			}
			foreach (var m in allCrops) m.Dispose();
		}

		private SideResult InferSingle(SideImageCtx ctx, int idx)
		{
			var result = new SideResult { Index = idx, Side = ctx.Side, Status = "OK" };
			try
			{
				using (var mat = ctx.Image.ToMat())
				{
					int h = mat.Height, w = mat.Width;
					int cropW = (int)(h * CropRatio); if (cropW > w) cropW = w;
					// 参考程序: 头+尾裁剪再批量推理
					using (var head = new Mat(mat, new CvRect(0, 0, cropW, h)).Clone())
					using (var tail = new Mat(mat, new CvRect(w - cropW, 0, cropW, h)).Clone())
					{
						if (_models.SideDefectModel != null)
						{
							var batch = new List<Mat> { head, tail };
							var batchResults = _models.SideDefectModel.PredictBatch(batch, ConfThreshold, IouThreshold);
							bool ng = false;
							// 头部检测：坐标保持原样(0~cropW)
							if (batchResults != null && batchResults.Count > 0 && batchResults[0]?.BoxesN != null)
							{
								ng = true;
								for (int j = 0; j < batchResults[0].BoxesN.Length; j++)
								{ var b = batchResults[0].BoxesN[j]; result.Defects.Add(new BoxDefect(j, "缺陷" + batchResults[0].ClassIds[j], new float[] { b.X / w, b.Y / h, (b.X + b.Width) / w, (b.Y + b.Height) / h }, batchResults[0].Scores[j])); }
							}
							// 尾部检测：坐标映射回原图(width-cropW ~ width)
							if (batchResults != null && batchResults.Count > 1 && batchResults[1]?.BoxesN != null)
							{
								ng = true;
								for (int j = 0; j < batchResults[1].BoxesN.Length; j++)
								{ var b = batchResults[1].BoxesN[j]; result.Defects.Add(new BoxDefect(j, "缺陷" + batchResults[1].ClassIds[j], new float[] { (w - cropW + b.X) / w, b.Y / h, (w - cropW + b.X + b.Width) / w, (b.Y + b.Height) / h }, batchResults[1].Scores[j])); }
							}
							if (ng) result.Status = "NG";
						}
					}
				}
			}
			catch (Exception ex) { result.Status = "错误"; Logger.Error("[Side] 推理异常: " + ex.Message); }
			return result;
		}

		private void BuildDisplayImages(List<SideImageCtx> leftImages, List<SideImageCtx> rightImages, int p)
		{
			lock (_resultLock)
			{
				_displayImages.Clear();
				int count = Math.Max(leftImages.Count, rightImages.Count);
				for (int i = 0; i < count; i++)
				{
					SideImageCtx img = i < leftImages.Count ? leftImages[i] : (i < rightImages.Count ? rightImages[i] : null);
					Bitmap bmp;
					if (img != null)
					{
						var results = img.Side == Side.Left ? _leftResults : _rightResults;
						bmp = RenderSideImage(img, i, count, results);
					}
					else
					{
						bmp = new Bitmap(800, 600);
						using (var g = Graphics.FromImage(bmp))
						{
							g.Clear(Color.FromArgb(30, 30, 30));
							using (var f = new Font("微软雅黑", Math.Max(48f, bmp.Height / 30f), FontStyle.Bold))
							{ g.DrawString((i + 1) + "/" + count + " 缺少", f, Brushes.Gray, 50, bmp.Height / 2 - 30); }
						}
					}
					var mat = OpenCvSharp.Extensions.BitmapConverter.ToMat(bmp);
					bmp.Dispose();
					_displayImages.Add(mat);
				}
				_displayIndex = _displayImages.Count > 0 ? 0 : -1;
			}
		}

		/// <summary>渲染单张侧面图（半透明裁图区+边界线+缺陷框+状态+序号），参考侧面调试工具风格</summary>
		private Bitmap RenderSideImage(SideImageCtx ctx, int index, int total, List<SideResult> results)
		{
			try
			{
				using (var src = ctx.Image.ToMat())
				{
					var drawImg = src.Clone();
					int w = drawImg.Width, h = drawImg.Height;
					int cropW = (int)(h * CropRatio);
					if (cropW > w) cropW = w;

					// ── 1. 裁图区域半透明覆盖 (OpenCV) ──
					// 头部区域 (左侧, 淡蓝色半透明)
					using (var overlay = new Mat(drawImg.Size(), MatType.CV_8UC3, new Scalar(0, 0, 0)))
					{
						Cv2.Rectangle(overlay, new OpenCvSharp.Rect(0, 0, cropW, h), new Scalar(255, 140, 0), -1);
						Cv2.AddWeighted(drawImg, 0.85, overlay, 0.15, 0, drawImg);
					}
					// 尾部区域 (右侧, 淡蓝色半透明)
					using (var overlay = new Mat(drawImg.Size(), MatType.CV_8UC3, new Scalar(0, 0, 0)))
					{
						Cv2.Rectangle(overlay, new OpenCvSharp.Rect(w - cropW, 0, cropW, h), new Scalar(255, 140, 0), -1);
						Cv2.AddWeighted(drawImg, 0.85, overlay, 0.15, 0, drawImg);
					}
					// 裁图区边界实线 (橙色)
					Cv2.Line(drawImg, new OpenCvSharp.Point(cropW, 0), new OpenCvSharp.Point(cropW, h), new Scalar(0, 165, 255), 2);
					Cv2.Line(drawImg, new OpenCvSharp.Point(w - cropW, 0), new OpenCvSharp.Point(w - cropW, h), new Scalar(0, 165, 255), 2);

					// ── 2. 转Bitmap，GDI+绘制文字 ──
					var bmp = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(drawImg);
					drawImg.Dispose();
					using (var g = Graphics.FromImage(bmp))
					{
						g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
						float penW = Math.Max(3f, h / 400f);
						float fontSz = Math.Max(20f, h / 60f);
						float bigFontSz = Math.Max(48f, h / 30f);

						// ── 缺陷框 (红色填充+边框+标签) ──
						var res = results.FirstOrDefault(r => r.Index == index);
						var defs = res?.Defects ?? new List<BoxDefect>();
						foreach (var d in defs)
						{
							if (d.BoundingBox == null || d.BoundingBox.Length < 4) continue;
							int x1 = (int)(d.BoundingBox[0] * w), y1 = (int)(d.BoundingBox[1] * h);
							int x2 = (int)(d.BoundingBox[2] * w), y2 = (int)(d.BoundingBox[3] * h);
							if (x2 <= x1 || y2 <= y1) continue;
							var rc = new Rectangle(x1, y1, x2 - x1, y2 - y1);
							using (var fl = new SolidBrush(Color.FromArgb(60, 255, 0, 0))) g.FillRectangle(fl, rc);
							using (var pn = new Pen(Color.Red, penW)) g.DrawRectangle(pn, rc);
							using (var df = new Font("微软雅黑", fontSz, FontStyle.Bold))
							{
								string dt = string.IsNullOrEmpty(d.DefectType) ? "缺陷" : d.DefectType;
								var dsz = g.MeasureString(dt, df);
								int dy = y1 - (int)dsz.Height - 6; if (dy < 4) dy = y1 + 4;
								using (var dbg = new SolidBrush(Color.Red)) g.FillRectangle(dbg, x1, dy, dsz.Width + 8, dsz.Height + 6);
								g.DrawString(dt, df, Brushes.White, x1 + 3, dy + 2);
							}
						}

						// ── 状态标签 (顶部居中: OK=绿, NG=红) ──
						string status = res?.Status ?? "?";
						Color stColor = status == "OK" ? Color.LimeGreen : (status == "?" ? Color.Gray : Color.Red);
						using (var sf = new Font("微软雅黑", bigFontSz, FontStyle.Bold))
						{
							var stSz = g.MeasureString(status, sf);
							int stX = w / 2 - (int)stSz.Width / 2;
							using (var stBg = new SolidBrush(Color.FromArgb(180, Color.Black)))
								g.FillRectangle(stBg, stX - 8, 4, stSz.Width + 16, stSz.Height + 8);
							using (var stBr = new SolidBrush(stColor))
								g.DrawString(status, sf, stBr, stX, 8);
						}

						// ── 序号 (右下角, 青色) ──
						string label = (index + 1) + "/" + total;
						using (var f = new Font("微软雅黑", bigFontSz, FontStyle.Bold))
						{
							var sz = g.MeasureString(label, f);
							using (var bg = new SolidBrush(Color.FromArgb(180, Color.Black)))
								g.FillRectangle(bg, w - (int)sz.Width - 16, h - (int)sz.Height - 12, sz.Width + 12, sz.Height + 10);
							g.DrawString(label, f, Brushes.Cyan, w - (int)sz.Width - 10, h - (int)sz.Height - 8);
						}

						// ── 图例 (右下角, 小字) ──
						DrawSmallLegend(g, w, h, fontSz);
					}
					return bmp;
				}
			}
			catch (Exception ex) { Logger.Error("[Side] 渲染异常: " + ex.Message); return null; }
		}

		private static void DrawSmallLegend(Graphics g, int imgW, int imgH, float fontSz)
		{
			var items = new[] {
				("裁剪区域", Color.Orange),
				("缺陷框", Color.Red),
			};
			int n = items.Length, pad = 6, itemH = (int)(fontSz * 1.3f);
			int legendW = (int)(fontSz * 6f), legendH = n * itemH + pad * 2 + (int)(fontSz * 1.2f);
			int lx = imgW - legendW - 10, ly = imgH - legendH - 60;

			using (var bgBrush = new SolidBrush(Color.FromArgb(200, 40, 40, 40)))
				g.FillRectangle(bgBrush, lx, ly, legendW, legendH);
			using (var pen = new Pen(Color.Gray, 1))
				g.DrawRectangle(pen, lx, ly, legendW, legendH);

			using (var titleFont = new Font("微软雅黑", fontSz, FontStyle.Bold))
			using (var itemFont = new Font("微软雅黑", fontSz * 0.75f, FontStyle.Regular))
			{
				var titleSz = g.MeasureString("图例", titleFont);
				g.DrawString("图例", titleFont, Brushes.White, lx + (legendW - titleSz.Width) / 2, ly + pad);
				for (int i = 0; i < n; i++)
				{
					int iy = ly + pad + (int)(fontSz * 1.2f) + i * itemH;
					using (var brush = new SolidBrush(items[i].Item2))
						g.FillRectangle(brush, lx + 8, iy + 2, (int)(fontSz * 0.9f), (int)(fontSz * 0.7f));
					g.DrawString(items[i].Item1, itemFont, Brushes.White, lx + (int)(fontSz * 1.6f), iy);
				}
			}
		}

		// ====== 轮播导航 ======
		public Mat GetDisplayImage()
		{
			lock (_resultLock) { if (_displayImages.Count > 0 && _displayIndex >= 0 && _displayIndex < _displayImages.Count) return _displayImages[_displayIndex].Clone(); return null; }
		}
		public void NavigatePrev() { lock (_resultLock) { if (_displayImages.Count > 0) _displayIndex = (_displayIndex - 1 + _displayImages.Count) % _displayImages.Count; } }
		public void NavigateNext() { lock (_resultLock) { if (_displayImages.Count > 0) _displayIndex = (_displayIndex + 1) % _displayImages.Count; } }
		public Mat GetCurrentLeftImage() { lock (_resultLock) { if (_displayImages.Count > 0 && _displayIndex >= 0 && _displayIndex < _displayImages.Count) return _displayImages[_displayIndex].Clone(); return null; } }
		public Mat GetCurrentRightImage() { lock (_resultLock) { if (_displayImages.Count > 0 && _displayIndex >= 0 && _displayIndex < _displayImages.Count) return _displayImages[_displayIndex].Clone(); return null; } }

		private void SaveImages(List<SideImageCtx> leftImages, List<SideImageCtx> rightImages, List<string> status, bool isOk)
		{
			try
			{
				if (leftImages == null || rightImages == null) return;
				bool so = _Config.IsSaveOkImage, sn = _Config.IsSaveNgImage; if (!so && !sn) return;
				string sh = GetShift(), dd = DateTime.Now.ToString("yyMMdd"), dir = Path.Combine(_savePath, dd, sh, "侧面工位", isOk ? "OK" : "NG");
				Directory.CreateDirectory(dir);
				long pid = DateTime.Now.Ticks; string nt = string.Join("_", status.Where(s => s != "OK").Distinct().DefaultIfEmpty("OK"));
				for (int i = 0; i < leftImages.Count; i++) { if (leftImages[i].Image != null) _imageSaver.AddSaveTask(Path.Combine(dir, pid + "_L" + (i + 1) + "_" + nt + ".jpg"), leftImages[i].Image.ToJpegBytesFast(85), true, 85); }
				for (int i = 0; i < rightImages.Count; i++) { if (rightImages[i].Image != null) _imageSaver.AddSaveTask(Path.Combine(dir, pid + "_R" + (i + 1) + "_" + nt + ".jpg"), rightImages[i].Image.ToJpegBytesFast(85), true, 85); }
			}
			catch (Exception ex) { Logger.Error("[Side] 存图异常: " + ex.Message); }
		}
		private string GetShift() { var n = DateTime.Now.TimeOfDay; if (n >= TimeSpan.Parse("00:00") && n <= TimeSpan.Parse("07:59")) return "晚班"; if (n >= TimeSpan.Parse("08:00") && n <= TimeSpan.Parse("15:59")) return "早班"; return "中班"; }

		private void ClearBatch() { lock (_countLock) { while (_leftQueue.TryDequeue(out _)) ; while (_rightQueue.TryDequeue(out _)) ; _leftCount = 0; _rightCount = 0; } _leftResults.Clear(); _rightResults.Clear(); }

		public void ClearCounters() { Interlocked.Exchange(ref _totalCount, 0); Interlocked.Exchange(ref _okCount, 0); Interlocked.Exchange(ref _ngCount, 0); }
		public void Dispose() { if (_disposed) return; _disposed = true; _motionCts?.Cancel(); }
	}

	public enum Side { Left, Right }
	internal class SideImageCtx { public Bitmap Image; public long ProductId; public Side Side; }
	internal class SideResult { public int Index; public Side Side; public string Status = "OK"; public List<BoxDefect> Defects = new List<BoxDefect>(); }
}
