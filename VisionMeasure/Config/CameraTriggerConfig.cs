using System.Collections.Generic;
using System.Linq;
using CommonLib;

namespace Config
{
	/// <summary>
	/// 相机触发IO配置 — 静态全局配置, 定义8台相机的触发信号映射关系
	/// 核心数据: TriggerConfigs字典(相机ID→触发信息), 包含:
	///   - 输入端口(InputPort): 传感器信号来源(IN4/IN10/IN13)
	///   - 输出端口(OutputPort): 相机外触发脉冲输出
	///   - 边缘模式(EdgeMode): 上升沿/下降沿触发
	///   - 脉冲宽度(PulseWidthMs): 输出脉冲持续时间
	/// IN12特殊处理: 侧面轴上传感器, 上升沿/下降沿分别触发左右相机, 通过 In12EdgeMode 可互换
	/// </summary>
	public static class CameraTriggerConfig
	{
		/// <summary>默认触发脉冲宽度(ms), 可在MainFrm中从DetectionParams覆盖</summary>
		public static int DefaultPulseWidthMs = 50;

		/// <summary>触发边缘模式: RisingEdge=上升沿触发, FallingEdge=下降沿触发</summary>
		public enum TriggerEdgeMode
		{
			RisingEdge,   // 上升沿触发(信号从0→1时触发)
			FallingEdge   // 下降沿触发(信号从1→0时触发)
		}

		/// <summary>
		/// 单个相机的触发配置信息
		/// InputPort=-1 或 OutputPort=-1 表示未配置
		/// </summary>
		public class CameraTriggerInfo
		{
			/// <summary>相机ID(1~8)</summary>
			public int CameraId { get; set; }
			/// <summary>相机名称(用于日志)</summary>
			public string Name { get; set; }
			/// <summary>所属工位名称(Front/Back/EndFace/Side)</summary>
			public string StationName { get; set; }
			/// <summary>触发信号输入端口(IN), -1=未配置</summary>
			public int InputPort { get; set; } = -1;
			/// <summary>相机触发脉冲输出端口(OUT), -1=未配置</summary>
			public int OutputPort { get; set; } = -1;
			/// <summary>输出脉冲宽度(毫秒)</summary>
			public int PulseWidthMs { get; set; } = 50;
			/// <summary>触发边缘模式</summary>
			public TriggerEdgeMode EdgeMode { get; set; } = TriggerEdgeMode.RisingEdge;
			public override string ToString() => $"Camera{CameraId}({Name}) IN={InputPort} OUT={OutputPort}";
		}

		/// <summary>
		/// IN12侧面传感器边缘模式
		/// RisingLeftFallingRight: IN12↑→触发Camera7(左侧) / IN12↓→触发Camera8(右侧) 【默认】
		/// RisingRightFallingLeft: IN12↑→触发Camera8(右侧) / IN12↓→触发Camera7(左侧)
		/// 可通过DetectionParametersForm→侧面Tab中的下拉框切换
		/// </summary>
		public enum SideSensorEdgeMode
		{
			RisingLeftFallingRight,   // 上升沿→左侧(Cam7), 下降沿→右侧(Cam8)
			RisingRightFallingLeft    // 上升沿→右侧(Cam8), 下降沿→左侧(Cam7)
		}

		/// <summary>IN12侧面传感器边缘模式，运行时切换后需调用ApplyIn12EdgeMode()生效</summary>
		public static SideSensorEdgeMode In12EdgeMode { get; set; } = SideSensorEdgeMode.RisingLeftFallingRight;

		/// <summary>
		/// 应用IN12边缘模式: 更新Camera7/8的EdgeMode以匹配当前In12EdgeMode配置
		/// 调用时机: MainFrm.InitCameras()初始化时、用户切换IN12模式后
		/// </summary>
		public static void ApplyIn12EdgeMode()
		{
			var cam7 = GetConfig(7);
			var cam8 = GetConfig(8);
			if (cam7 != null && cam8 != null)
			{
				if (In12EdgeMode == SideSensorEdgeMode.RisingLeftFallingRight)
				{
					cam7.EdgeMode = TriggerEdgeMode.RisingEdge;   // IN12↑→Cam7
					cam8.EdgeMode = TriggerEdgeMode.FallingEdge;  // IN12↓→Cam8
				}
				else
				{
					cam7.EdgeMode = TriggerEdgeMode.FallingEdge;  // IN12↓→Cam7
					cam8.EdgeMode = TriggerEdgeMode.RisingEdge;   // IN12↑→Cam8
				}
				Logger.Info($"IN12边缘模式已应用: {In12EdgeMode}, Cam7={cam7.EdgeMode}, Cam8={cam8.EdgeMode}");
			}
		}

