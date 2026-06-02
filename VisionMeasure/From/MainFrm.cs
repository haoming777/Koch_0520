using CommonLib;
using Config;
using Hardware;
using Models;
using MT.Camera.SDK;
using OpenCvSharp;
using PLC调试.Class;
using Stations;
using Sunny.UI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using VisionMeasure.Utils;
using CommonLib;
using VisionMeasure.From;
using XL.Controls;
using static CommonLib.Class_Config;
using BmpConverter = OpenCvSharp.Extensions.BitmapConverter;
// 解决 Point 和 Size 二义性问题 - 使用别名
using CvPoint = OpenCvSharp.Point;
using CvSize = OpenCvSharp.Size;
using DrawPoint = System.Drawing.Point;
using DrawSize = System.Drawing.Size;
using Timer = System.Windows.Forms.Timer;
using VisionMeasure.Stations;  // 引入 FrontStationProcessor 所在的命名空间
using VisionMeasure.Utils;
using CommonLib;     // 引入 SystemConfig 所在的命名空间

namespace VisionMeasure
{
	public partial class MainFrm : Form, ICamera
	{
		/// <summary>手动测试模式：true时停止所有自动触发</summary>
		public static bool ManualTestMode = false;
		/// <summary>各工位启用开关</summary>
		public static bool FrontEnabled = true, BackEnabled = true, EndFaceEnabled = true, SideEnabled = true;
		// ========== 硬件管理层 ==========
		private MotionControlManager _motionMgr;
		private CameraTriggerManager _triggerMgr;
		private PlcCommunication _plcComm;

		// ========== AI模型管理层 ==========
		private AiModelManager _aiModels;

		/// <summary>预加载的AI模型管理器（由Program.cs设置以避免重复加载）</summary>
		public static AiModelManager PreloadedModels { get; set; }

		/// <summary>预加载的SKU数据库</summary>
		public static SkuDatabase PreloadedSkuDb { get; set; }
		private static MotionControlManager _staticMotionMgr;
		public static MotionControlManager GetMotionManager() => _staticMotionMgr;

		// ========== 工位处理器 ==========
		private FrontStationProcessor _frontStation;
		private EndFaceStationProcessor _endFaceStation;
		private BackStationProcessor _backStation;
		private SideStationProcessor _sideStation;
	private volatile bool _sideTriggerPending;

		// ========== 数据管理 ==========
		private SkuDatabase _skuDb;
		private SkuData _currentSku;
		private PerformanceMonitor _perfMonitor;
		private DetectionParameters _detectionParams;
		private SQLiteHelper _dbHelper;

		// ========== 高速保存器 ==========
		private HighSpeedImageSaver _imageSaver;

		// ========== 产品ID计数器 ==========
		private long _productIdCounter = 0;

		// ========== 状态灯控件 ==========
		private List<UILight> _frontStatusLights = new List<UILight>();
		private List<UILight> _backStatusLights = new List<UILight>();
		private List<UILight> _upperStatusLights = new List<UILight>();
		private List<UILight> _lowerStatusLights = new List<UILight>();
		private List<UILight> _leftSideStatusLights = new List<UILight>();
		private List<UILight> _rightSideStatusLights = new List<UILight>();

		// ========== 轮播图索引标签 ==========
		private Label _endFaceIndexLabel;
		private Label _sideIndexLabel;

		// ========== SKU搜索 ==========
		private ComboBox _skuSearchCombo;

		// ========== 班次 ==========
		private string _currentShift = "";
		private DateTime _shiftStartTime;
		private System.Timers.Timer _shiftCheckTimer;

		// ========== 工具类 ==========
		private bool _isClosing = false;
		private Loading _loadingForm;

		// ========== 公共成员（供其他窗体访问）==========
		public IntPtr g_handle = IntPtr.Zero;
		public HCModbusClass modbusClass = new HCModbusClass();

		public DaHuaSDK camera1SDK, camera2SDK, camera3SDK, camera4SDK;
		public DaHuaSDK camera5SDK, camera6SDK, camera7SDK, camera8SDK;

		// ========== SKU搜索 ==========
		private TextBox _skuSearchTextBox;
		private ListBox _skuResultListBox;
		private Panel _skuSearchPanel;

		// ========== 公共访问方法 ==========
		public IntPtr GetMotionHandle() => g_handle;
		public HCModbusClass GetModbusClass() => modbusClass;

		public DaHuaSDK GetCamera1() => camera1SDK;
		public DaHuaSDK GetCamera2() => camera2SDK;
		public DaHuaSDK GetCamera3() => camera3SDK;
		public DaHuaSDK GetCamera4() => camera4SDK;
		public DaHuaSDK GetCamera5() => camera5SDK;
		public DaHuaSDK GetCamera6() => camera6SDK;
		public DaHuaSDK GetCamera7() => camera7SDK;
		public DaHuaSDK GetCamera8() => camera8SDK;

		public MainFrm(Loading loadingForm = null)
		{
			_loadingForm = loadingForm;
			InitializeComponent();
			this.FormClosing += MainFrm_FormClosing;
		}

		private void UpdateLoadingProgress(int percent, string message)
		{
			_loadingForm?.UpdateProgress(percent, message);
		}

		#region 窗体加载

