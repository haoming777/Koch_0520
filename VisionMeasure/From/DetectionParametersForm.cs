using System;
using System.Drawing;
using System.Windows.Forms;
using Config;
using VisionMeasure.Utils;using CommonLib;

namespace VisionMeasure.From
{
	public partial class DetectionParametersForm : Form
	{
		private DetectionParameters _params;
		private TabControl _tabControl;
		private Button _btnSave;
		private Button _btnCancel;
		private Button _btnReset;
		private Button _btnExport;
		private Button _btnImport;

		// 正面参数控件
		private TrackBar _trackFrontConf;
		private Label _lblFrontConfValue;
		private TrackBar _trackFrontIou;
		private Label _lblFrontIouValue;
		private TrackBar _trackFrontPCodeConf;
		private Label _lblFrontPCodeValue;

		// 端面参数控件
		private TrackBar _trackUpperConf;
		private Label _lblUpperConfValue;
		private TrackBar _trackUpperIou;
		private Label _lblUpperIouValue;
		private TrackBar _trackLowerConf;
		private Label _lblLowerConfValue;
		private TrackBar _trackLowerIou;
		private Label _lblLowerIouValue;
		private NumericUpDown _numEndFaceExposure;

		// 背面参数控件
		private TrackBar _trackBackConf;
		private Label _lblBackConfValue;
		private TrackBar _trackBackIou;
		private Label _lblBackIouValue;
		private NumericUpDown _numHookThickness;
		private NumericUpDown _numBlueClassId;
		private NumericUpDown _numHoleClassId;

		// 侧面参数控件
		private TrackBar _trackSideCropRatio;
		private Label _lblSideCropValue;
		private TrackBar _trackSideConf;
		private Label _lblSideConfValue;
		private CheckBox _chkMissingAsNg;
		private CheckBox _chkContinuousMode;
		private ComboBox _cboEdgeMode;

		// 相机参数控件
		private TextBox _txtCamera1SN;
		private TextBox _txtCamera2SN;
		private TextBox _txtCamera3SN;
		private TextBox _txtCamera4SN;
		private TextBox _txtCamera5SN;
		private TextBox _txtCamera6SN;
		private TextBox _txtCamera7SN;
		private TextBox _txtCamera8SN;
		private NumericUpDown _numPulseWidth;

		// 运动参数控件
		private TextBox _txtControlIp;
		private NumericUpDown _numStartPos;
		private NumericUpDown _numEndPos;
		private NumericUpDown _numMoveSpeed;
		private NumericUpDown _numMoveAccel;

		// 保存参数控件
		private CheckBox _chkSaveOkImage;
		private CheckBox _chkSaveNgImage;
		private CheckBox _chkSaveOkRaw;
		private CheckBox _chkSaveNgRaw;
		private NumericUpDown _numJpegQuality;
		private TextBox _txtSavePath;
		private NumericUpDown _numRetentionDays;

		// 工位参数控件
		private CheckBox _chkFrontEnable;
		private CheckBox _chkEndFaceEnable;
		private CheckBox _chkBackEnable;
		private CheckBox _chkSideEnable;
		private NumericUpDown _numInPortFront;
		private NumericUpDown _numInPortEndFace;
		private NumericUpDown _numInPortBack;
		private NumericUpDown _numInPortSideTrigger;
		private NumericUpDown _numInPortSideReady;

		public event EventHandler OnParametersChanged;

		public DetectionParametersForm(DetectionParameters parameters)
		{
			_params = parameters;
			InitializeComponent();
			LoadParameters();
		}

