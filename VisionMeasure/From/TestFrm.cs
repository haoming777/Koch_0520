using Config;
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
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using VisionMeasure.Utils;
using CommonLib;
using XL.Controls;
using YoloInference;
using YoloSegmentationEnd2End;
using BmpCvt = OpenCvSharp.Extensions.BitmapConverter;
using CvR = OpenCvSharp.Rect;
using Pt = System.Drawing.Point;
using Sz = System.Drawing.Size;
using Rc = System.Drawing.Rectangle;
using DetResult = YoloInference.YoloResult;
using SegResult = YoloSegmentationEnd2End.YoloResult;
using Newtonsoft.Json;

namespace VisionMeasure.From
{
	internal class TestDefect { public string Type; public float[] Box; public float Score; public TestDefect(string t, float[] b, float s) { Type = t; Box = b; Score = s; } }

	public partial class TestForm : UIForm
	{
		static readonly Color PriC = Color.FromArgb(0, 122, 204), OkC = Color.FromArgb(39, 174, 96), NgC = Color.FromArgb(231, 76, 60),
			BgC = Color.FromArgb(245, 247, 250), CardC = Color.White, PnG = Color.Lime;

		AiModelManager _ai; DaHuaSDK[] _cam; SkuData _sku = new SkuData { SkuNumber = "TEST", P = 8, Z = 2, MM = 42 };
		double _tMs; TabControl _tab; Font _f8 = new Font("微软雅黑", 8F), _f9 = new Font("微软雅黑", 9F), _f10b = new Font("微软雅黑", 10F, FontStyle.Bold);

		// ====== 工位测试 ======
		string _st = "正面"; Mat _s1, _s2, _s3; List<Mat> _sBatch = new List<Mat>(); int _sBi;
		UIComboBox _cmbSt; NumericUpDown _nP, _nConf, _nIou, _nCrop, _nThick, _nBlue, _nHole;
		CheckBox _chkRev, _chkPNum, _chkBc, _chkDc;
		UIButton _bImg, _bCam, _bRun, _bPrev, _bNext, _bSaveSt;
		XLPictureBox _pi1, _pi2, _po; UILabel _lblT, _lblPg; DataGridView _grd; RichTextBox _rLog;

		// ====== 模型测试 ======
		UIComboBox _cmbM; Panel _pnlParams; ModelParams _curMp; string _curKey;
		Mat _mM1, _mM2; Bitmap _mB1, _mB2;
		UIButton _bMI1, _bMI2, _bMRun, _bMSave;
		UILabel _lblMI1, _lblMI2, _lblMT, _lblMS;
		XLPictureBox _pm1, _pm2, _pm3; RichTextBox _rMLog;

		string[] _mKeys = { "front_box", "front_pcode", "endface_upper", "endface_lower", "barcode",
			"hook", "hook_slight", "side", "dc_seg", "dc_cls", "dc_ocr", "dc_all" };
		string[] _mNames = { "正面-盒子破", "正面-P号码OCR", "上端面-缺陷", "下端面-缺陷", "背面-条码检测",
			"背面-挂钩明显", "背面-挂钩轻微", "侧面-缺陷",
			"背面-日期码C1分割", "背面-日期码C2分类", "背面-日期码C3OCR", "背面-日期码综合" };

		public TestForm(MotionControlManager m, CameraManager cm, AiModelManager a = null) { _cam = null; _ai = a; Bld(); this.Load += OnLd; }
		public TestForm(MotionControlManager m, DaHuaSDK[] c, AiModelManager a = null) { _cam = c; _ai = a; Bld(); this.Load += OnLd; }

		void OnLd(object s, EventArgs e)
		{ VisionMeasure.MainFrm.ManualTestMode = true; if (_ai == null) LoadAi(); _cmbSt.SelectedIndex = 0; _cmbM.SelectedIndex = 0; LogS("就绪"); }

		void LoadAi()
		{ try { _ai = new AiModelManager(ModelPathConfig.LoadFromSysConfig()); _ai.LoadAllModels(); LogS("模型加载完成"); } catch (Exception ex) { LogS("模型加载失败:" + ex.Message, true); } }

		void Bld()
		{
			Text = "KOCH 测试工具"; Size = new Sz(1620, 1020); StartPosition = FormStartPosition.CenterParent; BackColor = BgC;
			_tab = new TabControl { Dock = DockStyle.Fill, Font = _f9 };
			_tab.TabPages.Add(BuildStationTab()); _tab.TabPages.Add(BuildModelTab());
			Controls.Add(_tab);
		}

		// ================================================================
		// 工位测试
		// ================================================================
		TabPage BuildStationTab()
		{
			var pg = new TabPage("工位测试"); var lo = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
			lo.RowStyles.Add(new RowStyle(SizeType.Absolute, 190)); lo.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); lo.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
			lo.Controls.Add(TopSt(), 0, 0); lo.Controls.Add(ImgSt(), 0, 1); lo.Controls.Add(BtmSt(), 0, 2); pg.Controls.Add(lo); return pg;
		}

		Panel TopSt()
		{
			var pn = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(6), BackColor = BgC };

			var c1 = new Panel { Width = 240, Height = 150, BackColor = CardC, Margin = new Padding(3) };
			_cmbSt = MakeCombo(new Pt(10, 10), 260, new[] { "正面", "背面", "上端面", "下端面", "侧面" }, 0);
			_cmbSt.SelectedIndexChanged += (s, e) => { _st = _cmbSt.SelectedItem.ToString(); ClrSt(); };
			_nP = MakeNum(8, 1, 20, 0); _nP.Location = new Pt(55, 45); AddLbl("P数:", 10, 47, 40, c1); c1.Controls.Add(_nP);
			_nCrop = MakeNum(0.33m, 0, 1); _nCrop.Location = new Pt(55, 72); AddLbl("裁底:", 10, 74, 40, c1); c1.Controls.Add(_nCrop);
			_nConf = MakeNum(0.5m, 0.05m, 1.0m); _nConf.Location = new Pt(55, 99); AddLbl("Conf:", 10, 101, 40, c1); c1.Controls.Add(_nConf);
			_nIou = MakeNum(0.45m, 0.05m, 1.0m); _nIou.Location = new Pt(150, 99); AddLbl("IOU:", 123, 101, 30, c1); c1.Controls.Add(_nIou);
			_nThick = MakeNum(30, 1, 200, 0); _nThick.Location = new Pt(55, 126); AddLbl("厚度:", 10, 128, 40, c1); c1.Controls.Add(_nThick);
			_nBlue = MakeNum(0, 0, 10, 0); _nBlue.Location = new Pt(55, 153); AddLbl("蓝区:", 10, 155, 40, c1); c1.Controls.Add(_nBlue);
			_nHole = MakeNum(1, 0, 10, 0); _nHole.Location = new Pt(140, 153); AddLbl("孔:", 115, 155, 25, c1); c1.Controls.Add(_nHole);
			_chkRev = MakeChk("反转盒序", false, new Pt(10, 175)); c1.Controls.Add(_chkRev);
			_chkPNum = MakeChk("P号检测", false, new Pt(100, 175)); c1.Controls.Add(_chkPNum);
			_chkBc = MakeChk("条码检测", false, new Pt(190, 175)); c1.Controls.Add(_chkBc);
			_chkDc = MakeChk("日期码", false, new Pt(10, 192)); c1.Controls.Add(_chkDc);
			pn.Controls.Add(c1); c1.Controls.Add(_cmbSt);

