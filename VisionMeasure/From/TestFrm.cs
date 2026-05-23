using Config;
using Detection;
using Hardware;
using Models;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using Sunny.UI;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using VisionMeasure.Utils;
using CommonLib;
using XL.Controls;
using BmpConverter = OpenCvSharp.Extensions.BitmapConverter;
// 解决 Point 和 Size 二义性问题
using CvPoint = OpenCvSharp.Point;
using CvRect = OpenCvSharp.Rect;
using CvSize = OpenCvSharp.Size;
using DrawPoint = System.Drawing.Point;
using DrawSize = System.Drawing.Size;

namespace UI
{
	public partial class TestForm : UIForm
	{
		private readonly MotionControlManager _motion;
		private readonly CameraManager _cameraMgr;
		private AiModelManager _aiModels;
		private SkuData _currentSku;

		// 当前加载的图像
		private Mat _currentLeftMat;
		private Mat _currentRightMat;
		private Mat _currentUpperMat;
		private Mat _currentLowerMat;
		private Mat _currentSideMat;

		// 当前工位选择
		private string _currentStation = "正面";

		// TabControl
		private TabControl _mainTab;
		private TabPage _tabStationTest;
		private TabPage _tabModelTest;

		// ========== 工位测试页控件 ==========
		private UIComboBox _stationCombo;
		private UIButton _loadImageBtn;
		private UIButton _runTestBtn;
		private XLPictureBox _picInputLeft;
		private XLPictureBox _picInputRight;
		private XLPictureBox _picResult;
		private UILabel _resultLabel;
		private UILabel _detailLabel;
		private UILabel _timeLabel;
		private UIListBox _defectListBox;
		private UITextBox _logTextBox;

		// ========== 模型单独测试页控件 ==========
		private UIComboBox _modelCombo;
		private UIButton _loadModelBtn;
		private UIButton _loadModelImageBtn;
		private UIButton _runModelBtn;
		private XLPictureBox _picModelInput;
		private XLPictureBox _picModelOutput;
		private UILabel _modelInfoLabel;
		private UILabel _modelTimeLabel;
		private UITextBox _modelResultBox;
		private UITextBox _modelLogTextBox;

		// ========== 状态栏 ==========
		private UILight _testStateLight;
		private UILabel _statusLabel;
		private UILabel _memLabel;

		public TestForm(MotionControlManager motion, CameraManager cameraMgr, AiModelManager aiModels = null)
		{
			_motion = motion;
			_cameraMgr = cameraMgr;
			_aiModels = aiModels;
			InitializeComponent();

			// 延迟加载，等待窗体完全创建
			this.Load += TestForm_Load;
		}

		private void TestForm_Load(object sender, EventArgs e)
		{
			if (_aiModels == null)
			{
				LoadAiModels();
			}
			else
			{
				AddLog("使用主界面的AI模型实例");
				UpdateUI(() =>
				{
					_statusLabel.Text = "AI模型已就绪（使用主界面实例）";
				});
			}
			InitSkuData();
		}

		private void InitializeComponent()
		{
			this.Text = "KOCH 检测测试工具";
			this.Size = new DrawSize(1400, 900);
			this.StartPosition = FormStartPosition.CenterParent;

			// 主TabControl
			_mainTab = new TabControl
			{
				Dock = DockStyle.Fill,
				Font = new Font("微软雅黑", 11F)
			};

			// ========== 工位测试页 ==========
			_tabStationTest = new TabPage("工位测试");
			BuildStationTestPage();
			_mainTab.TabPages.Add(_tabStationTest);

			// ========== 模型单独测试页 ==========
			_tabModelTest = new TabPage("模型单独测试");
			BuildModelTestPage();
			_mainTab.TabPages.Add(_tabModelTest);

			this.Controls.Add(_mainTab);

			// 底部状态栏
			var bottomPanel = new UIPanel
			{
				Dock = DockStyle.Bottom,
				Height = 38,
				FillColor = Color.FromArgb(47, 60, 76)
			};

			_testStateLight = new UILight
			{
				Location = new DrawPoint(10, 8),
				Size = new DrawSize(100, 22),
				Font = new Font("微软雅黑", 12F),
				ForeColor = Color.White,
				State = UILightState.On,
				Text = "测试工具"
			};
			bottomPanel.Controls.Add(_testStateLight);

			_statusLabel = new UILabel
			{
				Location = new DrawPoint(120, 8),
				Size = new DrawSize(800, 22),
				Font = new Font("微软雅黑", 11F),
				ForeColor = Color.White,
				Text = "就绪"
			};
			bottomPanel.Controls.Add(_statusLabel);

			_memLabel = new UILabel
			{
				Location = new DrawPoint(1150, 8),
				Size = new DrawSize(230, 22),
				Font = new Font("微软雅黑", 11F),
				ForeColor = Color.White,
				Text = "内存: -- MB"
			};
			bottomPanel.Controls.Add(_memLabel);

			this.Controls.Add(bottomPanel);

			// 内存监控
			var memTimer = new System.Windows.Forms.Timer { Interval = 3000 };
			memTimer.Tick += (s, e) =>
			{
				if (_memLabel != null && !_memLabel.IsDisposed)
				{
					_memLabel.Text = $"内存: {GC.GetTotalMemory(false) / 1024 / 1024} MB";
				}
			};
			memTimer.Start();
		}

		#region 工位测试页