		private void InitializeComponent()
		{
			this.Text = "检测参数设置";
			this.Size = new Size(800, 650);
			this.StartPosition = FormStartPosition.CenterParent;
			this.MinimumSize = new Size(750, 550);

			// 创建选项卡控件
			_tabControl = new TabControl
			{
				Dock = DockStyle.Fill,
				Font = new Font("微软雅黑", 10F)
			};

			// 创建各Tab页
			CreateFrontTab();
			CreateEndFaceTab();
			CreateBackTab();
			CreateSideTab();
			CreateCameraTab();
			CreateMotionTab();
			CreateSaveTab();
			CreateStationTab();

			// 按钮面板
			var btnPanel = new Panel
			{
				Dock = DockStyle.Bottom,
				Height = 50,
				BackColor = Color.FromArgb(240, 242, 245)
			};

			_btnSave = new Button
			{
				Text = "保存",
				Size = new Size(100, 35),
				Location = new Point(130, 8),
				BackColor = Color.FromArgb(39, 174, 96),
				ForeColor = Color.White,
				FlatStyle = FlatStyle.Flat
			};
			_btnSave.Click += BtnSave_Click;

			_btnCancel = new Button
			{
				Text = "取消",
				Size = new Size(100, 35),
				Location = new Point(250, 8),
				BackColor = Color.FromArgb(149, 165, 166),
				ForeColor = Color.White,
				FlatStyle = FlatStyle.Flat
			};
			_btnCancel.Click += (s, e) => this.Close();

			_btnReset = new Button
			{
				Text = "重置默认",
				Size = new Size(100, 35),
				Location = new Point(370, 8),
				BackColor = Color.FromArgb(230, 126, 34),
				ForeColor = Color.White,
				FlatStyle = FlatStyle.Flat
			};
			_btnReset.Click += BtnReset_Click;

			_btnExport = new Button
			{
				Text = "导出配置",
				Size = new Size(100, 35),
				Location = new Point(490, 8),
				BackColor = Color.FromArgb(52, 152, 219),
				ForeColor = Color.White,
				FlatStyle = FlatStyle.Flat
			};
			_btnExport.Click += BtnExport_Click;

			_btnImport = new Button
			{
				Text = "导入配置",
				Size = new Size(100, 35),
				Location = new Point(610, 8),
				BackColor = Color.FromArgb(52, 152, 219),
				ForeColor = Color.White,
				FlatStyle = FlatStyle.Flat
			};
			_btnImport.Click += BtnImport_Click;

			btnPanel.Controls.AddRange(new Control[] { _btnSave, _btnCancel, _btnReset, _btnExport, _btnImport });

			this.Controls.Add(_tabControl);
			this.Controls.Add(btnPanel);
		}

		private void CreateFrontTab()
		{
			var tab = new TabPage { Text = "正面参数" };
			var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20) };

			int y = 20;

			// 置信度阈值
			AddLabel(panel, "置信度阈值:", 20, y);
			_trackFrontConf = new TrackBar { Minimum = 1, Maximum = 100, Value = 25, Width = 200, Location = new Point(150, y - 5) };
			_lblFrontConfValue = new Label { Text = "0.25", Location = new Point(360, y + 3), AutoSize = true };
			_trackFrontConf.ValueChanged += (s, e) => _lblFrontConfValue.Text = (_trackFrontConf.Value / 100.0).ToString("F2");
			panel.Controls.Add(_trackFrontConf);
			panel.Controls.Add(_lblFrontConfValue);
			y += 40;

			// IOU阈值
			AddLabel(panel, "IOU阈值:", 20, y);
			_trackFrontIou = new TrackBar { Minimum = 1, Maximum = 100, Value = 45, Width = 200, Location = new Point(150, y - 5) };
			_lblFrontIouValue = new Label { Text = "0.45", Location = new Point(360, y + 3), AutoSize = true };
			_trackFrontIou.ValueChanged += (s, e) => _lblFrontIouValue.Text = (_trackFrontIou.Value / 100.0).ToString("F2");
			panel.Controls.Add(_trackFrontIou);
			panel.Controls.Add(_lblFrontIouValue);
			y += 40;

			// P号码OCR阈值
			AddLabel(panel, "P号码OCR阈值:", 20, y);
			_trackFrontPCodeConf = new TrackBar { Minimum = 1, Maximum = 100, Value = 50, Width = 200, Location = new Point(150, y - 5) };
			_lblFrontPCodeValue = new Label { Text = "0.50", Location = new Point(360, y + 3), AutoSize = true };
			_trackFrontPCodeConf.ValueChanged += (s, e) => _lblFrontPCodeValue.Text = (_trackFrontPCodeConf.Value / 100.0).ToString("F2");
			panel.Controls.Add(_trackFrontPCodeConf);
			panel.Controls.Add(_lblFrontPCodeValue);

