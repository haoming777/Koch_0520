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
using YoloInference;
using XL.Controls;

// 命名空间别名
using Size = System.Drawing.Size;
using Point = System.Drawing.Point;
using PointF = System.Drawing.PointF;
using Rect = System.Drawing.Rectangle;

namespace HookInspectionSystem
{
	public partial class DownDefectForm : Form
	{
		// ========== 核心组件 ==========
		private YoloOnnx _yoloModel;
		private List<Mat> _downImages;
		private List<List<string>> _downStatus;
		private List<Dictionary<string, List<Rect2f>>> _ngCoordinates;
		private int _currentViewIndex = 0;

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

		// 缺陷颜色映射
		private static readonly Dictionary<string, Color> DefectColors = new Dictionary<string, Color>
		{
			{ "搭舌缺陷", Color.FromArgb(231, 76, 60) },    // 红色
            { "边缘问题", Color.FromArgb(230, 126, 34) },    // 橙色
            { "破损", Color.FromArgb(155, 89, 182) },        // 紫色
            { "OK", Color.FromArgb(39, 174, 96) },           // 绿色
            { "数量错误", Color.FromArgb(149, 165, 166) },   // 灰色
        };

		// ========== 界面控件 ==========
		private TableLayoutPanel mainLayout;

		// 模型配置
		private TextBox txtModelPath;
		private TextBox txtMetaJsonPath;
		private Label lblModelStatus;
		private Button btnBrowseModel;
		private Button btnBrowseMeta;
		private Button btnLoadModel;
		private NumericUpDown numBatchSize;

		// 图片配置
		private TextBox txtImageFolder;
		private Button btnBrowseFolder;
		private Button btnLoadImages;
		private NumericUpDown numBoxCount;
		private Label lblImageCount;

		// 参数配置
		private NumericUpDown numConfThreshold;
		private NumericUpDown numIouThreshold;
		private Button btnRunDetection;

		// 图像显示
		private XLPictureBox picCurrentImage;
		private XLPictureBox picResultImage;
		private Button btnPrevImage;
		private Button btnNextImage;
		private Label lblImageIndex;

		// 状态面板
		private FlowLayoutPanel statusPanel;
		private List<Panel> statusCards;

		// 结果列表
		private DataGridView dgvResults;
		private RichTextBox rtbDefectDetails;
		private RichTextBox rtbDebugOutput;

		// 导出按钮
		private Button btnExportCsv;
		private Button btnSaveCurrent;
		private Button btnSaveAll;

		// 状态
		private Label lblStatus;
		private ProgressBar progressBar;
		private ToolTip _toolTip;

		public DownDefectForm()
		{
			//InitializeComponent();
			InitializeCustomComponents();
		}