		private void BuildStationTestPage()
		{
			var mainPanel = new UIPanel { Dock = DockStyle.Fill, FillColor = Color.White };

			// 左侧控制面板
			var leftPanel = new UIPanel
			{
				Location = new DrawPoint(10, 10),
				Size = new DrawSize(300, 820),
				FillColor = Color.White,
				RectColor = Color.FromArgb(47, 60, 76),
				RectSize = 1
			};

			int y = 15;

			var stationLabel = new UILabel
			{
				Text = "选择工位:",
				Location = new DrawPoint(15, y + 3),
				Size = new DrawSize(80, 25),
				Font = new Font("微软雅黑", 11F, FontStyle.Bold)
			};
			_stationCombo = new UIComboBox
			{
				Location = new DrawPoint(100, y),
				Size = new DrawSize(180, 29),
				DropDownStyle = UIDropDownStyle.DropDownList
			};
			_stationCombo.Items.AddRange(new object[] { "正面", "背面", "上端面", "下端面", "侧面" });
			_stationCombo.SelectedIndex = 0;
			_stationCombo.SelectedIndexChanged += StationCombo_SelectedIndexChanged;
			y += 42;

			_loadImageBtn = new UIButton
			{
				Text = "加载测试图像",
				Location = new DrawPoint(15, y),
				Size = new DrawSize(270, 40),
				Font = new Font("微软雅黑", 10F, FontStyle.Bold),
				Radius = 10,
				Cursor = Cursors.Hand
			};
			_loadImageBtn.Click += LoadImageBtn_Click;
			y += 50;

			_runTestBtn = new UIButton
			{
				Text = "执行检测",
				Location = new DrawPoint(15, y),
				Size = new DrawSize(270, 45),
				Font = new Font("微软雅黑", 12F, FontStyle.Bold),
				Radius = 10,
				FillColor = Color.FromArgb(39, 174, 96),
				Cursor = Cursors.Hand,
				Enabled = false
			};
			_runTestBtn.Click += RunTestBtn_Click;
			y += 55;

			var resultGroup = new UIGroupBox
			{
				Text = "检测结果",
				Location = new DrawPoint(10, y),
				Size = new DrawSize(280, 100),
				Font = new Font("微软雅黑", 10F, FontStyle.Bold)
			};
			_resultLabel = new UILabel
			{
				Text = "等待检测...",
				Location = new DrawPoint(10, 25),
				Size = new DrawSize(260, 30),
				Font = new Font("微软雅黑", 14F, FontStyle.Bold),
				TextAlign = ContentAlignment.MiddleCenter,
				ForeColor = Color.FromArgb(100, 100, 100)
			};
			_detailLabel = new UILabel
			{
				Text = "",
				Location = new DrawPoint(10, 60),
				Size = new DrawSize(260, 30),
				Font = new Font("微软雅黑", 10F),
				TextAlign = ContentAlignment.MiddleCenter
			};
			resultGroup.Controls.Add(_resultLabel);
			resultGroup.Controls.Add(_detailLabel);
			y += 110;

			var defectLabel = new UILabel
			{
				Text = "检测到的缺陷:",
				Location = new DrawPoint(15, y),
				Size = new DrawSize(150, 25),
				Font = new Font("微软雅黑", 10F, FontStyle.Bold)
			};
			y += 30;

			_defectListBox = new UIListBox
			{
				Location = new DrawPoint(15, y),
				Size = new DrawSize(270, 120),
				Font = new Font("微软雅黑", 9F)
			};
			y += 130;

			_timeLabel = new UILabel
			{
				Text = "耗时: -- ms",
				Location = new DrawPoint(15, y),
				Size = new DrawSize(270, 25),
				Font = new Font("微软雅黑", 10F),
				BackColor = Color.FromArgb(255, 255, 200),
				TextAlign = ContentAlignment.MiddleCenter
			};
			y += 35;

			var logLabel = new UILabel
			{
				Text = "运行日志:",
				Location = new DrawPoint(15, y),
				Size = new DrawSize(150, 25),
				Font = new Font("微软雅黑", 10F, FontStyle.Bold)
			};
			y += 30;

			_logTextBox = new UITextBox
			{
				Location = new DrawPoint(15, y),
				Size = new DrawSize(270, 200),
				Font = new Font("微软雅黑", 8F),
				Multiline = true
			};

			leftPanel.Controls.AddRange(new Control[] {
				stationLabel, _stationCombo,
				_loadImageBtn, _runTestBtn,
				resultGroup, defectLabel, _defectListBox, _timeLabel,
				logLabel, _logTextBox
			});

			// 右侧图像显示区域
			var rightPanel = new UIPanel
			{
				Location = new DrawPoint(320, 10),
				Size = new DrawSize(1050, 820),
				FillColor = Color.White,
				RectColor = Color.FromArgb(47, 60, 76),
				RectSize = 1
			};

			var inputGroup = new UIGroupBox
			{
				Text = "输入图像",
				Location = new DrawPoint(10, 10),
				Size = new DrawSize(500, 400),
				Font = new Font("微软雅黑", 10F, FontStyle.Bold)
			};
			_picInputLeft = new XLPictureBox
			{
				Location = new DrawPoint(10, 25),
				Size = new DrawSize(230, 360),
				BackColor1 = Color.FromArgb(60, 60, 60),
				BackColor2 = Color.FromArgb(80, 80, 80),
				BackgroundGridSize = 15
			};
			_picInputRight = new XLPictureBox
			{
				Location = new DrawPoint(250, 25),
				Size = new DrawSize(230, 360),
				BackColor1 = Color.FromArgb(60, 60, 60),
				BackColor2 = Color.FromArgb(80, 80, 80),
				BackgroundGridSize = 15
			};
			inputGroup.Controls.Add(_picInputLeft);
			inputGroup.Controls.Add(_picInputRight);

			var resultGroupBox = new UIGroupBox
			{
				Text = "检测结果",
				Location = new DrawPoint(520, 10),
				Size = new DrawSize(520, 800),
				Font = new Font("微软雅黑", 10F, FontStyle.Bold)
			};
			_picResult = new XLPictureBox
			{
				Location = new DrawPoint(10, 25),
				Size = new DrawSize(500, 760),
				BackColor1 = Color.FromArgb(60, 60, 60),
				BackColor2 = Color.FromArgb(80, 80, 80),
				BackgroundGridSize = 15
			};
			resultGroupBox.Controls.Add(_picResult);

			rightPanel.Controls.Add(inputGroup);
			rightPanel.Controls.Add(resultGroupBox);

			mainPanel.Controls.Add(leftPanel);
			mainPanel.Controls.Add(rightPanel);
			_tabStationTest.Controls.Add(mainPanel);
		}

		private void SafeInvoke(Action action)
		{
			if (this.IsDisposed || !this.IsHandleCreated)
			{
				return;
			}

			if (this.InvokeRequired)
			{
				try
				{
					this.Invoke(action);
				}
				catch (ObjectDisposedException)
				{
					// 忽略
				}
			}
			else
			{
				action();
			}
		}

		private void AddLog(string message)
		{
			if (_logTextBox == null || _logTextBox.IsDisposed) return;

			SafeInvoke(() =>
			{
				string timeStr = DateTime.Now.ToString("HH:mm:ss.fff");
				_logTextBox.AppendText($"[{timeStr}] {message}\n");
				if (_logTextBox.TextLength > 5000)
				{
					_logTextBox.Text = _logTextBox.Text.Substring(_logTextBox.TextLength - 4000);
				}
			});
			Logger.Info(message);
		}

