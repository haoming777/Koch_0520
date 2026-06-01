using Newtonsoft.Json;
using System;
using System.IO;

namespace Config
{
	/// <summary>单个模型的参数配置（每个模型独立文件）</summary>
	public class ModelParams
	{
		public string ModelName { get; set; } = "";
		public string ModelKey { get; set; } = "";

		// —— YOLO 通用 ——
		public float Confidence { get; set; } = 0.5f;
		public float Iou { get; set; } = 0.45f;
		public float CropRatio { get; set; } = 0.33f;

		// —— 条码预处理 (对照参考: PreprocessConfig) ——
		public bool BcEnablePreprocess { get; set; } = true;
		[Newtonsoft.Json.JsonIgnore] public bool EnablePreprocess { get=>BcEnablePreprocess; set=>BcEnablePreprocess=value; }
		[Newtonsoft.Json.JsonIgnore] public float ContrastAlpha { get=>BcContrastAlpha; set=>BcContrastAlpha=value; }
		[Newtonsoft.Json.JsonIgnore] public int BrightnessBeta { get=>BcBrightnessBeta; set=>BcBrightnessBeta=value; }
		[Newtonsoft.Json.JsonIgnore] public bool EnableGaussianBlur { get=>BcEnableGaussianBlur; set=>BcEnableGaussianBlur=value; }
		[Newtonsoft.Json.JsonIgnore] public bool EnableMedianBlur { get=>BcEnableMedianBlur; set=>BcEnableMedianBlur=value; }
		[Newtonsoft.Json.JsonIgnore] public bool EnableEqualizeHist { get=>BcEnableEqualizeHist; set=>BcEnableEqualizeHist=value; }
		[Newtonsoft.Json.JsonIgnore] public bool EnableInvert { get=>BcEnableInvert; set=>BcEnableInvert=value; }
		[Newtonsoft.Json.JsonIgnore] public int ThresholdMode { get=>BcThresholdMode; set=>BcThresholdMode=value; }
		[Newtonsoft.Json.JsonIgnore] public int AdaptiveBlockSize { get=>BcAdaptiveBlockSize; set=>BcAdaptiveBlockSize=value; }
		[Newtonsoft.Json.JsonIgnore] public double AdaptiveC { get=>BcAdaptiveC; set=>BcAdaptiveC=value; }
		[Newtonsoft.Json.JsonIgnore] public int FixedThreshold { get=>BcFixedThreshold; set=>BcFixedThreshold=value; }
		[Newtonsoft.Json.JsonIgnore] public bool EnableMorphClose { get=>BcEnableMorphClose; set=>BcEnableMorphClose=value; }
		[Newtonsoft.Json.JsonIgnore] public bool EnableMorphOpen { get=>BcEnableMorphOpen; set=>BcEnableMorphOpen=value; }
		[Newtonsoft.Json.JsonIgnore] public bool EnableMorphDilate { get=>BcEnableMorphDilate; set=>BcEnableMorphDilate=value; }
		[Newtonsoft.Json.JsonIgnore] public bool EnableMorphErode { get=>BcEnableMorphErode; set=>BcEnableMorphErode=value; }
		[Newtonsoft.Json.JsonIgnore] public double StartHeightRatio { get=>BcStartHeightRatio; set=>BcStartHeightRatio=value; }
		[Newtonsoft.Json.JsonIgnore] public bool EnableFilterBestMatch { get=>BcEnableFilterBestMatch; set=>BcEnableFilterBestMatch=value; }
		[Newtonsoft.Json.JsonIgnore] public int MinBarcodeLength { get=>BcMinBarcodeLength; set=>BcMinBarcodeLength=value; }
		[Newtonsoft.Json.JsonIgnore] public int MaxBarcodeLength { get=>BcMaxBarcodeLength; set=>BcMaxBarcodeLength=value; }
		[Newtonsoft.Json.JsonIgnore] public bool TryHarder { get=>BcTryHarder; set=>BcTryHarder=value; }
		[Newtonsoft.Json.JsonIgnore] public bool EnableRotationRetry { get=>BcEnableRotationRetry; set=>BcEnableRotationRetry=value; }
		public float BcContrastAlpha { get; set; } = 1.0f;
		public int BcBrightnessBeta { get; set; } = 0;
		public bool BcEnableGaussianBlur { get; set; } = false;
		public bool BcEnableMedianBlur { get; set; } = false;
		public bool BcEnableEqualizeHist { get; set; } = false;
		public int BcThresholdMode { get; set; } = 0; // 0=None 1=Adaptive 2=Otsu 3=Fixed
		public int BcAdaptiveBlockSize { get; set; } = 11;
		public double BcAdaptiveC { get; set; } = 2.0;
		public int BcFixedThreshold { get; set; } = 128;
		public bool BcEnableInvert { get; set; } = false;
		public bool BcEnableMorphClose { get; set; } = false;
		public bool BcEnableMorphOpen { get; set; } = false;
		public bool BcEnableMorphDilate { get; set; } = false;
		public bool BcEnableMorphErode { get; set; } = false;
		public double BcStartHeightRatio { get; set; } = 2.0 / 3.0;
		public bool BcEnableFilterBestMatch { get; set; } = true;
		public int BcMinBarcodeLength { get; set; } = 3;
		public int BcMaxBarcodeLength { get; set; } = 50;
		public bool BcTryHarder { get; set; } = true;
		public bool BcEnableRotationRetry { get; set; } = true;

