using OpenCvSharp;
using OpenCvSharp.Extensions;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using YoloInference;
using XL.Controls;
//using 侧面缺陷;
using Point = System.Drawing.Point;
using PointF = System.Drawing.PointF;
using Rect = System.Drawing.Rectangle;
// 命名空间别名
using Size = System.Drawing.Size;

namespace YoloMigration
{
	public partial class SideDefectForm : Form
	{
		// ========== 核心组件 ==========
		private YoloOnnx _yoloModel;
		private Mat _originalImage;
		private Tuple<List<string>, List<float[]>> _currentResult;

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

		// 模型配置
		private TextBox txtModelPath;
		private TextBox txtMetaJsonPath;
		private Label lblModelStatus;
		private Button btnBrowseModel;
		private Button btnBrowseMeta;
		private Button btnLoadModel;

		// 图片配置
		private TextBox txtImagePath;
		private Button btnBrowseImage;
		private Button btnLoadImage;

		// 参数配置
		private NumericUpDown numCropRatio;
		private NumericUpDown numConfThreshold;
		private NumericUpDown numBatchSize;
		private Label lblConfValue;
		private TrackBar trackConfThreshold;
		private Button btnRunDetection;

		// 图像显示（4个区域）
		private XLPictureBox picOriginal;
		private XLPictureBox picHeadCrop;
		private XLPictureBox picTailCrop;
		private XLPictureBox picResult;

		// 结果列表
		private DataGridView dgvResults;
		private RichTextBox rtbDefectDetails;
		private RichTextBox rtbDebugOutput;

		// 导出按钮
		private Button btnExportCsv;
		private Button btnSaveResult;
		private Button btnSaveHeadCrop;
		private Button btnSaveTailCrop;

		// 状态
		private Label lblStatus;
		private ProgressBar progressBar;

		public SideDefectForm()
		{
			//InitializeComponent();
			InitializeCustomComponents();
		}

