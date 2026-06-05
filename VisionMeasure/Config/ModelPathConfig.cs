using System;
using System.IO;
using static CommonLib.Class_Config;

namespace Config
{
/// <summary>AI模型路径配置 — 从setup.ini[AI_Models]段读取11个模型路径+GPU设备ID, GetFullPath拼接根目录</summary>
	public class ModelPathConfig
	{
		public string ModelRootPath { get; set; } = @".\AI\Models";

		// ========== 正面模型 ==========
		public string FrontPCodeOcrModel { get; set; }
		public string FrontPCodeOcrModuleId { get; set; } = "3";  // Vimo模型的moduleId
		public string FrontBoxBreakModel { get; set; }
		public string FrontFilmBreakModel { get; set; }

		// ========== 端面模型 ==========
		public string EndFaceUpperModel { get; set; }
		public string EndFaceLowerModel { get; set; }

		// ========== 背面模型 ==========
		public string BackBarcodeModel { get; set; }
		public string BackDateCodeSegModel { get; set; }
		public string BackDateCodeClsModel { get; set; }
		public string BackDateCodeOcrModel { get; set; }
		public string BackHookDamageModel { get; set; }
		public string BackHookSlightModel { get; set; }
		public string BackCutCharModel { get; set; }
		public string BackCutCharModuleId { get; set; } = "0";     // Vimo模型的moduleId

		// ========== 侧面模型 ==========
		public string SideDefectModel { get; set; }
		public string SideDefectModuleId { get; set; } = "0";       // Vimo模型的moduleId

		// ========== 全局GPU配置 ==========
		public bool UseGpu { get; set; } = true;
		public int DefaultGpuDeviceId { get; set; } = 0;

		// ========== Vimo模型专用GPU (背面日期码、正面P号码、切字等) ==========
		public int VimoGpuDeviceId { get; set; } = 1;     // Vimo模型用显卡1

		// ========== Yolo模型专用GPU (盒子破、挂钩、端面、侧面等) ==========
		public int YoloGpuDeviceId { get; set; } = 0;     // Yolo模型用显卡0

		/// <summary>拼接完整模型路径: ModelRootPath + 相对路径 → 绝对路径</summary>
		public string GetFullPath(string modelFile)
		{
			if (string.IsNullOrEmpty(modelFile)) return null;
			if (Path.IsPathRooted(modelFile)) return modelFile;
			return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ModelRootPath, modelFile);
		}

		/// <summary>从setup.ini [AI_Models]段加载所有模型路径+GPU配置 → 返回ModelPathConfig</summary>
		public static ModelPathConfig LoadFromSysConfig()
		{
			var config = new ModelPathConfig();

			config.ModelRootPath = _Config.ModelRootPath ?? @".\AI\Models";

			// 正面模型
			config.FrontPCodeOcrModel = _Config.FrontPCodeOcrModel ?? @"正面\P号码识别\model.vimosln";
			config.FrontPCodeOcrModuleId = _Config.FrontPCodeOcrModuleId ?? "3";
			config.FrontBoxBreakModel = _Config.FrontBoxBreakModel ?? @"正面\盒子破检测\best.onnx";
			config.FrontFilmBreakModel = _Config.FrontFilmBreakModel ?? @"正面\薄膜破检测\best.onnx";

			// 端面模型
			config.EndFaceUpperModel = _Config.EndFaceUpperModel ?? @"端面\上端面\best.onnx";
			config.EndFaceLowerModel = _Config.EndFaceLowerModel ?? @"端面\下端面\best.onnx";

			// 背面模型
			config.BackBarcodeModel = _Config.BackBarcodeModel ?? @"背面\条形码识别\best.onnx";
			config.BackDateCodeSegModel = null ?? @"背面\日期码识别\分割模型\model.vimosln";
			config.BackDateCodeClsModel = null ?? @"背面\日期码识别\分类模型\model.vimosln";
			config.BackDateCodeOcrModel = null ?? @"背面\日期码识别\OCR识别\model.vimosln";
			config.BackHookDamageModel = _Config.BackHookDamageModel ?? @"背面\明显挂钩错位\best.onnx";
			config.BackHookSlightModel = _Config.BackHookSlightModel ?? @"背面\轻微挂钩错位\best.onnx";
			config.BackCutCharModel = _Config.BackCutCharModel ?? @"背面\切字识别\model.vimosln";
			config.BackCutCharModuleId = _Config.BackCutCharModuleId ?? "0";

			// 侧面模型
			config.SideDefectModel = _Config.SideDefectModel ?? @"侧面\缺陷检测\best.onnx";
			config.SideDefectModuleId = _Config.SideDefectModuleId ?? "0";

			// GPU配置
			config.UseGpu = _Config.UseGpu;
			config.DefaultGpuDeviceId = _Config.DefaultGpuDeviceId;
			config.VimoGpuDeviceId = _Config.VimoGpuDeviceId;
			config.YoloGpuDeviceId = _Config.YoloGpuDeviceId;

			return config;
		}

		/// <summary>保存模型路径配置回setup.ini → 反向写入各字段</summary>
		public void SaveToConfig()
		{
			_Config.ModelRootPath = ModelRootPath;
			_Config.FrontPCodeOcrModel = FrontPCodeOcrModel;
			_Config.FrontPCodeOcrModuleId = FrontPCodeOcrModuleId;
			_Config.FrontBoxBreakModel = FrontBoxBreakModel;
			_Config.FrontFilmBreakModel = FrontFilmBreakModel;
			_Config.EndFaceUpperModel = EndFaceUpperModel;
			_Config.EndFaceLowerModel = EndFaceLowerModel;
			_Config.BackBarcodeModel = BackBarcodeModel;
			_Config.BackHookDamageModel = BackHookDamageModel;
			_Config.BackHookSlightModel = BackHookSlightModel;
			_Config.BackCutCharModel = BackCutCharModel;
			_Config.BackCutCharModuleId = BackCutCharModuleId;
			_Config.SideDefectModel = SideDefectModel;
			_Config.SideDefectModuleId = SideDefectModuleId;
			_Config.UseGpu = UseGpu;
			_Config.DefaultGpuDeviceId = DefaultGpuDeviceId;
			_Config.VimoGpuDeviceId = VimoGpuDeviceId;
			_Config.YoloGpuDeviceId = YoloGpuDeviceId;
		}
	}
}