using System;
using System.IO;
using AIsdk;
using Config;
using CommonLib;
using YoloInference;
using YoloSegmentationEnd2End;
using CommonLib;

namespace Models
{
/// <summary>
/// AI模型管理器, 统一加载所有AI模型(11个), 分配GPU资源.
/// GPU分配: 显卡0=YOLO目标检测, 显卡1=ViMo(OCR/分割/分类).
/// 模型: FrontOcr/FrontBoxBreak/EndFaceUpper/EndFaceLower/BackBarcode/
///   BackDateCodeSeg/Cls/Ocr/BackHook/HookSlight/SideDefect.
/// 加载顺序: Front->EndFace->Back->Side. ViMo优先GPU, 失败降级CPU.
/// </summary>
	public class AiModelManager : IDisposable
	{
		private readonly ModelPathConfig _config;
		private bool _disposed;

		// 正面模型
		public Vimo FrontOcrModel { get; private set; }           // Vimo -> .vimosln
		public YoloOnnx FrontBoxBreakModel { get; private set; }  // Yolo -> .onnx + meta.json

		// 端面模型
		public YoloOnnx EndFaceUpperModel { get; private set; }    // Yolo -> .onnx + meta.json
		public YoloOnnx EndFaceLowerModel { get; private set; }    // Yolo -> .onnx + meta.json

		// 背面模型
		public YoloOnnx BackBarcodeModel { get; private set; }     // Yolo -> .onnx + meta.json
		public Vimo BackDateCodeSegModel { get; private set; }
		public Vimo BackDateCodeClsModel { get; private set; }
		public Vimo BackDateCodeOcrModel { get; private set; }
		public YoloOnnx BackHookModel { get; private set; }        // Yolo -> .onnx + meta.json
		public YoloOnnxSegmentation HookSlightModel { get; private set; } // 分割 -> .onnx
		public Vimo BackCutCharModel { get; private set; }         // Vimo -> .vimosln

		// 侧面模型
		public YoloOnnx SideDefectModel { get; private set; }        // Yolo -> .onnx + meta.json

		public event Action<string, int, int> OnModelLoadProgress;

		public AiModelManager(ModelPathConfig config)
		{
			_config = config ?? throw new ArgumentNullException(nameof(config));
		}

		public bool LoadAllModels()
		/// <summary>加载所有AI模型(共11个): 按顺序 Front→EndFace→Back→Side, 返回全部成功标志, 记录GPU配置日志</summary>
		{
			try
			{
				Logger.Info("========== 开始加载AI模型 ==========");
				Logger.Info($"GPU配置: Vimo模型使用显卡{_config.VimoGpuDeviceId}, Yolo模型使用显卡{_config.YoloGpuDeviceId}");

				bool allSuccess = true;

				allSuccess &= LoadFrontModels();
				allSuccess &= LoadEndFaceModels();
				allSuccess &= LoadBackModels();
				allSuccess &= LoadSideModels();

				Logger.Info($"========== AI模型加载完成 ({(allSuccess ? "成功" : "部分失败")}) ==========");
				return allSuccess;
			}
			catch (Exception ex)
			{
				Logger.Error($"AI模型加载失败: {ex.Message}\r\n{ex.StackTrace}");
				return false;
			}
		}

