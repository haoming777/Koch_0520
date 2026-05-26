using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using XL.Controls;

// 命名空间别名
using Size = System.Drawing.Size;
using Point = System.Drawing.Point;
using PointF = System.Drawing.PointF;
using DetYolo = YoloInference.YoloOnnx;
using SegYolo = YoloSegmentationEnd2End.YoloOnnxSegmentation;

namespace HookInspectionSystem
{
	public partial class HookInspectionForm挂钩 : Form
	{
		// ========== 核心组件 ==========
		private DetYolo _obviousDefectModel;   // 全局检测模型
		private SegYolo _slightDefectModel;     // 局部分割模型
		private Mat _leftImage;
		private Mat _rightImage;
		private HookInspectionOutput _currentOutput;

		// ========== 扁平化颜色方案 ==========
		private static readonly Color PrimaryColor = Color.FromArgb(0, 122, 204);
		private static readonly Color SuccessColor = Color.FromArgb(39, 174, 96);
		private static readonly Color WarningColor = Color.FromArgb(230, 126, 34);
		private static readonly Color DangerColor = Color.FromArgb(231, 76, 60);
		private static readonly Color BgColor = Color.FromArgb(245, 247, 250);
		private static readonly Color CardBgColor = Color.White;
		private static readonly Color BorderColor = Color.FromArgb(224, 228, 234);
		private static readonly Color TextPrimary = Color.FromArgb(44, 62, 80);
		private static readonly Color TextSecondary = Color.FromArgb(127, 140, 141);

		// ========== 界面控件 ==========
		private TableLayoutPanel mainLayout;
		private Panel topPanel;
		private Panel centerPanel;
		private Panel bottomPanel;

		// 顶部 - 模型配置
		private TextBox txtObviousModelPath;
		private TextBox txtSlightModelPath;
		private Label lblObviousModelStatus;
		private Label lblSlightModelStatus;
		private Button btnLoadObviousModel;
		private Button btnLoadSlightModel;
		private Button btnLoadAllModels;

		// 顶部 - 图片配置
		private TextBox txtLeftImagePath;
		private TextBox txtRightImagePath;
		private Button btnLoadImages;

		// 顶部 - 参数配置
		private NumericUpDown numBoxCount;
		private NumericUpDown numThicknessThreshold;
		private NumericUpDown numBlueAreaClassId;
		private NumericUpDown numHangHoleClassId;
		private TrackBar trackConfThreshold;
		private Label lblConfValue;
		private Button btnRunInspection;

		// 中间 - 图像显示
		private XLPictureBox picLeftOriginal;
		private XLPictureBox picRightOriginal;
		private XLPictureBox picLeftResult;
		private XLPictureBox picRightResult;

		// 中间 - 挂钩状态面板
		private FlowLayoutPanel hookStatusPanel;
		private List<Panel> hookStatusCards;

		// 底部 - 检测结果表格
		private DataGridView dgvResults;

		// 底部 - 缺陷详情
		private RichTextBox rtbDefectDetails;

		// 底部 - 调试输出
		private RichTextBox rtbDebugOutput;

		// 状态栏
		private Label lblStatus;
		private ProgressBar progressBar;

		// 导出按钮
		private Button btnExportCsv;
		private Button btnSaveLeftResult;
		private Button btnSaveRightResult;
		private Button btnSaveAllResult;

		// 状态颜色
		private static readonly Dictionary<string, Color> StatusColors = new Dictionary<string, Color>
		{
			{ "缺少", Color.FromArgb(189, 195, 199) },        // 灰色
            { "OK", Color.FromArgb(39, 174, 96) },            // 绿色
            { "挂钩明显错位", Color.FromArgb(231, 76, 60) },   // 红色
            { "轻微挂钩错位", Color.FromArgb(230, 126, 34) },  // 橙色
        };

		public HookInspectionForm挂钩()
		{
			InitializeComponent();
			InitializeCustomComponents();
		}

		private void InitializeCustomComponents()
		{
			this.Text = "挂钩损伤检测调试工具 v1.0";
			this.Size = new Size(1700, 1050);
			this.StartPosition = FormStartPosition.CenterScreen;
			this.MinimumSize = new Size(1500, 900);
			this.BackColor = BgColor;
			this.Font = new Font("Microsoft YaHei UI", 9F);

			CreateMainLayout();
			CreateTopPanel();
			CreateCenterPanel();
			CreateBottomPanel();
			CreateStatusBar();

			UpdateUIState(false);
		}

