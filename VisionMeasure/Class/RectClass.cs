using OpenCvSharp;
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace VisionMeasure.Class
{
	public class RectClass
	{
		public float X1, Y1, X2, Y2, X3, Y3, X4, Y4;

		public RectClass(float x1, float y1, float x2, float y2, float x3, float y3, float x4, float y4)
		{
			X1 = x1; Y1 = y1;
			X2 = x2; Y2 = y2;
			X3 = x3; Y3 = y3;
			X4 = x4; Y4 = y4;
		}

		// 计算矩形的中心Y坐标
		public double GetCenterY()
		{
			return (Y1 + Y2 + Y3 + Y4) / 4.0;
		}
		public double GetCenterX()
		{
			return (X1 + X2 + X3 + X4) / 4.0;
		}

		public PointF GetCenterXy()
		{
			return new PointF(Convert.ToSingle((X1 + X2 + X3 + X4) / 4), Convert.ToSingle((Y1 + Y2 + Y3 + Y4) / 4));
		}
	}

	public struct VivoStruct
	{
		string Label;
		double Score;
		List<Point2f> Polygon;
	}
}