		private bool LoadFrontModels()
		/// <summary>加载正面模型: P号码OCR(ViMo/GPU1, .vimosln) + 盒子破损检测(YoloOnnx/GPU0, .onnx+meta.json)</summary>
		{
			try
			{
				// P号码OCR模型 (Vimo)
				if (!string.IsNullOrEmpty(_config.FrontPCodeOcrModel))
				{
					string fullPath = _config.GetFullPath(_config.FrontPCodeOcrModel);
					if (File.Exists(fullPath))
					{
						try
						{
							FrontOcrModel = new Vimo();
							// 使用配置的moduleId
							string moduleId = _config.FrontPCodeOcrModuleId ?? "3";
							int ret = FrontOcrModel.Init(fullPath, _config.UseGpu, _config.VimoGpuDeviceId, moduleId);
							if (ret == 0)
								Logger.Info($"正面P号码OCR模型加载成功: {fullPath} (moduleId={moduleId})");
							else
								Logger.Error($"正面P号码OCR模型加载失败: {FrontOcrModel.ErrorInfo}");
						}
						catch (Exception ex)
						{
							Logger.Error($"正面P号码OCR模型加载异常: {ex.Message}");
						}
					}
					else
					{
						Logger.Warning($"正面P号码OCR模型文件不存在: {fullPath}");
					}
				}

				// 盒子破检测模型 (Yolo)
				if (!string.IsNullOrEmpty(_config.FrontBoxBreakModel))
				{
					string fullPath = _config.GetFullPath(_config.FrontBoxBreakModel);
					string metaPath = Path.ChangeExtension(fullPath, "json");
					if (File.Exists(fullPath) && File.Exists(metaPath))
					{
						try
						{
							FrontBoxBreakModel = new YoloOnnx(fullPath, metaPath, 12);
							Logger.Info($"正面盒子破检测模型加载成功: {fullPath}");
						}
						catch (Exception ex)
						{
							Logger.Error($"正面盒子破检测模型加载异常: {ex.Message}");
						}
					}
					else
					{
						Logger.Warning($"正面盒子破检测模型文件不存在: {fullPath} 或 {metaPath}");
					}
				}

				return true;
			}
			catch (Exception ex)
			{
				Logger.Error($"加载正面模型失败: {ex.Message}\r\n{ex.StackTrace}");
				return false;
			}
		}

		private bool LoadEndFaceModels()
		/// <summary>加载端面模型: 上端面缺陷(YoloOnnx/GPU1) + 下端面缺陷(YoloOnnx/GPU1), 各需.onnx+meta.json</summary>
		{
			try
			{
				// 上端面缺陷模型 (Yolo -> .onnx + meta.json, 显卡1)
				if (!string.IsNullOrEmpty(_config.EndFaceUpperModel))
				{
					string fullPath = _config.GetFullPath(_config.EndFaceUpperModel);
					string metaPath = Path.ChangeExtension(fullPath, "json");
					ReportProgress($"正在加载上端面缺陷检测模型(Yolo, 显卡{_config.VimoGpuDeviceId}): {fullPath}", 40, 100);

					if (File.Exists(fullPath) && File.Exists(metaPath))
					{
						EndFaceUpperModel = new YoloOnnx(fullPath, metaPath, 8, gpuDeviceId: _config.VimoGpuDeviceId);
						Logger.Info($"上端面缺陷检测模型加载成功(显卡{_config.VimoGpuDeviceId})");
					}
					else
					{
						Logger.Warning($"上端面缺陷检测模型文件不存在: {fullPath} 或 {metaPath}");
					}
				}

				// 下端面缺陷模型 (Yolo -> .onnx + meta.json, 显卡1)
				if (!string.IsNullOrEmpty(_config.EndFaceLowerModel))
				{
					string fullPath = _config.GetFullPath(_config.EndFaceLowerModel);
					string metaPath = Path.ChangeExtension(fullPath, "json");
					ReportProgress($"正在加载下端面缺陷检测模型(Yolo, 显卡{_config.VimoGpuDeviceId}): {fullPath}", 50, 100);

					if (File.Exists(fullPath) && File.Exists(metaPath))
					{
						EndFaceLowerModel = new YoloOnnx(fullPath, metaPath, 8, gpuDeviceId: _config.VimoGpuDeviceId);
						Logger.Info($"下端面缺陷检测模型加载成功(显卡{_config.VimoGpuDeviceId})");
					}
					else
					{
						Logger.Warning($"下端面缺陷检测模型文件不存在: {fullPath} 或 {metaPath}");
					}
				}

				return true;
			}
			catch (Exception ex)
			{
				Logger.Error($"加载端面模型失败: {ex.Message}\r\n{ex.StackTrace}");
				return false;
			}
		}

