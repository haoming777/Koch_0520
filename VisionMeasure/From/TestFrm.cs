using Config;
using Detection;
using Hardware;
using Models;
using MT.Camera.SDK;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using SmartMore.ViMo;
using Sunny.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows.Forms;
using VisionMeasure.Utils;
using CommonLib;
using XL.Controls;
using YoloInference;
using YoloSegmentationEnd2End;
using BmpConverter = OpenCvSharp.Extensions.BitmapConverter;
using CvRect = OpenCvSharp.Rect;
using DrawPoint = System.Drawing.Point;
using DrawSize = System.Drawing.Size;
using Rect = System.Drawing.Rectangle;
using DetResult = YoloInference.YoloResult;
using SegResult = YoloSegmentationEnd2End.YoloResult;
using Newtonsoft.Json;

namespace UI
{
	internal class TestDefect { public string Type; public float[] Box; public float Score; public TestDefect(string t, float[] b, float s) { Type = t; Box = b; Score = s; } }

	internal class TestConfig
	{
		[JsonProperty("conf")] public float Conf { get; set; } = 0.5f;
		[JsonProperty("iou")] public float Iou { get; set; } = 0.45f;
		[JsonProperty("crop")] public float Crop { get; set; } = 0.33f;
		[JsonProperty("thickness")] public float Thickness { get; set; } = 30;
		[JsonProperty("blueClassId")] public int BlueClassId { get; set; } = 0;
		[JsonProperty("holeClassId")] public int HoleClassId { get; set; } = 1;
		[JsonProperty("pCount")] public int PCount { get; set; } = 8;
		[JsonProperty("batchCount")] public int BatchCount { get; set; } = 1;
		[JsonProperty("selectedStation")] public int SelectedStation { get; set; } = 0;
		[JsonProperty("selectedModel")] public int SelectedModel { get; set; } = 0;
		public static string Path { get { return System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "TestParams.json"); } }
		public void Save() { var dir = System.IO.Path.GetDirectoryName(Path); if (!Directory.Exists(dir)) Directory.CreateDirectory(dir); File.WriteAllText(Path, JsonConvert.SerializeObject(this, Formatting.Indented)); }
		public static TestConfig Load() { try { if (File.Exists(Path)) return JsonConvert.DeserializeObject<TestConfig>(File.ReadAllText(Path)) ?? new TestConfig(); } catch { } return new TestConfig(); }
	}

	public partial class TestForm : UIForm
	{
		private static readonly Color PriC = Color.FromArgb(0, 122, 204);
		private static readonly Color OkC = Color.FromArgb(39, 174, 96);
		private static readonly Color NgC = Color.FromArgb(231, 76, 60);
		private static readonly Color BgC = Color.FromArgb(245, 247, 250);
		private static readonly Color CardC = Color.White;
		private static readonly Color PnGreen = Color.Lime;

		private readonly MotionControlManager _motion;
		private readonly DaHuaSDK[] _cameras;
		private AiModelManager _aiModels;
		private SkuData _sku = new SkuData { SkuNumber = "TEST", P = 8, Z = 2, MM = 42 };
		private TestConfig _cfg;
		private string _st = "正面";
		private Mat _m1, _m2, _m3;
		private List<Mat> _batch = new List<Mat>(); private int _bi;
		private List<Mat> _mbatch = new List<Mat>(); private int _mbi;
		private double _tMs;

		private TabControl _tab;
		private UIComboBox _cmbSt, _cmbM;
		private UIButton _bImg, _bCam, _bRun, _bMImg, _bMCam, _bMRun, _bPrv, _bNxt, _bMPrv, _bMNxt, _bSave, _bMSave;
		private CheckBox _chkRev, _chkPNum, _chkMRev;
		private XLPictureBox _pi1, _pi2, _po, _pm1, _pm2;
		private DataGridView _grd; private RichTextBox _log, _mlog;
		private NumericUpDown _nConf, _nIou, _nP, _nCrop, _nThick, _nBlue, _nHole, _nMConf, _nMIou, _nMBatch, _nMCrop, _nMThick, _nMBlue, _nMHole;
		private UILabel _lblT, _lblMT, _lblPg, _lblMPg, _lblMI, _lblS;
		private ProgressBar _pb;

		public TestForm(MotionControlManager m, CameraManager cm, AiModelManager a = null) { _motion = m; _cameras = null; _aiModels = a; Bld(); this.Load += OnLd; }
		public TestForm(MotionControlManager m, DaHuaSDK[] c, AiModelManager a = null) { _motion = m; _cameras = c; _aiModels = a; Bld(); this.Load += OnLd; }

		private void OnLd(object s, EventArgs e)
		{
			VisionMeasure.MainFrm.ManualTestMode = true;
			_cfg = TestConfig.Load();
			ApplyCfg();
			Log("手动测试模式已启用 | 参数已加载:" + TestConfig.Path);
			if (_aiModels == null) { try { _aiModels = new AiModelManager(ModelPathConfig.LoadFromSysConfig()); _aiModels.LoadAllModels(); Log("模型加载完成"); } catch (Exception ex) { Log("加载失败:" + ex.Message, true); } }
			else Log("使用主界面模型");
		}

		void ApplyCfg()
		{
			_nConf.Value = (decimal)_cfg.Conf; _nIou.Value = (decimal)_cfg.Iou; _nCrop.Value = (decimal)_cfg.Crop;
			_nThick.Value = (decimal)_cfg.Thickness; _nBlue.Value = _cfg.BlueClassId; _nHole.Value = _cfg.HoleClassId;
			_nP.Value = _cfg.PCount; _nMBatch.Value = _cfg.BatchCount;
			_nMConf.Value = (decimal)_cfg.Conf; _nMIou.Value = (decimal)_cfg.Iou; _nMCrop.Value = (decimal)_cfg.Crop;
			_nMThick.Value = (decimal)_cfg.Thickness; _nMBlue.Value = _cfg.BlueClassId; _nMHole.Value = _cfg.HoleClassId;
			if (_cfg.SelectedStation >= 0 && _cfg.SelectedStation < _cmbSt.Items.Count) _cmbSt.SelectedIndex = _cfg.SelectedStation;
			if (_cfg.SelectedModel >= 0 && _cfg.SelectedModel < _cmbM.Items.Count) _cmbM.SelectedIndex = _cfg.SelectedModel;
		}

		void SaveCfg()
		{
			_cfg.Conf = (float)_nMConf.Value; _cfg.Iou = (float)_nMIou.Value; _cfg.Crop = (float)_nMCrop.Value;
			_cfg.Thickness = (float)_nMThick.Value; _cfg.BlueClassId = (int)_nMBlue.Value; _cfg.HoleClassId = (int)_nMHole.Value;
			_cfg.PCount = (int)_nP.Value; _cfg.BatchCount = (int)_nMBatch.Value;
			_cfg.SelectedStation = _cmbSt.SelectedIndex; _cfg.SelectedModel = _cmbM.SelectedIndex;
			_cfg.Save(); Log("参数已保存"); MLog("参数已保存");
		}

		void Bld()
		{
			this.Text = "KOCH 测试工具"; this.Size = new DrawSize(1500, 950); this.StartPosition = FormStartPosition.CenterParent; this.BackColor = BgC;
			_tab = new TabControl { Dock = DockStyle.Fill, Font = new Font("微软雅黑", 10F) }; _tab.TabPages.Add(TabSt()); _tab.TabPages.Add(TabM()); this.Controls.Add(_tab);
			var bar = new Panel { Dock = DockStyle.Bottom, Height = 28, BackColor = Color.FromArgb(47, 60, 76) };
			_lblS = new UILabel { Text = "就绪", ForeColor = Color.White, Location = new DrawPoint(12, 4), Size = new DrawSize(900, 20), Font = new Font("微软雅黑", 9F) };
			_pb = new ProgressBar { Style = ProgressBarStyle.Marquee, Width = 150, Height = 16, Location = new DrawPoint(1320, 6), Visible = false };
			bar.Controls.Add(_lblS); bar.Controls.Add(_pb); this.Controls.Add(bar);
		}
		Panel Cd(int w, int h) { return new Panel { Width = w, Height = h, BackColor = CardC, Margin = new Padding(4), BorderStyle = BorderStyle.None }; }
		XLPictureBox Pc() { return new XLPictureBox { Dock = DockStyle.Fill, BackColor1 = Color.FromArgb(50, 50, 50), BackColor2 = Color.FromArgb(70, 70, 70), BackgroundGridSize = 20 }; }
		Panel Wp(XLPictureBox p, string t) { var r = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) }; r.Controls.Add(p); r.Controls.Add(new Label { Text = "  " + t, Dock = DockStyle.Top, Height = 20, BackColor = Color.FromArgb(240, 242, 245), Font = new Font("微软雅黑", 8F, FontStyle.Bold) }); return r; }
		NumericUpDown Nu(decimal v, decimal mn, decimal mx, int w = 65) { return new NumericUpDown { Width = w, Minimum = mn, Maximum = mx, Value = v, DecimalPlaces = 2, Increment = 0.05m, Font = new Font("微软雅黑", 9F) }; }

		// ====== STATION TAB ======
		TabPage TabSt()
		{
			var pg = new TabPage("工位测试"); var lo = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
			lo.RowStyles.Add(new RowStyle(SizeType.Absolute, 200)); lo.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); lo.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
			lo.Controls.Add(TopSt(), 0, 0); lo.Controls.Add(ImgSt(), 0, 1); lo.Controls.Add(BtmSt(), 0, 2); pg.Controls.Add(lo); return pg;
		}
		Panel TopSt()
		{
			var pn = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(6), BackColor = BgC };
			var c1 = Cd(260, 175);
			_cmbSt = new UIComboBox { Location = new DrawPoint(10, 28), Size = new DrawSize(240, 28), DropDownStyle = UIDropDownStyle.DropDownList };
			_cmbSt.Items.AddRange(new object[] { "正面", "背面", "上端面", "下端面", "侧面" }); _cmbSt.SelectedIndex = 0;
			_cmbSt.SelectedIndexChanged += (s, e) => { _st = _cmbSt.SelectedItem.ToString(); Clr(); };
			c1.Controls.Add(_cmbSt); c1.Controls.Add(Lbl("P数:", 10, 65, 30)); _nP = Nu(8, 1, 20, 55); _nP.Location = new DrawPoint(45, 63); _nP.DecimalPlaces = 0; _nP.Increment = 1; c1.Controls.Add(_nP);
			c1.Controls.Add(Lbl("裁底:", 10, 95, 40)); _nCrop = Nu(0.33m, 0, 1, 55); _nCrop.Location = new DrawPoint(50, 93); c1.Controls.Add(_nCrop);
			_chkRev = new CheckBox { Text = "反转盒序", Location = new DrawPoint(10, 128), Size = new DrawSize(80, 22), Font = new Font("微软雅黑", 8F) }; c1.Controls.Add(_chkRev);
			_chkPNum = new CheckBox { Text = "P号码检测", Location = new DrawPoint(100, 128), Size = new DrawSize(90, 22), Font = new Font("微软雅黑", 8F) }; c1.Controls.Add(_chkPNum);
			pn.Controls.Add(c1);

			var c2 = Cd(260, 175);
			_bImg = new UIButton { Text = "加载离线图", Location = new DrawPoint(10, 15), Size = new DrawSize(240, 35), Font = new Font("微软雅黑", 9F), Radius = 6, Cursor = Cursors.Hand }; _bImg.Click += BtnImg;
			_bCam = new UIButton { Text = "相机采图", Location = new DrawPoint(10, 55), Size = new DrawSize(240, 35), Font = new Font("微软雅黑", 9F), Radius = 6, Cursor = Cursors.Hand, FillColor = PriC }; _bCam.Click += BtnCam;
			c2.Controls.Add(_bImg); c2.Controls.Add(_bCam);
			_bPrv = new UIButton { Text = "<", Location = new DrawPoint(60, 100), Size = new DrawSize(60, 28), Font = new Font("微软雅黑", 9F), Radius = 4, Cursor = Cursors.Hand, Enabled = false }; _bPrv.Click += (s, e) => { if (_batch.Count > 0) { _bi = (_bi - 1 + _batch.Count) % _batch.Count; ShwB(); } };
			_bNxt = new UIButton { Text = ">", Location = new DrawPoint(140, 100), Size = new DrawSize(60, 28), Font = new Font("微软雅黑", 9F), Radius = 4, Cursor = Cursors.Hand, Enabled = false }; _bNxt.Click += (s, e) => { if (_batch.Count > 0) { _bi = (_bi + 1) % _batch.Count; ShwB(); } };
			_lblPg = new UILabel { Text = "", Location = new DrawPoint(80, 130), Size = new DrawSize(100, 12), TextAlign = ContentAlignment.MiddleCenter, Font = new Font("微软雅黑", 7F) };
			_bSave = new UIButton { Text = "保存参数", Location = new DrawPoint(40, 145), Size = new DrawSize(160, 30), Font = new Font("微软雅黑", 8F), Radius = 4, Cursor = Cursors.Hand };
			_bSave.Click += (s, e) => SaveCfg();
			c2.Controls.Add(_bPrv); c2.Controls.Add(_bNxt); c2.Controls.Add(_lblPg); c2.Controls.Add(_bSave); pn.Controls.Add(c2);

			var c3 = Cd(240, 175);
			c3.Controls.Add(Lbl("Conf:", 10, 18, 45)); _nConf = Nu(0.5m, 0.05m, 1.0m); _nConf.Location = new DrawPoint(52, 16); c3.Controls.Add(_nConf);
			c3.Controls.Add(Lbl("IOU:", 125, 18, 35)); _nIou = Nu(0.45m, 0.05m, 1.0m); _nIou.Location = new DrawPoint(158, 16); c3.Controls.Add(_nIou);
			c3.Controls.Add(Lbl("厚度:", 10, 48, 45, 8)); _nThick = Nu(30, 1, 200, 55); _nThick.Location = new DrawPoint(52, 46); _nThick.DecimalPlaces = 1; c3.Controls.Add(_nThick);
			c3.Controls.Add(Lbl("蓝区/孔:", 10, 76, 55, 8)); _nBlue = Nu(0, 0, 10, 40); _nBlue.Location = new DrawPoint(65, 74); _nBlue.DecimalPlaces = 0; _nBlue.Increment = 1; c3.Controls.Add(_nBlue);
			_nHole = Nu(1, 0, 10, 40); _nHole.Location = new DrawPoint(110, 74); _nHole.DecimalPlaces = 0; _nHole.Increment = 1; c3.Controls.Add(_nHole); pn.Controls.Add(c3);

			var c4 = Cd(200, 175);
			_bRun = new UIButton { Text = "执行检测", Location = new DrawPoint(10, 20), Size = new DrawSize(180, 50), Font = new Font("微软雅黑", 12F, FontStyle.Bold), Radius = 8, Cursor = Cursors.Hand, FillColor = OkC, Enabled = false }; _bRun.Click += BtnRun;
			_lblT = new UILabel { Text = "耗时: --", Location = new DrawPoint(10, 80), Size = new DrawSize(180, 20), TextAlign = ContentAlignment.MiddleCenter, Font = new Font("微软雅黑", 9F) };
			c4.Controls.Add(_bRun); c4.Controls.Add(_lblT); pn.Controls.Add(c4); return pn;
		}
		Panel ImgSt() { var p = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 }; p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33)); p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33)); p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34)); _pi1 = Pc(); _pi2 = Pc(); _po = Pc(); p.Controls.Add(Wp(_pi1, "输入1"), 0, 0); p.Controls.Add(Wp(_pi2, "输入2"), 1, 0); p.Controls.Add(Wp(_po, "结果"), 2, 0); return p; }
		Panel BtmSt()
		{
			var p = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 }; p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45)); p.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
			_grd = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Color.White, Font = new Font("微软雅黑", 9F) };
			_grd.Columns.Add("Box", "#"); _grd.Columns.Add("St", "状态"); _grd.Columns.Add("Def", "缺陷"); _grd.Columns.Add("Conf", "置信度");
			_log = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(30, 30, 30), ForeColor = Color.FromArgb(180, 180, 180), Font = new Font("Consolas", 9F), BorderStyle = BorderStyle.None };
			var a = new Panel { Dock = DockStyle.Fill }; a.Controls.Add(new Label { Text = "  缺陷详情", Dock = DockStyle.Top, Height = 22, BackColor = Color.FromArgb(240, 242, 245), Font = new Font("微软雅黑", 9F, FontStyle.Bold) }); a.Controls.Add(_grd);
			var b = new Panel { Dock = DockStyle.Fill }; b.Controls.Add(new Label { Text = "  日志", Dock = DockStyle.Top, Height = 22, BackColor = Color.FromArgb(240, 242, 245), Font = new Font("微软雅黑", 9F, FontStyle.Bold) }); b.Controls.Add(_log);
			p.Controls.Add(a, 0, 0); p.Controls.Add(b, 1, 0); return p;
		}

		// ====== MODEL TAB ======
		TabPage TabM()
		{
			var pg = new TabPage("模型测试"); var lo = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
			lo.RowStyles.Add(new RowStyle(SizeType.Absolute, 195)); lo.RowStyles.Add(new RowStyle(SizeType.Percent, 45)); lo.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
			lo.Controls.Add(TopM(), 0, 0); lo.Controls.Add(ImgM(), 0, 1); lo.Controls.Add(BtmM(), 0, 2); pg.Controls.Add(lo); return pg;
		}
		Panel TopM()
		{
			var pn = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(6), BackColor = BgC };
			var c1 = Cd(340, 175);
			_cmbM = new UIComboBox { Location = new DrawPoint(10, 28), Size = new DrawSize(320, 28), DropDownStyle = UIDropDownStyle.DropDownList };
			_cmbM.Items.AddRange(new object[] { "正面-盒子破检测", "正面-P号码OCR", "上端面-缺陷检测", "下端面-缺陷检测", "背面-条形码检测", "背面-日期码OCR", "背面-明显挂钩错位", "背面-轻微挂钩错位", "背面-挂钩综合(双模型)", "侧面-缺陷检测" });
			_cmbM.SelectedIndex = 0; c1.Controls.Add(_cmbM);
			_lblMI = new UILabel { Text = "选择模型后加载图像执行推理", Location = new DrawPoint(10, 65), Size = new DrawSize(320, 55), Font = new Font("微软雅黑", 8F), ForeColor = Color.Gray }; c1.Controls.Add(_lblMI);
			_chkMRev = new CheckBox { Text = "反转盒序", Location = new DrawPoint(10, 125), Size = new DrawSize(80, 22), Font = new Font("微软雅黑", 8F) }; c1.Controls.Add(_chkMRev);
			pn.Controls.Add(c1);

			var c2 = Cd(280, 175);
			_bMImg = new UIButton { Text = "加载图像", Location = new DrawPoint(10, 12), Size = new DrawSize(260, 32), Font = new Font("微软雅黑", 8F), Radius = 4, Cursor = Cursors.Hand }; _bMImg.Click += BtnMImg;
			_bMCam = new UIButton { Text = "相机采图", Location = new DrawPoint(10, 48), Size = new DrawSize(260, 32), Font = new Font("微软雅黑", 8F), Radius = 4, Cursor = Cursors.Hand, FillColor = PriC }; _bMCam.Click += BtnMCam;
			_bMRun = new UIButton { Text = "执行推理", Location = new DrawPoint(10, 84), Size = new DrawSize(260, 38), Font = new Font("微软雅黑", 10F, FontStyle.Bold), Radius = 6, Cursor = Cursors.Hand, FillColor = OkC, Enabled = false }; _bMRun.Click += BtnMRun;
			_bMSave = new UIButton { Text = "保存参数", Location = new DrawPoint(10, 130), Size = new DrawSize(260, 30), Font = new Font("微软雅黑", 8F), Radius = 4, Cursor = Cursors.Hand };
			_bMSave.Click += (s, e) => SaveCfg();
			c2.Controls.Add(_bMImg); c2.Controls.Add(_bMCam); c2.Controls.Add(_bMRun); c2.Controls.Add(_bMSave); pn.Controls.Add(c2);

			var c3 = Cd(310, 175);
			c3.Controls.Add(Lbl("Conf:", 8, 12, 38, 8)); _nMConf = Nu(0.5m, 0.05m, 1.0m, 50); _nMConf.Location = new DrawPoint(45, 10); c3.Controls.Add(_nMConf);
			c3.Controls.Add(Lbl("IOU:", 100, 12, 32, 8)); _nMIou = Nu(0.45m, 0.05m, 1.0m, 50); _nMIou.Location = new DrawPoint(130, 10); c3.Controls.Add(_nMIou);
			c3.Controls.Add(Lbl("Batch:", 185, 12, 42, 8)); _nMBatch = Nu(1, 1, 20, 48); _nMBatch.Location = new DrawPoint(225, 10); _nMBatch.DecimalPlaces = 0; _nMBatch.Increment = 1; c3.Controls.Add(_nMBatch);
			c3.Controls.Add(Lbl("裁底:", 8, 40, 40, 8)); _nMCrop = Nu(0.33m, 0, 1, 50); _nMCrop.Location = new DrawPoint(48, 38); c3.Controls.Add(_nMCrop);
			c3.Controls.Add(Lbl("厚度:", 105, 40, 38, 8)); _nMThick = Nu(30, 1, 200, 50); _nMThick.Location = new DrawPoint(140, 38); _nMThick.DecimalPlaces = 1; c3.Controls.Add(_nMThick);
			c3.Controls.Add(Lbl("蓝区/孔:", 8, 66, 60, 8)); _nMBlue = Nu(0, 0, 10, 38); _nMBlue.Location = new DrawPoint(65, 64); _nMBlue.DecimalPlaces = 0; _nMBlue.Increment = 1; c3.Controls.Add(_nMBlue);
			_nMHole = Nu(1, 0, 10, 38); _nMHole.Location = new DrawPoint(108, 64); _nMHole.DecimalPlaces = 0; _nMHole.Increment = 1; c3.Controls.Add(_nMHole);
			_lblMT = new UILabel { Text = "时间: --", Location = new DrawPoint(8, 95), Size = new DrawSize(140, 18), Font = new Font("微软雅黑", 8F, FontStyle.Bold) };
			_bMPrv = new UIButton { Text = "<", Location = new DrawPoint(150, 92), Size = new DrawSize(40, 26), Font = new Font("微软雅黑", 8F), Radius = 3, Cursor = Cursors.Hand, Enabled = false }; _bMPrv.Click += (s, e) => { if (_mbatch.Count > 0) { _mbi = (_mbi - 1 + _mbatch.Count) % _mbatch.Count; ShwMB(); } };
			_bMNxt = new UIButton { Text = ">", Location = new DrawPoint(194, 92), Size = new DrawSize(40, 26), Font = new Font("微软雅黑", 8F), Radius = 3, Cursor = Cursors.Hand, Enabled = false }; _bMNxt.Click += (s, e) => { if (_mbatch.Count > 0) { _mbi = (_mbi + 1) % _mbatch.Count; ShwMB(); } };
			_lblMPg = new UILabel { Text = "", Location = new DrawPoint(240, 95), Size = new DrawSize(55, 18), Font = new Font("微软雅黑", 8F), TextAlign = ContentAlignment.MiddleCenter };
			c3.Controls.Add(_lblMT); c3.Controls.Add(_bMPrv); c3.Controls.Add(_bMNxt); c3.Controls.Add(_lblMPg); pn.Controls.Add(c3); return pn;
		}
		Panel ImgM() { var p = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 }; _pm1 = Pc(); _pm2 = Pc(); p.Controls.Add(Wp(_pm1, "输入"), 0, 0); p.Controls.Add(Wp(_pm2, "结果"), 1, 0); return p; }
		Panel BtmM() { _mlog = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(30, 30, 30), ForeColor = Color.FromArgb(180, 180, 180), Font = new Font("Consolas", 9F), BorderStyle = BorderStyle.None }; var p = new Panel { Dock = DockStyle.Fill }; p.Controls.Add(new Label { Text = "  推理日志", Dock = DockStyle.Top, Height = 22, BackColor = Color.FromArgb(240, 242, 245), Font = new Font("微软雅黑", 9F, FontStyle.Bold) }); p.Controls.Add(_mlog); return p; }

		// ====== HANDLERS ======
		Label Lbl(string t, int x, int y, int w, float sz = 9) { return new Label { Text = t, Location = new DrawPoint(x, y), Size = new DrawSize(w, 22), Font = new Font("微软雅黑", sz) }; }
		void UI(Action a) { this.BeginInvoke(a); }
		void Busy(bool b) { UI(() => _pb.Visible = b); }
		List<string> Ns(int p) { var s = new List<string>(p); for (int i = 0; i < p; i++) s.Add("OK"); return s; }
		bool Ao(List<string> s) { for (int i = 0; i < s.Count; i++) if (s[i] != "OK") return false; return true; }

		void Log(string m, bool e = false) { string l = "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + m; UI(() => { if (_log.IsDisposed) return; _log.SelectionStart = _log.TextLength; _log.SelectionColor = e ? NgC : Color.FromArgb(180, 180, 180); _log.AppendText(l + "\n"); _log.ScrollToCaret(); if (_log.TextLength > 10000) _log.Text = _log.Text.Substring(_log.TextLength - 8000); }); if (e) Logger.Error(m); else Logger.Info(m); }
		void MLog(string m) { string l = "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + m; UI(() => { if (_mlog.IsDisposed) return; _mlog.AppendText(l + "\n"); _mlog.ScrollToCaret(); }); Logger.Info("[MT] " + m); }

		void BtnImg(object s, EventArgs e)
		{
			bool fb = _st == "正面" || _st == "背面"; bool ef = _st == "上端面" || _st == "下端面";
			using (var d = new OpenFileDialog { Title = "选择图像", Filter = "所有图像|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff", Multiselect = fb || ef })
			{ if (d.ShowDialog() != DialogResult.OK) return; Clr(); try { if (fb) { if (d.FileNames.Length < 2) { MessageBox.Show("需要2张!"); return; } _m1 = Cv2.ImRead(d.FileNames[0]); _m2 = Cv2.ImRead(d.FileNames[1]); _pi1.Image = BmpConverter.ToBitmap(_m1); _pi2.Image = BmpConverter.ToBitmap(_m2); } else if (ef) { foreach (var f in d.FileNames) _batch.Add(Cv2.ImRead(f)); _bi = 0; UpPg(); ShwB(); Log("加载" + _batch.Count + "张"); } else { var m = Cv2.ImRead(d.FileName); _m3 = m; _pi1.Image = BmpConverter.ToBitmap(m); } _bRun.Enabled = true; Log("图像就绪"); } catch (Exception ex) { Log("加载失败:" + ex.Message, true); } }
		}
		void BtnCam(object s, EventArgs e) { if (_cameras == null) { MessageBox.Show("相机未初始化"); return; } int ci = _st == "背面" ? 4 : _st == "上端面" ? 2 : _st == "下端面" ? 3 : _st == "侧面" ? 6 : 0; if (_cameras[ci] == null) { MessageBox.Show("Cam" + (ci + 1) + "未连接"); return; } Busy(true); var c = _cameras[ci]; Task.Run(() => { try { c.setTriggerSource(0); System.Threading.Thread.Sleep(50); c.ExecuteSoftwareTrigger(); System.Threading.Thread.Sleep(100); c.setTriggerSource(1); UI(() => Log("Cam" + (ci + 1) + "触发完成")); } catch (Exception ex) { UI(() => Log("失败:" + ex.Message, true)); } finally { UI(() => Busy(false)); } }); }

		async void BtnRun(object s, EventArgs e)
		{
			if (_aiModels == null) { MessageBox.Show("模型未加载!"); return; }
			Busy(true); _bRun.Enabled = false; var sw = Stopwatch.StartNew(); Log("===== " + _st + " =====");
			try { await Task.Run(() => { if (_st == "正面") DoF(); else if (_st == "背面") DoB(); else if (_st == "上端面") DoE(true); else if (_st == "下端面") DoE(false); else DoS(); }); sw.Stop(); UI(() => { _lblT.Text = "耗时:" + sw.ElapsedMilliseconds + "ms(推理:" + _tMs.ToString("F0") + "ms)"; }); Log("完成:" + sw.ElapsedMilliseconds + "ms"); }
			catch (Exception ex) { Log("失败:" + ex.Message, true); }
			finally { UI(() => { _bRun.Enabled = true; Busy(false); }); }
		}

		void DoF()
		{
			int p = (int)_nP.Value, h = p / 2; var st = Ns(p); var ad = new Dictionary<int, List<TestDefect>>();
			bool doPnum = _chkPNum.Checked;

			// 盒子破损检测 (使用BoxesN归一化坐标)
			if (_aiModels.FrontBoxBreakModel != null)
			{
				var s = Stopwatch.StartNew();
				var lr = _aiModels.FrontBoxBreakModel.Predict(_m1, (float)_nConf.Value, (float)_nIou.Value);
				var rr = _aiModels.FrontBoxBreakModel.Predict(_m2, (float)_nConf.Value, (float)_nIou.Value);
				_tMs = s.Elapsed.TotalMilliseconds;
				ClN(lr, 0, h, "盒子破损", ad); ClN(rr, h, p, "盒子破损", ad);
			}

			// P号码OCR (与主界面一致: 逐盒ROI + Vimo OCR)
			if (doPnum && _aiModels.FrontOcrModel != null)
			{
				try
				{
					int hL = _m1.Height, wL = _m1.Width, hR = _m2.Height, wR = _m2.Width;
					int bwL = wL / h, bwR = wR / h;
					int syL = hL * 2 / 3, syR = hR * 2 / 3;
					var pnRegex = new Regex(@"P\d+", RegexOptions.IgnoreCase);

					for (int i = 0; i < h; i++)
					{
						int sx = i * bwL, rw = (i < h - 1) ? bwL : (wL - sx), rh = hL - syL;
						if (rw <= 0 || rh <= 0) continue;
						using (var roi = new Mat(_m1, new CvRect(sx, syL, rw, rh)).Clone())
						{
							ResponseList<OcrResponse> ocrR;
							if (_aiModels.FrontOcrModel.Run(roi, out ocrR) == 0 && ocrR != null && ocrR.Count > 0)
							{
								foreach (var rt in ocrR)
								{
									if (rt.Item2.Blocks == null) continue;
									foreach (var blk in rt.Item2.Blocks)
									{
										if (string.IsNullOrWhiteSpace(blk.Label)) continue;
										var m = pnRegex.Match(blk.Label);
										if (!m.Success || m.Value.Length < 6) continue;
										float[] nb = PnNormBox(blk, wL, hL, sx, syL);
										if (!ad.ContainsKey(i)) ad[i] = new List<TestDefect>();
										ad[i].Add(new TestDefect("P号:" + m.Value.ToUpper(), nb, 0.9f));
									}
								}
							}
						}
					}

					for (int j = 0; j < h; j++)
					{
						int gi = h + j, sx = j * bwR, rw = (j < h - 1) ? bwR : (wR - sx), rh = hR - syR;
						if (rw <= 0 || rh <= 0) continue;
						using (var roi = new Mat(_m2, new CvRect(sx, syR, rw, rh)).Clone())
						{
							ResponseList<OcrResponse> ocrR;
							if (_aiModels.FrontOcrModel.Run(roi, out ocrR) == 0 && ocrR != null && ocrR.Count > 0)
							{
								foreach (var rt in ocrR)
								{
									if (rt.Item2.Blocks == null) continue;
									foreach (var blk in rt.Item2.Blocks)
									{
										if (string.IsNullOrWhiteSpace(blk.Label)) continue;
										var m = pnRegex.Match(blk.Label);
										if (!m.Success || m.Value.Length < 6) continue;
										float[] nb = PnNormBox(blk, wR, hR, sx, syR);
										if (!ad.ContainsKey(gi)) ad[gi] = new List<TestDefect>();
										ad[gi].Add(new TestDefect("P号:" + m.Value.ToUpper(), nb, 0.9f));
									}
								}
							}
						}
					}
					Log("P号码OCR完成");
				}
				catch (Exception ex) { Log("P号码OCR异常:" + ex.Message, true); }
			}

			foreach (var kv in ad) if (kv.Value.Count > 0) st[kv.Key] = string.Join(",", kv.Value.ConvertAll(d => d.Type));
			bool rev = _chkRev.Checked;
			var mg = Mg(DrM(_m1, Fd(ad, 0, h), st, 0, h, rev), DrM(_m2, Fd(ad, h, p), st, h, p, rev));
			UI(() => { _po.Image = mg; Gp(st, ad); });
		}

		void DoB()
		{
			int p = (int)_nP.Value, h = p / 2; var st = Ns(p); var ad = new Dictionary<int, List<TestDefect>>();
			if (_aiModels.BackHookModel != null) { var s = Stopwatch.StartNew(); var lr = _aiModels.BackHookModel.Predict(_m1, (float)_nConf.Value, (float)_nIou.Value); var rr = _aiModels.BackHookModel.Predict(_m2, (float)_nConf.Value, (float)_nIou.Value); _tMs = s.Elapsed.TotalMilliseconds; Cl(lr, 0, h, "挂钩明显错位", ad); Cl(rr, h, p, "挂钩明显错位", ad); }
			if (_aiModels.HookSlightModel != null) { var lr = _aiModels.HookSlightModel.Predict(_m1, (float)_nConf.Value); var rr = _aiModels.HookSlightModel.Predict(_m2, (float)_nConf.Value); Cs(lr, 0, h, "轻微挂钩错位", ad); Cs(rr, h, p, "轻微挂钩错位", ad); }
			if (_aiModels.BackBarcodeModel != null) { var lr = _aiModels.BackBarcodeModel.Predict(_m1, (float)_nConf.Value, (float)_nIou.Value); var rr = _aiModels.BackBarcodeModel.Predict(_m2, (float)_nConf.Value, (float)_nIou.Value); Cl(lr, 0, h, "条形码错误", ad); Cl(rr, h, p, "条形码错误", ad); }
			foreach (var kv in ad) if (kv.Value.Count > 0) st[kv.Key] = string.Join(",", kv.Value.ConvertAll(d => d.Type));
			bool rev = _chkRev.Checked;
			var mg = Mg(DrM(_m1, Fd(ad, 0, h), st, 0, h, rev), DrM(_m2, Fd(ad, h, p), st, h, p, rev));
			UI(() => { _po.Image = mg; Gp(st, ad); });
		}
		void DoE(bool up) { var mdl = up ? _aiModels.EndFaceUpperModel : _aiModels.EndFaceLowerModel; var ms = _batch.Count > 0 ? _batch : (_m3 != null ? new List<Mat> { _m3 } : new List<Mat>()); if (mdl == null || ms.Count == 0) { Log("模型未加载或无图像"); return; } var s = Stopwatch.StartNew(); var rs = mdl.PredictBatch(ms, (float)_nConf.Value, (float)_nIou.Value); _tMs = s.Elapsed.TotalMilliseconds; _batch.Clear(); for (int j = 0; j < ms.Count; j++) { var bdf = new List<TestDefect>(); if (rs != null && j < rs.Count && rs[j].Boxes != null) for (int i = 0; i < rs[j].Boxes.Length; i++) { var bx = rs[j].BoxesN[i]; int cid = rs[j].ClassIds[i]; string tp = cid == 0 ? "搭舌缺陷" : cid == 1 ? "边缘问题" : cid == 2 ? "破损" : "缺陷" + cid; bdf.Add(new TestDefect(tp, new float[] { bx.X, bx.Y, bx.X + bx.Width, bx.Y + bx.Height }, rs[j].Scores[i])); } _batch.Add(BmpConverter.ToMat(Dv(ms[j], bdf))); } _bi = 0; UpPg(); UI(() => { ShwB(); _grd.Rows.Clear(); _grd.Rows.Add("-", _batch.Count + "张完成", "-", "-"); }); Log((up ? "上" : "下") + "端面:" + ms.Count + "张," + _tMs.ToString("F0") + "ms"); }
		void DoS() { if (_aiModels.SideDefectModel == null) { Log("侧面模型未加载"); return; } var s = Stopwatch.StartNew(); var r = _aiModels.SideDefectModel.Predict(_m3, (float)_nConf.Value, (float)_nIou.Value); _tMs = s.Elapsed.TotalMilliseconds; var df = new List<TestDefect>(); if (r?.BoxesN != null) for (int i = 0; i < r.BoxesN.Length; i++) { var bx = r.BoxesN[i]; df.Add(new TestDefect("缺陷" + r.ClassIds[i], new float[] { bx.X, bx.Y, bx.X + bx.Width, bx.Y + bx.Height }, r.Scores[i])); } var rd = Dv(_m3, df); UI(() => { _po.Image = rd; _grd.Rows.Clear(); for (int i = 0; i < df.Count; i++) _grd.Rows.Add(i + 1, "NG", df[i].Type, df[i].Score.ToString("F3")); if (df.Count == 0) _grd.Rows.Add("-", "OK", "-", "-"); }); }

		void BtnMImg(object s, EventArgs e) { int idx = _cmbM.SelectedIndex; if (idx == 2 || idx == 3) { using (var d = new FolderBrowserDialog { Description = "选择图像文件夹" }) { if (d.ShowDialog() != DialogResult.OK) return; _mbatch.Clear(); foreach (var f in Directory.GetFiles(d.SelectedPath).Where(f => f.EndsWith(".jpg") || f.EndsWith(".jpeg") || f.EndsWith(".png") || f.EndsWith(".bmp")).OrderBy(f => f).Take(50)) _mbatch.Add(Cv2.ImRead(f)); _mbi = 0; UpMPg(); ShwMB(); MLog("加载" + _mbatch.Count + "张"); _bMRun.Enabled = true; } } else { using (var d = new OpenFileDialog { Title = "选择图像", Filter = "所有图像|*.jpg;*.jpeg;*.png;*.bmp;*.tif;*.tiff" }) { if (d.ShowDialog() != DialogResult.OK) return; var m = Cv2.ImRead(d.FileName); _pm1.Image = BmpConverter.ToBitmap(m); m.Dispose(); MLog("加载:" + Path.GetFileName(d.FileName)); _bMRun.Enabled = true; } } }
		void BtnMCam(object s, EventArgs e) { if (_cameras == null) { MessageBox.Show("相机未初始化"); return; } int ci = _cmbM.SelectedIndex <= 1 ? 0 : _cmbM.SelectedIndex == 2 ? 2 : _cmbM.SelectedIndex == 3 ? 3 : _cmbM.SelectedIndex >= 4 && _cmbM.SelectedIndex <= 8 ? 4 : 6; if (_cameras[ci] == null) { MessageBox.Show("Cam" + (ci + 1) + "未连接"); return; } var c = _cameras[ci]; Task.Run(() => { try { c.setTriggerSource(0); System.Threading.Thread.Sleep(50); c.ExecuteSoftwareTrigger(); System.Threading.Thread.Sleep(100); c.setTriggerSource(1); UI(() => MLog("Cam" + (ci + 1) + "触发完成")); } catch (Exception ex) { UI(() => MLog("失败:" + ex.Message)); } }); }

		async void BtnMRun(object s, EventArgs e)
		{
			if (_pm1.Image == null && _mbatch.Count == 0) { MessageBox.Show("请先加载图像!"); return; }
			_bMRun.Enabled = false; var sw = Stopwatch.StartNew(); MLog("===== 推理开始 =====");
			try
			{
				await Task.Run(() =>
				{
					int idx = _cmbM.SelectedIndex; string mn = _cmbM.SelectedItem != null ? _cmbM.SelectedItem.ToString() : "";
					float cf = (float)_nMConf.Value, io = (float)_nMIou.Value;
					YoloOnnx ym = null;
					if (idx == 0) ym = _aiModels.FrontBoxBreakModel; else if (idx == 2) ym = _aiModels.EndFaceUpperModel; else if (idx == 3) ym = _aiModels.EndFaceLowerModel;
					else if (idx == 4) ym = _aiModels.BackBarcodeModel; else if (idx == 6) ym = _aiModels.BackHookModel; else if (idx == 9) ym = _aiModels.SideDefectModel;

					Bitmap rdr = null; string inf = "";

					if (ym != null)
					{
						if (idx == 2 || idx == 3) { var ms = _mbatch.Count > 0 ? _mbatch : new List<Mat> { BmpConverter.ToMat((Bitmap)_pm1.Image) }; var rs = ym.PredictBatch(ms, cf, io); _mbatch.Clear(); for (int j = 0; j < ms.Count; j++) { var bdf = new List<TestDefect>(); if (rs != null && j < rs.Count && rs[j].Boxes != null) for (int i = 0; i < rs[j].Boxes.Length; i++) { var bx = rs[j].BoxesN[i]; int cid = rs[j].ClassIds[i]; string tp = cid == 0 ? "搭舌缺陷" : cid == 1 ? "边缘问题" : cid == 2 ? "破损" : "缺陷" + cid; bdf.Add(new TestDefect(tp, new float[] { bx.X, bx.Y, bx.X + bx.Width, bx.Y + bx.Height }, rs[j].Scores[i])); } _mbatch.Add(BmpConverter.ToMat(Dv(ms[j], bdf))); } _mbi = 0; UpMPg(); inf = mn + ":" + ms.Count + "张"; MLog(inf); UI(() => { ShwMB(); _lblMT.Text = "耗时:" + sw.ElapsedMilliseconds + "ms"; _lblMI.Text = inf; }); return; }
						var mat = BmpConverter.ToMat((Bitmap)_pm1.Image); Mat cr = mat; float crv = (float)_nMCrop.Value; if (crv > 0 && crv < 1) { int ch = (int)(mat.Height * crv); cr = new Mat(mat, new CvRect(0, mat.Height - ch, mat.Width, ch)).Clone(); mat.Dispose(); }
						var res = ym.Predict(cr, cf, io); int cnt = res?.BoxesN?.Length ?? 0; var df = new List<TestDefect>(); if (res?.BoxesN != null) for (int i = 0; i < res.BoxesN.Length; i++) { var bx = res.BoxesN[i]; string tp = (idx == 2 || idx == 3) ? (res.ClassIds[i] == 0 ? "搭舌缺陷" : res.ClassIds[i] == 1 ? "边缘问题" : res.ClassIds[i] == 2 ? "破损" : "缺陷" + res.ClassIds[i]) : "类别" + res.ClassIds[i]; df.Add(new TestDefect(tp, new float[] { bx.X, bx.Y, bx.X + bx.Width, bx.Y + bx.Height }, res.Scores[i])); }
						rdr = Dv(cr, df); cr.Dispose(); inf = mn + ":" + cnt + "个目标"; MLog(inf + " 推理" + (res != null ? res.InferenceTimeMs.ToString("F0") : "?") + "ms");
					}
					else if (idx == 1 && _aiModels.FrontOcrModel != null)
					{
						var mat = BmpConverter.ToMat((Bitmap)_pm1.Image);
						int hh = mat.Height, ww = mat.Width, hp8 = 4;
						int bw = ww / hp8, sy = hh * 2 / 3;
						var df = new List<TestDefect>();
						var pnRegex = new Regex(@"P\d+", RegexOptions.IgnoreCase);
						for (int i = 0; i < hp8; i++)
						{
							int sx = i * bw, rw = (i < hp8 - 1) ? bw : (ww - sx), rh = hh - sy;
							if (rw <= 0 || rh <= 0) continue;
							using (var roi = new Mat(mat, new CvRect(sx, sy, rw, rh)).Clone())
							{
								ResponseList<OcrResponse> ocrR;
								if (_aiModels.FrontOcrModel.Run(roi, out ocrR) == 0 && ocrR != null && ocrR.Count > 0)
								{
									foreach (var rt in ocrR)
									{
										if (rt.Item2.Blocks == null) continue;
										foreach (var blk in rt.Item2.Blocks)
										{
											if (string.IsNullOrWhiteSpace(blk.Label)) continue;
											var m = pnRegex.Match(blk.Label);
											if (!m.Success || m.Value.Length < 6) continue;
											float[] nb = PnNormBox(blk, ww, hh, sx, sy);
											df.Add(new TestDefect("P号:" + m.Value.ToUpper(), nb, 0.9f));
										}
									}
								}
							}
						}
						rdr = Dv(mat, df); mat.Dispose();
						inf = "P号码OCR:" + df.Count + "个"; MLog(inf);
					}
					else if (idx == 5) { inf = "日期码OCR(模型待提供)"; MLog(inf); }
					else if (idx == 7 && _aiModels.HookSlightModel != null) { var mat = BmpConverter.ToMat((Bitmap)_pm1.Image); var sr = _aiModels.HookSlightModel.Predict(mat, cf); int cnt = sr?.BoxesN?.Length ?? 0; var df = new List<TestDefect>(); if (sr?.BoxesN != null) for (int i = 0; i < sr.BoxesN.Length; i++) { var bx = sr.BoxesN[i]; df.Add(new TestDefect("轻微挂钩错位", new float[] { bx.X, bx.Y, bx.X + bx.Width, bx.Y + bx.Height }, sr.Scores != null && i < sr.Scores.Length ? sr.Scores[i] : 0)); } rdr = Dv(mat, df); mat.Dispose(); inf = "轻微挂钩错位:" + cnt + "个"; MLog(inf); }
					else if (idx == 8 && _aiModels.BackHookModel != null && _aiModels.HookSlightModel != null) { var mat = BmpConverter.ToMat((Bitmap)_pm1.Image); var r1 = _aiModels.BackHookModel.Predict(mat, cf, io); var r2 = _aiModels.HookSlightModel.Predict(mat, cf); var df = new List<TestDefect>(); if (r1?.BoxesN != null) for (int i = 0; i < r1.BoxesN.Length; i++) { if (r1.ClassIds[i] == 1) { var bx = r1.BoxesN[i]; df.Add(new TestDefect("挂钩明显错位", new float[] { bx.X, bx.Y, bx.X + bx.Width, bx.Y + bx.Height }, r1.Scores[i])); } } if (r2?.BoxesN != null) for (int i = 0; i < r2.BoxesN.Length; i++) { var bx = r2.BoxesN[i]; df.Add(new TestDefect("轻微挂钩错位", new float[] { bx.X, bx.Y, bx.X + bx.Width, bx.Y + bx.Height }, r2.Scores != null && i < r2.Scores.Length ? r2.Scores[i] : 0)); } rdr = Dv(mat, df); mat.Dispose(); int c1 = r1?.BoxesN?.Length ?? 0, c2 = r2?.BoxesN?.Length ?? 0; inf = "挂钩综合:明显" + c1 + "轻微" + c2; MLog(inf); }
					else { inf = "暂不支持"; MLog(inf); }

					sw.Stop(); Bitmap fr = rdr; string fi = inf; UI(() => { if (fr != null) _pm2.Image = fr; _lblMT.Text = "耗时:" + sw.ElapsedMilliseconds + "ms"; _lblMI.Text = fi; });
				});
			}
			catch (Exception ex) { MLog("失败:" + ex.Message); }
			finally { UI(() => _bMRun.Enabled = true); }
		}

		// ====== RENDER HELPERS (与主界面一致的绘制风格) ======
		void Cl(DetResult r, int s, int e, string tp, Dictionary<int, List<TestDefect>> d) { if (r?.Boxes == null) return; int t = e - s; if (t <= 0) return; foreach (var b in r.Boxes) { float cx = (b.X + b.Width / 2f) / r.OrigImg.Width; int idx = s + (int)(cx * t); if (idx < s || idx >= e) continue; if (!d.ContainsKey(idx)) d[idx] = new List<TestDefect>(); d[idx].Add(new TestDefect(tp, new float[] { b.X, b.Y, b.X + b.Width, b.Y + b.Height }, 0)); } }
		void ClN(DetResult r, int s, int e, string tp, Dictionary<int, List<TestDefect>> d) { if (r?.BoxesN == null) return; int t = e - s; if (t <= 0) return; foreach (var b in r.BoxesN) { float cx = b.X + b.Width / 2f; int idx = s + (int)(cx * t); if (idx < s || idx >= e) continue; if (!d.ContainsKey(idx)) d[idx] = new List<TestDefect>(); d[idx].Add(new TestDefect(tp, new float[] { b.X, b.Y, b.X + b.Width, b.Y + b.Height }, 0)); } }
		void Cs(SegResult r, int s, int e, string tp, Dictionary<int, List<TestDefect>> d) { if (r?.Boxes == null) return; int t = e - s; if (t <= 0) return; foreach (var b in r.Boxes) { float cx = (b.X + b.Width / 2f) / r.OrigImg.Width; int idx = s + (int)(cx * t); if (idx < s || idx >= e) continue; if (!d.ContainsKey(idx)) d[idx] = new List<TestDefect>(); d[idx].Add(new TestDefect(tp, new float[] { b.X, b.Y, b.X + b.Width, b.Y + b.Height }, 0)); } }
		Dictionary<int, List<TestDefect>> Fd(Dictionary<int, List<TestDefect>> s, int st, int en) { var r = new Dictionary<int, List<TestDefect>>(); foreach (var kv in s) if (kv.Key >= st && kv.Key < en) r[kv.Key] = kv.Value; return r; }

		float[] PnNormBox(SmartMore.ViMo.TextBlock blk, int fw, int fh, int ox, int oy)
		{
			if (blk.Polygon == null || !blk.Polygon.Any()) return new float[] { 0, 0, 0.1f, 0.1f };
			float mix = float.MaxValue, miy = float.MaxValue, mxx = float.MinValue, mxy = float.MinValue;
			foreach (var pt in blk.Polygon) { float gx = pt.X + ox, gy = pt.Y + oy; if (gx < mix) mix = gx; if (gy < miy) miy = gy; if (gx > mxx) mxx = gx; if (gy > mxy) mxy = gy; }
			return new float[] { mix / fw, miy / fh, mxx / fw, mxy / fh };
		}

		Bitmap Dv(Mat m, List<TestDefect> df)
		{
			var bmp = m.ToBitmap(); int w = bmp.Width, h = bmp.Height;
			using (var g = Graphics.FromImage(bmp))
			{
				g.SmoothingMode = SmoothingMode.AntiAlias;
				foreach (var d in df)
				{
					float[] b = d.Box;
					int x1 = (int)(b[0] * w), y1 = (int)(b[1] * h), x2 = (int)(b[2] * w), y2 = (int)(b[3] * h);
					if (x2 <= x1 || y2 <= y1) continue;
					var rc = new Rect(x1, y1, x2 - x1, y2 - y1);
					Color c = NgC;
					if (d.Type.StartsWith("P号:")) c = PnGreen;
					else if (d.Type.Contains("搭舌")) c = Color.FromArgb(230, 126, 34);
					else if (d.Type.Contains("边缘")) c = Color.FromArgb(155, 89, 182);
					else if (d.Type.Contains("挂钩")) c = Color.DarkRed;
					else if (d.Type.Contains("条形码")) c = PriC;
					using (var fl = new SolidBrush(Color.FromArgb(50, c))) g.FillRectangle(fl, rc);
					using (var pn = new Pen(c, 3)) g.DrawRectangle(pn, rc);
					string lb = d.Type + " " + d.Score.ToString("F2");
					using (var f = new Font("微软雅黑", 10, FontStyle.Bold))
					{
						var sz = g.MeasureString(lb, f);
						int ly = y1 - (int)sz.Height - 6; if (ly < 4) ly = y1 + 4;
						using (var bg = new SolidBrush(c)) g.FillRectangle(bg, x1, ly, sz.Width + 8, sz.Height + 4);
						g.DrawString(lb, f, Brushes.White, x1 + 3, ly + 1);
					}
				}
				if (df.Count == 0) g.DrawString("OK", new Font("微软雅黑", 14, FontStyle.Bold), Brushes.Green, 10, 10);
			}
			return bmp;
		}

		Bitmap DrM(Mat m, Dictionary<int, List<TestDefect>> df, List<string> st, int si, int ei, bool rev)
		{
			var bmp = m.ToBitmap(); int w = bmp.Width, h = bmp.Height, t = ei - si, p = st.Count;
			using (var g = Graphics.FromImage(bmp))
			{
				g.SmoothingMode = SmoothingMode.AntiAlias;
				foreach (var kv in df)
				{
					foreach (var d in kv.Value)
					{
						float[] b = d.Box;
						int x1 = (int)(b[0] * w), y1 = (int)(b[1] * h), x2 = (int)(b[2] * w), y2 = (int)(b[3] * h);
						if (x2 <= x1 || y2 <= y1) continue;
						Color c = d.Type.StartsWith("P号:") ? PnGreen : NgC;
						if (d.Type.Contains("搭舌")) c = Color.FromArgb(230, 126, 34);
						else if (d.Type.Contains("边缘")) c = Color.FromArgb(155, 89, 182);
						else if (d.Type.Contains("挂钩")) c = Color.DarkRed;
						else if (d.Type.Contains("条形码")) c = PriC;
						using (var fl = new SolidBrush(Color.FromArgb(50, c))) g.FillRectangle(fl, new Rect(x1, y1, x2 - x1, y2 - y1));
						using (var pn = new Pen(c, 3)) g.DrawRectangle(pn, x1, y1, x2 - x1, y2 - y1);
						string lb = d.Type;
						if (lb.Length > 14) lb = lb.Substring(0, 14);
						using (var f = new Font("微软雅黑", 9, FontStyle.Bold))
						{
							var sz = g.MeasureString(lb, f);
							int ly = y1 - (int)sz.Height - 4; if (ly < 4) ly = y1 + 4;
							using (var bg = new SolidBrush(c)) g.FillRectangle(bg, x1, ly, sz.Width + 6, sz.Height + 4);
							g.DrawString(lb, f, Brushes.White, x1 + 2, ly + 1);
						}
					}
				}
				if (t > 1) using (var dp = new Pen(Color.FromArgb(120, 120, 120), 1) { DashStyle = DashStyle.Dash }) for (int i = 1; i < t; i++) g.DrawLine(dp, i * w / t, 0, i * w / t, h);
				using (var sf = new Font("微软雅黑", 28, FontStyle.Bold))
				using (var nf = new Font("微软雅黑", 16, FontStyle.Bold))
					for (int i = 0; i < t && si + i < st.Count; i++)
					{
						string ss = st[si + i], disp = ss == "OK" ? "OK" : (ss.Length > 4 ? ss.Substring(0, 4) : ss);
						Color cc = ss == "OK" ? OkC : NgC;
						float cx = (i + 0.5f) * w / t;
						var ts = g.MeasureString(disp, sf);
						using (var br = new SolidBrush(cc)) g.DrawString(disp, sf, br, cx - ts.Width / 2, 60);
						int boxNum = rev ? (p - (si + i)) : (si + i + 1);
						var bs = g.MeasureString("盒" + boxNum, nf);
						using (var br = new SolidBrush(Color.Yellow)) g.DrawString("盒" + boxNum, nf, br, cx - bs.Width / 2, 120);
					}
			}
			return bmp;
		}

		Bitmap Mg(Bitmap l, Bitmap r) { var mg = new Bitmap(l.Width + r.Width, Math.Max(l.Height, r.Height)); using (var g = Graphics.FromImage(mg)) { g.Clear(Color.Black); g.DrawImage(l, 0, (mg.Height - l.Height) / 2); g.DrawImage(r, l.Width, (mg.Height - r.Height) / 2); using (var pn = new Pen(Color.White, 2)) g.DrawLine(pn, l.Width, 0, l.Width, mg.Height); } l.Dispose(); r.Dispose(); return mg; }
		void Gp(List<string> st, Dictionary<int, List<TestDefect>> df) { _grd.Rows.Clear(); for (int i = 0; i < st.Count; i++) { if (df.ContainsKey(i)) foreach (var d in df[i]) _grd.Rows.Add(i + 1, "NG", d.Type, d.Score.ToString("F3")); else _grd.Rows.Add(i + 1, "OK", "-", "-"); } }
		void ShwB() { if (_batch.Count == 0 || _bi >= _batch.Count) return; _po.Image = BmpConverter.ToBitmap(_batch[_bi]); UpPg(); }
		void UpPg() { _lblPg.Text = _batch.Count > 0 ? (_bi + 1) + "/" + _batch.Count : ""; _bPrv.Enabled = _bNxt.Enabled = _batch.Count > 1; }
		void ShwMB() { if (_mbatch.Count == 0 || _mbi >= _mbatch.Count) return; _pm2.Image = BmpConverter.ToBitmap(_mbatch[_mbi]); UpMPg(); }
		void UpMPg() { _lblMPg.Text = _mbatch.Count > 0 ? (_mbi + 1) + "/" + _mbatch.Count : ""; _bMPrv.Enabled = _bMNxt.Enabled = _mbatch.Count > 1; }
		void Clr() { if (_m1 != null) { _m1.Dispose(); _m1 = null; } if (_m2 != null) { _m2.Dispose(); _m2 = null; } if (_m3 != null) { _m3.Dispose(); _m3 = null; } _batch.Clear(); _bi = 0; _pi1.Image = null; _pi2.Image = null; _po.Image = null; _bRun.Enabled = false; _grd.Rows.Clear(); UpPg(); }

		protected override void OnFormClosing(FormClosingEventArgs e) { VisionMeasure.MainFrm.ManualTestMode = false; SaveCfg(); Clr(); _mbatch.Clear(); base.OnFormClosing(e); }
	}
}