		#region 主布局
		private void CreateMainLayout()
		{
			mainLayout = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 1,
				RowCount = 4,
				Padding = new Padding(12)
			};
			mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 180F));  // 顶部配置
			mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));    // 图像显示
			mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 80F));   // 挂钩状态
			mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));    // 结果详情
			this.Controls.Add(mainLayout);
		}
		#endregion

		#region 顶部面板 - 配置区
		private void CreateTopPanel()
		{
			topPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
			var topLayout = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 4,
				RowCount = 1
			};
			topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
			topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
			topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));
			topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));

			// ========== 卡片1：检测模型配置 ==========
			var modelCard = CreateFlatCard("检测模型配置");
			var modelContent = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(10) };
			modelContent.RowStyles.Add(new RowStyle(SizeType.Percent, 33F));
			modelContent.RowStyles.Add(new RowStyle(SizeType.Percent, 33F));
			modelContent.RowStyles.Add(new RowStyle(SizeType.Percent, 34F));

			// 明显错位模型
			var obviousPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
			var lblObvious = new Label { Text = "明显错位模型:", AutoSize = true, Width = 95, TextAlign = ContentAlignment.MiddleRight, ForeColor = TextSecondary };
			txtObviousModelPath = CreateFlatTextBox(@"F:\models\obvious_defect.onnx");
			txtObviousModelPath.Width = 180;
			var btnBrowseObvious = CreateFlatButton("📁", PrimaryColor, false, 28);
			btnBrowseObvious.Click += (s, e) => BrowseFile(txtObviousModelPath, "ONNX|*.onnx");
			lblObviousModelStatus = new Label { Text = "●", AutoSize = true, ForeColor = TextSecondary, Font = new Font("Microsoft YaHei UI", 8F) };
			obviousPanel.Controls.AddRange(new Control[] { lblObvious, txtObviousModelPath, btnBrowseObvious, lblObviousModelStatus });

			// 轻微错位模型
			var slightPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
			var lblSlight = new Label { Text = "轻微错位模型:", AutoSize = true, Width = 95, TextAlign = ContentAlignment.MiddleRight, ForeColor = TextSecondary };
			txtSlightModelPath = CreateFlatTextBox(@"F:\models\slight_defect.onnx");
			txtSlightModelPath.Width = 180;
			var btnBrowseSlight = CreateFlatButton("📁", PrimaryColor, false, 28);
			btnBrowseSlight.Click += (s, e) => BrowseFile(txtSlightModelPath, "ONNX|*.onnx");
			lblSlightModelStatus = new Label { Text = "●", AutoSize = true, ForeColor = TextSecondary, Font = new Font("Microsoft YaHei UI", 8F) };
			slightPanel.Controls.AddRange(new Control[] { lblSlight, txtSlightModelPath, btnBrowseSlight, lblSlightModelStatus });

			// 加载按钮
			var loadModelPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
			btnLoadObviousModel = CreateFlatButton("加载明显模型", SuccessColor, true, 28);
			btnLoadObviousModel.Click += async (s, e) => await LoadObviousModel();
			btnLoadSlightModel = CreateFlatButton("加载轻微模型", PrimaryColor, true, 28);
			btnLoadSlightModel.Click += async (s, e) => await LoadSlightModel();
			btnLoadAllModels = CreateFlatButton("加载全部模型", WarningColor, true, 28);
			btnLoadAllModels.Click += async (s, e) => await LoadAllModels();
			loadModelPanel.Controls.Add(btnLoadAllModels);
			loadModelPanel.Controls.Add(btnLoadSlightModel);
			loadModelPanel.Controls.Add(btnLoadObviousModel);

			modelContent.Controls.Add(obviousPanel, 0, 0);
			modelContent.Controls.Add(slightPanel, 0, 1);
			modelContent.Controls.Add(loadModelPanel, 0, 2);
			modelCard.Controls.Add(modelContent);
			topLayout.Controls.Add(modelCard, 0, 0);

			// ========== 卡片2：图片配置 ==========
			var imageCard = CreateFlatCard("图片配置");
			var imageContent = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 3, Padding = new Padding(10) };
			imageContent.RowStyles.Add(new RowStyle(SizeType.Percent, 33F));
			imageContent.RowStyles.Add(new RowStyle(SizeType.Percent, 33F));
			imageContent.RowStyles.Add(new RowStyle(SizeType.Percent, 34F));

			var leftImgPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
			var lblLeft = new Label { Text = "左侧图片:", AutoSize = true, Width = 65, TextAlign = ContentAlignment.MiddleRight, ForeColor = TextSecondary };
			txtLeftImagePath = CreateFlatTextBox(@"F:\images\left.jpg");
			txtLeftImagePath.Width = 200;
			var btnBrowseLeft = CreateFlatButton("📁", PrimaryColor, false, 28);
			btnBrowseLeft.Click += (s, e) => BrowseFile(txtLeftImagePath, "图片|*.jpg;*.png;*.bmp");
			leftImgPanel.Controls.AddRange(new Control[] { lblLeft, txtLeftImagePath, btnBrowseLeft });

			var rightImgPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
			var lblRight = new Label { Text = "右侧图片:", AutoSize = true, Width = 65, TextAlign = ContentAlignment.MiddleRight, ForeColor = TextSecondary };
			txtRightImagePath = CreateFlatTextBox(@"F:\images\right.jpg");
			txtRightImagePath.Width = 200;
			var btnBrowseRight = CreateFlatButton("📁", PrimaryColor, false, 28);
			btnBrowseRight.Click += (s, e) => BrowseFile(txtRightImagePath, "图片|*.jpg;*.png;*.bmp");
			rightImgPanel.Controls.AddRange(new Control[] { lblRight, txtRightImagePath, btnBrowseRight });

			var loadImgPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
			btnLoadImages = CreateFlatButton("加载图片", SuccessColor, true, 30);
			btnLoadImages.Click += BtnLoadImages_Click;
			loadImgPanel.Controls.Add(btnLoadImages);

			imageContent.Controls.Add(leftImgPanel, 0, 0);
			imageContent.Controls.Add(rightImgPanel, 0, 1);
			imageContent.Controls.Add(loadImgPanel, 0, 2);
			imageCard.Controls.Add(imageContent);
			topLayout.Controls.Add(imageCard, 1, 0);

			// ========== 卡片3：检测参数 ==========
			var paramsCard = CreateFlatCard("检测参数");
			var paramsContent = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), WrapContents = true };

			// 盒子数量
			var boxCountPanel = new Panel { Width = 160, Height = 28 };
			var lblBoxCount = new Label { Text = "盒子总数(p):", Location = new Point(0, 3), AutoSize = true, ForeColor = TextSecondary, Font = new Font("Microsoft YaHei UI", 8F) };
			numBoxCount = new NumericUpDown { Minimum = 1, Maximum = 50, Value = 12, Width = 55, Location = new Point(95, 0) };
			boxCountPanel.Controls.AddRange(new Control[] { lblBoxCount, numBoxCount });
			paramsContent.Controls.Add(boxCountPanel);

			// 厚度阈值
			var thickPanel = new Panel { Width = 200, Height = 28 };
			var lblThick = new Label { Text = "厚度阈值(px):", Location = new Point(0, 3), AutoSize = true, ForeColor = TextSecondary, Font = new Font("Microsoft YaHei UI", 8F) };
			numThicknessThreshold = new NumericUpDown { Minimum = 1, Maximum = 200, Value = 30, Width = 55, Location = new Point(100, 0) };
			thickPanel.Controls.AddRange(new Control[] { lblThick, numThicknessThreshold });
			paramsContent.Controls.Add(thickPanel);

			// 内圈类别ID
			var bluePanel = new Panel { Width = 160, Height = 28 };
			var lblBlue = new Label { Text = "内圈类别ID:", Location = new Point(0, 3), AutoSize = true, ForeColor = TextSecondary, Font = new Font("Microsoft YaHei UI", 8F) };
			numBlueAreaClassId = new NumericUpDown { Minimum = 0, Maximum = 10, Value = 0, Width = 50, Location = new Point(95, 0) };
			bluePanel.Controls.AddRange(new Control[] { lblBlue, numBlueAreaClassId });
			paramsContent.Controls.Add(bluePanel);

			// 外圈类别ID
			var holePanel = new Panel { Width = 160, Height = 28 };
			var lblHole = new Label { Text = "外圈类别ID:", Location = new Point(0, 3), AutoSize = true, ForeColor = TextSecondary, Font = new Font("Microsoft YaHei UI", 8F) };
			numHangHoleClassId = new NumericUpDown { Minimum = 0, Maximum = 10, Value = 1, Width = 50, Location = new Point(95, 0) };
			holePanel.Controls.AddRange(new Control[] { lblHole, numHangHoleClassId });
			paramsContent.Controls.Add(holePanel);

			// 置信度阈值
			var confPanel = new Panel { Width = 280, Height = 30 };
			var confLabel = new Label { Text = "检测置信度:", Location = new Point(0, 5), AutoSize = true, ForeColor = TextSecondary, Font = new Font("Microsoft YaHei UI", 8F) };
			trackConfThreshold = new TrackBar { Minimum = 1, Maximum = 100, Value = 50, Width = 150, Location = new Point(80, 0), TickStyle = TickStyle.None };
			trackConfThreshold.Scroll += (s, ev) => lblConfValue.Text = $"{trackConfThreshold.Value / 100f:F2}";
			lblConfValue = new Label { Text = "0.50", Location = new Point(235, 5), AutoSize = true, Font = new Font("Consolas", 9F, FontStyle.Bold), ForeColor = PrimaryColor };
			confPanel.Controls.AddRange(new Control[] { confLabel, trackConfThreshold, lblConfValue });
			paramsContent.Controls.Add(confPanel);

			paramsCard.Controls.Add(paramsContent);
			topLayout.Controls.Add(paramsCard, 2, 0);

			// ========== 卡片4：操作按钮 ==========
			var actionCard = CreateFlatCard("操作");
			var actionContent = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), FlowDirection = FlowDirection.TopDown };

			btnRunInspection = CreateFlatButton("▶ 执行检测", DangerColor, true, 50);
			btnRunInspection.Width = 150;
			btnRunInspection.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
			btnRunInspection.Click += async (s, ev) => await RunInspection();

			var lblInfo = new Label
			{
				Text = "检测流程:\n1. 全局检测→明显错位\n2. 局部分割→轻微错位\n3. 距离变换→厚度计算",
				AutoSize = true,
				ForeColor = TextSecondary,
				Font = new Font("Microsoft YaHei UI", 7F),
				Padding = new Padding(5)
			};

			actionContent.Controls.Add(btnRunInspection);
			actionContent.Controls.Add(lblInfo);
			actionCard.Controls.Add(actionContent);
			topLayout.Controls.Add(actionCard, 3, 0);

			topPanel.Controls.Add(topLayout);
			mainLayout.Controls.Add(topPanel, 0, 0);
		}
		#endregion

		#region 中间面板 - 图像显示 + 挂钩状态
		private void CreateCenterPanel()
		{
			centerPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(0, 8, 0, 8) };
			var centerLayout = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				RowCount = 2
			};
			centerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 75F));
			centerLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 25F));

			// 图像显示行
			var imageRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4 };
			imageRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
			imageRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
			imageRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
			imageRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));

			// 左侧原图
			var leftOrigPanel = CreateImagePanel("左侧原图", out picLeftOriginal);
			// 右侧原图
			var rightOrigPanel = CreateImagePanel("右侧原图", out picRightOriginal);
			// 左侧结果
			var leftResPanel = CreateImagePanel("左侧检测结果", out picLeftResult);
			// 右侧结果
			var rightResPanel = CreateImagePanel("右侧检测结果", out picRightResult);

			imageRow.Controls.Add(leftOrigPanel, 0, 0);
			imageRow.Controls.Add(rightOrigPanel, 1, 0);
			imageRow.Controls.Add(leftResPanel, 2, 0);
			imageRow.Controls.Add(rightResPanel, 3, 0);

			centerLayout.Controls.Add(imageRow, 0, 0);

			// 挂钩状态行
			var statusCard = CreateFlatCard("挂钩状态概览");
			hookStatusPanel = new FlowLayoutPanel
			{
				Dock = DockStyle.Fill,
				AutoScroll = true,
				Padding = new Padding(8),
				WrapContents = true
			};
			hookStatusCards = new List<Panel>();
			statusCard.Controls.Add(hookStatusPanel);
			centerLayout.Controls.Add(statusCard, 0, 1);

			centerPanel.Controls.Add(centerLayout);
			mainLayout.Controls.Add(centerPanel, 0, 1);
		}

		private Panel CreateImagePanel(string title, out XLPictureBox pictureBox)
		{
			var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(3), BackColor = Color.Transparent };
			var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));

			var titleBar = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(240, 242, 245) };
			var titleLabel = new Label { Text = title, Location = new Point(4, 3), AutoSize = true, Font = new Font("Microsoft YaHei UI", 7F, FontStyle.Bold), ForeColor = TextPrimary };
			titleBar.Controls.Add(titleLabel);

			pictureBox = new XLPictureBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(20, 20, 20), BorderStyle = BorderStyle.None };

			layout.Controls.Add(pictureBox, 0, 0);
			layout.Controls.Add(titleBar, 0, 1);

			panel.Controls.Add(layout);
			return panel;
		}
		#endregion

		#region 底部面板 - 结果详情
		private void CreateBottomPanel()
		{
			bottomPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
			var bottomLayout = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 3,
				RowCount = 1
			};
			bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
			bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
			bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));

			// ========== 检测结果表格 ==========
			var resultsCard = CreateFlatCard("检测结果列表");
			var resultsContent = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
			resultsContent.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			resultsContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 40F));

			dgvResults = new DataGridView
			{
				Dock = DockStyle.Fill,
				AllowUserToAddRows = false,
				AllowUserToDeleteRows = false,
				ReadOnly = true,
				SelectionMode = DataGridViewSelectionMode.FullRowSelect,
				AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
				BackgroundColor = Color.White,
				BorderStyle = BorderStyle.None,
				ColumnHeadersHeight = 28,
				RowTemplate = { Height = 24 },
				EnableHeadersVisualStyles = false,
				ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
				{
					BackColor = Color.FromArgb(240, 242, 245),
					ForeColor = TextPrimary,
					Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold)
				}
			};
			dgvResults.Columns.Add("Index", "序号");
			dgvResults.Columns.Add("ImageSide", "图像");
			dgvResults.Columns.Add("GlobalIndex", "全局索引");
			dgvResults.Columns.Add("Status", "状态");
			dgvResults.Columns.Add("BBox_Norm", "归一化框");
			dgvResults.Columns.Add("Thickness", "厚度");
			dgvResults.Columns.Add("CenterPoint", "中心点(归一化)");
			dgvResults.Columns["Index"].Width = 40;
			dgvResults.Columns["ImageSide"].Width = 50;
			dgvResults.Columns["GlobalIndex"].Width = 70;
			dgvResults.Columns["Status"].Width = 110;
			dgvResults.Columns["Thickness"].Width = 70;

			// ========== 按钮面板 ==========
			var btnPanel = new FlowLayoutPanel
			{
				Dock = DockStyle.Fill,
				Padding = new Padding(4),
				FlowDirection = FlowDirection.LeftToRight,
				AutoScroll = true  // 添加滚动条
			};

			// CSV 导出按钮
			btnExportCsv = CreateFlatButton("📊 导出CSV", TextSecondary, false, 28);
			btnExportCsv.Click += ExportResults;

			// 左侧渲染图导出按钮
			btnSaveLeftResult = CreateFlatButton("🖼 左侧结果图", PrimaryColor, false, 28);
			btnSaveLeftResult.Click += ExportLeftRenderImage;

			// 右侧渲染图导出按钮
			btnSaveRightResult = CreateFlatButton("🖼 右侧结果图", SuccessColor, false, 28);
			btnSaveRightResult.Click += ExportRightRenderImage;

			// 全部拼接导出按钮
			btnSaveAllResult = CreateFlatButton("🖼 拼接导出", WarningColor, false, 28);
			btnSaveAllResult.Click += ExportAllRenderImages;


			btnExportCsv = CreateFlatButton("📊 导出CSV", TextSecondary, false, 32);  // 高度32
			btnSaveLeftResult = CreateFlatButton("🖼 左侧结果图", PrimaryColor, false, 32);
			btnSaveRightResult = CreateFlatButton("🖼 右侧结果图", SuccessColor, false, 32);
			btnSaveAllResult = CreateFlatButton("🖼 拼接导出", WarningColor, false, 32);
			// 添加到面板
			btnPanel.Controls.AddRange(new Control[]
			{
		btnExportCsv,
		btnSaveLeftResult,
		btnSaveRightResult,
		btnSaveAllResult
			});

			resultsContent.Controls.Add(dgvResults, 0, 0);
			resultsContent.Controls.Add(btnPanel, 0, 1);
			resultsCard.Controls.Add(resultsContent);
			bottomLayout.Controls.Add(resultsCard, 0, 0);

			// ========== 缺陷详情 ==========
			var defectCard = CreateFlatCard("缺陷坐标详情");
			rtbDefectDetails = new RichTextBox
			{
				Dock = DockStyle.Fill,
				ReadOnly = true,
				Font = new Font("Consolas", 9F),
				BackColor = Color.FromArgb(30, 30, 30),
				ForeColor = Color.FromArgb(0, 255, 0),
				BorderStyle = BorderStyle.None,
				WordWrap = false
			};
			defectCard.Controls.Add(rtbDefectDetails);
			bottomLayout.Controls.Add(defectCard, 1, 0);

			// ========== 调试输出 ==========
			var debugCard = CreateFlatCard("调试输出");
			rtbDebugOutput = new RichTextBox
			{
				Dock = DockStyle.Fill,
				ReadOnly = true,
				Font = new Font("Consolas", 9F),
				BackColor = Color.FromArgb(30, 30, 30),
				ForeColor = Color.FromArgb(180, 180, 180),
				BorderStyle = BorderStyle.None,
				WordWrap = false
			};
			debugCard.Controls.Add(rtbDebugOutput);
			bottomLayout.Controls.Add(debugCard, 2, 0);

			bottomPanel.Controls.Add(bottomLayout);
			mainLayout.Controls.Add(bottomPanel, 0, 3);
		}
		#endregion

		#region 状态栏
		private void CreateStatusBar()
		{
			var statusBar = new Panel
			{
				Dock = DockStyle.Bottom,
				Height = 26,
				BackColor = Color.FromArgb(250, 251, 252)
			};
			lblStatus = new Label
			{
				Text = "就绪 - 请加载模型和图片",
				Location = new Point(12, 4),
				AutoSize = true,
				Font = new Font("Microsoft YaHei UI", 8F),
				ForeColor = TextSecondary
			};
			progressBar = new ProgressBar
			{
				Style = ProgressBarStyle.Marquee,
				Width = 120,
				Height = 16,
				Location = new Point(this.Width - 140, 4),
				Visible = false,
				Anchor = AnchorStyles.Right | AnchorStyles.Top
			};
			statusBar.Controls.Add(lblStatus);
			statusBar.Controls.Add(progressBar);
			this.Controls.Add(statusBar);
		}
		#endregion

		#region UI 辅助方法
		private Panel CreateFlatCard(string title)
		{
			var card = new Panel { Dock = DockStyle.Fill, BackColor = CardBgColor, Margin = new Padding(4) };
			card.Paint += (s, e) =>
			{
				var panel = s as Panel;
				e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
				using (var pen = new Pen(BorderColor, 1))
					e.Graphics.DrawRectangle(pen, 0, 0, panel.Width - 1, panel.Height - 1);
			};
			return card;
		}

		private TextBox CreateFlatTextBox(string text)
		{
			return new TextBox
			{
				Text = text,
				BorderStyle = BorderStyle.FixedSingle,
				Font = new Font("Consolas", 8F),
				BackColor = Color.White,
				ForeColor = TextPrimary
			};
		}

		private Button CreateFlatButton(string text, Color color, bool fill = false, int height = 28)
		{
			var btn = new Button
			{
				Text = text,
				Height = height,
				FlatStyle = FlatStyle.Flat,
				BackColor = fill ? color : Color.White,
				ForeColor = fill ? Color.White : color,
				Font = new Font("Microsoft YaHei UI", 8F),
				Cursor = Cursors.Hand,
				Margin = new Padding(2),
				UseVisualStyleBackColor = false
			};
			btn.FlatAppearance.BorderColor = color;
			btn.FlatAppearance.BorderSize = 1;
			btn.MouseEnter += (s, e) => btn.BackColor = fill ? ControlPaint.Light(color, 0.2f) : ControlPaint.Light(color, 0.9f);
			btn.MouseLeave += (s, e) => btn.BackColor = fill ? color : Color.White;
			return btn;
		}

		private void BrowseFile(TextBox targetBox, string filter)
		{
			using (var dialog = new OpenFileDialog { Filter = filter, Title = "选择文件" })
			{
				if (dialog.ShowDialog() == DialogResult.OK)
					targetBox.Text = dialog.FileName;
			}
		}

		private void AppendDebug(string msg, bool isError = false)
		{
			if (rtbDebugOutput.InvokeRequired)
			{
				rtbDebugOutput.Invoke(new Action(() => AppendDebug(msg, isError)));
				return;
			}
			rtbDebugOutput.SelectionColor = isError ? Color.FromArgb(255, 100, 100) : Color.FromArgb(180, 180, 180);
			rtbDebugOutput.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
			rtbDebugOutput.ScrollToCaret();
		}

		private void SetBusyState(bool busy, string status = "")
		{
			if (this.InvokeRequired) { this.Invoke(new Action(() => SetBusyState(busy, status))); return; }
			progressBar.Visible = busy;
			btnRunInspection.Enabled = !busy && _obviousDefectModel != null && _slightDefectModel != null && _leftImage != null && _rightImage != null;
			if (!string.IsNullOrEmpty(status)) lblStatus.Text = status;
			Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
		}

		private void UpdateUIState(bool modelsLoaded)
		{
			btnRunInspection.Enabled = modelsLoaded && _leftImage != null && _rightImage != null;
			btnLoadImages.Enabled = true;
		}
		#endregion

		#region 模型加载
		private async Task LoadObviousModel()
		{
			try
			{
				SetBusyState(true, "加载明显错位模型...");
				lblObviousModelStatus.Text = "● 加载中...";
				lblObviousModelStatus.ForeColor = WarningColor;
				AppendDebug($"📦 加载明显错位模型: {Path.GetFileName(txtObviousModelPath.Text)}");

				await Task.Run(() =>
				{
					_obviousDefectModel?.Dispose();

					// 关键修复：需要指定 meta.json 路径
					string modelPath = txtObviousModelPath.Text;
					string metaJsonPath = Path.ChangeExtension(modelPath, "json"); // 假设 json 与 onnx 同名
																				   // 或者手动指定：string metaJsonPath = @"F:\models\meta.json";

					_obviousDefectModel = new DetYolo(modelPath, metaJsonPath, expectedBatchSize: 2);
				});

				lblObviousModelStatus.Text = "● 已就绪";
				lblObviousModelStatus.ForeColor = SuccessColor;
				AppendDebug($"✅ 明显错位模型加载成功");
				UpdateUIState(_slightDefectModel != null);
			}
			catch (Exception ex)
			{
				lblObviousModelStatus.Text = "● 失败";
				lblObviousModelStatus.ForeColor = DangerColor;
				AppendDebug($"❌ 加载失败: {ex.Message}\r\n{ex.StackTrace}", true);
			}
			finally { SetBusyState(false); }
		}

		private async Task LoadSlightModel()
		{
			try
			{
				SetBusyState(true, "加载轻微错位模型...");
				lblSlightModelStatus.Text = "● 加载中...";
				lblSlightModelStatus.ForeColor = WarningColor;
				AppendDebug($"📦 加载轻微错位模型: {Path.GetFileName(txtSlightModelPath.Text)}");

				await Task.Run(() =>
				{
					_slightDefectModel?.Dispose();
					_slightDefectModel = new SegYolo(txtSlightModelPath.Text, expectedBatchSize: 1);
				});

				lblSlightModelStatus.Text = "● 已就绪";
				lblSlightModelStatus.ForeColor = SuccessColor;
				AppendDebug($"✅ 轻微错位模型加载成功 (输入: {_slightDefectModel.InputWidth}x{_slightDefectModel.InputHeight})");
				UpdateUIState(_obviousDefectModel != null);
			}
			catch (Exception ex)
			{
				lblSlightModelStatus.Text = "● 失败";
				lblSlightModelStatus.ForeColor = DangerColor;
				AppendDebug($"❌ 加载失败: {ex.Message}\r\n{ex.StackTrace}", true);
			}
			finally { SetBusyState(false); }
		}

		private async Task LoadAllModels()
		{
			await LoadObviousModel();
			await LoadSlightModel();
		}
		#endregion

		#region 图片加载
		private void BtnLoadImages_Click(object sender, EventArgs e)
		{
			try
			{
				_leftImage?.Dispose();
				_rightImage?.Dispose();

				_leftImage = Cv2.ImRead(txtLeftImagePath.Text);
				_rightImage = Cv2.ImRead(txtRightImagePath.Text);

				if (_leftImage.Empty() || _rightImage.Empty())
				{
					MessageBox.Show("图片加载失败！");
					return;
				}

				DisplayMat(picLeftOriginal, _leftImage);
				DisplayMat(picRightOriginal, _rightImage);

				AppendDebug($"✅ 图片加载成功: 左({_leftImage.Width}x{_leftImage.Height}) 右({_rightImage.Width}x{_rightImage.Height})");
				UpdateUIState(_obviousDefectModel != null && _slightDefectModel != null);
				lblStatus.Text = "图片已就绪";
			}
			catch (Exception ex)
			{
				AppendDebug($"❌ 图片加载失败: {ex.Message}\r\n{ex.StackTrace}", true);
			}
		}

	
		#endregion

		#region 执行检测
		private async Task RunInspection()
		{
			if (_obviousDefectModel == null || _slightDefectModel == null)
			{ MessageBox.Show("请先加载所有模型！"); return; }
			if (_leftImage == null || _rightImage == null)
			{ MessageBox.Show("请先加载图片！"); return; }

			try
			{
				SetBusyState(true, "执行检测中...");
				AppendDebug("=== 🚀 开始挂钩损伤检测 ===");
				AppendDebug($"参数: 盒子数={(int)numBoxCount.Value} 厚度阈值={(int)numThicknessThreshold.Value}px 置信度={trackConfThreshold.Value / 100f:F2}");
				AppendDebug($"类别: 内圈ID={(int)numBlueAreaClassId.Value} 外圈ID={(int)numHangHoleClassId.Value}");

				var sw = Stopwatch.StartNew();

				await Task.Run(() =>
				{
					_currentOutput = HookDamageDetector.CheckAllHookDamages(
						_leftImage.Clone(),
						_rightImage.Clone(),
						(int)numBoxCount.Value,
						_obviousDefectModel,
						_slightDefectModel,
						(double)numThicknessThreshold.Value,
						(int)numBlueAreaClassId.Value,
						(int)numHangHoleClassId.Value
					);
				});

				sw.Stop();
				AppendDebug($"✅ 检测完成! 总耗时: {sw.Elapsed.TotalSeconds:F2}s");
				AppendDebug($"结果: {_currentOutput.HookStatus.Count} 个挂钩");
				AppendDebug($"  OK: {_currentOutput.HookStatus.Count(s => s == "OK")}");
				AppendDebug($"  明显错位: {_currentOutput.HookStatus.Count(s => s == "挂钩明显错位")}");
				AppendDebug($"  轻微错位: {_currentOutput.HookStatus.Count(s => s == "轻微挂钩错位")}");
				AppendDebug($"  缺少: {_currentOutput.HookStatus.Count(s => s == "缺少")}");

				UpdateHookStatusCards();
				UpdateResultsGrid();
				UpdateDefectDetails();
				DrawResults();

				lblStatus.Text = $"检测完成 ({sw.Elapsed.TotalSeconds:F1}s)";
			}
			catch (Exception ex)
			{
				AppendDebug($"❌ 检测失败: {ex.Message}\r\n{ex.StackTrace}", true);
				MessageBox.Show($"检测失败:\n{ex.Message}\r\n{ex.StackTrace}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			finally { SetBusyState(false); }
		}
		#endregion

		#region 更新UI显示
		private void UpdateHookStatusCards()
		{
			hookStatusPanel.Controls.Clear();
			hookStatusCards.Clear();

			if (_currentOutput?.HookStatus == null) return;

			for (int i = 0; i < _currentOutput.HookStatus.Count; i++)
			{
				var status = _currentOutput.HookStatus[i];
				var color = StatusColors.ContainsKey(status) ? StatusColors[status] : Color.Gray;

				var card = new Panel
				{
					Width = 80,
					Height = 50,
					Margin = new Padding(3),
					BackColor = color,
					Tag = i
				};

				var lblIndex = new Label
				{
					Text = $"#{i + 1}",
					Dock = DockStyle.Top,
					TextAlign = ContentAlignment.MiddleCenter,
					ForeColor = Color.White,
					Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold),
					Height = 20,
					BackColor = ControlPaint.Dark(color, 0.3f)
				};

				var lblStatus = new Label
				{
					Text = status,
					Dock = DockStyle.Fill,
					TextAlign = ContentAlignment.MiddleCenter,
					ForeColor = Color.White,
					Font = new Font("Microsoft YaHei UI", 7F)
				};

				card.Controls.Add(lblStatus);
				card.Controls.Add(lblIndex);

				// 点击卡片可筛选表格
				card.Click += (s, e) =>
				{
					var idx = (int)((Panel)s).Tag;
					FilterResultsByIndex(idx);
				};

				hookStatusPanel.Controls.Add(card);
				hookStatusCards.Add(card);
			}
		}

		private void UpdateResultsGrid()
		{
			dgvResults.Rows.Clear();
			if (_currentOutput == null) return;

			int rowIndex = 0;

			// 遍历挂钩状态，分配全局索引
			int halfP = (int)numBoxCount.Value / 2;

			// 处理左侧 (索引 0 ~ halfP-1)
			for (int i = 0; i < halfP; i++)
			{
				if (i < _currentOutput.HookStatus.Count && _currentOutput.HookStatus[i] != "OK" && _currentOutput.HookStatus[i] != "缺少")
				{
					AddDefectRow(ref rowIndex, "左侧", i, _currentOutput.HookStatus[i], _currentOutput.LeftNgCoordinates);
				}
			}

			// 处理右侧 (索引 halfP ~ p-1)
			for (int i = halfP; i < _currentOutput.HookStatus.Count; i++)
			{
				if (_currentOutput.HookStatus[i] != "OK" && _currentOutput.HookStatus[i] != "缺少")
				{
					AddDefectRow(ref rowIndex, "右侧", i, _currentOutput.HookStatus[i], _currentOutput.RightNgCoordinates);
				}
			}
		}

		private void AddDefectRow(ref int rowIndex, string side, int globalIndex, string status, Dictionary<string, List<object>> ngDict)
		{
			if (ngDict == null || !ngDict.ContainsKey(status)) return;

			foreach (var item in ngDict[status])
			{
				if (status == "挂钩明显错位")
				{
					var coords = item as List<double>;
					if (coords != null && coords.Count >= 4)
					{
						dgvResults.Rows.Add(++rowIndex, side, globalIndex + 1, status,
							$"[{coords[0]:F3},{coords[1]:F3},{coords[2]:F3},{coords[3]:F3}]",
							"-", "-");
					}
				}
				else if (status == "轻微挂钩错位")
				{
					var arr = item as object[];
					if (arr != null && arr.Length >= 2)
					{
						int diameter = (int)arr[0];
						var centerPt = arr[1] as double[];
						if (centerPt != null && centerPt.Length >= 2)
						{
							dgvResults.Rows.Add(++rowIndex, side, globalIndex + 1, status,
								"-",
								$"{diameter}px",
								$"[{centerPt[0]:F4},{centerPt[1]:F4}]");
						}
					}
				}
			}
		}

		private void AddResultsToGrid(ref int rowIndex, string side, Dictionary<string, List<object>> ngDict)
		{
			if (ngDict == null) return;

			foreach (var kvp in ngDict)
			{
				string defectType = kvp.Key;
				foreach (var item in kvp.Value)
				{
					if (defectType == "挂钩明显错位")
					{
						var coords = item as List<double>;
						if (coords != null && coords.Count >= 4)
						{
							dgvResults.Rows.Add(rowIndex + 1, side, "-", defectType,
								$"[{coords[0]:F3},{coords[1]:F3},{coords[2]:F3},{coords[3]:F3}]",
								"-", "-");
							rowIndex++;
						}
					}
					else if (defectType == "轻微挂钩错位")
					{
						var arr = item as object[];
						if (arr != null && arr.Length >= 2)
						{
							int diameter = (int)arr[0];
							var centerPt = arr[1] as double[];
							if (centerPt != null && centerPt.Length >= 2)
							{
								dgvResults.Rows.Add(rowIndex + 1, side, "-", defectType,
									"-",
									diameter,
									$"[{centerPt[0]:F4},{centerPt[1]:F4}]");
								rowIndex++;
							}
						}
					}
				}
			}
		}

		private void UpdateDefectDetails()
		{
			rtbDefectDetails.Clear();
			if (_currentOutput == null) return;

			rtbDefectDetails.AppendText("=== 缺陷坐标详情 ===\n\n");

			rtbDefectDetails.AppendText("【左侧】\n");
			AppendDictDetails(_currentOutput.LeftNgCoordinates);

			rtbDefectDetails.AppendText("\n【右侧】\n");
			AppendDictDetails(_currentOutput.RightNgCoordinates);

			rtbDefectDetails.AppendText("\n=== 挂钩状态列表 ===\n");
			for (int i = 0; i < _currentOutput.HookStatus.Count; i++)
			{
				rtbDefectDetails.AppendText($"#{i + 1}: {_currentOutput.HookStatus[i]}\n");
			}
		}

		private void AppendDictDetails(Dictionary<string, List<object>> ngDict)
		{
			if (ngDict == null || ngDict.Count == 0)
			{
				rtbDefectDetails.AppendText("  无缺陷\n");
				return;
			}

			foreach (var kvp in ngDict)
			{
				rtbDefectDetails.AppendText($"  [{kvp.Key}] ({kvp.Value.Count}个)\n");
				for (int i = 0; i < kvp.Value.Count; i++)
				{
					var item = kvp.Value[i];
					if (kvp.Key == "挂钩明显错位")
					{
						var coords = item as List<double>;
						if (coords != null)
							rtbDefectDetails.AppendText($"    #{i + 1}: xyn=[{coords[0]:F3},{coords[1]:F3},{coords[2]:F3},{coords[3]:F3}]\n");
					}
					else if (kvp.Key == "轻微挂钩错位")
					{
						var arr = item as object[];
						if (arr != null)
						{
							var center = arr[1] as double[];
							rtbDefectDetails.AppendText($"    #{i + 1}: 直径={arr[0]}px, 中心=({center[0]:F4},{center[1]:F4})\n");
						}
					}
				}
			}
		}

		private void FilterResultsByIndex(int index)
		{
			// 可以根据挂钩索引筛选表格（扩展功能）
			AppendDebug($"点击了挂钩 #{index + 1}: {_currentOutput?.HookStatus[index]}");
		}

		#region 绘制检测结果（支持中文 + GDI+）

		/// <summary>
		/// 绘制所有检测结果到图像
		/// </summary>
		private void DrawResults()
		{
			if (_leftImage != null)
			{
				using (var leftDraw = _leftImage.Clone())
				{
					DrawDetectionOnImage(leftDraw, _currentOutput?.LeftNgCoordinates, _currentOutput?.HookStatus, 0, (int)numBoxCount.Value / 2);
					DisplayMat(picLeftResult, leftDraw);
				}
			}

			if (_rightImage != null)
			{
				using (var rightDraw = _rightImage.Clone())
				{
					DrawDetectionOnImage(rightDraw, _currentOutput?.RightNgCoordinates, _currentOutput?.HookStatus, (int)numBoxCount.Value / 2, (int)numBoxCount.Value);
					DisplayMat(picRightResult, rightDraw);
				}
			}
		}

		/// <summary>
		/// 在图像上绘制所有检测结果（用 GDI+ 绘制中文标签）
		/// </summary>
		private void DrawDetectionOnImage(Mat image, Dictionary<string, List<object>> ngDict, List<string> allStatus, int startIndex, int endIndex)
		{
			if (image == null || image.Empty()) return;

			int imgW = image.Width;
			int imgH = image.Height;

			// 先转成 Bitmap，方便后面用 GDI+ 画中文
			var bitmap = image.ToBitmap();

			using (var g = Graphics.FromImage(bitmap))
			{
				g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
				g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

				// ========== 1. 绘制区域分割线 ==========
				if (allStatus != null && endIndex > startIndex)
				{
					int totalBoxes = endIndex - startIndex;
					using (var pen = new Pen(Color.FromArgb(100, 100, 100), 1))
					{
						pen.DashStyle = System.Drawing.Drawing2D.DashStyle.DashDot;
						for (int i = 1; i < totalBoxes; i++)
						{
							float normX = (float)i / totalBoxes;
							int pixelX = (int)(normX * imgW);
							g.DrawLine(pen, pixelX, 0, pixelX, imgH);
						}
					}
				}

				// ========== 2. 收集绘制信息 ==========
				var drawItems = new List<DrawItem>();

				if (ngDict != null)
				{
					// 明显错位
					if (ngDict.ContainsKey("挂钩明显错位"))
					{
						foreach (var item in ngDict["挂钩明显错位"])
						{
							var coords = item as List<double>;
							if (coords == null || coords.Count < 4) continue;

							int x1 = Math.Max(0, Math.Min(imgW - 1, (int)(coords[0] * imgW)));
							int y1 = Math.Max(0, Math.Min(imgH - 1, (int)(coords[1] * imgH)));
							int x2 = Math.Max(0, Math.Min(imgW - 1, (int)(coords[2] * imgW)));
							int y2 = Math.Max(0, Math.Min(imgH - 1, (int)(coords[3] * imgH)));

							drawItems.Add(new DrawItem
							{
								Type = DefectType.Obvious,
								Rect = new Rectangle(x1, y1, x2 - x1, y2 - y1),
								Color = Color.Red,
								Label = "挂钩明显错位"
							});
						}
					}

					// 轻微错位
					if (ngDict.ContainsKey("轻微挂钩错位"))
					{
						foreach (var item in ngDict["轻微挂钩错位"])
						{
							var arr = item as object[];
							if (arr == null || arr.Length < 2) continue;

							int diameter = (int)arr[0];
							var centerPt = arr[1] as double[];
							if (centerPt == null || centerPt.Length < 2) continue;

							int centerX = Math.Max(0, Math.Min(imgW - 1, (int)(centerPt[0] * imgW)));
							int centerY = Math.Max(0, Math.Min(imgH - 1, (int)(centerPt[1] * imgH)));

							drawItems.Add(new DrawItem
							{
								Type = DefectType.Slight,
								CenterPoint = new Point(centerX, centerY),
								Diameter = diameter,
								Radius = diameter / 2,
								Color = Color.Orange,
								Label = $"轻微挂钩错位 d={diameter}px"
							});
						}
					}
				}

				// ========== 3. 绘制缺陷标记 ==========
				foreach (var item in drawItems)
				{
					if (item.Type == DefectType.Obvious)
					{
						DrawObviousDefect(g, item);
					}
					else if (item.Type == DefectType.Slight)
					{
						DrawSlightDefect(g, item);
					}
				}

				// ========== 4. 绘制顶部状态标签 ==========
				if (allStatus != null && endIndex > startIndex)
				{
					int totalBoxes = endIndex - startIndex;
					using (var font = new Font("Microsoft YaHei", 16, FontStyle.Bold))
					{
						for (int i = 0; i < totalBoxes; i++)
						{
							int globalIndex = startIndex + i;
							if (globalIndex >= allStatus.Count) break;

							string status = allStatus[globalIndex];
							float normX = (i + 0.5f) / totalBoxes;
							int pixelX = (int)(normX * imgW);

							string displayText;
							Color textColor;
							switch (status)
							{
								case "OK":
									displayText = "OK";
									textColor = Color.LimeGreen;
									break;
								case "挂钩明显错位":
									displayText = "明显错位!";
									textColor = Color.Red;
									break;
								case "轻微挂钩错位":
									displayText = "轻微错位";
									textColor = Color.Orange;
									break;
								default:
									displayText = "缺少";
									textColor = Color.Gray;
									break;
							}

							var textSize = g.MeasureString(displayText, font);
							float textX = pixelX - textSize.Width / 2;
							float textY = 10;

							// 文字阴影
							g.DrawString(displayText, font, Brushes.Black, textX + 1, textY + 1);
							// 文字本体
							using (var brush = new SolidBrush(textColor))
							{
								g.DrawString(displayText, font, brush, textX, textY);
							}
						}
					}
				}

				// ========== 5. 绘制图例（右下角） ==========
				DrawLegendGdi(g, imgW, imgH);
			}

			// 把 Bitmap 转回 Mat
			var resultMat = bitmap.ToMat();
			bitmap.Dispose();

			// 把结果复制回原图
			resultMat.CopyTo(image);
			resultMat.Dispose();
		}

		/// <summary>
		/// 绘制明显错位（红色矩形框 + 半透明填充）
		/// </summary>
		private void DrawObviousDefect(Graphics g, DrawItem item)
		{
			var rect = item.Rect;

			// 半透明红色填充
			using (var fillBrush = new SolidBrush(Color.FromArgb(60, 255, 0, 0)))
			{
				g.FillRectangle(fillBrush, rect);
			}

			// 外边框（细线）
			using (var outerPen = new Pen(Color.Red, 5))
			{
				g.DrawRectangle(outerPen, rect);
			}

			// 内边框（白色虚线增强可见度）
			using (var innerPen = new Pen(Color.White, 2))
			{
				innerPen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
				g.DrawRectangle(innerPen, rect.X + 3, rect.Y + 3, rect.Width - 6, rect.Height - 6);
			}

			// 标签
			int labelX = rect.X;
			int labelY = rect.Y - 30;
			if (labelY < 5) labelY = rect.Y + 5;

			DrawChineseLabel(g, item.Label, labelX, labelY, Color.Red, Color.White);
		}

		/// <summary>
		/// 绘制轻微错位（橙色圆圈 + 十字准心）
		/// </summary>
		private void DrawSlightDefect(Graphics g, DrawItem item)
		{
			int cx = item.CenterPoint.X;
			int cy = item.CenterPoint.Y;
			int radius = item.Radius;

			// 半透明橙色填充圆
			using (var fillBrush = new SolidBrush(Color.FromArgb(60, 255, 165, 0)))
			{
				g.FillEllipse(fillBrush, cx - radius, cy - radius, radius * 2, radius * 2);
			}

			// 圆环
			using (var circlePen = new Pen(Color.Orange, 4))
			{
				g.DrawEllipse(circlePen, cx - radius, cy - radius, radius * 2, radius * 2);
			}

			// 外环虚线
			using (var dashPen = new Pen(Color.Orange, 2))
			{
				dashPen.DashStyle = System.Drawing.Drawing2D.DashStyle.Dash;
				g.DrawEllipse(dashPen, cx - radius - 5, cy - radius - 5, (radius + 5) * 2, (radius + 5) * 2);
			}

			// 十字准心
			int crossLen = radius + 20;
			using (var crossPen = new Pen(Color.Orange, 3))
			{
				g.DrawLine(crossPen, cx - crossLen, cy, cx + crossLen, cy);
				g.DrawLine(crossPen, cx, cy - crossLen, cx, cy + crossLen);
			}

			// 中心点
			using (var centerBrush = new SolidBrush(Color.Red))
			{
				g.FillEllipse(centerBrush, cx - 6, cy - 6, 12, 12);
			}
			using (var centerPen = new Pen(Color.White, 2))
			{
				g.DrawEllipse(centerPen, cx - 6, cy - 6, 12, 12);
			}

			// 标签
			int labelX = cx;
			int labelY = cy - radius - 35;
			if (labelY < 5) labelY = cy + radius + 10;

			DrawChineseLabel(g, item.Label, labelX, labelY, Color.Orange, Color.White);
		}

		/// <summary>
		/// 用 GDI+ 绘制中文标签
		/// </summary>
		private void DrawChineseLabel(Graphics g, string text, int x, int y, Color bgColor, Color textColor)
		{
			using (var font = new Font("Microsoft YaHei", 12, FontStyle.Bold))
			{
				var textSize = g.MeasureString(text, font);

				// 调整位置
				int labelX = x;
				int labelY = y;

				// 边界保护 - 使用 g.VisibleClipBounds 需要显式转换
				int maxWidth = (int)g.VisibleClipBounds.Width;
				int maxHeight = (int)g.VisibleClipBounds.Height;
				int textWidth = (int)textSize.Width;
				int textHeight = (int)textSize.Height;

				if (labelX + textWidth > maxWidth)
					labelX = maxWidth - textWidth - 10;
				if (labelX < 5) labelX = 5;
				if (labelY < 5) labelY = 5;
				if (labelY + textHeight > maxHeight)
					labelY = maxHeight - textHeight - 10;

				// 背景
				var bgRect = new Rectangle(labelX - 5, labelY - 2, textWidth + 10, textHeight + 8);
				using (var bgBrush = new SolidBrush(bgColor))
				{
					g.FillRectangle(bgBrush, bgRect);
				}

				// 边框
				using (var borderPen = new Pen(Color.White, 2))
				{
					g.DrawRectangle(borderPen, bgRect);
				}

				// 文字
				using (var textBrush = new SolidBrush(textColor))
				{
					g.DrawString(text, font, textBrush, labelX, labelY + 2);
				}
			}
		}

		/// <summary>
		/// 绘制图例（右下角）
		/// </summary>
		private void DrawLegendGdi(Graphics g, int imgW, int imgH)
		{
			var legendItems = new (string text, Color color)[]
			{
		("挂钩明显错位", Color.Red),
		("轻微挂钩错位", Color.Orange),
		("正常 (OK)", Color.LimeGreen),
		("缺少", Color.Gray),
			};

			int itemHeight = 28;
			int padding = 12;
			int legendWidth = 200;
			int legendHeight = legendItems.Length * itemHeight + padding * 2;

			// 图例位置：右下角
			int legendX = imgW - legendWidth - 15;
			int legendY = imgH - legendHeight - 15;

			using (var font = new Font("Microsoft YaHei", 11, FontStyle.Bold))
			{
				// 背景
				var bgRect = new Rectangle(legendX, legendY, legendWidth, legendHeight);
				using (var bgBrush = new SolidBrush(Color.FromArgb(200, 40, 40, 40)))
				{
					g.FillRectangle(bgBrush, bgRect);
				}
				using (var borderPen = new Pen(Color.FromArgb(150, 150, 150), 2))
				{
					g.DrawRectangle(borderPen, bgRect);
				}

				// 标题
				using (var titleFont = new Font("Microsoft YaHei", 13, FontStyle.Bold))
				{
					var titleSize = g.MeasureString("检测图例", titleFont);
					int titleX = legendX + (legendWidth - (int)titleSize.Width) / 2;
					int titleY = legendY + padding;

					using (var titleBrush = new SolidBrush(Color.White))
					{
						g.DrawString("检测图例", titleFont, titleBrush, titleX, titleY);
					}

					// 图例项
					for (int i = 0; i < legendItems.Length; i++)
					{
						int itemY = legendY + padding + (int)titleSize.Height + 10 + i * itemHeight;

						// 色块
						var colorRect = new Rectangle(legendX + 15, itemY + 3, 22, 18);
						using (var colorBrush = new SolidBrush(legendItems[i].color))
						{
							g.FillRectangle(colorBrush, colorRect);
						}
						using (var colorPen = new Pen(Color.White, 1))
						{
							g.DrawRectangle(colorPen, colorRect);
						}

						// 文字
						using (var textBrush = new SolidBrush(Color.White))
						{
							g.DrawString(legendItems[i].text, font, textBrush, legendX + 45, itemY);
						}
					}
				}
			}
		}

		/// <summary>
		/// 绘制信息类
		/// </summary>
		private enum DefectType
		{
			Obvious,  // 明显错位
			Slight    // 轻微错位
		}

		private class DrawItem
		{
			public DefectType Type { get; set; }
			public Rectangle Rect { get; set; }
			public Point CenterPoint { get; set; }
			public int Diameter { get; set; }
			public int Radius { get; set; }
			public Color Color { get; set; }
			public string Label { get; set; }
		}

		/// <summary>
		/// 在 XLPictureBox 上显示 Mat
		/// </summary>
		private void DisplayMat(XLPictureBox picBox, Mat mat)
		{
			if (mat == null || mat.Empty()) { picBox.Image = null; return; }
			try
			{
				var bitmap = mat.ToBitmap();
				var old = picBox.Image;
				picBox.Image = bitmap;
				old?.Dispose();
			}
			catch (Exception ex)
			{
				AppendDebug($"显示错误: {ex.Message}\r\n{ex.StackTrace}", true);
			}
		}
		#endregion

		#region 导出渲染图
		/// <summary>
		/// 导出左侧渲染图
		/// </summary>
		private void ExportLeftRenderImage(object sender, EventArgs e)
		{
			ExportRenderImage("左侧", picLeftResult);
		}

		/// <summary>
		/// 导出右侧渲染图
		/// </summary>
		private void ExportRightRenderImage(object sender, EventArgs e)
		{
			ExportRenderImage("右侧", picRightResult);
		}

		private void ExportAllRenderImages(object sender, EventArgs e)
		{
			if (_leftImage == null || _rightImage == null)
			{
				MessageBox.Show("请先执行检测！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}

			using (var dialog = new SaveFileDialog
			{
				Filter = "PNG图片|*.png|JPEG图片|*.jpg|BMP图片|*.bmp",
				Title = "保存全部渲染结果",
				FileName = $"hook_inspection_all_{DateTime.Now:yyyyMMdd_HHmmss}.png"
			})
			{
				if (dialog.ShowDialog() == DialogResult.OK)
				{
					try
					{
						using (var leftDraw = _leftImage.Clone())
						using (var rightDraw = _rightImage.Clone())
						{
							DrawDetectionOnImage(leftDraw, _currentOutput?.LeftNgCoordinates, _currentOutput?.HookStatus, 0, (int)numBoxCount.Value / 2);
							DrawDetectionOnImage(rightDraw, _currentOutput?.RightNgCoordinates, _currentOutput?.HookStatus, (int)numBoxCount.Value / 2, (int)numBoxCount.Value);

							using (var leftBitmap = leftDraw.ToBitmap())
							using (var rightBitmap = rightDraw.ToBitmap())
							{
								int totalWidth = leftBitmap.Width + rightBitmap.Width;
								int maxHeight = Math.Max(leftBitmap.Height, rightBitmap.Height);

								using (var combinedBitmap = new Bitmap(totalWidth, maxHeight))
								using (var g = Graphics.FromImage(combinedBitmap))
								{
									g.Clear(Color.Black);

									// 左侧
									g.DrawImage(leftBitmap, 0, (maxHeight - leftBitmap.Height) / 2);
									// 右侧
									g.DrawImage(rightBitmap, leftBitmap.Width, (maxHeight - rightBitmap.Height) / 2);

									// 分割线
									using (var pen = new Pen(Color.White, 3))
									{
										g.DrawLine(pen, leftBitmap.Width, 0, leftBitmap.Width, maxHeight);
									}

									// 标题
									using (var font = new Font("Microsoft YaHei", 24, FontStyle.Bold))
									{
										var title = $"挂钩检测结果 - {DateTime.Now:yyyy-MM-dd HH:mm:ss}";
										var titleSize = g.MeasureString(title, font);
										int titleX = (int)((totalWidth - titleSize.Width) / 2);
										int titleY = 10;

										// 阴影
										g.DrawString(title, font, Brushes.Black, titleX + 2, titleY + 2);
										// 主体
										using (var titleBrush = new SolidBrush(Color.Cyan))
										{
											g.DrawString(title, font, titleBrush, titleX, titleY);
										}
									}

									// 保存
									combinedBitmap.Save(dialog.FileName);
								}
							}
						}

						AppendDebug($"✅ 全部渲染图已导出: {dialog.FileName}");
						lblStatus.Text = $"渲染图已导出";
						MessageBox.Show("全部渲染图导出成功！", "成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
					}
					catch (Exception ex)
					{
						AppendDebug($"❌ 导出失败: {ex.Message}\r\n{ex.StackTrace}", true);
						MessageBox.Show($"导出失败:\n{ex.Message}\r\n{ex.StackTrace}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
					}
				}
			}
		}

		/// <summary>
		/// 导出单张渲染图
		/// </summary>
		private void ExportRenderImage(string side, XLPictureBox picBox)
		{
			if (picBox.Image == null)
			{
				MessageBox.Show($"没有{side}渲染结果！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}

			using (var dialog = new SaveFileDialog
			{
				Filter = "PNG图片|*.png|JPEG图片|*.jpg|BMP图片|*.bmp",
				Title = $"保存{side}渲染结果",
				FileName = $"hook_inspection_{side}_{DateTime.Now:yyyyMMdd_HHmmss}.png"
			})
			{
				if (dialog.ShowDialog() == DialogResult.OK)
				{
					try
					{
						picBox.Image.Save(dialog.FileName);
						AppendDebug($"✅ {side}渲染图已导出: {dialog.FileName}");
						lblStatus.Text = $"{side}渲染图已导出";
					}
					catch (Exception ex)
					{
						AppendDebug($"❌ 导出失败: {ex.Message}\r\n{ex.StackTrace}", true);
					}
				}
			}
		}
		#endregion

		#region 绘制检测结果
		

		/// <summary>
		/// 绘制标签（带背景）
		/// </summary>
		private void DrawLabel(Mat image, string text, OpenCvSharp.Point position, Scalar color, double scale = 1.0)
		{
			int baseline;
			var textSize = Cv2.GetTextSize(text, HersheyFonts.HersheySimplex, 0.6 * scale, 2, out baseline);

			// 背景框
			var bgRect = new OpenCvSharp.Rect(
				position.X - 5,
				position.Y - textSize.Height - 5,
				textSize.Width + 10,
				textSize.Height + 10
			);

			// 边界保护
			if (bgRect.X < 0) bgRect.X = 0;
			if (bgRect.Y < 0) bgRect.Y = 0;
			if (bgRect.Right > image.Width) bgRect.X = image.Width - bgRect.Width;
			if (bgRect.Bottom > image.Height) bgRect.Y = image.Height - bgRect.Height;

			Cv2.Rectangle(image, bgRect, color, -1);
			Cv2.PutText(image, text,
				new OpenCvSharp.Point(bgRect.X + 5, bgRect.Y + textSize.Height + 2),
				HersheyFonts.HersheySimplex, 0.6 * scale, Scalar.White, 2);
		}

		/// <summary>
		/// 绘制图例
		/// </summary>
		private void DrawLegend(Mat image)
		{
			int legendX = image.Width - 220;
			int legendY = 10;
			int lineHeight = 25;

			// 背景
			Cv2.Rectangle(image,
				new OpenCvSharp.Rect(legendX - 5, legendY - 5, 210, 120),
				new Scalar(50, 50, 50), -1);
			Cv2.Rectangle(image,
				new OpenCvSharp.Rect(legendX - 5, legendY - 5, 210, 120),
				new Scalar(150, 150, 150), 1);

			// 图例项
			var legendItems = new (string text, Scalar color)[]
			{
		("明显错位", new Scalar(0, 0, 255)),
		("轻微错位", new Scalar(0, 165, 255)),
		("OK", new Scalar(0, 255, 0)),
		("缺少", new Scalar(128, 128, 128)),
			};

			for (int i = 0; i < legendItems.Length; i++)
			{
				int y = legendY + i * lineHeight;

				// 色块
				Cv2.Rectangle(image,
					new OpenCvSharp.Rect(legendX, y, 20, 15),
					legendItems[i].color, -1);

				// 文字
				Cv2.PutText(image, legendItems[i].text,
					new OpenCvSharp.Point(legendX + 30, y + 13),
					HersheyFonts.HersheySimplex, 0.5, Scalar.White, 1);
			}
		}

		
		#endregion

	

		private void ExportResults(object sender, EventArgs e)
		{
			if (_currentOutput == null) return;
			using (var dialog = new SaveFileDialog { Filter = "CSV|*.csv", FileName = $"hook_inspection_{DateTime.Now:yyyyMMdd_HHmmss}.csv" })
			{
				if (dialog.ShowDialog() == DialogResult.OK)
				{
					using (var writer = new StreamWriter(dialog.FileName, false, System.Text.Encoding.UTF8))
					{
						writer.WriteLine("序号,图像,全局索引,状态,归一化框,厚度,中心点");
						foreach (DataGridViewRow row in dgvResults.Rows)
						{
							var cells = row.Cells;
							writer.WriteLine($"{cells[0].Value},{cells[1].Value},{cells[2].Value},{cells[3].Value},{cells[4].Value},{cells[5].Value},{cells[6].Value}");
						}
					}
				}
			}
		}
		#endregion

		protected override void OnFormClosing(FormClosingEventArgs e)
		{
			_obviousDefectModel?.Dispose();
			_slightDefectModel?.Dispose();
			_leftImage?.Dispose();
			_rightImage?.Dispose();
			base.OnFormClosing(e);
		}
	}
}