		private void InitializeCustomComponents()
		{
			this.Text = "侧面缺陷检测调试工具 v1.0";
			this.Size = new Size(1650, 1000);
			this.StartPosition = FormStartPosition.CenterScreen;
			this.MinimumSize = new Size(1400, 850);
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
			mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 150F));  // 顶部配置
			mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 55F));    // 图像显示
			mainLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 150F));  // 结果表格
			mainLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 45F));    // 详情+调试
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
			topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));  // 模型配置（宽一点）
			topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 26F));  // 图片配置
			topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 24F));  // 检测参数
			topLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 20F));  // 操作按钮

			// ================================================================
			// 卡片1：模型配置（ONNX + Meta JSON + 加载按钮）
			// ================================================================
			var modelCard = CreateFlatCard("模型配置");
			var modelContent = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 1,
				RowCount = 3,
				Padding = new Padding(10)
			};
			modelContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));   // ONNX路径
			modelContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));   // Meta JSON路径
			modelContent.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));   // 加载按钮

			// ------ 第1行：ONNX模型路径 ------
			var onnxPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
			var lblOnnx = new Label
			{
				Text = "ONNX:",
				AutoSize = true,
				Width = 45,
				TextAlign = ContentAlignment.MiddleRight,
				ForeColor = TextSecondary,
				Font = new Font("Microsoft YaHei UI", 8F)
			};
			txtModelPath = CreateFlatTextBox(@"E:\koch纸盒\模型\侧面缺陷\best.onnx");
			txtModelPath.Width = 200;
			btnBrowseModel = CreateFlatButton("📁", PrimaryColor, false, 28);
			btnBrowseModel.Width = 30;
			btnBrowseModel.Click += (s, e) => BrowseFile(txtModelPath, "ONNX模型文件|*.onnx|所有文件|*.*");
			onnxPanel.Controls.AddRange(new Control[] { lblOnnx, txtModelPath, btnBrowseModel });

			// ------ 第2行：Meta JSON路径 ------
			var metaPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
			var lblMeta = new Label
			{
				Text = "Meta:",
				AutoSize = true,
				Width = 45,
				TextAlign = ContentAlignment.MiddleRight,
				ForeColor = TextSecondary,
				Font = new Font("Microsoft YaHei UI", 8F)
			};
			txtMetaJsonPath = CreateFlatTextBox(@"E:\koch纸盒\模型\侧面缺陷\meta.json");
			txtMetaJsonPath.Width = 200;
			btnBrowseMeta = CreateFlatButton("📁", PrimaryColor, false, 28);
			btnBrowseMeta.Width = 30;
			btnBrowseMeta.Click += (s, e) => BrowseFile(txtMetaJsonPath, "JSON文件|*.json|所有文件|*.*");
			metaPanel.Controls.AddRange(new Control[] { lblMeta, txtMetaJsonPath, btnBrowseMeta });

			// ------ 第3行：加载按钮 + 状态 ------
			var loadPanel = new FlowLayoutPanel
			{
				Dock = DockStyle.Fill,
				FlowDirection = FlowDirection.RightToLeft,
				Padding = new Padding(0, 5, 0, 0)
			};

			lblModelStatus = new Label
			{
				Text = "● 未加载",
				AutoSize = true,
				ForeColor = TextSecondary,
				Font = new Font("Microsoft YaHei UI", 8F),
				TextAlign = ContentAlignment.MiddleRight,
				Padding = new Padding(5, 5, 5, 0)
			};

			btnLoadModel = CreateFlatButton("加载模型", SuccessColor, true, 36);
			btnLoadModel.Width = 100;
			btnLoadModel.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
			btnLoadModel.Click += async (s, e) => await LoadModel();

			loadPanel.Controls.Add(lblModelStatus);
			loadPanel.Controls.Add(btnLoadModel);

			modelContent.Controls.Add(onnxPanel, 0, 0);
			modelContent.Controls.Add(metaPanel, 0, 1);
			modelContent.Controls.Add(loadPanel, 0, 2);
			modelCard.Controls.Add(modelContent);
			topLayout.Controls.Add(modelCard, 0, 0);

			// ================================================================
			// 卡片2：图片配置
			// ================================================================
			var imageCard = CreateFlatCard("图片配置");
			var imageContent = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 1,
				RowCount = 2,
				Padding = new Padding(10)
			};
			imageContent.RowStyles.Add(new RowStyle(SizeType.Absolute, 42F));   // 图片路径
			imageContent.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));   // 加载按钮

			var imgPathPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, WrapContents = false };
			var lblImg = new Label
			{
				Text = "图片:",
				AutoSize = true,
				Width = 40,
				TextAlign = ContentAlignment.MiddleRight,
				ForeColor = TextSecondary,
				Font = new Font("Microsoft YaHei UI", 8F)
			};
			txtImagePath = CreateFlatTextBox(@"E:\koch纸盒\测试图\side_image.jpeg");
			txtImagePath.Width = 210;
			btnBrowseImage = CreateFlatButton("📁", PrimaryColor, false, 28);
			btnBrowseImage.Width = 30;
			btnBrowseImage.Click += (s, e) => BrowseFile(txtImagePath, "图片文件|*.jpg;*.jpeg;*.png;*.bmp|所有文件|*.*");
			imgPathPanel.Controls.AddRange(new Control[] { lblImg, txtImagePath, btnBrowseImage });

			var imgBtnPanel = new FlowLayoutPanel
			{
				Dock = DockStyle.Fill,
				FlowDirection = FlowDirection.RightToLeft,
				Padding = new Padding(0, 5, 0, 0)
			};
			btnLoadImage = CreateFlatButton("加载图片", PrimaryColor, true, 36);
			btnLoadImage.Width = 100;
			btnLoadImage.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
			btnLoadImage.Click += BtnLoadImage_Click;
			imgBtnPanel.Controls.Add(btnLoadImage);

			imageContent.Controls.Add(imgPathPanel, 0, 0);
			imageContent.Controls.Add(imgBtnPanel, 0, 1);
			imageCard.Controls.Add(imageContent);
			topLayout.Controls.Add(imageCard, 1, 0);

			// ================================================================
			// 卡片3：检测参数
			// ================================================================
			var paramsCard = CreateFlatCard("检测参数");
			var paramsContent = new FlowLayoutPanel
			{
				Dock = DockStyle.Fill,
				Padding = new Padding(10),
				WrapContents = true
			};

			// 裁剪比例
			var cropPanel = new Panel { Width = 200, Height = 28, Margin = new Padding(0, 0, 0, 4) };
			var lblCrop = new Label { Text = "裁剪比例(宽/高):", Location = new Point(0, 4), AutoSize = true, ForeColor = TextSecondary, Font = new Font("Microsoft YaHei UI", 8F) };
			numCropRatio = new NumericUpDown { Minimum = 0.5M, Maximum = 5.0M, Value = 2.0M, Width = 55, Location = new Point(115, 0), DecimalPlaces = 1, Increment = 0.1M };
			cropPanel.Controls.AddRange(new Control[] { lblCrop, numCropRatio });
			paramsContent.Controls.Add(cropPanel);

			// 置信度阈值
			var confPanel = new Panel { Width = 280, Height = 30, Margin = new Padding(0, 0, 0, 4) };
			var confLabel = new Label { Text = "置信度阈值:", Location = new Point(0, 5), AutoSize = true, ForeColor = TextSecondary, Font = new Font("Microsoft YaHei UI", 8F) };
			trackConfThreshold = new TrackBar { Minimum = 1, Maximum = 100, Value = 50, Width = 140, Location = new Point(80, 0), TickStyle = TickStyle.None };
			trackConfThreshold.Scroll += (s, ev) => lblConfValue.Text = $"{trackConfThreshold.Value / 100f:F2}";
			lblConfValue = new Label { Text = "0.50", Location = new Point(225, 5), AutoSize = true, Font = new Font("Consolas", 9F, FontStyle.Bold), ForeColor = PrimaryColor };
			confPanel.Controls.AddRange(new Control[] { confLabel, trackConfThreshold, lblConfValue });
			paramsContent.Controls.Add(confPanel);

			// 批次大小
			var batchPanel = new Panel { Width = 200, Height = 28, Margin = new Padding(0, 0, 0, 4) };
			var lblBatch = new Label { Text = "批量大小:", Location = new Point(0, 4), AutoSize = true, ForeColor = TextSecondary, Font = new Font("Microsoft YaHei UI", 8F) };
			numBatchSize = new NumericUpDown { Minimum = 1, Maximum = 8, Value = 2, Width = 55, Location = new Point(80, 0) };
			batchPanel.Controls.AddRange(new Control[] { lblBatch, numBatchSize });
			paramsContent.Controls.Add(batchPanel);

			paramsCard.Controls.Add(paramsContent);
			topLayout.Controls.Add(paramsCard, 2, 0);

			// ================================================================
			// 卡片4：操作按钮
			// ================================================================
			var actionCard = CreateFlatCard("操作");
			var actionContent = new FlowLayoutPanel
			{
				Dock = DockStyle.Fill,
				Padding = new Padding(10),
				FlowDirection = FlowDirection.TopDown
			};

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
		#region 中间面板 - 图像显示
		private void CreateCenterPanel()
		{
			var centerPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent, Padding = new Padding(0, 8, 0, 8) };
			var centerLayout = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 4,
				RowCount = 1
			};
			centerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
			centerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
			centerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));
			centerLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25F));

			var origPanel = CreateImagePanel("原始图像", out picOriginal);
			var headPanel = CreateImagePanel("头部裁剪 (ROI)", out picHeadCrop);
			var tailPanel = CreateImagePanel("尾部裁剪 (ROI)", out picTailCrop);
			var resultPanel = CreateImagePanel("检测结果 (含标注)", out picResult);

			centerLayout.Controls.Add(origPanel, 0, 0);
			centerLayout.Controls.Add(headPanel, 1, 0);
			centerLayout.Controls.Add(tailPanel, 2, 0);
			centerLayout.Controls.Add(resultPanel, 3, 0);

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
			var bottomPanel = new Panel { Dock = DockStyle.Fill, BackColor = Color.Transparent };
			var bottomLayout = new TableLayoutPanel
			{
				Dock = DockStyle.Fill,
				ColumnCount = 3,
				RowCount = 1
			};
			bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
			bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 35F));
			bottomLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 30F));

			// 结果列表
			var resultsCard = CreateFlatCard("检测结果");
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
			dgvResults.Columns.Add("DefectType", "缺陷类型");
			dgvResults.Columns.Add("CropSide", "裁剪侧");
			dgvResults.Columns.Add("NormBox_X1", "X1(归一化)");
			dgvResults.Columns.Add("NormBox_Y1", "Y1(归一化)");
			dgvResults.Columns.Add("NormBox_X2", "X2(归一化)");
			dgvResults.Columns.Add("NormBox_Y2", "Y2(归一化)");
			dgvResults.Columns["Index"].Width = 40;
			dgvResults.Columns["DefectType"].Width = 80;
			dgvResults.Columns["CropSide"].Width = 70;

			// 导出按钮
			var btnPanel = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(4), FlowDirection = FlowDirection.LeftToRight };
			btnExportCsv = CreateFlatButton("📊 导出CSV", TextSecondary, false, 28);
			btnExportCsv.Click += ExportCsv;
			btnSaveResult = CreateFlatButton("🖼 结果图", PrimaryColor, false, 28);
			btnSaveResult.Click += SaveResultImage;
			btnSaveHeadCrop = CreateFlatButton("头部裁剪", SuccessColor, false, 28);
			btnSaveHeadCrop.Click += (s, e) => SavePictureBox(picHeadCrop, "head_crop");
			btnSaveTailCrop = CreateFlatButton("尾部裁剪", WarningColor, false, 28);
			btnSaveTailCrop.Click += (s, e) => SavePictureBox(picTailCrop, "tail_crop");
			btnPanel.Controls.AddRange(new Control[] { btnExportCsv, btnSaveResult, btnSaveHeadCrop, btnSaveTailCrop });

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
			mainLayout.Controls.Add(bottomPanel, 0, 2);
		}
		#endregion

		#region 状态栏
		private void CreateStatusBar()
		{
			var statusBar = new Panel { Dock = DockStyle.Bottom, Height = 26, BackColor = Color.FromArgb(250, 251, 252) };
			lblStatus = new Label { Text = "就绪 - 请加载模型和图片", Location = new Point(12, 4), AutoSize = true, Font = new Font("Microsoft YaHei UI", 8F), ForeColor = TextSecondary };
			progressBar = new ProgressBar { Style = ProgressBarStyle.Marquee, Width = 120, Height = 16, Location = new Point(this.Width - 140, 4), Visible = false, Anchor = AnchorStyles.Right | AnchorStyles.Top };
			statusBar.Controls.Add(lblStatus);
			statusBar.Controls.Add(progressBar);
			this.Controls.Add(statusBar);
		}
		#endregion

		#region UI 辅助方法
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
			{ if (dialog.ShowDialog() == DialogResult.OK) targetBox.Text = dialog.FileName; }
		}

		private void AppendDebug(string msg, bool isError = false)
		{
			if (rtbDebugOutput.InvokeRequired)
			{ rtbDebugOutput.Invoke(new Action(() => AppendDebug(msg, isError))); return; }
			rtbDebugOutput.SelectionColor = isError ? Color.FromArgb(255, 100, 100) : Color.FromArgb(180, 180, 180);
			rtbDebugOutput.AppendText($"[{DateTime.Now:HH:mm:ss}] {msg}\n");
			rtbDebugOutput.ScrollToCaret();
		}

		private void SetBusyState(bool busy, string status = "")
		{
			if (this.InvokeRequired) { this.Invoke(new Action(() => SetBusyState(busy, status))); return; }
			progressBar.Visible = busy;
			btnRunDetection.Enabled = !busy && _yoloModel != null && _originalImage != null;
			if (!string.IsNullOrEmpty(status)) lblStatus.Text = status;
			Cursor = busy ? Cursors.WaitCursor : Cursors.Default;
		}

		private void UpdateUIState(bool modelLoaded)
		{
			btnRunDetection.Enabled = modelLoaded && _originalImage != null && !_originalImage.Empty();
		}

		private void DisplayMat(XLPictureBox picBox, Mat mat)
		{
			if (mat == null || mat.Empty()) { picBox.Image = null; return; }
			try { var bmp = mat.ToBitmap(); var old = picBox.Image; picBox.Image = bmp; old?.Dispose(); } catch { }
		}
		#endregion

		#region 模型加载
		private async Task LoadModel()
		{
			if (!File.Exists(txtModelPath.Text))
			{
				MessageBox.Show("ONNX 模型文件不存在！");
				return;
			}
			if (!File.Exists(txtMetaJsonPath.Text))
			{
				MessageBox.Show("Meta JSON 文件不存在！");
				return;
			}

			try
			{
				SetBusyState(true, "加载模型中...");
				lblModelStatus.Text = "● 加载中...";
				lblModelStatus.ForeColor = WarningColor;

				int batchSize = (int)numBatchSize.Value;

				AppendDebug($"📦 加载模型: {Path.GetFileName(txtModelPath.Text)}");
				AppendDebug($"   Meta: {Path.GetFileName(txtMetaJsonPath.Text)}");
				AppendDebug($"   BatchSize: {batchSize}");

				await Task.Run(() =>
				{
					_yoloModel?.Dispose();
					// 根据你的 YoloOnnx 构造函数调整
					_yoloModel = new YoloOnnx(
						txtModelPath.Text,
						txtMetaJsonPath.Text,
						batchSize
					);
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
				MessageBox.Show($"模型加载失败:\n{ex.Message}\r\n{ex.StackTrace}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
			finally { SetBusyState(false); }
		}
		#endregion

		#region 图片加载
		private void BtnLoadImage_Click(object sender, EventArgs e)
		{
			try
			{
				_originalImage?.Dispose();
				_originalImage = Cv2.ImRead(txtImagePath.Text);

				if (_originalImage == null || _originalImage.Empty())
				{ MessageBox.Show("图片加载失败！"); return; }

				DisplayMat(picOriginal, _originalImage);

				// 显示裁剪区域预览
				float cropRatio = (float)numCropRatio.Value;
				int height = _originalImage.Height;
				int width = _originalImage.Width;
				int cropWidth = Math.Min((int)(height * cropRatio), width);

				using (var headCrop = new Mat(_originalImage, new OpenCvSharp.Rect(0, 0, cropWidth, height)))
					DisplayMat(picHeadCrop, headCrop);

				using (var tailCrop = new Mat(_originalImage, new OpenCvSharp.Rect(width - cropWidth, 0, cropWidth, height)))
					DisplayMat(picTailCrop, tailCrop);

				// 清空结果
				picResult.Image = null;

				AppendDebug($"✅ 图片加载: {Path.GetFileName(txtImagePath.Text)} ({width}x{height})");
				AppendDebug($"   裁剪宽度: {cropWidth}px (比例={cropRatio})");
				UpdateUIState(_yoloModel != null);
			}
			catch (Exception ex) { AppendDebug($"❌ 加载失败: {ex.Message}\r\n{ex.StackTrace}", true); }
		}
		#endregion

		#region 执行检测
		private async Task RunDetection()
		{
			if (_yoloModel == null) { MessageBox.Show("请先加载模型！"); return; }
			if (_originalImage == null) { MessageBox.Show("请先加载图片！"); return; }

			try
			{
				SetBusyState(true, "检测中...");
				float cropRatio = (float)numCropRatio.Value;
				AppendDebug($"=== 🚀 侧面缺陷检测 ===");
				AppendDebug($"裁剪比例: {cropRatio} | 图像: {_originalImage.Width}x{_originalImage.Height}");

				var sw = Stopwatch.StartNew();

				await Task.Run(() =>
				{
					_currentResult = SideDefectProcessor.DetectSideDefects(_originalImage, cropRatio, _yoloModel);
				});

				sw.Stop();

				var defects = _currentResult.Item1;
				var boxes = _currentResult.Item2;

				AppendDebug($"✅ 检测完成! 耗时: {sw.Elapsed.TotalMilliseconds:F1}ms");
				AppendDebug($"   缺陷数: {defects.Count}");

				// 构造控制台格式输出
				var defectStrings = defects.Select(d => $"'{d}'").ToList();
				var boxStrings = boxes.Select(b => $"[{b[0]:F6}, {b[1]:F6}, {b[2]:F6}, {b[3]:F6}]").ToList();
				AppendDebug($"side_defects_list：[{string.Join(", ", defectStrings)}]");
				AppendDebug($"side_boxes_list：[{string.Join(", ", boxStrings)}]");

				UpdateResultsGrid(defects, boxes);
				UpdateDefectDetails(defects, boxes);
				DrawResultImage(defects, boxes);

				lblStatus.Text = $"检测完成 ({sw.Elapsed.TotalMilliseconds:F0}ms) - {defects.Count}个缺陷";
			}
			catch (Exception ex)
			{
				AppendDebug($"❌ 检测失败: {ex.Message}\r\n{ex.StackTrace}", true);
				MessageBox.Show($"检测失败:\n{ex.Message}\r\n{ex.StackTrace}");
			}
			finally { SetBusyState(false); }
		}
		#endregion

		#region 绘制结果
		private void DrawResultImage(List<string> defects, List<float[]> boxes)
		{
			if (_originalImage == null) return;

			using (var drawImg = _originalImage.Clone())
			{
				int height = drawImg.Height;
				int width = drawImg.Width;
				float cropRatio = (float)numCropRatio.Value;
				int cropWidth = Math.Min((int)(height * cropRatio), width);

				// 绘制裁剪区域标记
				// 头部裁剪区（左侧半透明蓝色）
				using (var overlay = new Mat(drawImg.Size(), MatType.CV_8UC3, new Scalar(0, 0, 0)))
				{
					Cv2.Rectangle(overlay, new OpenCvSharp.Rect(0, 0, cropWidth, height), new Scalar(255, 100, 0), -1);
					Cv2.AddWeighted(drawImg, 0.85, overlay, 0.15, 0, drawImg);
				}
				// 尾部裁剪区（右侧半透明蓝色）
				using (var overlay = new Mat(drawImg.Size(), MatType.CV_8UC3, new Scalar(0, 0, 0)))
				{
					Cv2.Rectangle(overlay, new OpenCvSharp.Rect(width - cropWidth, 0, cropWidth, height), new Scalar(255, 100, 0), -1);
					Cv2.AddWeighted(drawImg, 0.85, overlay, 0.15, 0, drawImg);
				}

				// 绘制裁剪区边界虚线
				Cv2.Line(drawImg, new OpenCvSharp.Point(cropWidth, 0), new OpenCvSharp.Point(cropWidth, height), new Scalar(255, 165, 0), 2);
				Cv2.Line(drawImg, new OpenCvSharp.Point(width - cropWidth, 0), new OpenCvSharp.Point(width - cropWidth, height), new Scalar(255, 165, 0), 2);

				// 转 Bitmap 用 GDI+ 绘制中文
				var bitmap = drawImg.ToBitmap();
				using (var g = Graphics.FromImage(bitmap))
				{
					g.SmoothingMode = SmoothingMode.AntiAlias;

					// 绘制缺陷框
					for (int i = 0; i < boxes.Count; i++)
					{
						var box = boxes[i];
						int x1 = (int)(box[0] * width);
						int y1 = (int)(box[1] * height);
						int x2 = (int)(box[2] * width);
						int y2 = (int)(box[3] * height);

						var rect = new Rect(x1, y1, x2 - x1, y2 - y1);

						// 红色半透明填充
						using (var fillBrush = new SolidBrush(Color.FromArgb(60, 255, 0, 0)))
							g.FillRectangle(fillBrush, rect);

						// 红色边框
						using (var pen = new Pen(Color.Red, 3))
							g.DrawRectangle(pen, rect);

						// 标签
						using (var font = new Font("Microsoft YaHei", 11, FontStyle.Bold))
						{
							var label = $"缺陷 #{i + 1}";
							var textSize = g.MeasureString(label, font);
							int labelX = x1;
							int labelY = y1 - (int)textSize.Height - 5;
							if (labelY < 5) labelY = y1 + 5;

							var bgRect = new Rect(labelX - 2, labelY - 2, (int)textSize.Width + 8, (int)textSize.Height + 6);
							using (var bgBrush = new SolidBrush(Color.Red))
								g.FillRectangle(bgBrush, bgRect);
							using (var textBrush = new SolidBrush(Color.White))
								g.DrawString(label, font, textBrush, labelX + 2, labelY + 1);
						}
					}

					// 图例（右下角）
					DrawLegend(g, width, height);
				}

				var resultMat = bitmap.ToMat();
				DisplayMat(picResult, resultMat);
				resultMat.Dispose();
				bitmap.Dispose();
			}
		}

		private void DrawLegend(Graphics g, int imgW, int imgH)
		{
			// 使用 System.Drawing.Color 而不是 OpenCvSharp 的
			var items = new (string text, System.Drawing.Color color)[]
			{
		("裁剪区域", System.Drawing.Color.Orange),
		("缺陷框", System.Drawing.Color.Red),
		("正常区域", System.Drawing.Color.LimeGreen)
			};

			int itemH = 22, pad = 10;
			int legendW = 150, legendH = items.Length * itemH + pad * 2 + 25;

			int lx = imgW - legendW - 15, ly = imgH - legendH - 15;

			using (var bgBrush = new SolidBrush(System.Drawing.Color.FromArgb(200, 40, 40, 40)))
				g.FillRectangle(bgBrush, lx, ly, legendW, legendH);
			using (var pen = new Pen(System.Drawing.Color.Gray, 1))
				g.DrawRectangle(pen, lx, ly, legendW, legendH);

			using (var titleFont = new Font("Microsoft YaHei", 11, FontStyle.Bold))
			using (var itemFont = new Font("Microsoft YaHei", 9, FontStyle.Regular))
			{
				g.DrawString("图例", titleFont, System.Drawing.Brushes.White, lx + (legendW - 40) / 2, ly + pad);

				for (int i = 0; i < items.Length; i++)
				{
					int iy = ly + pad + 22 + i * itemH;
					using (var brush = new SolidBrush(items[i].color))
						g.FillRectangle(brush, lx + 12, iy + 2, 18, 14);
					g.DrawString(items[i].text, itemFont, System.Drawing.Brushes.White, lx + 38, iy);
				}
			}
		}
		#endregion

		#region 更新结果
		private void UpdateResultsGrid(List<string> defects, List<float[]> boxes)
		{
			dgvResults.Rows.Clear();
			for (int i = 0; i < boxes.Count; i++)
			{
				var box = boxes[i];
				string cropSide = box[0] < 0.5f ? "头部" : "尾部";
				dgvResults.Rows.Add(i + 1, defects[i], cropSide,
					box[0].ToString("F6"), box[1].ToString("F6"),
					box[2].ToString("F6"), box[3].ToString("F6"));
			}
		}

		private void UpdateDefectDetails(List<string> defects, List<float[]> boxes)
		{
			rtbDefectDetails.Clear();
			rtbDefectDetails.AppendText("=== 侧面缺陷坐标详情 ===\n\n");
			rtbDefectDetails.AppendText($"缺陷总数: {defects.Count}\n");
			rtbDefectDetails.AppendText($"输出格式: [x1, y1, x2, y2] (归一化 0~1)\n\n");

			for (int i = 0; i < boxes.Count; i++)
			{
				var box = boxes[i];
				rtbDefectDetails.AppendText($"缺陷 #{i + 1}:\n");
				rtbDefectDetails.AppendText($"  类型: {defects[i]}\n");
				rtbDefectDetails.AppendText($"  坐标: [{box[0]:F6}, {box[1]:F6}, {box[2]:F6}, {box[3]:F6}]\n\n");
			}
		}
		#endregion

		#region 导出
		private void ExportCsv(object sender, EventArgs e)
		{
			if (_currentResult == null) return;
			using (var dialog = new SaveFileDialog { Filter = "CSV|*.csv", FileName = $"side_defects_{DateTime.Now:yyyyMMdd_HHmmss}.csv" })
			{
				if (dialog.ShowDialog() == DialogResult.OK)
				{
					using (var writer = new StreamWriter(dialog.FileName, false, System.Text.Encoding.UTF8))
					{
						writer.WriteLine("序号,缺陷类型,裁剪侧,X1,Y1,X2,Y2");
						foreach (DataGridViewRow row in dgvResults.Rows)
							writer.WriteLine($"{row.Cells[0].Value},{row.Cells[1].Value},{row.Cells[2].Value},{row.Cells[3].Value},{row.Cells[4].Value},{row.Cells[5].Value},{row.Cells[6].Value}");
					}
					AppendDebug($"✅ CSV已导出: {dialog.FileName}");
				}
			}
		}

		private void SaveResultImage(object sender, EventArgs e) => SavePictureBox(picResult, "side_result");
		private void SavePictureBox(XLPictureBox picBox, string prefix)
		{
			if (picBox.Image == null) return;
			using (var dialog = new SaveFileDialog { Filter = "PNG|*.png|JPEG|*.jpg", FileName = $"{prefix}_{DateTime.Now:yyyyMMdd_HHmmss}.png" })
			{ if (dialog.ShowDialog() == DialogResult.OK) picBox.Image.Save(dialog.FileName); }
		}
		#endregion

		protected override void OnFormClosing(FormClosingEventArgs e)
		{
			_yoloModel?.Dispose();
			_originalImage?.Dispose();
			base.OnFormClosing(e);
		}
	}

	
}