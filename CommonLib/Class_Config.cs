using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static Ini.IniAPI;
using static System.Net.Mime.MediaTypeNames;

namespace CommonLib
{
	public class Class_Config
	{
		private static Class_Config _instance;
		private Class_Config() { }
		// 定义标识符确保多线程安全性
		private static readonly object locker = new object();
		public static Class_Config GetInstance()
		{
			if (_instance == null)
			{
				lock (locker)
				{
					if (_instance == null)
					{
						_instance = new Class_Config();
					}
				}
			}
			return _instance;
		}

		public static Class_Config _Config
		{
			get
			{
				if (_instance == null)
				{
					lock (locker)
					{
						if (_instance == null)
						{
							_instance = new Class_Config();
						}
					}
				}

				return _instance;
			}
		}
		public bool test = false;

		public static string _path = Directory.GetCurrentDirectory();
		public string _iniPath = _path + "\\setup.ini";//这里改成了static类型的，不知道会不会影响
		public string _vppPath = _path + "\\vpp\\";
		public string _dataPath = _path + "\\data\\data.dt";
		#region 需要存储的数据


		/// <summary>
		/// 当前检测型号
		/// </summary>
		public string CurCheckSpec
		{
			get
			{
				return GetPrivateProfileString("system", "curSpec", "A50", _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "system", "curSpec", value.ToString());
			}
		}

		/// <summary>
		/// 当前检测把型
		/// </summary>
		public string CurCheckBa
		{
			get
			{
				return GetPrivateProfileString("system", "curBa", "塑料把", _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "system", "curBa", value.ToString());
			}
		}
		/// <summary>
		/// 第一行检测字符
		/// </summary>
		public string FirstString
		{
			get
			{
				return GetPrivateProfileString("system", "First", "MFG", _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "system", "First", value.ToString());
			}
		}
		/// <summary>
		/// 第二行检测字符
		/// </summary>
		public string SecondString
		{
			get
			{
				return GetPrivateProfileString("system", "Second", "MFG", _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "system", "Second", value.ToString());
			}
		}

		public int cameraDebug
		{
			get
			{
				return GetPrivateProfileInt("system", "cameraDebug", 0, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "system", "cameraDebug", value.ToString());
			}
		}

		// GPU配置
		public bool UseGpu
		{
			get => bool.Parse(GetPrivateProfileString("AI_Models", "UseGpu", "True", _iniPath));
			set => INIWriteValue(_iniPath, "AI_Models", "UseGpu", value.ToString());
		}

		public int DefaultGpuDeviceId
		{
			get => GetPrivateProfileInt("AI_Models", "DefaultGpuDeviceId", 0, _iniPath);
			set => INIWriteValue(_iniPath, "AI_Models", "DefaultGpuDeviceId", value.ToString());
		}

		public int VimoGpuDeviceId
		{
			get => GetPrivateProfileInt("AI_Models", "VimoGpuDeviceId", 1, _iniPath);
			set => INIWriteValue(_iniPath, "AI_Models", "VimoGpuDeviceId", value.ToString());
		}

		public int YoloGpuDeviceId
		{
			get => GetPrivateProfileInt("AI_Models", "YoloGpuDeviceId", 0, _iniPath);
			set => INIWriteValue(_iniPath, "AI_Models", "YoloGpuDeviceId", value.ToString());
		}

		
		/// <summary>
		/// 当前产品ID
		/// </summary>
		public string CurProductId
		{
			get
			{
				return GetPrivateProfileString("system", "curId", "1", _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "system", "curId", value.ToString());
			}
		}

		/// <summary>
		/// 图片存储地址
		/// </summary>
		public string ImagePath
		{
			get
			{
				return GetPrivateProfileString("system", "imagePath", _path + "\\image", _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "system", "imagePath", value.ToString());
			}
		}

		//public string DataPath
		//{
		//	get
		//	{
		//		return GetPrivateProfileString("路径", "dataPath", _path + "\\data", _iniPath);
		//	}
		//	set
		//	{
		//		INIWriteValue(_iniPath, "路径", "dataPath", value.ToString());
		//	}
		//}

		/// <summary>
		/// 相机序列号
		/// </summary>
		public string Camera1SN
		{
			get
			{
				return GetPrivateProfileString("camera", "camera1sn", "DA1665132", _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "camera", "camera1sn", value.ToString());
			}
		}



