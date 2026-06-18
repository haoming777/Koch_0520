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
	/// <summary>拍照模式: FlyCapture=飞拍(运动中拍照), StopCapture=停拍(到位后拍照)</summary>
	public enum CaptureMode { FlyCapture = 0, StopCapture = 1 }
	/// <summary>推理模式: PerImage=逐张推理, Batch=批量推理</summary>
	public enum InferenceMode { PerImage = 0, Batch = 1 }
	/// <summary>IN12边缘→相机映射: RisingLeftFallingRight=↑→左↓→右, RisingRightFallingLeft=↑→右↓→左</summary>
	public enum In12EdgeMap { RisingLeftFallingRight = 0, RisingRightFallingLeft = 1 }

	/// <summary>
	/// 侧面工位处理器. 负责运动轴控制(起点-终点往复)、实时推理(运动中每图即刻YOLO)、安全锁双层防护(硬件+软件).
	/// 触发流程: IN13下降沿 -> MainFrm.OnCameraTriggered -> StartDetection (并行ProcessStream推理线程 + StartMotion运动线程).
	/// 安全锁: IN8=0(门开) -> EmergencyStop -> 等待恢复 -> Continue(继续) 或 ReturnToStart(回起点).
	/// </summary>
	public class SideStationProcessor : IDisposable
	{
		// 依赖注入
		private readonly AiModelManager _models;          // 侧面缺陷YOLO模型
		private readonly string _savePath;                 // 图片保存根目录
		private SkuData _sku;                              // 当前SKU(提供P值)
		private readonly MotionControlManager _motion;     // 运动控制卡(轴运动/安全锁)
		private readonly HighSpeedImageSaver _imageSaver;  // 高速后台存图
		private readonly PerformanceMonitor _perfMonitor;  // 性能监控

		// 图像队列(线程安全, 生产者: OnCam7/OnCam8相机回调, 消费者: ProcessStream推理线程)
		private readonly ConcurrentQueue<SideImageCtx> _leftQueue = new ConcurrentQueue<SideImageCtx>();
		private readonly ConcurrentQueue<SideImageCtx> _rightQueue = new ConcurrentQueue<SideImageCtx>();
		private int _leftCount, _rightCount;               // 队列计数(调试用)
		private readonly object _countLock = new object();

		// 推理结果缓存
		private readonly List<SideResult> _leftResults = new List<SideResult>();
		private readonly List<SideResult> _rightResults = new List<SideResult>();
		// 显示缓存(左右独立)
		private readonly List<Mat> _displayImages = new List<Mat>();
		private readonly List<Bitmap> _displayBitmaps = new List<Bitmap>();  // 合并列表(存图用)
		private readonly List<Bitmap> _leftDisplayBitmaps = new List<Bitmap>();
		private readonly List<Bitmap> _rightDisplayBitmaps = new List<Bitmap>();
		private int _leftDisplayIndex;
		private int _rightDisplayIndex;
		private int _displayIndex;                          // 保留兼容
		private readonly object _resultLock = new object();

		// 统计计数
		private long _totalCount, _okCount, _ngCount;
		private bool _disposed;
		private CancellationTokenSource _motionCts;        // 运动周期取消令牌
		/// <summary>代际号: 防止旧批次finally覆盖新批次IsMoving</summary>
		private long _cycleId;

		public event Action<ProductResult> OnResultReady;
		public event Action<List<string>, List<string>, List<string>, int> OnStatusUpdate;
		/// <summary>实时显示事件：每推理一张立即推送渲染图到UI (Side, Bitmap)</summary>
		public event Action<Side, Bitmap> OnRealTimeDisplay;

		// ====== 开放参数 ======
		public float ConfThreshold = 0.5f, IouThreshold = 0.45f;  // 会被InitThresholdsFromModel覆盖
		public float CropRatio = 2.0f;
		public CaptureMode CaptureMode { get; set; } = CaptureMode.FlyCapture;
		public InferenceMode InferenceMode { get; set; } = InferenceMode.PerImage;
		public In12EdgeMap EdgeMapping { get; set; } = In12EdgeMap.RisingLeftFallingRight;
		// 安全锁: 读取传感器信号，不安全时阻止运动轴移动
		public int SafetyLockPort { get; set; } = 8;  // IN8=安全锁, 1=关门安全, 0=开门禁止
		public bool SafetyLockActiveHigh { get; set; } = false; // 反转后读0才是关门
	// 兼容旧接口
		public TriggerEdgeMode EdgeMode { get { return (TriggerEdgeMode)(int)EdgeMapping; } set { EdgeMapping = (In12EdgeMap)(int)value; } }
		public bool UseContinuousMode { get { return CaptureMode == CaptureMode.StopCapture; } set { CaptureMode = value ? CaptureMode.StopCapture : CaptureMode.FlyCapture; } }
		public int CurrentIndex => _leftDisplayIndex;
		public Bitmap GetCurrentDisplayImage() { lock (_resultLock) { if (_displayBitmaps.Count > 0 && _displayIndex >= 0 && _displayIndex < _displayBitmaps.Count) return (Bitmap)_displayBitmaps[_displayIndex].Clone(); return null; } }
		public Bitmap GetCurrentLeftImage() { lock (_resultLock) { if (_leftDisplayBitmaps.Count > 0 && _leftDisplayIndex >= 0 && _leftDisplayIndex < _leftDisplayBitmaps.Count) return (Bitmap)_leftDisplayBitmaps[_leftDisplayIndex].Clone(); return null; } }
		public Bitmap GetCurrentRightImage() { lock (_resultLock) { if (_rightDisplayBitmaps.Count > 0 && _rightDisplayIndex >= 0 && _rightDisplayIndex < _rightDisplayBitmaps.Count) return (Bitmap)_rightDisplayBitmaps[_rightDisplayIndex].Clone(); return null; } }

		public enum TriggerEdgeMode { RisingLeftFallingRight = 0, RisingRightFallingLeft = 1 }
		/// <summary>安全锁恢复模式：Continue=继续执行, ReturnToStart=返回起始位</summary>
		public enum SafetyRecovery { Continue = 0, ReturnToStart = 1 }
		public SafetyRecovery RecoveryMode { get; set; } = SafetyRecovery.Continue;
		public bool MissingAsNg { get; set; } = true;
		public bool ReverseBoxOrder { get; set; }
		public bool MotionEnabled { get; set; } = true;
		public bool EnableSideDefectCheck { get; set; } = true;
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
		{ _models = models; _savePath = savePath; _sku = sku; _motion = motion; _imageSaver = imageSaver; _perfMonitor = perfMonitor;
				var sideParams = Config.ModelParams.Load("side");
				CropRatio = sideParams.SideCropRatio;
			}

		public void UpdateSku(SkuData sku) { _sku = sku; }

		/// <summary>重新加载ModelParams，无需重启软件</summary>
		public void ReloadModelParams()
		{
			var sideParams = Config.ModelParams.Load("side");
			CropRatio = sideParams.SideCropRatio;
			if (sideParams.SideConf > 0) ConfThreshold = sideParams.SideConf;
			if (sideParams.SideIou > 0) IouThreshold = sideParams.SideIou;
			Logger.Info($"[Side] ModelParams已重新加载 Conf={ConfThreshold:F2} Iou={IouThreshold:F2}");
		}

		/// <summary>测试用：直接传入左/右侧面图片对进行推理（不影响P值）</summary>
		public void TestProcessPair(Bitmap leftBmp, Bitmap rightBmp)
		{
			long pid = DateTime.Now.Ticks;
			int originalP = _sku?.P ?? 1;
			ClearBatch();
			AddImage(_leftQueue, ref _leftCount, leftBmp, pid, Side.Left);
			AddImage(_rightQueue, ref _rightCount, rightBmp, pid, Side.Right);
			_sku.P = Math.Max(_leftCount, _rightCount);
			Task.Run(() =>
			{
				try
				{
					var leftImgs = new List<SideImageCtx>();
					var rightImgs = new List<SideImageCtx>();
					while (_leftQueue.TryDequeue(out var l)) leftImgs.Add(l);
					while (_rightQueue.TryDequeue(out var r)) rightImgs.Add(r);
					_leftCount = 0; _rightCount = 0;
					ProcessPerImageInference(leftImgs, rightImgs, _sku.P);
					// 填充并汇总
					while (_leftResults.Count < _sku.P) _leftResults.Add(new SideResult { Status = "OK", Side = Side.Left, Index = _leftResults.Count });
					while (_rightResults.Count < _sku.P) _rightResults.Add(new SideResult { Status = "OK", Side = Side.Right, Index = _rightResults.Count });
					var merged = new List<string>();
					for (int i = 0; i < _sku.P; i++)
						merged.Add((_leftResults[i].Status == "OK" && _rightResults[i].Status == "OK") ? "OK" : (_leftResults[i].Status != "OK" ? _leftResults[i].Status : _rightResults[i].Status));
					bool isOk = merged.All(s => s == "OK");
					if (isOk) _okCount += _sku.P; else { _okCount += _sku.P - merged.Count(s => s != "OK"); _ngCount += merged.Count(s => s != "OK"); }
					BuildDisplayImages(leftImgs, rightImgs, _sku.P);
					var result = new ProductResult { ProductId = pid, CreateTime = DateTime.Now, SideResult = isOk, SideDefects = merged.Where(s => s != "OK").Distinct().ToList() };
					lock (_resultLock) { if (_displayBitmaps.Count > 0) result.SideRenderImage = (Bitmap)_displayBitmaps[0].Clone(); }
					SaveImages(leftImgs, rightImgs, merged, isOk);
					OnResultReady?.Invoke(result);
					OnStatusUpdate?.Invoke(new List<string>(), new List<string>(), merged, _sku.P);
				}
				catch (Exception ex) { Logger.Error("[Side] 测试异常: " + ex.Message); }
				finally { _sku.P = originalP; _leftResults.Clear(); _rightResults.Clear(); }
			});
		}

		public void OnCam7(Bitmap bmp, long pid) { if (bmp != null) AddImage(_leftQueue, ref _leftCount, bmp, pid, Side.Left); }
		public void OnCam8(Bitmap bmp, long pid) { if (bmp != null) AddImage(_rightQueue, ref _rightCount, bmp, pid, Side.Right); }

		/// <summary>图像入队: ConcurrentQueue原子入队→Interlocked计数, 生产者=相机回调, 消费者=ProcessStream</summary>
		private void AddImage(ConcurrentQueue<SideImageCtx> q, ref int count, Bitmap bmp, long pid, Side side)
		{
			q.Enqueue(new SideImageCtx { Image = bmp, ProductId = pid, Side = side });
			Interlocked.Increment(ref count);
		}

		// 从模型best.json加载阈值
		/// <summary>从模型best.json加载侧面缺陷检测的Conf/Iou阈值(覆盖默认值)</summary>
		public void InitThresholdsFromModel() {
			if (_models.SideDefectModel != null) {
				ConfThreshold = _models.SideDefectModel.DefaultConfThres;
				IouThreshold = _models.SideDefectModel.DefaultIouThres;
				Logger.Info($"[Side] 阈值从模型加载: Conf={ConfThreshold:F2} Iou={IouThreshold:F2}");
			}
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

		/// <summary>
		/// IN13下降沿触发, 启动侧面检测(调用者确保IsMoving=false).
		/// 流程: 安全锁等待 -> ClearBatch -> 并行ProcessStream+StartMotion -> FinalizeResults.
		/// _cycleId代际号防止旧批次finally覆盖新批次IsMoving.
		/// </summary>
		public void StartDetection()
		{
			if (_disposed) { Logger.Warning("[Side] 已释放"); return; }

			long myCycleId = Interlocked.Increment(ref _cycleId);
			IsMoving = true;
			SetLimitSwitches();
			_motionCts = new CancellationTokenSource();
			var cts = _motionCts;
			Task.Run(() =>
			{
				try
				{
					// 等待安全锁释放
					if (!CheckSafetyLock())
					{
						Logger.Info("[Side] 安全锁未释放(门开) IN" + SafetyLockPort + "=0，等待关门...");
						while (!CheckSafetyLock() && !cts.Token.IsCancellationRequested)
							Thread.Sleep(5);
						if (cts.Token.IsCancellationRequested) { Logger.Warning("[Side] 安全锁等待被取消"); return; }
						Logger.Info("[Side] 安全锁已释放(门关) IN" + SafetyLockPort + "=1，继续执行");
					}
					Logger.Info("[Side] ====== 侧面检测开始 P=" + _sku.P + " ======");
					ClearBatch();
					lock (_resultLock) { _leftResults.Clear(); _rightResults.Clear(); }
					var streamTask = Task.Run(() => ProcessStream(cts.Token));
					StartMotion(cts.Token);
					streamTask.Wait(2000);  // ProcessStream内部有3s无图超时, 这里给2s兜底
					FinalizeResults();
				}
				catch (OperationCanceledException) { Logger.Warning("[Side] 运动被取消(安全锁)"); FinalizeResults(); }
				catch (Exception ex) { Logger.Error("[Side] 异常: " + ex.Message); }
				finally
				{
					if (Interlocked.Read(ref _cycleId) == myCycleId)
						IsMoving = false;
					cts.Dispose();
				}
			});
		}
		/// <summary>
		/// 运动控制: 三段式轴运动, 相机触发由外部硬件(ZMC BASIC程序)控制
		/// 流程:
		///   1. 到起点: WaitForSafetyLock → MoveAbs(起点) → WaitForMove(10s超时)
		///   2. 前进: WaitForSafetyLock → SetAxisSpeed(ForwardSpeed) → MoveAbs(终点) → WaitForMove(60s超时)
		///      前进阶段中ProcessStream同时进行推理
		///   3. 返回: SetAxisSpeed(ReturnSpeed) → MoveAbs(起点) → WaitForMove(10s超时)
		/// 每段运动中WaitForMove内部5ms安全锁轮询, 门开→EmergencyStop→恢复→Continue/Return
		/// </summary>
		private void StartMotion(CancellationToken cancel)
		{
			int p = _sku.P;
			Logger.Info("[Side] 运动开始: 轴" + SideAxis + " " + StartPosition + "→" + EndPosition + " 前进速度=" + ForwardSpeed + " 回程速度=" + ReturnSpeed + " 安全锁模式=" + (RecoveryMode == SafetyRecovery.Continue ? "继续执行" : "返回起始位"));

			// 到起点 — 使用回程速度快速就位(首次必须设置速度, 否则沿用上次未知值)
			WaitForSafetyLock(cancel);
			SetAxisSpeed(ReturnSpeed);
			_motion.MoveAbs(SideAxis, StartPosition);
			if (!WaitForMove(SideAxis, StartPosition, 10000, cancel)) return;

			// 前进到终点
			WaitForSafetyLock(cancel);
			SetAxisSpeed(ForwardSpeed);
			_motion.MoveAbs(SideAxis, EndPosition);
			if (!WaitForMove(SideAxis, EndPosition, 60000, cancel)) return;

			// 返回起点
			Logger.Info("[Side] 返回起始位 L=" + _leftCount + " R=" + _rightCount);
			WaitForSafetyLock(cancel);
			SetAxisSpeed(ReturnSpeed);
			_motion.MoveAbs(SideAxis, StartPosition);
			WaitForMove(SideAxis, StartPosition, 10000, cancel);
		}

		/// <summary>等待安全锁释放: 门开(IN8=0)则阻塞等待，门关(IN8=1)则通过</summary>
		private void WaitForSafetyLock(CancellationToken cancel)
		{
			if (CheckSafetyLock()) return;
			Logger.Info("[Side] 安全锁触发(门开) IN" + SafetyLockPort + "=0，等待关门...");
			while (!CheckSafetyLock() && !cancel.IsCancellationRequested)
				Thread.Sleep(5);
			if (!cancel.IsCancellationRequested)
				Logger.Info("[Side] 安全锁已释放(门关) IN" + SafetyLockPort + "=1，继续运动");
		}

		/// <summary>
		/// 等待轴运动到位(每5ms轮询)，支持安全锁暂停/恢复
		/// 安全锁触发流程:
		///   1. CheckSafetyLock()=false → EmergencyStop(axis, mode=0立即停) → stopped=true
		///   2. 等待安全锁恢复(持续5ms轮询)
		///   3. 恢复后:
		///      RecoveryMode.Continue: ClearHardwareAlarm + MoveAbs(目标) → 继续等待到位
		///      RecoveryMode.ReturnToStart: ClearHardwareAlarm + MoveAbs(起点) → return false
		///        (返回途中也有安全锁监控, 再次触发会递归处理)
		/// 位置验证: 轴停止后必须验证实际位置≈目标位置(±0.5容差), 防止被外部MoveAbs打断后误判到位
		/// 返回值: true=正常到位 | false=ReturnToStart已中止(外部应终止当前周期)
		/// </summary>
		/// <param name="axis">轴号</param>
		/// <param name="targetPos">目标位置</param>
		/// <param name="timeoutMs">超时时间(ms)</param>
		/// <param name="cancel">取消令牌(运动周期被取消时抛出)</param>
		private bool WaitForMove(int axis, float targetPos, int timeoutMs, CancellationToken cancel)
		{
			var sw = Stopwatch.StartNew();
			bool stopped = false;
			int reissueCount = 0;  // 重新发送指令计数器，防止无限循环
			while (!cancel.IsCancellationRequested && sw.ElapsedMilliseconds < timeoutMs)
			{
				// ── 安全锁检查(硬件IO读取，不受PC CPU影响) ──
				if (!CheckSafetyLock())
				{
					if (!stopped)
					{
						Logger.Warning("[Side] 运动中安全锁触发! 急停轴" + axis);
						_motion.EmergencyStop(axis);
						stopped = true;
					}
					Thread.Sleep(5);
					continue;
				}

				// 安全锁已恢复
				if (stopped)
				{
					stopped = false;
					if (RecoveryMode == SafetyRecovery.ReturnToStart)
					{
						Logger.Info("[Side] 安全锁已释放，模式=返回起始位，中止当前运动→回起点");
					_motion.ClearHardwareAlarm(axis);
						_motion.MoveAbs(axis, StartPosition);
						bool returnStopped = false;
						while (_motion.IsMoving(axis) && !cancel.IsCancellationRequested)
						{
							if (!CheckSafetyLock())
							{
								if (!returnStopped) { Logger.Warning("[Side] 回归途中安全锁触发! 急停轴" + axis); _motion.EmergencyStop(axis); returnStopped = true; }
								Thread.Sleep(5); continue;
							}
							_motion.ClearHardwareAlarm(axis); if (returnStopped) { Logger.Info("[Side] 安全锁恢复，继续回归起点"); _motion.MoveAbs(axis, StartPosition); returnStopped = false; }
							Thread.Sleep(5);
						}
						return false;
					}
					else
					{
						Logger.Info("[Side] 安全锁已释放，恢复运动→目标" + targetPos);
					_motion.ClearHardwareAlarm(axis);
						_motion.MoveAbs(axis, targetPos);
					}
				}

				// 位置验证: 轴停止后检查实际位置是否到达目标
				//    仅检查IsMoving不够——外部MoveAbs可能让轴在其他位置停下
				if (!_motion.IsMoving(axis))
				{
					float curPos = _motion.GetPosition(axis);
					if (Math.Abs(curPos - targetPos) <= 0.5f)
						return true;  // 已到达目标位置

					// 轴停了但不在目标位置(被外部指令打断)，重新发送MoveAbs
					if (reissueCount < 3)
					{
						reissueCount++;
						Logger.Warning($"[Side] 轴未到目标位置(cur={curPos:F1}, tgt={targetPos:F1})，第{reissueCount}次重发运动指令");
						_motion.MoveAbs(axis, targetPos);
						continue;
					}
					else
					{
						Logger.Error($"[Side] 轴{reissueCount}次重发仍未到达目标，放弃等待");
						return false;
					}
				}
				Thread.Sleep(5);
			}
			cancel.ThrowIfCancellationRequested();
			return false;
		}

		/// <summary>设置轴限位IO+限位IO设置(硬件ALM_IN已禁用, 仅用软件安全锁)</summary>
		private void SetLimitSwitches() { if (_motion.IsConnected) { try { _motion.SetLimitIn(SideAxis, FwdInPort, RevInPort, DatumInPort); Logger.Info("[Side] 限位已设置: FWD=IN" + FwdInPort + " REV=IN" + RevInPort + " DATUM=IN" + DatumInPort); } catch (Exception ex) { Logger.Warning("[Side] 限位设置失败: " + ex.Message); } } if (SafetyLockPort > 0) _motion.SetHardwareSafetyAlarm(SideAxis, SafetyLockPort); }
	/// <summary>设置轴运行参数: 速度+加速度+减速度, 每次运动段切换前调用(前进/返回各自速度)</summary>
		private void SetAxisSpeed(float speed) { _motion.SetSpeed(SideAxis, speed); _motion.SetAccel(SideAxis, Accel); _motion.SetDecel(SideAxis, Decel); }

		/// <summary>检查安全锁传感器: true=安全可运动, false=不安全阻止运动</summary>
	/// <summary>安全锁检查: 读IN8硬件IO → true=安全可运动 | false=不安全</summary>
		private bool CheckSafetyLock()
		{
			return _motion.CheckSafetyLock(SafetyLockPort, SafetyLockActiveHigh);
		}

		/// <summary>实时流式处理：运动过程中拍一张推理一张显示一张</summary>
		private List<SideImageCtx> _processedLeftImgs = new List<SideImageCtx>();
		private List<SideImageCtx> _processedRightImgs = new List<SideImageCtx>();

	/// <summary>实时流式处理 — 运动中交替取左右队列图片, 每张即刻InferSingle推理→OnRealTimeDisplay推送UI, 处理完p*2张或cancel退出
	/// 防止死等: 连续3秒无新图片到达则退出(运动可能已提前结束或被中断)</summary>
		private void ProcessStream(CancellationToken cancel)
		{
			int p = _sku.P, processed = 0;
			var swTotal = Stopwatch.StartNew();
			double totalInferMs = 0;
			bool tryLeftFirst = true;  // 交替优先，无偏处理：按收到顺序
			int noImgCount = 0;        // 连续无图计数(每1ms+1, 超3000即3秒退出)
			_processedLeftImgs.Clear(); _processedRightImgs.Clear();
			while (!cancel.IsCancellationRequested && processed < p * 2)
			{
				SideImageCtx ctx;
				bool gotOne;
				// 交替优先尝试的队列，避免永远先取左；任一侧空则取另一侧
				if (tryLeftFirst)
					gotOne = _leftQueue.TryDequeue(out ctx) || _rightQueue.TryDequeue(out ctx);
				else
					gotOne = _rightQueue.TryDequeue(out ctx) || _leftQueue.TryDequeue(out ctx);
				tryLeftFirst = !tryLeftFirst;

				if (!gotOne) {
					Thread.Sleep(1);
					noImgCount++;
					// Cancel或连续3秒无新图片 → 退出等待(缺失由FinalizeResults→MissingAsNg填NG)
					if (cancel.IsCancellationRequested)
					{
						Logger.Info($"[Side] ProcessStream 收到取消信号，退出(已处理{processed}/期望{p*2})");
						break;
					}
					if (noImgCount > 3000)
					{
						Logger.Warning($"[Side] ProcessStream {noImgCount}ms无新图片，退出等待(已处理{processed}/期望{p*2})");
						break;
					}
					continue;
				}
				noImgCount = 0;  // 有图就重置计数

				bool isLeft = ctx.Side == Side.Left;
				var targetResults = isLeft ? _leftResults : _rightResults;
				var targetList = isLeft ? _processedLeftImgs : _processedRightImgs;
				string sideTag = isLeft ? "L" : "R";
				int idx = targetResults.Count;

				var sw = Stopwatch.StartNew();
				var res = InferSingle(ctx, idx);
				double inferMs = sw.Elapsed.TotalMilliseconds;
				totalInferMs += inferMs;
				lock (_resultLock) targetResults.Add(res);
				targetList.Add(ctx);
				int displayIdx = ReverseBoxOrder ? (p - 1 - idx) : idx;
				Logger.Debug($"[Side] {sideTag}{idx + 1}/{p} 推理={inferMs:F0}ms 结果={res.Status}");
				try { var renderBmp = RenderSideImage(ctx, displayIdx, p, targetResults); OnRealTimeDisplay?.Invoke(ctx.Side, renderBmp); } catch (Exception rex) { Logger.Error("[Side] 实时渲染异常: " + rex.Message); }
				EmitPartial(p);
				processed++;
				Thread.Sleep(20);
			}
			Logger.Info($"[Side] ProcessStream结束 processed={processed} 总推理={totalInferMs:F0}ms 平均={(processed > 0 ? totalInferMs / processed : 0):F0}ms 总耗时={swTotal.Elapsed.TotalMilliseconds:F0}ms");
		}

	/// <summary>实时推送部分结果: 取左右结果最大数量, 逐索引合并左右状态→OnStatusUpdate通知UI更新轮播图索引</summary>
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

	/// <summary>最终汇总(两阶段):
	/// 阶段1(同步~1ms): 填充缺失→合并状态→更新计数器→日志→触发事件(无渲染图)
	/// 阶段2(后台Task): BuildDisplayImages→渲染→存图, 数据已Copy不依赖共享列表
	///   阶段2完成后再通过OnResultReady补发渲染图</summary>
		private void FinalizeResults()
		{
			int p = _sku.P;

			// ─── 阶段1: 快速汇总(同步, 不渲染) ───
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

			// 日志
			var lStats2 = new Dictionary<string, int>(); var rStats2 = new Dictionary<string, int>();
			foreach (var sr in _leftResults) { if (sr.Status != "OK") { if (lStats2.ContainsKey(sr.Status)) lStats2[sr.Status]++; else lStats2[sr.Status] = 1; } }
			foreach (var sr in _rightResults) { if (sr.Status != "OK") { if (rStats2.ContainsKey(sr.Status)) rStats2[sr.Status]++; else rStats2[sr.Status] = 1; } }
			string defStr2 = " | 左侧面:" + (lStats2.Count > 0 ? string.Join(" ", lStats2.Select(kv => kv.Key + kv.Value)) : "0");
			defStr2 += " 右侧面:" + (rStats2.Count > 0 ? string.Join(" ", rStats2.Select(kv => kv.Key + kv.Value)) : "0");
			Logger.Info($"[Side] 完成 P={p} OK={mergedStatus.Count(s => s == "OK")} NG={mergedStatus.Count(s => s != "OK")}{defStr2}");

			// 立即触发事件(统计+状态, 渲染图稍后由阶段2补充)
			OnResultReady?.Invoke(result);
			OnStatusUpdate?.Invoke(new List<string>(), new List<string>(), mergedStatus, p);

			// ─── 阶段2: 后台渲染+存图(Copy数据避免被下一批ClearBatch清空) ───
			var savedLeftImgs = _processedLeftImgs.ToList();
			var savedRightImgs = _processedRightImgs.ToList();
			var savedLeftRes = _leftResults.ToList();
			var savedRightRes = _rightResults.ToList();
			var savedMerged = mergedStatus.ToList();
			bool savedIsOk = isOk;

			Task.Run(() =>
			{
				try
				{
					BuildDisplayImages(savedLeftImgs, savedRightImgs, p);
					Bitmap leftRender = null, rightRender = null;
					lock (_resultLock)
					{
						if (savedLeftImgs.Count > 0)
							leftRender = RenderSideImage(savedLeftImgs[0], 0, p, savedLeftRes);
						if (savedRightImgs.Count > 0)
							rightRender = RenderSideImage(savedRightImgs[0], 0, p, savedRightRes);
					}
					result.SideRenderImage = leftRender;
					result.SideLeftRenderImage = leftRender;
					result.SideRightRenderImage = rightRender;
					SaveImages(savedLeftImgs, savedRightImgs, savedMerged, savedIsOk);
				}
				catch (Exception ex) { Logger.Error("[Side] 后台渲染异常: " + ex.Message); }
			});
		}

		/// <summary>保存渲染图（带文字+缺陷框）</summary>
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

			// ☆ 阶段1: 快速汇总(同步)
			var result = new ProductResult { ProductId = DateTime.Now.Ticks, CreateTime = DateTime.Now, SideResult = isOk, SideDefects = mergedStatus.Where(s => s != "OK").Distinct().ToList() };
			var lStats = new Dictionary<string, int>(); var rStats = new Dictionary<string, int>();
			foreach (var sr in _leftResults) { if (sr.Status != "OK") { if (lStats.ContainsKey(sr.Status)) lStats[sr.Status]++; else lStats[sr.Status] = 1; } }
			foreach (var sr in _rightResults) { if (sr.Status != "OK") { if (rStats.ContainsKey(sr.Status)) rStats[sr.Status]++; else rStats[sr.Status] = 1; } }
			string defStr = " | 左侧面:" + (lStats.Count > 0 ? string.Join(" ", lStats.Select(kv => kv.Key + kv.Value)) : "0");
			defStr += " 右侧面:" + (rStats.Count > 0 ? string.Join(" ", rStats.Select(kv => kv.Key + kv.Value)) : "0");
			Logger.Info($"[Side] 完成 P={p} OK={mergedStatus.Count(s => s == "OK")} NG={mergedStatus.Count(s => s != "OK")}{defStr} | 耗时={sw.Elapsed.TotalMilliseconds:F0}ms");
			OnResultReady?.Invoke(result);
			OnStatusUpdate?.Invoke(new List<string>(), new List<string>(), mergedStatus, p);

			// ☆ 阶段2: 后台渲染+存图
			var pli = leftImages.ToList(); var pri = rightImages.ToList();
			var plr = _leftResults.ToList(); var prr = _rightResults.ToList();
			var pms = mergedStatus.ToList(); bool pisOk = isOk;
			Task.Run(() => {
				try {
					BuildDisplayImages(pli, pri, p);
					lock (_resultLock) { if (_displayBitmaps.Count > 0) { result.SideRenderImage = (Bitmap)_displayBitmaps[0].Clone(); result.SideLeftRenderImage = result.SideRenderImage; } }
					SaveImages(pli, pri, pms, pisOk);
				} catch (Exception ex) { Logger.Error("[Side] 后台渲染异常: " + ex.Message); }
			});
		}

		/// <summary>逐张推理模式: 遍历左右列表→每张InferSingle→结果存入_leftResults/_rightResults</summary>
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

		/// <summary>批量推理模式: 所有图头尾裁剪→拼接→一次性PredictBatch→解析映射回原图坐标</summary>
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

			if (EnableSideDefectCheck && _models.SideDefectModel != null && allCrops.Count > 0)
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

	/// <summary>单张侧面图像推理: 头尾裁剪+YOLO批量推理(2张). head=左cropW像素, tail=右cropW像素. 头尾两段->YOLO推理->坐标映射回原图.</summary>
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
						if (EnableSideDefectCheck && _models.SideDefectModel != null)
						{
							var batch = new List<Mat> { head, tail };
							var batchResults = _models.SideDefectModel.PredictBatch(batch, ConfThreshold, IouThreshold);
							bool ng = false;
							// 头部检测：坐标保持原样(0~cropW)
							// 头部检测：BoxesN是裁剪图上的归一化坐标，需映射回原图
							if (batchResults != null && batchResults.Count > 0 && batchResults[0]?.BoxesN?.Length > 0)
							{
								ng = true;
								for (int j = 0; j < batchResults[0].BoxesN.Length; j++)
								{
									var b = batchResults[0].BoxesN[j];
									result.Defects.Add(new BoxDefect(j, "缺陷" + batchResults[0].ClassIds[j],
										new float[] { b.X * cropW / w, b.Y, (b.X + b.Width) * cropW / w, b.Y + b.Height }, batchResults[0].Scores[j]));
								}
							}
							// 尾部检测：裁剪图坐标+(w-cropW)偏移映射回原图
							if (batchResults != null && batchResults.Count > 1 && batchResults[1]?.BoxesN?.Length > 0)
							{
								ng = true;
								for (int j = 0; j < batchResults[1].BoxesN.Length; j++)
								{
									var b = batchResults[1].BoxesN[j];
									result.Defects.Add(new BoxDefect(j, "缺陷" + batchResults[1].ClassIds[j],
										new float[] { (w - cropW + b.X * cropW) / w, b.Y, (w - cropW + (b.X + b.Width) * cropW) / w, b.Y + b.Height }, batchResults[1].Scores[j]));
								}
							}
							if (ng) result.Status = "NG";
						}
					}
				}
			}
			catch (Exception ex) { result.Status = "错误"; Logger.Error("[Side] 推理异常: " + ex.Message); }
			return result;
		}

	/// <summary>构建左右独立显示图: 左侧→_leftDisplayBitmaps, 右侧→_rightDisplayBitmaps, 同时填充合并列表(存图用)</summary>
		private void BuildDisplayImages(List<SideImageCtx> leftImages, List<SideImageCtx> rightImages, int p)
		{
			lock (_resultLock)
			{
				// 释放旧Bitmap
				foreach (var oldBmp in _leftDisplayBitmaps) oldBmp?.Dispose();
				foreach (var oldBmp in _rightDisplayBitmaps) oldBmp?.Dispose();
				foreach (var oldBmp in _displayBitmaps) oldBmp?.Dispose();
				_displayImages.Clear();
				_leftDisplayBitmaps.Clear();
				_rightDisplayBitmaps.Clear();
				_displayBitmaps.Clear();
				int count = Math.Max(leftImages.Count, rightImages.Count);
				// 左侧渲染 → _leftDisplayBitmaps(显示用) + _displayBitmaps(存图用)
				for (int i = 0; i < count; i++)
				{
					Bitmap bmp = i < leftImages.Count
						? RenderSideImage(leftImages[i], ReverseBoxOrder ? (count - 1 - i) : i, count, _leftResults)
						: CreateMissingBmp(i, count);
					_leftDisplayBitmaps.Add(bmp);
					_displayBitmaps.Add(bmp);
					_displayImages.Add(OpenCvSharp.Extensions.BitmapConverter.ToMat(bmp));
				}
				// 右侧渲染 → _rightDisplayBitmaps(显示用) + _displayBitmaps(存图用)
				for (int i = 0; i < count; i++)
				{
					Bitmap bmp = i < rightImages.Count
						? RenderSideImage(rightImages[i], ReverseBoxOrder ? (count - 1 - i) : i, count, _rightResults)
						: CreateMissingBmp(i, count);
					_rightDisplayBitmaps.Add(bmp);
					_displayBitmaps.Add(bmp);
					_displayImages.Add(OpenCvSharp.Extensions.BitmapConverter.ToMat(bmp));
				}
				_leftDisplayIndex = Math.Max(0, count - 1);
				_rightDisplayIndex = Math.Max(0, count - 1);
				_displayIndex = Math.Max(0, _displayImages.Count - 1);
			}
		}

		/// <summary>创建缺图占位Bitmap: 深灰底色+"N/总数 缺少"文字, 用于不满P张时的显示补位</summary>
		private Bitmap CreateMissingBmp(int i, int total)
		{
			var bmp = new Bitmap(800, 600);
			using (var g = Graphics.FromImage(bmp))
			{
				g.Clear(Color.FromArgb(30, 30, 30));
				using (var f = new Font("微软雅黑", Math.Max(48f, bmp.Height / 30f), FontStyle.Bold))
					g.DrawString((i + 1) + "/" + total + " 缺少", f, Brushes.Gray, 50, bmp.Height / 2 - 30);
			}
			return bmp;
		}
	/// <summary>渲染单张侧面图: 1.裁图区半透明橙色覆盖+边界线 2.缺陷红框+标签 3.旋转(左-90°/右+90°) 4.状态文字+序号(图片正中)</summary>
		private Bitmap RenderSideImage(SideImageCtx ctx, int index, int total, List<SideResult> results)
		{
			try
			{
				using (var drawImg = ctx.Image.ToMat())  // 直接用ToMat结果，省掉Clone
				{
					int w = drawImg.Width, h = drawImg.Height;
					int cropW = (int)(h * CropRatio);
					if (cropW > w) cropW = w;

					// ── 1. 裁图区域半透明覆盖 (合并为一次overlay分配) ──
					using (var overlay = new Mat(drawImg.Size(), MatType.CV_8UC3, new Scalar(0, 0, 0)))
					{
						Cv2.Rectangle(overlay, new OpenCvSharp.Rect(0, 0, cropW, h), new Scalar(255, 140, 0), -1);
						Cv2.Rectangle(overlay, new OpenCvSharp.Rect(w - cropW, 0, cropW, h), new Scalar(255, 140, 0), -1);
						Cv2.AddWeighted(drawImg, 0.85, overlay, 0.15, 0, drawImg);
					}
					// 裁图区边界实线 (橙色)
					Cv2.Line(drawImg, new OpenCvSharp.Point(cropW, 0), new OpenCvSharp.Point(cropW, h), new Scalar(0, 165, 255), 2);
					Cv2.Line(drawImg, new OpenCvSharp.Point(w - cropW, 0), new OpenCvSharp.Point(w - cropW, h), new Scalar(0, 165, 255), 2);

					// ── 2. 缺陷框：绘制在原始方向（随图片旋转）──
					var res = results.FirstOrDefault(r => r.Index == index);
					var bmp = OpenCvSharp.Extensions.BitmapConverter.ToBitmap(drawImg);
					using (var g = Graphics.FromImage(bmp))
					{
						g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
						float penW = Math.Max(3f, h / 400f);
						float fontSz = Math.Max(20f, h / 60f);
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
								if (d.Score > 0 && d.Score < 1.0f) dt = dt + " " + d.Score.ToString("F2");
								var dsz = g.MeasureString(dt, df);
								int dy = y1 - (int)dsz.Height - 6; if (dy < 4) dy = y1 + 4;
								using (var dbg = new SolidBrush(Color.Red)) g.FillRectangle(dbg, x1, dy, dsz.Width + 8, dsz.Height + 6);
								g.DrawString(dt, df, Brushes.White, x1 + 3, dy + 2);
							}
						}
					}

					// ── 3. 显示旋转 ──
					if (ctx.Side == Side.Left || ctx.Side == Side.Right)
					{
						float angle = ctx.Side == Side.Left ? -90 : 90;
						var rotatedBmp = new Bitmap(bmp.Height, bmp.Width);
						using (var rg = Graphics.FromImage(rotatedBmp))
						{
							rg.TranslateTransform(rotatedBmp.Width / 2f, rotatedBmp.Height / 2f);
							rg.RotateTransform(angle);
							rg.TranslateTransform(-bmp.Width / 2f, -bmp.Height / 2f);
							rg.DrawImage(bmp, 0, 0);
						}
						bmp.Dispose();
						bmp = rotatedBmp;
					}
					// ── 4. 状态+序号：绘制在旋转后（水平文字，放在图片中间不推理区域）──
					int rw = bmp.Width, rh = bmp.Height;
					float bigFontSz = Math.Max(96f, Math.Min(rw, rh) / 12f);  // 放大字体
					string status = res?.Status ?? "?";
					Color stColor = status == "OK" ? Color.LimeGreen : (status == "?" ? Color.Gray : Color.Red);
					using (var g2 = Graphics.FromImage(bmp))
					{
						g2.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
						// 状态文字：图片正中偏上
						using (var sf = new Font("微软雅黑", bigFontSz, FontStyle.Bold))
						{
							var stSz = g2.MeasureString(status, sf);
							int stX = (rw - (int)stSz.Width) / 2, stY = rh / 3;
							using (var stBg = new SolidBrush(Color.FromArgb(180, Color.Black)))
								g2.FillRectangle(stBg, stX - 12, stY - 4, stSz.Width + 24, stSz.Height + 10);
							using (var stBr = new SolidBrush(stColor))
								g2.DrawString(status, sf, stBr, stX, stY);
						}
						// 序号：图片正中偏下
						string label = (index + 1) + "/" + total;
						using (var f = new Font("微软雅黑", bigFontSz * 0.7f, FontStyle.Bold))
						{
							var sz = g2.MeasureString(label, f);
							int lx = (rw - (int)sz.Width) / 2, ly = rh * 2 / 3;
							using (var bg = new SolidBrush(Color.FromArgb(180, Color.Black)))
								g2.FillRectangle(bg, lx - 12, ly - 4, sz.Width + 24, sz.Height + 10);
							g2.DrawString(label, f, Brushes.Cyan, lx, ly);
						}
					}
					return bmp;
				}
			}
			catch (Exception ex) { Logger.Error("[Side] 渲染异常: " + ex.Message); return null; }
		}

		/// <summary>获取当前显示的Mat图像(Clone副本, 线程安全)</summary>
		public Mat GetDisplayImage()
		{
			lock (_resultLock) { if (_displayImages.Count > 0 && _displayIndex >= 0 && _displayIndex < _displayImages.Count) return _displayImages[_displayIndex].Clone(); return null; }
		}
		/// <summary>轮播已禁用 — 左右独立固定显示, 不再循环切换</summary>
		public void NavigatePrev() { }
		public void NavigateNext() { }

	/// <summary>保存侧面工位图片: 左/右原图+渲染图 → JPEG 85% → Images/{日期}/{班次}/侧面工位/{OK|NG}/{左/右侧面}/</summary>
		private void SaveImages(List<SideImageCtx> leftImages, List<SideImageCtx> rightImages, List<string> status, bool isOk)
		{
			try
			{
				if (leftImages == null || rightImages == null) return;
				bool so = _Config.IsSaveOkImage, sn = _Config.IsSaveNgImage;
				Logger.Info("[Side] 存图配置: SaveOk=" + so + " SaveNg=" + sn + " isOk=" + isOk + " L=" + leftImages.Count + " R=" + rightImages.Count);
				if (isOk && !so) { Logger.Info("[Side] 跳过存图(OK图不存)"); return; }
				if (!isOk && !sn) { Logger.Info("[Side] 跳过存图(NG图不存)"); return; }
				string sh = GetShift(), dd = DateTime.Now.ToString("yyMMdd");
				string baseDir = Path.Combine(_savePath, dd, sh, "侧面工位", isOk ? "OK" : "NG");
				string leftDir = Path.Combine(baseDir, "左侧面");
				string rightDir = Path.Combine(baseDir, "右侧面");
				Directory.CreateDirectory(leftDir); Directory.CreateDirectory(rightDir);
				long pid = DateTime.Now.Ticks; string nt = string.Join("_", status.Where(s => s != "OK").Distinct().DefaultIfEmpty("OK"));
				string ts = DateTime.Now.ToString("yyyyMMdd_HHmmss_fff");
				// 左侧原图
				for (int j = 0; j < leftImages.Count; j++) { if (leftImages[j].Image != null) { bool ln = isOk || (j < _leftResults.Count && _leftResults[j].Status != "OK"); if (ln) _imageSaver.AddSaveTask(Path.Combine(leftDir, ts + "_原图_" + (j + 1) + "_" + nt + ".jpg"), leftImages[j].Image.ToJpegBytesFast(85), true, 85); } }
				// 右侧原图
				for (int j = 0; j < rightImages.Count; j++) { if (rightImages[j].Image != null) { bool rn = isOk || (j < _rightResults.Count && _rightResults[j].Status != "OK"); if (rn) _imageSaver.AddSaveTask(Path.Combine(rightDir, ts + "_原图_" + (j + 1) + "_" + nt + ".jpg"), rightImages[j].Image.ToJpegBytesFast(85), true, 85); } }
				// 渲染图：左侧渲染图存左目录，右侧存右目录
				int savedRender = 0;
				lock (_resultLock)
				{
					int lc = leftImages.Count;
					for (int i = 0; i < _displayBitmaps.Count; i++) {
						if (_displayBitmaps[i] == null) continue; int boxIdx = i < lc ? i : (i - lc); { bool isLeft = i < lc; bool ng = isOk || (isLeft ? (boxIdx < _leftResults.Count && _leftResults[boxIdx].Status != "OK") : (boxIdx < _rightResults.Count && _rightResults[boxIdx].Status != "OK")); if (!ng) continue; }
						var d = i < lc ? leftDir : rightDir;
						int idx = i < lc ? (i + 1) : (i - lc + 1);
						_imageSaver.AddSaveTask(Path.Combine(d, ts + "_渲染_" + idx + "_" + nt + ".jpg"), _displayBitmaps[i].ToJpegBytesFast(85), true, 85);
						savedRender++;
					}
				}
				Logger.Info("[Side] 存图完成: 左原图" + leftImages.Count + " 右原图" + rightImages.Count + " 渲染图" + savedRender);
			}
			catch (Exception ex) { Logger.Error("[Side] 存图异常: " + ex.Message); }
		}
	/// <summary>清空本批数据: 清空左右队列+清零计数+清空结果+释放DisplayBitmaps, 每个周期开始前调用</summary>
		private void ClearBatch() { lock (_countLock) { while (_leftQueue.TryDequeue(out _)) ; while (_rightQueue.TryDequeue(out _)) ; _leftCount = 0; _rightCount = 0; } _leftResults.Clear(); _rightResults.Clear(); lock (_resultLock) { foreach (var b in _leftDisplayBitmaps) b?.Dispose(); foreach (var b in _rightDisplayBitmaps) b?.Dispose(); foreach (var b in _displayBitmaps) b?.Dispose(); _leftDisplayBitmaps.Clear(); _rightDisplayBitmaps.Clear(); _displayBitmaps.Clear(); } }
		private string GetShift() { var n = DateTime.Now.TimeOfDay; if (n >= TimeSpan.Parse("00:00") && n <= TimeSpan.Parse("07:59")) return "晚班"; if (n >= TimeSpan.Parse("08:00") && n <= TimeSpan.Parse("15:59")) return "早班"; return "中班"; }
		public void RestoreCounts(long ok, long ng) { _okCount = ok; _ngCount = ng; _totalCount = ok + ng; }
		public void ClearCounters() { Interlocked.Exchange(ref _totalCount, 0); Interlocked.Exchange(ref _okCount, 0); Interlocked.Exchange(ref _ngCount, 0); }
		public void Dispose() { if (_disposed) return; _disposed = true; _motionCts?.Cancel(); lock (_resultLock) { foreach (var b in _leftDisplayBitmaps) b?.Dispose(); foreach (var b in _rightDisplayBitmaps) b?.Dispose(); foreach (var b in _displayBitmaps) b?.Dispose(); _leftDisplayBitmaps.Clear(); _rightDisplayBitmaps.Clear(); _displayBitmaps.Clear(); _displayImages.Clear(); } }
	}

	public enum Side { Left, Right }
	internal class SideImageCtx { public Bitmap Image; public long ProductId; public Side Side; }
	internal class SideResult { public int Index; public Side Side; public string Status = "OK"; public List<BoxDefect> Defects = new List<BoxDefect>(); }
}
