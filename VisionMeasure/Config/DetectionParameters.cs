using Newtonsoft.Json;
using System;
using System.Globalization;
using System.IO;
using System.Text;
using VisionMeasure.Utils;using CommonLib;
using CommonLib;

namespace Config
{
	/// <summary>
	/// 检测参数配置 - JSON格式存储
	/// </summary>
	public class DetectionParameters
	{
		private static DetectionParameters _instance;
		private static readonly object _lock = new object();
		private string _configPath;

		// 版本号
		public string Version { get; set; } = "1.0";
	[JsonProperty("上次SKU")] public string LastSkuNumber { get; set; } = "";

		// 最后修改时间
		public DateTime LastModified { get; set; } = DateTime.Now;

		// ========== 正面检测参数 ==========
		[JsonProperty("正面检测参数")]
		public FrontParams Front { get; set; } = new FrontParams();

		// ========== 端面检测参数 ==========
		[JsonProperty("端面检测参数")]
		public EndFaceParams EndFace { get; set; } = new EndFaceParams();

		// ========== 背面检测参数 ==========
		[JsonProperty("背面检测参数")]
		public BackParams Back { get; set; } = new BackParams();

		// ========== 侧面检测参数 ==========
		[JsonProperty("侧面检测参数")]
		public SideParams Side { get; set; } = new SideParams();

		// ========== 相机参数 ==========
		[JsonProperty("相机参数")]
		public CameraParams Camera { get; set; } = new CameraParams();

		// ========== 运动控制参数 ==========
		[JsonProperty("运动控制参数")]
		public MotionParams Motion { get; set; } = new MotionParams();

		// ========== 图像保存参数 ==========
		[JsonProperty("图像保存参数")]
		public SaveParams Save { get; set; } = new SaveParams();

		// ========== 工位参数 ==========
		[JsonProperty("工位参数")]
		public StationParams Station { get; set; } = new StationParams();

		#region 参数子类

		public class FrontParams
		{
			[JsonProperty("置信度阈值")] public float ConfThreshold { get; set; } = 0.25f;
			[JsonProperty("IOU阈值")] public float IouThreshold { get; set; } = 0.45f;
			[JsonProperty("P号码OCR阈值")] public float PCodeConfThreshold { get; set; } = 0.5f;
			[JsonProperty("盒子破检测阈值")] public float BoxBreakConfThreshold { get; set; } = 0.3f;
			[JsonProperty("薄膜破检测阈值")] public float FilmBreakConfThreshold { get; set; } = 0.25f;
			[JsonProperty("显示OK数")] public long OkCount { get; set; } = 0;
			[JsonProperty("显示NG数")] public long NgCount { get; set; } = 0;
			[JsonProperty("良率")] public double YieldRate { get; set; } = 0;
		}

		public class EndFaceParams
		{
			[JsonProperty("上端面置信度阈值")] public float UpperConfThreshold { get; set; } = 0.5f;
			[JsonProperty("上端面IOU阈值")] public float UpperIouThreshold { get; set; } = 0.2f;
			[JsonProperty("下端面置信度阈值")] public float LowerConfThreshold { get; set; } = 0.5f;
			[JsonProperty("下端面IOU阈值")] public float LowerIouThreshold { get; set; } = 0.2f;
			[JsonProperty("曝光时间(ms)")] public int ExposureMs { get; set; } = 20;
			[JsonProperty("飞拍延时(ms)")] public int FlyDelayMs { get; set; } = 10;
			[JsonProperty("显示OK数")] public long OkCount { get; set; } = 0;
			[JsonProperty("显示NG数")] public long NgCount { get; set; } = 0;
			[JsonProperty("良率")] public double YieldRate { get; set; } = 0;
		}

		public class BackParams
		{
			[JsonProperty("置信度阈值")] public float ConfThreshold { get; set; } = 0.5f;
			[JsonProperty("IOU阈值")] public float IouThreshold { get; set; } = 0.2f;
			[JsonProperty("条形码识别阈值")] public float BarcodeConfThreshold { get; set; } = 0.5f;
			[JsonProperty("日期码识别阈值")] public float DateCodeConfThreshold { get; set; } = 0.5f;
			[JsonProperty("挂钩厚度阈值(px)")] public float HookThicknessThreshold { get; set; } = 30.0f;
			[JsonProperty("挂钩内圈类别ID")] public int HookBlueAreaClassId { get; set; } = 0;
			[JsonProperty("挂钩外圈类别ID")] public int HookHangHoleClassId { get; set; } = 1;
			[JsonProperty("显示OK数")] public long OkCount { get; set; } = 0;
			[JsonProperty("显示NG数")] public long NgCount { get; set; } = 0;
			[JsonProperty("良率")] public double YieldRate { get; set; } = 0;
		}