			tab.Controls.Add(panel);
			_tabControl.TabPages.Add(tab);
		}

		private void CreateEndFaceTab()
		{
			var tab = new TabPage { Text = "端面参数" };
			var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20) };

			int y = 20;

			// 上端面置信度
			AddLabel(panel, "上端面置信度:", 20, y);
			_trackUpperConf = new TrackBar { Minimum = 1, Maximum = 100, Value = 50, Width = 200, Location = new Point(150, y - 5) };
			_lblUpperConfValue = new Label { Text = "0.50", Location = new Point(360, y + 3), AutoSize = true };
			_trackUpperConf.ValueChanged += (s, e) => _lblUpperConfValue.Text = (_trackUpperConf.Value / 100.0).ToString("F2");
			panel.Controls.Add(_trackUpperConf);
			panel.Controls.Add(_lblUpperConfValue);
			y += 40;

			// 上端面IOU
			AddLabel(panel, "上端面IOU:", 20, y);
			_trackUpperIou = new TrackBar { Minimum = 1, Maximum = 100, Value = 20, Width = 200, Location = new Point(150, y - 5) };
			_lblUpperIouValue = new Label { Text = "0.20", Location = new Point(360, y + 3), AutoSize = true };
			_trackUpperIou.ValueChanged += (s, e) => _lblUpperIouValue.Text = (_trackUpperIou.Value / 100.0).ToString("F2");
			panel.Controls.Add(_trackUpperIou);
			panel.Controls.Add(_lblUpperIouValue);
			y += 40;

			// 下端面置信度
			AddLabel(panel, "下端面置信度:", 20, y);
			_trackLowerConf = new TrackBar { Minimum = 1, Maximum = 100, Value = 50, Width = 200, Location = new Point(150, y - 5) };
			_lblLowerConfValue = new Label { Text = "0.50", Location = new Point(360, y + 3), AutoSize = true };
			_trackLowerConf.ValueChanged += (s, e) => _lblLowerConfValue.Text = (_trackLowerConf.Value / 100.0).ToString("F2");
			panel.Controls.Add(_trackLowerConf);
			panel.Controls.Add(_lblLowerConfValue);
			y += 40;

			// 下端面IOU
			AddLabel(panel, "下端面IOU:", 20, y);
			_trackLowerIou = new TrackBar { Minimum = 1, Maximum = 100, Value = 20, Width = 200, Location = new Point(150, y - 5) };
			_lblLowerIouValue = new Label { Text = "0.20", Location = new Point(360, y + 3), AutoSize = true };
			_trackLowerIou.ValueChanged += (s, e) => _lblLowerIouValue.Text = (_trackLowerIou.Value / 100.0).ToString("F2");
			panel.Controls.Add(_trackLowerIou);
			panel.Controls.Add(_lblLowerIouValue);
			y += 40;

			// 曝光时间
			AddLabel(panel, "曝光时间(ms):", 20, y);
			_numEndFaceExposure = new NumericUpDown { Minimum = 1, Maximum = 100, Value = 20, Width = 100, Location = new Point(150, y) };
			panel.Controls.Add(_numEndFaceExposure);

			tab.Controls.Add(panel);
			_tabControl.TabPages.Add(tab);
		}

		private void CreateBackTab()
		{
			var tab = new TabPage { Text = "背面参数" };
			var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20) };

			int y = 20;

			// 置信度阈值
			AddLabel(panel, "置信度阈值:", 20, y);
			_trackBackConf = new TrackBar { Minimum = 1, Maximum = 100, Value = 50, Width = 200, Location = new Point(150, y - 5) };
			_lblBackConfValue = new Label { Text = "0.50", Location = new Point(360, y + 3), AutoSize = true };
			_trackBackConf.ValueChanged += (s, e) => _lblBackConfValue.Text = (_trackBackConf.Value / 100.0).ToString("F2");
			panel.Controls.Add(_trackBackConf);
			panel.Controls.Add(_lblBackConfValue);
			y += 40;

			// IOU阈值
			AddLabel(panel, "IOU阈值:", 20, y);
			_trackBackIou = new TrackBar { Minimum = 1, Maximum = 100, Value = 20, Width = 200, Location = new Point(150, y - 5) };
			_lblBackIouValue = new Label { Text = "0.20", Location = new Point(360, y + 3), AutoSize = true };
			_trackBackIou.ValueChanged += (s, e) => _lblBackIouValue.Text = (_trackBackIou.Value / 100.0).ToString("F2");
			panel.Controls.Add(_trackBackIou);
			panel.Controls.Add(_lblBackIouValue);
			y += 40;

			// 挂钩厚度阈值
			AddLabel(panel, "挂钩厚度阈值(px):", 20, y);
			_numHookThickness = new NumericUpDown { Minimum = 1, Maximum = 200, Value = 30, Width = 100, Location = new Point(150, y) };
			panel.Controls.Add(_numHookThickness);
			y += 40;

			// 内圈类别ID
			AddLabel(panel, "内圈类别ID:", 20, y);
			_numBlueClassId = new NumericUpDown { Minimum = 0, Maximum = 10, Value = 0, Width = 100, Location = new Point(150, y) };
			panel.Controls.Add(_numBlueClassId);
			y += 40;

			// 外圈类别ID
			AddLabel(panel, "外圈类别ID:", 20, y);
			_numHoleClassId = new NumericUpDown { Minimum = 0, Maximum = 10, Value = 1, Width = 100, Location = new Point(150, y) };
			panel.Controls.Add(_numHoleClassId);

			tab.Controls.Add(panel);
			_tabControl.TabPages.Add(tab);
		}

		private void CreateSideTab()
		{
			var tab = new TabPage { Text = "侧面参数" };
			var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20) };

			int y = 20;

			// 裁剪比例
			AddLabel(panel, "裁剪比例(宽/高):", 20, y);
			_trackSideCropRatio = new TrackBar { Minimum = 5, Maximum = 50, Value = 20, Width = 200, Location = new Point(150, y - 5) };
			_lblSideCropValue = new Label { Text = "2.0", Location = new Point(360, y + 3), AutoSize = true };
			_trackSideCropRatio.ValueChanged += (s, e) => _lblSideCropValue.Text = (_trackSideCropRatio.Value / 10.0).ToString("F1");
			panel.Controls.Add(_trackSideCropRatio);
			panel.Controls.Add(_lblSideCropValue);
			y += 40;

			// 置信度阈值
			AddLabel(panel, "置信度阈值:", 20, y);
			_trackSideConf = new TrackBar { Minimum = 1, Maximum = 100, Value = 50, Width = 200, Location = new Point(150, y - 5) };
			_lblSideConfValue = new Label { Text = "0.50", Location = new Point(360, y + 3), AutoSize = true };
			_trackSideConf.ValueChanged += (s, e) => _lblSideConfValue.Text = (_trackSideConf.Value / 100.0).ToString("F2");
			panel.Controls.Add(_trackSideConf);
			panel.Controls.Add(_lblSideConfValue);
			y += 40;

			// 缺少图片判NG
			AddLabel(panel, "缺少图片判NG:", 20, y);
			_chkMissingAsNg = new CheckBox { Text = "启用", Location = new Point(150, y), AutoSize = true };
			panel.Controls.Add(_chkMissingAsNg);
			y += 40;

			// 连续运动模式
			AddLabel(panel, "连续运动模式:", 20, y);
			_chkContinuousMode = new CheckBox { Text = "启用", Location = new Point(150, y), AutoSize = true };
			panel.Controls.Add(_chkContinuousMode);
			y += 40;

			// 触发边缘模式
			AddLabel(panel, "触发边缘模式:", 20, y);
			_cboEdgeMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 200, Location = new Point(150, y) };
			_cboEdgeMode.Items.AddRange(new object[] { "上升沿触发左侧/下降沿触发右侧", "上升沿触发右侧/下降沿触发左侧" });
			_cboEdgeMode.SelectedIndex = 0;
			panel.Controls.Add(_cboEdgeMode);

			tab.Controls.Add(panel);
			_tabControl.TabPages.Add(tab);
		}

		private void CreateCameraTab()
		{
			var tab = new TabPage { Text = "相机参数" };
			var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20) };

			int y = 20;

			// 触发脉冲宽度
			AddLabel(panel, "触发脉冲宽度(ms):", 20, y);
			_numPulseWidth = new NumericUpDown { Minimum = 10, Maximum = 200, Value = 50, Width = 100, Location = new Point(180, y) };
			panel.Controls.Add(_numPulseWidth);
			y += 40;

			// 相机序列号
			AddLabel(panel, "相机1序列号:", 20, y);
			_txtCamera1SN = new TextBox { Width = 200, Location = new Point(180, y) };
			panel.Controls.Add(_txtCamera1SN);
			y += 35;

			AddLabel(panel, "相机2序列号:", 20, y);
			_txtCamera2SN = new TextBox { Width = 200, Location = new Point(180, y) };
			panel.Controls.Add(_txtCamera2SN);
			y += 35;

			AddLabel(panel, "相机3序列号:", 20, y);
			_txtCamera3SN = new TextBox { Width = 200, Location = new Point(180, y) };
			panel.Controls.Add(_txtCamera3SN);
			y += 35;

			AddLabel(panel, "相机4序列号:", 20, y);
			_txtCamera4SN = new TextBox { Width = 200, Location = new Point(180, y) };
			panel.Controls.Add(_txtCamera4SN);
			y += 35;

			AddLabel(panel, "相机5序列号:", 20, y);
			_txtCamera5SN = new TextBox { Width = 200, Location = new Point(180, y) };
			panel.Controls.Add(_txtCamera5SN);
			y += 35;

			AddLabel(panel, "相机6序列号:", 20, y);
			_txtCamera6SN = new TextBox { Width = 200, Location = new Point(180, y) };
			panel.Controls.Add(_txtCamera6SN);
			y += 35;

			AddLabel(panel, "相机7序列号:", 20, y);
			_txtCamera7SN = new TextBox { Width = 200, Location = new Point(180, y) };
			panel.Controls.Add(_txtCamera7SN);
			y += 35;

			AddLabel(panel, "相机8序列号:", 20, y);
			_txtCamera8SN = new TextBox { Width = 200, Location = new Point(180, y) };
			panel.Controls.Add(_txtCamera8SN);

			tab.Controls.Add(panel);
			_tabControl.TabPages.Add(tab);
		}

		private void CreateMotionTab()
		{
			var tab = new TabPage { Text = "运动参数" };
			var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20) };

			int y = 20;

			// 控制卡IP
			AddLabel(panel, "运动控制卡IP:", 20, y);
			_txtControlIp = new TextBox { Text = "192.168.0.11", Width = 150, Location = new Point(180, y) };
			panel.Controls.Add(_txtControlIp);
			y += 40;

			// 起点位置
			AddLabel(panel, "侧面运动轴起点:", 20, y);
			_numStartPos = new NumericUpDown { Minimum = -1000, Maximum = 1000, Value = 0, Width = 120, Location = new Point(180, y), DecimalPlaces = 1 };
			panel.Controls.Add(_numStartPos);
			y += 40;

			// 终点位置
			AddLabel(panel, "侧面运动轴终点:", 20, y);
			_numEndPos = new NumericUpDown { Minimum = -1000, Maximum = 1000, Value = 100, Width = 120, Location = new Point(180, y), DecimalPlaces = 1 };
			panel.Controls.Add(_numEndPos);
			y += 40;

			// 运动速度
			AddLabel(panel, "侧面运动速度:", 20, y);
			_numMoveSpeed = new NumericUpDown { Minimum = 1, Maximum = 100, Value = 20, Width = 120, Location = new Point(180, y) };
			panel.Controls.Add(_numMoveSpeed);
			y += 40;

			// 运动加速度
			AddLabel(panel, "侧面运动加速度:", 20, y);
			_numMoveAccel = new NumericUpDown { Minimum = 1000, Maximum = 100000, Increment = 1000, Value = 10000, Width = 120, Location = new Point(180, y) };
			panel.Controls.Add(_numMoveAccel);

			tab.Controls.Add(panel);
			_tabControl.TabPages.Add(tab);
		}

		private void CreateSaveTab()
		{
			var tab = new TabPage { Text = "保存参数" };
			var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20) };

			int y = 20;

			// 保存选项
			AddLabel(panel, "保存OK渲染图:", 20, y);
			_chkSaveOkImage = new CheckBox { Text = "启用", Location = new Point(180, y), AutoSize = true };
			panel.Controls.Add(_chkSaveOkImage);
			y += 35;

			AddLabel(panel, "保存NG渲染图:", 20, y);
			_chkSaveNgImage = new CheckBox { Text = "启用", Location = new Point(180, y), AutoSize = true };
			panel.Controls.Add(_chkSaveNgImage);
			y += 35;

			AddLabel(panel, "保存OK原图:", 20, y);
			_chkSaveOkRaw = new CheckBox { Text = "启用", Location = new Point(180, y), AutoSize = true };
			panel.Controls.Add(_chkSaveOkRaw);
			y += 35;

			AddLabel(panel, "保存NG原图:", 20, y);
			_chkSaveNgRaw = new CheckBox { Text = "启用", Location = new Point(180, y), AutoSize = true };
			panel.Controls.Add(_chkSaveNgRaw);
			y += 40;

			// JPEG质量
			AddLabel(panel, "JPEG压缩质量:", 20, y);
			_numJpegQuality = new NumericUpDown { Minimum = 1, Maximum = 100, Value = 85, Width = 80, Location = new Point(180, y) };
			panel.Controls.Add(_numJpegQuality);
			y += 40;

			// 保存路径
			AddLabel(panel, "图片保存路径:", 20, y);
			_txtSavePath = new TextBox { Width = 300, Location = new Point(180, y) };
			panel.Controls.Add(_txtSavePath);
			y += 40;

			// 保留天数
			AddLabel(panel, "保留天数:", 20, y);
			_numRetentionDays = new NumericUpDown { Minimum = 1, Maximum = 30, Value = 7, Width = 80, Location = new Point(180, y) };
			panel.Controls.Add(_numRetentionDays);

			tab.Controls.Add(panel);
			_tabControl.TabPages.Add(tab);
		}

		private void CreateStationTab()
		{
			var tab = new TabPage { Text = "工位参数" };
			var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20) };

			int y = 20;

			// 工位启用
			AddLabel(panel, "正面工位启用:", 20, y);
			_chkFrontEnable = new CheckBox { Text = "启用", Location = new Point(150, y), AutoSize = true };
			panel.Controls.Add(_chkFrontEnable);
			y += 35;

			AddLabel(panel, "端面工位启用:", 20, y);
			_chkEndFaceEnable = new CheckBox { Text = "启用", Location = new Point(150, y), AutoSize = true };
			panel.Controls.Add(_chkEndFaceEnable);
			y += 35;

			AddLabel(panel, "背面工位启用:", 20, y);
			_chkBackEnable = new CheckBox { Text = "启用", Location = new Point(150, y), AutoSize = true };
			panel.Controls.Add(_chkBackEnable);
			y += 35;

			AddLabel(panel, "侧面工位启用:", 20, y);
			_chkSideEnable = new CheckBox { Text = "启用", Location = new Point(150, y), AutoSize = true };
			panel.Controls.Add(_chkSideEnable);
			y += 40;

			// 输入端口
			AddLabel(panel, "正面到位输入口(IN9):", 20, y);
			_numInPortFront = new NumericUpDown { Minimum = 0, Maximum = 20, Value = 9, Width = 60, Location = new Point(180, y) };
			panel.Controls.Add(_numInPortFront);
			y += 35;

			AddLabel(panel, "端面到位输入口(IN10):", 20, y);
			_numInPortEndFace = new NumericUpDown { Minimum = 0, Maximum = 20, Value = 10, Width = 60, Location = new Point(180, y) };
			panel.Controls.Add(_numInPortEndFace);
			y += 35;

			AddLabel(panel, "背面到位输入口(IN11):", 20, y);
			_numInPortBack = new NumericUpDown { Minimum = 0, Maximum = 20, Value = 11, Width = 60, Location = new Point(180, y) };
			panel.Controls.Add(_numInPortBack);
			y += 35;

			AddLabel(panel, "侧面触发输入口(IN12):", 20, y);
			_numInPortSideTrigger = new NumericUpDown { Minimum = 0, Maximum = 20, Value = 12, Width = 60, Location = new Point(180, y) };
			panel.Controls.Add(_numInPortSideTrigger);
			y += 35;

			AddLabel(panel, "侧面到位输入口(IN13):", 20, y);
			_numInPortSideReady = new NumericUpDown { Minimum = 0, Maximum = 20, Value = 13, Width = 60, Location = new Point(180, y) };
			panel.Controls.Add(_numInPortSideReady);

			tab.Controls.Add(panel);
			_tabControl.TabPages.Add(tab);
		}

		private void AddLabel(Control parent, string text, int x, int y)
		{
			var label = new Label
			{
				Text = text,
				Location = new Point(x, y),
				Size = new Size(150, 25),
				TextAlign = ContentAlignment.MiddleRight
			};
			parent.Controls.Add(label);
		}

		private void LoadParameters()
		{
			// 正面参数
			_trackFrontConf.Value = (int)(_params.Front.ConfThreshold * 100);
			_trackFrontIou.Value = (int)(_params.Front.IouThreshold * 100);
			_trackFrontPCodeConf.Value = (int)(_params.Front.PCodeConfThreshold * 100);

			// 端面参数
			_trackUpperConf.Value = (int)(_params.EndFace.UpperConfThreshold * 100);
			_trackUpperIou.Value = (int)(_params.EndFace.UpperIouThreshold * 100);
			_trackLowerConf.Value = (int)(_params.EndFace.LowerConfThreshold * 100);
			_trackLowerIou.Value = (int)(_params.EndFace.LowerIouThreshold * 100);
			_numEndFaceExposure.Value = _params.EndFace.ExposureMs;

			// 背面参数
			_trackBackConf.Value = (int)(_params.Back.ConfThreshold * 100);
			_trackBackIou.Value = (int)(_params.Back.IouThreshold * 100);
			_numHookThickness.Value = (decimal)_params.Back.HookThicknessThreshold;
			_numBlueClassId.Value = _params.Back.HookBlueAreaClassId;
			_numHoleClassId.Value = _params.Back.HookHangHoleClassId;

			// 侧面参数
			_trackSideCropRatio.Value = (int)(_params.Side.CropRatio * 10);
			_trackSideConf.Value = (int)(_params.Side.ConfThreshold * 100);
			_chkMissingAsNg.Checked = _params.Side.MissingAsNg;
			_chkContinuousMode.Checked = _params.Side.UseContinuousMode;
			_cboEdgeMode.SelectedIndex = _params.Side.TriggerEdgeMode == "RisingLeftFallingRight" ? 0 : 1;

			// 相机参数
			_numPulseWidth.Value = _params.Camera.PulseWidthMs;
			_txtCamera1SN.Text = _params.Camera.Camera1SN;
			_txtCamera2SN.Text = _params.Camera.Camera2SN;
			_txtCamera3SN.Text = _params.Camera.Camera3SN;
			_txtCamera4SN.Text = _params.Camera.Camera4SN;
			_txtCamera5SN.Text = _params.Camera.Camera5SN;
			_txtCamera6SN.Text = _params.Camera.Camera6SN;
			_txtCamera7SN.Text = _params.Camera.Camera7SN;
			_txtCamera8SN.Text = _params.Camera.Camera8SN;

			// 运动参数
			_txtControlIp.Text = _params.Motion.ControlIp;
			_numStartPos.Value = (decimal)_params.Motion.SideStartPosition;
			_numEndPos.Value = (decimal)_params.Motion.SideEndPosition;
			_numMoveSpeed.Value = _params.Motion.SideMoveSpeed;
			_numMoveAccel.Value = _params.Motion.SideMoveAccel;

			// 保存参数
			_chkSaveOkImage.Checked = _params.Save.SaveOkImage;
			_chkSaveNgImage.Checked = _params.Save.SaveNgImage;
			_chkSaveOkRaw.Checked = _params.Save.SaveOkRawImage;
			_chkSaveNgRaw.Checked = _params.Save.SaveNgRawImage;
			_numJpegQuality.Value = _params.Save.JpegQuality;
			_txtSavePath.Text = _params.Save.ImageSavePath;
			_numRetentionDays.Value = _params.Save.RetentionDays;

			// 工位参数
			_chkFrontEnable.Checked = _params.Station.FrontEnabled;
			_chkEndFaceEnable.Checked = _params.Station.EndFaceEnabled;
			_chkBackEnable.Checked = _params.Station.BackEnabled;
			_chkSideEnable.Checked = _params.Station.SideEnabled;
			_numInPortFront.Value = _params.Station.InPortFront;
			_numInPortEndFace.Value = _params.Station.InPortEndFace;
			_numInPortBack.Value = _params.Station.InPortBack;
			_numInPortSideTrigger.Value = _params.Station.InPortSideTrigger;
			_numInPortSideReady.Value = _params.Station.InPortSideReady;

			Logger.Debug("检测参数加载到界面完成");
		}

		private void SaveParameters()
		{
			// 正面参数
			_params.Front.ConfThreshold = _trackFrontConf.Value / 100f;
			_params.Front.IouThreshold = _trackFrontIou.Value / 100f;
			_params.Front.PCodeConfThreshold = _trackFrontPCodeConf.Value / 100f;

			// 端面参数
			_params.EndFace.UpperConfThreshold = _trackUpperConf.Value / 100f;
			_params.EndFace.UpperIouThreshold = _trackUpperIou.Value / 100f;
			_params.EndFace.LowerConfThreshold = _trackLowerConf.Value / 100f;
			_params.EndFace.LowerIouThreshold = _trackLowerIou.Value / 100f;
			_params.EndFace.ExposureMs = (int)_numEndFaceExposure.Value;

			// 背面参数
			_params.Back.ConfThreshold = _trackBackConf.Value / 100f;
			_params.Back.IouThreshold = _trackBackIou.Value / 100f;
			_params.Back.HookThicknessThreshold = (float)_numHookThickness.Value;
			_params.Back.HookBlueAreaClassId = (int)_numBlueClassId.Value;
			_params.Back.HookHangHoleClassId = (int)_numHoleClassId.Value;

			// 侧面参数
			_params.Side.CropRatio = _trackSideCropRatio.Value / 10f;
			_params.Side.ConfThreshold = _trackSideConf.Value / 100f;
			_params.Side.MissingAsNg = _chkMissingAsNg.Checked;
			_params.Side.UseContinuousMode = _chkContinuousMode.Checked;
			_params.Side.TriggerEdgeMode = _cboEdgeMode.SelectedIndex == 0 ? "RisingLeftFallingRight" : "RisingRightFallingLeft";

			// 相机参数
			_params.Camera.PulseWidthMs = (int)_numPulseWidth.Value;
			_params.Camera.Camera1SN = _txtCamera1SN.Text;
			_params.Camera.Camera2SN = _txtCamera2SN.Text;
			_params.Camera.Camera3SN = _txtCamera3SN.Text;
			_params.Camera.Camera4SN = _txtCamera4SN.Text;
			_params.Camera.Camera5SN = _txtCamera5SN.Text;
			_params.Camera.Camera6SN = _txtCamera6SN.Text;
			_params.Camera.Camera7SN = _txtCamera7SN.Text;
			_params.Camera.Camera8SN = _txtCamera8SN.Text;

			// 运动参数
			_params.Motion.ControlIp = _txtControlIp.Text;
			_params.Motion.SideStartPosition = (float)_numStartPos.Value;
			_params.Motion.SideEndPosition = (float)_numEndPos.Value;
			_params.Motion.SideMoveSpeed = (int)_numMoveSpeed.Value;
			_params.Motion.SideMoveAccel = (int)_numMoveAccel.Value;

			// 保存参数
			_params.Save.SaveOkImage = _chkSaveOkImage.Checked;
			_params.Save.SaveNgImage = _chkSaveNgImage.Checked;
			_params.Save.SaveOkRawImage = _chkSaveOkRaw.Checked;
			_params.Save.SaveNgRawImage = _chkSaveNgRaw.Checked;
			_params.Save.JpegQuality = (int)_numJpegQuality.Value;
			_params.Save.ImageSavePath = _txtSavePath.Text;
			_params.Save.RetentionDays = (int)_numRetentionDays.Value;

			// 工位参数
			_params.Station.FrontEnabled = _chkFrontEnable.Checked;
			_params.Station.EndFaceEnabled = _chkEndFaceEnable.Checked;
			_params.Station.BackEnabled = _chkBackEnable.Checked;
			_params.Station.SideEnabled = _chkSideEnable.Checked;
			_params.Station.InPortFront = (int)_numInPortFront.Value;
			_params.Station.InPortEndFace = (int)_numInPortEndFace.Value;
			_params.Station.InPortBack = (int)_numInPortBack.Value;
			_params.Station.InPortSideTrigger = (int)_numInPortSideTrigger.Value;
			_params.Station.InPortSideReady = (int)_numInPortSideReady.Value;

			_params.SaveToFile();
			OnParametersChanged?.Invoke(this, EventArgs.Empty);

			MessageBox.Show("参数保存成功！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
			Logger.Info("检测参数已保存");
		}

		private void BtnSave_Click(object sender, EventArgs e)
		{
			SaveParameters();
			this.Close();
		}

		private void BtnReset_Click(object sender, EventArgs e)
		{
			if (MessageBox.Show("确认重置所有参数为默认值吗？", "确认",
				MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
			{
				_params.ResetToDefault();
				LoadParameters();
				MessageBox.Show("参数已重置为默认值，请点击保存生效。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
		}

		private void BtnExport_Click(object sender, EventArgs e)
		{
			using (var dialog = new SaveFileDialog
			{
				Filter = "JSON文件|*.json",
				FileName = $"DetectionParams_{DateTime.Now:yyyyMMdd_HHmmss}.json"
			})
			{
				if (dialog.ShowDialog() == DialogResult.OK)
				{
					string json = _params.ExportToJson();
					System.IO.File.WriteAllText(dialog.FileName, json);
					MessageBox.Show($"配置已导出到: {dialog.FileName}", "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
					Logger.Info($"参数已导出: {dialog.FileName}");
				}
			}
		}

		private void BtnImport_Click(object sender, EventArgs e)
		{
			using (var dialog = new OpenFileDialog
			{
				Filter = "JSON文件|*.json"
			})
			{
				if (dialog.ShowDialog() == DialogResult.OK)
				{
					string json = System.IO.File.ReadAllText(dialog.FileName);
					if (_params.ImportFromJson(json))
					{
						LoadParameters();
						MessageBox.Show("配置导入成功！", "导入成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
						Logger.Info($"参数已导入: {dialog.FileName}");
					}
					else
					{
						MessageBox.Show("配置导入失败，文件格式不正确！", "导入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
					}
				}
			}
		}
	}
}