		public bool IsSaveOkImage
		{
			get
			{
				return bool.Parse(GetPrivateProfileString("system", "saveokimage", "true", _iniPath));
			}
			set
			{
				INIWriteValue(_iniPath, "system", "saveokimage", value.ToString());
			}

		}
		public bool IsSaveNgImage
		{
			get
			{
				return bool.Parse(GetPrivateProfileString("system", "savengimage", "true", _iniPath));
			}
			set
			{
				INIWriteValue(_iniPath, "system", "savengimage", value.ToString());
			}

		}
		public bool IsSaveOkRawImage
		{
			get
			{
				return bool.Parse(GetPrivateProfileString("system", "saveokrawimage", "true", _iniPath));
			}
			set
			{
				INIWriteValue(_iniPath, "system", "saveokrawimage", value.ToString());
			}

		}
		public bool IsSaveNgRawImage
		{
			get
			{
				return bool.Parse(GetPrivateProfileString("system", "savengrawimage", "true", _iniPath));
			}
			set
			{
				INIWriteValue(_iniPath, "system", "savengrawimage", value.ToString());
			}

		}
		/// <summary>
		/// 保留天数
		/// </summary>
		public int ImageDays
		{
			get
			{
				return GetPrivateProfileInt("system", "imagedays", (int)7, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "system", "imagedays", value.ToString());
			}
		}
		public string Camera2SN
		{
			get
			{
				return GetPrivateProfileString("camera", "camera2sn", "DA1665132", _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "camera", "camera2sn", value.ToString());
			}
		}


		/// <summary>
		/// 相机1是否屏蔽最后一位
		/// </summary>
		public string Camera1Ignore
		{
			get
			{
				return GetPrivateProfileString("StandChar", "Camera1Ignore", "False", _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "StandChar", "Camera1Ignore", value.ToString());
			}
		}
		/// <summary>
		/// 相机2是否屏蔽最后一位
		/// </summary>
		public string Camera2Ignore
		{
			get
			{
				return GetPrivateProfileString("StandChar", "Camera2Ignore", "False", _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "StandChar", "Camera2Ignore", value.ToString());
			}
		}



		public string Camera3SN
		{
			get
			{
				return GetPrivateProfileString("camera", "camera3sn", "DA1665132", _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "camera", "camera3sn", value.ToString());
			}
		}

		public string Camera4SN
		{
			get
			{
				return GetPrivateProfileString("camera", "camera4sn", "DA1665132", _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "camera", "camera4sn", value.ToString());
			}
		}

		public string Camera5SN
		{
			get
			{
				return GetPrivateProfileString("camera", "camera5sn", "DA1665132", _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "camera", "camera5sn", value.ToString());
			}
		}

		public string Camera6SN
		{
			get
			{
				return GetPrivateProfileString("camera", "camera6sn", "DA1665132", _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "camera", "camera6sn", value.ToString());
			}
		}

		public string Camera7SN
		{
			get
			{
				return GetPrivateProfileString("camera", "camera7sn", "DA1665132", _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "camera", "camera7sn", value.ToString());
			}
		}

		public string Camera8SN
		{
			get
			{
				return GetPrivateProfileString("camera", "camera8sn", "DA1665132", _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "camera", "camera8sn", value.ToString());
			}
		}

		public string ControlIP
		{
			get
			{
				return GetPrivateProfileString("control", "ipaddr", "192.168000.0.11", _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "control", "ipaddr", value.ToString());
			}
		}


		public string ModbusIP
		{
			get
			{
				return GetPrivateProfileString("modbus", "modbusIP", "127.0.0.1", _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "modbus", "modbusIP", value.ToString());
			}
		}

		public int ModbusPort
		{
			get
			{
				return GetPrivateProfileInt("modbus", "modbusPort", 502, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "modbus", "modbusPort", value.ToString());
			}
		}
		/// <summary>
		/// 倍率，系统属性
		/// </summary>
		public double K
		{
			get
			{
				return GetPrivateProfileDouble("system", "K", 0.05, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "system", "K", value.ToString());
			}
		}

		/// <summary>
		/// 倍率，系统属性
		/// </summary>
		public double K_Cam3
		{
			get
			{
				return GetPrivateProfileDouble("params", "K_Cam3", 0.05, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "params", "K_Cam3", value.ToString());
			}
		}

