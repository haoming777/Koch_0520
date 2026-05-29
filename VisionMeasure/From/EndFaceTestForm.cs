using Models;
using Config;
using OpenCvSharp;
using OpenCvSharp.Extensions;
using Sunny.UI;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using VisionMeasure.Utils;
using CommonLib;
using XL.Controls;
using YoloInference;
using CvR = OpenCvSharp.Rect;
using Sz = System.Drawing.Size;
using Pt = System.Drawing.Point;

namespace VisionMeasure.From
{
	public partial class EndFaceTestForm : UIForm
	{
		static readonly Color Pri = Color.FromArgb(0,122,204), Ok = Color.FromArgb(39,174,96), Ng = Color.FromArgb(231,76,60), Bg = Color.FromArgb(245,247,250), Card = Color.White;
		AiModelManager _ai; YoloOnnx _model; string _side = "上端面";
		List<Mat> _images = new List<Mat>(); List<Mat> _results = new List<Mat>(); int _idx;

		ComboBox _cmbSide; TextBox _txtFolder; NumericUpDown _nConf, _nIou, _nP;
		UIButton _btnFolder, _btnLoad, _btnRun, _btnPrev, _btnNext, _btnSave, _btnExport;
		XLPictureBox _pbOri, _pbRes; Label _lblCnt, _lblTime, _lblStatus;
		DataGridView _grd; RichTextBox _rLog;
		Font _f9 = new Font("微软雅黑", 9F), _f10b = new Font("微软雅黑", 10F, FontStyle.Bold);

		public EndFaceTestForm(AiModelManager ai = null) { _ai = ai; Build(); Load += (s,e) => { if (_ai == null) LoadAi(); Log("端面测试就绪"); }; }
		void LoadAi() { try { _ai = new AiModelManager(ModelPathConfig.LoadFromSysConfig()); _ai.LoadAllModels(); Log("模型加载完成"); } catch (Exception ex) { Log("加载失败:" + ex.Message, true); } }

		void Build()
		{
			Text = "端面缺陷测试"; Size = new Sz(1400, 900); StartPosition = FormStartPosition.CenterParent; BackColor = Bg;
			var lo = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3, Padding = new Padding(10) };
			lo.RowStyles.Add(new RowStyle(SizeType.Absolute, 95));
			lo.RowStyles.Add(new RowStyle(SizeType.Percent, 55));
			lo.RowStyles.Add(new RowStyle(SizeType.Percent, 45));
			lo.Controls.Add(BuildTop(), 0, 0); lo.Controls.Add(BuildMid(), 0, 1); lo.Controls.Add(BuildBot(), 0, 2);
			Controls.Add(lo);
		}