		private bool LoadBackModels()
	/// <summary>
	/// 加载背面模型: 条码(YoloOnnx/GPU0), 日期码分割/分类/OCR(ViMo/GPU1),
	/// 挂钩明显(YoloOnnx/GPU0), 挂钩轻微分割(YoloSeg/GPU0), 切字(ViMo/GPU1).
	/// </summary>
		{
			try
			{
				// 条形码检测模型 (Yolo -> .onnx + meta.json, 显卡0)
				if (!string.IsNullOrEmpty(_config.BackBarcodeModel))
				{
					string fullPath = _config.GetFullPath(_config.BackBarcodeModel);
					string metaPath = Path.ChangeExtension(fullPath, "json");
					ReportProgress($"正在加载背面条形码检测模型(Yolo, 显卡{_config.YoloGpuDeviceId}): {fullPath}", 60, 100);

					if (File.Exists(fullPath) && File.Exists(metaPath))
					{
						BackBarcodeModel = new YoloOnnx(fullPath, metaPath, 2);
						Logger.Info($"背面条形码检测模型加载成功(显卡{_config.YoloGpuDeviceId})");
					}
					else
					{
						Logger.Warning($"背面条形码检测模型文件不存在: {fullPath} 或 {metaPath}");
					}
				}

				// 日期码模型 (3个Vimo子模型: 分割/分类/OCR, 显卡1)
				BackDateCodeSegModel = LoadVimoModel(_config.BackDateCodeSegModel, "分割", "5");
				BackDateCodeClsModel = LoadVimoModel(_config.BackDateCodeClsModel, "分类", "2");
				BackDateCodeOcrModel = LoadVimoModel(_config.BackDateCodeOcrModel, "OCR", "2");

				// 明显挂钩错位模型 (Yolo -> .onnx + meta.json, 显卡0)
				if (!string.IsNullOrEmpty(_config.BackHookDamageModel))
				{
					string fullPath = _config.GetFullPath(_config.BackHookDamageModel);
					string metaPath = Path.ChangeExtension(fullPath, "json");
					ReportProgress($"正在加载背面挂钩明显错位检测模型(Yolo, 显卡{_config.YoloGpuDeviceId}): {fullPath}", 75, 100);

					if (File.Exists(fullPath) && File.Exists(metaPath))
					{
						BackHookModel = new YoloOnnx(fullPath, metaPath, 2);
						Logger.Info($"背面挂钩明显错位检测模型加载成功(显卡{_config.YoloGpuDeviceId})");
					}
					else
					{
						Logger.Warning($"背面挂钩明显错位检测模型文件不存在: {fullPath} 或 {metaPath}");
					}
				}

				// 轻微挂钩错位模型 (分割 -> .onnx, 显卡0)
				if (!string.IsNullOrEmpty(_config.BackHookSlightModel))
				{
					string fullPath = _config.GetFullPath(_config.BackHookSlightModel);
					ReportProgress($"正在加载背面挂钩轻微错位分割模型(显卡{_config.YoloGpuDeviceId}): {fullPath}", 80, 100);

					if (File.Exists(fullPath))
					{
						HookSlightModel = new YoloOnnxSegmentation(fullPath, 1);
						Logger.Info($"背面挂钩轻微错位分割模型加载成功(显卡{_config.YoloGpuDeviceId})");
					}
					else
					{
						Logger.Warning($"背面挂钩轻微错位分割模型文件不存在: {fullPath}");
					}
				}

				// 切字识别模型 (Vimo -> .vimosln, 显卡1)
				if (!string.IsNullOrEmpty(_config.BackCutCharModel))
				{
					string fullPath = _config.GetFullPath(_config.BackCutCharModel);
					ReportProgress($"正在加载背面切字识别模型(Vimo, 显卡{_config.VimoGpuDeviceId}): {fullPath}", 85, 100);

					if (File.Exists(fullPath))
					{
						BackCutCharModel = new Vimo();
						string moduleId = _config.BackCutCharModuleId ?? "0";
						int ret = BackCutCharModel.Init(fullPath, _config.UseGpu, _config.VimoGpuDeviceId, moduleId);
						if (ret == 0)
							Logger.Info($"背面切字识别模型加载成功(显卡{_config.VimoGpuDeviceId}, moduleId={moduleId})");
						else
							Logger.Error($"背面切字识别模型加载失败: {BackCutCharModel.ErrorInfo}");
					}
					else
					{
						Logger.Warning($"背面切字识别模型文件不存在: {fullPath}");
					}
				}

				return true;
			}
			catch (Exception ex)
			{
				Logger.Error($"加载背面模型失败: {ex.Message}");
				return false;
			}
		}

