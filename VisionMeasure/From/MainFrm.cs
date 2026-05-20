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
using Utils;
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

namespace VisionMeasure
{
	public partial class MainFrm : Form
	{
		// ========== 硬件管理层 ==========
		private MotionControlManager _motionMgr;
		private CameraManager _cameraMgr;
		private CameraTriggerManager _triggerMgr;
		private PlcCommunication _plcComm;

		// ========== AI模型管理层 ==========
		private AiModelManager _aiModels;

		// ========== 工位处理器 ==========
		private FrontStationProcessor _frontStation;
		private EndFaceStationProcessor _endFaceStation;
		private BackStationProcessor _backStation;
		private SideStationProcessor _sideStation;

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

		public MainFrm()
		{
			InitializeComponent();
			this.Load += MainFrm_Load;
			this.FormClosing += MainFrm_FormClosing;
		}

		#region 窗体加载

		private async void MainFrm_Load(object sender, EventArgs e)
		{
			try
			{
				Logger.Info("========== 系统启动 ==========");

				// 初始化数据库
				Logger.Info("正在初始化数据库...");
				_dbHelper = new SQLiteHelper();

				// 加载检测参数
				Logger.Info("正在加载检测参数...");
				_detectionParams = DetectionParameters.Instance;

				// 初始化SKU数据库
				Logger.Info("正在加载SKU数据...");
				_skuDb = new SkuDatabase();
				_skuDb.LoadData();

				// 初始化性能监控
				Logger.Info("正在初始化性能监控...");
				_perfMonitor = new PerformanceMonitor();

				// 初始化高速保存器
				Logger.Info("正在初始化图像保存器...");
				_imageSaver = new HighSpeedImageSaver("主保存器", 4, 500);

				// 初始化硬件
				Logger.Info("正在初始化硬件...");
				InitHardware();

				// 初始化AI模型
				Logger.Info("正在加载AI模型...");
				//InitAiModels();

				// 初始化工位处理器
				Logger.Info("正在初始化工位处理器...");
				InitStations();

				// 初始化UI
				Logger.Info("正在初始化界面...");
				InitUI();

				// 绑定统计控件
				Logger.Info("正在绑定统计控件...");
				BindStatisticsControls();

				// 启动班次检查
				StartShiftCheckTimer();

				// 刷新显示
				RefreshCarouselDisplays();

				_skuDb.LoadData();

				// 验证数据是否正确加载
				var testSku = _skuDb.GetBySkuNumber("181712303");
				if (testSku != null)
				{
					Logger.Info($"测试SKU: {testSku.SkuNumber}, P={testSku.P}, Z={testSku.Z}, MM={testSku.MM}");
				}

				this.WindowState = FormWindowState.Maximized;

				Logger.Info("系统初始化完成");
			}
			catch (Exception ex)
			{
				Logger.Error($"系统初始化失败: {ex.Message}\r\n{ex.StackTrace}");
				MessageBox.Show($"系统初始化失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
				Application.Exit();
				//test
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

			if (_motionMgr.Connect())
			{
				_motionMgr.InitAxes();
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

			// 相机管理
			_cameraMgr = new CameraManager(useSimulateMode);
			_cameraMgr.OnImageReceived += OnCameraImageReceived;
			_cameraMgr.OnConnectionChanged += OnCameraConnectionChanged;

			if (_cameraMgr.InitializeAll())
			{
				_cameraMgr.StartAll();
				Logger.Info("相机初始化成功");
			}
			else
			{
				Logger.Warning("部分相机初始化失败");
			}

			// 触发管理器
			if (_motionMgr.IsConnected && !useSimulateMode)
			{
				_triggerMgr = new CameraTriggerManager(_motionMgr, useSimulateMode);
				_triggerMgr.OnTriggered += OnCameraTriggered;
				_triggerMgr.Start();
				Logger.Info("触发管理器已启动");
			}
			else
			{
				Logger.Warning("运动控制卡未连接或模拟模式，跳过触发管理器初始化");
			}
		}
		/// <summary>
		/// 相机触发回调 - 当检测到触发信号并输出脉冲后调用
		/// </summary>
		/// <param name="cameraId">被触发的相机ID</param>
		private void OnCameraTriggered(int cameraId)
		{
			// 记录触发日志（生产环境建议注释掉，避免日志过多）
			Logger.Debug($"相机{cameraId} 触发脉冲已发送", "Trigger");

			// 可选：更新UI上的触发指示灯
			this.Invoke(new Action(() =>
			{
				switch (cameraId)
				{
					case 1:
						// 更新正面左相机触发指示灯
						break;
					case 2:
						// 更新正面右相机触发指示灯
						break;
					case 3:
						// 更新背面左相机触发指示灯
						break;
					case 4:
						// 更新背面右相机触发指示灯
						break;
					case 5:
						// 更新上端面相机触发指示灯
						break;
					case 6:
						// 更新下端面相机触发指示灯
						break;
					case 7:
						// 更新左侧面相机触发指示灯
						break;
					case 8:
						// 更新右侧面相机触发指示灯
						break;
				}
			}));

			// 可选：统计触发次数
			// _triggerCount[cameraId]++;
		}
		private void InitAiModels()
		{
			var modelConfig = ModelPathConfig.LoadFromSysConfig();
			_aiModels = new AiModelManager(modelConfig);
			//_aiModels.LoadAllModels();
		}

		private void InitStations()
		{
			string imgPath = _detectionParams.Save.ImageSavePath;
			_currentSku = _skuDb.Search("").FirstOrDefault() ?? new SkuData { P = 8, Z = 2, MM = 42 };

			_frontStation = new FrontStationProcessor(_aiModels, imgPath, _currentSku, _imageSaver, _perfMonitor);
			_frontStation.OnResultReady += OnStationResult;
			_frontStation.Start();

			_endFaceStation = new EndFaceStationProcessor(_aiModels, imgPath, _currentSku.P, _imageSaver, _perfMonitor);
			_endFaceStation.OnResultReady += OnStationResult;
			_endFaceStation.OnStatusUpdate += OnEndFaceStatusUpdate;
			_endFaceStation.Start();

			_backStation = new BackStationProcessor(_aiModels, imgPath, _currentSku, _imageSaver, _perfMonitor);
			_backStation.OnResultReady += OnStationResult;
			_backStation.Start();

			_sideStation = new SideStationProcessor(_aiModels, imgPath, _currentSku, _motionMgr, _imageSaver, _perfMonitor);
			_sideStation.OnResultReady += OnStationResult;
			_sideStation.OnStatusUpdate += OnSideStatusUpdate;
			_sideStation.Start();
		}

		#endregion

		#region 相机回调

		private void OnCameraImageReceived(int cameraId, Bitmap image)
		{
			if (_isClosing || image == null) return;
			long pid = Interlocked.Increment(ref _productIdCounter);

			switch (cameraId)
			{
				case 1: _frontStation?.OnCam1(image, pid); UpdatePictureBox(xlPictureBox1, image); break;
				case 2: _frontStation?.OnCam2(image, pid); UpdatePictureBox(xlPictureBox2, image); break;
				case 3: _backStation?.OnCam3(image, pid); UpdatePictureBox(xlPictureBox3, image); break;
				case 4: _backStation?.OnCam4(image, pid); UpdatePictureBox(xlPictureBox4, image); break;
				case 5: _endFaceStation?.OnCam5(image, pid); break;
				case 6: _endFaceStation?.OnCam6(image, pid); break;
				case 7: _sideStation?.OnCam7(image, pid); break;
				case 8: _sideStation?.OnCam8(image, pid); break;
			}
		}

		private void OnCameraConnectionChanged(int cameraId, bool isConnected)
		{
			this.Invoke(new Action(() =>
			{
				var state = isConnected ? UILightState.On : UILightState.Off;
				switch (cameraId)
				{
					case 1: if (camera1State != null) camera1State.State = state; break;
					case 2: if (camera2State != null) camera2State.State = state; break;
					case 3: if (camera3State != null) camera3State.State = state; break;
					case 4: if (camera4State != null) camera4State.State = state; break;
					case 5: if (camera5State != null) camera5State.State = state; break;
					case 6: if (camera5State != null) camera6State.State = state; break;
					case 7: if (camera5State != null) camera7State.State = state; break;
					case 8: if (camera5State != null) camera8State.State = state; break;
				}
			}));
		}

		#endregion

		#region 工位结果回调

		private void OnStationResult(ProductResult result)
		{
			if (result.IsComplete)
			{
				this.Invoke(new Action(() =>
				{
					UpdateStatistics(result);
				}));
			}
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
			this.Invoke(new Action(() =>
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
			this.Invoke(new Action(() =>
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
				this.Invoke(new Action(() =>
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
						_sideStation?.UpdateSku(_currentSku);
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
				// 刷新端面轮播图
				if (_endFaceStation != null)
				{
					var displayMat = _endFaceStation.GetCurrentDisplayImage();
					if (displayMat != null && !displayMat.Empty())
					{
						var displayBitmap = BmpConverter.ToBitmap(displayMat);
						UpdatePictureBox(xlPictureBox3, displayBitmap);
						displayMat.Dispose();
					}
				}

				// 刷新侧面轮播图
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
			if (pb == null) return;

			if (pb.InvokeRequired)
			{
				pb.Invoke(new Action(() => UpdatePictureBox(pb, image)));
				return;
			}

			try
			{
				var old = pb.Image;
				pb.Image = image;
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
				return "Night";
			if (now >= TimeSpan.Parse("08:00:00") && now <= TimeSpan.Parse("15:59:59"))
				return "Morning";
			return "Afternoon";
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

				_frontStation?.Dispose();
				_backStation?.Dispose();
				_endFaceStation?.Dispose();
				_sideStation?.Dispose();

				_cameraMgr?.Dispose();
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
		/// 获取相机管理器
		/// </summary>
		public CameraManager GetCameraManager()
		{
			return _cameraMgr;
		}
		#endregion
	}
}