			var c2 = new Panel { Width = 240, Height = 150, BackColor = CardC, Margin = new Padding(2) };
			_bImg = MakeBtn("加载离线图", new Pt(10, 12), 240, 32, BtnStImg); c2.Controls.Add(_bImg);
			_bCam = MakeBtn("相机采图", new Pt(10, 48), 240, 28, BtnStCam); _bCam.FillColor = PriC; c2.Controls.Add(_bCam);
			_bRun = MakeBtn("执行检测", new Pt(10, 85), 240, 45, BtnStRun); _bRun.FillColor = OkC; _bRun.Font = new Font("微软雅黑", 11F, FontStyle.Bold); _bRun.Enabled = false; c2.Controls.Add(_bRun);
			_lblT = new UILabel { Text = "---", Location = new Pt(10, 138), Size = new Sz(240, 16), TextAlign = ContentAlignment.MiddleCenter, Font = _f8 }; c2.Controls.Add(_lblT);
			_bPrev = MakeBtn("<", new Pt(40, 158), 45, 22, (s2, e2) => { if (_sBatch.Count > 0) { _sBi = (_sBi - 1 + _sBatch.Count) % _sBatch.Count; ShwStB(); } }); _bPrev.Enabled = false; c2.Controls.Add(_bPrev);
			_bNext = MakeBtn(">", new Pt(175, 158), 45, 22, (s2, e2) => { if (_sBatch.Count > 0) { _sBi = (_sBi + 1) % _sBatch.Count; ShwStB(); } }); _bNext.Enabled = false; c2.Controls.Add(_bNext);
			_lblPg = new UILabel { Text = "", Location = new Pt(90, 160), Size = new Sz(80, 16), TextAlign = ContentAlignment.MiddleCenter, Font = _f8 }; c2.Controls.Add(_lblPg);
			pn.Controls.Add(c2);

			var c3 = new Panel { Width = 180, Height = 150, BackColor = CardC, Margin = new Padding(2) };
			_bSaveSt = MakeBtn("保存参数", new Pt(10, 10), 180, 30, (s2, e2) => SaveStCfg()); c3.Controls.Add(_bSaveSt);
			pn.Controls.Add(c3);

