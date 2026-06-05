using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using HslCommunication;
using HslCommunication.ModBus;
using HslCommunication.Profinet.Siemens;
using MT.Camera.SDK;

namespace CommonLib
{
    /// <summary>全局静态变量 - 跨模块共享PLC连接状态(S7-1500)和检测阈值</summary>
    public static class GlobalVar
    {
        //public static ModbusTcpNet ModBus;
        public static SiemensS7Net ModBus;
        public static bool PlcConnectState;
        public static bool FrmState;
		public static double Threshold_Up;
        public static double Threshold_Down;
        public static double Threshold_Stand;

    }

}