		Panel BuildTop()
		{
			var p = new FlowLayoutPanel { Dock = DockStyle.Fill, BackColor = Bg, Padding = new Padding(4) };
			var c1 = Pnl(200, 80);
			AddLbl("端面选择", 8, 5, c1, _f10b);
			_cmbSide = new ComboBox { Location = new Pt(8, 28), Size = new Sz(180, 24), DropDownStyle = ComboBoxStyle.DropDownList, Font = _f9 };
			_cmbSide.Items.AddRange(new[] { "上端面", "下端面" }); _cmbSide.SelectedIndex = 0;
			_cmbSide.SelectedIndexChanged += (s,e) => _side = _cmbSide.SelectedItem.ToString();
			c1.Controls.Add(_cmbSide); p.Controls.Add(c1);

			var c2 = Pnl(380, 80);
			AddLbl("图像文件夹", 8, 5, c2, _f10b);
			_txtFolder = new TextBox { Location = new Pt(8, 28), Size = new Sz(280, 24), Font = _f9, ReadOnly = true, BackColor = Color.White };
			_btnFolder = Btn("浏览...", 295, 28, 70, 24, (s,e) => { using (var d = new FolderBrowserDialog()) { if (d.ShowDialog() == DialogResult.OK) _txtFolder.Text = d.SelectedPath; } });
			_btnLoad = Btn("加载", 8, 56, 80, 24, BtnLoad); ((Sunny.UI.UIButton)_btnLoad).FillColor = Pri;
			_nP = new NumericUpDown { Location = new Pt(95, 56), Width = 55, Minimum = 1, Maximum = 100, Value = 8, Font = _f9, DecimalPlaces = 0 };
			AddLbl("盒数:", 155, 58, c2);
			c2.Controls.AddRange(new Control[] { _txtFolder, _btnFolder, _btnLoad, _nP }); p.Controls.Add(c2);

			var c3 = Pnl(240, 80);
			AddLbl("参数", 8, 5, c3, _f10b);
			_nConf = Num(0.5m, 0.05m, 1.0m, 8, 28); _nIou = Num(0.2m, 0.05m, 1.0m, 78, 28);
			AddLbl("Conf:", 8, 30, c3); AddLbl("IOU:", 78, 30, c3);
			_btnRun = Btn("执行检测", 8, 54, 160, 24, BtnRun); ((Sunny.UI.UIButton)_btnRun).FillColor = Ok; _btnRun.Font = _f10b;
			c3.Controls.AddRange(new Control[] { _nConf, _nIou, _btnRun });
			_lblTime = new Label { Text = "---", Location = new Pt(175, 56), Size = new Sz(60, 18), Font = _f9 };
			c3.Controls.Add(_lblTime); p.Controls.Add(c3);

			var c4 = Pnl(280, 80);
			_btnPrev = Btn("<", 10, 10, 45, 30, (s,e) => { if (_results.Count>0) { _idx = (_idx-1+_results.Count)%_results.Count; ShowRes(); } });
			_btnNext = Btn(">", 65, 10, 45, 30, (s,e) => { if (_results.Count>0) { _idx = (_idx+1)%_results.Count; ShowRes(); } });
			_lblCnt = new Label { Text = "0/0", Location = new Pt(115, 16), Size = new Sz(60, 18), Font = _f9, TextAlign = ContentAlignment.MiddleCenter };
			_btnSave = Btn("保存结果图", 10, 44, 100, 30, BtnSave);
			_btnExport = Btn("导出CSV", 120, 44, 80, 30, BtnExport);
			c4.Controls.AddRange(new Control[] { _btnPrev, _btnNext, _lblCnt, _btnSave, _btnExport }); p.Controls.Add(c4);
			return p;
		}

		Panel BuildMid()
		{
			var p = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
			_pbOri = MakePb(); _pbRes = MakePb();
			p.Controls.Add(WrapPb(_pbOri, "原图"), 0, 0); p.Controls.Add(WrapPb(_pbRes, "检测结果"), 1, 0); return p;
		}

