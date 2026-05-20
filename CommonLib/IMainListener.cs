using Cognex.VisionPro.ToolBlock;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CommonLib
{
	public interface IMainListener
	{
		/// <summary>
		/// 切换产品
		/// </summary>
		/// <param name="productId">产品id</param>
		/// <param name="spec">规格型号</param>
		bool ChangeCheckProduct(string productId, string spec);

		/// <summary>
		/// 切换产品是 获取对应的视觉程序
		/// </summary>
		CogToolBlock GetVpp(int index);

		/// <summary>
		/// 保存视觉程序
		/// </summary>
		bool SaveVpp(int index);


	}
}