		/// <summary>
		/// 补偿值
		/// </summary>
		public double Offset
		{
			get
			{
				return GetPrivateProfileDouble("system", "Offset", 0.05, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "system", "Offset", value.ToString());
			}
		}


		/// <summary>
		/// 补偿值
		/// </summary>
		public double Astrict
		{
			get
			{
				return GetPrivateProfileDouble("system", "Astrict", 2, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "system", "Astrict", value.ToString());
			}
		}

		/// <summary>
		/// 段差阈值parameters
		/// </summary>
		public double DuanChaThreshold
		{
			get
			{
				return GetPrivateProfileDouble("Parameter", _Config.CurCheckSpec + "_Duancha", 5, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "Parameter", _Config.CurCheckSpec + "_Duancha", value.ToString());
			}
		}

		/// <summary>
		/// 检测总数
		/// </summary>
		public double total
		{
			get
			{
				return GetPrivateProfileDouble("count", "total", 0, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "count", "total", value.ToString());
			}
		}
		/// <summary>
		/// 合格数
		/// </summary>
		public double ok
		{
			get
			{
				return GetPrivateProfileDouble("count", "ok", 0, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "count", "ok", value.ToString());
			}
		}
		/// <summary>
		/// 不良数
		/// </summary>
		public double ng_cam1
		{
			get
			{
				return GetPrivateProfileDouble("count", "ng_cam1", 0, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "count", "ng_cam1", value.ToString());
			}
		}

		/// <summary>
		/// 不良数
		/// </summary>
		public double ng_cam2
		{
			get
			{
				return GetPrivateProfileDouble("count", "ng_cam2", 0, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "count", "ng_cam2", value.ToString());
			}
		}

		/// <summary>
		/// 不良数
		/// </summary>
		public double ng_cam3
		{
			get
			{
				return GetPrivateProfileDouble("count", "ng_cam3", 0, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "count", "ng_cam3", value.ToString());
			}
		}

		/// <summary>
		/// 不良数
		/// </summary>
		public double ng_cam4
		{
			get
			{
				return GetPrivateProfileDouble("count", "ng_cam4", 0, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "count", "ng_cam4", value.ToString());
			}
		}

		/// <summary>
		/// 不良数
		/// </summary>
		public double ng_cam5
		{
			get
			{
				return GetPrivateProfileDouble("count", "ng_cam5", 0, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "count", "ng_cam5", value.ToString());
			}
		}

		/// <summary>
		/// 相机一运行总耗时
		/// </summary>
		public int TotalTimeCam1
		{
			get
			{
				return GetPrivateProfileInt("system", "totalTimeCam1", 0, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "system", "totalTimeCam1", value.ToString());
			}
		}

		/// <summary>
		/// 相机一运行总耗时
		/// </summary>
		public int TotalTimeCam2
		{
			get
			{
				return GetPrivateProfileInt("system", "totalTimeCam2", 0, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "system", "totalTimeCam2", value.ToString());
			}
		}


		#endregion

		#region 系统设置
		/// <summary>
		/// 是否初始化相机
		/// </summary>
		public string IFInitCamera
		{
			get
			{
				return GetPrivateProfileString("system", "IFInitCamera", "True", _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "system", "IFInitCamera", value.ToString());
			}
		}

		public bool IFSaveLog
		{
			get
			{
				return bool.Parse(GetPrivateProfileString("system", "IFRunSaveLog", "False", _iniPath));
			}
			set
			{
				INIWriteValue(_iniPath, "system", "IFRunSaveLog", value.ToString());
			}
		}

		/// <summary>
		/// 是否强制NG相机四
		/// </summary>
		public bool IFCamera4NG
		{
			get
			{
				return bool.Parse(GetPrivateProfileString("system", "IFCamera4NG", "False", _iniPath));
			}
			set
			{
				INIWriteValue(_iniPath, "system", "IFCamera4NG", value.ToString());
			}
		}

		public string ImagePath1
		{
			get
			{
				return GetPrivateProfileString("temp", "ImagePath1", "True", _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "temp", "ImagePath1", value.ToString());
			}
		}

