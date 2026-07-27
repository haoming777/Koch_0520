using OpenCvSharp;
using SmartMore.ViMo;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using XL.Tool;
using VisionMeasure.Utils;using CommonLib;                // 工具类
using VisionMeasure.Utils;using CommonLib;

namespace AIsdk
{
/// <summary>SmartMore ViMo推理封装 — OCR/分割/分类模型(.vimosln), Init→Run, 支持GPU/CPU</summary>
	public class Vimo : IDisposable
	{
		private const int ERROR_OK = 0;
		private const int ERROR_FAILED = -1;
		private int returnValue = 0;
		public string ErrorInfo = "";
		private string modelsPath = "";
		private string modelID = "";
		private bool useGpu = false;
		private int deviceId = 0;
		private int SegmentArea = 0;
		Stopwatch stopwatch = new Stopwatch();
		XLToolClass toolClass = new XLToolClass();
		public ModuleType moduleType { get; set; }

		IPipelines pipelines1;
		Solution solution;
		IOcrModule module;
		ISegmentationModule module_segmentation;
		IClassificationModule module_class;
		/// <summary>无参构造, 需后续调用Init加载模型</summary>
		public Vimo()
		{
		}

		/// <summary>构造并初始化: 传modelPath+useGpu+deviceId+moduleId → Init加载模型</summary>
		public Vimo(string modelPath, bool useGpu, int deviceId, string moduleId)
		{
			this.modelsPath = modelPath;
			this.useGpu = useGpu;
			this.deviceId = deviceId;
			this.modelID = moduleId;
		}


