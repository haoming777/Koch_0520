using OpenCvSharp;
using System.Drawing;

namespace Utils
{
	public static class MatExtensions
	{
		/// <summary>
		/// Mat 转换为 Bitmap - 使用不同方法名避免与 OpenCvSharp.Extensions 冲突
		/// </summary>
		public static Bitmap MatToBitmap(Mat mat)
		{
			if (mat == null || mat.Empty())
				return null;

			try
			{
				return OpenCvSharp.Extensions.BitmapConverter.ToBitmap(mat);
			}
			catch (System.Exception ex)
			{
				Logger.Error($"MatToBitmap 转换失败: {ex.Message}");
				return null;
			}
		}

		/// <summary>
		/// Bitmap 转换为 Mat
		/// </summary>
		public static Mat BitmapToMat(Bitmap bitmap)
		{
			if (bitmap == null)
				return null;

			try
			{
				return OpenCvSharp.Extensions.BitmapConverter.ToMat(bitmap);
			}
			catch (System.Exception ex)
			{
				Logger.Error($"BitmapToMat 转换失败: {ex.Message}");
				return null;
			}
		}

		/// <summary>
		/// 安全释放 Mat
		/// </summary>
		public static void SafeDispose(this Mat mat)
		{
			if (mat != null && !mat.IsDisposed)
			{
				mat.Dispose();
			}
		}
	}
}