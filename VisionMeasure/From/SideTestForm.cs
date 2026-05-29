using Models;
using Config; using OpenCvSharp;
using OpenCvSharp.Extensions; using Sunny.UI; using System; using System.Collections.Generic; using System.Diagnostics; using System.Drawing; using System.Drawing.Drawing2D; using System.IO; using System.Linq; using System.Threading.Tasks; using System.Windows.Forms; using CommonLib; using XL.Controls; using YoloInference; using CvR = OpenCvSharp.Rect; using Sz = System.Drawing.Size; using Pt = System.Drawing.Point;

namespace VisionMeasure.From
{
	public partial class SideTestForm : UIForm
	{
		static readonly Color Pri = Color.FromArgb(0,122,204), Ok = Color.FromArgb(39,174,96), Ng = Color.FromArgb(231,76,60), Bg = Color.FromArgb(245,247,250), Card = Color.White;
		AiModelManager _ai; YoloOnnx _model; Mat _img, _resHead, _resTail, _resMerged;
		TextBox _txtImg; NumericUpDown _nConf, _nIou, _nCrop;
		UIButton _btnImg, _btnRun, _btnSave;
		XLPictureBox _pbOri, _pbHead, _pbTail, _pbRes; Label _lblTime; RichTextBox _rLog;
		Font _f9 = new Font("微软雅黑",9F);

		public SideTestForm(AiModelManager ai=null) { _ai=ai; Build(); Load+=(s,e)=>{ if(_ai==null)LoadAi();Log("侧面测试就绪");}; }
		void LoadAi() { try{_ai=new AiModelManager(ModelPathConfig.LoadFromSysConfig());_ai.LoadAllModels();Log("模型加载完成");}catch(Exception ex){Log("加载失败:"+ex.Message,true);} }

		void Build()
		{
			Text="侧面缺陷测试";Size=new Sz(1400,900);StartPosition=FormStartPosition.CenterParent;BackColor=Bg;
			var lo=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=3,Padding=new Padding(10)};
			lo.RowStyles.Add(new RowStyle(SizeType.Absolute,80));
			lo.RowStyles.Add(new RowStyle(SizeType.Percent,55));
			lo.RowStyles.Add(new RowStyle(SizeType.Percent,45));
			lo.Controls.Add(BuildTop(),0,0);lo.Controls.Add(BuildMid(),0,1);lo.Controls.Add(BuildBot(),0,2);
			Controls.Add(lo);
		}

		Panel BuildTop()
		{
			var p=new FlowLayoutPanel{Dock=DockStyle.Fill,BackColor=Bg,Padding=new Padding(4)};
			var c1=Pnl(380,65);
			AddLbl("图像",8,5,c1);_txtImg=new TextBox{Location=new Pt(8,26),Size=new Sz(260,24),Font=_f9,ReadOnly=true,BackColor=Color.White};
			_btnImg=Btn("浏览...",275,26,45,24,(s,e)=>{using(var d=new OpenFileDialog{Title="选择图像",Filter="图像|*.jpg;*.jpeg;*.png;*.bmp;*.tif"}){if(d.ShowDialog()==DialogResult.OK){_txtImg.Text=d.FileName;_img?.Dispose();_img=Cv2.ImRead(d.FileName);_pbOri.Image=BitmapConverter.ToBitmap(_img);Log("加载:"+_img.Width+"x"+_img.Height);}}});
			((Sunny.UI.UIButton)_btnImg).FillColor=Pri;c1.Controls.AddRange(new Control[]{_txtImg,_btnImg});p.Controls.Add(c1);

			var c2=Pnl(280,65);
			AddLbl("参数",8,5,c2);_nConf=Num(0.5m,0.05m,1.0m,8,26);_nIou=Num(0.45m,0.05m,1.0m,78,26);_nCrop=Num(2.0m,0.5m,5.0m,148,26);
			AddLbl("Conf:",8,28,38,c2);AddLbl("IOU:",78,28,32,c2);AddLbl("裁比:",148,28,32,c2);
			c2.Controls.AddRange(new Control[]{_nConf,_nIou,_nCrop});p.Controls.Add(c2);

			var c3=Pnl(200,65);
			_btnRun=Btn("执行检测",8,10,120,40,BtnRun);((Sunny.UI.UIButton)_btnRun).FillColor=Ok;_btnRun.Font=new Font("微软雅黑",11F,FontStyle.Bold);
			_lblTime=new Label{BackColor=Color.Transparent,Text="---",Location=new Pt(8,55),Size=new Sz(120,18),Font=_f9};
			_btnSave=Btn("保存",135,10,55,40,BtnSave);c3.Controls.AddRange(new Control[]{_btnRun,_lblTime,_btnSave});p.Controls.Add(c3);
			return p;
		}