			// 独立测试界面入口
			var c4 = new Panel { Width = 340, Height = 170, BackColor = CardC, Margin = new Padding(2) };
			AddLbl("独立测试", 10, 2, 300, c4, _f10b);
			var b1 = MakeBtn("端面测试", new Pt(10, 25), 100, 32, (s2, e2) => new EndFaceTestForm(_ai).ShowDialog()); b1.FillColor = PriC;
			var b2 = MakeBtn("侧面测试", new Pt(120, 25), 100, 32, (s2, e2) => new SideTestForm(_ai).ShowDialog()); b2.FillColor = PriC;
			var b3 = MakeBtn("日期码测试", new Pt(10, 62), 100, 32, (s2, e2) => new DateCodeTestForm(_ai).ShowDialog()); b3.FillColor = PriC;
			var b4 = MakeBtn("条码测试", new Pt(120, 62), 100, 32, (s2, e2) => new BarcodeTestForm().ShowDialog()); b4.FillColor = PriC;
			AddLbl("正面/背面/挂钩：正在开发", 10, 100, c4);
			c4.Controls.AddRange(new Control[] { b1, b2, b3, b4 });
			pn.Controls.Add(c4);
			return pn;
		}

		Panel ImgSt() { var p = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 }; _pi1 = MakePb(); _pi2 = MakePb(); _po = MakePb(); p.Controls.Add(WrapPb(_pi1, "输入1"), 0, 0); p.Controls.Add(WrapPb(_pi2, "输入2"), 1, 0); p.Controls.Add(WrapPb(_po, "结果"), 2, 0); return p; }

		Panel BtmSt()
		{
			var p = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
			_grd = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Color.White, Font = _f9 };
			_grd.Columns.Add("#", "#"); _grd.Columns.Add("状态", "状态"); _grd.Columns.Add("缺陷", "缺陷"); _grd.Columns.Add("置信度", "置信度");
			_rLog = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(30, 30, 30), ForeColor = Color.FromArgb(180, 180, 180), Font = new Font("Consolas", 9F), BorderStyle = BorderStyle.None };
			p.Controls.Add(WrapCtrl(_grd, "结果列表"), 0, 0); p.Controls.Add(WrapCtrl(_rLog, "日志"), 1, 0); return p;
		}

		// ================================================================
		// 模型测试
		// ================================================================
		TabPage BuildModelTab()
		{
			var pg = new TabPage("模型测试"); var lo = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3 };
			lo.RowStyles.Add(new RowStyle(SizeType.Absolute, 200)); lo.RowStyles.Add(new RowStyle(SizeType.Percent, 50)); lo.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
			lo.Controls.Add(TopM(), 0, 0); lo.Controls.Add(ImgM(), 0, 1); lo.Controls.Add(BtmM(), 0, 2); pg.Controls.Add(lo); return pg;
		}

		Panel TopM()
		{
			var pn = new FlowLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(6), BackColor = BgC };

			var c1 = new Panel { Width = 320, Height = 185, BackColor = CardC, Margin = new Padding(2) };
			AddLbl("选择模型", 10, 5, 300, c1, _f10b);
			_cmbM = MakeCombo(new Pt(10, 26), 300, _mNames, 0);
			_cmbM.SelectedIndexChanged += OnMdlChange;
			c1.Controls.Add(_cmbM); pn.Controls.Add(c1);

			_pnlParams = new Panel { Width = 600, Height = 185, BackColor = CardC, Margin = new Padding(2), AutoScroll = true };
			pn.Controls.Add(_pnlParams);

			var c3 = new Panel { Width = 620, Height = 185, BackColor = CardC, Margin = new Padding(2) };
			_bMI1 = new UIButton { Text = "加载左图", Location = new Pt(10, 10), Size = new Sz(145, 38), Font = _f9, Radius = 6, Cursor = Cursors.Hand }; _bMI1.Click += BtnMI1;
			_bMI2 = new UIButton { Text = "加载右图", Location = new Pt(165, 10), Size = new Sz(145, 38), Font = _f9, Radius = 6, Cursor = Cursors.Hand, FillColor = PriC }; _bMI2.Click += BtnMI2;
			_lblMI1 = new UILabel { Text = "左图:未加载", Location = new Pt(10, 52), Size = new Sz(145, 16), Font = _f8, ForeColor = Color.Gray };
			_lblMI2 = new UILabel { Text = "右图:未加载", Location = new Pt(165, 52), Size = new Sz(145, 16), Font = _f8, ForeColor = Color.Gray };
			_bMRun = new UIButton { Text = "▶ 执行推理", Location = new Pt(320, 10), Size = new Sz(140, 80), Font = new Font("微软雅黑", 12F, FontStyle.Bold), Radius = 8, Cursor = Cursors.Hand, FillColor = OkC, Enabled = false }; _bMRun.Click += BtnMRun;
			_bMSave = MakeBtn("保存参数", new Pt(470, 10), 140, 28, (s2, e2) => { ReadMP(); _curMp.Save(); LogM("已保存:" + _curMp.FilePath); });
			var bExport = MakeBtn("保存渲染图", new Pt(470, 42), 140, 28, (s2, e2) => { if (_pm3.Image != null) { using (var sd = new SaveFileDialog { Title = "保存渲染图", Filter = "PNG|*.png|JPG|*.jpg", DefaultExt = "png" }) { if (sd.ShowDialog() == DialogResult.OK) { _pm3.Image.Save(sd.FileName); LogM("已保存:" + sd.FileName); } } } });
			_lblMT = new UILabel { Text = "---", Location = new Pt(320, 94), Size = new Sz(280, 20), Font = new Font("微软雅黑", 10F, FontStyle.Bold), TextAlign = ContentAlignment.MiddleCenter };
			_lblMS = new UILabel { Text = "", Location = new Pt(10, 155), Size = new Sz(600, 20), Font = _f8, ForeColor = Color.Gray };
			c3.Controls.AddRange(new Control[] { _bMI1, _bMI2, _lblMI1, _lblMI2, _bMRun, _bMSave, bExport, _lblMT, _lblMS });
			pn.Controls.Add(c3);
			return pn;
		}

		Panel ImgM()
		{
			var p = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 1 };
			_pm1 = MakePb(); _pm2 = MakePb(); _pm3 = MakePb();
			p.Controls.Add(WrapPb(_pm1, "左图/输入"), 0, 0);
			p.Controls.Add(WrapPb(_pm2, "右图"), 1, 0);
			p.Controls.Add(WrapPb(_pm3, "推理结果"), 2, 0); return p;
		}

		Panel BtmM()
		{
			_rMLog = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(30, 30, 30), ForeColor = Color.FromArgb(180, 180, 180), Font = new Font("Consolas", 9F), BorderStyle = BorderStyle.None };
			return WrapCtrl(_rMLog, "推理日志");
		}

		// ====== 模型切换动态参数 ======
		void OnMdlChange(object s, EventArgs e)
		{
			int idx = _cmbM.SelectedIndex; if (idx < 0 || idx >= _mKeys.Length) return;
			_curKey = _mKeys[idx]; _curMp = ModelParams.Load(_curKey);
			BuildMPanel();
		}

		void BuildMPanel()
		{
			_pnlParams.Controls.Clear(); if (_curMp == null) return;
			var p = _curMp; string k = _curKey; int x = 8, y = 22;
			AddLbl("模型参数: " + p.ModelName, x, 0, 580, _pnlParams, _f10b);

			if (k == "front_box" || k == "hook" || k == "hook_slight" || k == "side" || k == "endface_upper" || k == "endface_lower")
			{ AddNumP("Conf", (decimal)p.Confidence, 0.05m, 1.0m, ref x, ref y); AddNumP("IOU", (decimal)p.Iou, 0.05m, 1.0m, ref x, ref y); }
			if (k == "hook" || k == "hook_slight") { AddNumP("厚度", (decimal)p.HookThickness, 1, 200, ref x, ref y, 0); AddNumP("蓝区ID", p.HookBlueClassId, 0, 10, ref x, ref y, 0); AddNumP("孔ID", p.HookHoleClassId, 0, 10, ref x, ref y, 0); }
			if (k == "endface_upper") { AddNumP("Conf上", (decimal)p.EndFaceUpperConf, 0.05m, 1.0m, ref x, ref y); AddNumP("IOU上", (decimal)p.EndFaceUpperIou, 0.05m, 1.0m, ref x, ref y); }
			if (k == "endface_lower") { AddNumP("Conf下", (decimal)p.EndFaceLowerConf, 0.05m, 1.0m, ref x, ref y); AddNumP("IOU下", (decimal)p.EndFaceLowerIou, 0.05m, 1.0m, ref x, ref y); }
			if (k == "side") { AddNumP("裁图比", (decimal)p.SideCropRatio, 0.5m, 5.0m, ref x, ref y); }
			if (k == "barcode") { y = 22; x = 8;
				AddChkP("预处理", p.BcEnablePreprocess, ref x, ref y); AddNumP("对比度", (decimal)p.BcContrastAlpha, 0.1m, 3.0m, ref x, ref y);
				AddNumP("亮度", p.BcBrightnessBeta, -100, 100, ref x, ref y, 0); AddChkP("高斯模糊", p.BcEnableGaussianBlur, ref x, ref y);
				AddChkP("中值模糊", p.BcEnableMedianBlur, ref x, ref y); AddChkP("直方均衡", p.BcEnableEqualizeHist, ref x, ref y);
				AddNumP("阈值模式", p.BcThresholdMode, 0, 3, ref x, ref y, 0);
				AddNumP("自适应块", p.BcAdaptiveBlockSize, 3, 99, ref x, ref y, 0, 2);
				AddNumP("自适应C", (decimal)p.BcAdaptiveC, -20, 20, ref x, ref y);
				AddNumP("固定阈值", p.BcFixedThreshold, 0, 255, ref x, ref y, 0);
				AddChkP("反色", p.BcEnableInvert, ref x, ref y); AddChkP("闭运算", p.BcEnableMorphClose, ref x, ref y);
				AddChkP("开运算", p.BcEnableMorphOpen, ref x, ref y); AddChkP("膨胀", p.BcEnableMorphDilate, ref x, ref y);
				AddChkP("腐蚀", p.BcEnableMorphErode, ref x, ref y);
				AddNumP("起始高%", (decimal)(p.BcStartHeightRatio * 100), 0, 100, ref x, ref y);
				AddChkP("最佳匹配", p.BcEnableFilterBestMatch, ref x, ref y);
				AddNumP("最小长度", p.BcMinBarcodeLength, 1, 20, ref x, ref y, 0);
				AddNumP("最大长度", p.BcMaxBarcodeLength, 5, 200, ref x, ref y, 0);
				AddChkP("TryHarder", p.BcTryHarder, ref x, ref y); AddChkP("旋转重试", p.BcEnableRotationRetry, ref x, ref y);
			}
			if (k == "datecode" || k.StartsWith("dc_")) { AddNumP("Conf", (decimal)p.Confidence, 0.05m, 1.0m, ref x, ref y); }
		}

		void AddNumP(string lb, decimal v, decimal mn, decimal mx, ref int x, ref int y, int dec = 2, int inc = 0)
		{ AddLbl(lb, x, y, 55, _pnlParams); var nu = MakeNum(v, mn, mx, dec, inc); nu.Location = new Pt(x + 58, y - 2); _pnlParams.Controls.Add(nu); x += 125; if (x > 560) { x = 8; y += 22; } }
		void AddChkP(string lb, bool v, ref int x, ref int y)
		{ var c = MakeChk(lb, v); c.Location = new Pt(x, y); _pnlParams.Controls.Add(c); x += c.PreferredSize.Width + 8; if (x > 560) { x = 8; y += 22; } }

		void ReadMP()
		{ if (_curMp == null) return; var p = _curMp; int ni = 0; string k = _curKey;
			foreach (Control c in _pnlParams.Controls) {
				if (c is NumericUpDown nu) { float v = (float)nu.Value;
					if (k == "barcode") { switch (ni) { case 0: p.BcContrastAlpha = v; break; case 1: p.BcBrightnessBeta = (int)v; break; case 2: p.BcThresholdMode = (int)v; break; case 3: p.BcAdaptiveBlockSize = (int)v; break; case 4: p.BcAdaptiveC = v; break; case 5: p.BcFixedThreshold = (int)v; break; case 6: p.BcStartHeightRatio = v / 100f; break; case 7: p.BcMinBarcodeLength = (int)v; break; case 8: p.BcMaxBarcodeLength = (int)v; break; } }
					else { if (ni == 0) p.Confidence = v; else if (ni == 1) p.Iou = v; else if (ni == 2) p.HookThickness = v; }
					ni++; }
				else if (c is CheckBox chk) { if (k == "barcode") { switch (ni) { case 0: p.BcEnablePreprocess = chk.Checked; break; case 1: p.BcEnableGaussianBlur = chk.Checked; break; case 2: p.BcEnableMedianBlur = chk.Checked; break; case 3: p.BcEnableEqualizeHist = chk.Checked; break; case 4: p.BcEnableInvert = chk.Checked; break; case 5: p.BcEnableMorphClose = chk.Checked; break; case 6: p.BcEnableMorphOpen = chk.Checked; break; case 7: p.BcEnableMorphDilate = chk.Checked; break; case 8: p.BcEnableMorphErode = chk.Checked; break; case 9: p.BcEnableFilterBestMatch = chk.Checked; break; case 10: p.BcTryHarder = chk.Checked; break; case 11: p.BcEnableRotationRetry = chk.Checked; break; } ni++; } }
			}
		}

		// ====== 工位测试逻辑 ======
		void BtnStImg(object s, EventArgs e)
		{ bool fb = _st == "正面" || _st == "背面", ef = _st == "上端面" || _st == "下端面";
			using (var d = new OpenFileDialog { Title = "选择图像", Filter = "图像|*.jpg;*.jpeg;*.png;*.bmp;*.tif", Multiselect = fb || ef }) { if (d.ShowDialog() != DialogResult.OK) return; ClrSt(); try {
				if (fb) { if (d.FileNames.Length < 2) { MessageBox.Show("需要2张"); return; } _s1 = Cv2.ImRead(d.FileNames[0]); _s2 = Cv2.ImRead(d.FileNames[1]); _pi1.Image = BmpCvt.ToBitmap(_s1); _pi2.Image = BmpCvt.ToBitmap(_s2); }
				
				else { _s3 = Cv2.ImRead(d.FileName); _pi1.Image = BmpCvt.ToBitmap(_s3); }
				_bRun.Enabled = true; LogS("图像就绪");
			} catch (Exception ex) { LogS("加载失败:" + ex.Message, true); } } }
		void BtnStCam(object s, EventArgs e) { if (_cam == null) { MessageBox.Show("相机未初始化"); return; } int ci = _st == "背面" ? 4 : _st == "上端面" ? 5 : _st == "下端面" ? 6 : 0; if (_cam[ci] == null) { MessageBox.Show("Cam" + (ci+1) + "未连接"); return; } var c2 = _cam[ci]; Task.Run(() => { try { c2.setTriggerSource(0); Thread.Sleep(50); c2.ExecuteSoftwareTrigger(); Thread.Sleep(100); c2.setTriggerSource(1); InvokeUI(() => LogS("Cam" + (ci+1) + "触发完成")); } catch (Exception ex) { InvokeUI(() => LogS("失败:" + ex.Message, true)); } }); }

		async void BtnStRun(object s, EventArgs e)
		{ if (_ai == null) { MessageBox.Show("模型未加载"); return; } _bRun.Enabled = false; var sw = Stopwatch.StartNew(); LogS("===== " + _st + " =====");
			try { await Task.Run(() => DoStation()); sw.Stop(); InvokeUI(() => { _lblT.Text = "耗时:" + sw.ElapsedMilliseconds + "ms"; }); LogS("完成:" + sw.ElapsedMilliseconds + "ms"); }
			catch (Exception ex) { LogS("失败:" + ex.Message, true); } finally { InvokeUI(() => _bRun.Enabled = true); } }

		void DoStation()
		{ int p = (int)_nP.Value; float cf = (float)_nConf.Value, io = (float)_nIou.Value; var st = new List<string>(p); for (int i2 = 0; i2 < p; i2++) st.Add("OK"); var ad = new Dictionary<int, List<TestDefect>>();
			if (_st == "正面") { int h = p/2; if (_ai.FrontBoxBreakModel != null) { var lr = _ai.FrontBoxBreakModel.Predict(_s1, cf, io); var rr = _ai.FrontBoxBreakModel.Predict(_s2, cf, io); ClN2(lr, 0, h, "盒子破损", ad); ClN2(rr, h, p, "盒子破损", ad); }
				if (_chkPNum.Checked && _ai.FrontOcrModel != null) DoPnumSt(_s1, _s2, h, ad); }
			if (_st == "背面") { int h = p/2; if (_ai.BackHookModel != null) { var lr = _ai.BackHookModel.Predict(_s1, cf, io); var rr = _ai.BackHookModel.Predict(_s2, cf, io); Cl2(lr, 0, h, "挂钩明显错位", ad); Cl2(rr, h, p, "挂钩明显错位", ad); }
				if (_ai.HookSlightModel != null) { var lr = _ai.HookSlightModel.Predict(_s1, cf); var rr = _ai.HookSlightModel.Predict(_s2, cf); Cs2(lr, 0, h, "轻微挂钩错位", ad); Cs2(rr, h, p, "轻微挂钩错位", ad); }
				if (_chkBc.Checked) DoBcSt(_s1, h, ad); if (_chkDc.Checked && _ai.BackDateCodeOcrModel != null) DoDcSt(_s1, h, ad); }
			if (_st == "上端面" || _st == "下端面") { var mdl2 = _st == "上端面" ? _ai.EndFaceUpperModel : _ai.EndFaceLowerModel; var ms2 = _sBatch.Count > 0 ? _sBatch : (_s3 != null ? new List<Mat> { _s3 } : new List<Mat>()); if (mdl2 != null && ms2.Count > 0) { var sw2 = Stopwatch.StartNew(); var rs2 = mdl2.PredictBatch(ms2, cf, io); _tMs = sw2.Elapsed.TotalMilliseconds; _sBatch.Clear(); for (int j = 0; j < ms2.Count; j++) { var dfB = new List<TestDefect>(); if (rs2 != null && j < rs2.Count && rs2[j].BoxesN != null) for (int ii = 0; ii < rs2[j].Boxes.Length; ii++) { var bx = rs2[j].BoxesN[ii]; int cid = rs2[j].ClassIds[ii]; dfB.Add(new TestDefect(cid==0?"搭舌缺陷":cid==1?"边缘问题":cid==2?"破损":"缺陷"+cid, new float[]{bx.X,bx.Y,bx.X+bx.Width,bx.Y+bx.Height}, rs2[j].Scores[ii])); } _sBatch.Add(BmpCvt.ToMat(DrawStImg(ms2[j], dfB))); } _sBi=0; UpStPg(); InvokeUI(()=>{ShwStB();_grd.Rows.Clear();_grd.Rows.Add("-",_sBatch.Count+"张完成","-","-");}); LogS((_st=="上端面"?"上":"下")+"端面:"+ms2.Count+"张,"+_tMs.ToString("F0")+"ms"); return; } }
			if (_st == "侧面" && _ai.SideDefectModel != null) { var resS = _ai.SideDefectModel.Predict(_s3, cf, io); var dfS = new List<TestDefect>(); if (resS?.BoxesN != null) for (int ii = 0; ii < resS.BoxesN.Length; ii++) { var bx = resS.BoxesN[ii]; dfS.Add(new TestDefect("缺陷"+resS.ClassIds[ii], new float[]{bx.X,bx.Y,bx.X+bx.Width,bx.Y+bx.Height}, resS.Scores[ii])); } var rdS = DrawStImg(_s3, dfS); InvokeUI(()=>{_po.Image=rdS;_grd.Rows.Clear();for(int ii=0;ii<dfS.Count;ii++)_grd.Rows.Add(ii+1,"NG",dfS[ii].Type,dfS[ii].Score.ToString("F3"));if(dfS.Count==0)_grd.Rows.Add("-","OK","-","-");}); return; }
			foreach (var kv in ad) if (kv.Value.Count > 0) st[kv.Key] = string.Join(",", kv.Value.ConvertAll(dd => dd.Type));
			bool rev = _chkRev.Checked; int hp2 = p/2;
			Bitmap mg = MergeBmps(DrStM2(_s1, Fd3(ad, 0, hp2), st, 0, hp2, rev), DrStM2(_s2, Fd3(ad, hp2, p), st, hp2, p, rev));
			InvokeUI(() => { _po.Image = mg; GpSt2(st, ad); }); }

		void DoPnumSt(Mat l, Mat r, int h, Dictionary<int, List<TestDefect>> ad)
		{ int hL = l.Height, wL = l.Width, hR = r.Height, wR = r.Width, bwL = wL/h, bwR = wR/h, syL = hL*2/3, syR = hR*2/3; var rx = new Regex(@"P\d+", RegexOptions.IgnoreCase);
			for (int i = 0; i < h; i++) { int sx = i*bwL, rw = (i<h-1)?bwL:(wL-sx), rh2 = hL-syL; if (rw<=0||rh2<=0) continue; using (var roi = new Mat(l, new CvR(sx, syL, rw, rh2)).Clone()) { ResponseList<OcrResponse> ocr; if (_ai.FrontOcrModel.Run(roi, out ocr) == 0 && ocr != null) foreach (var rt in ocr) { if (rt.Item2.Blocks == null) continue; foreach (var blk in rt.Item2.Blocks) { if (string.IsNullOrWhiteSpace(blk.Label)) continue; var m2 = rx.Match(blk.Label); if (!m2.Success || m2.Value.Length < 6) continue; if (!ad.ContainsKey(i)) ad[i] = new List<TestDefect>(); ad[i].Add(new TestDefect("P号:" + m2.Value.ToUpper(), PnBxSt(blk, wL, hL, sx, syL), 0.9f)); } } } }
			for (int j = 0; j < h; j++) { int gi = h+j, sx = j*bwR, rw = (j<h-1)?bwR:(wR-sx), rh2 = hR-syR; if (rw<=0||rh2<=0) continue; using (var roi = new Mat(r, new CvR(sx, syR, rw, rh2)).Clone()) { ResponseList<OcrResponse> ocr; if (_ai.FrontOcrModel.Run(roi, out ocr) == 0 && ocr != null) foreach (var rt in ocr) { if (rt.Item2.Blocks == null) continue; foreach (var blk in rt.Item2.Blocks) { if (string.IsNullOrWhiteSpace(blk.Label)) continue; var m2 = rx.Match(blk.Label); if (!m2.Success || m2.Value.Length < 6) continue; if (!ad.ContainsKey(gi)) ad[gi] = new List<TestDefect>(); ad[gi].Add(new TestDefect("P号:" + m2.Value.ToUpper(), PnBxSt(blk, wR, hR, sx, syR), 0.9f)); } } } } } 
		float[] PnBxSt(SmartMore.ViMo.TextBlock blk, int fw, int fh, int ox, int oy)
		{ if (blk.Polygon == null || !blk.Polygon.Any()) return new float[]{0,0,0.1f,0.1f}; float mx=float.MaxValue,my=float.MaxValue,Mx=float.MinValue,My=float.MinValue; foreach(var pt in blk.Polygon){float gx=pt.X+ox,gy=pt.Y+oy;if(gx<mx)mx=gx;if(gy<my)my=gy;if(gx>Mx)Mx=gx;if(gy>My)My=gy;} return new float[]{mx/fw,my/fh,Mx/fw,My/fh}; }
		void DoBcSt(Mat m, int h, Dictionary<int, List<TestDefect>> ad) { try { var bcp2 = ModelParams.Load("barcode"); int hh=m.Height,ww=m.Width,bw=ww/h,sy=(int)(hh*bcp2.BcStartHeightRatio); for(int i=0;i<h;i++){int sx=i*bw,rw=(i<h-1)?bw:(ww-sx),rh=hh-sy;if(rw<=0||rh<=0)continue;using(var roi=new Mat(m,new CvR(sx,sy,rw,rh)).Clone()){var pt=ApplyBcPre(roi,bcp2);using(pt)using(var bmp=pt.ToBitmap()){var rd2=new ZXing.BarcodeReader{AutoRotate=true,Options=new ZXing.Common.DecodingOptions{TryHarder=bcp2.BcTryHarder,PossibleFormats=new List<ZXing.BarcodeFormat>{ZXing.BarcodeFormat.CODE_128,ZXing.BarcodeFormat.EAN_13}}};var rs2=rd2.DecodeMultiple(bmp);string txt=rs2!=null&&rs2.Length>0?string.Join(",",rs2.Select(rr=>rr.Text)):"(无)";if(!ad.ContainsKey(i))ad[i]=new List<TestDefect>();ad[i].Add(new TestDefect("条码:"+txt,new float[]{(float)sx/ww,(float)sy/hh,(float)(sx+rw)/ww,(float)(sy+rh)/hh},0.9f));}}}} catch { } }
		void DoDcSt(Mat m, int h, Dictionary<int, List<TestDefect>> ad) { try { int hh=m.Height,ww=m.Width,bw=ww/h,sy=hh*2/3; for(int i=0;i<h;i++){int sx=i*bw,rw=(i<h-1)?bw:(ww-sx),rh=hh-sy;if(rw<=0||rh<=0)continue;using(var roi=new Mat(m,new CvR(sx,sy,rw,rh)).Clone()){ResponseList<OcrResponse> ocr;if(_ai.BackDateCodeOcrModel.Run(roi,out ocr)==0&&ocr!=null){var txts=new List<string>();foreach(var rt in ocr){if(rt.Item2.Blocks==null)continue;foreach(var blk in rt.Item2.Blocks)if(!string.IsNullOrWhiteSpace(blk.Label))txts.Add(blk.Label);}if(txts.Count>0){if(!ad.ContainsKey(i))ad[i]=new List<TestDefect>();ad[i].Add(new TestDefect("日期码:"+string.Join(" ",txts),new float[]{0,(float)sy/hh,1,(float)(sy+rh)/hh},0.9f));}}}} } catch { } }

		// ====== 模型测试逻辑 ======
		void BtnMI1(object s, EventArgs e) { using (var d = new OpenFileDialog { Title = "加载左图", Filter = "图像|*.jpg;*.jpeg;*.png;*.bmp;*.tif" }) { if (d.ShowDialog() != DialogResult.OK) return; _mM1?.Dispose(); _mM1 = Cv2.ImRead(d.FileName); _bMI1.Text = Path.GetFileName(d.FileName); _lblMI1.Text = "左:" + _mM1.Width + "x" + _mM1.Height; _lblMI1.ForeColor = OkC; _pm1.Image = BmpCvt.ToBitmap(_mM1); _bMRun.Enabled = true; LogM("左:" + _mM1.Width + "x" + _mM1.Height); } }
		void BtnMI2(object s, EventArgs e) { using (var d = new OpenFileDialog { Title = "加载右图(日期码用)", Filter = "图像|*.jpg;*.jpeg;*.png;*.bmp;*.tif" }) { if (d.ShowDialog() != DialogResult.OK) return; _mM2?.Dispose(); _mM2 = Cv2.ImRead(d.FileName); _bMI2.Text = Path.GetFileName(d.FileName); _lblMI2.Text = "右:" + _mM2.Width + "x" + _mM2.Height; _lblMI2.ForeColor = OkC; _pm2.Image = BmpCvt.ToBitmap(_mM2); LogM("右:" + _mM2.Width + "x" + _mM2.Height); } }

		async void BtnMRun(object s, EventArgs e) { if (_mM1 == null) { MessageBox.Show("请先加载左图"); return; } _bMRun.Enabled = false; ReadMP(); _curMp.Save(); var sw = Stopwatch.StartNew(); LogM("===== " + _curKey + " =====");
			_mB1 = new Bitmap((Bitmap)_pm1.Image); _mB2 = _mM2 != null ? new Bitmap((Bitmap)_pm2.Image) : null;
			try { await Task.Run(() => DoModel()); sw.Stop(); InvokeUI(() => _lblMT.Text = sw.ElapsedMilliseconds + "ms"); LogM("完成:" + sw.ElapsedMilliseconds + "ms"); }
			catch (Exception ex) { LogM("失败:" + ex.Message, true); } finally { InvokeUI(() => _bMRun.Enabled = true); } }

		void DoModel()
		{ string k = _curKey; var p = _curMp; float cf = p.Confidence, io = p.Iou;
			Mat ml = BmpCvt.ToMat(_mB1), mr = _mB2 != null ? BmpCvt.ToMat(_mB2) : null;
			Bitmap rdr = null; string inf = "";
			if ((k.StartsWith("dc_") || k == "datecode") && mr != null)
			{ if (ml.Rows != mr.Rows) { int h2 = Math.Max(ml.Rows, mr.Rows); Cv2.Resize(ml, ml, new OpenCvSharp.Size(ml.Cols * h2 / ml.Rows, h2)); Cv2.Resize(mr, mr, new OpenCvSharp.Size(mr.Cols * h2 / mr.Rows, h2)); LogM("拼接:高度不一致已缩放至" + h2); } var mg2 = new Mat(); Cv2.HConcat(ml, mr, mg2); ml.Dispose(); mr.Dispose(); ml = mg2; mr = null; LogM("拼接:" + ml.Width + "x" + ml.Height); }
			switch (k) {
				case "front_box": rdr = DoYolo(ml, _ai.FrontBoxBreakModel, cf, io, "盒子破损"); break;
				case "front_pcode": if (_ai.FrontOcrModel != null) rdr = DoPnumM(ml); break;
				case "endface_upper": rdr = DoYolo(ml, _ai.EndFaceUpperModel, cf, io, "上端面"); break;
				case "endface_lower": rdr = DoYolo(ml, _ai.EndFaceLowerModel, cf, io, "下端面"); break;
				case "barcode": rdr = DoBarcodeM(ml, p); break;
				case "hook": rdr = DoYolo(ml, _ai.BackHookModel, cf, io, "挂钩明显"); break;
				case "hook_slight": if (_ai.HookSlightModel != null) rdr = DoYoloSeg(ml, _ai.HookSlightModel, cf, "挂钩轻微"); break;
				case "side": rdr = DoYolo(ml, _ai.SideDefectModel, cf, io, "侧面"); break;
				case "dc_seg": TestDcSegM(ml, cf, out rdr, out inf); break;
				case "dc_cls": TestDcClsM(ml, cf, out rdr, out inf); break;
				case "dc_ocr": TestDcOcrM(ml, cf, out rdr, out inf); break;
				case "dc_all": TestDcAllM(ml, cf, out rdr, out inf); break;
			}
			ml?.Dispose(); mr?.Dispose(); var fr = rdr; var fi = inf;
			InvokeUI(() => { if (fr != null) _pm3.Image = fr; _lblMT.Text = _lblMT.Text + " " + fi; });
		}

		Bitmap DoYolo(Mat m, YoloOnnx mdl, float cf, float io, string tag) { var r = mdl.Predict(m, cf, io); var df = new List<TestDefect>(); if (r?.BoxesN != null) for (int i = 0; i < r.BoxesN.Length; i++) { var bx = r.BoxesN[i]; df.Add(new TestDefect(tag, new float[] { bx.X, bx.Y, bx.X + bx.Width, bx.Y + bx.Height }, r.Scores[i])); } LogM(tag + ":" + df.Count + "个"); return DrawM(m, df); }
		Bitmap DoYoloSeg(Mat m, YoloOnnxSegmentation mdl, float cf, string tag) { var r = mdl.Predict(m, cf); var df = new List<TestDefect>(); if (r?.BoxesN != null) for (int i = 0; i < r.BoxesN.Length; i++) { var bx = r.BoxesN[i]; df.Add(new TestDefect(tag, new float[] { bx.X, bx.Y, bx.X + bx.Width, bx.Y + bx.Height }, 0)); } LogM(tag + ":" + df.Count + "个"); return DrawM(m, df); }
		Bitmap DoPnumM(Mat m) { int hh=m.Height,ww=m.Width,hp=4,bw=ww/hp,sy=hh*2/3; var df=new List<TestDefect>(); var rx=new Regex(@"P\d+",RegexOptions.IgnoreCase); for(int i=0;i<hp;i++){int sx=i*bw,rw=(i<hp-1)?bw:(ww-sx),rh=hh-sy;if(rw<=0||rh<=0)continue;using(var roi=new Mat(m,new CvR(sx,sy,rw,rh)).Clone()){ResponseList<OcrResponse> ocr;if(_ai.FrontOcrModel.Run(roi,out ocr)==0&&ocr!=null)foreach(var rt in ocr){if(rt.Item2.Blocks==null)continue;foreach(var blk in rt.Item2.Blocks){if(string.IsNullOrWhiteSpace(blk.Label))continue;var mt2=rx.Match(blk.Label);if(!mt2.Success||mt2.Value.Length<6)continue;df.Add(new TestDefect(mt2.Value.ToUpper(),PnBxSt(blk,ww,hh,sx,sy),0.9f));}}}}LogM("P号:"+df.Count+"个");return DrawM(m,df); }
		Bitmap DoBarcodeM(Mat m, ModelParams p2) { int hh=m.Height,ww=m.Width,hp=6,bw=ww/hp,sy=(int)(hh*p2.BcStartHeightRatio);var df=new List<TestDefect>();for(int i=0;i<hp;i++){int sx=i*bw,rw=(i<hp-1)?bw:(ww-sx),rh=hh-sy;if(rw<=0||rh<=0)continue;using(var roi=new Mat(m,new CvR(sx,sy,rw,rh)).Clone()){var pt=ApplyBcPre(roi,p2);using(pt)using(var bmp=pt.ToBitmap()){var rd2=new ZXing.BarcodeReader{AutoRotate=true,Options=new ZXing.Common.DecodingOptions{TryHarder=p2.BcTryHarder,PossibleFormats=new List<ZXing.BarcodeFormat>{ZXing.BarcodeFormat.CODE_128,ZXing.BarcodeFormat.EAN_13}}};var rs2=rd2.DecodeMultiple(bmp);string txt=rs2!=null&&rs2.Length>0?string.Join(",",rs2.Select(rr2=>rr2.Text)):"(无)";df.Add(new TestDefect("盒"+(i+1)+":"+txt,new float[]{(float)sx/ww,(float)sy/hh,(float)(sx+rw)/ww,(float)(sy+rh)/hh},0.9f));}}}LogM("条码:"+hp+"盒");return DrawM(m,df); }

		Mat ApplyBcPre(Mat src, ModelParams p2)
		{ if (!p2.BcEnablePreprocess) { var g = new Mat(); Cv2.CvtColor(src, g, ColorConversionCodes.BGR2GRAY); return g; } Mat m2 = src.Clone();
			if (Math.Abs(p2.BcContrastAlpha - 1.0f) > 0.001f || p2.BcBrightnessBeta != 0) { var t = new Mat(); m2.ConvertTo(t, -1, p2.BcContrastAlpha, p2.BcBrightnessBeta); m2.Dispose(); m2 = t; }
			if (m2.Channels() != 1) { var g = new Mat(); Cv2.CvtColor(m2, g, m2.Channels() == 3 ? ColorConversionCodes.BGR2GRAY : ColorConversionCodes.BGRA2GRAY); m2.Dispose(); m2 = g; }
			if (p2.BcEnableEqualizeHist) { var e2 = new Mat(); Cv2.EqualizeHist(m2, e2); m2.Dispose(); m2 = e2; }
			if (p2.BcEnableGaussianBlur) { var b2 = new Mat(); Cv2.GaussianBlur(m2, b2, new OpenCvSharp.Size(5, 5), 0); m2.Dispose(); m2 = b2; }
			if (p2.BcEnableMedianBlur) { var b2 = new Mat(); Cv2.MedianBlur(m2, b2, 5); m2.Dispose(); m2 = b2; }
			switch (p2.BcThresholdMode) { case 1: { int bs = p2.BcAdaptiveBlockSize; if (bs % 2 == 0) bs++; var t = new Mat(); Cv2.AdaptiveThreshold(m2, t, 255, AdaptiveThresholdTypes.MeanC, ThresholdTypes.Binary, bs, p2.BcAdaptiveC); m2.Dispose(); m2 = t; } break; case 2: { var t = new Mat(); Cv2.Threshold(m2, t, 0, 255, ThresholdTypes.Otsu | ThresholdTypes.Binary); m2.Dispose(); m2 = t; } break; case 3: { var t = new Mat(); Cv2.Threshold(m2, t, p2.BcFixedThreshold, 255, ThresholdTypes.Binary); m2.Dispose(); m2 = t; } break; }
			if (p2.BcEnableInvert) { var t = new Mat(); Cv2.BitwiseNot(m2, t); m2.Dispose(); m2 = t; }
			var k2 = Cv2.GetStructuringElement(MorphShapes.Rect, new OpenCvSharp.Size(3, 3));
			if (p2.BcEnableMorphClose) { var t = new Mat(); Cv2.MorphologyEx(m2, t, MorphTypes.Close, k2); m2.Dispose(); m2 = t; }
			if (p2.BcEnableMorphOpen) { var t = new Mat(); Cv2.MorphologyEx(m2, t, MorphTypes.Open, k2); m2.Dispose(); m2 = t; }
			if (p2.BcEnableMorphDilate) { var t = new Mat(); Cv2.MorphologyEx(m2, t, MorphTypes.Dilate, k2); m2.Dispose(); m2 = t; }
			if (p2.BcEnableMorphErode) { var t = new Mat(); Cv2.MorphologyEx(m2, t, MorphTypes.Erode, k2); m2.Dispose(); m2 = t; }
			k2.Dispose(); return m2;
		}

		void TestDcSegM(Mat m, float cf, out Bitmap rd, out string inf) { rd=null;inf="";if(_ai.BackDateCodeSegModel==null){inf="C1未加载";return;}var sw=Stopwatch.StartNew();ResponseList<SegmentationResponse> seg;int ret=_ai.BackDateCodeSegModel.Run(m,out seg);var df=new List<TestDefect>();int rgn=0;if(ret==0&&seg!=null)foreach(var item in seg){var mask=item.Item2.Mask;if(mask==null||mask.Empty())continue;using(Mat mc=mask.Clone()){Mat lb=new Mat(),st2=new Mat(),ct=new Mat();int n=Cv2.ConnectedComponentsWithStats(mc,lb,st2,ct,PixelConnectivity.Connectivity8);for(int k3=1;k3<n;k3++){int sx=st2.At<int>(k3,0),sy=st2.At<int>(k3,1),sw2=st2.At<int>(k3,2),sh=st2.At<int>(k3,3);if(sw2>5&&sh>5){df.Add(new TestDefect("R"+rgn,new float[]{(float)sx/m.Width,(float)sy/m.Height,(float)(sx+sw2)/m.Width,(float)(sy+sh)/m.Height},0.9f));rgn++;}}}}rd=DrawM(m,df);inf="C1:"+rgn+"区 "+sw.ElapsedMilliseconds+"ms";LogM(inf);}
		void TestDcClsM(Mat m, float cf, out Bitmap rd, out string inf) { rd=null;inf="";if(_ai.BackDateCodeClsModel==null){inf="C2未加载";return;}var sw=Stopwatch.StartNew();ResponseList<ClassificationResponse> cls;int ret=_ai.BackDateCodeClsModel.Run(m,out cls);var df=new List<TestDefect>();if(ret==0&&cls!=null)foreach(var ci in cls){string cn=ci.Item2.Labels.Any()?ci.Item2.Labels.First().Label:"";float sc=ci.Item2.Labels.Any()?ci.Item2.Labels.First().Score:0f;df.Add(new TestDefect(cn+"("+sc.ToString("F2")+")",new float[]{0.1f,0.1f,0.9f,0.9f},sc));}rd=DrawM(m,df);inf="C2:"+df.Count+" "+sw.ElapsedMilliseconds+"ms";LogM(inf);}
		void TestDcOcrM(Mat m, float cf, out Bitmap rd, out string inf) { rd=null;inf="";if(_ai.BackDateCodeOcrModel==null){inf="C3未加载";return;}var sw=Stopwatch.StartNew();ResponseList<OcrResponse> ocr;int ret=_ai.BackDateCodeOcrModel.Run(m,out ocr);var df=new List<TestDefect>();if(ret==0&&ocr!=null)foreach(var rt in ocr){if(rt.Item2.Blocks==null)continue;foreach(var blk in rt.Item2.Blocks){if(string.IsNullOrWhiteSpace(blk.Label))continue;float[] nb=new float[]{0,0,0.5f,0.1f};if(blk.Polygon!=null&&blk.Polygon.Any()){float mx=float.MaxValue,my=float.MaxValue,Mx=float.MinValue,My=float.MinValue;foreach(var pt2 in blk.Polygon){if(pt2.X<mx)mx=pt2.X;if(pt2.Y<my)my=pt2.Y;if(pt2.X>Mx)Mx=pt2.X;if(pt2.Y>My)My=pt2.Y;}nb=new float[]{mx/m.Width,my/m.Height,Mx/m.Width,My/m.Height};}df.Add(new TestDefect(blk.Label,nb,blk.Score));}}rd=DrawM(m,df);inf="C3:"+df.Count+"文本 "+sw.ElapsedMilliseconds+"ms";LogM(inf);}
		void TestDcAllM(Mat m, float cf, out Bitmap rd, out string inf) { rd=null;inf="";if(_ai.BackDateCodeSegModel==null||_ai.BackDateCodeClsModel==null||_ai.BackDateCodeOcrModel==null){inf="模型不全";return;}int fw=m.Width,fh=m.Height;var sw=Stopwatch.StartNew();var df=new List<TestDefect>();int s1=0,s2=0,s3=0;ResponseList<SegmentationResponse> seg;if(_ai.BackDateCodeSegModel.Run(m,out seg)==0&&seg!=null)foreach(var item in seg){var mask=item.Item2.Mask;if(mask==null||mask.Empty())continue;using(Mat mc=mask.Clone()){Mat lb=new Mat(),st2=new Mat(),ct=new Mat();int n=Cv2.ConnectedComponentsWithStats(mc,lb,st2,ct,PixelConnectivity.Connectivity8);for(int k3=1;k3<n;k3++){int sx=st2.At<int>(k3,0),sy=st2.At<int>(k3,1),sw2=st2.At<int>(k3,2),sh=st2.At<int>(k3,3);if(sw2<=5||sh<=5)continue;s1++;int mx2=Math.Max(0,sx-5),my2=Math.Max(0,sy-5),mw=Math.Min(fw-mx2,sw2+10),mh=Math.Min(fh-my2,sh+10);using(var c2=new Mat(m,new CvR(mx2,my2,mw,mh)).Clone()){ResponseList<ClassificationResponse> cls2;if(_ai.BackDateCodeClsModel.Run(c2,out cls2)==0&&cls2!=null)s2+=cls2.Count;}using(var c3=new Mat(m,new CvR(mx2,my2,mw,mh)).Clone()){ResponseList<OcrResponse> ocr2;if(_ai.BackDateCodeOcrModel.Run(c3,out ocr2)==0&&ocr2!=null)foreach(var rt2 in ocr2){if(rt2.Item2.Blocks==null)continue;foreach(var blk2 in rt2.Item2.Blocks){if(string.IsNullOrWhiteSpace(blk2.Label))continue;s3++;df.Add(new TestDefect(blk2.Label,new float[]{(float)mx2/fw,(float)my2/fh,(float)(mx2+mw)/fw,(float)(my2+mh)/fh},blk2.Score));}}}}}}rd=DrawM(m,df);inf="C1+C2+C3:"+s1+"/"+s2+"/"+s3+" "+sw.ElapsedMilliseconds+"ms";LogM(inf);}

		// ====== 绘制 ======
		Bitmap DrawStImg(Mat m, List<TestDefect> df) { var bmp=m.ToBitmap();int w=bmp.Width,h=bmp.Height;using(var g=Graphics.FromImage(bmp)){g.SmoothingMode=SmoothingMode.AntiAlias;foreach(var d in df)DrawOne(g,d,w,h);if(df.Count==0)g.DrawString("OK",new Font("微软雅黑",14,FontStyle.Bold),Brushes.Green,10,10);}return bmp;}
		Bitmap DrStM2(Mat m, Dictionary<int,List<TestDefect>> df,List<string> st,int si,int ei,bool rev){var bmp=m.ToBitmap();int w=bmp.Width,h=bmp.Height,t=ei-si;using(var g=Graphics.FromImage(bmp)){g.SmoothingMode=SmoothingMode.AntiAlias;foreach(var kv in df)foreach(var d in kv.Value)DrawOne(g,d,w,h);if(t>1)using(var dp=new Pen(Color.FromArgb(120,120,120),1){DashStyle=DashStyle.Dash})for(int i=1;i<t;i++)g.DrawLine(dp,i*w/t,0,i*w/t,h);for(int i=0;i<t&&si+i<st.Count;i++){string ss=st[si+i];string disp=ss=="OK"?"OK":(ss.Length>4?ss.Substring(0,4):ss);Color cc=ss=="OK"?OkC:NgC;float cx=(i+0.5f)*w/t;using(var sf2=new Font("微软雅黑",28,FontStyle.Bold)){var sz2=g.MeasureString(disp,sf2);using(var br2=new SolidBrush(cc))g.DrawString(disp,sf2,br2,cx-sz2.Width/2,60);}int bx=rev?(st.Count-(si+i)):(si+i+1);using(var nf2=new Font("微软雅黑",16,FontStyle.Bold)){var bs2=g.MeasureString("盒"+bx,nf2);using(var br3=new SolidBrush(Color.Yellow))g.DrawString("盒"+bx,nf2,br3,cx-bs2.Width/2,120);}}}return bmp;}
		Bitmap DrawM(Mat m, List<TestDefect> df) { var bmp=m.ToBitmap();int w=bmp.Width,h=bmp.Height;using(var g=Graphics.FromImage(bmp)){g.SmoothingMode=SmoothingMode.AntiAlias;foreach(var d in df)DrawOne(g,d,w,h);if(df.Count==0)g.DrawString("OK",new Font("微软雅黑",14,FontStyle.Bold),Brushes.Green,10,10);}return bmp;}

		void DrawOne(Graphics g, TestDefect d, int w, int h) { float[] b=d.Box;int x1=(int)(b[0]*w),y1=(int)(b[1]*h),x2=(int)(b[2]*w),y2=(int)(b[3]*h);if(x2<=x1||y2<=y1)return;var rc=new Rc(x1,y1,x2-x1,y2-y1);Color c=d.Type.StartsWith("P号:")?PnG:(d.Type.Contains("搭舌")?Color.FromArgb(230,126,34):(d.Type.Contains("边缘")?Color.FromArgb(155,89,182):(d.Type.Contains("挂钩")?Color.DarkRed:(d.Type.Contains("条码")||d.Type.Contains("日期")?PriC:NgC))));using(var fl=new SolidBrush(Color.FromArgb(50,c)))g.FillRectangle(fl,rc);using(var pn2=new Pen(c,3))g.DrawRectangle(pn2,rc);string lb=d.Type+" "+d.Score.ToString("F2");using(var f3=new Font("微软雅黑",10,FontStyle.Bold)){var sz3=g.MeasureString(lb,f3);int ly=y1-(int)sz3.Height-4;if(ly<4)ly=y1+4;using(var bg2=new SolidBrush(c))g.FillRectangle(bg2,x1,ly,sz3.Width+6,sz3.Height+4);g.DrawString(lb,f3,Brushes.White,x1+2,ly+1);}}

		Bitmap MergeBmps(Bitmap l, Bitmap r) { var m3=new Bitmap(l.Width+r.Width,Math.Max(l.Height,r.Height));using(var g=Graphics.FromImage(m3)){g.Clear(Color.Black);g.DrawImage(l,0,(m3.Height-l.Height)/2);g.DrawImage(r,l.Width,(m3.Height-r.Height)/2);using(var pn2=new Pen(Color.White,2))g.DrawLine(pn2,l.Width,0,l.Width,m3.Height);}l.Dispose();r.Dispose();return m3;}

		void GpSt2(List<string> st, Dictionary<int,List<TestDefect>> df) { _grd.Rows.Clear();for(int i=0;i<st.Count;i++){if(df.ContainsKey(i))foreach(var d in df[i])_grd.Rows.Add(i+1,"NG",d.Type,d.Score.ToString("F3"));else _grd.Rows.Add(i+1,"OK","-","-");}}

		void Cl2(DetResult r2,int s,int e,string tp,Dictionary<int,List<TestDefect>> d){if(r2?.Boxes==null)return;int t2=e-s;if(t2<=0)return;foreach(var b2 in r2.Boxes){float cx=(b2.X+b2.Width/2f)/r2.OrigImg.Width;int idx=s+(int)(cx*t2);if(idx<s||idx>=e)continue;if(!d.ContainsKey(idx))d[idx]=new List<TestDefect>();d[idx].Add(new TestDefect(tp,new float[]{b2.X,b2.Y,b2.X+b2.Width,b2.Y+b2.Height},0));}}
		void ClN2(DetResult r2,int s,int e,string tp,Dictionary<int,List<TestDefect>> d){if(r2?.BoxesN==null)return;int t2=e-s;if(t2<=0)return;foreach(var b2 in r2.BoxesN){float cx=b2.X+b2.Width/2f;int idx=s+(int)(cx*t2);if(idx<s||idx>=e)continue;if(!d.ContainsKey(idx))d[idx]=new List<TestDefect>();d[idx].Add(new TestDefect(tp,new float[]{b2.X,b2.Y,b2.X+b2.Width,b2.Y+b2.Height},0));}}
		void Cs2(SegResult r2,int s,int e,string tp,Dictionary<int,List<TestDefect>> d){if(r2?.Boxes==null)return;int t2=e-s;if(t2<=0)return;foreach(var b2 in r2.Boxes){float cx=(b2.X+b2.Width/2f)/r2.OrigImg.Width;int idx=s+(int)(cx*t2);if(idx<s||idx>=e)continue;if(!d.ContainsKey(idx))d[idx]=new List<TestDefect>();d[idx].Add(new TestDefect(tp,new float[]{b2.X,b2.Y,b2.X+b2.Width,b2.Y+b2.Height},0));}}
		Dictionary<int,List<TestDefect>> Fd3(Dictionary<int,List<TestDefect>> s,int st2,int en2){var r3=new Dictionary<int,List<TestDefect>>();foreach(var kv in s)if(kv.Key>=st2&&kv.Key<en2)r3[kv.Key]=kv.Value;return r3;}

		// ====== 轮播 ======
		void ShwStB() { if (_sBatch.Count == 0 || _sBi >= _sBatch.Count) return; _po.Image = BmpCvt.ToBitmap(_sBatch[_sBi]); UpStPg(); }
		void UpStPg() { _lblPg.Text = _sBatch.Count > 0 ? (_sBi+1)+"/"+_sBatch.Count : ""; _bPrev.Enabled = _bNext.Enabled = _sBatch.Count > 1; }
		void ClrSt() { _s1?.Dispose(); _s2?.Dispose(); _s3?.Dispose(); _s1 = _s2 = _s3 = null; _sBatch.Clear(); _sBi = 0; _pi1.Image = _pi2.Image = _po.Image = null; _bRun.Enabled = false; _grd.Rows.Clear(); UpStPg(); }

		void InvokeUI(Action a) { if (!IsDisposed) BeginInvoke(a); }
		void LogS(string m, bool e = false) { string l = "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + m; InvokeUI(() => { if (_tab.SelectedIndex != 0) return; try { _rLog.SelectionStart = _rLog.TextLength; _rLog.SelectionColor = e ? NgC : Color.FromArgb(180, 180, 180); _rLog.AppendText(l + "\n"); _rLog.ScrollToCaret(); if (_rLog.TextLength > 10000) _rLog.Text = _rLog.Text.Substring(_rLog.TextLength - 8000); } catch { } }); if (e) Logger.Error(m); else Logger.Info(m); }
		void LogM(string m, bool e = false) { string l = "[" + DateTime.Now.ToString("HH:mm:ss.fff") + "] " + m; InvokeUI(() => { if (_tab.SelectedIndex != 1) return; try { _rMLog.SelectionStart = _rMLog.TextLength; _rMLog.SelectionColor = e ? NgC : Color.FromArgb(180, 180, 180); _rMLog.AppendText(l + "\n"); _rMLog.ScrollToCaret(); } catch { } }); if (e) Logger.Error(m); else Logger.Info(m); }

		void SaveStCfg() { try { var cfg = new { conf = (float)_nConf.Value, iou = (float)_nIou.Value, crop = (float)_nCrop.Value, pCount = (int)_nP.Value, thickness = (float)_nThick.Value, blueId = (int)_nBlue.Value, holeId = (int)_nHole.Value, rev = _chkRev.Checked, pnum = _chkPNum.Checked, bc = _chkBc.Checked, dc = _chkDc.Checked }; var dir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config"); Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "TestStParams.json"), JsonConvert.SerializeObject(cfg, Formatting.Indented)); LogS("工位参数已保存"); } catch { } }

		// ====== 通用控件工厂 ======
		Panel WrapPb(XLPictureBox pb, string t) { var r = new Panel { Dock = DockStyle.Fill, Padding = new Padding(4) }; r.Controls.Add(pb); r.Controls.Add(new Label { Text = "  " + t, Dock = DockStyle.Top, Height = 20, BackColor = Color.FromArgb(240, 242, 245), Font = _f8 }); return r; }
		Panel WrapCtrl(Control c, string t) { var r = new Panel { Dock = DockStyle.Fill }; r.Controls.Add(c); r.Controls.Add(new Label { Text = "  " + t, Dock = DockStyle.Top, Height = 20, BackColor = Color.FromArgb(240, 242, 245), Font = _f8 }); return r; }
		XLPictureBox MakePb() { return new XLPictureBox { Dock = DockStyle.Fill, BackColor1 = Color.FromArgb(50, 50, 50), BackColor2 = Color.FromArgb(70, 70, 70), BackgroundGridSize = 20 }; }
		UIButton MakeBtn(string t, Pt loc, int w, int h, EventHandler eh) { var b = new UIButton { Text = t, Location = loc, Size = new Sz(w, h), Font = _f9, Radius = 6, Cursor = Cursors.Hand }; b.Click += eh; return b; }
		UIComboBox MakeCombo(Pt loc, int w, string[] items, int sel) { var c = new UIComboBox { Location = loc, Size = new Sz(w, 26), DropDownStyle = UIDropDownStyle.DropDownList }; foreach (var it in items) c.Items.Add(it); c.SelectedIndex = sel; return c; }
		NumericUpDown MakeNum(decimal v, decimal mn, decimal mx, int dec = 2, int inc = 0) { var n = new NumericUpDown { Width = 55, Minimum = mn, Maximum = mx, Value = v, DecimalPlaces = dec, Increment = dec == 0 ? (inc > 0 ? inc : 1) : 0.05m, Font = _f8 }; return n; }
		CheckBox MakeChk(string t, bool v, Pt loc = default) { return new CheckBox { Text = t, Checked = v, Location = loc, Font = _f8, AutoSize = true }; }
		void AddLbl(string t, int x, int y, Control parent, Font f = null) { var lb = new Label { Text = t, Location = new Pt(x, y), Size = new Sz(50, 18), Font = f ?? _f8, TextAlign = ContentAlignment.MiddleLeft }; lb.Parent = parent; }
		void AddLbl(string t, int x, int y, int w, Control parent, Font f = null) { var lb = new Label { Text = t, Location = new Pt(x, y), Size = new Sz(w, 18), Font = f ?? _f8, TextAlign = ContentAlignment.MiddleRight }; lb.Parent = parent; }

		protected override void OnFormClosing(FormClosingEventArgs e) { VisionMeasure.MainFrm.ManualTestMode = false; _s1?.Dispose(); _s2?.Dispose(); _s3?.Dispose(); _mM1?.Dispose(); _mM2?.Dispose(); _mB1?.Dispose(); _mB2?.Dispose(); base.OnFormClosing(e); }
	}
}
