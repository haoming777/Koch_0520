using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace CommonLib
{
    public interface IPLCListener
    {
        /// <summary>
        /// 获取ModBus对象
        /// </summary>
        /// <returns></returns>
        object GetModBus();
    }
}