		Panel BuildBot()
		{
			var p = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1 };
			_grd = new DataGridView { Dock = DockStyle.Fill, ReadOnly = true, AllowUserToAddRows = false, AllowUserToDeleteRows = false, RowHeadersVisible = false, AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill, BackgroundColor = Color.White, Font = _f9 };
			_grd.Columns.Add("#","#"); _grd.Columns.Add("状态","状态"); _grd.Columns.Add("缺陷","缺陷"); _grd.Columns.Add("置信度","置信度");
			_rLog = new RichTextBox { Dock = DockStyle.Fill, ReadOnly = true, BackColor = Color.FromArgb(30,30,30), ForeColor = Color.FromArgb(180,180,180), Font = new Font("Consolas", 9F), BorderStyle = BorderStyle.None };
			p.Controls.Add(WrapCtrl(_grd, "结果列表"), 0, 0); p.Controls.Add(WrapCtrl(_rLog, "日志"), 1, 0); return p;
		}

		void BtnLoad(object s, EventArgs e) { if (string.IsNullOrEmpty(_txtFolder.Text)) return; _images.Clear(); _results.Clear(); _idx=0; try { foreach (var f in Directory.GetFiles(_txtFolder.Text).Where(fn=>fn.EndsWith(".jpg")||fn.EndsWith(".jpeg")||fn.EndsWith(".png")||fn.EndsWith(".bmp")).OrderBy(fn=>fn).Take(50)) _images.Add(Cv2.ImRead(f)); if (_images.Count>0) _pbOri.Image = BitmapConverter.ToBitmap(_images[0]); Log("加载"+_images.Count+"张"); _lblCnt.Text = "0/"+_images.Count; } catch (Exception ex) { Log("加载失败:"+ex.Message, true); } }

		async void BtnRun(object s, EventArgs e)
		{ if (_images.Count==0) { Log("请先加载图像", true); return; } _btnRun.Enabled = false; var sw = Stopwatch.StartNew(); Log("===== "+_side+" =====");
			try { await Task.Run(() => DoDetect()); sw.Stop(); _lblTime.Text = sw.ElapsedMilliseconds+"ms"; Log("完成:"+sw.ElapsedMilliseconds+"ms"); }
			catch (Exception ex) { Log("失败:"+ex.Message, true); } finally { _btnRun.Enabled = true; } }

		void DoDetect()
		{ _model = _side=="上端面" ? _ai.EndFaceUpperModel : _ai.EndFaceLowerModel;
			if (_model==null) { Log("模型未加载",true); return; }
			float cf=(float)_nConf.Value, io=(float)_nIou.Value; int p=(int)_nP.Value;
			_results.Clear(); _grd.Rows.Clear();
			for (int i=0;i<_images.Count;i+=p) {
				int cnt=Math.Min(p,_images.Count-i);
				var batch=_images.GetRange(i,cnt);
				var rs=_model.PredictBatch(batch,cf,io);
				for (int j=0;j<cnt;j++) {
					var df=new List<(string t,float[] b,float s)>();
					if (rs!=null&&j<rs.Count&&rs[j].BoxesN!=null) for (int k=0;k<rs[j].BoxesN.Length;k++) { var bx=rs[j].BoxesN[k]; int cid=rs[j].ClassIds[k]; df.Add((cid==0?"搭舌缺陷":cid==1?"边缘问题":"破损",new float[]{bx.X,bx.Y,bx.X+bx.Width,bx.Y+bx.Height},rs[j].Scores[k])); }
					var rd=DrawRes(batch[j],df,i+j); lock(_results)_results.Add(rd);
				}
			}
			_idx=0; InvokeUI(()=>{ShowRes();Log("检测"+_results.Count+"张完成");}); }

		Mat DrawRes(Mat src, List<(string t,float[] b,float s)> df, int idx)
		{ var bmp = BitmapConverter.ToBitmap(src); int w=bmp.Width,h=bmp.Height; using (var g=Graphics.FromImage(bmp)) { g.SmoothingMode=SmoothingMode.AntiAlias;
				foreach (var d in df) { float[] bb=d.b;int x1=(int)(bb[0]*w),y1=(int)(bb[1]*h),x2=(int)(bb[2]*w),y2=(int)(bb[3]*h);if(x2<=x1||y2<=y1)continue;
					var rc=new Rectangle(x1,y1,x2-x1,y2-y1); Color c=Ng; if(d.t=="搭舌缺陷") c=Color.FromArgb(231,76,60); else if(d.t=="边缘问题") c=Color.FromArgb(230,126,34); else if(d.t=="破损") c=Color.FromArgb(155,89,182);
					using(var fl=new SolidBrush(Color.FromArgb(40,c)))g.FillRectangle(fl,rc);using(var pn=new Pen(c,3))g.DrawRectangle(pn,rc);
					using(var f=new Font("微软雅黑",11,FontStyle.Bold)){var sz=g.MeasureString(d.t,f);int ly=y1-(int)sz.Height-4;if(ly<4)ly=y1+4;using(var bg=new SolidBrush(c))g.FillRectangle(bg,x1,ly,sz.Width+6,sz.Height+4);g.DrawString(d.t,f,Brushes.White,x1+2,ly+1);}
				}
				using(var f2=new Font("微软雅黑",14,FontStyle.Bold)){string txt=df.Count==0?"OK":"NG:"+df.Count+"缺陷";Color cc=df.Count==0?Ok:Ng;g.DrawString(txt,f2,new SolidBrush(cc),10,10);}
				using(var f3=new Font("微软雅黑",10))g.DrawString("#"+(idx+1),f3,Brushes.Gray,10,38);
			} return OpenCvSharp.Extensions.BitmapConverter.ToMat(bmp); }

		void ShowRes() { if (_results.Count==0) return; _pbRes.Image = BitmapConverter.ToBitmap(_results[_idx]); _lblCnt.Text = (_idx+1)+"/"+_results.Count; }

		void BtnSave(object s, EventArgs e) { using (var d = new SaveFileDialog { Filter = "PNG|*.png", DefaultExt = "png" }) { if (d.ShowDialog()==DialogResult.OK && _pbRes.Image!=null) { _pbRes.Image.Save(d.FileName); Log("已保存:"+d.FileName); } } }
		void BtnExport(object s, EventArgs e) { using (var d = new SaveFileDialog { Filter = "CSV|*.csv", DefaultExt = "csv" }) { if (d.ShowDialog()==DialogResult.OK) { var sb = new System.Text.StringBuilder(); sb.AppendLine("序号,状态,缺陷,置信度"); for (int i=0;i<_grd.Rows.Count;i++) sb.AppendLine(string.Join(",", _grd.Rows[i].Cells.Cast<DataGridViewCell>().Select(c=>c.Value?.ToString()??""))); File.WriteAllText(d.FileName, sb.ToString()); Log("已导出:"+d.FileName); } } }

		void Log(string m, bool e=false) { var l = "["+DateTime.Now.ToString("HH:mm:ss.fff")+"] "+m; InvokeUI(() => { _rLog.SelectionStart=_rLog.TextLength; _rLog.SelectionColor=e?Ng:Color.FromArgb(180,180,180); _rLog.AppendText(l+"\n"); _rLog.ScrollToCaret(); }); if(e) Logger.Error(m); else Logger.Info(m); }
		void InvokeUI(Action a) { if (!IsDisposed) BeginInvoke(a); }

		Panel Pnl(int w, int h) { return new Panel { Width=w, Height=h, BackColor=Card, Margin=new Padding(4) }; }
		XLPictureBox MakePb() { return new XLPictureBox { Dock=DockStyle.Fill, BackColor1=Color.FromArgb(50,50,50), BackColor2=Color.FromArgb(70,70,70), BackgroundGridSize=20 }; }
		Panel WrapPb(XLPictureBox pb, string t) { var r=new Panel { Dock=DockStyle.Fill, Padding=new Padding(4) }; r.Controls.Add(pb); r.Controls.Add(new Label{Text="  "+t,Dock=DockStyle.Top,Height=20,BackColor=Color.FromArgb(240,242,245),Font=new Font("微软雅黑",8F,FontStyle.Bold)}); return r; }
		Panel WrapCtrl(Control c, string t) { var r=new Panel { Dock=DockStyle.Fill }; r.Controls.Add(c); r.Controls.Add(new Label{Text="  "+t,Dock=DockStyle.Top,Height=20,BackColor=Color.FromArgb(240,242,245),Font=new Font("微软雅黑",8F,FontStyle.Bold)}); return r; }
		UIButton Btn(string t, int x, int y, int w, int h, EventHandler eh) { var b=new UIButton{Text=t,Location=new Pt(x,y),Size=new Sz(w,h),Font=_f9,Radius=4,Cursor=Cursors.Hand};b.Click+=eh;return b; }
		NumericUpDown Num(decimal v, decimal mn, decimal mx, int x, int y) { return new NumericUpDown { Location=new Pt(x,y),Width=62,Minimum=mn,Maximum=mx,Value=v,DecimalPlaces=2,Increment=0.05m,Font=_f9 }; }
		void AddLbl(string t, int x, int y, Control p, Font f=null) { var lb = new Label { Text=t, Location=new Pt(x,y), Size=new Sz(50,18), Font=f??_f9 }; lb.Parent = p; }
		void AddLbl(string t, int x, int y, int w, Control p, Font f=null) { var lb = new Label { Text=t, Location=new Pt(x,y), Size=new Sz(w,18), Font=f??_f9 }; lb.Parent = p; }
		
	}
}
