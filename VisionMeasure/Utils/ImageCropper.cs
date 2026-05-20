using System.Drawing;
using OpenCvSharp;

namespace Utils
{
	public class ImageCropper
	{
		public Mat CropBottomHalf(Mat src, int boxCount)
		{
			int h = src.Height, startY = h * 2 / 3;
			return new Mat(src, new Rect(0, startY, src.Width, h - startY)).Clone();
		}

		public Mat CropByRatio(Mat src, double ratio)
		{
			int w = (int)(src.Width * ratio);
			int x = (src.Width - w) / 2;
			return new Mat(src, new Rect(x, 0, w, src.Height)).Clone();
		}
	}
}