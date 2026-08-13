using System;
using System.Drawing;
using System.Windows.Forms;
using Config;
using VisionMeasure.Utils;
using CommonLib;

namespace VisionMeasure.From
{
/// <summary>检测参数设置界面 — 11个Tab(正面/条码/日期码/挂钩/端面/侧面/字号/相机/运动/保存/工位), 保存后触发OnParametersChanged热更新</summary>
	public partial class DetectionParametersForm : Form
	{
		private DetectionParameters _params;
		private Config.ModelParams _barcodeParams, _datecodeParams, _frontPcodeParams, _hookParams, _sideParams, _endfaceUpperParams;
		private TabControl _tabControl;
		private Button _btnSave, _btnCancel, _btnReset, _btnExport, _btnImport;
		// 正面
		private CheckBox _chkEnablePNumberCheck;
		private CheckBox _chkEnableBoxBreakCheck;
		private CheckBox _chkEnableBarcodeCheck;
			private CheckBox _chkEnableHookCheck;
			private CheckBox _chkEnableUpperDefectCheck;
			private CheckBox _chkEnableSideDefectCheck;
		private TrackBar _trackPCodeStartRatio, _trackFrontBoxConf, _trackFrontBoxIou;
		private Label _lblPCodeStartRatio, _lblFrontBoxConf, _lblFrontBoxIou;
		// 背面-条码
		private CheckBox _chkBcEnablePreprocess, _chkBcGaussianBlur, _chkBcMedianBlur, _chkBcEqualizeHist;
		private CheckBox _chkBcInvert, _chkBcMorphClose, _chkBcMorphOpen, _chkBcMorphDilate, _chkBcMorphErode;
		private CheckBox _chkBcFilterBestMatch, _chkBcTryHarder, _chkBcRotationRetry;
		private TrackBar _trackBcContrastAlpha, _trackBcBrightnessBeta, _trackBcStartRatio;
		private Label _lblBcContrastAlpha, _lblBcBrightnessBeta, _lblBcStartRatio;
		private ComboBox _cboBcThresholdMode;
		private NumericUpDown _numBcAdaptiveBlockSize, _numBcAdaptiveC, _numBcFixedThreshold, _numBcMinLength, _numBcMaxLength;
		// 背面-日期码
		private TrackBar _trackDcStartRatio, _trackDcBottomRatio;
		private Label _lblDcStartRatio, _lblDcBottomRatio;
		// 背面-挂钩
		private NumericUpDown _numHookThickness, _numHookBlueClassId, _numHookHoleClassId;
		private TrackBar _trackHookConf, _trackHookIou;
		private Label _lblHookConf, _lblHookIou;
		// 端面
		private NumericUpDown _numEndFaceExposure, _numEndFaceFontDefect, _numEndFaceFontStatus;
		private TrackBar _trackEndFaceConf, _trackEndFaceIou;
		private Label _lblEndFaceConf, _lblEndFaceIou;
		// 侧面
		private TrackBar _trackSideCropRatio, _trackSideConf, _trackSideIou;
		private Label _lblSideCropRatio, _lblSideConf, _lblSideIou;
		private CheckBox _chkSideMotionEnabled, _chkSideContinuousMode, _chkSideMissingAsNg;
		private ComboBox _cboSideEdgeMode;
		// 显示字号
		private NumericUpDown _numFontBarcode, _numFontDefect, _numFontStatus, _numFontBoxNum;
		// 相机
		private TextBox _txtCamera1SN, _txtCamera2SN, _txtCamera3SN, _txtCamera4SN;
		private TextBox _txtCamera5SN, _txtCamera6SN, _txtCamera7SN, _txtCamera8SN;
		private NumericUpDown _numPulseWidth;
		// 运动
		private TextBox _txtControlIp;
		private NumericUpDown _numStartPos, _numEndPos, _numMoveSpeed, _numMoveAccel;
		private NumericUpDown _numSafetyLockPort;
		private CheckBox _chkSafetyLockActiveHigh;
			private ComboBox _cboSafetyLockRecovery;
		// 保存
		private CheckBox _chkSaveOkImage, _chkSaveNgImage, _chkSaveOkRaw, _chkSaveNgRaw;
		private NumericUpDown _numJpegQuality, _numRetentionDays;
		private TextBox _txtSavePath;
		// 工位
		private CheckBox _chkFrontEnable, _chkEndFaceEnable, _chkBackEnable, _chkSideEnable;
		private NumericUpDown _numInPortFront, _numInPortEndFace, _numInPortBack, _numInPortSideTrigger, _numInPortSideReady;
		public event EventHandler OnParametersChanged;


		public DetectionParametersForm(DetectionParameters parameters)
		{
			_params = parameters;
			LoadModelParams();
			InitializeComponent();
			LoadParameters();
		}

		private void LoadModelParams()
		{
			_barcodeParams = Config.ModelParams.Load("barcode");
			_datecodeParams = Config.ModelParams.Load("datecode");
			_frontPcodeParams = Config.ModelParams.Load("front_pcode");
			_hookParams = Config.ModelParams.Load("hook");
			_sideParams = Config.ModelParams.Load("side");
			_endfaceUpperParams = Config.ModelParams.Load("endface_upper");
		}

