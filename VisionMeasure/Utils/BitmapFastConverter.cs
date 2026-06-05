using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

 namespace VisionMeasure.Utils
{
	public static class BitmapFastConverter
	/// <summary>Bitmap快速转换扩展 — 提供ToJpegBytesFast(缓存JpegCodec避免每次遍历编码器)和ToPngBytesFast方法, 用于高速存图场景</summary>
	{
		// 缓存JPEG编码器，避免每次存图遍历ImageCodecInfo
		private static readonly ImageCodecInfo JpegCodec = ImageCodecInfo.GetImageEncoders()
			.First(c => c.FormatID == ImageFormat.Jpeg.Guid);

		public static byte[] ToJpegBytesFast(this Bitmap bitmap, int quality = 85)
		{
			if (bitmap == null) return null;
			try
			{
				using (var ms = new MemoryStream())
				{
					var encoderParams = new EncoderParameters(1);
					encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, quality);
					bitmap.Save(ms, JpegCodec, encoderParams);
					return ms.ToArray();
				}
			}
			catch { return null; }
		}

		public static byte[] ToBmpBytesFast(this Bitmap bitmap)
		{
			if (bitmap == null) return null;
			try
			{
				using (var ms = new MemoryStream())
				{
					bitmap.Save(ms, ImageFormat.Jpeg);
					return ms.ToArray();
				}
			}
			catch { return null; }
		}
	}
}