		/// <summary>
		/// 8台相机触发配置表 (静态全局)
		/// 端口映射:
		///   IN4  = 正面+背面共享触发信号
		///   IN10 = 端面工位触发信号
		///   IN13 = 侧面工位传感器(IN12=轴上位置传感器, IN13=到位传感器)
		/// 注意: 当前 Camera1/2/5/6 都用IN4, Camera3/4用IN10, Camera7/8用IN13
		/// </summary>
		public static Dictionary<int, CameraTriggerInfo> TriggerConfigs = new Dictionary<int, CameraTriggerInfo>
		{
			// CameraId, Name, Station, InputPort, OutputPort, EdgeMode, PulseWidthMs
			[1] = new CameraTriggerInfo { CameraId = 1, Name = "正面左", StationName = "Front", InputPort = 4, OutputPort = 9, EdgeMode = TriggerEdgeMode.RisingEdge, PulseWidthMs = 10 },
			[2] = new CameraTriggerInfo { CameraId = 2, Name = "正面右", StationName = "Front", InputPort = 4, OutputPort = 8, EdgeMode = TriggerEdgeMode.RisingEdge, PulseWidthMs = 10 },
			[3] = new CameraTriggerInfo { CameraId = 3, Name = "上端面", StationName = "EndFace", InputPort = 10, OutputPort = 10, EdgeMode = TriggerEdgeMode.RisingEdge, PulseWidthMs = 10 },
			[4] = new CameraTriggerInfo { CameraId = 4, Name = "下端面", StationName = "EndFace", InputPort = 10, OutputPort = 11, EdgeMode = TriggerEdgeMode.RisingEdge, PulseWidthMs = 10 },
			[5] = new CameraTriggerInfo { CameraId = 5, Name = "背面左", StationName = "Back", InputPort = 4, OutputPort = 12, EdgeMode = TriggerEdgeMode.RisingEdge, PulseWidthMs = 10 },
			[6] = new CameraTriggerInfo { CameraId = 6, Name = "背面右", StationName = "Back", InputPort = 4, OutputPort = 13, EdgeMode = TriggerEdgeMode.RisingEdge, PulseWidthMs = 10 },
			[7] = new CameraTriggerInfo { CameraId = 7, Name = "左侧面", StationName = "Side", InputPort = 13, OutputPort = 14, EdgeMode = TriggerEdgeMode.RisingEdge, PulseWidthMs = 10 },
			[8] = new CameraTriggerInfo { CameraId = 8, Name = "右侧面", StationName = "Side", InputPort = 13, OutputPort = 15, EdgeMode = TriggerEdgeMode.FallingEdge, PulseWidthMs = 10 },
		};

		/// <summary>根据相机ID获取触发配置, 不存在返回null</summary>
		public static CameraTriggerInfo GetConfig(int cameraId)
		{
			TriggerConfigs.TryGetValue(cameraId, out var config);
			return config;
		}

		/// <summary>动态设置相机IO端口(运行时修改触发映射)</summary>
		public static void SetPorts(int cameraId, int inputPort, int outputPort, int pulseWidthMs = 50)
		{
			if (TriggerConfigs.TryGetValue(cameraId, out var config))
			{
				config.InputPort = inputPort;
				config.OutputPort = outputPort;
				config.PulseWidthMs = pulseWidthMs;
			}
		}

		/// <summary>获取所有未配置的相机列表(InputPort或OutputPort为-1)</summary>
		public static List<CameraTriggerInfo> GetUnconfigured()
		{
			return TriggerConfigs.Values.Where(c => c.InputPort < 0 || c.OutputPort < 0).ToList();
		}

		/// <summary>检查是否所有相机都已配置IO端口</summary>
		public static bool IsAllConfigured()
		{
			return TriggerConfigs.Values.All(c => c.InputPort >= 0 && c.OutputPort >= 0);
		}
	}
}