		public class SideParams
		{
			[JsonProperty("裁剪比例(宽/高)")] public float CropRatio { get; set; } = 2.0f;
			[JsonProperty("置信度阈值")] public float ConfThreshold { get; set; } = 0.5f;
			[JsonProperty("IOU阈值")] public float IouThreshold { get; set; } = 0.45f;
			[JsonProperty("缺少图片判NG")] public bool MissingAsNg { get; set; } = true;
			[JsonProperty("连续运动模式")] public bool UseContinuousMode { get; set; } = false;
			[JsonProperty("触发边缘模式")] public string TriggerEdgeMode { get; set; } = "RisingLeftFallingRight";
			[JsonProperty("显示OK数")] public long OkCount { get; set; } = 0;
			[JsonProperty("显示NG数")] public long NgCount { get; set; } = 0;
			[JsonProperty("良率")] public double YieldRate { get; set; } = 0;
		}

		public class CameraParams
		{
			[JsonProperty("触发脉冲宽度(ms)")] public int PulseWidthMs { get; set; } = 50;
			[JsonProperty("相机1序列号")] public string Camera1SN { get; set; } = "FC79423AAK00087";
			[JsonProperty("相机2序列号")] public string Camera2SN { get; set; } = "FC79423AAK00060";
			[JsonProperty("相机3序列号")] public string Camera3SN { get; set; } = "EM57578AAK00046";
			[JsonProperty("相机4序列号")] public string Camera4SN { get; set; } = "EM57578AAK00010";
			[JsonProperty("相机5序列号")] public string Camera5SN { get; set; } = "FB12474AAK00017";
			[JsonProperty("相机6序列号")] public string Camera6SN { get; set; } = "FB12474AAK00006";
			[JsonProperty("相机7序列号")] public string Camera7SN { get; set; } = "EK64117CAK00003";
			[JsonProperty("相机8序列号")] public string Camera8SN { get; set; } = "EK64117CAK00004";
			[JsonProperty("相机1曝光时间(us)")] public double ExposureTime1 { get; set; } = 5000;
			[JsonProperty("相机2曝光时间(us)")] public double ExposureTime2 { get; set; } = 5000;
			[JsonProperty("相机3曝光时间(us)")] public double ExposureTime3 { get; set; } = 5000;
			[JsonProperty("相机4曝光时间(us)")] public double ExposureTime4 { get; set; } = 5000;
			[JsonProperty("相机5曝光时间(us)")] public double ExposureTime5 { get; set; } = 5000;
			[JsonProperty("相机6曝光时间(us)")] public double ExposureTime6 { get; set; } = 5000;
			[JsonProperty("相机7曝光时间(us)")] public double ExposureTime7 { get; set; } = 5000;
			[JsonProperty("相机8曝光时间(us)")] public double ExposureTime8 { get; set; } = 5000;
			[JsonProperty("模拟模式")] public bool SimulateMode { get; set; } = false;

			public bool GetSimulateMode() => SimulateMode;
		}

		public class MotionParams
		{
			[JsonProperty("侧面运动轴起点")] public float SideStartPosition { get; set; } = 0;
			[JsonProperty("侧面运动轴终点")] public float SideEndPosition { get; set; } = 100;
			[JsonProperty("侧面运动速度")] public int SideMoveSpeed { get; set; } = 20;
			[JsonProperty("侧面运动加速度")] public int SideMoveAccel { get; set; } = 10000;
			[JsonProperty("运动控制卡IP")] public string ControlIp { get; set; } = "192.168.0.11";
			[JsonProperty("回零速度")] public float HomeSpeed { get; set; } = 20;
			[JsonProperty("回零加速度")] public float HomeAccel { get; set; } = 10000;
		}

		public class SaveParams
		{
			[JsonProperty("保存OK渲染图")] public bool SaveOkImage { get; set; } = true;
			[JsonProperty("保存NG渲染图")] public bool SaveNgImage { get; set; } = true;
			[JsonProperty("保存OK原图")] public bool SaveOkRawImage { get; set; } = false;
			[JsonProperty("保存NG原图")] public bool SaveNgRawImage { get; set; } = true;
			[JsonProperty("JPEG压缩质量")] public int JpegQuality { get; set; } = 85;
			[JsonProperty("图片保存路径")] public string ImageSavePath { get; set; } = @".\Images";
			[JsonProperty("保留天数")] public int RetentionDays { get; set; } = 7;
		}

