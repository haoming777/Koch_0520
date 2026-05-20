using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace CommonLib
{
	public interface IFormPlugin
	{
		
		/// <summary>
		/// 返回窗口
		/// </summary>
		/// <returns></returns>
		Form GetForm();
		/// <summary>
		/// 设置窗口参数
		/// </summary>
		/// <param name="vParams"></param>
		void SetParams(Dictionary<string, object> vParams);
		void setMainListener(IMainListener listener);
	}
}