		public string ImagePath2
		{
			get
			{
				return GetPrivateProfileString("temp", "ImagePath2", "True", _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "temp", "ImagePath2", value.ToString());
			}
		}
		public string ImagePath2_1
		{
			get
			{
				return GetPrivateProfileString("temp", "ImagePath2_1", "True", _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "temp", "ImagePath2_1", value.ToString());
			}
		}

		public string ImagePath3
		{
			get
			{
				return GetPrivateProfileString("temp", "ImagePath3", "True", _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "temp", "ImagePath3", value.ToString());
			}
		}

		public string ImagePath4
		{
			get
			{
				return GetPrivateProfileString("temp", "ImagePath4", "True", _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "temp", "ImagePath4", value.ToString());
			}
		}

		public string ImagePath5
		{
			get
			{
				return GetPrivateProfileString("temp", "ImagePath5", "True", _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "temp", "ImagePath5", value.ToString());
			}
		}
		#endregion

		#region 运动控制
		/// <summary>
		/// 红灯输入口
		/// </summary>
		public int Red_Light_Num
		{
			get
			{
				return GetPrivateProfileInt("control", "Red_Light_Num", 0, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "control", "Red_Light_Num", value.ToString());
			}
		}

		/// <summary>
		/// 绿灯输入口
		/// </summary>
		public int Green_Light_Num
		{
			get
			{
				return GetPrivateProfileInt("control", "Green_Light_Num", 0, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "control", "Green_Light_Num", value.ToString());
			}
		}

		/// <summary>
		/// 黄灯输入口
		/// </summary>
		public int Yellow_Light_Num
		{
			get
			{
				return GetPrivateProfileInt("control", "Yellow_Light_Num", 0, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "control", "Yellow_Light_Num", value.ToString());
			}
		}

		/// <summary>
		/// 黄灯输入口
		/// </summary>
		public int Buzzer_Num
		{
			get
			{
				return GetPrivateProfileInt("control", "Buzzer_Num", 0, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "control", "Buzzer_Num", value.ToString());
			}
		}
		#endregion

		#region 运行时轴初始化参数

		#region 轴0
		/// <summary>
		/// 脉冲当量
		/// </summary>
		public double axis0_Units
		{
			get
			{
				return GetPrivateProfileDouble("axis", "axis0_Units", 2500, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "axis", "axis0_Units", value.ToString());
			}
		}

		/// <summary>
		/// 轴速度
		/// </summary>
		public double axis0_Speed
		{
			get
			{
				return GetPrivateProfileDouble("axis", "axis0_Speed", 20, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "axis", "axis0_Speed", value.ToString());
			}
		}

		/// <summary>
		/// 加速度
		/// </summary>
		public double axis0_Accel
		{
			get
			{
				return GetPrivateProfileDouble("axis", "axis0_Accel", 10000, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "axis", "axis0_Accel", value.ToString());
			}
		}

		/// <summary>
		/// 减速度
		/// </summary>
		public double axis0_Decel
		{
			get
			{
				return GetPrivateProfileDouble("axis", "axis0_Decel", 10000, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "axis", "axis0_Decel", value.ToString());
			}
		}

		/// <summary>
		/// S曲线
		/// </summary>
		public double axis0_Sramp
		{
			get
			{
				return GetPrivateProfileDouble("axis", "axis0_Sramp", 0, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "axis", "axis0_Sramp", value.ToString());
			}
		}

		/// <summary>
		/// 起始速度
		/// </summary>
		public double axis0_Lspeed
		{
			get
			{
				return GetPrivateProfileDouble("axis", "axis0_Lspeed", 0, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "axis", "axis0_Lspeed", value.ToString());
			}
		}

		#endregion

		#endregion

		#region 初始化时轴初始化参数
		#region 正负极限、原点
		/// <summary>
		/// 轴0 原点
		/// </summary>
		public int axis0_Datum
		{
			get
			{
				return GetPrivateProfileInt("axis", "axis0_Datum", 4, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "axis", "axis0_Datum", value.ToString());
			}
		}

		/// <summary>
		/// 轴0 正限
		/// </summary>
		public int axis0_Fwd
		{
			get
			{
				return GetPrivateProfileInt("axis", "axis0_Fwd", 2, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "axis", "axis0_Fwd", value.ToString());
			}
		}

		/// <summary>
		/// 轴0 负限
		/// </summary>
		public int axis0_Rev
		{
			get
			{
				return GetPrivateProfileInt("axis", "axis0_Rev", 3, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "axis", "axis0_Rev", value.ToString());
			}
		}