		private void InitializeComponent()
		{
			this.Text = "检测参数设置";
			this.BackColor = Color.White;
			this.Size = new Size(820, 680);
			this.StartPosition = FormStartPosition.CenterParent;
			this.MinimumSize = new Size(750, 550);

			_tabControl = new TabControl { Dock = DockStyle.Fill, Font = new Font("微软雅黑", 10F) };
			CreateFrontTab();
			CreateBarcodeTab();
			CreateDateCodeTab();
			CreateHookTab();
			CreateEndFaceTab();
			CreateSideTab();
			CreateFontTab();
			CreateCameraTab();
			CreateMotionTab();
			CreateSaveTab();
			CreateStationTab();

			var btnPanel = new Panel { Dock = DockStyle.Bottom, Height = 50, BackColor = Color.FromArgb(240, 242, 245) };
			_btnSave = new Button { Text = "保存并应用", Size = new Size(110, 35), Location = new Point(80, 8), BackColor = Color.FromArgb(39, 174, 96), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
			_btnSave.Click += BtnSave_Click;
			_btnCancel = new Button { Text = "取消", Size = new Size(80, 35), Location = new Point(210, 8), BackColor = Color.FromArgb(149, 165, 166), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
			_btnCancel.Click += (s, e) => this.Close();
			_btnReset = new Button { Text = "重置默认", Size = new Size(90, 35), Location = new Point(310, 8), BackColor = Color.FromArgb(230, 126, 34), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
			_btnReset.Click += BtnReset_Click;
			_btnExport = new Button { Text = "导出配置", Size = new Size(90, 35), Location = new Point(430, 8), BackColor = Color.FromArgb(52, 152, 219), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
			_btnExport.Click += BtnExport_Click;
			_btnImport = new Button { Text = "导入配置", Size = new Size(90, 35), Location = new Point(540, 8), BackColor = Color.FromArgb(52, 152, 219), ForeColor = Color.White, FlatStyle = FlatStyle.Flat };
			_btnImport.Click += BtnImport_Click;
			btnPanel.Controls.AddRange(new Control[] { _btnSave, _btnCancel, _btnReset, _btnExport, _btnImport });

			this.Controls.Add(_tabControl);
			this.Controls.Add(btnPanel);
		}

		// ====== 正面参数 ======
		private void CreateFrontTab()
		{
			var tab = new TabPage { BackColor = Color.White, Text = "正面参数" };
			var pnl = new Panel { BackColor = Color.White, Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20) };
			int y = 20;
			_chkEnablePNumberCheck = new CheckBox { Text = "启用P号码检测(关闭时仅显示不判NG)", Location = new Point(20, y), AutoSize = true, Font = new Font("微软雅黑", 10F, FontStyle.Bold) };
			pnl.Controls.Add(_chkEnablePNumberCheck);
			y += 45;
			_chkEnableBoxBreakCheck = new CheckBox { Text = "启用盒子破检测(关闭=跳过推理，结果OK)", Location = new Point(20, y), AutoSize = true, Font = new Font("微软雅黑", 10F, FontStyle.Bold) };
			pnl.Controls.Add(_chkEnableBoxBreakCheck);
			y += 35;
			AddLabel(pnl, "P号码裁剪起始比例:", 20, y);
			_trackPCodeStartRatio = new TrackBar { Minimum = 0, Maximum = 100, Value = 66, Width = 250, Location = new Point(180, y - 5) };
			_lblPCodeStartRatio = new Label { BackColor = Color.Transparent, Text = "0.66", Location = new Point(440, y + 3), AutoSize = true };
			_trackPCodeStartRatio.ValueChanged += (s, e) => _lblPCodeStartRatio.Text = (_trackPCodeStartRatio.Value / 100.0).ToString("F2");
			pnl.Controls.Add(_trackPCodeStartRatio);
			pnl.Controls.Add(_lblPCodeStartRatio);
			y += 45;
			AddLabel(pnl, "(比例=1.0则整图搜索, =0.66则从下1/3处开始)", 20, y); pnl.Controls[pnl.Controls.Count - 1].ForeColor = Color.Gray;
			y += 40;
			// 盒子破检测(YOLO)阈值
			AddLabel(pnl, "━━ 盒子破检测(YOLO)阈值 ━━", 20, y); pnl.Controls[pnl.Controls.Count - 1].Font = new Font("微软雅黑", 9F, FontStyle.Bold); pnl.Controls[pnl.Controls.Count - 1].ForeColor = Color.DarkBlue; y += 30;
			AddTrackBar(pnl, "置信度:", 20, ref y, 1, 100, 50, out _trackFrontBoxConf, out _lblFrontBoxConf, "F2");
			AddTrackBar(pnl, "IoU:", 20, ref y, 1, 100, 45, out _trackFrontBoxIou, out _lblFrontBoxIou, "F2");
			tab.Controls.Add(pnl);
			_tabControl.TabPages.Add(tab);
		}


		// ====== 背面-条码预处理 ======
		private void CreateBarcodeTab()
		{
			var tab = new TabPage { BackColor = Color.White, Text = "背面-条码" };
			var pnl = new Panel { BackColor = Color.White, Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20) };
			int y = 20, cx = 180;
			_chkBcEnablePreprocess = AddCheckBox(pnl, "启用条码预处理管线", 20, ref y);
			y += 5;
			_chkEnableBarcodeCheck = new CheckBox { Text = "启用条形码检测(关闭=跳过，结果OK)", Location = new Point(20, y), AutoSize = true, Font = new Font("微软雅黑", 10F, FontStyle.Bold), Checked = true };
			pnl.Controls.Add(_chkEnableBarcodeCheck);
			y += 35;
			AddLabel(pnl, "对比度增强系数:", 20, y);
			_trackBcContrastAlpha = new TrackBar { Minimum = 10, Maximum = 30, Value = 10, Width = 200, Location = new Point(cx, y - 5) };
			_lblBcContrastAlpha = new Label { BackColor = Color.Transparent, Text = "1.0", Location = new Point(390, y + 3), AutoSize = true };
			_trackBcContrastAlpha.ValueChanged += (s, e) => _lblBcContrastAlpha.Text = (_trackBcContrastAlpha.Value / 10.0).ToString("F1");
			pnl.Controls.Add(_trackBcContrastAlpha); pnl.Controls.Add(_lblBcContrastAlpha); y += 40;
			AddLabel(pnl, "亮度偏移:", 20, y);
			_trackBcBrightnessBeta = new TrackBar { Minimum = -100, Maximum = 100, Value = 0, Width = 200, Location = new Point(cx, y - 5) };
			_lblBcBrightnessBeta = new Label { BackColor = Color.Transparent, Text = "0", Location = new Point(390, y + 3), AutoSize = true };
			_trackBcBrightnessBeta.ValueChanged += (s, e) => _lblBcBrightnessBeta.Text = _trackBcBrightnessBeta.Value.ToString();
			pnl.Controls.Add(_trackBcBrightnessBeta); pnl.Controls.Add(_lblBcBrightnessBeta); y += 40;
			_chkBcGaussianBlur = AddCheckBox(pnl, "高斯模糊(5x5)", 20, ref y);
			_chkBcMedianBlur = AddCheckBox(pnl, "中值滤波(5x5)", 20, ref y);
			_chkBcEqualizeHist = AddCheckBox(pnl, "直方图均衡化", 20, ref y);
			AddLabel(pnl, "二值化模式:", 20, y);
			_cboBcThresholdMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 150, Location = new Point(cx, y) };
			_cboBcThresholdMode.Items.AddRange(new object[] { "不二值化", "自适应阈值", "Otsu", "固定阈值" });
			_cboBcThresholdMode.SelectedIndex = 0;
			pnl.Controls.Add(_cboBcThresholdMode); y += 40;
			AddLabel(pnl, "自适应块大小(奇数):", 20, y);
			_numBcAdaptiveBlockSize = new NumericUpDown { Minimum = 3, Maximum = 51, Increment = 2, Value = 11, Width = 80, Location = new Point(cx, y) };
			pnl.Controls.Add(_numBcAdaptiveBlockSize); y += 35;
			AddLabel(pnl, "自适应C值:", 20, y);
			_numBcAdaptiveC = new NumericUpDown { Minimum = -10, Maximum = 10, Value = 2, Width = 80, Location = new Point(cx, y), DecimalPlaces = 1 };
			pnl.Controls.Add(_numBcAdaptiveC); y += 35;
			AddLabel(pnl, "固定阈值(0-255):", 20, y);
			_numBcFixedThreshold = new NumericUpDown { Minimum = 0, Maximum = 255, Value = 128, Width = 80, Location = new Point(cx, y) };
			pnl.Controls.Add(_numBcFixedThreshold); y += 35;
			_chkBcInvert = AddCheckBox(pnl, "图像翻转(黑底白字→白底黑字)", 20, ref y);
			_chkBcMorphClose = AddCheckBox(pnl, "形态学闭运算(连接断线)", 20, ref y);
			_chkBcMorphOpen = AddCheckBox(pnl, "形态学开运算(去噪点)", 20, ref y);
			_chkBcMorphDilate = AddCheckBox(pnl, "膨胀", 20, ref y);
			_chkBcMorphErode = AddCheckBox(pnl, "腐蚀", 20, ref y);
			y += 5;
			AddLabel(pnl, "条码裁剪起始比例:", 20, y);
			_trackBcStartRatio = new TrackBar { Minimum = 0, Maximum = 100, Value = 66, Width = 200, Location = new Point(cx, y - 5) };
			_lblBcStartRatio = new Label { BackColor = Color.Transparent, Text = "0.66", Location = new Point(390, y + 3), AutoSize = true };
			_trackBcStartRatio.ValueChanged += (s, e) => _lblBcStartRatio.Text = (_trackBcStartRatio.Value / 100.0).ToString("F2");
			pnl.Controls.Add(_trackBcStartRatio); pnl.Controls.Add(_lblBcStartRatio); y += 40;
			_chkBcFilterBestMatch = AddCheckBox(pnl, "启用最佳匹配过滤", 20, ref y);
			_chkBcTryHarder = AddCheckBox(pnl, "强解析模式(TryHarder)", 20, ref y);
			_chkBcRotationRetry = AddCheckBox(pnl, "旋转重试", 20, ref y);
			AddLabel(pnl, "最短条码长度:", 20, y);
			_numBcMinLength = new NumericUpDown { Minimum = 1, Maximum = 100, Value = 3, Width = 80, Location = new Point(cx, y) };
			pnl.Controls.Add(_numBcMinLength); y += 35;
			AddLabel(pnl, "最长条码长度:", 20, y);
			_numBcMaxLength = new NumericUpDown { Minimum = 1, Maximum = 100, Value = 50, Width = 80, Location = new Point(cx, y) };
			pnl.Controls.Add(_numBcMaxLength); y += 35;
			tab.Controls.Add(pnl); _tabControl.TabPages.Add(tab);
		}

		// ====== 背面-日期码 ======
		private void CreateDateCodeTab()
		{
			var tab = new TabPage { BackColor = Color.White, Text = "背面-日期码" };
			var pnl = new Panel { BackColor = Color.White, Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20) };
			int y = 20;
			AddLabel(pnl, "裁掉上方比例(从拼接全图顶部裁):", 20, y);
			pnl.Controls[pnl.Controls.Count - 1].Size = new Size(280, 25);
			_trackDcStartRatio = new TrackBar { Minimum = 0, Maximum = 100, Value = 66, Width = 250, Location = new Point(310, y - 5) };
			_lblDcStartRatio = new Label { BackColor = Color.Transparent, Text = "0.66", Location = new Point(570, y + 3), AutoSize = true };
			_trackDcStartRatio.ValueChanged += (s, e) => _lblDcStartRatio.Text = (_trackDcStartRatio.Value / 100.0).ToString("F2");
			pnl.Controls.Add(_trackDcStartRatio); pnl.Controls.Add(_lblDcStartRatio); y += 45;
			AddLabel(pnl, "底部边界从顶算(保留至全图高度百分比):", 20, y);
			pnl.Controls[pnl.Controls.Count - 1].Size = new Size(300, 25);
			_trackDcBottomRatio = new TrackBar { Minimum = 0, Maximum = 100, Value = 100, Width = 250, Location = new Point(310, y - 5) };
			_lblDcBottomRatio = new Label { BackColor = Color.Transparent, Text = "1.00", Location = new Point(570, y + 3), AutoSize = true };
			_trackDcBottomRatio.ValueChanged += (s, e) => _lblDcBottomRatio.Text = (_trackDcBottomRatio.Value / 100.0).ToString("F2");
			pnl.Controls.Add(_trackDcBottomRatio); pnl.Controls.Add(_lblDcBottomRatio); y += 45;
			AddLabel(pnl, "(可视: 上方滑块↔下方滑块间距 = 有效识别区域, 开关在「工位参数」, 格式由SKU决定)", 20, y);
			pnl.Controls[pnl.Controls.Count - 1].Size = new Size(530, 25);
			pnl.Controls[pnl.Controls.Count - 1].ForeColor = Color.Gray;
			tab.Controls.Add(pnl); _tabControl.TabPages.Add(tab);
		}