		private void AddModelLog(string message)
		{
			if (_modelLogTextBox == null || _modelLogTextBox.IsDisposed) return;

			SafeInvoke(() =>
			{
				string timeStr = DateTime.Now.ToString("HH:mm:ss.fff");
				_modelLogTextBox.AppendText($"[{timeStr}] {message}\n");
				if (_modelLogTextBox.TextLength > 5000)
				{
					_modelLogTextBox.Text = _modelLogTextBox.Text.Substring(_modelLogTextBox.TextLength - 4000);
				}
			});
			Logger.Info($"[模型测试] {message}");
		}

		private void UpdateUI(Action action)
		{
			if (this.IsDisposed || !this.IsHandleCreated) return;

			if (this.InvokeRequired)
			{
				try
				{
					this.Invoke(action);
				}
				catch (ObjectDisposedException) { }
			}
			else
			{
				action();
			}
		}

		private void StationCombo_SelectedIndexChanged(object sender, EventArgs e)
		{
			_currentStation = _stationCombo.SelectedItem.ToString();

			SafeInvoke(() =>
			{
				_picInputLeft.Image = null;
				_picInputRight.Image = null;
				_picResult.Image = null;
				_resultLabel.Text = "等待检测...";
				_resultLabel.ForeColor = Color.FromArgb(100, 100, 100);
				_defectListBox.Items.Clear();
				_detailLabel.Text = "";
			});

			_currentLeftMat?.Dispose();
			_currentRightMat?.Dispose();
			_currentUpperMat?.Dispose();
			_currentLowerMat?.Dispose();
			_currentSideMat?.Dispose();
			_currentLeftMat = null;
			_currentRightMat = null;

			AddLog($"切换到 {_currentStation} 工位");
			UpdateUI(() => _statusLabel.Text = $"已切换到 {_currentStation} 工位");
		}

