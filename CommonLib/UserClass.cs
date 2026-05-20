using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CommonLib
{
	public class UserClass
	{
		/// <summary>
		/// 用户编号
		/// </summary>
		public string id;
		/// <summary>
		/// 用户名称
		/// </summary>
		public string name;
		/// <summary>
		/// 权限编号 （0：超级管理员，1：技术员，2：操作员）
		/// </summary>
		public string Grade;
		/// <summary>
		/// 用户权限
		/// </summary>
		public string Role;
	}
}
