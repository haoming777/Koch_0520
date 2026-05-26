using System;
using OpenCvSharp; // 需要 NuGet 安装 OpenCvSharp4

namespace VisionMeasure.Utils
{
	public  class ImageHelper
	{
		/// <summary>
		        /// 基于 OpenCvSharp 根据指定的左右像素值水平裁剪图片。
		        /// </summary>
		        /// <param name="img">由 OpenCvSharp 读取的 Mat 图像对象（相当于 Python 的 np.ndarray）。</param>
		        /// <param name="leftPixels">左侧需要裁剪掉的像素宽度。如果为 null，则默认不裁。</param>
		        /// <param name="rightPixels">右侧需要裁剪掉的像素宽度。如果为 null，则默认不裁。</param>
		        /// <returns>裁剪后的图像矩阵（Mat 对象）。</returns>
		        /// <exception cref="ArgumentNullException">当传入的图片为空时抛出。</exception>
		        /// <exception cref="ArgumentOutOfRangeException">当裁剪像素值为负数，或裁剪总像素超过图片宽度时抛出。</exception>
		public static Mat CropImageHorizontallyCv2(Mat img, int? leftPixels = null, int? rightPixels = null)
		{
			// 1. 基础校验 (防御性编程)
			if (img == null || img.Empty())
			{
				throw new ArgumentNullException(nameof(img), "传入的图像不能为空。");
			}

			// 获取图像尺寸
			int width = img.Width;
			int height = img.Height;

			// 2. 计算切片边界 (使用 C# 的 ?? 空合并运算符)
			int leftBoundary = leftPixels ?? 0;

			// 使用 .HasValue 判断可空类型是否有值
			int rightBoundary = rightPixels.HasValue ? width - rightPixels.Value : width;

			// 3. 数据与边界校验
			if (leftBoundary < 0 || (rightPixels.HasValue && rightPixels.Value < 0))
			{
				throw new ArgumentOutOfRangeException("裁剪的像素值不能为负数。");
			}

			if (leftBoundary >= rightBoundary)
			{
				throw new ArgumentOutOfRangeException(
				  $"裁剪无效：左侧裁剪了 {leftBoundary} 像素，右侧边界在 {rightBoundary}，" +
				  $"裁剪总和已超出或等于图片总宽度 ({width} 像素)。");
			}

			// 4. 执行裁剪
			// 在 C# 的 OpenCvSharp 中，我们不使用切片，而是定义一个 Rect (X, Y, Width, Height) 作为 ROI (感兴趣区域)
			int newWidth = rightBoundary - leftBoundary;
			Rect roi = new Rect(leftBoundary, 0, newWidth, height);

			// 基于原图和 ROI 创建新的 Mat 对象。
			// 注意：这在内存中共享原图数据（等同于 NumPy 切片的浅拷贝行为）。
			Mat croppedImg = new Mat(img, roi);

			return croppedImg;
		}
	}
}