		private void LoadImageBtn_Click(object sender, EventArgs e)
		{
			using (var ofd = new OpenFileDialog())
			{
				ofd.Title = $"选择{_currentStation}工位测试图像";
				ofd.Filter = "图像文件|*.jpg;*.png;*.bmp;*.jpeg";

				if (_currentStation == "正面" || _currentStation == "背面")
				{
					ofd.Multiselect = true;
					ofd.Title = $"选择{_currentStation}工位左右两张图像（左图+右图）";
				}

				if (ofd.ShowDialog() == DialogResult.OK)
				{
					try
					{
						switch (_currentStation)
						{
							case "正面":
							case "背面":
								if (ofd.FileNames.Length >= 2)
								{
									_currentLeftMat?.Dispose();
									_currentRightMat?.Dispose();
									_currentLeftMat = Cv2.ImRead(ofd.FileNames[0]);
									_currentRightMat = Cv2.ImRead(ofd.FileNames[1]);

									var leftBmp = BmpConverter.ToBitmap(_currentLeftMat);
									var rightBmp = BmpConverter.ToBitmap(_currentRightMat);

									UpdateUI(() =>
									{
										_picInputLeft.Image = leftBmp;
										_picInputRight.Image = rightBmp;
									});

									AddLog($"已加载左图: {Path.GetFileName(ofd.FileNames[0])}, 尺寸: {_currentLeftMat.Width}x{_currentLeftMat.Height}");
									AddLog($"已加载右图: {Path.GetFileName(ofd.FileNames[1])}, 尺寸: {_currentRightMat.Width}x{_currentRightMat.Height}");
									UpdateUI(() => _statusLabel.Text = $"已加载左图: {Path.GetFileName(ofd.FileNames[0])}, 右图: {Path.GetFileName(ofd.FileNames[1])}");
								}
								else
								{
									MessageBox.Show("正面/背面工位需要选择左右两张图像！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
								}
								break;

							case "上端面":
							case "下端面":
								_currentUpperMat?.Dispose();
								_currentUpperMat = Cv2.ImRead(ofd.FileName);
								var bmp = BmpConverter.ToBitmap(_currentUpperMat);
								UpdateUI(() => _picInputLeft.Image = bmp);
								AddLog($"已加载图像: {Path.GetFileName(ofd.FileName)}, 尺寸: {_currentUpperMat.Width}x{_currentUpperMat.Height}");
								UpdateUI(() => _statusLabel.Text = $"已加载: {Path.GetFileName(ofd.FileName)}");
								break;

							case "侧面":
								_currentSideMat?.Dispose();
								_currentSideMat = Cv2.ImRead(ofd.FileName);
								var sideBmp = BmpConverter.ToBitmap(_currentSideMat);
								UpdateUI(() => _picInputLeft.Image = sideBmp);
								AddLog($"已加载图像: {Path.GetFileName(ofd.FileName)}, 尺寸: {_currentSideMat.Width}x{_currentSideMat.Height}");
								UpdateUI(() => _statusLabel.Text = $"已加载: {Path.GetFileName(ofd.FileName)}");
								break;
						}

						UpdateUI(() =>
						{
							_runTestBtn.Enabled = true;
							_resultLabel.Text = "等待检测...";
							_resultLabel.ForeColor = Color.FromArgb(100, 100, 100);
							_defectListBox.Items.Clear();
						});
					}
					catch (Exception ex)
					{
						AddLog($"加载图像失败: {ex.Message}");
						Logger.Error($"加载图像失败: {ex.Message}");
						MessageBox.Show($"加载失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
					}
				}
			}
		}

		private async void RunTestBtn_Click(object sender, EventArgs e)
		{
			if (!CheckImageLoaded()) return;
			if (_aiModels == null)
			{
				MessageBox.Show("AI模型未加载，请先加载模型！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}

			UpdateUI(() => _runTestBtn.Enabled = false);
			UpdateUI(() => _statusLabel.Text = "正在检测...");
			AddLog($"========== 开始 {_currentStation} 工位检测 ==========");
			var sw = System.Diagnostics.Stopwatch.StartNew();

			try
			{
				await Task.Run(() =>
				{
					switch (_currentStation)
					{
						case "正面":
							RunFrontTest();
							break;
						case "背面":
							RunBackTest();
							break;
						case "上端面":
							RunUpperTest();
							break;
						case "下端面":
							RunLowerTest();
							break;
						case "侧面":
							RunSideTest();
							break;
					}
				});

				sw.Stop();
				UpdateUI(() => _timeLabel.Text = $"耗时: {sw.ElapsedMilliseconds} ms");
				AddLog($"检测完成，总耗时: {sw.ElapsedMilliseconds} ms");
				UpdateUI(() => _statusLabel.Text = $"检测完成 - {_currentStation}工位");
			}
			catch (Exception ex)
			{
				AddLog($"检测失败: {ex.Message}");
				Logger.Error($"检测失败: {ex.Message}");
				UpdateUI(() =>
				{
					_resultLabel.Text = "检测失败";
					_resultLabel.ForeColor = Color.Red;
					_statusLabel.Text = $"检测失败: {ex.Message}";
				});
				MessageBox.Show($"检测失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			finally
			{
				UpdateUI(() => _runTestBtn.Enabled = true);
			}
		}

		private bool CheckImageLoaded()
		{
			switch (_currentStation)
			{
				case "正面":
				case "背面":
					if (_currentLeftMat == null || _currentRightMat == null)
					{
						MessageBox.Show("请先加载左右两张图像！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
						return false;
					}
					break;
				case "上端面":
				case "下端面":
					if (_currentUpperMat == null)
					{
						MessageBox.Show("请先加载图像！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
						return false;
					}
					break;
				case "侧面":
					if (_currentSideMat == null)
					{
						MessageBox.Show("请先加载图像！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
						return false;
					}
					break;
			}
			return true;
		}

		private void RunFrontTest()
		{
			AddLog("正面工位检测开始...");

			int halfP = _currentSku.P / 2;
			int h = _currentLeftMat.Height;
			int w = _currentLeftMat.Width;
			int roiHeight = h / 4;
			int boxWidth = w / halfP;

			var statusList = new List<string>(_currentSku.P);
			for (int i = 0; i < _currentSku.P; i++) statusList.Add("OK");

			AddLog($"P数: {_currentSku.P}, halfP: {halfP}, 图像尺寸: {w}x{h}");

			Mat resultImage = null;

			if (_aiModels.FrontOcrModel != null)
			{
				AddLog("开始P号码OCR识别...");
				int detectedCount = 0;

				for (int i = 0; i < halfP; i++)
				{
					int x = i * boxWidth;
					int y = h - roiHeight;
					var roi = new CvRect(x, y, boxWidth, roiHeight);
					using (var roiMat = new Mat(_currentLeftMat, roi))
					using (var rotated = new Mat())
					{
						Cv2.Rotate(roiMat, rotated, RotateFlags.Rotate90Clockwise);
						var ocrResult = _aiModels.FrontOcrModel.RunOrderOcr(rotated);
						string recognizedText = "";
						float confidence = 0;
						if (ocrResult != null && ocrResult.Blocks != null && ocrResult.Blocks.Any())
						{
							recognizedText = string.Join("", ocrResult.Blocks.Select(b => b.Label));
							confidence = ocrResult.Blocks.First().Score;
						}
						AddLog($"左侧盒子{i + 1}: 识别到 [{recognizedText}], 置信度={confidence:F2}, 标准={_currentSku.FrontPCode}");
						if (!string.IsNullOrEmpty(recognizedText) && recognizedText != _currentSku.FrontPCode)
						{
							statusList[i] = "P号码错误";
							detectedCount++;
						}
					}
				}

				for (int i = 0; i < halfP; i++)
				{
					int boxIndex = halfP + i;
					int x = i * boxWidth;
					int y = h - roiHeight;
					var roi = new CvRect(x, y, boxWidth, roiHeight);
					using (var roiMat = new Mat(_currentRightMat, roi))
					using (var rotated = new Mat())
					{
						Cv2.Rotate(roiMat, rotated, RotateFlags.Rotate90Clockwise);
						var ocrResult = _aiModels.FrontOcrModel.RunOrderOcr(rotated);
						string recognizedText = "";
						float confidence = 0;
						if (ocrResult != null && ocrResult.Blocks != null && ocrResult.Blocks.Any())
						{
							recognizedText = string.Join("", ocrResult.Blocks.Select(b => b.Label));
							confidence = ocrResult.Blocks.First().Score;
						}
						AddLog($"右侧盒子{i + 1}: 识别到 [{recognizedText}], 置信度={confidence:F2}, 标准={_currentSku.FrontPCode}");
						if (!string.IsNullOrEmpty(recognizedText) && recognizedText != _currentSku.FrontPCode)
						{
							statusList[boxIndex] = "P号码错误";
							detectedCount++;
						}
					}
				}

				AddLog($"P号码识别完成，发现 {detectedCount} 个错误");

				resultImage = ResultDrawer.DrawOcrResult(_currentLeftMat, null, _currentSku.FrontPCode);
			}
			else
			{
				AddLog("警告: 正面P号码OCR模型未加载！");
				resultImage = _currentLeftMat.Clone();
				Cv2.PutText(resultImage, "OCR Model Not Loaded", new OpenCvSharp.Point(10, 30),
					HersheyFonts.HersheySimplex, 1.0, new Scalar(0, 0, 255), 2);
			}

			bool isOk = statusList.All(s => s == "OK");

			UpdateUI(() =>
			{
				_resultLabel.Text = isOk ? "合格 (OK)" : "不合格 (NG)";
				_resultLabel.ForeColor = isOk ? Color.Green : Color.Red;

				_defectListBox.Items.Clear();
				for (int i = 0; i < statusList.Count; i++)
				{
					if (statusList[i] != "OK")
					{
						_defectListBox.Items.Add($"盒子 #{i + 1}: {statusList[i]}");
					}
				}
				if (_defectListBox.Items.Count == 0)
				{
					_defectListBox.Items.Add("无缺陷");
				}
				_detailLabel.Text = $"P={_currentSku.P}, 检测盒子数: {statusList.Count}";

				if (resultImage != null)
				{
					var bmp = BmpConverter.ToBitmap(resultImage);
					_picResult.Image = bmp;
					resultImage.Dispose();
				}
			});

			AddLog($"正面检测结果: {(isOk ? "OK" : "NG")}");
		}

		private void RunBackTest()
		{
			AddLog("背面工位检测开始...");

			var statusList = new List<string>(_currentSku.P);
			for (int i = 0; i < _currentSku.P; i++) statusList.Add("OK");

			Detection.HookInspectionOutput result = null;

			if (_aiModels.BackHookModel != null && _aiModels.HookSlightModel != null)
			{
				AddLog("开始挂钩错位检测...");

				result = HookDamageDetector.CheckAllHookDamages(
					_currentLeftMat, _currentRightMat, _currentSku.P,
					_aiModels.BackHookModel, _aiModels.HookSlightModel,
					thicknessThreshold: 30.0,
					blueAreaClassId: 0, hangHoleClassId: 1);

				for (int i = 0; i < result.HookStatus.Count; i++)
				{
					if (result.HookStatus[i] != "缺少" && result.HookStatus[i] != "OK")
					{
						statusList[i] = result.HookStatus[i];
						AddLog($"盒子{i + 1}: {result.HookStatus[i]}");
					}
				}
			}
			else
			{
				AddLog("警告: 挂钩检测模型未加载！");
			}

			bool isOk = statusList.All(s => s == "OK");

			Mat resultImage = ResultDrawer.DrawHookDamageResult(_currentLeftMat, _currentRightMat, result);

			UpdateUI(() =>
			{
				_resultLabel.Text = isOk ? "合格 (OK)" : "不合格 (NG)";
				_resultLabel.ForeColor = isOk ? Color.Green : Color.Red;

				_defectListBox.Items.Clear();
				for (int i = 0; i < statusList.Count; i++)
				{
					if (statusList[i] != "OK")
					{
						_defectListBox.Items.Add($"盒子 #{i + 1}: {statusList[i]}");
					}
				}
				if (_defectListBox.Items.Count == 0)
				{
					_defectListBox.Items.Add("无缺陷");
				}

				if (resultImage != null)
				{
					var bmp = BmpConverter.ToBitmap(resultImage);
					_picResult.Image = bmp;
					resultImage.Dispose();
				}
			});

			AddLog($"背面检测结果: {(isOk ? "OK" : "NG")}");
		}

		private void RunUpperTest()
		{
			AddLog("上端面缺陷检测开始...");

			if (_aiModels.EndFaceUpperModel == null)
			{
				AddLog("错误: 上端面模型未加载！");
				UpdateUI(() =>
				{
					_resultLabel.Text = "模型未加载";
					_resultLabel.ForeColor = Color.Red;
					_defectListBox.Items.Clear();
					_defectListBox.Items.Add("上端面模型未加载");
				});
				return;
			}

			var results = _aiModels.EndFaceUpperModel.PredictBatch(new List<Mat> { _currentUpperMat }, 0.5f, 0.2f);

			var defects = new List<string>();
			if (results != null && results.Count > 0 && results[0].Boxes != null && results[0].Boxes.Length > 0)
			{
				AddLog($"检测到 {results[0].Boxes.Length} 个缺陷");
				for (int i = 0; i < results[0].Boxes.Length; i++)
				{
					int classId = results[0].ClassIds[i];
					string defectType;
					if (classId == 0) defectType = "搭舌缺陷";
					else if (classId == 1) defectType = "边缘问题";
					else if (classId == 2) defectType = "破损";
					else defectType = $"未知缺陷{classId}";
					defects.Add(defectType);
				}
			}
			else
			{
				AddLog("未检测到缺陷");
			}

			bool isOk = defects.Count == 0;

			Mat resultImage = ResultDrawer.DrawEndFaceResult(_currentUpperMat, results != null && results.Count > 0 ? results[0] : null);

			UpdateUI(() =>
			{
				_resultLabel.Text = isOk ? "合格 (OK)" : "不合格 (NG)";
				_resultLabel.ForeColor = isOk ? Color.Green : Color.Red;

				_defectListBox.Items.Clear();
				foreach (var defect in defects.Distinct())
				{
					_defectListBox.Items.Add($"检测到: {defect}");
				}
				if (_defectListBox.Items.Count == 0)
				{
					_defectListBox.Items.Add("无缺陷");
				}

				if (resultImage != null)
				{
					var bmp = BmpConverter.ToBitmap(resultImage);
					_picResult.Image = bmp;
					resultImage.Dispose();
				}
			});

			AddLog($"上端面检测结果: {(isOk ? "OK" : "NG")}");
		}

		private void RunLowerTest()
		{
			AddLog("下端面缺陷检测开始...");

			if (_aiModels.EndFaceLowerModel == null)
			{
				AddLog("错误: 下端面模型未加载！");
				UpdateUI(() =>
				{
					_resultLabel.Text = "模型未加载";
					_resultLabel.ForeColor = Color.Red;
					_defectListBox.Items.Clear();
					_defectListBox.Items.Add("下端面模型未加载");
				});
				return;
			}

			var results = _aiModels.EndFaceLowerModel.PredictBatch(new List<Mat> { _currentUpperMat }, 0.5f, 0.2f);

			var defects = new List<string>();
			if (results != null && results.Count > 0 && results[0].Boxes != null && results[0].Boxes.Length > 0)
			{
				AddLog($"检测到 {results[0].Boxes.Length} 个缺陷");
				for (int i = 0; i < results[0].Boxes.Length; i++)
				{
					int classId = results[0].ClassIds[i];
					string defectType;
					if (classId == 0) defectType = "搭舌缺陷";
					else if (classId == 1) defectType = "边缘问题";
					else if (classId == 2) defectType = "破损";
					else defectType = $"未知缺陷{classId}";
					defects.Add(defectType);
				}
			}
			else
			{
				AddLog("未检测到缺陷");
			}

			bool isOk = defects.Count == 0;

			Mat resultImage = ResultDrawer.DrawEndFaceResult(_currentUpperMat, results != null && results.Count > 0 ? results[0] : null);

			UpdateUI(() =>
			{
				_resultLabel.Text = isOk ? "合格 (OK)" : "不合格 (NG)";
				_resultLabel.ForeColor = isOk ? Color.Green : Color.Red;

				_defectListBox.Items.Clear();
				foreach (var defect in defects.Distinct())
				{
					_defectListBox.Items.Add($"检测到: {defect}");
				}
				if (_defectListBox.Items.Count == 0)
				{
					_defectListBox.Items.Add("无缺陷");
				}

				if (resultImage != null)
				{
					var bmp = BmpConverter.ToBitmap(resultImage);
					_picResult.Image = bmp;
					resultImage.Dispose();
				}
			});

			AddLog($"下端面检测结果: {(isOk ? "OK" : "NG")}");
		}

		private void RunSideTest()
		{
			AddLog("侧面缺陷检测开始...");

			Mat resultImage = null;
			var defects = new List<string>();

			if (_aiModels.SideDefectModel != null)
			{
				var yoloResult = _aiModels.SideDefectModel.Predict(_currentSideMat);
				
				if (yoloResult != null && yoloResult.Boxes != null && yoloResult.Boxes.Length > 0)
				{
					AddLog($"检测到 {yoloResult.Boxes.Length} 个侧面缺陷");
					for (int i = 0; i < yoloResult.Boxes.Length; i++)
					{
						int classId = yoloResult.ClassIds[i];
						float score = yoloResult.Scores[i];
						string defectType = $"缺陷{classId}";
						defects.Add(defectType);
						AddLog($"  缺陷{i + 1}: 类别={classId}, 置信度={score:F4}");
					}
					resultImage = ResultDrawer.DrawSideDefectResult(_currentSideMat, yoloResult);
				}
				else
				{
					AddLog("未检测到侧面缺陷");
					resultImage = _currentSideMat.Clone();
					Cv2.PutText(resultImage, "No side defects detected", new OpenCvSharp.Point(10, 30),
						HersheyFonts.HersheySimplex, 1.0, new Scalar(0, 255, 0), 2);
				}
			}
			else
			{
				AddLog("警告: 侧面缺陷检测模型未加载！");
				resultImage = _currentSideMat.Clone();
				Cv2.PutText(resultImage, "Side Model Not Loaded", new OpenCvSharp.Point(10, 30),
					HersheyFonts.HersheySimplex, 1.0, new Scalar(0, 0, 255), 2);
			}

			bool isOk = defects.Count == 0;

			UpdateUI(() =>
			{
				_resultLabel.Text = isOk ? "合格 (OK)" : "不合格 (NG)";
				_resultLabel.ForeColor = isOk ? Color.Green : Color.Red;
				_defectListBox.Items.Clear();
				foreach (var defect in defects)
				{
					_defectListBox.Items.Add($"检测到: {defect}");
				}
				if (_defectListBox.Items.Count == 0)
				{
					_defectListBox.Items.Add("无缺陷");
				}

				if (resultImage != null)
				{
					var bmp = BmpConverter.ToBitmap(resultImage);
					_picResult.Image = bmp;
					resultImage.Dispose();
				}
			});

			AddLog($"侧面检测结果: {(isOk ? "OK" : "NG")}");
		}

		#endregion

		#region 模型单独测试页

		private void BuildModelTestPage()
		{
			var mainPanel = new UIPanel { Dock = DockStyle.Fill, FillColor = Color.White };

			var leftPanel = new UIPanel
			{
				Location = new DrawPoint(10, 10),
				Size = new DrawSize(350, 820),
				FillColor = Color.White,
				RectColor = Color.FromArgb(47, 60, 76),
				RectSize = 1
			};

			int y = 15;

			var modelLabel = new UILabel
			{
				Text = "选择模型:",
				Location = new DrawPoint(15, y + 3),
				Size = new DrawSize(80, 25),
				Font = new Font("微软雅黑", 11F, FontStyle.Bold)
			};
			_modelCombo = new UIComboBox
			{
				Location = new DrawPoint(100, y),
				Size = new DrawSize(230, 29),
				DropDownStyle = UIDropDownStyle.DropDownList
			};
			_modelCombo.Items.AddRange(new object[] {
				"正面-P号码OCR",
				"正面-盒子破检测",
				"上端面-缺陷检测",
				"下端面-缺陷检测",
				"背面-条形码检测",
				"背面-日期码OCR",
				"背面-明显挂钩错位",
				"背面-轻微挂钩错位",
				"背面-切字识别",
				"侧面-缺陷检测"
			});
			_modelCombo.SelectedIndex = 0;
			y += 42;

			_loadModelBtn = new UIButton
			{
				Text = "加载模型",
				Location = new DrawPoint(15, y),
				Size = new DrawSize(320, 35),
				Font = new Font("微软雅黑", 10F),
				Radius = 10,
				Cursor = Cursors.Hand
			};
			_loadModelBtn.Click += LoadModelBtn_Click;
			y += 45;

			_loadModelImageBtn = new UIButton
			{
				Text = "加载测试图像",
				Location = new DrawPoint(15, y),
				Size = new DrawSize(320, 35),
				Font = new Font("微软雅黑", 10F),
				Radius = 10,
				Cursor = Cursors.Hand
			};
			_loadModelImageBtn.Click += LoadModelImageBtn_Click;
			y += 45;

			_runModelBtn = new UIButton
			{
				Text = "执行推理",
				Location = new DrawPoint(15, y),
				Size = new DrawSize(320, 40),
				Font = new Font("微软雅黑", 11F, FontStyle.Bold),
				Radius = 10,
				FillColor = Color.FromArgb(39, 174, 96),
				Cursor = Cursors.Hand,
				Enabled = false
			};
			_runModelBtn.Click += RunModelBtn_Click;
			y += 50;

			_modelInfoLabel = new UILabel
			{
				Text = "模型信息:\n未加载",
				Location = new DrawPoint(15, y),
				Size = new DrawSize(320, 80),
				Font = new Font("微软雅黑", 10F),
				BackColor = Color.FromArgb(245, 245, 245),
				BorderStyle = BorderStyle.FixedSingle
			};
			y += 90;

			_modelTimeLabel = new UILabel
			{
				Text = "推理时间: -- ms",
				Location = new DrawPoint(15, y),
				Size = new DrawSize(320, 30),
				Font = new Font("微软雅黑", 11F, FontStyle.Bold),
				BackColor = Color.FromArgb(255, 255, 200),
				TextAlign = ContentAlignment.MiddleCenter
			};
			y += 40;

			var resultBoxLabel = new UILabel
			{
				Text = "识别结果:",
				Location = new DrawPoint(15, y),
				Size = new DrawSize(320, 25),
				Font = new Font("微软雅黑", 10F, FontStyle.Bold)
			};
			y += 30;

			_modelResultBox = new UITextBox
			{
				Location = new DrawPoint(15, y),
				Size = new DrawSize(320, 150),
				Font = new Font("微软雅黑", 9F),
				Multiline = true
			};
			y += 160;

			var logLabel = new UILabel
			{
				Text = "运行日志:",
				Location = new DrawPoint(15, y),
				Size = new DrawSize(320, 25),
				Font = new Font("微软雅黑", 10F, FontStyle.Bold)
			};
			y += 30;

			_modelLogTextBox = new UITextBox
			{
				Location = new DrawPoint(15, y),
				Size = new DrawSize(320, 150),
				Font = new Font("微软雅黑", 8F),
				Multiline = true
			};

			leftPanel.Controls.AddRange(new Control[] {
				modelLabel, _modelCombo,
				_loadModelBtn, _loadModelImageBtn, _runModelBtn,
				_modelInfoLabel, _modelTimeLabel,
				resultBoxLabel, _modelResultBox,
				logLabel, _modelLogTextBox
			});

			// 右侧图像显示
			var rightPanel = new UIPanel
			{
				Location = new DrawPoint(370, 10),
				Size = new DrawSize(1000, 820),
				FillColor = Color.White,
				RectColor = Color.FromArgb(47, 60, 76),
				RectSize = 1
			};

			_picModelInput = new XLPictureBox
			{
				Location = new DrawPoint(10, 10),
				Size = new DrawSize(480, 400),
				BackColor1 = Color.FromArgb(60, 60, 60),
				BackColor2 = Color.FromArgb(80, 80, 80),
				BackgroundGridSize = 15
			};

			_picModelOutput = new XLPictureBox
			{
				Location = new DrawPoint(500, 10),
				Size = new DrawSize(480, 400),
				BackColor1 = Color.FromArgb(60, 60, 60),
				BackColor2 = Color.FromArgb(80, 80, 80),
				BackgroundGridSize = 15
			};

			rightPanel.Controls.Add(_picModelInput);
			rightPanel.Controls.Add(_picModelOutput);

			mainPanel.Controls.Add(leftPanel);
			mainPanel.Controls.Add(rightPanel);
			_tabModelTest.Controls.Add(mainPanel);
		}

		private void LoadModelBtn_Click(object sender, EventArgs e)
		{
			string modelName = _modelCombo.SelectedItem?.ToString() ?? "未知";

			UpdateUI(() =>
			{
				_modelInfoLabel.Text = $"模型信息:\n名称: {modelName}\n状态: 已选择（待加载）";
				_runModelBtn.Enabled = true;
				_statusLabel.Text = $"模型已选择: {modelName}";
			});

			AddModelLog($"已选择模型: {modelName}");
		}

		private void LoadModelImageBtn_Click(object sender, EventArgs e)
		{
			using (var ofd = new OpenFileDialog())
			{
				ofd.Title = "选择测试图像";
				ofd.Filter = "图像文件|*.jpg;*.png;*.bmp;*.jpeg";
				if (ofd.ShowDialog() == DialogResult.OK)
				{
					try
					{
						var mat = Cv2.ImRead(ofd.FileName);
						var bmp = BmpConverter.ToBitmap(mat);

						UpdateUI(() =>
						{
							_picModelInput.Image = bmp;
							_statusLabel.Text = $"已加载: {Path.GetFileName(ofd.FileName)}";
						});

						AddModelLog($"已加载图像: {Path.GetFileName(ofd.FileName)}, 尺寸: {mat.Width}x{mat.Height}");
						mat.Dispose();
					}
					catch (Exception ex)
					{
						AddModelLog($"加载图像失败: {ex.Message}");
						MessageBox.Show($"加载失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
					}
				}
			}
		}

		private async void RunModelBtn_Click(object sender, EventArgs e)
		{
			if (_picModelInput.Image == null)
			{
				MessageBox.Show("请先加载测试图像！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}

			UpdateUI(() => _runModelBtn.Enabled = false);
			var sw = System.Diagnostics.Stopwatch.StartNew();

			try
			{
				await Task.Run(() =>
				{
					var mat = BmpConverter.ToMat((Bitmap)_picModelInput.Image);
					string modelName = "";

					UpdateUI(() =>
					{
						modelName = _modelCombo.SelectedItem?.ToString() ?? "未知";
					});

					string resultText = $"模型: {modelName}\n图像尺寸: {mat.Width}x{mat.Height}\n\n";

					AddModelLog($"========== 开始推理: {modelName} ==========");
					AddModelLog($"图像尺寸: {mat.Width}x{mat.Height}");

					int selectedIndex = 0;
					UpdateUI(() =>
					{
						selectedIndex = _modelCombo.SelectedIndex;
					});

					if (selectedIndex == 0) // 正面-P号码OCR
					{
						AddModelLog("调用正面P号码OCR模型...");
						if (_aiModels.FrontOcrModel != null)
						{
							AddModelLog("模型已加载，开始推理...");
							var ocrResult = _aiModels.FrontOcrModel.RunOrderOcr(mat);
							string text = "";
							float confidence = 0;
							if (ocrResult != null && ocrResult.Blocks != null && ocrResult.Blocks.Any())
							{
								text = string.Join("", ocrResult.Blocks.Select(b => b.Label));
								confidence = ocrResult.Blocks.First().Score;
								AddModelLog($"识别结果: [{text}], 置信度: {confidence:F4}");
							}
							else
							{
								AddModelLog("识别结果为空");
							}
							resultText += $"识别结果: {text}\n置信度: {confidence:F4}";
						}
						else
						{
							AddModelLog("错误: 正面P号码OCR模型未加载！");
							resultText += "模型未加载";
						}
					}
					else if (selectedIndex == 1) // 正面-盒子破检测
					{
						AddModelLog("调用正面盒子破检测模型...");
						if (_aiModels.FrontBoxBreakModel != null)
						{
							AddModelLog("模型已加载，开始推理...");
							var yoloResult = _aiModels.FrontBoxBreakModel.Predict(mat);
							AddModelLog($"检测到 {yoloResult?.Boxes?.Length ?? 0} 个目标");
							resultText += $"检测到 {yoloResult?.Boxes?.Length ?? 0} 个目标\n";
							if (yoloResult != null && yoloResult.Boxes != null)
							{
								for (int i = 0; i < yoloResult.Boxes.Length; i++)
								{
									resultText += $"目标{i + 1}: 类别{yoloResult.ClassIds[i]}, 置信度{yoloResult.Scores[i]:F2}\n";
									AddModelLog($"  目标{i + 1}: 类别={yoloResult.ClassIds[i]}, 置信度={yoloResult.Scores[i]:F4}");
								}
							}
						}
						else
						{
							AddModelLog("错误: 正面盒子破检测模型未加载！");
							resultText += "模型未加载";
						}
					}
					else if (selectedIndex == 2) // 上端面
					{
						AddModelLog("调用上端面缺陷检测模型...");
						if (_aiModels.EndFaceUpperModel != null)
						{
							AddModelLog("模型已加载，开始推理...");
							var yoloResult = _aiModels.EndFaceUpperModel.Predict(mat);
							AddModelLog($"检测到 {yoloResult?.Boxes?.Length ?? 0} 个缺陷");
							resultText += $"检测到 {yoloResult?.Boxes?.Length ?? 0} 个缺陷\n";
							if (yoloResult != null && yoloResult.Boxes != null)
							{
								for (int i = 0; i < yoloResult.Boxes.Length; i++)
								{
									int classId = yoloResult.ClassIds[i];
									string defectName;
									if (classId == 0) defectName = "搭舌缺陷";
									else if (classId == 1) defectName = "边缘问题";
									else if (classId == 2) defectName = "破损";
									else defectName = $"未知{classId}";
									resultText += $"{defectName}: 置信度{yoloResult.Scores[i]:F2}\n";
									AddModelLog($"  缺陷{i + 1}: {defectName}, 置信度={yoloResult.Scores[i]:F4}");
								}
							}
						}
						else
						{
							AddModelLog("错误: 上端面缺陷检测模型未加载！");
							resultText += "模型未加载";
						}
					}
					else if (selectedIndex == 3) // 下端面
					{
						AddModelLog("调用下端面缺陷检测模型...");
						if (_aiModels.EndFaceLowerModel != null)
						{
							AddModelLog("模型已加载，开始推理...");
							var yoloResult = _aiModels.EndFaceLowerModel.Predict(mat);
							AddModelLog($"检测到 {yoloResult?.Boxes?.Length ?? 0} 个缺陷");
							resultText += $"检测到 {yoloResult?.Boxes?.Length ?? 0} 个缺陷\n";
							if (yoloResult != null && yoloResult.Boxes != null)
							{
								for (int i = 0; i < yoloResult.Boxes.Length; i++)
								{
									int classId = yoloResult.ClassIds[i];
									string defectName;
									if (classId == 0) defectName = "搭舌缺陷";
									else if (classId == 1) defectName = "边缘问题";
									else if (classId == 2) defectName = "破损";
									else defectName = $"未知{classId}";
									resultText += $"{defectName}: 置信度{yoloResult.Scores[i]:F2}\n";
									AddModelLog($"  缺陷{i + 1}: {defectName}, 置信度={yoloResult.Scores[i]:F4}");
								}
							}
						}
						else
						{
							AddModelLog("错误: 下端面缺陷检测模型未加载！");
							resultText += "模型未加载";
						}
					}
					else
					{
						AddModelLog("该模型检测暂未实现详细输出");
						resultText += "该模型检测暂未实现详细输出";
					}

					AddModelLog($"推理完成，总耗时: {sw.ElapsedMilliseconds} ms");

					UpdateUI(() =>
					{
						_modelResultBox.Text = resultText;
					});

					mat.Dispose();
				});

				sw.Stop();

				UpdateUI(() =>
				{
					_modelTimeLabel.Text = $"推理时间: {sw.ElapsedMilliseconds} ms";
					_statusLabel.Text = $"推理完成 - {_modelCombo.SelectedItem}";
				});

				AddModelLog($"推理完成，总耗时: {sw.ElapsedMilliseconds} ms");
			}
			catch (Exception ex)
			{
				AddModelLog($"推理失败: {ex.Message}");
				UpdateUI(() =>
				{
					_modelResultBox.Text = $"推理失败: {ex.Message}";
					_statusLabel.Text = $"推理失败: {ex.Message}";
				});
			}
			finally
			{
				UpdateUI(() =>
				{
					_runModelBtn.Enabled = true;
				});
			}
		}

		#endregion

		private void LoadAiModels()
		{
			AddLog("开始加载AI模型...");
			try
			{
				var modelConfig = ModelPathConfig.LoadFromSysConfig();
				AddLog($"模型根路径: {modelConfig.ModelRootPath}");
				AddLog($"正面P号码模型: {modelConfig.FrontPCodeOcrModel ?? "未配置"}");
				AddLog($"正面盒子破模型: {modelConfig.FrontBoxBreakModel ?? "未配置"}");
				AddLog($"上端面模型: {modelConfig.EndFaceUpperModel ?? "未配置"}");
				AddLog($"下端面模型: {modelConfig.EndFaceLowerModel ?? "未配置"}");
				AddLog($"背面挂钩明显模型: {modelConfig.BackHookDamageModel ?? "未配置"}");
				AddLog($"背面挂钩轻微模型: {modelConfig.BackHookSlightModel ?? "未配置"}");

				_aiModels = new AiModelManager(modelConfig);
				_aiModels.LoadAllModels();
				AddLog("AI模型加载完成");

				UpdateUI(() =>
				{
					_statusLabel.Text = "AI模型加载完成";
				});
			}
			catch (Exception ex)
			{
				AddLog($"AI模型加载失败: {ex.Message}");
				Logger.Error($"AI模型加载失败: {ex.Message}");
				UpdateUI(() =>
				{
					_statusLabel.Text = $"AI模型加载失败: {ex.Message}";
				});
			}
		}

		private void InitSkuData()
		{
			_currentSku = new SkuData
			{
				SkuNumber = "TEST001",
				P = 8,
				Z = 2,
				MM = 42,
				FrontPCode = "P1837741411",
				BackBarcode = "6901234567890",
				CodingFormat = "MFG"
			};
			AddLog($"SKU初始化: P={_currentSku.P}, Z={_currentSku.Z}, MM={_currentSku.MM}");
		}

		protected override void OnFormClosing(FormClosingEventArgs e)
		{
			AddLog("测试工具关闭");
			_currentLeftMat?.Dispose();
			_currentRightMat?.Dispose();
			_currentUpperMat?.Dispose();
			_currentLowerMat?.Dispose();
			_currentSideMat?.Dispose();
			base.OnFormClosing(e);
		}
	}
}