		// ====== 背面-挂钩 ======
		private void CreateHookTab()
		{
			var tab = new TabPage { BackColor = Color.White, Text = "背面-挂钩" };
			var pnl = new Panel { BackColor = Color.White, Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20) };
			int y = 20, cx = 180;
			AddLabel(pnl, "挂钩厚度阈值(px):", 20, y);
			_chkEnableHookCheck = new CheckBox { Text = "启用挂钩检测(关闭=跳过推理，结果OK)", Location = new Point(20, y), AutoSize = true, Font = new Font("微软雅黑", 10F, FontStyle.Bold), Checked = true };
			pnl.Controls.Add(_chkEnableHookCheck);
			y += 40;
			_numHookThickness = new NumericUpDown { Minimum = 1, Maximum = 500, Value = 30, Width = 80, Location = new Point(cx, y) };
			pnl.Controls.Add(_numHookThickness); y += 40;
			AddLabel(pnl, "内圈(蓝色)类别ID:", 20, y);
			_numHookBlueClassId = new NumericUpDown { Minimum = 0, Maximum = 10, Value = 0, Width = 80, Location = new Point(cx, y) };
			pnl.Controls.Add(_numHookBlueClassId); y += 40;
			AddLabel(pnl, "外圈(挂孔)类别ID:", 20, y);
			_numHookHoleClassId = new NumericUpDown { Minimum = 0, Maximum = 10, Value = 1, Width = 80, Location = new Point(cx, y) };
			pnl.Controls.Add(_numHookHoleClassId); y += 45;
			AddLabel(pnl, "(厚度越大越宽松, 类别ID对应YOLO分割模型输出)", 20, y);
			pnl.Controls[pnl.Controls.Count - 1].ForeColor = Color.Gray;
			y += 35;
			// 明显挂钩错位(YOLO)阈值
			AddLabel(pnl, "━━ 明显挂钩错位(YOLO)阈值 ━━", 20, y); pnl.Controls[pnl.Controls.Count - 1].Font = new Font("微软雅黑", 9F, FontStyle.Bold); pnl.Controls[pnl.Controls.Count - 1].ForeColor = Color.DarkBlue; y += 30;
			AddTrackBar(pnl, "置信度:", 20, ref y, 1, 100, 50, out _trackHookConf, out _lblHookConf, "F2");
			AddTrackBar(pnl, "IoU:", 20, ref y, 1, 100, 20, out _trackHookIou, out _lblHookIou, "F2");
			tab.Controls.Add(pnl); _tabControl.TabPages.Add(tab);
		}