		private async void MainFrm_Load(object sender, EventArgs e)
		{
			try
			{
				Logger.Info("========== 系统启动 ==========");

				uiMonitor1.Activte = true;
				uiMonitor2.Activte = true;

				// 初始化数据库
				UpdateLoadingProgress(5, "正在初始化数据库...");
				Logger.Info("正在初始化数据库...");
				_dbHelper = new SQLiteHelper();

				// 加载检测参数
				UpdateLoadingProgress(10, "正在加载检测参数...");
				Logger.Info("正在加载检测参数...");
				_detectionParams = DetectionParameters.Instance;

				// 初始化SKU数据库（优先使用预加载）
				UpdateLoadingProgress(15, "正在加载SKU数据...");
				Logger.Info("正在加载SKU数据...");
				if (PreloadedSkuDb != null)
				{
					_skuDb = PreloadedSkuDb;
					PreloadedSkuDb = null;
					Logger.Info("使用预加载的SKU数据，跳过重复加载");
				}
				else
				{
					_skuDb = new SkuDatabase();
					_skuDb.LoadData();
				}

				// 初始化性能监控
				UpdateLoadingProgress(20, "正在初始化性能监控...");
				Logger.Info("正在初始化性能监控...");
				_perfMonitor = new PerformanceMonitor();

				// 初始化高速保存器
				UpdateLoadingProgress(25, "正在初始化图像保存器...");
				Logger.Info("正在初始化图像保存器...");
				_imageSaver = new HighSpeedImageSaver("主保存器", 4, 500);

				// 初始化硬件（运动控制卡 + PLC）
				UpdateLoadingProgress(30, "正在连接运动控制卡...");
				Logger.Info("正在初始化硬件...");
				InitHardware();
				UpdateLoadingProgress(45, "运动控制卡连接成功");

				// 初始化相机
				UpdateLoadingProgress(50, "正在初始化相机SDK...");
				Logger.Info("正在初始化相机SDK...");
				InitCameras();
				UpdateLoadingProgress(65, "相机初始化完成");

				// 初始化AI模型
				UpdateLoadingProgress(70, "正在加载AI模型...");
				Logger.Info("正在加载AI模型...");
				InitAiModels();
				UpdateLoadingProgress(85, "AI模型加载完成");

				// 从配置读取工位启用开关
				FrontEnabled = _detectionParams.Station.FrontEnabled;
				BackEnabled = _detectionParams.Station.BackEnabled;
				EndFaceEnabled = _detectionParams.Station.EndFaceEnabled;
				SideEnabled = _detectionParams.Station.SideEnabled;
				Logger.Info($"工位开关: 正面={FrontEnabled}, 背面={BackEnabled}, 端面={EndFaceEnabled}, 侧面={SideEnabled}");
				// 初始化工位处理器
				UpdateLoadingProgress(88, "正在初始化工位处理器...");
				Logger.Info("正在初始化工位处理器...");
				InitStations();

				// 初始化UI
				UpdateLoadingProgress(90, "正在初始化界面...");
				Logger.Info("正在初始化界面...");
				InitUI();

				// 绑定统计控件
				UpdateLoadingProgress(92, "正在绑定统计控件...");
				Logger.Info("正在绑定统计控件...");
				BindStatisticsControls();

				// 启动班次检查
				StartShiftCheckTimer();

				// 刷新显示
				RefreshCarouselDisplays();
				// 验证数据是否正确加载
				var testSku = _skuDb.GetBySkuNumber("181712303");
				if (testSku != null)
				{
					Logger.Info($"测试SKU: {testSku.SkuNumber}, P={testSku.P}, Z={testSku.Z}, MM={testSku.MM}");
				}

				this.WindowState = FormWindowState.Maximized;

				UpdateLoadingProgress(100, "系统初始化完成，准备启动...");
				Logger.Info("系统初始化完成");

				// 添加手动测试按钮
				var btnTest = new Sunny.UI.UIButton { Text = "手动测试", Size = new System.Drawing.Size(100, 36),
					Location = new System.Drawing.Point(800, 10), Anchor = AnchorStyles.Top | AnchorStyles.Right,
					FillColor = System.Drawing.Color.FromArgb(0, 122, 204), Radius = 6, Font = new System.Drawing.Font("微软雅黑", 9F) };
				btnTest.Click += BtnManualTest_Click;
				this.Controls.Add(btnTest);
				btnTest.BringToFront();
			}
			catch (Exception ex)
			{
				Logger.Error($"系统初始化失败: {ex.Message}\r\n{ex.StackTrace}");
				MessageBox.Show($"系统初始化失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
				Application.Exit();
			}
		}

		#endregion

		#region 硬件初始化

		private void InitHardware()
		{
			// 是否使用模拟模式（没有真实硬件时使用）
			bool useSimulateMode = false;  // 改为 false 使用真实硬件

			// 运动控制卡
			string controlIp = _detectionParams.Motion.ControlIp;
			_motionMgr = new MotionControlManager(controlIp, useSimulateMode);
			_staticMotionMgr = _motionMgr;

			if (_motionMgr.Connect())
			{
				_motionMgr.InitAxes();
				var axisCfg = CommonLib.AxisParamConfig.Load();
				_motionMgr.ApplyAxisParams(axisCfg);
				Logger.Info("轴" + axisCfg.Axis + "参数已从本地加载并应用");
				if (MotionState != null) MotionState.State = UILightState.On;
				Logger.Info("运动控制卡初始化成功");
			}
			else
			{
				if (MotionState != null) MotionState.State = UILightState.Off;
				Logger.Warning("运动控制卡连接失败，将使用模拟模式");
			}

			// PLC通讯
			string plcIp = SystemConfig.GetValue("PlcIp", "192.168.1.101");
			int plcPort = SystemConfig.GetInt("PlcPort", 502);
			_plcComm = new PlcCommunication(plcIp, plcPort, useSimulateMode);

			if (_plcComm.Connect())
			{
				if (PlcState != null) PlcState.State = UILightState.On;
				Logger.Info("PLC连接成功");
			}
			else
			{
				if (PlcState != null) PlcState.State = UILightState.Off;
				Logger.Warning("PLC连接失败，将使用模拟模式");
			}
		}

		private void InitCameras()
		{
			bool useSimulateMode = _detectionParams.Camera.GetSimulateMode();
			var camCfg = _detectionParams.Camera;
			Logger.Info($"========== 初始化相机（模拟模式: {useSimulateMode}） ==========");

			// 相机配置数组: (字段引用, 序列号, 名称, 相机ID)
			var cameraConfigs = new (DaHuaSDK field, string sn, string name, int id)[]
			{
				(null, camCfg.Camera1SN, "正面左", 1),
				(null, camCfg.Camera2SN, "正面右", 2),
				(null, camCfg.Camera3SN, "上端面", 3),
				(null, camCfg.Camera4SN, "下端面", 4),
				(null, camCfg.Camera5SN, "背面左", 5),
				(null, camCfg.Camera6SN, "背面右", 6),
				(null, camCfg.Camera7SN, "左侧面", 7),
				(null, camCfg.Camera8SN, "右侧面", 8),
			};

			int successCount = 0;
			foreach (var cfg in cameraConfigs)
			{
				try
				{
					if (useSimulateMode)
					{
						Logger.Info($"[Camera{cfg.id}] {cfg.name} 模拟模式初始化成功");
						UpdateCameraState(cfg.id, true);
						successCount++;
						continue;
					}

					if (string.IsNullOrEmpty(cfg.sn))
					{
						Logger.Warning($"[Camera{cfg.id}] {cfg.name} 序列号未配置，跳过");
						continue;
					}

					Logger.Info($"[Camera{cfg.id}] {cfg.name} 开始初始化, SN={cfg.sn}");

					var sdk = new DaHuaSDK();
					Logger.Debug($"[Camera{cfg.id}] DaHuaSDK实例已创建");

					sdk.SetCameraInterface(this);
					Logger.Debug($"[Camera{cfg.id}] SetCameraInterface完成");

					// 订阅OnImage事件
					SubscribeCameraImageEvent(sdk, cfg.id);
					Logger.Debug($"[Camera{cfg.id}] OnImage事件已订阅");

					sdk.SetCameraByKey(cfg.sn);
					Logger.Debug($"[Camera{cfg.id}] SetCameraByKey完成 SN={cfg.sn}");

					sdk.Open();
					Logger.Info($"[Camera{cfg.id}] {cfg.name} Open成功");

					sdk.StopStreamGrabber();
					Logger.Debug($"[Camera{cfg.id}] StopStreamGrabber完成");

					sdk.SetAcquisitionMode(0);
					sdk.SetTriggerMode(1);
					sdk.setTriggerSource(1);
					Logger.Debug($"[Camera{cfg.id}] 模式设置: AcquisitionMode=0, TriggerMode=1, TriggerSource=1");

					// 设置曝光时间
					SetCameraExposure(sdk, cfg.id, camCfg);

					sdk.StartStreamGrabber();
					Logger.Info($"[Camera{cfg.id}] {cfg.name} StartStreamGrabber完成, 初始化成功");

					// 赋值给公共字段
					SetCameraField(cfg.id, sdk);

					UpdateCameraState(cfg.id, true);
					successCount++;
				}
				catch (Exception ex)
				{
					Logger.Error($"[Camera{cfg.id}] {cfg.name} 初始化失败: {ex.Message}\r\n{ex.StackTrace}");
					UpdateCameraState(cfg.id, false);
				}
			}

			Logger.Info($"========== 相机初始化完成: {successCount}/{cameraConfigs.Length} ==========");

			// 初始化触发管理器
			if (_motionMgr != null && _motionMgr.IsConnected && !useSimulateMode)
			{
				CameraTriggerConfig.ApplyIn12EdgeMode();
				// 从DetectionParams加载触发脉冲宽度(ms)，覆盖硬编码默认值
				int pw = _detectionParams.Camera.PulseWidthMs;
				if (pw > 0) { CameraTriggerConfig.DefaultPulseWidthMs = pw; foreach (var kv in CameraTriggerConfig.TriggerConfigs) kv.Value.PulseWidthMs = pw; }
				_triggerMgr = new CameraTriggerManager(_motionMgr, useSimulateMode);
				_triggerMgr.OnTriggered += OnCameraTriggered;
				Hardware.CameraTriggerManager.ExternalTriggerEnabled = true; // 相机触发由外部ZMC BASIC程序控制
				_triggerMgr.Start();
				Logger.Info("触发管理器已启动");
				// 每150ms重置心跳标志，ZMC BASIC程序监控此标志检测PC是否存活
				_motionMgr?.StartHeartbeat();
			}
			else
			{
				Logger.Warning("运动控制卡未连接或模拟模式，跳过触发管理器初始化");
			}
		}

		/// <summary>根据相机ID订阅对应的OnImage事件</summary>
		private void SubscribeCameraImageEvent(DaHuaSDK sdk, int cameraId)
		{
			switch (cameraId)
			{
				case 1: sdk.OnImage += OnCamera1Image; break;
				case 2: sdk.OnImage += OnCamera2Image; break;
				case 3: sdk.OnImage += OnCamera3Image; break;
				case 4: sdk.OnImage += OnCamera4Image; break;
				case 5: sdk.OnImage += OnCamera5Image; break;
				case 6: sdk.OnImage += OnCamera6Image; break;
				case 7: sdk.OnImage += OnCamera7Image; break;
				case 8: sdk.OnImage += OnCamera8Image; break;
			}
		}

		/// <summary>将DaHuaSDK实例赋值给对应的公共字段</summary>
		private void SetCameraField(int cameraId, DaHuaSDK sdk)
		{
			switch (cameraId)
			{
				case 1: camera1SDK = sdk; break;
				case 2: camera2SDK = sdk; break;
				case 3: camera3SDK = sdk; break;
				case 4: camera4SDK = sdk; break;
				case 5: camera5SDK = sdk; break;
				case 6: camera6SDK = sdk; break;
				case 7: camera7SDK = sdk; break;
				case 8: camera8SDK = sdk; break;
			}
		}

		/// <summary>设置相机曝光时间</summary>
		private void SetCameraExposure(DaHuaSDK sdk, int cameraId, DetectionParameters.CameraParams camCfg)
		{
			try
			{
				double exp = 5000.0;
				switch (cameraId)
				{
					case 1: exp = camCfg.ExposureTime1; break;
					case 2: exp = camCfg.ExposureTime2; break;
					case 3: exp = camCfg.ExposureTime3; break;
					case 4: exp = camCfg.ExposureTime4; break;
					case 5: exp = camCfg.ExposureTime5; break;
					case 6: exp = camCfg.ExposureTime6; break;
					case 7: exp = camCfg.ExposureTime7; break;
					case 8: exp = camCfg.ExposureTime8; break;
				}
				sdk.SetExposureTime(exp);
				Logger.Debug($"[Camera{cameraId}] 曝光时间: {exp}us");
			}
			catch (Exception ex)
			{
				Logger.Warning($"[Camera{cameraId}] 设置曝光时间失败: {ex.Message}");
			}
		}

		/// <summary>
		/// 相机触发回调 - 当检测到触发信号并输出脉冲后调用
		/// </summary>
		/// <param name="cameraId">被触发的相机ID</param>
		private void OnCameraTriggered(int cameraId)
		{
			// 侧面工位：
			// IN13上升沿(Camera7) → 检查轴位置，不在起点则预归位
			if (cameraId == 7 && SideEnabled && _sideStation != null && _motionMgr != null && _motionMgr.IsConnected)
			{
				float curPos = _motionMgr.GetPosition(_sideStation.SideAxis);
				float startPos = _sideStation.StartPosition;
				if (Math.Abs(curPos - startPos) > 0.5f)
				{
					Logger.Info($"[Side] IN13↑ 轴不在起点(cur={curPos:F1}, target={startPos:F1})，预归位");
					Task.Run(() =>
					{
						_motionMgr.SetSpeed(_sideStation.SideAxis, _sideStation.ReturnSpeed);
						_motionMgr.MoveAbs(_sideStation.SideAxis, startPos);
						_motionMgr.WaitForMoveComplete(_sideStation.SideAxis, 15000);
						Logger.Info($"[Side] 预归位完成 pos={_motionMgr.GetPosition(_sideStation.SideAxis):F1}");
					});
				}
			}
			// IN13下降沿(Camera8) → 启动检测（此时轴已在起点）
			if (cameraId == 8 && SideEnabled && _sideStation != null)
			{
				if (!_sideStation.IsMoving)
				{
					Logger.Info("[Side] IN13↓ 检测到工件，启动侧面运动控制");
					_sideTriggerPending = false;
					Task.Factory.StartNew(() => _sideStation.StartDetection(), TaskCreationOptions.LongRunning);
				}
				else if (!_sideTriggerPending)
				{
					Logger.Info("[Side] IN13↓ 侧面正忙，标记待触发（当前周期结束后自动启动）");
					_sideTriggerPending = true;
					Task.Run(() =>
					{
						while (_sideTriggerPending && _sideStation != null)
						{
							Thread.Sleep(50);
							if (!_sideStation.IsMoving)
							{
								Logger.Info("[Side] 待触发IN13↓ 上一批已完成，启动新的侧面运动控制");
								_sideTriggerPending = false;
								_sideStation.StartDetection();
								break;
							}
						}
					});
				}
			}
		}
		private void InitAiModels()
		{
			if (PreloadedModels != null)
			{
				_aiModels = PreloadedModels;
				PreloadedModels = null;
				Logger.Info("使用预加载的AI模型，跳过重复加载");
				return;
			}
			var modelConfig = ModelPathConfig.LoadFromSysConfig();
			_aiModels = new AiModelManager(modelConfig);
			_aiModels.LoadAllModels();
		}

		private void InitStations()
		{
			string imgPath = _detectionParams.Save.ImageSavePath;
			// 恢复上次SKU
			string lastSku = _detectionParams.LastSkuNumber;
			if (!string.IsNullOrEmpty(lastSku))
			{
				var saved = _skuDb.GetBySkuNumber(lastSku);
				if (saved != null) { _currentSku = saved; Logger.Info($"恢复上次SKU: {lastSku}, P={_currentSku.P}"); }
				else _currentSku = _skuDb.Search("").FirstOrDefault() ?? new SkuData { P = 8, Z = 2, MM = 42 };
			}
			else _currentSku = _skuDb.Search("").FirstOrDefault() ?? new SkuData { P = 8, Z = 2, MM = 42 };

			_frontStation = new FrontStationProcessor(_aiModels, _detectionParams);
			_frontStation.OnResultReady += OnStationResult;
			_frontStation.ReverseBoxOrder = _detectionParams.Station.FrontReverseBox;
			_frontStation.UpdateSku(_currentSku);
			_frontStation.Start();

			_endFaceStation = new EndFaceStationProcessor(_aiModels, imgPath, _currentSku.P, _imageSaver, _perfMonitor);
			_endFaceStation.OnResultReady += OnStationResult;
			_endFaceStation.ReverseBoxOrder = _detectionParams.Station.EndFaceReverseBox;
			_endFaceStation.OnStatusUpdate += OnEndFaceStatusUpdate;
			_endFaceStation.UpdateSku(_currentSku);
			_endFaceStation.Start();

			_backStation = new BackStationProcessor(_aiModels, imgPath, _currentSku, _imageSaver, _perfMonitor);
			_backStation.ReverseBoxOrder = _detectionParams.Station.BackReverseBox;
			_backStation.EnableDateCodeCheck = true;
			_backStation.OnResultReady += OnStationResult;
			_backStation.Start();

			_sideStation = new SideStationProcessor(_aiModels, imgPath, _currentSku, _motionMgr, _imageSaver, _perfMonitor);
			_sideStation.OnResultReady += OnStationResult;
			_sideStation.OnStatusUpdate += OnSideStatusUpdate;
			_sideStation.OnRealTimeDisplay += (side, bmp) =>
			{
				if (bmp == null) return;
				this.BeginInvoke(new Action(() =>
				{
					if (side == Side.Left)
						UpdatePictureBox(xlPictureBox5, bmp);
					else
						UpdatePictureBox(xlPictureBox6, bmp);
				}));
			};

			// 从AxisParams.json加载运动轴参数（与ControlFrm共用同一配置）
			var axisCfg = AxisParamConfig.Load();
			_sideStation.SideAxis = axisCfg.Axis;
			_sideStation.StartPosition = axisCfg.StartPos;
			_sideStation.EndPosition = axisCfg.EndPos;
			_sideStation.ForwardSpeed = axisCfg.FwdSpeed;
			_sideStation.ReturnSpeed = axisCfg.RetSpeed;
			_sideStation.Accel = axisCfg.Accel;
			_sideStation.Decel = axisCfg.Decel;
			_sideStation.FwdInPort = axisCfg.FwdIn;
			_sideStation.RevInPort = axisCfg.RevIn;
			_sideStation.DatumInPort = axisCfg.DatumIn;

			// 同步IN12边缘模式
			_sideStation.EdgeMode = CameraTriggerConfig.In12EdgeMode == CameraTriggerConfig.SideSensorEdgeMode.RisingRightFallingLeft
				? SideStationProcessor.TriggerEdgeMode.RisingRightFallingLeft
				: SideStationProcessor.TriggerEdgeMode.RisingLeftFallingRight;
			_sideStation.ReverseBoxOrder = _detectionParams.Station.SideReverseBox;
			_sideStation.UseContinuousMode = _detectionParams.Side.UseContinuousMode;
			_sideStation.MissingAsNg = _detectionParams.Side.MissingAsNg;
			int sidePw = _detectionParams.Camera.PulseWidthMs;
			if (sidePw > 0) _sideStation.TriggerPulseMs = sidePw;
			Logger.Info($"侧面工位配置: Axis={_sideStation.SideAxis} Pos={_sideStation.StartPosition}~{_sideStation.EndPosition} FwdSpd={_sideStation.ForwardSpeed} RetSpd={_sideStation.ReturnSpeed} Acc={_sideStation.Accel}/{_sideStation.Decel} 限位IN{_sideStation.FwdInPort}/{_sideStation.RevInPort}/{_sideStation.DatumInPort} EdgeMode={_sideStation.EdgeMode} Pulse={_sideStation.TriggerPulseMs}ms");

			_sideStation.Start();
		}

		#endregion

		#region 相机回调

		/// <summary>
		/// 旧相机回调入口（保留兼容），新代码使用独立的OnCameraNImage事件
		/// 显示控件映射: xlPictureBox1=正面, xlPictureBox2=背面, xlPictureBox3=上端面, xlPictureBox4=下端面, xlPictureBox5=左侧面, xlPictureBox6=右侧面
		/// </summary>
		private void OnCameraImageReceived(int cameraId, Bitmap image)
		{
			if (_isClosing || image == null) return;
			long pid = Interlocked.Increment(ref _productIdCounter);

			switch (cameraId)
			{
				case 1: if (FrontEnabled) _frontStation?.OnCam1(image, pid); break;
				case 2: if (FrontEnabled) _frontStation?.OnCam2(image, pid); break;
				case 3: if (EndFaceEnabled) _endFaceStation?.OnCam5(image, pid); break;
				case 4: if (EndFaceEnabled) _endFaceStation?.OnCam6(image, pid); break;
				case 5: if (BackEnabled) _backStation?.OnCam3(image, pid); break;
				case 6: if (BackEnabled) _backStation?.OnCam4(image, pid); break;
				case 7: if (SideEnabled) _sideStation?.OnCam7(image, pid); break;
				case 8: if (SideEnabled) _sideStation?.OnCam8(image, pid); break;
			}
		}

		#region ICamera 接口实现

		public void OnCameraOpen(string cameraName, string cameraKey)
		{
			Logger.Info($"[ICamera] 相机打开: Name={cameraName}, Key={cameraKey}");
			int camId = GetCameraIdByKey(cameraKey);
			if (camId > 0) UpdateCameraState(camId, true);
		}

		public void OnCameraClose(string cameraName, string cameraKey)
		{
			Logger.Warning($"[ICamera] 相机关闭: Name={cameraName}, Key={cameraKey}");
			int camId = GetCameraIdByKey(cameraKey);
			if (camId > 0) UpdateCameraState(camId, false);
		}

		public void OnCameraConnectLoss(string cameraName, string cameraKey)
		{
			Logger.Warning($"[ICamera] 相机掉线: Name={cameraName}, Key={cameraKey}");
			int camId = GetCameraIdByKey(cameraKey);
			if (camId > 0) UpdateCameraState(camId, false);
		}

		#endregion

		#region 各相机OnImage事件处理

		private void OnCamera1Image(Bitmap bitmap, string cameraName, string cameraKey)
		{
			try
			{
				if (_isClosing || bitmap == null) return;
				long pid = Interlocked.Increment(ref _productIdCounter);
//				Logger.Debug($"[Camera1] 正面左 收到图像 {bitmap.Width}x{bitmap.Height}, ProductId={pid}");
				Interlocked.Increment(ref Hardware.CameraTriggerManager.ImageReceivedCount[1]);
				if (FrontEnabled) _frontStation?.OnCam1(bitmap, pid);
			}
			catch (Exception ex) { Logger.Error($"[Camera1] OnImage异常: {ex.Message}"); }
		}

		private void OnCamera2Image(Bitmap bitmap, string cameraName, string cameraKey)
		{
			try
			{
				if (_isClosing || bitmap == null) return;
				long pid = Interlocked.Increment(ref _productIdCounter);
//				Logger.Debug($"[Camera2] 正面右 收到图像 {bitmap.Width}x{bitmap.Height}, ProductId={pid}");
				Interlocked.Increment(ref Hardware.CameraTriggerManager.ImageReceivedCount[2]);
				if (FrontEnabled) _frontStation?.OnCam2(bitmap, pid);
			}
			catch (Exception ex) { Logger.Error($"[Camera2] OnImage异常: {ex.Message}"); }
		}

		private void OnCamera3Image(Bitmap bitmap, string cameraName, string cameraKey)
		{
			try
			{
				if (_isClosing || bitmap == null) return;
				long pid = Interlocked.Increment(ref _productIdCounter);
//				Logger.Debug($"[Camera3] 上端面 收到图像 {bitmap.Width}x{bitmap.Height}, ProductId={pid}");
				Interlocked.Increment(ref Hardware.CameraTriggerManager.ImageReceivedCount[3]);
				if (EndFaceEnabled) _endFaceStation?.OnCam5(bitmap, pid);
			}
			catch (Exception ex) { Logger.Error($"[Camera3] OnImage异常: {ex.Message}"); }
		}

		private void OnCamera4Image(Bitmap bitmap, string cameraName, string cameraKey)
		{
			try
			{
				if (_isClosing || bitmap == null) return;
				long pid = Interlocked.Increment(ref _productIdCounter);
//				Logger.Debug($"[Camera4] 下端面 收到图像 {bitmap.Width}x{bitmap.Height}, ProductId={pid}");
				Interlocked.Increment(ref Hardware.CameraTriggerManager.ImageReceivedCount[4]);
				if (EndFaceEnabled) _endFaceStation?.OnCam6(bitmap, pid);
			}
			catch (Exception ex) { Logger.Error($"[Camera4] OnImage异常: {ex.Message}"); }
		}

		private void OnCamera5Image(Bitmap bitmap, string cameraName, string cameraKey)
		{
			try
			{
				if (_isClosing || bitmap == null) return;
				long pid = Interlocked.Increment(ref _productIdCounter);
//				Logger.Debug($"[Camera5] 背面左 收到图像 {bitmap.Width}x{bitmap.Height}, ProductId={pid}");
				Interlocked.Increment(ref Hardware.CameraTriggerManager.ImageReceivedCount[5]);
				if (BackEnabled) _backStation?.OnCam3(bitmap, pid);
			}
			catch (Exception ex) { Logger.Error($"[Camera5] OnImage异常: {ex.Message}"); }
		}

		private void OnCamera6Image(Bitmap bitmap, string cameraName, string cameraKey)
		{
			try
			{
				if (_isClosing || bitmap == null) return;
				long pid = Interlocked.Increment(ref _productIdCounter);
//				Logger.Debug($"[Camera6] 背面右 收到图像 {bitmap.Width}x{bitmap.Height}, ProductId={pid}");
				Interlocked.Increment(ref Hardware.CameraTriggerManager.ImageReceivedCount[6]);
				if (BackEnabled) _backStation?.OnCam4(bitmap, pid);
			}
			catch (Exception ex) { Logger.Error($"[Camera6] OnImage异常: {ex.Message}"); }
		}

		private void OnCamera7Image(Bitmap bitmap, string cameraName, string cameraKey)
		{
			try
			{
				if (_isClosing || bitmap == null) return;
				long pid = Interlocked.Increment(ref _productIdCounter);
//				Logger.Debug($"[Camera7] 收到图像 {bitmap.Width}x{bitmap.Height}, ProductId={pid}");
				Interlocked.Increment(ref Hardware.CameraTriggerManager.ImageReceivedCount[7]);
				if (SideEnabled) _sideStation?.OnCam7(bitmap, pid);
			}
			catch (Exception ex) { Logger.Error($"[Camera7] OnImage异常: {ex.Message}"); }
		}

		private void OnCamera8Image(Bitmap bitmap, string cameraName, string cameraKey)
		{
			try
			{
				if (_isClosing || bitmap == null) return;
				long pid = Interlocked.Increment(ref _productIdCounter);
//				Logger.Debug($"[Camera8] 收到图像 {bitmap.Width}x{bitmap.Height}, ProductId={pid}");
				Interlocked.Increment(ref Hardware.CameraTriggerManager.ImageReceivedCount[8]);
				if (SideEnabled) _sideStation?.OnCam8(bitmap, pid);
			}
			catch (Exception ex) { Logger.Error($"[Camera8] OnImage异常: {ex.Message}"); }
		}

		#endregion

		/// <summary>统一的相机连接状态更新（修复B1: case 6/7/8之前错误检查camera5State）</summary>
		private void UpdateCameraState(int cameraId, bool isConnected)
		{
			this.BeginInvoke(new Action(() =>
			{
				var state = isConnected ? UILightState.On : UILightState.Off;
				switch (cameraId)
				{
					case 1: if (camera1State != null) camera1State.State = state; break;
					case 2: if (camera2State != null) camera2State.State = state; break;
					case 3: if (camera3State != null) camera3State.State = state; break;
					case 4: if (camera4State != null) camera4State.State = state; break;
					case 5: if (camera5State != null) camera5State.State = state; break;
					case 6: if (camera6State != null) camera6State.State = state; break;
					case 7: if (camera7State != null) camera7State.State = state; break;
					case 8: if (camera8State != null) camera8State.State = state; break;
				}
				Logger.Debug($"[Camera{cameraId}] 状态更新: {(isConnected ? "已连接" : "已断开")}");
			}));
		}

		/// <summary>根据相机序列号(Key)查找相机ID</summary>
		private int GetCameraIdByKey(string cameraKey)
		{
			if (string.IsNullOrEmpty(cameraKey)) return 0;
			var camCfg = _detectionParams?.Camera;
			if (camCfg == null) return 0;
			if (camCfg.Camera1SN == cameraKey) return 1;
			if (camCfg.Camera2SN == cameraKey) return 2;
			if (camCfg.Camera3SN == cameraKey) return 3;
			if (camCfg.Camera4SN == cameraKey) return 4;
			if (camCfg.Camera5SN == cameraKey) return 5;
			if (camCfg.Camera6SN == cameraKey) return 6;
			if (camCfg.Camera7SN == cameraKey) return 7;
			if (camCfg.Camera8SN == cameraKey) return 8;
			return 0;
		}

		private void OnCameraConnectionChanged(int cameraId, bool isConnected)
		{
			UpdateCameraState(cameraId, isConnected);
		}

		#endregion

		#region 工位结果回调

		private void OnStationResult(Bitmap mergedImage, bool[] ngArray, long okCount, long ngCount)
		{
			if (mergedImage == null) return;
			if (this.InvokeRequired)
			{
				this.BeginInvoke(new Action(() => OnStationResult(mergedImage, ngArray, okCount, ngCount)));
				return;
			}
			// 正面工位合并结果显示在 xlPictureBox1
			UpdatePictureBox(xlPictureBox1, mergedImage);
		}
		private void OnStationResult(ProductResult result)
		{
			this.BeginInvoke(new Action(() =>
			{
				// 显示渲染图像到对应控件
				if (result.BackRenderImage != null)
					UpdatePictureBox(xlPictureBox2, result.BackRenderImage);
				if (result.EndFaceRenderImage != null)
					UpdatePictureBox(xlPictureBox3, result.EndFaceRenderImage);
				if (result.EndFaceLowerRenderImage != null)
					UpdatePictureBox(xlPictureBox4, result.EndFaceLowerRenderImage);
				if (result.SideRenderImage != null)
					UpdatePictureBox(xlPictureBox5, result.SideRenderImage);
				if (result.SideLeftRenderImage != null)
					UpdatePictureBox(xlPictureBox5, result.SideLeftRenderImage);
				if (result.SideRightRenderImage != null)
					UpdatePictureBox(xlPictureBox6, result.SideRightRenderImage);

				if (result.IsComplete)
					UpdateStatistics(result);
			}));
		}

		private void UpdateStatistics(ProductResult result)
		{
			// 更新正面统计
			if (result.FrontResult == true && OK_zheng_Lb != null)
			{
				int ok = int.Parse(OK_zheng_Lb.Text);
				OK_zheng_Lb.Text = (ok + 1).ToString();
			}
			else if (result.FrontResult == false && NG_zheng_Lb != null)
			{
				int ng = int.Parse(NG_zheng_Lb.Text);
				NG_zheng_Lb.Text = (ng + 1).ToString();
			}

			// 更新反面统计
			if (result.BackResult == true && OK_fan_Lb != null)
			{
				int ok = int.Parse(OK_fan_Lb.Text);
				OK_fan_Lb.Text = (ok + 1).ToString();
			}
			else if (result.BackResult == false && NG_fan_Lb != null)
			{
				int ng = int.Parse(NG_fan_Lb.Text);
				NG_fan_Lb.Text = (ng + 1).ToString();
			}

			// 更新端面统计
			if (result.EndFaceResult == true && OK_duanmian_Lb != null)
			{
				int ok = int.Parse(OK_duanmian_Lb.Text);
				OK_duanmian_Lb.Text = (ok + 1).ToString();
			}
			else if (result.EndFaceResult == false && NG_duanmian_Lb != null)
			{
				int ng = int.Parse(NG_duanmian_Lb.Text);
				NG_duanmian_Lb.Text = (ng + 1).ToString();
			}

			// 更新侧面统计
			if (result.SideResult == true && OK_cemian_Lb != null)
			{
				int ok = int.Parse(OK_cemian_Lb.Text);
				OK_cemian_Lb.Text = (ok + 1).ToString();
			}
			else if (result.SideResult == false && NG_cemian_Lb != null)
			{
				int ng = int.Parse(NG_cemian_Lb.Text);
				NG_cemian_Lb.Text = (ng + 1).ToString();
			}
		}

		private void OnEndFaceStatusUpdate(List<string> upperStatus, List<string> lowerStatus, List<string> mergedStatus, int p)
		{
			this.BeginInvoke(new Action(() =>
			{
				if (_endFaceIndexLabel != null && _endFaceStation != null)
				{
					_endFaceIndexLabel.Text = $"{_endFaceStation.CurrentIndex + 1}/{p}";
				}
				RefreshCarouselDisplays();
			}));
		}

		private void OnSideStatusUpdate(List<string> leftStatus, List<string> rightStatus, List<string> mergedStatus, int p)
		{
			this.BeginInvoke(new Action(() =>
			{
				if (_sideIndexLabel != null && _sideStation != null)
				{
					_sideIndexLabel.Text = $"{_sideStation.CurrentIndex + 1}/{p}";
				}
				RefreshCarouselDisplays();
			}));
		}

		#endregion

		#region 窗体按钮

		private void mainTitleBar1_OnMenuButtonClick(object sender, EventArgs e)
		{
			DrawPoint point = new DrawPoint(3, 5);
			TabFrm tabFrm = new TabFrm(point, this);
			tabFrm.Show();
		}

		private void mainTitleBar1_OnMinButtonClick(object sender, EventArgs e)
		{
			this.WindowState = FormWindowState.Minimized;
		}

		private void mainTitleBar1_OnCloseButtonClick(object sender, EventArgs e)
		{
			if (MessageBox.Show("确定要退出程序吗？", "退出确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
			{
				this.Close();
				System.Windows.Forms.Application.Exit();
			}
		}

		#endregion

		#region UI初始化

		private void InitUI()
		{
			SetupSkuSearch();
			// InitCarouselLabels();
			BindButtonEvents();
			UpdateSkuDisplay();
		}



		private void SetupSkuSearch()
		{
			if (SKU_Txt == null || SKU_Txt.Parent == null) return;

			Logger.Info("初始化SKU搜索控件");

			SKU_Txt.Visible = false;

			// 创建搜索框
			_skuSearchCombo = new ComboBox
			{
				Font = new Font("微软雅黑", 10F),
				DropDownStyle = ComboBoxStyle.DropDown,
				Width = SKU_Txt.Width,
				Height = SKU_Txt.Height,
				Location = SKU_Txt.Location,
				Text = ""
			};

			SKU_Txt.Parent.Controls.Add(_skuSearchCombo);
			_skuSearchCombo.BringToFront();

			// 使用 System.Windows.Forms.Timer 防抖
			System.Windows.Forms.Timer debounceTimer = new System.Windows.Forms.Timer();
			debounceTimer.Interval = 300;

			_skuSearchCombo.TextChanged += (s, e) =>
			{
				// 重新启动计时器
				debounceTimer.Stop();
				debounceTimer.Start();
			};

			debounceTimer.Tick += (timerSender, timerE) =>
			{
				debounceTimer.Stop();

				string keyword = _skuSearchCombo.Text;
				if (string.IsNullOrWhiteSpace(keyword))
				{
					_skuSearchCombo.Items.Clear();
					return;
				}

				// 在UI线程上更新
				this.BeginInvoke(new Action(() =>
				{
					var results = _skuDb.Search(keyword);
					_skuSearchCombo.Items.Clear();

					foreach (var sku in results)
					{
						_skuSearchCombo.Items.Add(sku.SkuNumber);
					}

					if (results.Count > 0 && _skuSearchCombo.DroppedDown == false)
					{
						_skuSearchCombo.DroppedDown = true;
					}
				}));
			};

			// 用户选择时触发
			_skuSearchCombo.SelectedIndexChanged += (s, e) =>
			{
				if (_skuSearchCombo.SelectedItem != null)
				{
					string skuNum = _skuSearchCombo.SelectedItem.ToString();
					Logger.Info($"选择SKU: {skuNum}");

					_currentSku = _skuDb.GetBySkuNumber(skuNum);
					if (_currentSku != null)
					{
						UpdateSkuDisplay();
						_frontStation?.UpdateSku(_currentSku);
						_backStation?.UpdateSku(_currentSku);
						// 保存到配置
						_detectionParams.LastSkuNumber = skuNum;
						_detectionParams.SaveToFile();
						_sideStation?.UpdateSku(_currentSku);
						_endFaceStation?.UpdateSku(_currentSku);
						_endFaceStation?.UpdatePCount(_currentSku.P);
						Logger.Info($"SKU已切换: {skuNum}, P={_currentSku.P}, Z={_currentSku.Z}, MM={_currentSku.MM}");
					}
					else
					{
						Logger.Warning($"未找到SKU: {skuNum}");
					}

					_skuSearchCombo.DroppedDown = false;
				}
			};

			// 回车键确认
			_skuSearchCombo.KeyDown += (s, e) =>
			{
				if (e.KeyCode == Keys.Enter)
				{
					string skuNum = _skuSearchCombo.Text;
					if (!string.IsNullOrWhiteSpace(skuNum))
					{
						_currentSku = _skuDb.GetBySkuNumber(skuNum);
						if (_currentSku != null)
						{
							UpdateSkuDisplay();
							_frontStation?.UpdateSku(_currentSku);
							_backStation?.UpdateSku(_currentSku);
							_sideStation?.UpdateSku(_currentSku);
							_detectionParams.LastSkuNumber = skuNum;
							_detectionParams.SaveToFile();
							_endFaceStation?.UpdateSku(_currentSku);
							_endFaceStation?.UpdatePCount(_currentSku.P);
							Logger.Info($"SKU已切换(回车): {skuNum}, P={_currentSku.P}");
						}
						_skuSearchCombo.DroppedDown = false;
					}
				}
			};
		}
		private void ApplySkuChange()
		{
			if (_currentSku == null) return;

			Logger.Info($"应用SKU: {_currentSku.SkuNumber}");

			// 更新显示区域
			UpdateSkuDisplay();

			// 更新工位处理器
			_frontStation?.UpdateSku(_currentSku);
			_backStation?.UpdateSku(_currentSku);
			_sideStation?.UpdateSku(_currentSku);
			_endFaceStation?.UpdatePCount(_currentSku.P);
		}

		private void UpdateSkuDisplay()
		{
			if (_currentSku == null)
			{
				Logger.Warning("_currentSku is null");
				return;
			}

			if (this.InvokeRequired)
			{
				this.Invoke(new Action(UpdateSkuDisplay));
				return;
			}

			try
			{
				Logger.Debug($"更新SKU显示: {_currentSku.SkuNumber}");
				Logger.Debug($"  P={_currentSku.P}, Z={_currentSku.Z}, MM={_currentSku.MM}");
				Logger.Debug($"  P号码(背卡P号)={_currentSku.FrontPCode}");
				Logger.Debug($"  条形码={_currentSku.BackBarcode}");
				Logger.Debug($"  打码格式={_currentSku.CodingFormat}");

				if (_skuSearchCombo != null) _skuSearchCombo.Text = _currentSku.SkuNumber ?? "";
				if (P_Lb != null) P_Lb.Text = _currentSku.P.ToString();
				if (Z_Lb != null) Z_Lb.Text = _currentSku.Z.ToString();
				if (MM_Lb != null) MM_Lb.Text = _currentSku.MM.ToString();

				// 正面P号码标准 - 使用 FrontPNumber_Lb
				if (FrontPNumber_Lb != null)
				{
					FrontPNumber_Lb.Text = string.IsNullOrEmpty(_currentSku.FrontPCode) ? "-" : _currentSku.FrontPCode;
				}

				// 背面条形码标准 - 使用 BackBarcode_Lb
				if (BackBarcode_Lb != null)
				{
					BackBarcode_Lb.Text = string.IsNullOrEmpty(_currentSku.BackBarcode) ? "-" : _currentSku.BackBarcode;
				}

				// 打码格式
				if (CodingFormat_Lb != null)
				{
					CodingFormat_Lb.Text = string.IsNullOrEmpty(_currentSku.CodingFormat) ? "-" : _currentSku.CodingFormat;
				}
			}
			catch (Exception ex)
			{
				Logger.Error($"更新SKU显示失败: {ex.Message}");
			}
		}
		private void InitCarouselLabels()
		{
			// 只添加索引标签，不添加按钮
			if (xlPictureBox3 != null && _endFaceIndexLabel == null)
			{
				_endFaceIndexLabel = new Label
				{
					Text = "1/8",
					Width = 80,
					Height = 30,
					TextAlign = ContentAlignment.MiddleCenter,
					Font = new Font("微软雅黑", 10F, FontStyle.Bold),
					ForeColor = Color.White,
					BackColor = Color.FromArgb(47, 60, 76),
					Location = new DrawPoint(xlPictureBox3.Left + xlPictureBox3.Width / 2 - 40, xlPictureBox3.Bottom + 5)
				};
				this.Controls.Add(_endFaceIndexLabel);
				_endFaceIndexLabel.BringToFront();
			}

			if (xlPictureBox5 != null && _sideIndexLabel == null)
			{
				_sideIndexLabel = new Label
				{
					Text = "1/8",
					Width = 80,
					Height = 30,
					TextAlign = ContentAlignment.MiddleCenter,
					Font = new Font("微软雅黑", 10F, FontStyle.Bold),
					ForeColor = Color.White,
					BackColor = Color.FromArgb(47, 60, 76),
					Location = new DrawPoint(xlPictureBox5.Left + xlPictureBox5.Width / 2 - 40, xlPictureBox5.Bottom + 5)
				};
				this.Controls.Add(_sideIndexLabel);
				_sideIndexLabel.BringToFront();
			}
		}

		private void RefreshCarouselDisplays()
		{
			try
			{
				// 端面轮播图 — 上端面→xlPictureBox3, 下端面→xlPictureBox4
				if (_endFaceStation != null)
				{
					var upperMat = _endFaceStation.GetCurrentUpperImage();
					if (upperMat != null && !upperMat.Empty())
					{
						var bmp = BmpConverter.ToBitmap(upperMat);
						UpdatePictureBox(xlPictureBox3, bmp);
						upperMat.Dispose();
					}
					var lowerMat = _endFaceStation.GetCurrentLowerImage();
					if (lowerMat != null && !lowerMat.Empty())
					{
						var bmp = BmpConverter.ToBitmap(lowerMat);
						UpdatePictureBox(xlPictureBox4, bmp);
						lowerMat.Dispose();
					}
				}

				// 侧面轮播图 — 左侧面→xlPictureBox5
				if (_sideStation != null)
				{
					var displayBitmap = _sideStation.GetCurrentDisplayImage();
					if (displayBitmap != null)
					{
						UpdatePictureBox(xlPictureBox5, displayBitmap);
					}
				}
			}
			catch (Exception ex)
			{
				Logger.Error($"刷新轮播图失败: {ex.Message}");
			}
		}

		private void UpdatePictureBox(XLPictureBox pb, Bitmap image)
		{
			if (pb == null || image == null) return;

			if (pb.InvokeRequired)
			{
				pb.BeginInvoke(new Action(() => UpdatePictureBox(pb, image)));
				return;
			}

			try
			{
				Bitmap display = image;
				int maxW = 1920;
				if (image.Width > maxW)
				{
					float scale = (float)maxW / image.Width;
					int newH = (int)(image.Height * scale);
					display = new Bitmap(image, new DrawSize(maxW, newH));
				}
				var old = pb.Image;
				pb.Image = display;
				old?.Dispose();
			}
			catch (Exception ex)
			{
				Logger.Error($"更新图片显示失败: {ex.Message}");
			}
		}

		private void BindButtonEvents()
		{
			if (clearBtn != null)
				clearBtn.Click += (s, e) => ClearAllStatistics();
		}

		private void BindStatisticsControls()
		{
			// 初始化统计显示
			if (OK_zheng_Lb != null) OK_zheng_Lb.Text = "0";
			if (NG_zheng_Lb != null) NG_zheng_Lb.Text = "0";
			if (Yield_zheng_Lb != null) Yield_zheng_Lb.Text = "0%";

			if (OK_fan_Lb != null) OK_fan_Lb.Text = "0";
			if (NG_fan_Lb != null) NG_fan_Lb.Text = "0";
			if (Yield_fan_Lb != null) Yield_fan_Lb.Text = "0%";

			if (OK_duanmian_Lb != null) OK_duanmian_Lb.Text = "0";
			if (NG_duanmian_Lb != null) NG_duanmian_Lb.Text = "0";
			if (Yield_duanmian_Lb != null) Yield_duanmian_Lb.Text = "0%";

			if (OK_cemian_Lb != null) OK_cemian_Lb.Text = "0";
			if (NG_cemian_Lb != null) NG_cemian_Lb.Text = "0";
			if (Yield_cemian_Lb != null) Yield_cemian_Lb.Text = "0%";
		}

		#endregion

		#region 统计清除

		private void ClearAllStatistics()
		{
			if (MessageBox.Show("确认清除所有统计数据吗？", "确认",
				MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
			{
				_frontStation?.ClearCounters();
				_backStation?.ClearCounters();
				_endFaceStation?.ClearCounters();
				_sideStation?.ClearCounters();

				if (OK_zheng_Lb != null) OK_zheng_Lb.Text = "0";
				if (NG_zheng_Lb != null) NG_zheng_Lb.Text = "0";
				if (OK_fan_Lb != null) OK_fan_Lb.Text = "0";
				if (NG_fan_Lb != null) NG_fan_Lb.Text = "0";
				if (OK_duanmian_Lb != null) OK_duanmian_Lb.Text = "0";
				if (NG_duanmian_Lb != null) NG_duanmian_Lb.Text = "0";
				if (OK_cemian_Lb != null) OK_cemian_Lb.Text = "0";
				if (NG_cemian_Lb != null) NG_cemian_Lb.Text = "0";

				Logger.Info("所有统计数据已清除");
			}
		}

		#endregion

		#region 班次管理

		private void StartShiftCheckTimer()
		{
			_shiftCheckTimer = new System.Timers.Timer(60000);
			_shiftCheckTimer.Elapsed += (s, e) => CheckShiftChange();
			_shiftCheckTimer.AutoReset = true;
			_shiftCheckTimer.Start();

			_currentShift = GetCurrentShift();
			_shiftStartTime = DateTime.Now;
			Logger.Info($"当前班次: {_currentShift}");
		}

		private void CheckShiftChange()
		{
			string newShift = GetCurrentShift();
			if (_currentShift != newShift)
			{
				Logger.Info($"班次切换: {_currentShift} -> {newShift}");
				SaveCurrentShiftStatistics();
				_currentShift = newShift;
				_shiftStartTime = DateTime.Now;
			}
		}

		private string GetCurrentShift()
		{
			var now = DateTime.Now.TimeOfDay;
			if (now >= TimeSpan.Parse("00:00:00") && now <= TimeSpan.Parse("07:59:59"))
				return "晚班";
			if (now >= TimeSpan.Parse("08:00:00") && now <= TimeSpan.Parse("15:59:59"))
				return "早班";
			return "中班";
		}

		private void SaveCurrentShiftStatistics()
		{
			try
			{
				long totalOk = 0, totalNg = 0;

				if (OK_zheng_Lb != null) totalOk += long.Parse(OK_zheng_Lb.Text);
				if (NG_zheng_Lb != null) totalNg += long.Parse(NG_zheng_Lb.Text);
				if (OK_fan_Lb != null) totalOk += long.Parse(OK_fan_Lb.Text);
				if (NG_fan_Lb != null) totalNg += long.Parse(NG_fan_Lb.Text);
				if (OK_duanmian_Lb != null) totalOk += long.Parse(OK_duanmian_Lb.Text);
				if (NG_duanmian_Lb != null) totalNg += long.Parse(NG_duanmian_Lb.Text);
				if (OK_cemian_Lb != null) totalOk += long.Parse(OK_cemian_Lb.Text);
				if (NG_cemian_Lb != null) totalNg += long.Parse(NG_cemian_Lb.Text);

				_dbHelper.SaveShiftStatistics(_currentShift, _shiftStartTime, (int)(totalOk + totalNg), (int)totalOk, (int)totalNg);
				Logger.Info($"班次统计已保存: {_currentShift}, OK={totalOk}, NG={totalNg}");
			}
			catch (Exception ex)
			{
				Logger.Error($"保存班次统计失败: {ex.Message}");
			}
		}

		#endregion

		#region 窗体关闭

		private void MainFrm_FormClosing(object sender, FormClosingEventArgs e)
		{
			try
			{
				Logger.Info("应用程序正在关闭...");
				_isClosing = true;

				SaveCurrentShiftStatistics();

				_sideTriggerPending = false; // 终止待触发轮询任务
				_frontStation?.Dispose();
				_backStation?.Dispose();
				_endFaceStation?.Dispose();
				_sideStation?.Dispose();

				_triggerMgr?.Dispose();  // 释放触发管理器（包含后台线程）
										 // 释放所有相机SDK实例
				DisposeAllCameras();
				_motionMgr?.Disconnect();
				_plcComm?.Disconnect();
				_perfMonitor?.Dispose();
				_imageSaver?.Dispose();
				_shiftCheckTimer?.Stop();
				_shiftCheckTimer?.Dispose();
				_aiModels?.Dispose();

				Logger.Shutdown();
			}
			catch (Exception ex)
			{
				Console.WriteLine($"关闭异常: {ex.Message}");
			}
		}
		/// <summary>释放所有相机SDK实例</summary>
		private void DisposeAllCameras()
		{
			var cameras = new[] { camera1SDK, camera2SDK, camera3SDK, camera4SDK, camera5SDK, camera6SDK, camera7SDK, camera8SDK };
			foreach (var cam in cameras)
			{
				if (cam == null) continue;
				try { cam.StopStreamGrabber(); } catch (Exception ex) { Logger.Error($"相机StopStreamGrabber异常: {ex.Message}"); }
				try { cam.Close(); } catch (Exception ex) { Logger.Error($"相机Close异常: {ex.Message}"); }
			}
			Logger.Info("所有相机已释放");
		}


		#endregion

		#region 测试窗体入口

		public void OpenTestForm()
		{
			// 测试窗体逻辑
			var testForm = new Form
			{
				Text = "算法调试",
				Size = new DrawSize(800, 600),
				StartPosition = FormStartPosition.CenterParent
			};
			testForm.ShowDialog();
		}
		/// <summary>
		/// 获取运动控制管理器
		/// </summary>
		public MotionControlManager GetMotionControlManager()
		{
			return _motionMgr;
		}
		/// <summary>
		/// 获取相机管理器（已弃用，现在相机由MainFrm直接管理）
		/// </summary>
		[Obsolete("相机现在由MainFrm直接管理，请使用GetDaHuaSDK(int cameraId)")]
		public CameraManager GetCameraManager()
		{
			return null;
		}

		/// <summary>
		/// 获取AI模型管理器
		/// </summary>
		public AiModelManager GetAiModelManager()
		{
			return _aiModels;
		}

			private void BtnManualTest_Click(object sender, EventArgs e)
			{
				using (var d = new OpenFileDialog { Title = "选择背面左图", Filter = "图像|*.jpg;*.jpeg;*.png;*.bmp;*.tif" })
				{if (d.ShowDialog() != DialogResult.OK) return;
					var left = new Bitmap(d.FileName);
					using (var d2 = new OpenFileDialog { Title = "选择背面右图", Filter = "图像|*.jpg;*.jpeg;*.png;*.bmp;*.tif" })
					{if (d2.ShowDialog() != DialogResult.OK) return;
						var right = new Bitmap(d2.FileName);
							_backStation?.UpdateSku(new SkuData { SkuNumber = "MANUAL", P = _currentSku?.P ?? 12, Z = _currentSku?.Z ?? 2, MM = _currentSku?.MM ?? 42, BackBarcode = _currentSku?.BackBarcode, CodingFormat = _currentSku?.CodingFormat, FrontPCode = _currentSku?.FrontPCode });
						_backStation?.OnCam3(left, 0);
						_backStation?.OnCam4(right, 0);
						UIMessageTip.ShowOk(this, "已提交测试");
					}}
			}
		#endregion
	}
}