		Panel BuildMid()
		{
			var p=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=4,RowCount=1};
			_pbOri=MakePb();_pbHead=MakePb();_pbTail=MakePb();_pbRes=MakePb();
			p.Controls.Add(WrapPb(_pbOri,"原图"),0,0);p.Controls.Add(WrapPb(_pbHead,"头部裁剪"),1,0);
			p.Controls.Add(WrapPb(_pbTail,"尾部裁剪"),2,0);p.Controls.Add(WrapPb(_pbRes,"检测结果"),3,0);return p;
		}

		Panel BuildBot()
		{ _rLog=new RichTextBox{Dock=DockStyle.Fill,ReadOnly=true,BackColor=Color.FromArgb(30,30,30),ForeColor=Color.FromArgb(180,180,180),Font=new Font("Consolas",9F),BorderStyle=BorderStyle.None};return WrapCtrl(_rLog,"日志"); }

		async void BtnRun(object s,EventArgs e)
		{ if(_img==null){Log("请先加载图像",true);return;}_btnRun.Enabled=false;var sw=Stopwatch.StartNew();Log("===== 侧面检测 =====");
			try{await Task.Run(()=>DoDetect());sw.Stop();_lblTime.Text=sw.ElapsedMilliseconds+"ms";Log("完成:"+sw.ElapsedMilliseconds+"ms");}
			catch(Exception ex){Log("失败:"+ex.Message,true);}finally{_btnRun.Enabled=true;} }

		void DoDetect()
		{ _model=_ai.SideDefectModel;if(_model==null){Log("模型未加载",true);return;}
			float cf=(float)_nConf.Value,io=(float)_nIou.Value,cropR=(float)_nCrop.Value;
			int h=_img.Height,w=_img.Width,cw=(int)(h*cropR);if(cw>w)cw=w;
			using(var hd=new Mat(_img,new CvR(0,0,cw,h)).Clone())using(var tl=new Mat(_img,new CvR(w-cw,0,cw,h)).Clone())
			{
				_resHead=hd.Clone();_resTail=tl.Clone();
				var bt=new List<Mat>{hd,tl};var rs=_model.PredictBatch(bt,cf,io);
				var df=new List<(string t,float[] b,float s)>();
				if(rs!=null)for(int ti=0;ti<rs.Count;ti++){var r=rs[ti];int ox=ti==1?w-cw:0;if(r?.BoxesN!=null)for(int j=0;j<r.BoxesN.Length;j++){var bx=r.BoxesN[j];df.Add(("缺陷"+r.ClassIds[j],new float[]{(ox+bx.X)/w,bx.Y/h,(ox+bx.X+bx.Width)/w,(bx.Y+bx.Height)/h},r.Scores[j]));}}
				_resMerged=DrawRes(_img.Clone(),df,cropR,cw);
				InvokeUI(()=>{_pbHead.Image=BitmapConverter.ToBitmap(_resHead);_pbTail.Image=BitmapConverter.ToBitmap(_resTail);_pbRes.Image=BitmapConverter.ToBitmap(_resMerged);});
				Log("检出:"+df.Count+"个缺陷");
			}
		}

		Mat DrawRes(Mat src,List<(string t,float[] b,float s)> df,float cropR,int cw)
		{ var bmp=BitmapConverter.ToBitmap(src);int w2=bmp.Width,h2=bmp.Height;using(var g=Graphics.FromImage(bmp)){g.SmoothingMode=SmoothingMode.AntiAlias;
			// 绘制裁剪区域（黄虚线）
			using(var pnY=new Pen(Color.Yellow,2){DashStyle=DashStyle.Dash}){
				g.DrawRectangle(pnY,0,0,cw,h2); g.DrawRectangle(pnY,w2-cw,0,cw,h2);
				using(var fY=new Font("微软雅黑",9)){g.DrawString("头部裁剪区域",fY,Brushes.Yellow,5,5);g.DrawString("尾部裁剪区域",fY,Brushes.Yellow,w2-cw-100,5);}
			}
			foreach(var d in df){float[] bb=d.b;int x1=(int)(bb[0]*w2),y1=(int)(bb[1]*h2),x2=(int)(bb[2]*w2),y2=(int)(bb[3]*h2);if(x2<=x1||y2<=y1)continue;var rc=new Rectangle(x1,y1,x2-x1,y2-y1);
				using(var fl=new SolidBrush(Color.FromArgb(40,Ng)))g.FillRectangle(fl,rc);using(var pn=new Pen(Ng,3))g.DrawRectangle(pn,rc);
				using(var f=new Font("微软雅黑",10,FontStyle.Bold)){var sz=g.MeasureString(d.t,f);int ly=y1-(int)sz.Height-4;if(ly<4)ly=y1+4;using(var bg=new SolidBrush(Ng))g.FillRectangle(bg,x1,ly,sz.Width+6,sz.Height+4);g.DrawString(d.t,f,Brushes.White,x1+2,ly+1);}}
			using(var f2=new Font("微软雅黑",14,FontStyle.Bold))g.DrawString(df.Count==0?"OK":"NG:"+df.Count+"缺陷 裁比:"+cropR.ToString("F1"),f2,df.Count==0?Brushes.Green:Brushes.Red,10,10);
			using(var f3=new Font("微软雅黑",9))g.DrawString("裁剪宽="+cw+"px",f3,Brushes.LightGray,cw+10,5);
		}return BitmapConverter.ToMat(bmp);}

		void BtnSave(object s,EventArgs e){using(var d=new SaveFileDialog{Filter="PNG|*.png",DefaultExt="png"}){if(d.ShowDialog()==DialogResult.OK&&_pbRes.Image!=null){_pbRes.Image.Save(d.FileName);Log("已保存:"+d.FileName);}}}

		void Log(string m,bool e2=false){var l="["+DateTime.Now.ToString("HH:mm:ss.fff")+"] "+m;InvokeUI(()=>{_rLog.SelectionStart=_rLog.TextLength;_rLog.SelectionColor=e2?Ng:Color.FromArgb(180,180,180);_rLog.AppendText(l+"\n");_rLog.ScrollToCaret();});if(e2)Logger.Error(m);else Logger.Info(m);}
		void InvokeUI(Action a){if(!IsDisposed)BeginInvoke(a);}
		Panel Pnl(int w,int h){return new Panel{Width=w,Height=h,BackColor=Card,Margin=new Padding(4)};}
		XLPictureBox MakePb(){return new XLPictureBox{Dock=DockStyle.Fill,BackColor1=Color.FromArgb(50,50,50),BackColor2=Color.FromArgb(70,70,70),BackgroundGridSize=20};}
		Panel WrapPb(XLPictureBox pb,string t){var r=new Panel{Dock=DockStyle.Fill,Padding=new Padding(4)};r.Controls.Add(pb);r.Controls.Add(new Label{Text="  "+t,Dock=DockStyle.Top,Height=20,BackColor=Color.FromArgb(240,242,245),Font=new Font("微软雅黑",8F,FontStyle.Bold)});return r;}
		Panel WrapCtrl(Control c,string t){var r=new Panel{Dock=DockStyle.Fill};r.Controls.Add(c);r.Controls.Add(new Label{Text="  "+t,Dock=DockStyle.Top,Height=20,BackColor=Color.FromArgb(240,242,245),Font=new Font("微软雅黑",8F,FontStyle.Bold)});return r;}
		UIButton Btn(string t,int x,int y,int w,int h,EventHandler eh){var b=new UIButton{Text=t,Location=new Pt(x,y),Size=new Sz(w,h),Font=_f9,Radius=4,Cursor=Cursors.Hand};b.Click+=eh;return b;}
		NumericUpDown Num(
			decimal v,decimal mn,decimal mx,int x,int y){return new NumericUpDown{Location=new Pt(x,y),Width=62,Minimum=mn,Maximum=mx,Value=v,DecimalPlaces=2,Increment=0.05m,Font=_f9};}
		void AddLbl(string t,int x,int y,Control p,Font f2=null){var lb=new Label{BackColor=Color.Transparent,Text=t,Location=new Pt(x,y),Size=new Sz(50,18),Font=f2??_f9};lb.Parent=p;}
		void AddLbl(string t,int x,int y,int w,Control p,Font f2=null){var lb=new Label{BackColor=Color.Transparent,Text=t,Location=new Pt(x,y),Size=new Sz(w,18),Font=f2??_f9};lb.Parent=p;}
	}
}
