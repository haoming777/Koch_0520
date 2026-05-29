using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace VisionMeasure.From
{
	public partial class ZoomablePictureBox : UserControl
	{
		private Image _image;
		private float _zoomFactor = 1.0f;
		private float _offsetX = 0f;
		private float _offsetY = 0f;

		private PointF _mouseLastPos;
		private bool _isDragging = false;

		public Image Image
		{
			get => _image;
			set { _image = value; ResetView(); }
		}

		public ZoomablePictureBox()
		{
			// 激活双缓冲，彻底干掉工业相机高频刷新时的撕裂和闪烁
			this.SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint | ControlStyles.OptimizedDoubleBuffer, true);

			this.MouseWheel += Zoom_MouseWheel;
			this.MouseDown += Zoom_MouseDown;
			this.MouseMove += Zoom_MouseMove;
			this.MouseUp += Zoom_MouseUp;
			this.DoubleClick += (s, e) => ResetView();
		}

		public void ResetView()
		{
			if (_image == null)
			{
				Invalidate();
				return;
			}

			// 自动避让上方标签占用的 26px 空间
			float usableHeight = this.Height - 26;

			float scaleX = (float)this.Width / _image.Width;
			float scaleY = usableHeight / _image.Height;

			_zoomFactor = Math.Min(scaleX, scaleY) * 0.96f;
			if (_zoomFactor <= 0) _zoomFactor = 1.0f;

			_offsetX = (this.Width - _image.Width * _zoomFactor) / 2f;
			_offsetY = 26 + (usableHeight - _image.Height * _zoomFactor) / 2f;

			Invalidate();
		}

		protected override void OnPaint(PaintEventArgs e)
		{
			base.OnPaint(e);
			Graphics g = e.Graphics;
			g.SmoothingMode = SmoothingMode.HighQuality; // 开启高级抗锯齿

			// ====================================================================================
			// 【核心实现：利用 GDI+ 纯手写打造高端扁平网页圆角卡片，不依赖任何第三方插件】
			// ====================================================================================
			int borderRadius = 8; // 圆角弧度半径
			Rectangle boundsRect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);

			using (GraphicsPath path = new GraphicsPath())
			{
				path.AddArc(boundsRect.X, boundsRect.Y, borderRadius * 2, borderRadius * 2, 180, 90);
				path.AddArc(boundsRect.Right - (borderRadius * 2), boundsRect.Y, borderRadius * 2, borderRadius * 2, 270, 90);
				path.AddArc(boundsRect.Right - (borderRadius * 2), boundsRect.Bottom - (borderRadius * 2), borderRadius * 2, borderRadius * 2, 0, 90);
				path.AddArc(boundsRect.X, boundsRect.Bottom - (borderRadius * 2), borderRadius * 2, borderRadius * 2, 90, 90);
				path.CloseAllFigures();

				// 设定整个控件的圆角裁剪剪切域 (实现完美的物理圆角裁剪)
				this.Region = new Region(path);

				// 绘制一圈极为细腻的轻量灰网页边缘线
				using (Pen borderPen = new Pen(Color.FromArgb(220, 223, 230), 1))
				{
					g.DrawPath(borderPen, path);
				}
			}

			if (_image == null) return;

			// 保持最清晰的图像细节，专门针对微观条码符号缺陷分析
			g.InterpolationMode = InterpolationMode.NearestNeighbor;
			g.PixelOffsetMode = PixelOffsetMode.HighQuality;

			g.TranslateTransform(_offsetX, _offsetY);
			g.ScaleTransform(_zoomFactor, _zoomFactor);

			g.DrawImage(_image, 0, 0);
		}

		private void Zoom_MouseWheel(object sender, MouseEventArgs e)
		{
			if (_image == null) return;

			float oldZoom = _zoomFactor;
			float scaleFactor = e.Delta > 0 ? 1.15f : 1.0f / 1.15f;

			_zoomFactor *= scaleFactor;
			if (_zoomFactor < 0.01f) _zoomFactor = 0.01f;
			if (_zoomFactor > 100.0f) _zoomFactor = 100.0f;

			float mouseX = e.X;
			float mouseY = e.Y;

			_offsetX = mouseX - (mouseX - _offsetX) * (_zoomFactor / oldZoom);
			_offsetY = mouseY - (mouseY - _offsetY) * (_zoomFactor / oldZoom);

			Invalidate();
		}

		private void Zoom_MouseDown(object sender, MouseEventArgs e)
		{
			if (e.Button == MouseButtons.Left && _image != null)
			{
				_mouseLastPos = e.Location;
				_isDragging = true;
				this.Cursor = Cursors.Hand;
			}
		}

		private void Zoom_MouseMove(object sender, MouseEventArgs e)
		{
			if (_isDragging)
			{
				_offsetX += e.X - _mouseLastPos.X;
				_offsetY += e.Y - _mouseLastPos.Y;
				_mouseLastPos = e.Location;
				Invalidate();
			}
		}

		private void Zoom_MouseUp(object sender, MouseEventArgs e)
		{
			_isDragging = false;
			this.Cursor = Cursors.Default;
		}

		protected override void OnResize(EventArgs e)
		{
			base.OnResize(e);
			ResetView();
		}
	}
}