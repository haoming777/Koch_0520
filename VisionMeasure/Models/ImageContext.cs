using System;
using System.Drawing;
using OpenCvSharp;

namespace Models
{
	public class ImageContext : IDisposable
	{
		public long ProductId { get; set; }
		public Bitmap OriginalBitmap { get; set; }
		public Mat OriginalMat { get; set; }
		public Bitmap RenderBitmap { get; set; }  // 渲染后的图片
		public Mat RenderMat { get; set; }
		public DateTime ReceiveTime { get; set; } = DateTime.Now;
		public DateTime ProcessStartTime { get; set; }
		public DateTime ProcessEndTime { get; set; }

		public void Dispose()
		{
			OriginalBitmap?.Dispose();
			OriginalBitmap = null;
			OriginalMat?.Dispose();
			OriginalMat = null;
			RenderBitmap?.Dispose();
			RenderBitmap = null;
			RenderMat?.Dispose();
			RenderMat = null;
		}
	}
}