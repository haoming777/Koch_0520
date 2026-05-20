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
    public static class GlobalVar
    {
        //public static ModbusTcpNet ModBus;
        public static SiemensS7Net ModBus;
        public static bool PlcConnectState;
        public static bool FrmState;
        public static DaHuaSDK CameraSdk1;
        public static DaHuaSDK CameraSdk2;
        public static DaHuaSDK CameraSdk3;
        public static DaHuaSDK CameraSdk4;
        public static DaHuaSDK CameraSdk5;
		public static DaHuaSDK CameraSdk6;
		public static DaHuaSDK CameraSdk7;
		public static DaHuaSDK CameraSdk8;
		public static double Threshold_Up;
        public static double Threshold_Down;
        public static double Threshold_Stand;

    }

}