		private bool LoadSideModels()
		/// <summary>加载侧面模型: 侧面缺陷检测(YoloOnnx/GPU1, .onnx+meta.json)</summary>
		{
			try
			{
				// 侧面缺陷模型 (Yolo -> .onnx + meta.json, 显卡0)
				if (!string.IsNullOrEmpty(_config.SideDefectModel))
				{
					string fullPath = _config.GetFullPath(_config.SideDefectModel);
					string metaPath = Path.ChangeExtension(fullPath, "json");
					ReportProgress($"正在加载侧面缺陷检测模型(Yolo, 显卡{_config.YoloGpuDeviceId}): {fullPath}", 90, 100);

					if (File.Exists(fullPath) && File.Exists(metaPath))
					{
						try
						{
							SideDefectModel = new YoloOnnx(fullPath, metaPath, 2, gpuDeviceId: _config.VimoGpuDeviceId);
							Logger.Info($"侧面缺陷检测模型加载成功(显卡{_config.VimoGpuDeviceId})");
						}
						catch (Exception ex)
						{
							Logger.Error($"侧面缺陷检测模型加载异常: {ex.Message}");
						}
					}
					else
					{
						Logger.Warning($"侧面缺陷检测模型文件不存在: {fullPath} 或 {metaPath}");
					}
				}

				return true;
			}
			catch (Exception ex)
			{
				Logger.Error($"加载侧面模型失败: {ex.Message}");
				return false;
			}
		}

		private void ReportProgress(string message, int current, int total)
		{
			Logger.Info(message);
			OnModelLoadProgress?.Invoke(message, current, total);
		}

		public void Dispose()
		{
			if (_disposed) return;
			_disposed = true;

			FrontBoxBreakModel?.Dispose();
			EndFaceUpperModel?.Dispose();
			EndFaceLowerModel?.Dispose();
			BackBarcodeModel?.Dispose();
			BackHookModel?.Dispose();
			HookSlightModel?.Dispose();

			Logger.Info("AI模型管理器已释放");
		}

		private Vimo LoadVimoModel(string modelPath, string name, string moduleId = "0")
		/// <summary>加载ViMo模型: Init(优先GPU, 失败自动降级CPU)→返回Vimo实例或null, 日志记录加载结果和GPU/CPU状态</summary>
		{
			if (string.IsNullOrEmpty(modelPath)) return null;
			string fullPath = _config.GetFullPath(modelPath);
			if (!File.Exists(fullPath)) { Logger.Warning("背面日期码" + name + "模型文件不存在: " + fullPath); return null; }
			// 优先GPU，失败自动降级CPU
			Vimo v = new Vimo();
			int ret = v.Init(fullPath, true, _config.VimoGpuDeviceId, moduleId);
			if (ret == 0) { Logger.Info("背面日期码" + name + "模型加载成功(GPU" + _config.VimoGpuDeviceId + ")"); return v; }
			Logger.Warning("背面日期码" + name + "GPU加载失败, 降级CPU: " + v.ErrorInfo);
			v = new Vimo();
			ret = v.Init(fullPath, false, _config.VimoGpuDeviceId, moduleId);
			if (ret == 0) { Logger.Info("背面日期码" + name + "模型加载成功(CPU)"); return v; }
			Logger.Error("背面日期码" + name + "模型加载失败: " + v.ErrorInfo);
			return v;
		}
	}
}
