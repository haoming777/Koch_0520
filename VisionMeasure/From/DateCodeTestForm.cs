using Config; using Models; using OpenCvSharp;
using OpenCvSharp.Extensions; using SmartMore.ViMo; using Sunny.UI; using System; using System.Collections.Generic; using System.Diagnostics; using System.Drawing; using System.Drawing.Drawing2D; using System.IO; using System.Linq; using System.Threading.Tasks; using System.Windows.Forms; using CommonLib; using XL.Controls; using CvR = OpenCvSharp.Rect; using Sz = System.Drawing.Size; using Pt = System.Drawing.Point;

namespace VisionMeasure.From
{
	public partial class DateCodeTestForm : UIForm
	{
		static readonly Color Pri = Color.FromArgb(0,122,204), Ok = Color.FromArgb(39,174,96), Ng = Color.FromArgb(231,76,60), Bg = Color.FromArgb(245,247,250), Card = Color.White;
		AiModelManager _ai; Mat _mLeft, _mRight; Bitmap _resBmp;
		TextBox _txtLeft, _txtRight; ComboBox _cmbMode; NumericUpDown _nConf;
		UIButton _btnL, _btnR, _btnRun, _btnSave;
		XLPictureBox _pbL, _pbR, _pbRes; Label _lblTime; RichTextBox _rLog;
		Font _f9 = new Font("微软雅黑",9F);

		public DateCodeTestForm(AiModelManager ai=null) { _ai=ai; Build(); Load+=(s,e)=>{ if(_ai==null)LoadAi();Log("日期码测试就绪");}; }
		void LoadAi() { try{_ai=new AiModelManager(ModelPathConfig.LoadFromSysConfig());_ai.LoadAllModels();Log("模型加载完成");}catch(Exception ex){Log("加载失败:"+ex.Message,true);} }

		void Build()
		{
			Text="日期码测试";Size=new Sz(1550,980);StartPosition=FormStartPosition.CenterParent;BackColor=Bg;
			var lo=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(10)};
			lo.RowStyles.Add(new RowStyle(SizeType.Absolute,145));
			lo.RowStyles.Add(new RowStyle(SizeType.Percent,55));
			lo.RowStyles.Add(new RowStyle(SizeType.Percent,45));
			lo.Controls.Add(BuildTop(),0,0);lo.Controls.Add(BuildMid(),0,1);lo.Controls.Add(BuildBot(),0,2);Controls.Add(lo);
		}

		Panel BuildTop()
		{
			var p=new FlowLayoutPanel{Dock=DockStyle.Fill,BackColor=Bg,Padding=new Padding(4)};
			var c1=Pnl(350,120);
			AddLbl("左图",8,3,c1);_txtLeft=new TextBox{Location=new Pt(8,20),Size=new Sz(200,22),Font=_f9,ReadOnly=true,BackColor=Color.White};
			_btnL=Btn("浏览",215,20,50,22,(s,e)=>{using(var d=new OpenFileDialog{Title="左图",Filter="图像|*.jpg;*.jpeg;*.png;*.bmp;*.tif"}){if(d.ShowDialog()==DialogResult.OK){_txtLeft.Text=d.FileName;_mLeft?.Dispose();_mLeft=Cv2.ImRead(d.FileName);_pbL.Image=BitmapConverter.ToBitmap(_mLeft);Log("左:"+_mLeft.Width+"x"+_mLeft.Height);}}});((Sunny.UI.UIButton)_btnL).FillColor=Pri;
			AddLbl("右图",8,45,c1);_txtRight=new TextBox{Location=new Pt(8,62),Size=new Sz(200,22),Font=_f9,ReadOnly=true,BackColor=Color.White};
			_btnR=Btn("浏览",215,62,50,22,(s,e)=>{using(var d=new OpenFileDialog{Title="右图",Filter="图像|*.jpg;*.jpeg;*.png;*.bmp;*.tif"}){if(d.ShowDialog()==DialogResult.OK){_txtRight.Text=d.FileName;_mRight?.Dispose();_mRight=Cv2.ImRead(d.FileName);_pbR.Image=BitmapConverter.ToBitmap(_mRight);Log("右:"+_mRight.Width+"x"+_mRight.Height);}}});((Sunny.UI.UIButton)_btnR).FillColor=Pri;
			c1.Controls.AddRange(new Control[]{_txtLeft,_btnL,_txtRight,_btnR});p.Controls.Add(c1);

			var c2=Pnl(280,120);
			AddLbl("测试模式",8,3,c2);_cmbMode=new ComboBox{Location=new Pt(8,20),Size=new Sz(220,22),DropDownStyle=ComboBoxStyle.DropDownList,Font=_f9};
			_cmbMode.Items.AddRange(new[]{"C1分割","C2分类","C3 OCR","C1+C2+C3综合"});_cmbMode.SelectedIndex=3;
			_nConf=Num(0.5m,0.05m,1.0m,8,48);AddLbl("Conf:",8,50,38,c2);c2.Controls.AddRange(new Control[]{_cmbMode,_nConf});p.Controls.Add(c2);

			var c3=Pnl(200,120);
			_btnRun=Btn("执行推理",8,8,120,42,BtnRun);((Sunny.UI.UIButton)_btnRun).FillColor=Ok;_btnRun.Font=new Font("微软雅黑",11F,FontStyle.Bold);
			_lblTime=new Label{BackColor=Color.Transparent,Text="---",Location=new Pt(8,54),Size=new Sz(120,18),Font=_f9};
			_btnSave=Btn("保存结果",135,8,40,42,BtnSave);c3.Controls.AddRange(new Control[]{_btnRun,_lblTime,_btnSave});p.Controls.Add(c3);
			return p;
		}

