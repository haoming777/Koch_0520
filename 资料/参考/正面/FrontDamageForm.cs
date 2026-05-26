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
using YoloInference;

// 命名空间别名
using Size = System.Drawing.Size;
using Point = System.Drawing.Point;
using PointF = System.Drawing.PointF;
using Rect = System.Drawing.Rectangle;

namespace HookInspectionSystem
{
	public partial class FrontDamageForm : Form
	{
		// ========== 核心组件 ==========
		private YoloOnnx _yoloModel;
		private Mat _leftImage;
		private Mat _rightImage;
		private List<string> _statusList;
		private Dictionary<string, List<float[]>> _finalLeftDict;
		private Dictionary<string, List<float[]>> _finalRightDict;

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

		// 状态颜色映射
		private static readonly Dictionary<string, Color> StatusColors = new Dictionary<string, Color>
		{
			{ "OK", Color.FromArgb(39, 174, 96) },
			{ "破损", Color.FromArgb(231, 76, 60) },
			{ "damage", Color.FromArgb(231, 76, 60) },
			{ "数量错误", Color.FromArgb(149, 165, 166) },
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
		private TextBox txtLeftImagePath;
		private TextBox txtRightImagePath;
		private Button btnBrowseLeft;
		private Button btnBrowseRight;
		private Button btnLoadImages;
		private NumericUpDown numBoxCount;
		private Label lblImageInfo;

		// 参数配置
		private NumericUpDown numConfThreshold;
		private NumericUpDown numIouThreshold;
		private Button btnRunDetection;

		// 图像显示（4个区域）
		private XLPictureBox picLeftOriginal;
		private XLPictureBox picRightOriginal;
		private XLPictureBox picLeftResult;
		private XLPictureBox picRightResult;

		// 切片预览
		private XLPictureBox picPatchesPreview;

		// 状态面板
		private FlowLayoutPanel statusPanel;
		private List<Panel> statusCards;

		// 结果列表
		private DataGridView dgvResults;
		private RichTextBox rtbDefectDetails;
		private RichTextBox rtbDebugOutput;

		// 导出按钮
		private Button btnExportCsv;
		private Button btnSaveLeft;
		private Button btnSaveRight;
		private Button btnSaveAll;

		// 状态
		private Label lblStatus;
		private ProgressBar progressBar;
		private ToolTip _toolTip;

		public FrontDamageForm()
		{
			//InitializeComponent();
			InitializeCustomComponents();
		}

		private void InitializeCustomComponents()
		{
			this.Text = "正面损伤检测调试工具 v1.0";
			this.Size = new Size(1700, 1050);
			this.StartPosition = FormStartPosition.CenterScreen;
			this.MinimumSize = new Size(1500, 900);
			this.BackColor = BgColor;
			this.Font = new Font("Microsoft YaHei UI", 9F);

			_toolTip = new ToolTip();

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
			mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 155F));   // 顶部配置
			mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 85F));    // 状态卡片
			mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 55F));     // 图像显示
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

			var onnxPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
			onnxPanel.Controls.Add(new Label { Text = "ONNX:", Width = 42, TextAlign = ContentAlignment.MiddleRight, ForeColor = TextSecondary, Font = new Font("Microsoft YaHei UI", 8F) });
			txtModelPath = CreateFlatTextBox(@"F:\models\front_damage.onnx");
			txtModelPath.Width = 185;
			btnBrowseModel = CreateFlatButton("📁", PrimaryColor, false, 26);
			btnBrowseModel.Click += (s, e) => BrowseFile(txtModelPath, "ONNX|*.onnx");
			onnxPanel.Controls.AddRange(new Control[] { txtModelPath, btnBrowseModel });

			var metaPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
			metaPanel.Controls.Add(new Label { Text = "Meta:", Width = 42, TextAlign = ContentAlignment.MiddleRight, ForeColor = TextSecondary, Font = new Font("Microsoft YaHei UI", 8F) });
			txtMetaJsonPath = CreateFlatTextBox(@"F:\models\meta.json");
			txtMetaJsonPath.Width = 185;
			btnBrowseMeta = CreateFlatButton("📁", PrimaryColor, false, 26);
			btnBrowseMeta.Click += (s, e) => BrowseFile(txtMetaJsonPath, "JSON|*.json");
			metaPanel.Controls.AddRange(new Control[] { txtMetaJsonPath, btnBrowseMeta });

			var loadPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 3, 0, 0) };
			lblModelStatus = new Label { Text = "● 未加载", AutoSize = true, ForeColor = TextSecondary, Font = new Font("Microsoft YaHei UI", 8F), Padding = new Padding(5, 5, 5, 0) };
			numBatchSize = new NumericUpDown { Minimum = 1, Maximum = 16, Value = 6, Width = 50 };
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
			var imageCard = CreateFlatCard("图片配置（双相机）");
			var imageContent = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
			imageContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
			imageContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 38F));
			imageContent.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

			var leftImgPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
			leftImgPanel.Controls.Add(new Label { Text = "左侧:", Width = 35, TextAlign = ContentAlignment.MiddleRight, ForeColor = TextSecondary, Font = new Font("Microsoft YaHei UI", 8F) });
			txtLeftImagePath = CreateFlatTextBox(@"F:\images\front_left.jpg");
			txtLeftImagePath.Width = 165;
			btnBrowseLeft = CreateFlatButton("📁", PrimaryColor, false, 26);
			btnBrowseLeft.Click += (s, e) => BrowseFile(txtLeftImagePath, "图片|*.jpg;*.jpeg;*.png;*.bmp");
			leftImgPanel.Controls.AddRange(new Control[] { txtLeftImagePath, btnBrowseLeft });

			var rightImgPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
			rightImgPanel.Controls.Add(new Label { Text = "右侧:", Width = 35, TextAlign = ContentAlignment.MiddleRight, ForeColor = TextSecondary, Font = new Font("Microsoft YaHei UI", 8F) });
			txtRightImagePath = CreateFlatTextBox(@"F:\images\front_right.jpg");
			txtRightImagePath.Width = 165;
			btnBrowseRight = CreateFlatButton("📁", PrimaryColor, false, 26);
			btnBrowseRight.Click += (s, e) => BrowseFile(txtRightImagePath, "图片|*.jpg;*.jpeg;*.png;*.bmp");
			rightImgPanel.Controls.AddRange(new Control[] { txtRightImagePath, btnBrowseRight });

			var countPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft, Padding = new Padding(0, 3, 0, 0) };
			numBoxCount = new NumericUpDown { Minimum = 2, Maximum = 50, Value = 12, Width = 55 };
			var lblP = new Label { Text = "盒子总数(p):", AutoSize = true, ForeColor = TextSecondary, Font = new Font("Microsoft YaHei UI", 8F), Padding = new Padding(0, 5, 0, 0) };
			lblImageInfo = new Label { Text = "未加载", AutoSize = true, ForeColor = TextSecondary, Font = new Font("Microsoft YaHei UI", 8F), Padding = new Padding(5, 5, 5, 0) };
			btnLoadImages = CreateFlatButton("加载图片", PrimaryColor, true, 32);
			btnLoadImages.Width = 90;
			btnLoadImages.Click += BtnLoadImages_Click;
			countPanel.Controls.AddRange(new Control[] { lblImageInfo, lblP, numBoxCount, btnLoadImages });

			imageContent.Controls.Add(leftImgPanel, 0, 0);
			imageContent.Controls.Add(rightImgPanel, 0, 1);
			imageContent.Controls.Add(countPanel, 0, 2);
			imageCard.Controls.Add(imageContent);
			topLayout.Controls.Add(imageCard, 1, 0);

			// ==================== 卡片3：检测参数 ====================
			var paramsCard = CreateFlatCard("检测参数");
			var paramsContent = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(10), WrapContents = true };

			var confPanel = new Panel { Width = 200, Height = 28, Margin = new Padding(0, 0, 0, 4) };
			confPanel.Controls.Add(new Label { Text = "置信度:", Location = new Point(0, 4), AutoSize = true, ForeColor = TextSecondary, Font = new Font("Microsoft YaHei UI", 8F) });
			numConfThreshold = new NumericUpDown { Minimum = 0.1M, Maximum = 1.0M, Value = 0.25M, Width = 55, Location = new Point(60, 0), DecimalPlaces = 2, Increment = 0.05M };
			confPanel.Controls.Add(numConfThreshold);

			var iouPanel = new Panel { Width = 200, Height = 28, Margin = new Padding(0, 0, 0, 4) };
			iouPanel.Controls.Add(new Label { Text = "IOU阈值:", Location = new Point(0, 4), AutoSize = true, ForeColor = TextSecondary, Font = new Font("Microsoft YaHei UI", 8F) });
			numIouThreshold = new NumericUpDown { Minimum = 0.1M, Maximum = 1.0M, Value = 0.45M, Width = 55, Location = new Point(65, 0), DecimalPlaces = 2, Increment = 0.05M };
			iouPanel.Controls.Add(numIouThreshold);

			var infoLabel = new Label
			{
				Text = "流程: 切分6个区域 → 批量推理 → 坐标映射 → NMS合并",
				AutoSize = true,
				ForeColor = TextSecondary,
				Font = new Font("Microsoft YaHei UI", 7F),
				Padding = new Padding(0, 3, 0, 0)
			};
			var infoLabel2 = new Label
			{
				Text = "切片: 3列(0-40%,40-80%,80-100%) × 2行(0-55%,45-100%)",
				AutoSize = true,
				ForeColor = TextSecondary,
				Font = new Font("Microsoft YaHei UI", 7F)
			};

			paramsContent.Controls.AddRange(new Control[] { confPanel, iouPanel, infoLabel, infoLabel2 });
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
			var statusCard = CreateFlatCard("正面状态概览（点击切换视角）");
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
			if (statusPanel == null) CreateStatusCardsPanel();
			statusPanel.Controls.Clear();
			statusCards.Clear();

			if (_statusList == null) return;

			for (int i = 0; i < _statusList.Count; i++)
			{
				string status = _statusList[i];
				Color color = StatusColors.ContainsKey(status) ? StatusColors[status] : Color.Gray;

				var card = new Panel
				{
					Width = 80,
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

				string displayText = status.Length > 3 ? status.Substring(0, 3) : status;
				var lblStatus = new Label
				{
					Text = displayText,
					Dock = DockStyle.Fill,
					TextAlign = ContentAlignment.MiddleCenter,
					ForeColor = Color.White,
					Font = new Font("Microsoft YaHei UI", 8F)
				};

				card.Controls.Add(lblStatus);
				card.Controls.Add(lblIdx);

				int index = i;
				card.Click += (s, e) => AppendDebug($"点击了盒子 #{index + 1}: {status}");
				lblIdx.Click += (s, e) => AppendDebug($"点击了盒子 #{index + 1}: {status}");
				lblStatus.Click += (s, e) => AppendDebug($"点击了盒子 #{index + 1}: {status}");

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
				ColumnCount = 5,
				RowCount = 1
			};
			centerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
			centerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
			centerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
			centerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));
			centerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));

			var leftOrigPanel = CreateImagePanel("左侧原图", out picLeftOriginal);
			var rightOrigPanel = CreateImagePanel("右侧原图", out picRightOriginal);
			var leftResultPanel = CreateImagePanel("左侧结果", out picLeftResult);
			var rightResultPanel = CreateImagePanel("右侧结果", out picRightResult);
			var patchesPanel = CreateImagePanel("切片预览", out picPatchesPreview);

			centerLayout.Controls.Add(leftOrigPanel, 0, 0);
			centerLayout.Controls.Add(rightOrigPanel, 1, 0);
			centerLayout.Controls.Add(leftResultPanel, 2, 0);
			centerLayout.Controls.Add(rightResultPanel, 3, 0);
			centerLayout.Controls.Add(patchesPanel, 4, 0);

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
			var bottomLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
			bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
			bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
			bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));

			// 结果列表
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
			dgvResults.Columns.Add("Side", "侧");
			dgvResults.Columns.Add("DefectType", "缺陷类型");
			dgvResults.Columns.Add("X1", "X1");
			dgvResults.Columns.Add("Y1", "Y1");
			dgvResults.Columns.Add("X2", "X2");
			dgvResults.Columns.Add("Y2", "Y2");
			dgvResults.Columns.Add("Score", "置信度");
			dgvResults.Columns["Index"].Width = 30;
			dgvResults.Columns["Side"].Width = 35;
			dgvResults.Columns["DefectType"].Width = 65;
			dgvResults.Columns["Score"].Width = 55;

			var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(4), FlowDirection = FlowDirection.LeftToRight };
			btnExportCsv = CreateFlatButton("📊 导出CSV", TextSecondary, false, 26);
			btnExportCsv.Click += ExportCsv;
			btnSaveLeft = CreateFlatButton("🖼 保存左侧", PrimaryColor, false, 26);
			btnSaveLeft.Click += (s, e) => SaveImage(picLeftResult, "left");
			btnSaveRight = CreateFlatButton("🖼 保存右侧", SuccessColor, false, 26);
			btnSaveRight.Click += (s, e) => SaveImage(picRightResult, "right");
			btnSaveAll = CreateFlatButton("🖼 保存全部", WarningColor, false, 26);
			btnSaveAll.Click += SaveAllImages;
			btnPanel.Controls.AddRange(new Control[] { btnExportCsv, btnSaveLeft, btnSaveRight, btnSaveAll });

			resultsContent.Controls.Add(dgvResults, 0, 0);
			resultsContent.Controls.Add(btnPanel, 0, 1);
			resultsCard.Controls.Add(resultsContent);
			bottomLayout.Controls.Add(resultsCard, 0, 0);

			// 缺陷详情
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

			// 调试输出
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
		private TextBox CreateFlatTextBox(string text) => new TextBox { Text = text, BorderStyle = BorderStyle.FixedSingle, Font = new Font("Consolas", 8F), BackColor = Color.White, ForeColor = TextPrimary };
		private Button CreateFlatButton(string text, Color color, bool fill = false, int height = 26)
		{
			var btn = new Button { Text = text, Height = height, FlatStyle = FlatStyle.Flat, BackColor = fill ? color : Color.White, ForeColor = fill ? Color.White : color, Font = new Font("Microsoft YaHei UI", 8F), Cursor = Cursors.Hand, Margin = new Padding(2), UseVisualStyleBackColor = false };
			btn.FlatAppearance.BorderColor = color; btn.FlatAppearance.BorderSize = 1;
			btn.MouseEnter += (s, e) => btn.BackColor = fill ? ControlPaint.Light(color, 0.2f) : ControlPaint.Light(color, 0.9f);
			btn.MouseLeave += (s, e) => btn.BackColor = fill ? color : Color.White;
			return btn;
		}
		private void BrowseFile(TextBox tb, string filter) { using (var dlg = new OpenFileDialog { Filter = filter }) { if (dlg.ShowDialog() == DialogResult.OK) tb.Text = dlg.FileName; } }
		private void AppendDebug(string msg, bool err = false) { if (rtbDebugOutput.InvokeRequired) { rtbDebugOutput.Invoke(new Action(() => AppendDebug(msg, err))); return; } rtbDebugOutput.SelectionColor = err ? Color.FromArgb(255, 100, 100) : Color.FromArgb(180, 180, 180); rtbDebugOutput.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n"); rtbDebugOutput.ScrollToCaret(); }
		private void SetBusy(bool busy, string s = "") { if (this.InvokeRequired) { this.Invoke(new Action(() => SetBusy(busy, s))); return; } progressBar.Visible = busy; btnRunDetection.Enabled = !busy && _yoloModel != null && _leftImage != null; if (!string.IsNullOrEmpty(s)) lblStatus.Text = s; Cursor = busy ? Cursors.WaitCursor : Cursors.Default; }
		private void UpdateUIState(bool modelLoaded) { btnRunDetection.Enabled = modelLoaded && _leftImage != null && !_leftImage.Empty(); }
		private void DisplayMat(XLPictureBox pb, Mat mat) { if (mat == null || mat.Empty()) { pb.Image = null; return; } try { var bmp = mat.ToBitmap(); var old = pb.Image; pb.Image = bmp; old?.Dispose(); } catch { } }
		#endregion

		#region 模型加载
		private async Task LoadModel()
		{
			if (!File.Exists(txtModelPath.Text)) { MessageBox.Show("ONNX模型不存在！"); return; }
			if (!File.Exists(txtMetaJsonPath.Text)) { MessageBox.Show("Meta JSON不存在！"); return; }
			try
			{
				SetBusy(true, "加载模型中...");
				lblModelStatus.Text = "● 加载中..."; lblModelStatus.ForeColor = WarningColor;
				AppendDebug($"📦 加载模型: {Path.GetFileName(txtModelPath.Text)}");
				await Task.Run(() => { _yoloModel?.Dispose(); _yoloModel = new YoloOnnx(txtModelPath.Text, txtMetaJsonPath.Text, (int)numBatchSize.Value); });
				lblModelStatus.Text = "● 已就绪"; lblModelStatus.ForeColor = SuccessColor;
				UpdateUIState(true); AppendDebug($"✅ 模型加载成功");
			}
			catch (Exception ex) { lblModelStatus.Text = "● 失败"; lblModelStatus.ForeColor = DangerColor; AppendDebug($"❌ 加载失败: {ex.Message}\r\n{ex.StackTrace}", true); }
			finally { SetBusy(false); }
		}
		#endregion

		#region 图片加载
		private void BtnLoadImages_Click(object sender, EventArgs e)
		{
			try
			{
				_leftImage?.Dispose(); _rightImage?.Dispose();
				_leftImage = Cv2.ImRead(txtLeftImagePath.Text);
				_rightImage = Cv2.ImRead(txtRightImagePath.Text);
				if (_leftImage.Empty() || _rightImage.Empty()) { MessageBox.Show("图片加载失败！"); return; }

				DisplayMat(picLeftOriginal, _leftImage);
				DisplayMat(picRightOriginal, _rightImage);
				picLeftResult.Image = null;
				picRightResult.Image = null;

				// 显示切片预览
				ShowPatchesPreview();

				lblImageInfo.Text = $"左:{_leftImage.Width}x{_leftImage.Height} 右:{_rightImage.Width}x{_rightImage.Height}";
				UpdateUIState(_yoloModel != null);
				AppendDebug($"✅ 图片加载成功");
			}
			catch (Exception ex) { AppendDebug($"❌ 加载失败: {ex.Message}\r\n{ex.StackTrace}", true); }
		}

		private void ShowPatchesPreview()
		{
			if (_leftImage == null) return;
			int h = _leftImage.Height, w = _leftImage.Width;
			using (var preview = _leftImage.Clone())
			{
				// 绘制切片边界
				int[] xCuts = P_div_2_is_5() ? new[] { (int)(w * 0.4), (int)(w * 0.8) } : new[] { w / 3, w * 2 / 3 };
				int yCut1 = (int)(h * 0.55);
				int yCut2 = (int)(h * 0.45);

				foreach (int x in xCuts)
					Cv2.Line(preview, new OpenCvSharp.Point(x, 0), new OpenCvSharp.Point(x, h), new Scalar(0, 255, 255), 2);
				Cv2.Line(preview, new OpenCvSharp.Point(0, yCut1), new OpenCvSharp.Point(w, yCut1), new Scalar(255, 0, 255), 2);
				Cv2.Line(preview, new OpenCvSharp.Point(0, yCut2), new OpenCvSharp.Point(w, yCut2), new Scalar(255, 0, 255), 1, LineTypes.AntiAlias);

				var bmp = preview.ToBitmap();
				using (var g = Graphics.FromImage(bmp))
				{
					using (var font = new Font("Microsoft YaHei", 10, FontStyle.Bold))
					{
						int idx = 1;
						foreach (int x in xCuts)
						{
							g.DrawString($"切{idx++}", font, Brushes.Yellow, x + 5, 5);
							g.DrawString($"切{idx++}", font, Brushes.Yellow, x + 5, yCut2 + 5);
						}
					}
				}
				var resultMat = bmp.ToMat();
				DisplayMat(picPatchesPreview, resultMat);
				resultMat.Dispose(); bmp.Dispose();
			}
		}

		private bool P_div_2_is_5() => ((int)numBoxCount.Value / 2) == 5;
		#endregion

		#region 执行检测
		private async Task RunDetection()
		{
			if (_yoloModel == null) { MessageBox.Show("请先加载模型！"); return; }
			if (_leftImage == null || _rightImage == null) { MessageBox.Show("请先加载图片！"); return; }

			try
			{
				SetBusy(true, "检测中...");
				AppendDebug($"=== 🚀 正面损伤检测 ===");
				AppendDebug($"盒子数P: {numBoxCount.Value} (halfP={numBoxCount.Value / 2})");

				var sw = Stopwatch.StartNew();

				await Task.Run(() =>
				{
					var result = FrontDamageInspection.CheckFrontDamage(
						_leftImage, _rightImage, (int)numBoxCount.Value, _yoloModel);
					_statusList = result.StatusList;
					_finalLeftDict = result.FinalLeftDict;
					_finalRightDict = result.FinalRightDict;
				});

				sw.Stop();
				AppendDebug($"✅ 检测完成! 耗时: {sw.Elapsed.TotalMilliseconds:F0}ms");

				// 安全地统计缺陷数
				int defectCount = 0;
				if (_statusList != null)
				{
					defectCount = _statusList.Count(s => s != "OK");
					AppendDebug($"   状态列表长度: {_statusList.Count}");
					AppendDebug($"   缺陷数: {defectCount}");
				}

				// 安全地统计坐标数
				int leftCount = 0, rightCount = 0;
				if (_finalLeftDict != null)
				{
					foreach (var kvp in _finalLeftDict)
						if (kvp.Value != null) leftCount += kvp.Value.Count;
				}
				if (_finalRightDict != null)
				{
					foreach (var kvp in _finalRightDict)
						if (kvp.Value != null) rightCount += kvp.Value.Count;
				}
				AppendDebug($"   左侧缺陷坐标数: {leftCount}");
				AppendDebug($"   右侧缺陷坐标数: {rightCount}");

				UpdateStatusCards();
				UpdateResultsGrid();
				UpdateDefectDetails();
				DrawResults();

				lblStatus.Text = $"检测完成 ({sw.Elapsed.TotalSeconds:F1}s)";
			}
			catch (Exception ex)
			{
				AppendDebug($"❌ 检测失败: {ex.Message}\r\n{ex.StackTrace}", true);
				AppendDebug($"   堆栈: {ex.StackTrace}", true);
				MessageBox.Show($"检测失败:\n{ex.Message}\r\n{ex.StackTrace}\n\n请查看调试输出获取详细信息");
			}
			finally { SetBusy(false); }
		}
		#endregion

		#region 绘制结果
		private void DrawResults()
		{
			int halfP = (int)numBoxCount.Value / 2;

			// 左侧结果
			if (_leftImage != null)
			{
				using (var drawImg = _leftImage.Clone())
				{
					DrawDefectsOnImage(drawImg, _finalLeftDict, _statusList, 0, halfP);
					DisplayMat(picLeftResult, drawImg);
				}
			}
			// 右侧结果
			if (_rightImage != null)
			{
				using (var drawImg = _rightImage.Clone())
				{
					DrawDefectsOnImage(drawImg, _finalRightDict, _statusList, halfP, (int)numBoxCount.Value);
					DisplayMat(picRightResult, drawImg);
				}
			}
		}

		private void DrawDefectsOnImage(Mat image, Dictionary<string, List<float[]>> defectDict,
			List<string> statusList, int startIdx, int endIdx)
		{
			if (image == null || image.Empty()) return;
			int w = image.Width, h = image.Height;

			// 安全检查
			if (statusList == null) return;

			// 确保索引不超出范围
			int totalBoxes = endIdx - startIdx;
			if (totalBoxes <= 0) return;
			if (startIdx >= statusList.Count) return;
			if (endIdx > statusList.Count) endIdx = statusList.Count;

			var bitmap = image.ToBitmap();
			using (var g = Graphics.FromImage(bitmap))
			{
				g.SmoothingMode = SmoothingMode.AntiAlias;

				// 绘制区域分割线
				if (totalBoxes > 1)
				{
					using (var dashPen = new Pen(Color.FromArgb(100, 100, 100), 1) { DashStyle = DashStyle.Dash })
					{
						for (int i = 1; i < totalBoxes; i++)
						{
							int x = (int)((float)i / totalBoxes * w);
							g.DrawLine(dashPen, x, 0, x, h);
						}
					}
				}

				// 绘制顶部状态标签
				using (var font = new Font("Microsoft YaHei", 14, FontStyle.Bold))
				{
					int count = Math.Min(totalBoxes, statusList.Count - startIdx);
					for (int i = 0; i < count; i++)
					{
						int globalIdx = startIdx + i;
						if (globalIdx >= statusList.Count) break;

						string st = statusList[globalIdx];
						if (string.IsNullOrEmpty(st)) st = "未知";

						Color sc = StatusColors.ContainsKey(st) ? StatusColors[st] : Color.Gray;
						string txt = st.Length > 3 ? st.Substring(0, 3) : st;

						float px = (i + 0.5f) / totalBoxes * w;
						var ts = g.MeasureString(txt, font);

						// 阴影
						g.DrawString(txt, font, Brushes.Black, px - ts.Width / 2 + 1, 6);
						// 主体
						using (var brush = new SolidBrush(sc))
							g.DrawString(txt, font, brush, px - ts.Width / 2, 5);
					}
				}

				// 绘制缺陷框
				if (defectDict != null && defectDict.Count > 0)
				{
					int idx = 0;
					foreach (var kvp in defectDict)
					{
						string dtype = kvp.Key;
						Color dcolor = StatusColors.ContainsKey(dtype) ? StatusColors[dtype] : Color.Red;

						foreach (var box in kvp.Value)
						{
							if (box == null || box.Length < 4) continue;

							int x1 = (int)(box[0] * w);
							int y1 = (int)(box[1] * h);
							int x2 = (int)(box[2] * w);
							int y2 = (int)(box[3] * h);

							// 边界保护
							x1 = Math.Max(0, Math.Min(w - 1, x1));
							y1 = Math.Max(0, Math.Min(h - 1, y1));
							x2 = Math.Max(0, Math.Min(w - 1, x2));
							y2 = Math.Max(0, Math.Min(h - 1, y2));

							if (x2 <= x1 || y2 <= y1) continue;

							var rect = new Rect(x1, y1, x2 - x1, y2 - y1);

							// 半透明填充
							using (var fill = new SolidBrush(Color.FromArgb(50, dcolor)))
								g.FillRectangle(fill, rect);

							// 边框
							using (var pen = new Pen(dcolor, 3))
								g.DrawRectangle(pen, rect);

							// 标签
							using (var font = new Font("Microsoft YaHei", 10, FontStyle.Bold))
							{
								string lbl = $"{dtype} #{idx + 1}";
								var ts = g.MeasureString(lbl, font);
								int lx = x1;
								int ly = y1 - (int)ts.Height - 5;
								if (ly < 5) ly = y1 + 5;

								var bg = new Rect(lx - 2, ly - 2, (int)ts.Width + 8, (int)ts.Height + 6);
								using (var bgBrush = new SolidBrush(dcolor))
									g.FillRectangle(bgBrush, bg);
								g.DrawString(lbl, font, Brushes.White, lx + 2, ly + 1);
							}
							idx++;
						}
					}
				}

				// 图例（右下角）
				DrawLegend(g, w, h, defectDict);
			}

			var resultMat = bitmap.ToMat();
			resultMat.CopyTo(image);
			resultMat.Dispose();
			bitmap.Dispose();
		}

		/// <summary>
		/// 绘制图例（右下角）
		/// </summary>
		private void DrawLegend(Graphics g, int imgW, int imgH, Dictionary<string, List<float[]>> defectDict)
		{
			var items = new List<(string text, Color color)>
	{
		("正常 OK", Color.FromArgb(39, 174, 96)),
		("破损", Color.FromArgb(231, 76, 60)),
	};

			int itemH = 22, pad = 10;
			int legendW = 150, legendH = items.Count * itemH + pad * 2 + 25;

			int lx = imgW - legendW - 15, ly = imgH - legendH - 15;

			using (var bgBrush = new SolidBrush(Color.FromArgb(200, 40, 40, 40)))
				g.FillRectangle(bgBrush, lx, ly, legendW, legendH);
			using (var pen = new Pen(Color.Gray, 1))
				g.DrawRectangle(pen, lx, ly, legendW, legendH);

			using (var titleFont = new Font("Microsoft YaHei", 11, FontStyle.Bold))
			using (var itemFont = new Font("Microsoft YaHei", 9))
			{
				var titleSize = g.MeasureString("图例", titleFont);
				g.DrawString("图例", titleFont, Brushes.White, lx + (legendW - titleSize.Width) / 2, ly + pad);

				for (int i = 0; i < items.Count; i++)
				{
					int iy = ly + pad + (int)titleSize.Height + 5 + i * itemH;
					using (var brush = new SolidBrush(items[i].color))
						g.FillRectangle(brush, lx + 12, iy + 2, 18, 14);
					using (var borderPen = new Pen(Color.White, 1))
						g.DrawRectangle(borderPen, lx + 12, iy + 2, 18, 14);
					g.DrawString(items[i].text, itemFont, Brushes.White, lx + 38, iy);
				}
			}
		}
		#endregion

		#region 更新结果
		private void UpdateResultsGrid()
		{
			dgvResults.Rows.Clear();
			if (_finalLeftDict != null)
				AddDictToGrid(_finalLeftDict, "左");
			if (_finalRightDict != null)
				AddDictToGrid(_finalRightDict, "右");
		}

		private void AddDictToGrid(Dictionary<string, List<float[]>> dict, string side)
		{
			int idx = 0;
			foreach (var kvp in dict)
			{
				foreach (var box in kvp.Value)
				{
					float score = box.Length > 4 ? box[4] : 0;
					dgvResults.Rows.Add(++idx, side, kvp.Key,
						box[0].ToString("F4"), box[1].ToString("F4"),
						box[2].ToString("F4"), box[3].ToString("F4"),
						score.ToString("F3"));
				}
			}
		}

		private void UpdateDefectDetails()
		{
			rtbDefectDetails.Clear();
			if (_statusList == null) return;
			rtbDefectDetails.AppendText("=== 正面损伤检测结果 ===\n\n");
			rtbDefectDetails.AppendText($"盒子总数: {_statusList.Count}\n\n");
			for (int i = 0; i < _statusList.Count; i++)
				rtbDefectDetails.AppendText($"盒子 #{i + 1}: {_statusList[i]}\n");
			rtbDefectDetails.AppendText("\n--- 左侧缺陷坐标 ---\n");
			AppendDictDetails(_finalLeftDict);
			rtbDefectDetails.AppendText("\n--- 右侧缺陷坐标 ---\n");
			AppendDictDetails(_finalRightDict);
		}

		private void AppendDictDetails(Dictionary<string, List<float[]>> dict)
		{
			if (dict == null || dict.Count == 0)
			{
				rtbDefectDetails.AppendText("  无缺陷\n");
				return;
			}

			foreach (var kvp in dict)
			{
				rtbDefectDetails.AppendText($"  [{kvp.Key}] x{kvp.Value.Count}\n");

				if (kvp.Value == null) continue;

				for (int i = 0; i < kvp.Value.Count; i++)
				{
					var b = kvp.Value[i];
					if (b == null || b.Length < 4) continue;

					float score = b.Length > 4 ? b[4] : 0;
					rtbDefectDetails.AppendText(
						$"    #{i + 1}: [{b[0]:F4},{b[1]:F4},{b[2]:F4},{b[3]:F4}] score={score:F3}\n");
				}
			}
		}
		#endregion

		#region 导出
		private void ExportCsv(object sender, EventArgs e)
		{
			using (var dlg = new SaveFileDialog { Filter = "CSV|*.csv", FileName = $"front_damage_{DateTime.Now:yyyyMMdd_HHmmss}.csv" })
			{
				if (dlg.ShowDialog() == DialogResult.OK)
				{
					using (var w = new StreamWriter(dlg.FileName, false, System.Text.Encoding.UTF8))
					{
						w.WriteLine("序号,侧,缺陷类型,X1,Y1,X2,Y2,置信度");
						foreach (DataGridViewRow row in dgvResults.Rows)
							w.WriteLine($"{row.Cells[0].Value},{row.Cells[1].Value},{row.Cells[2].Value},{row.Cells[3].Value},{row.Cells[4].Value},{row.Cells[5].Value},{row.Cells[6].Value},{row.Cells[7].Value}");
					}
				}
			}
		}
		private void SaveImage(XLPictureBox pb, string prefix) { if (pb.Image == null) return; using (var dlg = new SaveFileDialog { Filter = "PNG|*.png", FileName = $"{prefix}_result_{DateTime.Now:HHmmss}.png" }) { if (dlg.ShowDialog() == DialogResult.OK) pb.Image.Save(dlg.FileName); } }
		private void SaveAllImages(object sender, EventArgs e) { SaveImage(picLeftResult, "left"); SaveImage(picRightResult, "right"); }
		#endregion

		protected override void OnFormClosing(FormClosingEventArgs e)
		{
			_yoloModel?.Dispose();
			_leftImage?.Dispose();
			_rightImage?.Dispose();
			base.OnFormClosing(e);
		}
	}

	
}