		private void InitializeCustomComponents()
		{
			this.Text = "下端面缺陷检测调试工具 v1.0";
			this.Size = new Size(1650, 1000);
			this.StartPosition = FormStartPosition.CenterScreen;
			this.MinimumSize = new Size(1400, 850);
			this.BackColor = BgColor;
			this.Font = new Font("Microsoft YaHei UI", 9F);

			_toolTip = new ToolTip();
			_downImages = new List<Mat>();

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
			mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 150F));
			mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 90F));    // 状态卡片
			mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 55F));     // 图像+表格
			mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 45F));     // 详情+调试
			this.Controls.Add(mainLayout);
		}
		#endregion

		#region 顶部面板
		private void CreateTopPanel()
		{
			var topPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
			var topLayout = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 4,
				RowCount = 1
			};
			topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));
			topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 28F));
			topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24F));
			topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));

			// ==================== 卡片1：模型配置 ====================
			var modelCard = CreateFlatCard("模型配置");
			var modelContent = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
			modelContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
			modelContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
			modelContent.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

			// ONNX路径
			var onnxPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
			onnxPanel.Controls.Add(new Label { Text = "ONNX:", Width = 42, TextAlign = ContentAlignment.MiddleRight, ForeColor = TextSecondary, Font = new Font("Microsoft YaHei UI", 8F) });
			txtModelPath = CreateFlatTextBox(@"F:\models\down_defect.onnx");
			txtModelPath.Width = 190;
			btnBrowseModel = CreateFlatButton("📁", PrimaryColor, false, 26);
			btnBrowseModel.Click += (s, e) => BrowseFile(txtModelPath, "ONNX|*.onnx");
			onnxPanel.Controls.AddRange(new Control[] { txtModelPath, btnBrowseModel });

			// Meta JSON路径
			var metaPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
			metaPanel.Controls.Add(new Label { Text = "Meta:", Width = 42, TextAlign = ContentAlignment.MiddleRight, ForeColor = TextSecondary, Font = new Font("Microsoft YaHei UI", 8F) });
			txtMetaJsonPath = CreateFlatTextBox(@"F:\models\meta.json");
			txtMetaJsonPath.Width = 190;
			btnBrowseMeta = CreateFlatButton("📁", PrimaryColor, false, 26);
			btnBrowseMeta.Click += (s, e) => BrowseFile(txtMetaJsonPath, "JSON|*.json");
			metaPanel.Controls.AddRange(new Control[] { txtMetaJsonPath, btnBrowseMeta });

			// 加载按钮
			var loadPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 3, 0, 0) };
			lblModelStatus = new Label { Text = "● 未加载", AutoSize = true, ForeColor = TextSecondary, Font = new Font("Microsoft YaHei UI", 8F), Padding = new Padding(5, 5, 5, 0) };
			numBatchSize = new NumericUpDown { Minimum = 1, Maximum = 16, Value = 4, Width = 50 };
			var lblBatch = new Label { Text = "Batch:", AutoSize = true, ForeColor = TextSecondary, Font = new Font("Microsoft YaHei UI", 7F), Padding = new Padding(0, 5, 0, 0) };
			btnLoadModel = CreateFlatButton("加载模型", SuccessColor, true, 32);
			btnLoadModel.Width = 90;
			btnLoadModel.Click += async (s, e) => await LoadModel();
			loadPanel.Controls.AddRange(new Control[] { lblModelStatus, lblBatch, numBatchSize, btnLoadModel });

			modelContent.Controls.Add(onnxPanel, 0, 0);
			modelContent.Controls.Add(metaPanel, 0, 1);
			modelContent.Controls.Add(loadPanel, 0, 2);
			modelCard.Controls.Add(modelContent);
			topLayout.Controls.Add(modelCard, 0, 0);

			// ==================== 卡片2：图片配置 ====================
			var imageCard = CreateFlatCard("图片配置");
			var imageContent = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
			imageContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
			imageContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
			imageContent.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

			var folderPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
			folderPanel.Controls.Add(new Label { Text = "图片目录:", Width = 58, TextAlign = ContentAlignment.MiddleRight, ForeColor = TextSecondary, Font = new Font("Microsoft YaHei UI", 8F) });
			txtImageFolder = CreateFlatTextBox(@"F:\images\down\");
			txtImageFolder.Width = 180;
			btnBrowseFolder = CreateFlatButton("📁", PrimaryColor, false, 26);
			btnBrowseFolder.Click += BrowseImageFolder;
			folderPanel.Controls.AddRange(new Control[] { txtImageFolder, btnBrowseFolder });

			var countPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
			countPanel.Controls.Add(new Label { Text = "盒子总数(p):", Width = 70, TextAlign = ContentAlignment.MiddleRight, ForeColor = TextSecondary, Font = new Font("Microsoft YaHei UI", 8F) });
			numBoxCount = new NumericUpDown { Minimum = 1, Maximum = 50, Value = 12, Width = 55 };
			lblImageCount = new Label { Text = "已加载: 0", AutoSize = true, ForeColor = TextSecondary, Font = new Font("Microsoft YaHei UI", 8F), Padding = new Padding(5, 0, 0, 0) };
			countPanel.Controls.AddRange(new Control[] { numBoxCount, lblImageCount });

			var imgBtnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 3, 0, 0) };
			btnLoadImages = CreateFlatButton("加载图片", PrimaryColor, true, 32);
			btnLoadImages.Width = 90;
			btnLoadImages.Click += BtnLoadImages_Click;
			imgBtnPanel.Controls.Add(btnLoadImages);

			imageContent.Controls.Add(folderPanel, 0, 0);
			imageContent.Controls.Add(countPanel, 0, 1);
			imageContent.Controls.Add(imgBtnPanel, 0, 2);
			imageCard.Controls.Add(imageContent);
			topLayout.Controls.Add(imageCard, 1, 0);

			// ==================== 卡片3：检测参数 ====================
			var paramsCard = CreateFlatCard("检测参数");
			var paramsContent = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), WrapContents = true };

			var confPanel = new Panel { Width = 200, Height = 28, Margin = new Padding(0, 0, 0, 4) };
			confPanel.Controls.Add(new Label { Text = "置信度:", Location = new Point(0, 4), AutoSize = true, ForeColor = TextSecondary, Font = new Font("Microsoft YaHei UI", 8F) });
			numConfThreshold = new NumericUpDown { Minimum = 0.1M, Maximum = 1.0M, Value = 0.5M, Width = 55, Location = new Point(60, 0), DecimalPlaces = 2, Increment = 0.05M };
			confPanel.Controls.Add(numConfThreshold);

			var iouPanel = new Panel { Width = 200, Height = 28, Margin = new Padding(0, 0, 0, 4) };
			iouPanel.Controls.Add(new Label { Text = "IOU阈值:", Location = new Point(0, 4), AutoSize = true, ForeColor = TextSecondary, Font = new Font("Microsoft YaHei UI", 8F) });
			numIouThreshold = new NumericUpDown { Minimum = 0.1M, Maximum = 1.0M, Value = 0.2M, Width = 55, Location = new Point(65, 0), DecimalPlaces = 2, Increment = 0.05M };
			iouPanel.Controls.Add(numIouThreshold);

			var infoLabel = new Label { Text = "类别: 搭舌缺陷(0), 边缘问题(1), 破损(2)", AutoSize = true, ForeColor = TextSecondary, Font = new Font("Microsoft YaHei UI", 7F), Padding = new Padding(0, 3, 0, 0) };

			paramsContent.Controls.AddRange(new Control[] { confPanel, iouPanel, infoLabel });
			paramsCard.Controls.Add(paramsContent);
			topLayout.Controls.Add(paramsCard, 2, 0);

			// ==================== 卡片4：操作按钮 ====================
			var actionCard = CreateFlatCard("操作");
			var actionContent = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), FlowDirection = FlowDirection.TopDown };

			btnRunDetection = CreateFlatButton("▶ 执行检测", DangerColor, true, 56);
			btnRunDetection.Width = 140;
			btnRunDetection.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
			btnRunDetection.Click += async (s, ev) => await RunDetection();

			actionContent.Controls.Add(btnRunDetection);
			actionCard.Controls.Add(actionContent);
			topLayout.Controls.Add(actionCard, 3, 0);

			topPanel.Controls.Add(topLayout);
			mainLayout.Controls.Add(topPanel, 0, 0);
		}
		#endregion

		#region 状态卡片面板
		private void CreateStatusCardsPanel()
		{
			var statusCard = CreateFlatCard("下端面状态概览");
			statusPanel = new FlowLayoutPanel
			{
				Dock = DockStyle.Fill,
				AutoScroll = true,
				Padding = new Padding(8),
				WrapContents = true
			};
			statusCards = new List<Panel>();
			statusCard.Controls.Add(statusPanel);
			mainLayout.Controls.Add(statusCard, 0, 1);
		}

		private void UpdateStatusCards()
		{
			if (statusPanel == null)
			{
				CreateStatusCardsPanel();
			}

			statusPanel.Controls.Clear();
			statusCards.Clear();

			if (_downStatus == null) return;

			for (int i = 0; i < _downStatus.Count; i++)
			{
				var statuses = _downStatus[i];
				var mainStatus = statuses.FirstOrDefault() ?? "未知";
				var color = DefectColors.ContainsKey(mainStatus) ? DefectColors[mainStatus] : Color.Gray;

				var card = new Panel
				{
					Width = 85,
					Height = 55,
					Margin = new Padding(3),
					BackColor = color,
					Tag = i,
					Cursor = Cursors.Hand
				};

				var lblIdx = new Label
				{
					Text = $"#{i + 1}",
					Dock = DockStyle.Top,
					TextAlign = ContentAlignment.MiddleCenter,
					ForeColor = Color.White,
					Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold),
					Height = 18,
					BackColor = ControlPaint.Dark(color, 0.25f)
				};

				var lblStatus = new Label
				{
					Text = mainStatus.Length > 4 ? mainStatus.Substring(0, 4) : mainStatus,
					Dock = DockStyle.Fill,
					TextAlign = ContentAlignment.MiddleCenter,
					ForeColor = Color.White,
					Font = new Font("Microsoft YaHei UI", 7F)
				};

				card.Controls.Add(lblStatus);
				card.Controls.Add(lblIdx);

				int index = i;
				card.Click += (s, e) => NavigateToImage(index);
				lblIdx.Click += (s, e) => NavigateToImage(index);
				lblStatus.Click += (s, e) => NavigateToImage(index);

				statusPanel.Controls.Add(card);
				statusCards.Add(card);
			}
		}
		#endregion

		#region 中间面板 - 图像显示
		private void CreateCenterPanel()
		{
			var centerPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(0, 8, 0, 8) };
			var centerLayout = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 3,
				RowCount = 1
			};
			centerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
			centerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
			centerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));

			// 原图
			var origPanel = CreateImagePanel("当前下端面图像", out picCurrentImage);
			// 结果图
			var resultPanel = CreateImagePanel("检测结果", out picResultImage);

			// 导航按钮
			var navPanel = new Panel { Dock = DockStyle.Top, Height = 30, BackColor = Color.FromArgb(240, 242, 245) };
			btnPrevImage = CreateFlatButton("◀ 上一个", PrimaryColor, false, 26);
			btnPrevImage.Width = 80;
			btnPrevImage.Click += (s, e) => NavigateImage(-1);
			btnNextImage = CreateFlatButton("下一个 ▶", PrimaryColor, false, 26);
			btnNextImage.Width = 80;
			btnNextImage.Click += (s, e) => NavigateImage(1);
			lblImageIndex = new Label { Text = "0 / 0", AutoSize = true, ForeColor = TextPrimary, Font = new Font("Consolas", 10F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };
			navPanel.Controls.AddRange(new Control[] { btnPrevImage, lblImageIndex, btnNextImage });
			lblImageIndex.Location = new Point(170, 5);
			btnNextImage.Location = new Point(250, 0);

			// 结果表格
			var resultsCard = CreateFlatCard("当前图像缺陷列表");
			var resultsContent = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
			resultsContent.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			resultsContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 36F));

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
				ColumnHeadersHeight = 26,
				RowTemplate = { Height = 22 },
				EnableHeadersVisualStyles = false,
				ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
				{
					BackColor = Color.FromArgb(240, 242, 245),
					ForeColor = TextPrimary,
					Font = new Font("Microsoft YaHei UI", 7F, FontStyle.Bold)
				}
			};
			dgvResults.Columns.Add("Index", "#");
			dgvResults.Columns.Add("DefectType", "缺陷类型");
			dgvResults.Columns.Add("X1", "X1");
			dgvResults.Columns.Add("Y1", "Y1");
			dgvResults.Columns.Add("X2", "X2");
			dgvResults.Columns.Add("Y2", "Y2");
			dgvResults.Columns["Index"].Width = 30;
			dgvResults.Columns["DefectType"].Width = 70;

			var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(4), FlowDirection = FlowDirection.LeftToRight };
			btnExportCsv = CreateFlatButton("导出CSV", TextSecondary, false, 26);
			btnExportCsv.Click += ExportCsv;
			btnSaveCurrent = CreateFlatButton("保存当前", PrimaryColor, false, 26);
			btnSaveCurrent.Click += SaveCurrentImage;
			btnSaveAll = CreateFlatButton("保存全部", SuccessColor, false, 26);
			btnSaveAll.Click += SaveAllImages;
			btnPanel.Controls.AddRange(new Control[] { btnExportCsv, btnSaveCurrent, btnSaveAll });

			resultsContent.Controls.Add(dgvResults, 0, 0);
			resultsContent.Controls.Add(btnPanel, 0, 1);
			resultsCard.Controls.Add(resultsContent);

			// 组装
			var leftPanel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
			leftPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			leftPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 35F));
			leftPanel.Controls.Add(origPanel, 0, 0);
			leftPanel.Controls.Add(navPanel, 0, 1);

			centerLayout.Controls.Add(leftPanel, 0, 0);
			centerLayout.Controls.Add(resultPanel, 1, 0);
			centerLayout.Controls.Add(resultsCard, 2, 0);

			centerPanel.Controls.Add(centerLayout);
			mainLayout.Controls.Add(centerPanel, 0, 2);
		}

		private Panel CreateImagePanel(string title, out XLPictureBox pictureBox)
		{
			var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(3), BackColor = Color.Transparent };
			var layout = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
			layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
			layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 24F));

			var titleBar = new Panel { Dock = DockStyle.Fill, BackColor = Color.FromArgb(240, 242, 245) };
			titleBar.Controls.Add(new Label { Text = title, Location = new Point(4, 3), AutoSize = true, Font = new Font("Microsoft YaHei UI", 7F, FontStyle.Bold), ForeColor = TextPrimary });

			pictureBox = new XLPictureBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(20, 20, 20), BorderStyle = BorderStyle.None };

			layout.Controls.Add(pictureBox, 0, 0);
			layout.Controls.Add(titleBar, 0, 1);
			panel.Controls.Add(layout);
			return panel;
		}
		#endregion

		#region 底部面板
		private void CreateBottomPanel()
		{
			var bottomPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
			var bottomLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
			bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));
			bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50F));

			var defectCard = CreateFlatCard("全部缺陷坐标详情");
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

			bottomLayout.Controls.Add(defectCard, 0, 0);
			bottomLayout.Controls.Add(debugCard, 1, 0);
			bottomPanel.Controls.Add(bottomLayout);
			mainLayout.Controls.Add(bottomPanel, 0, 3);
		}
		#endregion

		#region 状态栏
		private void CreateStatusBar()
		{
			var bar = new Panel { Dock = DockStyle.Bottom, Height = 26, BackColor = Color.FromArgb(250, 251, 252) };
			lblStatus = new Label { Text = "就绪", Location = new Point(12, 4), AutoSize = true, Font = new Font("Microsoft YaHei UI", 8F), ForeColor = TextSecondary };
			progressBar = new ProgressBar { Style = ProgressBarStyle.Marquee, Width = 120, Height = 16, Location = new Point(this.Width - 140, 4), Visible = false, Anchor = AnchorStyles.Right | AnchorStyles.Top };
			bar.Controls.Add(lblStatus);
			bar.Controls.Add(progressBar);
			this.Controls.Add(bar);
		}
		#endregion

		#region UI辅助方法
		private Panel CreateFlatCard(string title)
		{
			var card = new Panel { Dock = DockStyle.Fill, BackColor = CardBgColor, Margin = new Padding(4) };
			card.Paint += (s, e) => { using (var pen = new Pen(BorderColor, 1)) e.Graphics.DrawRectangle(pen, 0, 0, card.Width - 1, card.Height - 1); };
			return card;
		}

		private TextBox CreateFlatTextBox(string text)
		{
			return new TextBox { Text = text, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 8F), BackColor = Color.White, ForeColor = TextPrimary };
		}

		private Button CreateFlatButton(string text, Color color, bool fill = false, int height = 26)
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

		private void BrowseFile(TextBox tb, string filter)
		{
			using (var dlg = new OpenFileDialog { Filter = filter })
			{ if (dlg.ShowDialog() == DialogResult.OK) tb.Text = dlg.FileName; }
		}

		private void BrowseImageFolder(object sender, EventArgs e)
		{
			using (var dlg = new FolderBrowserDialog { Description = "选择下端面图片目录" })
			{ if (dlg.ShowDialog() == DialogResult.OK) txtImageFolder.Text = dlg.SelectedPath; }
		}

		private void AppendDebug(string msg, bool isError = false)
		{
			if (rtbDebugOutput.InvokeRequired)
			{ rtbDebugOutput.Invoke(new Action(() => AppendDebug(msg, isError))); return; }
			rtbDebugOutput.SelectionColor = isError ? Color.FromArgb(255, 100, 100) : Color.FromArgb(180, 180, 180);
			rtbDebugOutput.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
			rtbDebugOutput.ScrollToCaret();
		}

		private void SetBusy(bool busy, string status = "")
		{
			if (this.InvokeRequired) { this.Invoke(new Action(() => SetBusy(busy, status))); return; }
			progressBar.Visible = busy;
			btnRunDetection.Enabled = !busy && _yoloModel != null && _downImages.Count > 0;
			if (!string.IsNullOrEmpty(status)) lblStatus.Text = status;
			Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
		}

		private void UpdateUIState(bool modelLoaded)
		{
			btnRunDetection.Enabled = modelLoaded && _downImages.Count > 0;
		}

		private void DisplayMat(XLPictureBox pb, Mat mat)
		{
			if (mat == null || mat.Empty()) { pb.Image = null; return; }
			try { var bmp = mat.ToBitmap(); var old = pb.Image; pb.Image = bmp; old?.Dispose(); } catch { }
		}
		#endregion

		#region 模型加载
		private async Task LoadModel()
		{
			if (!File.Exists(txtModelPath.Text)) { MessageBox.Show("ONNX模型不存在！"); return; }
			if (!File.Exists(txtMetaJsonPath.Text)) { MessageBox.Show("Meta JSON不存在！"); return; }

			try
			{
				SetBusy(true, "加载模型中...");
				lblModelStatus.Text = "● 加载中...";
				lblModelStatus.ForeColor = WarningColor;
				AppendDebug($"📦 加载模型: {Path.GetFileName(txtModelPath.Text)} (Batch={numBatchSize.Value})");

				await Task.Run(() =>
				{
					_yoloModel?.Dispose();
					_yoloModel = new YoloOnnx(txtModelPath.Text, txtMetaJsonPath.Text, (int)numBatchSize.Value);
				});

				lblModelStatus.Text = "● 已就绪";
				lblModelStatus.ForeColor = SuccessColor;
				UpdateUIState(true);
				AppendDebug($"✅ 模型加载成功");
			}
			catch (Exception ex)
			{
				lblModelStatus.Text = "● 失败";
				lblModelStatus.ForeColor = DangerColor;
				AppendDebug($"❌ 加载失败: {ex.Message}\r\n{ex.StackTrace}", true);
			}
			finally { SetBusy(false); }
		}
		#endregion

		#region 图片加载
		private void BtnLoadImages_Click(object sender, EventArgs e)
		{
			try
			{
				string folder = txtImageFolder.Text;
				if (!Directory.Exists(folder)) { MessageBox.Show("图片目录不存在！"); return; }

				var files = Directory.GetFiles(folder)
					.Where(f => f.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase) ||
								f.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase) ||
								f.EndsWith(".png", StringComparison.OrdinalIgnoreCase) ||
								f.EndsWith(".bmp", StringComparison.OrdinalIgnoreCase))
					.OrderBy(f => f)
					.Take((int)numBoxCount.Value)
					.ToList();

				// 清理旧图片
				foreach (var img in _downImages) img?.Dispose();
				_downImages.Clear();

				foreach (var file in files)
				{
					var mat = Cv2.ImRead(file);
					if (!mat.Empty()) _downImages.Add(mat);
				}

				lblImageCount.Text = $"已加载: {_downImages.Count}";
				_currentViewIndex = 0;
				NavigateToImage(0);
				UpdateUIState(_yoloModel != null);
				AppendDebug($"✅ 加载 {_downImages.Count} 张图片");
			}
			catch (Exception ex) { AppendDebug($"❌ 加载失败: {ex.Message}\r\n{ex.StackTrace}", true); }
		}

		private void NavigateToImage(int index)
		{
			if (_downImages.Count == 0) return;
			_currentViewIndex = Math.Max(0, Math.Min(index, _downImages.Count - 1));
			DisplayMat(picCurrentImage, _downImages[_currentViewIndex]);
			lblImageIndex.Text = $"{_currentViewIndex + 1} / {_downImages.Count}";

			// 如果有检测结果，绘制当前图像的结果
			if (_ngCoordinates != null && _currentViewIndex < _ngCoordinates.Count)
			{
				DrawResultForCurrent();
			}
			UpdateCurrentResultsGrid();
		}

		private void NavigateImage(int delta)
		{
			NavigateToImage(_currentViewIndex + delta);
		}
		#endregion

		#region 执行检测
		private async Task RunDetection()
		{
			if (_yoloModel == null) { MessageBox.Show("请先加载模型！"); return; }
			if (_downImages.Count == 0) { MessageBox.Show("请先加载图片！"); return; }

			try
			{
				SetBusy(true, "检测中...");
				AppendDebug($"=== 🚀 下端面缺陷检测 ===");
				AppendDebug($"图片数: {_downImages.Count} | 盒子数: {numBoxCount.Value}");

				var sw = Stopwatch.StartNew();

				await Task.Run(() =>
				{
					// 调用 DefectDetectionService
					var result = DefectDetectionService.CheckDownDefects(
						_yoloModel, _downImages, (int)numBoxCount.Value);
					_downStatus = result.downStatus;
					_ngCoordinates = result.ngCoordinates;
				});

				sw.Stop();
				AppendDebug($"✅ 检测完成! 耗时: {sw.Elapsed.TotalMilliseconds:F0}ms");

				int defectCount = _downStatus.Count(s => s.FirstOrDefault() != "OK");
				AppendDebug($"   缺陷数: {defectCount} / {_downStatus.Count}");

				UpdateStatusCards();
				UpdateDefectDetails();
				NavigateToImage(_currentViewIndex);
				DrawResultForCurrent();

				lblStatus.Text = $"检测完成 ({sw.Elapsed.TotalSeconds:F1}s)";
			}
			catch (Exception ex)
			{
				AppendDebug($"❌ 检测失败: {ex.Message}\r\n{ex.StackTrace}", true);
				MessageBox.Show($"检测失败:\n{ex.Message}\r\n{ex.StackTrace}");
			}
			finally { SetBusy(false); }
		}
		#endregion

		#region 绘制结果
		private void DrawResultForCurrent()
		{
			if (_downImages.Count == 0 || _currentViewIndex >= _downImages.Count) return;
			if (_ngCoordinates == null || _currentViewIndex >= _ngCoordinates.Count) return;

			using (var drawImg = _downImages[_currentViewIndex].Clone())
			{
				int w = drawImg.Width;
				int h = drawImg.Height;
				var coords = _ngCoordinates[_currentViewIndex];

				// 先转Bitmap用GDI+画中文
				var bitmap = drawImg.ToBitmap();
				using (var g = Graphics.FromImage(bitmap))
				{
					g.SmoothingMode = SmoothingMode.AntiAlias;

					int idx = 0;
					foreach (var kvp in coords)
					{
						string defectType = kvp.Key;
						var boxes = kvp.Value;
						var color = DefectColors.ContainsKey(defectType) ? DefectColors[defectType] : Color.Red;

						foreach (var box in boxes)
						{
							int x1 = (int)(box.X * w);
							int y1 = (int)(box.Y * h);
							int x2 = (int)((box.X + box.Width) * w);
							int y2 = (int)((box.Y + box.Height) * h);

							var rect = new Rect(x1, y1, x2 - x1, y2 - y1);

							// 半透明填充
							using (var fill = new SolidBrush(Color.FromArgb(50, color)))
								g.FillRectangle(fill, rect);
							// 边框
							using (var pen = new Pen(color, 3))
								g.DrawRectangle(pen, rect);

							// 标签
							using (var font = new Font("Microsoft YaHei", 10, FontStyle.Bold))
							{
								string label = $"{defectType} #{idx + 1}";
								var ts = g.MeasureString(label, font);
								int lx = x1, ly = y1 - (int)ts.Height - 5;
								if (ly < 5) ly = y1 + 5;

								var bg = new Rect(lx - 2, ly - 2, (int)ts.Width + 8, (int)ts.Height + 6);
								using (var bgBrush = new SolidBrush(color))
									g.FillRectangle(bgBrush, bg);
								g.DrawString(label, font, Brushes.White, lx + 2, ly + 1);
							}
							idx++;
						}
					}
				}

				var resultMat = bitmap.ToMat();
				DisplayMat(picResultImage, resultMat);
				resultMat.Dispose();
				bitmap.Dispose();
			}
		}

		private void UpdateCurrentResultsGrid()
		{
			dgvResults.Rows.Clear();
			if (_ngCoordinates == null || _currentViewIndex >= _ngCoordinates.Count) return;

			var coords = _ngCoordinates[_currentViewIndex];
			int idx = 0;
			foreach (var kvp in coords)
			{
				foreach (var box in kvp.Value)
				{
					dgvResults.Rows.Add(++idx, kvp.Key,
						box.X.ToString("F4"), box.Y.ToString("F4"),
						(box.X + box.Width).ToString("F4"), (box.Y + box.Height).ToString("F4"));
				}
			}
		}

		private void UpdateDefectDetails()
		{
			rtbDefectDetails.Clear();
			if (_downStatus == null) return;

			rtbDefectDetails.AppendText("=== 下端面缺陷坐标详情 ===\n\n");
			rtbDefectDetails.AppendText($"总图像数: {_downStatus.Count}\n");
			rtbDefectDetails.AppendText($"类别映射: 0=搭舌缺陷, 1=边缘问题, 2=破损\n\n");

			for (int i = 0; i < _downStatus.Count; i++)
			{
				var statuses = _downStatus[i];
				rtbDefectDetails.AppendText($"图像 #{i + 1}: [{string.Join(", ", statuses)}]\n");

				if (_ngCoordinates != null && i < _ngCoordinates.Count)
				{
					foreach (var kvp in _ngCoordinates[i])
					{
						rtbDefectDetails.AppendText($"  {kvp.Key}: {kvp.Value.Count}个\n");
						for (int j = 0; j < kvp.Value.Count; j++)
						{
							var b = kvp.Value[j];
							rtbDefectDetails.AppendText($"    [{b.X:F4},{b.Y:F4},{b.X + b.Width:F4},{b.Y + b.Height:F4}]\n");
						}
					}
				}
				rtbDefectDetails.AppendText("\n");
			}
		}
		#endregion

		#region 导出
		private void ExportCsv(object sender, EventArgs e)
		{
			if (_downStatus == null) return;
			using (var dlg = new SaveFileDialog { Filter = "CSV|*.csv", FileName = $"down_defects_{DateTime.Now:yyyyMMdd_HHmmss}.csv" })
			{
				if (dlg.ShowDialog() == DialogResult.OK)
				{
					using (var w = new StreamWriter(dlg.FileName, false, System.Text.Encoding.UTF8))
					{
						w.WriteLine("图像序号,缺陷类型,X1,Y1,X2,Y2");
						for (int i = 0; i < _ngCoordinates.Count; i++)
						{
							foreach (var kvp in _ngCoordinates[i])
								foreach (var b in kvp.Value)
									w.WriteLine($"{i + 1},{kvp.Key},{b.X:F6},{b.Y:F6},{b.X + b.Width:F6},{b.Y + b.Height:F6}");
						}
					}
					AppendDebug($"✅ CSV已导出");
				}
			}
		}

		private void SaveCurrentImage(object sender, EventArgs e)
		{
			if (picResultImage.Image == null) return;
			using (var dlg = new SaveFileDialog { Filter = "PNG|*.png", FileName = $"down_result_{_currentViewIndex + 1}_{DateTime.Now:HHmmss}.png" })
			{ if (dlg.ShowDialog() == DialogResult.OK) picResultImage.Image.Save(dlg.FileName); }
		}

		private void SaveAllImages(object sender, EventArgs e)
		{
			if (_downImages.Count == 0) return;
			using (var dlg = new FolderBrowserDialog { Description = "选择保存目录" })
			{
				if (dlg.ShowDialog() == DialogResult.OK)
				{
					for (int i = 0; i < _downImages.Count; i++)
					{
						_currentViewIndex = i;
						DrawResultForCurrent();
						if (picResultImage.Image != null)
						{
							string path = Path.Combine(dlg.SelectedPath, $"down_result_{i + 1}.png");
							picResultImage.Image.Save(path);
						}
					}
					AppendDebug($"✅ 已保存 {_downImages.Count} 张结果图");
				}
			}
		}
		#endregion

		protected override void OnFormClosing(FormClosingEventArgs e)
		{
			_yoloModel?.Dispose();
			foreach (var img in _downImages) img?.Dispose();
			_downImages.Clear();
			base.OnFormClosing(e);
		}
	}

	
}