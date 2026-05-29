using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace VisionMeasure.From
{
    public class BarcodeTestForm : Form
    {
        private Bitmap _leftImage;
        private Bitmap _rightImage;
        private Bitmap _leftAnnotated;
        private Bitmap _rightAnnotated;

        // 图像加载与预览
        private PictureBox picLeft, picRight;
        private Label lblLeftPath, lblRightPath;
        private Button btnLoadLeft, btnLoadRight;

        // 结果展示 —— 可缩放拖拽的自定义控件
        private ZoomablePictureBox pbLeftResult, pbRightResult;

        // 参数配置
        private TextBox txtRefBarcode;
        private NumericUpDown nudP;
        private NumericUpDown nudStartRatio, nudMinBarcodeLen, nudMaxBarcodeLen;
        private CheckBox chkFilterBestMatch, chkTryHarder, chkRotationRetry;
        private CheckBox chkEnablePreprocess;
        private NumericUpDown nudContrast, nudBrightness, nudAdaptiveBlock, nudAdaptiveC, nudFixedThresh;
        private Label lblContrastVal, lblBrightnessVal, lblFixedThreshVal;
        private CheckBox chkGaussianBlur, chkMedianBlur, chkEqualizeHist, chkInvert;
        private CheckBox chkMorphClose, chkMorphOpen, chkMorphDilate, chkMorphErode;
        private ComboBox cmbThreshold;

        // 执行与结果
        private Button btnExecute;
        private TextBox txtLog;
        private Label lblElapsed;
        private DataGridView dgvResults;

        // 条码绘制色盘
        private static readonly Color[] BarcodeColors = {
            Color.Lime, Color.Orange, Color.Cyan, Color.Yellow,
            Color.Magenta, Color.DeepSkyBlue, Color.Red, Color.Gold
        };

        public BarcodeTestForm()
        {
            BuildUI();
        }

        private void BuildUI()
        {
            this.Text = "BarcodeChecker 测试工具";
            this.Size = new Size(1500, 950);
            this.StartPosition = FormStartPosition.CenterScreen;
            this.Font = new Font("Microsoft YaHei", 9F);

            var mainLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(8) };
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 520));
            mainLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            this.Controls.Add(mainLayout);

            // ========== 左面板: 参数配置 ==========
            var leftPanel = new Panel { Dock = DockStyle.Fill, AutoScroll = true, Padding = new Padding(6) };
            mainLayout.Controls.Add(leftPanel, 0, 0);

            int y = 0;
            int cardW = 490;

            // ---- 图像加载 ----
            var grpImage = CreateGroupBox("图像加载", ref y, cardW, 230);
            leftPanel.Controls.Add(grpImage);

            btnLoadLeft = new Button { Text = "加载左图", Size = new Size(100, 30), Location = new Point(10, 20) };
            btnLoadLeft.Click += BtnLoadLeft_Click;
            btnLoadRight = new Button { Text = "加载右图", Size = new Size(100, 30), Location = new Point(120, 20) };
            btnLoadRight.Click += BtnLoadRight_Click;

            picLeft = new PictureBox { Size = new Size(220, 150), Location = new Point(10, 55), BorderStyle = BorderStyle.FixedSingle, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
            picRight = new PictureBox { Size = new Size(220, 150), Location = new Point(240, 55), BorderStyle = BorderStyle.FixedSingle, SizeMode = PictureBoxSizeMode.Zoom, BackColor = Color.Black };
            lblLeftPath = new Label { Text = "未加载", Size = new Size(220, 20), Location = new Point(10, 208), ForeColor = Color.Gray };
            lblRightPath = new Label { Text = "未加载", Size = new Size(220, 20), Location = new Point(240, 208), ForeColor = Color.Gray };

            grpImage.Controls.AddRange(new Control[] { btnLoadLeft, btnLoadRight, picLeft, picRight, lblLeftPath, lblRightPath });

            // ---- 基础参数 ----
            var grpBase = CreateGroupBox("基础检测参数", ref y, cardW, 200);
            leftPanel.Controls.Add(grpBase);

            var lblRef = new Label { Text = "基准条码:", Size = new Size(80, 25), Location = new Point(10, 22), TextAlign = ContentAlignment.MiddleRight };
            txtRefBarcode = new TextBox { Text = "REF123456", Size = new Size(180, 25), Location = new Point(95, 22) };

            var lblP = new Label { Text = "盒子数 P:", Size = new Size(80, 25), Location = new Point(10, 50), TextAlign = ContentAlignment.MiddleRight };
            nudP = new NumericUpDown { Minimum = 2, Maximum = 100, Value = 4, Increment = 2, Size = new Size(70, 25), Location = new Point(95, 50) };

            var lblStartRatio = new Label { Text = "裁剪起始:", Size = new Size(80, 25), Location = new Point(10, 78), TextAlign = ContentAlignment.MiddleRight };
            nudStartRatio = new NumericUpDown { Minimum = 0, Maximum = 100, Value = 67, DecimalPlaces = 0, Size = new Size(65, 25), Location = new Point(95, 78) };
            var lblStartRatioHint = new Label { Text = "% (0=整图, 67=底部1/3)", Size = new Size(210, 20), Location = new Point(163, 80), ForeColor = Color.Gray, Font = new Font("Microsoft YaHei", 8F) };

            chkEnablePreprocess = new CheckBox { Text = "启用 OpenCV 预处理", Checked = true, Size = new Size(160, 25), Location = new Point(300, 78) };
            chkEnablePreprocess.CheckedChanged += (s, e) => TogglePreprocessControls();

            // 条码智能过滤
            chkFilterBestMatch = new CheckBox { Text = "仅保留最佳匹配 (过滤幻读)", Checked = true, Size = new Size(190, 25), Location = new Point(10, 108) };
            var lblMinLen = new Label { Text = "最小长度:", Size = new Size(70, 25), Location = new Point(10, 138), TextAlign = ContentAlignment.MiddleRight };
            nudMinBarcodeLen = new NumericUpDown { Minimum = 1, Maximum = 20, Value = 3, Size = new Size(50, 25), Location = new Point(83, 138) };
            var lblMaxLen = new Label { Text = "最大长度:", Size = new Size(70, 25), Location = new Point(140, 138), TextAlign = ContentAlignment.MiddleRight };
            nudMaxBarcodeLen = new NumericUpDown { Minimum = 5, Maximum = 200, Value = 50, Size = new Size(50, 25), Location = new Point(213, 138) };

            chkTryHarder = new CheckBox { Text = "TryHarder 深搜 (慢但全)", Checked = false, Size = new Size(200, 25), Location = new Point(10, 168) };
            chkRotationRetry = new CheckBox { Text = "90°旋转重试 (慢但稳)", Checked = false, Size = new Size(200, 25), Location = new Point(210, 168) };

            grpBase.Controls.AddRange(new Control[] { lblRef, txtRefBarcode, lblP, nudP,
                lblStartRatio, nudStartRatio, lblStartRatioHint, chkEnablePreprocess,
                chkFilterBestMatch, lblMinLen, nudMinBarcodeLen, lblMaxLen, nudMaxBarcodeLen,
                chkTryHarder, chkRotationRetry });

            // ---- 图像增强 ----
            var grpEnhance = CreateGroupBox("图像增强 (对比度/亮度/滤波)", ref y, cardW, 130);
            leftPanel.Controls.Add(grpEnhance);

            var lblContrast = new Label { Text = "对比度:", Size = new Size(70, 25), Location = new Point(10, 22), TextAlign = ContentAlignment.MiddleRight };
            nudContrast = new NumericUpDown { Minimum = 1.0M, Maximum = 3.0M, Value = 1.0M, DecimalPlaces = 1, Increment = 0.1M, Size = new Size(60, 25), Location = new Point(85, 22) };
            lblContrastVal = new Label { Text = "1.0", Size = new Size(40, 25), Location = new Point(148, 22) };
            nudContrast.ValueChanged += (s, e) => lblContrastVal.Text = nudContrast.Value.ToString("F1");

            var lblBrightness = new Label { Text = "亮度:", Size = new Size(70, 25), Location = new Point(200, 22), TextAlign = ContentAlignment.MiddleRight };
            nudBrightness = new NumericUpDown { Minimum = -100, Maximum = 100, Value = 0, Size = new Size(60, 25), Location = new Point(275, 22) };
            lblBrightnessVal = new Label { Text = "0", Size = new Size(40, 25), Location = new Point(338, 22) };
            nudBrightness.ValueChanged += (s, e) => lblBrightnessVal.Text = nudBrightness.Value.ToString();

            chkGaussianBlur = new CheckBox { Text = "高斯平滑", Checked = true, Size = new Size(85, 25), Location = new Point(10, 52) };
            chkMedianBlur = new CheckBox { Text = "中值去噪", Size = new Size(85, 25), Location = new Point(100, 52) };
            chkEqualizeHist = new CheckBox { Text = "直方均衡", Size = new Size(85, 25), Location = new Point(190, 52) };
            chkInvert = new CheckBox { Text = "反色", Size = new Size(80, 25), Location = new Point(280, 52) };

            grpEnhance.Controls.AddRange(new Control[] { lblContrast, nudContrast, lblContrastVal,
                lblBrightness, nudBrightness, lblBrightnessVal, chkGaussianBlur, chkMedianBlur, chkEqualizeHist, chkInvert });

            // ---- 二值化 ----
            var grpThresh = CreateGroupBox("二值化阈值", ref y, cardW, 110);
            leftPanel.Controls.Add(grpThresh);

            var lblThreshMode = new Label { Text = "模式:", Size = new Size(45, 25), Location = new Point(10, 22), TextAlign = ContentAlignment.MiddleRight };
            cmbThreshold = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Size = new Size(120, 25), Location = new Point(60, 22) };
            cmbThreshold.Items.AddRange(new[] { "无(None)", "自适应(Adaptive)", "大津(Otsu)", "固定(Fixed)" });
            cmbThreshold.SelectedIndex = 1;

            var lblABlock = new Label { Text = "邻域块:", Size = new Size(65, 25), Location = new Point(10, 50), TextAlign = ContentAlignment.MiddleRight };
            nudAdaptiveBlock = new NumericUpDown { Minimum = 3, Maximum = 99, Value = 11, Increment = 2, Size = new Size(55, 25), Location = new Point(80, 50) };
            var lblAC = new Label { Text = "常数C:", Size = new Size(55, 25), Location = new Point(145, 50), TextAlign = ContentAlignment.MiddleRight };
            nudAdaptiveC = new NumericUpDown { Minimum = -20, Maximum = 20, Value = 2, Size = new Size(55, 25), Location = new Point(200, 50) };

            var lblFixThresh = new Label { Text = "固定阈值:", Size = new Size(75, 25), Location = new Point(10, 78), TextAlign = ContentAlignment.MiddleRight };
            nudFixedThresh = new NumericUpDown { Minimum = 0, Maximum = 255, Value = 128, Size = new Size(55, 25), Location = new Point(90, 78) };
            lblFixedThreshVal = new Label { Text = "128", Size = new Size(40, 25), Location = new Point(148, 78) };
            nudFixedThresh.ValueChanged += (s, e) => lblFixedThreshVal.Text = nudFixedThresh.Value.ToString();

            grpThresh.Controls.AddRange(new Control[] { lblThreshMode, cmbThreshold,
                lblABlock, nudAdaptiveBlock, lblAC, nudAdaptiveC,
                lblFixThresh, nudFixedThresh, lblFixedThreshVal });

            // ---- 形态学 ----
            var grpMorph = CreateGroupBox("形态学操作", ref y, cardW, 55);
            leftPanel.Controls.Add(grpMorph);

            chkMorphClose = new CheckBox { Text = "闭运算(合裂线)", Checked = true, Size = new Size(130, 25), Location = new Point(10, 22) };
            chkMorphOpen = new CheckBox { Text = "开运算(消杂点)", Size = new Size(130, 25), Location = new Point(145, 22) };
            chkMorphDilate = new CheckBox { Text = "膨胀", Size = new Size(80, 25), Location = new Point(280, 22) };
            chkMorphErode = new CheckBox { Text = "腐蚀", Size = new Size(80, 25), Location = new Point(360, 22) };

            grpMorph.Controls.AddRange(new Control[] { chkMorphClose, chkMorphOpen, chkMorphDilate, chkMorphErode });

            // ---- 执行按钮 ----
            y += 8;
            btnExecute = new Button
            {
                Text = "执行检测",
                Size = new Size(cardW, 40),
                Location = new Point(8, y),
                BackColor = Color.FromArgb(122, 0, 245),
                ForeColor = Color.White,
                Font = new Font("Microsoft YaHei", 11F, FontStyle.Bold),
                FlatStyle = FlatStyle.Flat
            };
            btnExecute.FlatAppearance.BorderSize = 0;
            btnExecute.Click += BtnExecute_Click;
            leftPanel.Controls.Add(btnExecute);
            y += 48;

            lblElapsed = new Label { Text = "", Size = new Size(cardW, 25), Location = new Point(8, y), ForeColor = Color.Gray };
            leftPanel.Controls.Add(lblElapsed);

            // ========== 右面板: 结果展示 ==========
            var rightPanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(4) };
            rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 48));
            rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 22));
            rightPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 30));
            mainLayout.Controls.Add(rightPanel, 1, 0);

            // ---- 结果图像: 左右并排，使用可缩放控件 ----
            var resultImagePanel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(2) };
            resultImagePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            resultImagePanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            rightPanel.Controls.Add(resultImagePanel, 0, 0);

            var pnlLeftRes = CreateResultPanel("左图检测结果", out pbLeftResult);
            var pnlRightRes = CreateResultPanel("右图检测结果", out pbRightResult);
            resultImagePanel.Controls.Add(pnlLeftRes, 0, 0);
            resultImagePanel.Controls.Add(pnlRightRes, 1, 0);

            // ---- 结果表格 ----
            dgvResults = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill };
            rightPanel.Controls.Add(dgvResults, 0, 1);

            // ---- 日志 ----
            txtLog = new TextBox { Dock = DockStyle.Fill, Multiline = true, ScrollBars = ScrollBars.Vertical, BackColor = Color.FromArgb(12, 14, 20), ForeColor = Color.Lime, Font = new Font("Consolas", 10F), ReadOnly = true };
            rightPanel.Controls.Add(txtLog, 0, 2);
        }

        private Panel CreateResultPanel(string title, out ZoomablePictureBox zoomBox)
        {
            var panel = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) };
            var lbl = new Label { Text = title, ForeColor = Color.White, BackColor = Color.FromArgb(122, 0, 245), Dock = DockStyle.Top, Height = 24, TextAlign = ContentAlignment.MiddleCenter, Font = new Font("Consolas", 9F, FontStyle.Bold) };
            zoomBox = new ZoomablePictureBox { Dock = DockStyle.Fill, BackColor = Color.FromArgb(15, 15, 15) };
            panel.Controls.Add(zoomBox);
            panel.Controls.Add(lbl);
            return panel;
        }

        private GroupBox CreateGroupBox(string title, ref int y, int width, int height)
        {
            var gb = new GroupBox
            {
                Text = title,
                Size = new Size(width, height),
                Location = new Point(8, y),
                Font = new Font("Microsoft YaHei", 9F, FontStyle.Bold)
            };
            y += height + 8;
            return gb;
        }

        private void TogglePreprocessControls()
        {
            bool enabled = chkEnablePreprocess.Checked;
            foreach (Control c in this.Controls)
                SetPreprocessEnabled(c, enabled);
            chkEnablePreprocess.Enabled = true;
            btnExecute.Enabled = true;
        }

        private void SetPreprocessEnabled(Control parent, bool enabled)
        {
            foreach (Control c in parent.Controls)
            {
                if (c is GroupBox gb && (gb.Text.Contains("增强") || gb.Text.Contains("二值化") || gb.Text.Contains("形态学")))
                    gb.Enabled = enabled;
                SetPreprocessEnabled(c, enabled);
            }
        }

        private void BtnLoadLeft_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog { Filter = "图像文件|*.jpg;*.jpeg;*.png;*.bmp;*.tiff" })
            {
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    _leftImage?.Dispose();
                    _leftAnnotated?.Dispose();
                    _leftAnnotated = null;
                    _leftImage = new Bitmap(ofd.FileName);
                    picLeft.Image = _leftImage;
                    pbLeftResult.Image = null;
                    lblLeftPath.Text = ofd.FileName;
                }
            }
        }

        private void BtnLoadRight_Click(object sender, EventArgs e)
        {
            using (var ofd = new OpenFileDialog { Filter = "图像文件|*.jpg;*.jpeg;*.png;*.bmp;*.tiff" })
            {
                if (ofd.ShowDialog() == DialogResult.OK)
                {
                    _rightImage?.Dispose();
                    _rightAnnotated?.Dispose();
                    _rightAnnotated = null;
                    _rightImage = new Bitmap(ofd.FileName);
                    picRight.Image = _rightImage;
                    pbRightResult.Image = null;
                    lblRightPath.Text = ofd.FileName;
                }
            }
        }

        private async void BtnExecute_Click(object sender, EventArgs e)
        {
            if (_leftImage == null || _rightImage == null)
            {
                MessageBox.Show("请先加载左图和右图！");
                return;
            }

            btnExecute.Enabled = false;
            btnExecute.Text = "检测中...";

            var config = new BarcodeChecker.PreprocessConfig
            {
                EnablePreprocess = chkEnablePreprocess.Checked,
                ContrastAlpha = (float)nudContrast.Value,
                BrightnessBeta = (int)nudBrightness.Value,
                EnableGaussianBlur = chkGaussianBlur.Checked,
                EnableMedianBlur = chkMedianBlur.Checked,
                EnableEqualizeHist = chkEqualizeHist.Checked,
                EnableInvert = chkInvert.Checked,
                ThresholdMode = (BarcodeChecker.ThresholdModeEnum)cmbThreshold.SelectedIndex,
                AdaptiveBlockSize = (int)nudAdaptiveBlock.Value,
                AdaptiveC = (double)nudAdaptiveC.Value,
                FixedThreshold = (int)nudFixedThresh.Value,
                EnableMorphClose = chkMorphClose.Checked,
                EnableMorphOpen = chkMorphOpen.Checked,
                EnableMorphDilate = chkMorphDilate.Checked,
                EnableMorphErode = chkMorphErode.Checked,
                StartHeightRatio = (double)nudStartRatio.Value / 100.0,
                EnableFilterBestMatch = chkFilterBestMatch.Checked,
                MinBarcodeLength = (int)nudMinBarcodeLen.Value,
                MaxBarcodeLength = (int)nudMaxBarcodeLen.Value,
                TryHarder = chkTryHarder.Checked,
                EnableRotationRetry = chkRotationRetry.Checked
            };

            string refBarcode = txtRefBarcode.Text.Trim();
            int p = (int)nudP.Value;

            txtLog.Clear();
            txtLog.AppendText($"========== 检测开始 [{DateTime.Now:HH:mm:ss}] ==========\r\n");
            txtLog.AppendText($"基准条码: {refBarcode}, 盒子数 P: {p}, 裁剪起始: {config.StartHeightRatio:P0}, 预处理: {config.EnablePreprocess}\r\n\r\n");

            var sw = Stopwatch.StartNew();
            try
            {
                var (statuses, leftDict, rightDict) = await BarcodeChecker.CheckBackBarcodeCv2Async(
                    _leftImage, _rightImage, refBarcode, p, config);

                sw.Stop();
                lblElapsed.Text = $"耗时: {sw.ElapsedMilliseconds} ms";

                // 绘制标注图像
                _leftAnnotated?.Dispose();
                _rightAnnotated?.Dispose();
                double startRatio = config.StartHeightRatio;
                _leftAnnotated = DrawBarcodeAnnotations(_leftImage, leftDict, p / 2, startRatio);
                _rightAnnotated = DrawBarcodeAnnotations(_rightImage, rightDict, p / 2, startRatio);
                pbLeftResult.Image = _leftAnnotated;
                pbRightResult.Image = _rightAnnotated;

                // 日志输出 — 完整展示返回值结构
                txtLog.AppendText("══════════ 返回值结构说明 ══════════\r\n");
                txtLog.AppendText("Tuple< string[],                           ← backBarcodeStatuses 状态数组[P]\r\n");
                txtLog.AppendText("       Dictionary<string,                   ← leftBarcodeDict  左图条码字典\r\n");
                txtLog.AppendText("           List<List<double[]>>>,           ←   每个条码值 → 检测实例列表 → 坐标点列表\r\n");
                txtLog.AppendText("       Dictionary<string,                   ← rightBarcodeDict 右图条码字典\r\n");
                txtLog.AppendText("           List<List<double[]>>> >          ←   结构同上\r\n");
                txtLog.AppendText("═══════════════════════════════════════════\r\n\r\n");

                // 1. 状态数组
                txtLog.AppendText("━━━ ① backBarcodeStatuses (string[P]) ━━━\r\n");
                txtLog.AppendText($"数组长度: {statuses.Length} (P={p})\r\n");
                for (int i = 0; i < statuses.Length; i++)
                {
                    string side = i < p / 2 ? "左图" : "右图";
                    int boxIdx = i < p / 2 ? i + 1 : i - p / 2 + 1;
                    txtLog.AppendText($"  [{i}] {side} Box#{boxIdx} → \"{statuses[i]}\"\r\n");
                }

                // 2. 左图条码字典
                txtLog.AppendText($"\r\n━━━ ② leftBarcodeDict ({leftDict.Count} 个条码值) ━━━\r\n");
                DumpBarcodeDict(txtLog, leftDict, _leftImage.Width, _leftImage.Height, "左图");

                // 3. 右图条码字典
                txtLog.AppendText($"\r\n━━━ ③ rightBarcodeDict ({rightDict.Count} 个条码值) ━━━\r\n");
                DumpBarcodeDict(txtLog, rightDict, _rightImage.Width, _rightImage.Height, "右图");

                // 填充表格
                var table = new List<dynamic>();
                for (int i = 0; i < p; i++)
                {
                    var dict = i < p / 2 ? leftDict : rightDict;
                    string side = i < p / 2 ? "左图" : "右图";
                    int boxIdx = i < p / 2 ? i : i - p / 2;
                    table.Add(new
                    {
                        序号 = i + 1,
                        图像 = side,
                        区域 = boxIdx + 1,
                        状态 = statuses[i],
                        检测条码 = dict.Any() ? string.Join("; ", dict.Keys) : "无"
                    });
                }
                dgvResults.DataSource = table.ToList();
                dgvResults.Columns["序号"].Width = 50;
                dgvResults.Columns["图像"].Width = 50;
                dgvResults.Columns["区域"].Width = 50;
                dgvResults.Columns["状态"].Width = 60;

                txtLog.AppendText($"\r\n========== 检测完成 ==========\r\n");
            }
            catch (Exception ex)
            {
                sw.Stop();
                txtLog.AppendText($"\r\n[错误] {ex.Message}\r\n{ex.StackTrace}\r\n");
                lblElapsed.Text = $"耗时: {sw.ElapsedMilliseconds} ms (异常)";
            }
            finally
            {
                btnExecute.Enabled = true;
                btnExecute.Text = "执行检测";
            }
        }

        private void DumpBarcodeDict(TextBox log, Dictionary<string, List<List<double[]>>> dict, int imgW, int imgH, string side)
        {
            if (dict.Count == 0)
            {
                log.AppendText($"  (空 — 未检测到任何条码)\r\n");
                return;
            }

            int kvIdx = 0;
            foreach (var kv in dict)
            {
                log.AppendText($"  ┌ 条码值[{kvIdx}]: \"{kv.Key}\"  检测到 {kv.Value.Count} 个实例\r\n");
                for (int i = 0; i < kv.Value.Count; i++)
                {
                    var points = kv.Value[i];
                    log.AppendText($"  │   实例#{i + 1}: {points.Count} 个顶点\r\n");
                    for (int j = 0; j < points.Count; j++)
                    {
                        double rx = points[j][0];
                        double ry = points[j][1];
                        int px = (int)(rx * imgW);
                        int py = (int)(ry * imgH);
                        log.AppendText($"  │     P{j}: 归一化[{rx:F4}, {ry:F4}]  →  像素({px}, {py})\r\n");
                    }
                }
                log.AppendText("  └\r\n");
                kvIdx++;
            }
        }

        /// <summary>
        /// 在图像上绘制条码多边形和标签。坐标使用相对于整张图的 0~1 归一化坐标。
        /// </summary>
        private Bitmap DrawBarcodeAnnotations(Bitmap srcImage, Dictionary<string, List<List<double[]>>> barcodeDict, int boxCount, double startRatio)
        {
            Bitmap result = new Bitmap(srcImage.Width, srcImage.Height);
            using (Graphics g = Graphics.FromImage(result))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.DrawImage(srcImage, 0, 0);

                int colorIdx = 0;
                int halfBoxW = srcImage.Width / boxCount;
                float startY = (float)(srcImage.Height * startRatio);

                // 绘制 ROI 区域分隔线（虚线）
                using (Pen dashPen = new Pen(Color.FromArgb(180, 255, 255, 255), 1) { DashStyle = DashStyle.Dash })
                {
                    for (int i = 1; i < boxCount; i++)
                    {
                        int x = i * halfBoxW;
                        g.DrawLine(dashPen, x, startY, x, srcImage.Height);
                    }
                    g.DrawLine(dashPen, 0, startY, srcImage.Width, startY);
                }

                foreach (var kv in barcodeDict)
                {
                    string barcodeText = kv.Key;
                    var pointsGroups = kv.Value;
                    Color color = BarcodeColors[colorIdx % BarcodeColors.Length];
                    colorIdx++;

                    foreach (var pointsList in pointsGroups)
                    {
                        if (pointsList.Count < 2) continue;

                        // 将归一化坐标转回像素坐标
                        var pixelPoints = pointsList
                            .Select(p => new PointF((float)(p[0] * srcImage.Width), (float)(p[1] * srcImage.Height)))
                            .ToArray();

                        // 绘制多边形边框
                        using (Pen pen = new Pen(color, 3) { LineJoin = LineJoin.Round })
                        {
                            g.DrawPolygon(pen, pixelPoints);
                        }

                        // 半透明填充
                        using (Brush fillBrush = new SolidBrush(Color.FromArgb(40, color)))
                        {
                            g.FillPolygon(fillBrush, pixelPoints);
                        }

                        // 绘制顶点圆点
                        foreach (var pt in pixelPoints)
                        {
                            g.FillEllipse(Brushes.White, pt.X - 4, pt.Y - 4, 8, 8);
                            g.FillEllipse(new SolidBrush(color), pt.X - 3, pt.Y - 3, 6, 6);
                        }

                        // 计算多边形中心，标签画在中心位置
                        float cx = pixelPoints.Average(p => p.X);
                        float cy = pixelPoints.Average(p => p.Y);

                        using (Font font = new Font("Consolas", 10, FontStyle.Bold))
                        {
                            SizeF textSize = g.MeasureString(barcodeText, font);
                            // 标签矩形居中于多边形中心
                            float bx = cx - (textSize.Width + 6) / 2f;
                            float by = cy - (textSize.Height + 6) / 2f;
                            RectangleF bgRect = new RectangleF(bx, by, textSize.Width + 6, textSize.Height + 6);
                            g.FillRectangle(new SolidBrush(Color.FromArgb(200, 0, 0, 0)), bgRect);
                            g.DrawRectangle(new Pen(color, 2), bgRect.X, bgRect.Y, bgRect.Width, bgRect.Height);
                            g.DrawString(barcodeText, font, Brushes.White, bx + 3, by + 3);
                        }
                    }
                }
            }
            return result;
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            _leftImage?.Dispose();
            _rightImage?.Dispose();
            _leftAnnotated?.Dispose();
            _rightAnnotated?.Dispose();
            base.OnFormClosed(e);
        }
    }
}