		public class StationParams
		{
			[JsonProperty("正面工位启用")] public bool FrontEnabled { get; set; } = true;
			[JsonProperty("端面工位启用")] public bool EndFaceEnabled { get; set; } = true;
			[JsonProperty("背面工位启用")] public bool BackEnabled { get; set; } = true;
			[JsonProperty("侧面工位启用")] public bool SideEnabled { get; set; } = true;
			[JsonProperty("输入IN4端口")] public int InPortFront { get; set; } = 4;
			[JsonProperty("输入IN10端口")] public int InPortEndFace { get; set; } = 10;
			[JsonProperty("输入IN11端口")] public int InPortBack { get; set; } = 11;
			[JsonProperty("输入IN12端口")] public int InPortSideTrigger { get; set; } = 12;
			[JsonProperty("输入IN13端口")] public int InPortSideReady { get; set; } = 13;
			[JsonProperty("正面反转盒序")] public bool FrontReverseBox { get; set; } = false;
			[JsonProperty("背面反转盒序")] public bool BackReverseBox { get; set; } = false;
			[JsonProperty("端面反转盒序")] public bool EndFaceReverseBox { get; set; } = false;
			[JsonProperty("侧面反转盒序")] public bool SideReverseBox { get; set; } = false;
		}

		#endregion

		public static DetectionParameters Instance
		{
			get
			{
				if (_instance == null)
				{
					lock (_lock)
					{
						if (_instance == null)
						{
							string configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "DetectionParams.json");
							_instance = LoadFromFile(configPath);
						}
					}
				}
				return _instance;
			}
		}

		public static DetectionParameters LoadFromFile(string configPath)
		{
			try
			{
				if (File.Exists(configPath))
				{
					string json = File.ReadAllText(configPath, Encoding.UTF8);

					// 使用 InvariantCulture 解析浮点数
					var settings = new JsonSerializerSettings
					{
						Culture = CultureInfo.InvariantCulture,
						FloatParseHandling = FloatParseHandling.Double
					};

					var params_ = JsonConvert.DeserializeObject<DetectionParameters>(json, settings);
					if (params_ != null)
					{
						params_._configPath = configPath;
						Logger.Info($"检测参数加载成功: {configPath}");
						return params_;
					}
				}
			}
			catch (Exception ex)
			{
				Logger.Error($"加载检测参数失败: {ex.Message}");
			}

			var defaultParams = new DetectionParameters { _configPath = configPath };
			defaultParams.SaveToFile();
			return defaultParams;
		}

		public void SaveToFile()
		{
			lock (_lock)
			{
				try
				{
					LastModified = DateTime.Now;

					string directory = Path.GetDirectoryName(_configPath);
					if (!Directory.Exists(directory))
						Directory.CreateDirectory(directory);

					string json = JsonConvert.SerializeObject(this, Formatting.Indented);
					File.WriteAllText(_configPath, json, Encoding.UTF8);

					Logger.Info($"检测参数保存成功: {_configPath}");
				}
				catch (Exception ex)
				{
					Logger.Error($"保存检测参数失败: {ex.Message}");
				}
			}
		}

		public void ResetToDefault()
		{
			Front = new FrontParams();
			EndFace = new EndFaceParams();
			Back = new BackParams();
			Side = new SideParams();
			Camera = new CameraParams();
			Motion = new MotionParams();
			Save = new SaveParams();
			Station = new StationParams();
			Version = "1.0";
			LastModified = DateTime.Now;
			SaveToFile();
		}

		public string ExportToJson()
		{
			return JsonConvert.SerializeObject(this, Formatting.Indented);
		}

		public bool ImportFromJson(string json)
		{
			try
			{
				var imported = JsonConvert.DeserializeObject<DetectionParameters>(json);
				if (imported != null)
				{
					this.Front = imported.Front;
					this.EndFace = imported.EndFace;
					this.Back = imported.Back;
					this.Side = imported.Side;
					this.Camera = imported.Camera;
					this.Motion = imported.Motion;
					this.Save = imported.Save;
					this.Station = imported.Station;
					this.Version = imported.Version;
					this.LastModified = DateTime.Now;
					SaveToFile();
					return true;
				}
			}
			catch (Exception ex)
			{
				Logger.Error($"导入参数失败: {ex.Message}");
			}
			return false;
		}
	}
}