		/// <summary>
		/// 轴1 原点
		/// </summary>
		public int axis1_Datum
		{
			get
			{
				return GetPrivateProfileInt("axis", "axis1_Datum", 7, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "axis", "axis1_Datum", value.ToString());
			}
		}

		/// <summary>
		/// 轴1 正限
		/// </summary>
		public int axis1_Fwd
		{
			get
			{
				return GetPrivateProfileInt("axis", "axis1_Fwd", 5, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "axis", "axis1_Fwd", value.ToString());
			}
		}

		/// <summary>
		/// 轴1 负限
		/// </summary>
		public int axis1_Rev
		{
			get
			{
				return GetPrivateProfileInt("axis", "axis1_Rev", 6, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "axis", "axis1_Rev", value.ToString());
			}
		}

		/// <summary>
		/// 轴2 原点
		/// </summary>
		public int axis2_Datum
		{
			get
			{
				return GetPrivateProfileInt("axis", "axis2_Datum", 13, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "axis", "axis2_Datum", value.ToString());
			}
		}

		/// <summary>
		/// 轴2 正限
		/// </summary>
		public int axis2_Fwd
		{
			get
			{
				return GetPrivateProfileInt("axis", "axis2_Fwd", 11, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "axis", "axis2_Fwd", value.ToString());
			}
		}

		/// <summary>
		/// 轴2 负限
		/// </summary>
		public int axis2_Rev
		{
			get
			{
				return GetPrivateProfileInt("axis", "axis2_Rev", 12, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "axis", "axis2_Rev", value.ToString());
			}
		}

		#endregion



		/// <summary>
		/// 爬行速度
		/// </summary>
		public double axis_CreepSpeed_Init
		{
			get
			{
				return GetPrivateProfileDouble("axis", "CreepSpeed_Init", 1, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "axis", "CreepSpeed_Init", value.ToString());
			}
		}

		/// <summary>
		/// 脉冲当量
		/// </summary>
		public double axis_Units_Init
		{
			get
			{
				return GetPrivateProfileDouble("axis", "Units_Init", 2500, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "axis", "Units_Init", value.ToString());
			}
		}

		/// <summary>
		/// 轴速度
		/// </summary>
		public double axis_Speed_Init
		{
			get
			{
				return GetPrivateProfileDouble("axis", "Speed_Init", 20, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "axis", "Speed_Init", value.ToString());
			}
		}

		/// <summary>
		/// 加速度
		/// </summary>
		public double axis_Accel_Init
		{
			get
			{
				return GetPrivateProfileDouble("axis", "Accel_Init", 10000, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "axis", "Accel_Init", value.ToString());
			}
		}

		/// <summary>
		/// 减速度
		/// </summary>
		public double axis_Decel_Init
		{
			get
			{
				return GetPrivateProfileDouble("axis", "Decel_Init", 10000, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "axis", "Decel_Init", value.ToString());
			}
		}

		/// <summary>
		/// 减速度
		/// </summary>
		public double axis_Sramp_Init
		{
			get
			{
				return GetPrivateProfileDouble("axis", "Sramp_Init", 0, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "axis", "Sramp_Init", value.ToString());
			}
		}

		/// <summary>
		/// 起始速度
		/// </summary>
		public double axis_Lspeed_Init
		{
			get
			{
				return GetPrivateProfileDouble("axis", "Lspeed_Init", 0, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "axis", "Lspeed_Init", value.ToString());
			}
		}

		#endregion

		#region PLC


		public string PlcIP
		{
			get
			{
				return GetPrivateProfileString("plc", "ip", "192.160.1.88", _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "plc", "ip", value.ToString());
			}
		}

		public int PlcPort
		{
			get
			{
				return GetPrivateProfileInt("plc", "port", 502, _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "plc", "port", value.ToString());
			}
		}

		/// <summary>
		/// 心跳地址
		/// </summary>
		public string keepAlive
		{
			get
			{
				return GetPrivateProfileString("plc_address", "keepAlive", "D10006", _iniPath);
			}
			set
			{
				INIWriteValue(_iniPath, "plc_address", "keepAlive", value.ToString());
			}
		}

		#endregion


	
		#region AI模型路径配置

