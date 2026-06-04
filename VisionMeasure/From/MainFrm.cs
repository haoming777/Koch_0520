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
using System.Diagnostics;
using System.Threading;
using Newtonsoft.Json;
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

				xlPictureBox5.ISRealTimeDisplay = true;
				xlPictureBox6.ISRealTimeDisplay = true;

				// 添加手动测试按钮
				var btnTest = new Sunny.UI.UIButton
				{
					Text = "手动测试",
					Size = new System.Drawing.Size(100, 36),
					Location = new System.Drawing.Point(800, 10),
					Anchor = AnchorStyles.Top | AnchorStyles.Right,
					FillColor = System.Drawing.Color.FromArgb(0, 122, 204),
					Radius = 6,
					Font = new System.Drawing.Font("微软雅黑", 9F)
				};
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
				if (cameraId == 7 && SideEnabled && _sideStation != null && _sideStation.MotionEnabled && _motionMgr != null && _motionMgr.IsConnected)
				{
					float curPos = _motionMgr.GetPosition(_sideStation.SideAxis);
					float startPos = _sideStation.StartPosition;
					if (Math.Abs(curPos - startPos) > 0.5f)
					{
						Logger.Info($"[Side] IN13↑ 轴不在起点(cur={curPos:F1}, target={startPos:F1})，预归位");
						int axis = _sideStation.SideAxis;
						int lockPort = _sideStation.SafetyLockPort;
						bool lockHigh = _sideStation.SafetyLockActiveHigh;
						bool returnToStart = _sideStation.RecoveryMode == SideStationProcessor.SafetyRecovery.ReturnToStart;
						Task.Run(() =>
						{
							// 安全锁检查
							if (!_motionMgr.CheckSafetyLock(lockPort, lockHigh))
							{
								Logger.Info($"[Side] 预归位等待安全锁 IN{lockPort}=0...");
								while (!_motionMgr.CheckSafetyLock(lockPort, lockHigh))
									Thread.Sleep(10);
								Logger.Info($"[Side] 安全锁释放，开始预归位");
							}
							_motionMgr.SetSpeed(axis, _sideStation.ReturnSpeed);
							_motionMgr.MoveAbs(axis, startPos);
							// 运动中监控安全锁
							bool stopped = false;
							var sw = System.Diagnostics.Stopwatch.StartNew();
							while (sw.ElapsedMilliseconds < 15000)
							{
								if (!_motionMgr.IsMoving(axis)) break;
								if (!_motionMgr.CheckSafetyLock(lockPort, lockHigh))
								{
									if (!stopped) { Logger.Warning("[Side] 预归位中安全锁触发! 急停"); _motionMgr.EmergencyStop(axis); stopped = true; }
									Thread.Sleep(10); continue;
								}
								if (stopped)
								{
									stopped = false;
									Logger.Info("[Side] 安全锁恢复，继续预归位");
									_motionMgr.MoveAbs(axis, startPos);
								}
								Thread.Sleep(10);
							}
							Logger.Info($"[Side] 预归位完成 pos={_motionMgr.GetPosition(axis):F1}");
						});
					}
				}
			// IN13下降沿(Camera8) → 启动检测（此时轴已在起点）
			if (cameraId == 8 && SideEnabled && _sideStation != null && _sideStation.MotionEnabled)
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
							Thread.Sleep(10);
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
			_frontStation.InitThresholdsFromModel();  // 从模型best.json加载阈值
			_frontStation.EnableBoxBreakCheck = _detectionParams.Front.EnableBoxBreakCheck;
			_frontStation.Start();

			_endFaceStation = new EndFaceStationProcessor(_aiModels, imgPath, _currentSku.P, _imageSaver, _perfMonitor);
			_endFaceStation.OnResultReady += OnStationResult;
			_endFaceStation.ReverseBoxOrder = _detectionParams.Station.EndFaceReverseBox;
			_endFaceStation.OnStatusUpdate += OnEndFaceStatusUpdate;
			_endFaceStation.UpdateSku(_currentSku);
			_endFaceStation.InitThresholdsFromModel();  // 从模型best.json加载阈值
			_endFaceStation.EnableUpperDefectCheck = _detectionParams.EndFace.EnableUpperDefectCheck;
			_endFaceStation.Start();

			_backStation = new BackStationProcessor(_aiModels, imgPath, _currentSku, _imageSaver, _perfMonitor);
			_backStation.ReverseBoxOrder = _detectionParams.Station.BackReverseBox;
			_backStation.OnResultReady += OnStationResult;
			_backStation.InitThresholdsFromModel();  // 从模型best.json加载阈值
			_backStation.EnableBarcodeCheck = _detectionParams.Back.EnableBarcodeCheck;
			_backStation.EnableHookCheck = _detectionParams.Back.EnableHookCheck;
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
			_sideStation.MotionEnabled = _detectionParams.Side.MotionEnabled;
			_sideStation.EnableSideDefectCheck = _detectionParams.Side.EnableSideDefectCheck;
			_sideStation.SafetyLockPort = _detectionParams.Side.SafetyLockPort;
			_sideStation.SafetyLockActiveHigh = _detectionParams.Side.SafetyLockActiveHigh;
			_sideStation.RecoveryMode = (SideStationProcessor.SafetyRecovery)_detectionParams.Side.SafetyLockRecovery;
			Logger.Info($"侧面工位配置: Axis={_sideStation.SideAxis} Pos={_sideStation.StartPosition}~{_sideStation.EndPosition} FwdSpd={_sideStation.ForwardSpeed} RetSpd={_sideStation.ReturnSpeed} Acc={_sideStation.Accel}/{_sideStation.Decel} 限位IN{_sideStation.FwdInPort}/{_sideStation.RevInPort}/{_sideStation.DatumInPort} EdgeMode={_sideStation.EdgeMode} Pulse={_sideStation.TriggerPulseMs}ms 安全锁IN={(_sideStation.SafetyLockPort > 0 ? _sideStation.SafetyLockPort.ToString() : "禁用")} 恢复模式={(_sideStation.RecoveryMode == SideStationProcessor.SafetyRecovery.ReturnToStart ? "返回起始位" : "继续执行")}");

			_sideStation.InitThresholdsFromModel();  // 从模型best.json加载阈值
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
		if (OK_zheng_Lb != null) OK_zheng_Lb.Text = okCount.ToString();
		if (NG_zheng_Lb != null) NG_zheng_Lb.Text = ngCount.ToString();
		long ft2 = okCount + ngCount;
		if (Yield_zheng_Lb != null) Yield_zheng_Lb.Text = ft2 > 0 ? (okCount * 100.0 / ft2).ToString("F1") + "%" : "0%";
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

				UpdateStatistics(result);  // 每次工位结果到达即更新计数
			}));
		}

		private void UpdateStatistics(ProductResult result)
		{
			// 更新正面统计
			// 使用各工位累计计数，按支计算
			this.BeginInvoke(new Action(() => {
				if (_frontStation != null) {
					if (OK_zheng_Lb != null) OK_zheng_Lb.Text = _frontStation.OkCount.ToString();
					if (NG_zheng_Lb != null) NG_zheng_Lb.Text = _frontStation.NgCount.ToString();
					long ft = _frontStation.OkCount + _frontStation.NgCount;
					if (Yield_zheng_Lb != null) Yield_zheng_Lb.Text = (ft > 0 ? (_frontStation.OkCount * 100.0 / ft).ToString("F1") + "%" : "0%");
				}
				if (_backStation != null) {
					if (OK_fan_Lb != null) OK_fan_Lb.Text = _backStation.OkCount.ToString();
					if (NG_fan_Lb != null) NG_fan_Lb.Text = _backStation.NgCount.ToString();
					long bt = _backStation.OkCount + _backStation.NgCount;
					if (Yield_fan_Lb != null) Yield_fan_Lb.Text = (bt > 0 ? (_backStation.OkCount * 100.0 / bt).ToString("F1") + "%" : "0%");
				}
				if (_endFaceStation != null) {
					if (OK_duanmian_Lb != null) OK_duanmian_Lb.Text = _endFaceStation.OkCount.ToString();
					if (NG_duanmian_Lb != null) NG_duanmian_Lb.Text = _endFaceStation.NgCount.ToString();
					long et = _endFaceStation.OkCount + _endFaceStation.NgCount;
					if (Yield_duanmian_Lb != null) Yield_duanmian_Lb.Text = (et > 0 ? (_endFaceStation.OkCount * 100.0 / et).ToString("F1") + "%" : "0%");
				}
				if (_sideStation != null) {
					if (OK_cemian_Lb != null) OK_cemian_Lb.Text = _sideStation.OkCount.ToString();
					if (NG_cemian_Lb != null) NG_cemian_Lb.Text = _sideStation.NgCount.ToString();
					long st2 = _sideStation.OkCount + _sideStation.NgCount;
					if (Yield_cemian_Lb != null) Yield_cemian_Lb.Text = (st2 > 0 ? (_sideStation.OkCount * 100.0 / st2).ToString("F1") + "%" : "0%");
				}
			}));
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
			LoadSkuParams();
			if (OpenNGimageBtn != null) OpenNGimageBtn.Click += (s2,e2) => { string d = Path.Combine(_detectionParams.Save.ImageSavePath, DateTime.Now.ToString("yyMMdd")); if (!Directory.Exists(d)) d = _detectionParams.Save.ImageSavePath; if (Directory.Exists(d)) Process.Start("explorer.exe", d); };
			BindButtonEvents();
			LoadCounts();
			UpdateSkuDisplay();
			InitTestButtons();
		}

		private void InitTestButtons()
		{
			if (tableLayoutPanel34 == null) return;
			tableLayoutPanel34.SuspendLayout();
			tableLayoutPanel34.Controls.Clear();
			tableLayoutPanel34.RowCount = 4;
			for (int i = 0; i < 4; i++)
			{
				string text; EventHandler handler;
				switch (i)
				{
					case 0: text = "正面测试"; handler = TestFrontBtn_Click; break;
					case 1: text = "背面测试"; handler = TestBackBtn_Click; break;
					case 2: text = "端面测试"; handler = TestEndFaceBtn_Click; break;
					default: text = "侧面测试"; handler = TestSideBtn_Click; break;
				}
				var btn = new Button
				{
					Text = text,
					Dock = DockStyle.Fill,
					FlatStyle = FlatStyle.Flat,
					BackColor = Color.FromArgb(52, 152, 219),
					ForeColor = Color.White,
					Font = new Font("微软雅黑", 10F, FontStyle.Bold),
					Margin = new Padding(2)
				};
				btn.Click += handler;
				tableLayoutPanel34.Controls.Add(btn, 0, i);
			}
			tableLayoutPanel34.ResumeLayout();
			Logger.Info("工位测试按钮已初始化");
		}

		private Bitmap PickImage(string title)
		{
			using (var dlg = new OpenFileDialog { Title = title, Filter = "图片文件|*.jpg;*.jpeg;*.png;*.bmp;*.tiff" })
			{
				if (dlg.ShowDialog() == DialogResult.OK)
				{
					try { return new Bitmap(dlg.FileName); }
					catch (Exception ex) { Logger.Error("加载图片失败: " + ex.Message); MessageBox.Show("加载图片失败: " + ex.Message); }
				}
			}
			return null;
		}

		private void TestFrontBtn_Click(object sender, EventArgs e)
		{
			if (_frontStation == null) { MessageBox.Show("正面工位未初始化"); return; }
			var left = PickImage("选择正面左图 (相机1)");
			if (left == null) return;
			var right = PickImage("选择正面右图 (相机2)");
			if (right == null) { left.Dispose(); return; }
			Logger.Info("[Test] 正面测试开始 " + left.Width + "x" + left.Height + " / " + right.Width + "x" + right.Height);
			_frontStation.SkipCrop = true;
			_frontStation.OnCam1(left, DateTime.Now.Ticks);
			_frontStation.OnCam2(right, DateTime.Now.Ticks);
			_frontStation.SkipCrop = false;
		}

		private void TestBackBtn_Click(object sender, EventArgs e)
		{
			if (_backStation == null) { MessageBox.Show("背面工位未初始化"); return; }
			var left = PickImage("选择背面左图 (相机5)");
			if (left == null) return;
			var right = PickImage("选择背面右图 (相机6)");
			if (right == null) { left.Dispose(); return; }
			Logger.Info("[Test] 背面测试开始 " + left.Width + "x" + left.Height + " / " + right.Width + "x" + right.Height);
			_backStation.SkipCrop = true;
			_backStation.OnCam3(left, DateTime.Now.Ticks);
			_backStation.OnCam4(right, DateTime.Now.Ticks);
			_backStation.SkipCrop = false;
		}

		private void TestEndFaceBtn_Click(object sender, EventArgs e)
		{
			if (_endFaceStation == null) { MessageBox.Show("端面工位未初始化"); return; }
			var upper = PickImage("选择上端面图片 (相机3)");
			if (upper == null) return;
			var lower = PickImage("选择下端面图片 (相机4)");
			if (lower == null) { upper.Dispose(); return; }
			Logger.Info("[Test] 端面测试开始 " + upper.Width + "x" + upper.Height + " / " + lower.Width + "x" + lower.Height);
			_endFaceStation.TestProcessPair(upper, lower);
		}

		private void TestSideBtn_Click(object sender, EventArgs e)
		{
			if (_sideStation == null) { MessageBox.Show("侧面工位未初始化"); return; }
			var left = PickImage("选择左侧面图片 (相机7)");
			if (left == null) return;
			var right = PickImage("选择右侧面图片 (相机8)");
			if (right == null) { left.Dispose(); return; }
			Logger.Info("[Test] 侧面测试开始 " + left.Width + "x" + left.Height + " / " + right.Width + "x" + right.Height);
			_sideStation.TestProcessPair(left, right);
		}



		private void SaveCounts() { try { var data = new Dictionary<string,string>() { {"shift",GetCurrentShift()},{"date",DateTime.Now.ToString("yyyyMMdd")},{"frontOk",_frontStation?.OkCount.ToString()??"0"},{"frontNg",_frontStation?.NgCount.ToString()??"0"},{"backOk",_backStation?.OkCount.ToString()??"0"},{"backNg",_backStation?.NgCount.ToString()??"0"},{"endOk",_endFaceStation?.OkCount.ToString()??"0"},{"endNg",_endFaceStation?.NgCount.ToString()??"0"},{"sideOk",_sideStation?.OkCount.ToString()??"0"},{"sideNg",_sideStation?.NgCount.ToString()??"0"} }; File.WriteAllText(Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Config","counts.json"), Newtonsoft.Json.JsonConvert.SerializeObject(data)); } catch {} }
		private void LoadCounts() { try { var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Config","counts.json"); if(!File.Exists(path)) return; var data = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string,string>>(File.ReadAllText(path)); if(data==null) return; this.BeginInvoke(new Action(()=>{ string savedShift = data.ContainsKey("shift") ? data["shift"] : ""; if(!string.IsNullOrEmpty(savedShift) && savedShift != GetCurrentShift() || (data.ContainsKey("date") && data["date"] != DateTime.Now.ToString("yyyyMMdd"))) { Logger.Info("计数班次不匹配("+savedShift+"!="+GetCurrentShift()+"),从0开始"); return; } if(data.ContainsKey("frontOk")){ if(OK_zheng_Lb!=null)OK_zheng_Lb.Text=data["frontOk"]; if(NG_zheng_Lb!=null)NG_zheng_Lb.Text=data["frontNg"]; if(OK_fan_Lb!=null)OK_fan_Lb.Text=data["backOk"]; if(NG_fan_Lb!=null)NG_fan_Lb.Text=data["backNg"]; if(OK_duanmian_Lb!=null)OK_duanmian_Lb.Text=data["endOk"]; if(NG_duanmian_Lb!=null)NG_duanmian_Lb.Text=data["endNg"]; if(OK_cemian_Lb!=null)OK_cemian_Lb.Text=data["sideOk"]; if(NG_cemian_Lb!=null)NG_cemian_Lb.Text=data["sideNg"]; } long fOk = long.Parse(data.ContainsKey("frontOk")?data["frontOk"]:"0"); long fNg = long.Parse(data.ContainsKey("frontNg")?data["frontNg"]:"0");
					long bOk = long.Parse(data.ContainsKey("backOk")?data["backOk"]:"0"); long bNg = long.Parse(data.ContainsKey("backNg")?data["backNg"]:"0");
					long eOk = long.Parse(data.ContainsKey("endOk")?data["endOk"]:"0"); long eNg = long.Parse(data.ContainsKey("endNg")?data["endNg"]:"0");
					long sOk = long.Parse(data.ContainsKey("sideOk")?data["sideOk"]:"0"); long sNg = long.Parse(data.ContainsKey("sideNg")?data["sideNg"]:"0");
					_frontStation?.RestoreCounts(fOk, fNg); _backStation?.RestoreCounts(bOk, bNg);
					_endFaceStation?.RestoreCounts(eOk, eNg); _sideStation?.RestoreCounts(sOk, sNg);
					Logger.Info("计数已从本地恢复"); })); } catch {} }

		private void SaveSkuParams()
		{
			try
			{
				var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "sku_params.json");
				var data = new Dictionary<string, string>();
				foreach (var kv in new (string, Control)[] {
					("SKU", _skuSearchCombo), ("P", P_Lb), ("Z", Z_Lb), ("MM", MM_Lb),
					("FrontPNumber", FrontPNumber_Lb), ("BackBarcode", BackBarcode_Lb), ("CodingFormat", CodingFormat_Lb)
				}) {
					if (kv.Item2 == null) continue;
					string v = kv.Item2 is ComboBox cb ? cb.Text : kv.Item2.Text;
					if (!string.IsNullOrWhiteSpace(v)) data[kv.Item1] = v;
				}
				File.WriteAllText(path, Newtonsoft.Json.JsonConvert.SerializeObject(data));
			}
			catch (Exception ex) { Logger.Error("保存SKU参数失败: " + ex.Message); }
		}

		private void LoadSkuParams()
		{
			try
			{
				var path = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "sku_params.json");
				if (!File.Exists(path)) return;
				var json = File.ReadAllText(path);
				var data = Newtonsoft.Json.JsonConvert.DeserializeObject<Dictionary<string, string>>(json);
				if (data == null) return;
				this.BeginInvoke(new Action(() => {
					foreach (var kv in data)
					{
						Control ctrl = null;
						switch (kv.Key) {
							case "SKU": ctrl = _skuSearchCombo; break;
							case "P": ctrl = P_Lb; break;
							case "Z": ctrl = Z_Lb; break;
							case "MM": ctrl = MM_Lb; break;
							case "FrontPNumber": ctrl = FrontPNumber_Lb; break;
							case "BackBarcode": ctrl = BackBarcode_Lb; break;
							case "CodingFormat": ctrl = CodingFormat_Lb; break;
						}
						if (ctrl != null) {
							if (ctrl is ComboBox cb) { int idx = cb.FindStringExact(kv.Value); if (idx >= 0) cb.SelectedIndex = idx; else cb.Text = kv.Value; }
							else ctrl.Text = kv.Value;
						}
					}
					if (_currentSku != null) {
						if (data.TryGetValue("P", out string rp2) && int.TryParse(rp2, out int rpi) && rpi > 0) _currentSku.P = rpi;
						if (data.TryGetValue("Z", out string rz2) && int.TryParse(rz2, out int rzi)) _currentSku.Z = rzi;
						if (data.TryGetValue("MM", out string rm2) && int.TryParse(rm2, out int rmi)) _currentSku.MM = rmi;
						if (data.TryGetValue("FrontPNumber", out string fp)) _currentSku.FrontPCode = fp;
						if (data.TryGetValue("BackBarcode", out string bc)) _currentSku.BackBarcode = bc;
						if (data.TryGetValue("CodingFormat", out string cf)) _currentSku.CodingFormat = cf;
					}
					_frontStation?.UpdateSku(_currentSku); _backStation?.UpdateSku(_currentSku);
					_sideStation?.UpdateSku(_currentSku); _endFaceStation?.UpdateSku(_currentSku);
					_endFaceStation?.UpdatePCount(_currentSku?.P ?? 12);
					Logger.Info("SKU参数已从本地恢复并推送到各工位");
				}));
			}
			catch (Exception ex) { Logger.Error("加载SKU参数失败: " + ex.Message); }
		}

		private void SaveAllModelParams()
		{
			try
			{
				_aiModels.FrontBoxBreakModel?.SaveParams(_aiModels.FrontBoxBreakModel.MetaPath);
				_aiModels.EndFaceUpperModel?.SaveParams(_aiModels.EndFaceUpperModel.MetaPath);
				_aiModels.EndFaceLowerModel?.SaveParams(_aiModels.EndFaceLowerModel.MetaPath);
				_aiModels.BackHookModel?.SaveParams(_aiModels.BackHookModel.MetaPath);
				_aiModels.SideDefectModel?.SaveParams(_aiModels.SideDefectModel.MetaPath);
				Logger.Info("模型参数已同步保存到best.json");
			}
			catch (Exception ex) { Logger.Error("保存模型参数失败: " + ex.Message); }
		}

		private void SetupSkuSearch()
		{
			if (SKU_Txt == null) return;
			Logger.Info("初始化SKU搜索控件");

			SKU_Txt.Visible = false;

			// 创建搜索框（Parent未就绪时挂到窗体）
			var comboParent = SKU_Txt.Parent ?? (Control)this;
			_skuSearchCombo = new ComboBox
			{
				Font = new Font("微软雅黑", 10F),
				DropDownStyle = ComboBoxStyle.DropDown,
				Width = SKU_Txt.Width,
				Height = SKU_Txt.Height,
				Location = SKU_Txt.Location,
				Text = ""
			};

			comboParent.Controls.Add(_skuSearchCombo);
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

				// 防卡顿：BeginUpdate/EndUpdate批量更新，避免每Add一次就刷新UI
				var results = _skuDb.Search(keyword);
				_skuSearchCombo.BeginUpdate();
				_skuSearchCombo.Items.Clear();
				foreach (var sku in results)
					_skuSearchCombo.Items.Add(sku.SkuNumber);
				_skuSearchCombo.EndUpdate();

				// 选中文本保持输入内容，避免光标跳到最前面
				if (_skuSearchCombo.Items.Count > 0)
				{
					_skuSearchCombo.DroppedDown = true;
					_skuSearchCombo.Select(keyword.Length, 0);
				}
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
						SaveSkuParams();
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
				Bitmap display;
				int maxW = 1920;
				if (image.Width > maxW)
				{
					float scale = (float)maxW / image.Width;
					int newH = (int)(image.Height * scale);
					display = new Bitmap(image, new DrawSize(maxW, newH));
				}
				else
				{
					display = new Bitmap(image);  // Clone独立副本，避免外部释放导致Paint时"参数无效"
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
			// 总清空
			if (clearBtn != null) clearBtn.Click += (s, e) => ClearAllStatistics();
			// 检测参数
			if (SetDetectionParametersBtn != null)
				SetDetectionParametersBtn.Click += (s, e) => {
					var form = new DetectionParametersForm(_detectionParams);
					form.OnParametersChanged += (s2, e2) => {
						_frontStation?.ReloadModelParams();
						_backStation?.ReloadModelParams();
						_endFaceStation?.ReloadModelParams();
						_sideStation?.ReloadModelParams();
						if (_frontStation != null) { _frontStation.EnablePNumberCheck = _detectionParams.Front.EnablePNumberCheck; _frontStation.EnableBoxBreakCheck = _detectionParams.Front.EnableBoxBreakCheck; }
						if (_backStation != null) { _backStation.EnableBarcodeCheck = _detectionParams.Back.EnableBarcodeCheck; _backStation.EnableHookCheck = _detectionParams.Back.EnableHookCheck; }
						if (_endFaceStation != null) _endFaceStation.EnableUpperDefectCheck = _detectionParams.EndFace.EnableUpperDefectCheck;
						if (_sideStation != null) {
							_sideStation.MotionEnabled = _detectionParams.Side.MotionEnabled;
							_sideStation.EnableSideDefectCheck = _detectionParams.Side.EnableSideDefectCheck;
							_sideStation.SafetyLockPort = _detectionParams.Side.SafetyLockPort;
							_sideStation.SafetyLockActiveHigh = _detectionParams.Side.SafetyLockActiveHigh;
							_sideStation.RecoveryMode = (SideStationProcessor.SafetyRecovery)_detectionParams.Side.SafetyLockRecovery;
						}
						Logger.Info("所有工位ModelParams已重新加载，无需重启");
					};
					form.ShowDialog();
				};
			// SKU保存并立即生效（手动输入优先于CSV数据）
			if (saveBtn != null) saveBtn.Click += (s, e) => {
				string sku = _skuSearchCombo?.Text?.Trim() ?? "";
				if (string.IsNullOrWhiteSpace(sku)) return;
				_currentSku = _skuDb.GetBySkuNumber(sku) ?? new SkuData();
				// 记录修改前值
				int oldP = _currentSku.P, oldZ = _currentSku.Z, oldMM = _currentSku.MM;
				string oldFP = _currentSku.FrontPCode ?? "-", oldBC = _currentSku.BackBarcode ?? "-", oldCF = _currentSku.CodingFormat ?? "-";
				// 手动输入的值覆盖CSV
				if (int.TryParse(P_Lb?.Text, out int pv) && pv > 0) _currentSku.P = pv;
				if (int.TryParse(Z_Lb?.Text, out int zv)) _currentSku.Z = zv;
				if (int.TryParse(MM_Lb?.Text, out int mv)) _currentSku.MM = mv;
				if (FrontPNumber_Lb != null && !string.IsNullOrWhiteSpace(FrontPNumber_Lb.Text)) _currentSku.FrontPCode = FrontPNumber_Lb.Text.Trim();
				if (BackBarcode_Lb != null && !string.IsNullOrWhiteSpace(BackBarcode_Lb.Text)) _currentSku.BackBarcode = BackBarcode_Lb.Text.Trim();
				if (CodingFormat_Lb != null && !string.IsNullOrWhiteSpace(CodingFormat_Lb.Text)) _currentSku.CodingFormat = CodingFormat_Lb.Text.Trim();
				if (_currentSku.P <= 0) _currentSku.P = 12;
				UpdateSkuDisplay();
				// 生成变更摘要
				var changes = new List<string>();
				if (oldP != _currentSku.P) changes.Add("P: " + oldP + " → " + _currentSku.P);
				if (oldZ != _currentSku.Z) changes.Add("Z: " + oldZ + " → " + _currentSku.Z);
				if (oldMM != _currentSku.MM) changes.Add("MM: " + oldMM + " → " + _currentSku.MM);
				if (oldFP != (_currentSku.FrontPCode??"-")) changes.Add("P号: " + oldFP + " → " + _currentSku.FrontPCode);
				if (oldBC != (_currentSku.BackBarcode??"-")) changes.Add("条码: " + oldBC + " → " + _currentSku.BackBarcode);
				if (oldCF != (_currentSku.CodingFormat??"-")) changes.Add("格式: " + oldCF + " → " + _currentSku.CodingFormat);
				string diff = changes.Count > 0 ? "\n\n变更:\n" + string.Join("\n", changes) : "";
				_frontStation?.UpdateSku(_currentSku); _backStation?.UpdateSku(_currentSku);
				_sideStation?.UpdateSku(_currentSku); _endFaceStation?.UpdateSku(_currentSku);
				_endFaceStation?.UpdatePCount(_currentSku.P);
				_detectionParams.LastSkuNumber = sku; _detectionParams.SaveToFile();
				SaveSkuParams(); SaveCounts();
				MessageBox.Show("SKU【" + sku + "】保存成功！\nP=" + _currentSku.P + " Z=" + _currentSku.Z + " MM=" + _currentSku.MM + diff, "保存成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
		};
		// 各工位小清空
			if (Clear_zheng_Btn != null) Clear_zheng_Btn.Click += (ss,ee) => { _frontStation?.ClearCounters(); if(OK_zheng_Lb!=null)OK_zheng_Lb.Text="0"; if(NG_zheng_Lb!=null)NG_zheng_Lb.Text="0"; };
			if (Clear_fan_Btn != null) Clear_fan_Btn.Click += (ss,ee) => { _backStation?.ClearCounters(); if(OK_fan_Lb!=null)OK_fan_Lb.Text="0"; if(NG_fan_Lb!=null)NG_fan_Lb.Text="0"; };
			if (Clear_duanmian_Btn != null) Clear_duanmian_Btn.Click += (ss,ee) => { _endFaceStation?.ClearCounters(); if(OK_duanmian_Lb!=null)OK_duanmian_Lb.Text="0"; if(NG_duanmian_Lb!=null)NG_duanmian_Lb.Text="0"; };
			if (Clear_cemian_Btn != null) Clear_cemian_Btn.Click += (ss,ee) => { _sideStation?.ClearCounters(); if(OK_cemian_Lb!=null)OK_cemian_Lb.Text="0"; if(NG_cemian_Lb!=null)NG_cemian_Lb.Text="0"; };
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

				SaveCounts();
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
			{
				if (d.ShowDialog() != DialogResult.OK) return;
				var left = new Bitmap(d.FileName);
				using (var d2 = new OpenFileDialog { Title = "选择背面右图", Filter = "图像|*.jpg;*.jpeg;*.png;*.bmp;*.tif" })
				{
					if (d2.ShowDialog() != DialogResult.OK) return;
					var right = new Bitmap(d2.FileName);
					_backStation?.UpdateSku(new SkuData { SkuNumber = "MANUAL", P = _currentSku?.P ?? 12, Z = _currentSku?.Z ?? 2, MM = _currentSku?.MM ?? 42, BackBarcode = _currentSku?.BackBarcode, CodingFormat = _currentSku?.CodingFormat, FrontPCode = _currentSku?.FrontPCode });
					_backStation?.OnCam3(left, 0);
					_backStation?.OnCam4(right, 0);
					UIMessageTip.ShowOk(this, "已提交测试");
				}
			}
		}
		#endregion
	}
}
