using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;

namespace Utils
{
	public static class BitmapFastConverter
	{
		public static byte[] ToJpegBytesFast(this Bitmap bitmap, int quality = 85)
		{
			if (bitmap == null) return null;
			try
			{
				using (var ms = new MemoryStream())
				{
					var jpegEncoder = ImageCodecInfo.GetImageEncoders()
						.FirstOrDefault(codec => codec.MimeType == "image/jpeg");
					if (jpegEncoder != null)
					{
						var encoderParams = new EncoderParameters(1);
						encoderParams.Param[0] = new EncoderParameter(Encoder.Quality, quality);
						bitmap.Save(ms, jpegEncoder, encoderParams);
					}
					else { bitmap.Save(ms, ImageFormat.Jpeg); }
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