		// —— 挂钩 ——
		public float HookThickness { get; set; } = 30f;
		public int HookBlueClassId { get; set; } = 0;
		public int HookHoleClassId { get; set; } = 1;

		// —— 侧面 ——
		public float SideConf { get; set; } = 0.5f;
		public float SideIou { get; set; } = 0.45f;
		public float SideCropRatio { get; set; } = 2.0f;

		// —— 端面 ——
		public float EndFaceUpperConf { get; set; } = 0.5f;
		public float EndFaceUpperIou { get; set; } = 0.2f;
		public float EndFaceLowerConf { get; set; } = 0.5f;
		public float EndFaceLowerIou { get; set; } = 0.2f;

		// —— 绘制字号（0=使用代码默认值，可按模型分别配置） ——
		public int DrawFontBarcode { get; set; } = 28;
		public int DrawFontDefect { get; set; } = 18;
		public int DrawFontStatus { get; set; } = 48;
		public int DrawFontBoxNum { get; set; } = 28;

		// —— 各检测项裁剪起始比例 ——
		public double StartHeightRatioPCode { get; set; } = 2.0 / 3.0;
		public double StartHeightRatioDateCode { get; set; } = 2.0 / 3.0;

		public static ModelParams CreateDefault(string key, string name)
		{
			var m = new ModelParams { ModelKey = key, ModelName = name };
			if (key == "barcode") { /* defaults already set */ }
			else if (key == "hook") { m.Confidence = 0.5f; m.Iou = 0.2f; }
			else if (key == "hook_slight") { m.Confidence = 0.5f; }
			else if (key == "front_box") { m.Confidence = 0.5f; m.Iou = 0.45f; }
			else if (key == "front_pcode") { }
			else if (key == "datecode") { }
			else if (key == "side") { m.Confidence = 0.5f; m.Iou = 0.45f; }
			else if (key == "endface_upper") { m.Confidence = 0.5f; m.Iou = 0.2f; }
			else if (key == "endface_lower") { m.Confidence = 0.5f; m.Iou = 0.2f; }
			return m;
		}

		public static string Dir => Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "ModelParams");
		public string FilePath => Path.Combine(Dir, ModelKey + ".json");

		public void Save()
		{
			try { if (!Directory.Exists(Dir)) Directory.CreateDirectory(Dir); File.WriteAllText(FilePath, JsonConvert.SerializeObject(this, Formatting.Indented)); }
			catch { }
		}
		public static ModelParams Load(string key)
		{
			try { var p = Path.Combine(Dir, key + ".json"); if (File.Exists(p)) return JsonConvert.DeserializeObject<ModelParams>(File.ReadAllText(p)) ?? CreateDefault(key, key); }
			catch { }
			return CreateDefault(key, key);
		}
		public ModelParams Clone() { return (ModelParams)MemberwiseClone(); }
	}
}