		/// <summary>初始化ViMo检测模型(.vimosln) → GPU优先, 失败自动降级CPU</summary>
		public int Init(string modelPath, bool usegup, int deviceid, string modelid)
		{
			try
			{

				Logger.Info($"Init加载");
				modelsPath = modelPath;
				useGpu = usegup;
				deviceId = deviceid;
				modelID = modelid;
				solution = new Solution();
				solution.LoadFromFile(modelsPath);
				pipelines1 = solution.CreatePipelines(modelID, useGpu, deviceId);
				moduleType = solution.GetModuleInfo(modelID).Type;
				Logger.Info($"Init加载完成");
				return ERROR_OK;
			}
			catch (Exception ex)
			{
				Logger.Info($"Init加载时时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
				ErrorInfo = ex.ToString();
				return ERROR_FAILED;
			}
		}

		/// <summary>初始化ViMo分割模型 → 用于日期码C1分割步骤</summary>
		public int Init_Segmentation(string modelPath, bool usegup, int deviceid, string modelid)
		{
			try
			{
				Logger.Info("Init_Segmentation加载");
				modelsPath = modelPath;
				useGpu = usegup;
				deviceId = deviceid;
				modelID = modelid;
				solution = new Solution();
				solution.LoadFromFile(modelsPath);
				//pipelines1 = solution.CreatePipelines(modelID, useGpu, deviceId);
				//moduleType = solution.GetModuleInfo(modelID).Type;
				module_segmentation = solution.CreateModule<ISegmentationModule>(modelID, useGpu, deviceId);
				Logger.Info("Init_Segmentation加载完成");
				return ERROR_OK;
			}
			catch (Exception ex)
			{
				Logger.Info($"Init_Segmentation加载时时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
				ErrorInfo = ex.ToString();
				return ERROR_FAILED;
			}
		}

		/// <summary>初始化ViMo有序OCR模型 → 用于日期码C3识别步骤</summary>
		public int Init_OrderOcr(string modelPath, bool usegup, int deviceid, string modelid)
		{
			try
			{
				Logger.Info("Init_OrderOcr加载");
				modelsPath = modelPath;
				useGpu = usegup;
				deviceId = deviceid;
				modelID = modelid;
				solution = new Solution();
				solution.LoadFromFile(modelsPath);
				//pipelines1 = solution.CreatePipelines(modelID, useGpu, deviceId);
				//moduleType = solution.GetModuleInfo(modelID).Type;
				module = solution.CreateModule<IOcrModule>(modelID, useGpu, deviceId);
				var ocrParams = module.Params;
				ocrParams.Order = OutputOrder.LeftToRight;
				module.Params = ocrParams;
				Logger.Info("Init_OrderOcr加载完成");
				return ERROR_OK;
			}
			catch (Exception ex)
			{
				Logger.Info($"Init_OrderOcr加载时时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
				ErrorInfo = ex.ToString();
				return ERROR_FAILED;
			}
		}

		/// <summary>初始化ViMo分类模型 → 用于日期码C2重影分类</summary>
		public int Init_Class(string modelPath, bool usegup, int deviceid, string modelid)
		{
			try
			{
				Logger.Info("Init_Class加载");
				modelsPath = modelPath;
				useGpu = usegup;
				deviceId = deviceid;
				modelID = modelid;
				solution = new Solution();
				solution.LoadFromFile(modelsPath);
				module_class = solution.CreateModule<IClassificationModule>(modelID, useGpu, deviceId);
				Logger.Info("Init_Class加载完成");
				return ERROR_OK;
			}
			catch (Exception ex)
			{
				Logger.Info($"Init_Class加载时时发生异常...\r\n {ex.Message} \r\n {ex.StackTrace}");
				ErrorInfo = ex.ToString();
				return ERROR_FAILED;
			}
		}

		/// <summary>
		/// SDK Run接口
		/// </summary>
		/// <param name="image"></param>
		/// <param name="LabelImage"></param>
		/// <param name="defects"></param>
		/// <returns></returns>
		/// <summary>运行目标检测推理 → 输出DetectionResponse列表(Box+ClassId+Score)</summary>
		public int Run(Mat image, out ResponseList<DetectionResponse> results)
		{
			try
			{
				Mat srcmat = image.Clone();
				var req = new Request(srcmat);
				stopwatch.Restart();
				pipelines1.Run(req, out results);
				Console.WriteLine($"运行SDK耗时{stopwatch.ElapsedMilliseconds}ms");
				stopwatch.Stop();
				srcmat?.Dispose();
				return ERROR_OK;
			}
			catch (Exception ex)
			{
				results = null;
				ErrorInfo = ex.ToString();
				Console.WriteLine(ErrorInfo);

				return ERROR_FAILED;
			}
		}

		/// <summary>
		/// SDK Run接口
		/// </summary>
		/// <param name="image"></param>
		/// <param name="LabelImage"></param>
		/// <param name="defects"></param>
		/// <returns></returns>
		/// <summary>运行分割推理 → 输出SegmentationResponse(Mask+连通域)</summary>
		public int Run(Mat image, out ResponseList<SegmentationResponse> results)
		{
			try
			{
				GC.Collect();

				Mat srcmat = image.Clone();
				var req = new Request(srcmat);
				stopwatch.Restart();

				pipelines1.Run(req, out results);
				Console.WriteLine($"运行SDK耗时{stopwatch.ElapsedMilliseconds}");
				Logger.Info($"运行分割SDK耗时{stopwatch.ElapsedMilliseconds}");

				stopwatch.Stop();
				srcmat?.Dispose();
				return ERROR_OK;
			}
			catch (Exception ex)
			{
				results = null;
				ErrorInfo = ex.ToString();
				Console.WriteLine(ErrorInfo);

				return ERROR_FAILED;
			}
		}

		/// <summary>
		/// SDK Run接口
		/// </summary>
		/// <param name="image"></param>
		/// <param name="LabelImage"></param>
		/// <param name="defects"></param>
		/// <returns></returns>
		/// <summary>运行分类推理 → 输出ClassificationResponse(Label+Score)</summary>
		public int Run(Mat image, out ResponseList<ClassificationResponse> results)
		{
			try
			{
				Mat srcmat = image.Clone();
				var req = new Request(srcmat);
				stopwatch.Restart();
				pipelines1.Run(req, out results);
				Console.WriteLine($"运行SDK耗时{stopwatch.ElapsedMilliseconds}");
				Logger.Info($"运行分类SDK耗时{stopwatch.ElapsedMilliseconds}");
				stopwatch.Stop();
				srcmat?.Dispose();
				return ERROR_OK;
			}
			catch (Exception ex)
			{
				results = null;
				ErrorInfo = ex.ToString();
				Console.WriteLine(ErrorInfo);

				return ERROR_FAILED;
			}
		}
		// <summary>
		/// SDK Run接口
		/// </summary>
		/// <param name="image"></param>
		/// <param name="LabelImage"></param>
		/// <param name="defects"></param>
		/// <returns></returns>
		/// <summary>运行OCR推理 → 输出OcrResponse(Blocks文本+Polygon位置)</summary>
		public int Run(Mat image, out ResponseList<OcrResponse> results)
		{
			try
			{
				Mat srcmat = image.Clone();
				var req = new Request(srcmat);
				stopwatch.Restart();

				pipelines1.Run(req, out results);
				Console.WriteLine($"运行SDK耗时{stopwatch.ElapsedMilliseconds}");
				//Logger.Info($"运行OcrSDK耗时{stopwatch.ElapsedMilliseconds}");
				stopwatch.Stop();
				srcmat?.Dispose();
				return ERROR_OK;
			}
			catch (Exception ex)
			{
				results = null;
				ErrorInfo = ex.ToString();
				Console.WriteLine(ErrorInfo);

				return ERROR_FAILED;
			}
		}

		/// <summary>运行有序OCR(全图) → 输出带顺序的文本识别结果</summary>
		public int Run_OrderOcr(Mat image, out OcrResponse results)
		{
			try
			{
				Mat srcmat = image.Clone();
				var req = new Request(srcmat);
				stopwatch.Restart();

				module.Run(req, out results);
				Console.WriteLine($"运行SDK耗时{stopwatch.ElapsedMilliseconds}");
				//Logger.Info($"运行OcrSDK耗时{stopwatch.ElapsedMilliseconds}");
				stopwatch.Stop();
				srcmat?.Dispose();
				return ERROR_OK;
			}
			catch (Exception ex)
			{
				results = null;
				ErrorInfo = ex.ToString();
				Console.WriteLine(ErrorInfo);

				return ERROR_FAILED;
			}
		}

		/// <summary>运行有序OCR(指定ROI) → 输出带顺序的文本识别结果</summary>
		public int Run_OrderOcr(Mat image, Rect roi, out OcrResponse results)
		{
			try
			{
				Mat srcmat = image.Clone();
				var req = new Request(srcmat, roi);
				stopwatch.Restart();

				module.Run(req, out results);
				//Console.WriteLine($"运行SDK耗时{stopwatch.ElapsedMilliseconds}");
				//Logger.Info($"运行OcrSDK耗时{stopwatch.ElapsedMilliseconds}");
				stopwatch.Stop();
				srcmat?.Dispose();
				return ERROR_OK;
			}
			catch (Exception ex)
			{
				results = null;
				ErrorInfo = ex.ToString();
				Console.WriteLine(ErrorInfo);

				return ERROR_FAILED;
			}
		}
		public OcrResponse RunOrderOcr(Mat image)
		{
			if (module == null) return null;
			OcrResponse results;
			int ret = Run_OrderOcr(image, out results);
			return ret == 0 ? results : null;
		}
		public int Run_Segmentation(Mat image, out SegmentationResponse results)
		{
			try
			{
				Mat srcmat = image.Clone();
				var req = new Request(srcmat);
				stopwatch.Restart();

				module_segmentation.Run(req, out results);
				Console.WriteLine($"运行SDK耗时{stopwatch.ElapsedMilliseconds}");
				Logger.Info($"运行Run_Segmentation耗时{stopwatch.ElapsedMilliseconds}");
				stopwatch.Stop();
				srcmat?.Dispose();
				return ERROR_OK;
			}
			catch (Exception ex)
			{
				results = null;
				ErrorInfo = ex.ToString();
				Console.WriteLine(ErrorInfo);

				return ERROR_FAILED;
			}
		}
		public int Run_Segmentation(Mat image,Rect roi, out SegmentationResponse results)
		{
			try
			{
				Mat srcmat = image.Clone();
				var req = new Request(srcmat, roi);
				stopwatch.Restart();
				module_segmentation.Run(req, out results);
				Console.WriteLine($"运行SDK耗时{stopwatch.ElapsedMilliseconds}");
				Logger.Info($"运行Run_Color耗时{stopwatch.ElapsedMilliseconds}");
				stopwatch.Stop();
				srcmat?.Dispose();
				return ERROR_OK;
			}
			catch (Exception ex)
			{
				results = null;
				ErrorInfo = ex.ToString();
				Console.WriteLine(ErrorInfo);

				return ERROR_FAILED;
			}
		}

		public int Run_Class(Mat image, Rect roi, out ClassificationResponse results)
		{
			try
			{
				Mat srcmat = image.Clone();
				var req = new Request(srcmat, roi);
				stopwatch.Restart();

				module_class.Run(req, out results);
				Console.WriteLine($"运行SDK耗时{stopwatch.ElapsedMilliseconds}");
				Logger.Info($"运行Run_Class耗时{stopwatch.ElapsedMilliseconds}");
				stopwatch.Stop();
				srcmat?.Dispose();
				return ERROR_OK;
			}
			catch (Exception ex)
			{
				results = null;
				ErrorInfo = ex.ToString();
				Console.WriteLine(ErrorInfo);

				return ERROR_FAILED;
			}
		}

		/// <summary>
		/// Mat格式转为Bitmap
		/// </summary>
		/// <param name="mat"></param>
		/// <returns></returns>
		public void Dispose()
		{
			try
			{
				(pipelines1 as IDisposable)?.Dispose();
				(module as IDisposable)?.Dispose();
				(module_segmentation as IDisposable)?.Dispose();
				(module_class as IDisposable)?.Dispose();
				(solution as IDisposable)?.Dispose();
			}
			catch { }
			pipelines1 = null;
			module = null;
			module_segmentation = null;
			module_class = null;
			solution = null;
		}

		private Bitmap Visualize(Mat mat)
		{
			Bitmap bitmap = new Bitmap(mat.Cols, mat.Rows, (int)mat.Step(), PixelFormat.Format24bppRgb, mat.Data);
			return bitmap;
		}
	}
}