		Panel BuildMid()
		{
			var p=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=3,RowCount=1};
			_pbL=MakePb();_pbR=MakePb();_pbRes=MakePb();
			p.Controls.Add(WrapPb(_pbL,"左图"),0,0);p.Controls.Add(WrapPb(_pbR,"右图"),1,0);p.Controls.Add(WrapPb(_pbRes,"推理结果"),2,0);return p;
		}

		Panel BuildBot()
		{ _rLog=new RichTextBox{Dock=DockStyle.Fill,ReadOnly=true,BackColor=Color.FromArgb(30,30,30),ForeColor=Color.FromArgb(180,180,180),Font=new Font("Consolas",9F),BorderStyle=BorderStyle.None};return WrapCtrl(_rLog,"日志"); }

		async void BtnRun(object s,EventArgs e)
		{ if(_mLeft==null){Log("请先加载左图",true);return;}_btnRun.Enabled=false;var sw=Stopwatch.StartNew();Log("===== 日期码 =====");
			try{await Task.Run(()=>DoDetect());sw.Stop();_lblTime.Text=sw.ElapsedMilliseconds+"ms";Log("完成:"+sw.ElapsedMilliseconds+"ms");}
			catch(Exception ex){Log("失败:"+ex.Message,true);}finally{_btnRun.Enabled=true;} }

		void DoDetect()
		{ var p=ModelParams.Load("barcode");float cf=(float)_nConf.Value;int mode=_cmbMode.SelectedIndex;
			Mat ml=_mLeft.Clone(),mr=_mRight?.Clone();
			if(mr!=null){if(ml.Rows!=mr.Rows){int hh=Math.Max(ml.Rows,mr.Rows);Cv2.Resize(ml,ml,new OpenCvSharp.Size(ml.Cols*hh/ml.Rows,hh));Cv2.Resize(mr,mr,new OpenCvSharp.Size(mr.Cols*hh/mr.Rows,hh));}var mg=new Mat();Cv2.HConcat(ml,mr,mg);ml.Dispose();mr.Dispose();ml=mg;Log("拼接:"+ml.Width+"x"+ml.Height);}
			mr?.Dispose();
			string inf="";Bitmap rd=null;
			switch(mode){
				case 0:TestC1(ml,cf,out rd,out inf);break;
				case 1:TestC2(ml,cf,out rd,out inf);break;
				case 2:TestC3(ml,cf,out rd,out inf);break;
				case 3:TestAll(ml,cf,out rd,out inf);break;
			}
			_resBmp=rd;ml.Dispose();
			InvokeUI(()=>{if(_resBmp!=null)_pbRes.Image=_resBmp;_lblTime.Text=_lblTime.Text+" "+inf;}); }

		void TestC1(Mat m,float cf,out Bitmap rd,out string inf){rd=null;inf="";if(_ai.BackDateCodeSegModel==null){inf="C1未加载";return;}var sw=Stopwatch.StartNew();ResponseList<SegmentationResponse> rsp;int ret=_ai.BackDateCodeSegModel.Run(m,out rsp);var df=new List<(string,float[],float)>();int n=0;if(ret==0&&rsp!=null)foreach(var it in rsp){var mask=it.Item2.Mask;if(mask==null||mask.Empty())continue;using(Mat mc=mask.Clone()){Mat lb=new Mat(),st=new Mat(),ct=new Mat();int nc=Cv2.ConnectedComponentsWithStats(mc,lb,st,ct,PixelConnectivity.Connectivity8);for(int k=1;k<nc;k++){int sx=st.At<int>(k,0),sy=st.At<int>(k,1),sw2=st.At<int>(k,2),sh=st.At<int>(k,3);if(sw2>5&&sh>5){df.Add(("R"+n,new float[]{(float)sx/m.Width,(float)sy/m.Height,(float)(sx+sw2)/m.Width,(float)(sy+sh)/m.Height},0.9f));n++;}}}}rd=DrawRes(m,df);inf="C1:"+n+"区 "+sw.ElapsedMilliseconds+"ms";Log(inf);}
		void TestC2(Mat m,float cf,out Bitmap rd,out string inf){rd=null;inf="";if(_ai.BackDateCodeClsModel==null){inf="C2未加载";return;}var sw=Stopwatch.StartNew();ResponseList<ClassificationResponse> rsp;int ret=_ai.BackDateCodeClsModel.Run(m,out rsp);var df=new List<(string,float[],float)>();if(ret==0&&rsp!=null)foreach(var ci in rsp){string cn=ci.Item2.Labels.Any()?ci.Item2.Labels.First().Label:"";df.Add((cn,new float[]{0.1f,0.1f,0.9f,0.9f},ci.Item2.Labels.Any()?ci.Item2.Labels.First().Score:0f));}rd=DrawRes(m,df);inf="C2:"+df.Count+" "+sw.ElapsedMilliseconds+"ms";Log(inf);}
		void TestC3(Mat m,float cf,out Bitmap rd,out string inf){rd=null;inf="";if(_ai.BackDateCodeOcrModel==null){inf="C3未加载";return;}var sw=Stopwatch.StartNew();ResponseList<OcrResponse> rsp;int ret=_ai.BackDateCodeOcrModel.Run(m,out rsp);var df=new List<(string,float[],float)>();if(ret==0&&rsp!=null)foreach(var rt in rsp){if(rt.Item2.Blocks==null)continue;foreach(var blk in rt.Item2.Blocks){if(string.IsNullOrWhiteSpace(blk.Label))continue;float[] nb=new float[]{0,0,0.5f,0.1f};if(blk.Polygon!=null&&blk.Polygon.Any()){float mx=float.MaxValue,my=float.MaxValue,Mx=float.MinValue,My=float.MinValue;foreach(var pt2 in blk.Polygon){if(pt2.X<mx)mx=pt2.X;if(pt2.Y<my)my=pt2.Y;if(pt2.X>Mx)Mx=pt2.X;if(pt2.Y>My)My=pt2.Y;}nb=new float[]{mx/m.Width,my/m.Height,Mx/m.Width,My/m.Height};}df.Add((blk.Label,nb,blk.Score));}}rd=DrawRes(m,df);inf="C3:"+df.Count+"文本 "+sw.ElapsedMilliseconds+"ms";Log(inf);}
		void TestAll(Mat m,float cf,out Bitmap rd,out string inf){rd=null;inf="";if(_ai.BackDateCodeSegModel==null||_ai.BackDateCodeClsModel==null||_ai.BackDateCodeOcrModel==null){inf="模型不全";return;}int fw=m.Width,fh=m.Height;var sw=Stopwatch.StartNew();var df=new List<(string,float[],float)>();int s1=0,s2=0,s3=0;ResponseList<SegmentationResponse> seg;if(_ai.BackDateCodeSegModel.Run(m,out seg)==0&&seg!=null)foreach(var it in seg){var mask=it.Item2.Mask;if(mask==null||mask.Empty())continue;using(Mat mc=mask.Clone()){Mat lb=new Mat(),st=new Mat(),ct=new Mat();int nc=Cv2.ConnectedComponentsWithStats(mc,lb,st,ct,PixelConnectivity.Connectivity8);for(int k=1;k<nc;k++){int sx=st.At<int>(k,0),sy=st.At<int>(k,1),sw2=st.At<int>(k,2),sh=st.At<int>(k,3);if(sw2<=5||sh<=5)continue;s1++;int mx=Math.Max(0,sx-5),my=Math.Max(0,sy-5),mw=Math.Min(fw-mx,sw2+10),mh=Math.Min(fh-my,sh+10);using(var c2=new Mat(m,new CvR(mx,my,mw,mh)).Clone()){ResponseList<ClassificationResponse> cls;_ai.BackDateCodeClsModel.Run(c2,out cls);if(cls!=null)s2+=cls.Count;}using(var c3=new Mat(m,new CvR(mx,my,mw,mh)).Clone()){ResponseList<OcrResponse> ocr;if(_ai.BackDateCodeOcrModel.Run(c3,out ocr)==0&&ocr!=null)foreach(var rt in ocr){if(rt.Item2.Blocks==null)continue;foreach(var blk in rt.Item2.Blocks){if(string.IsNullOrWhiteSpace(blk.Label))continue;s3++;df.Add((blk.Label,new float[]{(float)mx/fw,(float)my/fh,(float)(mx+mw)/fw,(float)(my+mh)/fh},blk.Score));}}}}}}rd=DrawRes(m,df);inf="C1+C2+C3:"+s1+"/"+s2+"/"+s3+" "+sw.ElapsedMilliseconds+"ms";Log(inf);}

		Bitmap DrawRes(Mat m,List<(string t,float[] b,float s)> df){var bmp=BitmapConverter.ToBitmap(m);int w=bmp.Width,h=bmp.Height;using(var g=Graphics.FromImage(bmp)){g.SmoothingMode=SmoothingMode.AntiAlias;foreach(var d in df){float[] bb=d.b;int x1=(int)(bb[0]*w),y1=(int)(bb[1]*h),x2=(int)(bb[2]*w),y2=(int)(bb[3]*h);if(x2<=x1||y2<=y1)continue;var rc=new Rectangle(x1,y1,x2-x1,y2-y1);using(var fl=new SolidBrush(Color.FromArgb(40,Pri)))g.FillRectangle(fl,rc);using(var pn=new Pen(Pri,3))g.DrawRectangle(pn,rc);using(var f2=new Font("微软雅黑",10,FontStyle.Bold)){var sz=g.MeasureString(d.t,f2);int ly=y1-(int)sz.Height-4;if(ly<4)ly=y1+4;using(var bg2=new SolidBrush(Pri))g.FillRectangle(bg2,x1,ly,sz.Width+6,sz.Height+4);g.DrawString(d.t,f2,Brushes.White,x1+2,ly+1);}}using(var f3=new Font("微软雅黑",14,FontStyle.Bold))g.DrawString(df.Count==0?"无结果":"结果:"+df.Count,f3,Brushes.LightGray,10,10);}return bmp;}

		void BtnSave(object s,EventArgs e){using(var d=new SaveFileDialog{Filter="PNG|*.png",DefaultExt="png"}){if(d.ShowDialog()==DialogResult.OK&&_resBmp!=null){_resBmp.Save(d.FileName);Log("已保存:"+d.FileName);}}}
		void Log(string m,bool e2=false){var l="["+DateTime.Now.ToString("HH:mm:ss.fff")+"] "+m;InvokeUI(()=>{_rLog.SelectionStart=_rLog.TextLength;_rLog.SelectionColor=e2?Ng:Color.FromArgb(180,180,180);_rLog.AppendText(l+"\n");_rLog.ScrollToCaret();});if(e2)Logger.Error(m);else Logger.Info(m);}
		void InvokeUI(Action a){if(!IsDisposed)BeginInvoke(a);}
		Panel Pnl(int w,int h){return new Panel{Width=w,Height=h,BackColor=Card,Margin=new Padding(4)};}
		XLPictureBox MakePb(){return new XLPictureBox{Dock=DockStyle.Fill,BackColor1=Color.FromArgb(50,50,50),BackColor2=Color.FromArgb(70,70,70),BackgroundGridSize=20};}
		Panel WrapPb(XLPictureBox pb,string t){var r=new Panel{Dock=DockStyle.Fill,Padding=new Padding(4)};r.Controls.Add(pb);r.Controls.Add(new Label{Text="  "+t,Dock=DockStyle.Top,Height=20,BackColor=Color.FromArgb(240,242,245),Font=new Font("微软雅黑",8F,FontStyle.Bold)});return r;}
		Panel WrapCtrl(Control c,string t){var r=new Panel{Dock=DockStyle.Fill};r.Controls.Add(c);r.Controls.Add(new Label{Text="  "+t,Dock=DockStyle.Top,Height=20,BackColor=Color.FromArgb(240,242,245),Font=new Font("微软雅黑",8F,FontStyle.Bold)});return r;}
		UIButton Btn(string t,int x,int y,int w,int h,EventHandler eh){var b=new UIButton{Text=t,Location=new Pt(x,y),Size=new Sz(w,h),Font=_f9,Radius=4,Cursor=Cursors.Hand};b.Click+=eh;return b;}
		NumericUpDown Num(decimal v,decimal mn,decimal mx,int x,int y){return new NumericUpDown{Location=new Pt(x,y),Width=62,Minimum=mn,Maximum=mx,Value=v,DecimalPlaces=2,Increment=0.05m,Font=_f9};}
		void AddLbl(string t,int x,int y,Control p,Font f2=null){var lb=new Label{BackColor=Color.Transparent,Text=t,Location=new Pt(x,y),Size=new Sz(50,18),Font=f2??_f9};lb.Parent=p;}
		void AddLbl(string t,int x,int y,int w,Control p,Font f2=null){var lb=new Label{BackColor=Color.Transparent,Text=t,Location=new Pt(x,y),Size=new Sz(w,18),Font=f2??_f9};lb.Parent=p;}
	}
}