		public string ModelRootPath
		{
			get => GetPrivateProfileString("AI_Models", "ModelRootPath", @"D:\AI\Models", _iniPath);
			set => INIWriteValue(_iniPath, "AI_Models", "ModelRootPath", value);
		}

		// 正面模型
		public string FrontPCodeOcrModel
		{
			get => GetPrivateProfileString("AI_Models", "FrontPCodeOcrModel", "", _iniPath);
			set => INIWriteValue(_iniPath, "AI_Models", "FrontPCodeOcrModel", value);
		}

		public string FrontPCodeOcrModuleId
		{
			get => GetPrivateProfileString("AI_Models", "FrontPCodeOcrModuleId", "3", _iniPath);
			set => INIWriteValue(_iniPath, "AI_Models", "FrontPCodeOcrModuleId", value);
		}

		public string FrontBoxBreakModel
		{
			get => GetPrivateProfileString("AI_Models", "FrontBoxBreakModel", "", _iniPath);
			set => INIWriteValue(_iniPath, "AI_Models", "FrontBoxBreakModel", value);
		}

		public string FrontFilmBreakModel
		{
			get => GetPrivateProfileString("AI_Models", "FrontFilmBreakModel", "", _iniPath);
			set => INIWriteValue(_iniPath, "AI_Models", "FrontFilmBreakModel", value);
		}

		// 端面模型
		public string EndFaceUpperModel
		{
			get => GetPrivateProfileString("AI_Models", "EndFaceUpperModel", "", _iniPath);
			set => INIWriteValue(_iniPath, "AI_Models", "EndFaceUpperModel", value);
		}

		public string EndFaceLowerModel
		{
			get => GetPrivateProfileString("AI_Models", "EndFaceLowerModel", "", _iniPath);
			set => INIWriteValue(_iniPath, "AI_Models", "EndFaceLowerModel", value);
		}

		// 背面模型
		public string BackBarcodeModel
		{
			get => GetPrivateProfileString("AI_Models", "BackBarcodeModel", "", _iniPath);
			set => INIWriteValue(_iniPath, "AI_Models", "BackBarcodeModel", value);
		}

		public string BackDateCodeModel
		{
			get => GetPrivateProfileString("AI_Models", "BackDateCodeModel", "", _iniPath);
			set => INIWriteValue(_iniPath, "AI_Models", "BackDateCodeModel", value);
		}

		public string BackDateCodeModuleId
		{
			get => GetPrivateProfileString("AI_Models", "BackDateCodeModuleId", "0", _iniPath);
			set => INIWriteValue(_iniPath, "AI_Models", "BackDateCodeModuleId", value);
		}

		public string BackHookDamageModel
		{
			get => GetPrivateProfileString("AI_Models", "BackHookDamageModel", "", _iniPath);
			set => INIWriteValue(_iniPath, "AI_Models", "BackHookDamageModel", value);
		}

		public string BackHookSlightModel
		{
			get => GetPrivateProfileString("AI_Models", "BackHookSlightModel", "", _iniPath);
			set => INIWriteValue(_iniPath, "AI_Models", "BackHookSlightModel", value);
		}

		public string BackCutCharModel
		{
			get => GetPrivateProfileString("AI_Models", "BackCutCharModel", "", _iniPath);
			set => INIWriteValue(_iniPath, "AI_Models", "BackCutCharModel", value);
		}

		public string BackCutCharModuleId
		{
			get => GetPrivateProfileString("AI_Models", "BackCutCharModuleId", "0", _iniPath);
			set => INIWriteValue(_iniPath, "AI_Models", "BackCutCharModuleId", value);
		}

		// 侧面模型
		public string SideDefectModel
		{
			get => GetPrivateProfileString("AI_Models", "SideDefectModel", "", _iniPath);
			set => INIWriteValue(_iniPath, "AI_Models", "SideDefectModel", value);
		}

		public string SideDefectModuleId
		{
			get => GetPrivateProfileString("AI_Models", "SideDefectModuleId", "0", _iniPath);
			set => INIWriteValue(_iniPath, "AI_Models", "SideDefectModuleId", value);
		}



		public int GpuDeviceId
		{
			get => GetPrivateProfileInt("AI_Models", "GpuDeviceId", 0, _iniPath);
			set => INIWriteValue(_iniPath, "AI_Models", "GpuDeviceId", value.ToString());
		}

		#endregion
	}
}