		// ====== 端面参数 ======
		private void CreateEndFaceTab()
		{
			var tab = new TabPage { BackColor = Color.White, Text = "端面参数" };
			var pnl = new Panel { BackColor = Color.White, Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20) };
			int y = 20, cx = 180;
			_chkEnableUpperDefectCheck = new CheckBox { Text = "启用上端面缺陷检测(关闭=跳过推理，结果OK)", Location = new Point(20, y), AutoSize = true, Font = new Font("微软雅黑", 10F, FontStyle.Bold), Checked = true };
			pnl.Controls.Add(_chkEnableUpperDefectCheck);
			y += 40;
			AddLabel(pnl, "曝光时间(ms):", 20, y);
			_numEndFaceExposure = new NumericUpDown { Minimum = 1, Maximum = 100, Value = 20, Width = 80, Location = new Point(cx, y) };
			pnl.Controls.Add(_numEndFaceExposure); y += 40;
			AddLabel(pnl, "缺陷标注字号:", 20, y);
			_numEndFaceFontDefect = new NumericUpDown { Minimum = 6, Maximum = 72, Value = 18, Width = 80, Location = new Point(cx, y) };
			pnl.Controls.Add(_numEndFaceFontDefect); y += 40;
			AddLabel(pnl, "OK/NG状态字号:", 20, y);
			_numEndFaceFontStatus = new NumericUpDown { Minimum = 12, Maximum = 120, Value = 48, Width = 80, Location = new Point(cx, y) };
			pnl.Controls.Add(_numEndFaceFontStatus); y += 45;
			// 上端面缺陷(YOLO)阈值
			AddLabel(pnl, "━━ 上端面缺陷(YOLO)阈值 ━━", 20, y); pnl.Controls[pnl.Controls.Count - 1].Font = new Font("微软雅黑", 9F, FontStyle.Bold); pnl.Controls[pnl.Controls.Count - 1].ForeColor = Color.DarkBlue; y += 30;
			AddTrackBar(pnl, "置信度:", 20, ref y, 1, 100, 50, out _trackEndFaceConf, out _lblEndFaceConf, "F2");
			int endFaceIouVal = 20;
			AddTrackBar(pnl, "IoU:", 20, ref y, 1, 100, endFaceIouVal, out _trackEndFaceIou, out _lblEndFaceIou, "F2");
			tab.Controls.Add(pnl); _tabControl.TabPages.Add(tab);
		}

		// ====== 侧面参数 ======
		private void CreateSideTab()
		{
			var tab = new TabPage { BackColor = Color.White, Text = "侧面参数" };
			var pnl = new Panel { BackColor = Color.White, Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20) };
			int y = 20, cx = 180;
			_chkEnableSideDefectCheck = new CheckBox { Text = "启用侧面缺陷检测(关闭=跳过推理，结果OK)", Location = new Point(20, y), AutoSize = true, Font = new Font("微软雅黑", 10F, FontStyle.Bold), Checked = true };
			pnl.Controls.Add(_chkEnableSideDefectCheck);
			y += 40;
			AddLabel(pnl, "裁剪比例(宽/高):", 20, y);
			_trackSideCropRatio = new TrackBar { Minimum = 5, Maximum = 50, Value = 20, Width = 200, Location = new Point(cx, y - 5) };
			_lblSideCropRatio = new Label { BackColor = Color.Transparent, Text = "2.0", Location = new Point(390, y + 3), AutoSize = true };
			_trackSideCropRatio.ValueChanged += (s, e) => _lblSideCropRatio.Text = (_trackSideCropRatio.Value / 10.0).ToString("F1");
			pnl.Controls.Add(_trackSideCropRatio); pnl.Controls.Add(_lblSideCropRatio); y += 40;
			_chkSideMotionEnabled = AddCheckBox(pnl, "启用运动轴(关=不触发侧面检测)", 20, ref y);
			_chkSideContinuousMode = AddCheckBox(pnl, "连续运动模式(飞拍→停拍)", 20, ref y);
			_chkSideMissingAsNg = AddCheckBox(pnl, "缺少图片判NG", 20, ref y);
			AddLabel(pnl, "触发边缘模式(IN12):", 20, y);
			_cboSideEdgeMode = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 220, Location = new Point(cx, y) };
			_cboSideEdgeMode.Items.AddRange(new object[] { "上升沿→左侧 下降沿→右侧", "上升沿→右侧 下降沿→左侧" });
			_cboSideEdgeMode.SelectedIndex = 0;
			pnl.Controls.Add(_cboSideEdgeMode); y += 45;
			// 侧面缺陷(YOLO)阈值
			AddLabel(pnl, "━━ 侧面缺陷检测(YOLO)阈值 ━━", 20, y); pnl.Controls[pnl.Controls.Count - 1].Font = new Font("微软雅黑", 9F, FontStyle.Bold); pnl.Controls[pnl.Controls.Count - 1].ForeColor = Color.DarkBlue; y += 30;
			AddTrackBar(pnl, "置信度:", 20, ref y, 1, 100, 50, out _trackSideConf, out _lblSideConf, "F2");
			AddTrackBar(pnl, "IoU:", 20, ref y, 1, 100, 45, out _trackSideIou, out _lblSideIou, "F2");
			AddLabel(pnl, "(连续运动模式和边缘模式需重启软件生效)", 20, y);
			pnl.Controls[pnl.Controls.Count - 1].ForeColor = Color.Gray;
			tab.Controls.Add(pnl); _tabControl.TabPages.Add(tab);
		}

		// ====== 显示字号 ======
		private void CreateFontTab()
		{
			var tab = new TabPage { BackColor = Color.White, Text = "显示字号" };
			var pnl = new Panel { BackColor = Color.White, Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20) };
			int y = 20, cx = 180;
			AddLabel(pnl, "条码结果字号(背面):", 20, y);
			_numFontBarcode = new NumericUpDown { Minimum = 8, Maximum = 72, Value = 28, Width = 80, Location = new Point(cx, y) };
			pnl.Controls.Add(_numFontBarcode); y += 35;
			AddLabel(pnl, "缺陷标注字号(背面):", 20, y);
			_numFontDefect = new NumericUpDown { Minimum = 8, Maximum = 72, Value = 18, Width = 80, Location = new Point(cx, y) };
			pnl.Controls.Add(_numFontDefect); y += 35;
			AddLabel(pnl, "OK/NG状态字号(背面):", 20, y);
			_numFontStatus = new NumericUpDown { Minimum = 12, Maximum = 120, Value = 48, Width = 80, Location = new Point(cx, y) };
			pnl.Controls.Add(_numFontStatus); y += 35;
			AddLabel(pnl, "盒序号字号(正面+背面):", 20, y);
			_numFontBoxNum = new NumericUpDown { Minimum = 8, Maximum = 72, Value = 28, Width = 80, Location = new Point(cx, y) };
			pnl.Controls.Add(_numFontBoxNum); y += 45;
			AddLabel(pnl, "(0=使用代码默认值, 仅影响渲染显示不影响检测)", 20, y);
			pnl.Controls[pnl.Controls.Count - 1].ForeColor = Color.Gray;
			tab.Controls.Add(pnl); _tabControl.TabPages.Add(tab);
		}


		// ====== 相机参数 ======
		private void CreateCameraTab()
		{
			var tab = new TabPage { BackColor = Color.White, Text = "相机参数" };
			var pnl = new Panel { BackColor = Color.White, Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20) };
			int y = 20, cx = 180;
			AddLabel(pnl, "触发脉冲宽度(ms):", 20, y);
			_numPulseWidth = new NumericUpDown { Minimum = 10, Maximum = 200, Value = 50, Width = 80, Location = new Point(cx, y) };
			pnl.Controls.Add(_numPulseWidth); y += 40;
			for (int i = 1; i <= 8; i++) { AddLabel(pnl, "相机" + i + "序列号:", 20, y); var tb = new TextBox { Width = 200, Location = new Point(cx, y + 2) }; pnl.Controls.Add(tb); var f = typeof(DetectionParametersForm).GetField("_txtCamera" + i + "SN", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance); f?.SetValue(this, tb); y += 35; }
			tab.Controls.Add(pnl); _tabControl.TabPages.Add(tab);
		}

		// ====== 运动参数 ======
		private void CreateMotionTab()
		{
			var tab = new TabPage { BackColor = Color.White, Text = "运动参数" };
			var pnl = new Panel { BackColor = Color.White, Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20) };
			int y = 20, cx = 180;
			AddLabel(pnl, "运动控制卡IP:", 20, y);
			_txtControlIp = new TextBox { Text = "192.168.0.11", Width = 150, Location = new Point(cx, y) };
			pnl.Controls.Add(_txtControlIp); y += 40;
			AddLabel(pnl, "侧面运动轴起点:", 20, y);
			_numStartPos = new NumericUpDown { Minimum = -1000, Maximum = 1000, Value = 0, Width = 100, Location = new Point(cx, y), DecimalPlaces = 1 };
			pnl.Controls.Add(_numStartPos); y += 40;
			AddLabel(pnl, "侧面运动轴终点:", 20, y);
			_numEndPos = new NumericUpDown { Minimum = -1000, Maximum = 1000, Value = 100, Width = 100, Location = new Point(cx, y), DecimalPlaces = 1 };
			pnl.Controls.Add(_numEndPos); y += 40;
			AddLabel(pnl, "侧面运动速度:", 20, y);
			_numMoveSpeed = new NumericUpDown { Minimum = 1, Maximum = 1000, Value = 20, Width = 100, Location = new Point(cx, y) };
			pnl.Controls.Add(_numMoveSpeed); y += 40;
			AddLabel(pnl, "侧面运动加速度:", 20, y);
			_numMoveAccel = new NumericUpDown { Minimum = 1000, Maximum = 100000, Increment = 1000, Value = 10000, Width = 100, Location = new Point(cx, y) };
			pnl.Controls.Add(_numMoveAccel); y += 45;
			// 安全锁
			AddLabel(pnl, "━━ 安全锁(传感器) ━━", 20, y); y += 30;
			AddLabel(pnl, "安全锁IN端口(0=禁用):", 20, y);
			_numSafetyLockPort = new NumericUpDown { Minimum = 0, Maximum = 32, Value = 8, Width = 80, Location = new Point(cx, y) };
			pnl.Controls.Add(_numSafetyLockPort); y += 40;
			_chkSafetyLockActiveHigh = AddCheckBox(pnl, "高电平=安全(低电平有效请取消勾选)", 20, ref y);
			AddLabel(pnl, "安全锁恢复模式:", 20, y);
			_cboSafetyLockRecovery = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 180, Location = new Point(cx, y) };
			_cboSafetyLockRecovery.Items.AddRange(new object[] { "继续执行(信号恢复后接着走)", "返回起始位(信号恢复后回起点)" });
			_cboSafetyLockRecovery.SelectedIndex = 0;
			pnl.Controls.Add(_cboSafetyLockRecovery); y += 40;
			AddLabel(pnl, "(安全锁: 读取传感器信号, 不安全时阻止运动轴移动并急停)", 20, y);
			tab.Controls.Add(pnl); _tabControl.TabPages.Add(tab);
		}

		// ====== 保存参数 ======
		private void CreateSaveTab()
		{
			var tab = new TabPage { BackColor = Color.White, Text = "保存参数" };
			var pnl = new Panel { BackColor = Color.White, Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20) };
			int y = 20, cx = 180;
			_chkSaveOkImage = AddCheckBox(pnl, "保存OK渲染图", 20, ref y);
			_chkSaveNgImage = AddCheckBox(pnl, "保存NG渲染图", 20, ref y);
			_chkSaveOkRaw = AddCheckBox(pnl, "保存OK原图", 20, ref y);
			_chkSaveNgRaw = AddCheckBox(pnl, "保存NG原图", 20, ref y);
			AddLabel(pnl, "JPEG压缩质量(1-100):", 20, y);
			_numJpegQuality = new NumericUpDown { Minimum = 1, Maximum = 100, Value = 85, Width = 80, Location = new Point(cx, y) };
			pnl.Controls.Add(_numJpegQuality); y += 40;
			AddLabel(pnl, "图片保存路径:", 20, y);
			_txtSavePath = new TextBox { Width = 300, Location = new Point(cx, y) };
			pnl.Controls.Add(_txtSavePath); y += 40;
			AddLabel(pnl, "保留天数:", 20, y);
			_numRetentionDays = new NumericUpDown { Minimum = 1, Maximum = 30, Value = 7, Width = 80, Location = new Point(cx, y) };
			pnl.Controls.Add(_numRetentionDays);
			tab.Controls.Add(pnl); _tabControl.TabPages.Add(tab);
		}

		// ====== 工位参数 ======
		private void CreateStationTab()
		{
			var tab = new TabPage { BackColor = Color.White, Text = "工位参数" };
			var pnl = new Panel { BackColor = Color.White, Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(20) };
			int y = 20, cx = 180;
			AddLabel(pnl, "工位启用:", 20, y); y += 25;
			_chkFrontEnable = AddCheckBox(pnl, "正面工位", 30, ref y);
			_chkEndFaceEnable = AddCheckBox(pnl, "端面工位", 30, ref y);
			_chkBackEnable = AddCheckBox(pnl, "背面工位(含日期码检测)", 30, ref y);
			_chkSideEnable = AddCheckBox(pnl, "侧面工位", 30, ref y);
			y += 10;
			AddLabel(pnl, "输入端口:", 20, y); y += 25;
			AddLabel(pnl, "正面到位(IN4):", 30, y);
			_numInPortFront = new NumericUpDown { Minimum = 0, Maximum = 20, Value = 4, Width = 60, Location = new Point(cx, y - 3) };
			pnl.Controls.Add(_numInPortFront); y += 30;
			AddLabel(pnl, "端面到位(IN10):", 30, y);
			_numInPortEndFace = new NumericUpDown { Minimum = 0, Maximum = 20, Value = 10, Width = 60, Location = new Point(cx, y - 3) };
			pnl.Controls.Add(_numInPortEndFace); y += 30;
			AddLabel(pnl, "背面到位(IN11):", 30, y);
			_numInPortBack = new NumericUpDown { Minimum = 0, Maximum = 20, Value = 11, Width = 60, Location = new Point(cx, y - 3) };
			pnl.Controls.Add(_numInPortBack); y += 30;
			AddLabel(pnl, "侧面触发(IN12):", 30, y);
			_numInPortSideTrigger = new NumericUpDown { Minimum = 0, Maximum = 20, Value = 12, Width = 60, Location = new Point(cx, y - 3) };
			pnl.Controls.Add(_numInPortSideTrigger); y += 30;
			AddLabel(pnl, "侧面到位(IN13):", 30, y);
			_numInPortSideReady = new NumericUpDown { Minimum = 0, Maximum = 20, Value = 13, Width = 60, Location = new Point(cx, y - 3) };
			pnl.Controls.Add(_numInPortSideReady);
			tab.Controls.Add(pnl); _tabControl.TabPages.Add(tab);
		}


		// ====== 辅助方法 ======
		private void AddLabel(Control parent, string text, int x, int y)
		{
			var lbl = new Label { BackColor = Color.Transparent, Text = text, Location = new Point(x, y), Size = new Size(160, 25), TextAlign = ContentAlignment.MiddleLeft };
			parent.Controls.Add(lbl);
		}

		private CheckBox AddCheckBox(Control parent, string text, int x, ref int y)
		{
			var cb = new CheckBox { Text = text, Location = new Point(x, y), AutoSize = true };
			parent.Controls.Add(cb);
			y += 30;
			return cb;
		}

		private void AddTrackBar(Control parent, string label, int x, ref int y, int min, int max, int val, out TrackBar outTb, out Label outLbl, string fmt)
		{
			AddLabel(parent, label, x, y);
			var tb = new TrackBar { Minimum = min, Maximum = max, Value = val, Width = 200, Location = new Point(180, y - 5) };
			double dv = (double)val / 100.0;
			var lbl = new Label { BackColor = Color.Transparent, Text = dv.ToString(fmt), Location = new Point(390, y + 3), AutoSize = true };
			tb.ValueChanged += (ss, ee) => lbl.Text = (tb.Value / 100.0).ToString(fmt);
			parent.Controls.Add(tb);
			parent.Controls.Add(lbl);
			outTb = tb;
			outLbl = lbl;
			y += 40;
		}

			private int ClampTrack(int val, int min, int max) { if (val < min) return min; if (val > max) return max; return val; }

		// ====== 从JSON加载参数到UI ======
		private void LoadParameters()
		/// <summary>从DetectionParams加载参数到各Tab控件: 遍历所有TrackBar/NumericUpDown/CheckBox/TextBox/ComboBox设置值</summary>
		{
			// 正面
			_chkEnablePNumberCheck.Checked = _params.Front.EnablePNumberCheck;
				_chkEnableBoxBreakCheck.Checked = _params.Front.EnableBoxBreakCheck;
			_trackPCodeStartRatio.Value = (int)(_frontPcodeParams.StartHeightRatioPCode * 100);
			var fbParams = Config.ModelParams.Load("front_box");
			_trackFrontBoxConf.Value = ClampTrack((int)(fbParams.Confidence * 100), 1, 100);
			_trackFrontBoxIou.Value = ClampTrack((int)(fbParams.Iou * 100), 1, 100);

			// 背面-条码
			_chkBcEnablePreprocess.Checked = _barcodeParams.BcEnablePreprocess;
				_chkEnableBarcodeCheck.Checked = _params.Back.EnableBarcodeCheck;
			_trackBcContrastAlpha.Value = (int)(_barcodeParams.BcContrastAlpha * 10);
			_trackBcBrightnessBeta.Value = _barcodeParams.BcBrightnessBeta;
			_chkBcGaussianBlur.Checked = _barcodeParams.BcEnableGaussianBlur;
			_chkBcMedianBlur.Checked = _barcodeParams.BcEnableMedianBlur;
			_chkBcEqualizeHist.Checked = _barcodeParams.BcEnableEqualizeHist;
			_cboBcThresholdMode.SelectedIndex = _barcodeParams.BcThresholdMode;
			_numBcAdaptiveBlockSize.Value = _barcodeParams.BcAdaptiveBlockSize;
			_numBcAdaptiveC.Value = (decimal)_barcodeParams.BcAdaptiveC;
			_numBcFixedThreshold.Value = _barcodeParams.BcFixedThreshold;
			_chkBcInvert.Checked = _barcodeParams.BcEnableInvert;
			_chkBcMorphClose.Checked = _barcodeParams.BcEnableMorphClose;
			_chkBcMorphOpen.Checked = _barcodeParams.BcEnableMorphOpen;
			_chkBcMorphDilate.Checked = _barcodeParams.BcEnableMorphDilate;
			_chkBcMorphErode.Checked = _barcodeParams.BcEnableMorphErode;
			_trackBcStartRatio.Value = (int)(_barcodeParams.BcStartHeightRatio * 100);
			_chkBcFilterBestMatch.Checked = _barcodeParams.BcEnableFilterBestMatch;
			_chkBcTryHarder.Checked = _barcodeParams.BcTryHarder;
			_chkBcRotationRetry.Checked = _barcodeParams.BcEnableRotationRetry;
			_numBcMinLength.Value = _barcodeParams.BcMinBarcodeLength;
			_numBcMaxLength.Value = _barcodeParams.BcMaxBarcodeLength;

			// 背面-日期码
			_trackDcStartRatio.Value = (int)(_datecodeParams.StartHeightRatioDateCode * 100);
			// 底部边界(从顶算) = 1 - 裁底比例, 滑块向右=保留更多
			_trackDcBottomRatio.Value = (int)((1.0 - _datecodeParams.DateCodeCropBottomRatio) * 100);

			// 背面-挂钩
			_numHookThickness.Value = (decimal)_hookParams.HookThickness;
				_chkEnableHookCheck.Checked = _params.Back.EnableHookCheck;
			_numHookBlueClassId.Value = _hookParams.HookBlueClassId;
			_numHookHoleClassId.Value = _hookParams.HookHoleClassId;
			_trackHookConf.Value = ClampTrack((int)(_hookParams.Confidence * 100), 1, 100);
			_trackHookIou.Value = ClampTrack((int)(_hookParams.Iou * 100), 1, 100);

			// 端面
			_numEndFaceExposure.Value = _params.EndFace.ExposureMs;
				_chkEnableUpperDefectCheck.Checked = _params.EndFace.EnableUpperDefectCheck;
			_numEndFaceFontDefect.Value = _endfaceUpperParams.DrawFontDefect > 0 ? _endfaceUpperParams.DrawFontDefect : 18;
			_numEndFaceFontStatus.Value = _endfaceUpperParams.DrawFontStatus > 0 ? _endfaceUpperParams.DrawFontStatus : 48;
			_trackEndFaceConf.Value = ClampTrack((int)(_endfaceUpperParams.EndFaceUpperConf * 100), 1, 100);
			_trackEndFaceIou.Value = ClampTrack((int)(_endfaceUpperParams.EndFaceUpperIou * 100), 1, 100);

			// 侧面
			_trackSideCropRatio.Value = (int)(_sideParams.SideCropRatio * 10);
			_chkSideMotionEnabled.Checked = _params.Side.MotionEnabled;
				_chkEnableSideDefectCheck.Checked = _params.Side.EnableSideDefectCheck;
			_chkSideContinuousMode.Checked = _params.Side.UseContinuousMode;
			_chkSideMissingAsNg.Checked = _params.Side.MissingAsNg;
			_cboSideEdgeMode.SelectedIndex = _params.Side.TriggerEdgeMode == "RisingLeftFallingRight" ? 0 : 1;
			_trackSideConf.Value = ClampTrack((int)(_sideParams.SideConf * 100), 1, 100);
			_trackSideIou.Value = ClampTrack((int)(_sideParams.SideIou * 100), 1, 100);

			// 字号
			_numFontBarcode.Value = _barcodeParams.DrawFontBarcode > 0 ? _barcodeParams.DrawFontBarcode : 28;
			_numFontDefect.Value = _barcodeParams.DrawFontDefect > 0 ? _barcodeParams.DrawFontDefect : 18;
			_numFontStatus.Value = _barcodeParams.DrawFontStatus > 0 ? _barcodeParams.DrawFontStatus : 48;
			_numFontBoxNum.Value = _barcodeParams.DrawFontBoxNum > 0 ? _barcodeParams.DrawFontBoxNum : 28;

			// 相机
			try { _numPulseWidth.Value = _params.Camera.PulseWidthMs; } catch { }
			if (_txtCamera1SN != null) _txtCamera1SN.Text = _params.Camera.Camera1SN;
			if (_txtCamera2SN != null) _txtCamera2SN.Text = _params.Camera.Camera2SN;
			if (_txtCamera3SN != null) _txtCamera3SN.Text = _params.Camera.Camera3SN;
			if (_txtCamera4SN != null) _txtCamera4SN.Text = _params.Camera.Camera4SN;
			if (_txtCamera5SN != null) _txtCamera5SN.Text = _params.Camera.Camera5SN;
			if (_txtCamera6SN != null) _txtCamera6SN.Text = _params.Camera.Camera6SN;
			if (_txtCamera7SN != null) _txtCamera7SN.Text = _params.Camera.Camera7SN;
			if (_txtCamera8SN != null) _txtCamera8SN.Text = _params.Camera.Camera8SN;

			// 运动
			_txtControlIp.Text = _params.Motion.ControlIp;
			_numStartPos.Value = (decimal)_params.Motion.SideStartPosition;
			_numEndPos.Value = (decimal)_params.Motion.SideEndPosition;
			_numMoveSpeed.Value = _params.Motion.SideMoveSpeed;
			_numMoveAccel.Value = _params.Motion.SideMoveAccel;

				// 安全锁
				_numSafetyLockPort.Value = _params.Side.SafetyLockPort;
				_chkSafetyLockActiveHigh.Checked = _params.Side.SafetyLockActiveHigh;
				_cboSafetyLockRecovery.SelectedIndex = _params.Side.SafetyLockRecovery;
			// 保存
			_chkSaveOkImage.Checked = _params.Save.SaveOkImage;
			_chkSaveNgImage.Checked = _params.Save.SaveNgImage;
			_chkSaveOkRaw.Checked = _params.Save.SaveOkRawImage;
			_chkSaveNgRaw.Checked = _params.Save.SaveNgRawImage;
			_numJpegQuality.Value = _params.Save.JpegQuality;
			_txtSavePath.Text = _params.Save.ImageSavePath;
			_numRetentionDays.Value = _params.Save.RetentionDays;

			// 工位
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


		// ====== 保存参数到JSON并通知重载 ======
		private void SaveParameters()
		/// <summary>保存各Tab参数到DetectionParams: 从控件读取→赋值给_params子类→保存ModelParams到各best.json→保存AxisParams到AxisParams.json</summary>
		{
			// ── 1. 写入 ModelParams JSON ──
			// barcode
			_barcodeParams.BcEnablePreprocess = _chkBcEnablePreprocess.Checked;
			_barcodeParams.BcContrastAlpha = _trackBcContrastAlpha.Value / 10f;
			_barcodeParams.BcBrightnessBeta = _trackBcBrightnessBeta.Value;
			_barcodeParams.BcEnableGaussianBlur = _chkBcGaussianBlur.Checked;
			_barcodeParams.BcEnableMedianBlur = _chkBcMedianBlur.Checked;
			_barcodeParams.BcEnableEqualizeHist = _chkBcEqualizeHist.Checked;
			_barcodeParams.BcThresholdMode = _cboBcThresholdMode.SelectedIndex;
			_barcodeParams.BcAdaptiveBlockSize = (int)_numBcAdaptiveBlockSize.Value;
			_barcodeParams.BcAdaptiveC = (double)_numBcAdaptiveC.Value;
			_barcodeParams.BcFixedThreshold = (int)_numBcFixedThreshold.Value;
			_barcodeParams.BcEnableInvert = _chkBcInvert.Checked;
			_barcodeParams.BcEnableMorphClose = _chkBcMorphClose.Checked;
			_barcodeParams.BcEnableMorphOpen = _chkBcMorphOpen.Checked;
			_barcodeParams.BcEnableMorphDilate = _chkBcMorphDilate.Checked;
			_barcodeParams.BcEnableMorphErode = _chkBcMorphErode.Checked;
			_barcodeParams.BcStartHeightRatio = _trackBcStartRatio.Value / 100.0;
			_barcodeParams.BcEnableFilterBestMatch = _chkBcFilterBestMatch.Checked;
			_barcodeParams.BcTryHarder = _chkBcTryHarder.Checked;
			_barcodeParams.BcEnableRotationRetry = _chkBcRotationRetry.Checked;
			_barcodeParams.BcMinBarcodeLength = (int)_numBcMinLength.Value;
			_barcodeParams.BcMaxBarcodeLength = (int)_numBcMaxLength.Value;
			_barcodeParams.DrawFontBarcode = (int)_numFontBarcode.Value;
			_barcodeParams.DrawFontDefect = (int)_numFontDefect.Value;
			_barcodeParams.DrawFontStatus = (int)_numFontStatus.Value;
			_barcodeParams.DrawFontBoxNum = (int)_numFontBoxNum.Value;
			_barcodeParams.Save();

		// datecode — 防呆: 有效区域=底部边界-裁顶, 必须≥5%
			double dcTop = _trackDcStartRatio.Value / 100.0;
			double dcBottomEnd = _trackDcBottomRatio.Value / 100.0; // 滑块值=底部边界(从顶算)
			double effectiveRegion = dcBottomEnd - dcTop;
			if (effectiveRegion < 0.05)
			{
				MessageBox.Show($"日期码有效区域太小: 裁顶={dcTop:F2}, 底部边界={dcBottomEnd:F2}, 有效区域={effectiveRegion:F2}\n请调整使有效区域至少5%。",
					"参数错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
				return;
			}
			_datecodeParams.StartHeightRatioDateCode = dcTop;
			_datecodeParams.DateCodeCropBottomRatio = 1.0 - dcBottomEnd; // 存为裁底比例
			_datecodeParams.Save();

			// front_pcode
			_frontPcodeParams.StartHeightRatioPCode = _trackPCodeStartRatio.Value / 100.0;
			_frontPcodeParams.Save();

			// front_box (盒子破YOLO阈值)
			var fbParams = Config.ModelParams.Load("front_box");
			fbParams.Confidence = _trackFrontBoxConf.Value / 100f;
			fbParams.Iou = _trackFrontBoxIou.Value / 100f;
			fbParams.Save();

			// hook
			_hookParams.HookThickness = (float)_numHookThickness.Value;
			_hookParams.HookBlueClassId = (int)_numHookBlueClassId.Value;
			_hookParams.HookHoleClassId = (int)_numHookHoleClassId.Value;
			_hookParams.Confidence = _trackHookConf.Value / 100f;
			_hookParams.Iou = _trackHookIou.Value / 100f;
			_hookParams.Save();

			// side
			_sideParams.SideCropRatio = _trackSideCropRatio.Value / 10f;
			_sideParams.SideConf = _trackSideConf.Value / 100f;
			_sideParams.SideIou = _trackSideIou.Value / 100f;
			_sideParams.Save();

			// endface_upper
			_endfaceUpperParams.DrawFontDefect = (int)_numEndFaceFontDefect.Value;
			_endfaceUpperParams.DrawFontStatus = (int)_numEndFaceFontStatus.Value;
			_endfaceUpperParams.EndFaceUpperConf = _trackEndFaceConf.Value / 100f;
			_endfaceUpperParams.EndFaceUpperIou = _trackEndFaceIou.Value / 100f;
			_endfaceUpperParams.Save();

			// ── 2. 写入 DetectionParams.json ──
			_params.Front.EnablePNumberCheck = _chkEnablePNumberCheck.Checked;
				_params.Front.EnableBoxBreakCheck = _chkEnableBoxBreakCheck.Checked;
			_params.Back.EnableDateCodeCheck = _chkBackEnable.Checked; // 背面工位启用=日期码启用
				_params.Back.EnableBarcodeCheck = _chkEnableBarcodeCheck.Checked;
				_params.Back.EnableHookCheck = _chkEnableHookCheck.Checked;
			_params.EndFace.ExposureMs = (int)_numEndFaceExposure.Value;
				_params.EndFace.EnableUpperDefectCheck = _chkEnableUpperDefectCheck.Checked;

			_params.Side.CropRatio = _trackSideCropRatio.Value / 10f;
			_params.Side.MotionEnabled = _chkSideMotionEnabled.Checked;
				_params.Side.EnableSideDefectCheck = _chkEnableSideDefectCheck.Checked;
			_params.Side.UseContinuousMode = _chkSideContinuousMode.Checked;
			_params.Side.MissingAsNg = _chkSideMissingAsNg.Checked;
			_params.Side.TriggerEdgeMode = _cboSideEdgeMode.SelectedIndex == 0 ? "RisingLeftFallingRight" : "RisingRightFallingLeft";
			_params.Side.SafetyLockPort = (int)_numSafetyLockPort.Value;
			_params.Side.SafetyLockActiveHigh = _chkSafetyLockActiveHigh.Checked;
			_params.Side.SafetyLockRecovery = _cboSafetyLockRecovery.SelectedIndex;
			_params.Camera.PulseWidthMs = (int)_numPulseWidth.Value;
			if (_txtCamera1SN != null) { _params.Camera.Camera1SN = _txtCamera1SN.Text; _params.Camera.Camera2SN = _txtCamera2SN.Text; _params.Camera.Camera3SN = _txtCamera3SN.Text; _params.Camera.Camera4SN = _txtCamera4SN.Text; _params.Camera.Camera5SN = _txtCamera5SN.Text; _params.Camera.Camera6SN = _txtCamera6SN.Text; _params.Camera.Camera7SN = _txtCamera7SN.Text; _params.Camera.Camera8SN = _txtCamera8SN.Text; }

			_params.Motion.ControlIp = _txtControlIp.Text;
			_params.Motion.SideStartPosition = (float)_numStartPos.Value;
			_params.Motion.SideEndPosition = (float)_numEndPos.Value;
			_params.Motion.SideMoveSpeed = (int)_numMoveSpeed.Value;
			_params.Motion.SideMoveAccel = (int)_numMoveAccel.Value;

			// 同步运动参数到AxisParams.json (运行时实际使用的配置文件)
			try {
				var axisCfg = AxisParamConfig.Load();
				axisCfg.StartPos = (float)_numStartPos.Value;
				axisCfg.EndPos = (float)_numEndPos.Value;
				// FwdSpeed/RetSpeed 仅通过调试界面(ControlFrm)修改，此处不再覆盖
				axisCfg.Accel = (int)_numMoveAccel.Value;
				axisCfg.SafetyLockPort = (int)_numSafetyLockPort.Value;
				axisCfg.SafetyLockActiveHigh = _chkSafetyLockActiveHigh.Checked;
				axisCfg.SafetyLockRecovery = _cboSafetyLockRecovery.SelectedIndex;
				axisCfg.Save();
				Logger.Info("运动/安全锁参数已同步到AxisParams.json");
			} catch (Exception ex) { Logger.Error("同步AxisParams.json失败: " + ex.Message); }

			_params.Save.SaveOkImage = _chkSaveOkImage.Checked;
			_params.Save.SaveNgImage = _chkSaveNgImage.Checked;
			_params.Save.SaveOkRawImage = _chkSaveOkRaw.Checked;
			_params.Save.SaveNgRawImage = _chkSaveNgRaw.Checked;
			_params.Save.JpegQuality = (int)_numJpegQuality.Value;
			_params.Save.ImageSavePath = _txtSavePath.Text;
			_params.Save.RetentionDays = (int)_numRetentionDays.Value;

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

			// ── 3. 通知MainFrm重新加载 ──
			OnParametersChanged?.Invoke(this, EventArgs.Empty);

			MessageBox.Show("参数已保存！\r\n- AI检测参数(ModelParams) -> 实时生效, 无需重启\r\n- 相机/运动/工位配置 -> 部分需重启", "保存成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
			Logger.Info("检测参数已保存到ModelParams+DetectionParams");
		}

		private void BtnSave_Click(object sender, EventArgs e)
		/// <summary>保存并应用检测参数: 从各Tab控件读取值→更新_params→SaveToFile写入DetectionParams.json→触发OnParametersChanged热更新(无需重启软件)</summary>
		{
			SaveParameters();
			this.DialogResult = DialogResult.OK;
			this.Close();
		}

		private void BtnReset_Click(object sender, EventArgs e)
		/// <summary>重置所有参数为默认值: 重新new各子类→保存→重新LoadModelParams→重新LoadParameters刷新UI</summary>
		{
			if (MessageBox.Show("确认重置所有参数为默认值？", "确认", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
			{
				_params.ResetToDefault();
				_barcodeParams = Config.ModelParams.CreateDefault("barcode", "barcode");
				_datecodeParams = Config.ModelParams.CreateDefault("datecode", "datecode");
				_frontPcodeParams = Config.ModelParams.CreateDefault("front_pcode", "front_pcode");
				_hookParams = Config.ModelParams.CreateDefault("hook", "hook");
				_sideParams = Config.ModelParams.CreateDefault("side", "side");
				_endfaceUpperParams = Config.ModelParams.CreateDefault("endface_upper", "endface_upper");
				LoadParameters();
				MessageBox.Show("参数已重置，请点击保存生效。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
			}
		}

		private void BtnExport_Click(object sender, EventArgs e)
		{
			using (var dlg = new SaveFileDialog { Filter = "JSON文件|*.json", FileName = "DetectionParams_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".json" })
			{
				if (dlg.ShowDialog() == DialogResult.OK)
				{
					System.IO.File.WriteAllText(dlg.FileName, _params.ExportToJson());
					MessageBox.Show("配置已导出到: " + dlg.FileName, "导出成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
				}
			}
		}

		private void BtnImport_Click(object sender, EventArgs e)
		{
			using (var dlg = new OpenFileDialog { Filter = "JSON文件|*.json" })
			{
				if (dlg.ShowDialog() == DialogResult.OK)
				{
					string json = System.IO.File.ReadAllText(dlg.FileName);
					if (_params.ImportFromJson(json))
					{
						LoadModelParams();
						LoadParameters();
						MessageBox.Show("配置导入成功！", "导入成功", MessageBoxButtons.OK, MessageBoxIcon.Information);
					}
					else MessageBox.Show("配置导入失败！", "导入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
				}
			}
		}
	}
}
