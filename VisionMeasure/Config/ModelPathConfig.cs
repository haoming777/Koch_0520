using System;
using System.IO;
using static CommonLib.Class_Config;

namespace Config
{
	public class ModelPathConfig
	{
		public string ModelRootPath { get; set; } = @".\AI\Models";

		// ========== 正面模型 ==========
		public string FrontPCodeOcrModel { get; set; }
		public string FrontBoxBreakModel { get; set; }
		public string FrontFilmBreakModel { get; set; }

		// ========== 端面模型 ==========
		public string EndFaceUpperModel { get; set; }
		public string EndFaceLowerModel { get; set; }

		// ========== 背面模型 ==========
		public string BackBarcodeModel { get; set; }
		public string BackDateCodeModel { get; set; }
		public string BackHookDamageModel { get; set; }
		public string BackHookSlightModel { get; set; }
		public string BackCutCharModel { get; set; }

		// ========== 侧面模型 ==========
		public string SideDefectModel { get; set; }

		// ========== 全局GPU配置 ==========
		public bool UseGpu { get; set; } = true;
		public int DefaultGpuDeviceId { get; set; } = 0;

		// ========== Vimo模型专用GPU (背面日期码、正面P号码、切字等) ==========
		public int VimoGpuDeviceId { get; set; } = 1;     // Vimo模型用显卡1

		// ========== Yolo模型专用GPU (盒子破、挂钩、端面、侧面等) ==========
		public int YoloGpuDeviceId { get; set; } = 0;     // Yolo模型用显卡0

		public string GetFullPath(string modelFile)
		{
			if (string.IsNullOrEmpty(modelFile)) return null;
			if (Path.IsPathRooted(modelFile)) return modelFile;
			return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, ModelRootPath, modelFile);
		}

		public static ModelPathConfig LoadFromSysConfig()
		{
			var config = new ModelPathConfig();

			config.ModelRootPath = _Config.ModelRootPath ?? @".\AI\Models";

			// 正面模型
			config.FrontPCodeOcrModel = _Config.FrontPCodeOcrModel ?? @"正面\P号码识别\model.onnx";
			config.FrontBoxBreakModel = _Config.FrontBoxBreakModel ?? @"正面\盒子破检测\model.onnx";
			config.FrontFilmBreakModel = _Config.FrontFilmBreakModel ?? @"正面\薄膜破检测\model.onnx";

			// 端面模型
			config.EndFaceUpperModel = _Config.EndFaceUpperModel ?? @"端面\上端面\model.onnx";
			config.EndFaceLowerModel = _Config.EndFaceLowerModel ?? @"端面\下端面\model.onnx";

			// 背面模型
			config.BackBarcodeModel = _Config.BackBarcodeModel ?? @"背面\条形码识别\model.onnx";
			config.BackDateCodeModel = _Config.BackDateCodeModel ?? @"背面\日期码识别\model.onnx";
			config.BackHookDamageModel = _Config.BackHookDamageModel ?? @"背面\明显挂钩错位\model.onnx";
			config.BackHookSlightModel = _Config.BackHookSlightModel ?? @"背面\轻微挂钩错位\model.onnx";
			config.BackCutCharModel = _Config.BackCutCharModel ?? @"背面\切字识别\model.onnx";

			// 侧面模型
			config.SideDefectModel = _Config.SideDefectModel ?? @"侧面\缺陷检测\model.onnx";

			// GPU配置
			config.UseGpu = _Config.UseGpu;
			config.DefaultGpuDeviceId = _Config.DefaultGpuDeviceId;
			config.VimoGpuDeviceId = _Config.VimoGpuDeviceId;
			config.YoloGpuDeviceId = _Config.YoloGpuDeviceId;

			return config;
		}

		public void SaveToConfig()
		{
			_Config.ModelRootPath = ModelRootPath;
			_Config.FrontPCodeOcrModel = FrontPCodeOcrModel;
			_Config.FrontBoxBreakModel = FrontBoxBreakModel;
			_Config.FrontFilmBreakModel = FrontFilmBreakModel;
			_Config.EndFaceUpperModel = EndFaceUpperModel;
			_Config.EndFaceLowerModel = EndFaceLowerModel;
			_Config.BackBarcodeModel = BackBarcodeModel;
			_Config.BackDateCodeModel = BackDateCodeModel;
			_Config.BackHookDamageModel = BackHookDamageModel;
			_Config.BackHookSlightModel = BackHookSlightModel;
			_Config.BackCutCharModel = BackCutCharModel;
			_Config.SideDefectModel = SideDefectModel;
			_Config.UseGpu = UseGpu;
			_Config.DefaultGpuDeviceId = DefaultGpuDeviceId;
			_Config.VimoGpuDeviceId = VimoGpuDeviceId;
			_Config.YoloGpuDeviceId = YoloGpuDeviceId;
		}
	}
}