using System;
using OpenCvSharp; // 需要 NuGet 安装 OpenCvSharp4

namespace VisionMeasure.Utils
{
	public  class ImageHelper
	{
		/// <summary>
		/// 根据左右像素值水平裁剪图像(OpenCvSharp).
		/// </summary>
		/// <param name="img">输入Mat图像</param>
		/// <param name="leftPixels">左侧裁剪像素宽度, null则不裁</param>
		/// <param name="rightPixels">右侧裁剪像素宽度, null则不裁</param>
		/// <returns>裁剪后的Mat对象(与原图共享内存)</returns>
		/// <exception cref="ArgumentNullException">图像为空</exception>
		/// <exception cref="ArgumentOutOfRangeException">裁剪参数无效</exception>
		public static Mat CropImageHorizontallyCv2(Mat img, int? leftPixels = null, int? rightPixels = null)
		{
		// 输入校验
		if (img == null || img.Empty())
		{
			throw new ArgumentNullException(nameof(img), "传入的图像不能为空。");
		}

		int width = img.Width;
		int height = img.Height;

		// 计算裁剪边界
		int leftBoundary = leftPixels ?? 0;
		int rightBoundary = rightPixels.HasValue ? width - rightPixels.Value : width;

		// 边界校验
		if (leftBoundary < 0 || (rightPixels.HasValue && rightPixels.Value < 0))
			throw new ArgumentOutOfRangeException("裁剪的像素值不能为负数。");

		if (leftBoundary >= rightBoundary)
			throw new ArgumentOutOfRangeException($"裁剪无效: 左裁{leftBoundary}px, 右边界{rightBoundary}, 超出图片宽度({width}px)。");

		// 基于ROI裁剪(与原图共享内存)
		int newWidth = rightBoundary - leftBoundary;
		Rect roi = new Rect(leftBoundary, 0, newWidth, height);
		Mat croppedImg = new Mat(img, roi);

		return croppedImg;
		}
	}
}