using System.Collections.Generic;
using System.Linq;
using CommonLib;

namespace Config
{
	public static class CameraTriggerConfig
	{
		public static int DefaultPulseWidthMs = 50;

		public enum TriggerEdgeMode
		{
			RisingEdge,
			FallingEdge
		}

		public class CameraTriggerInfo
		{
			public int CameraId { get; set; }
			public string Name { get; set; }
			public string StationName { get; set; }
			public int InputPort { get; set; } = -1;
			public int OutputPort { get; set; } = -1;
			public int PulseWidthMs { get; set; } = 50;
			public TriggerEdgeMode EdgeMode { get; set; } = TriggerEdgeMode.RisingEdge;
			public override string ToString() => $"Camera{CameraId}({Name}) IN={InputPort} OUT={OutputPort}";
		}

		/// <summary>
		/// IN12传感器边缘模式: RisingLeftFallingRight=上升沿触发左侧(相机7)/下降沿触发右侧(相机8)
		///                      RisingRightFallingLeft=上升沿触发右侧(相机8)/下降沿触发左侧(相机7)
		/// </summary>
		public enum SideSensorEdgeMode
		{
			RisingLeftFallingRight,
			RisingRightFallingLeft
		}

		/// <summary>IN12侧面传感器边缘模式，可在运行时切换</summary>
		public static SideSensorEdgeMode In12EdgeMode { get; set; } = SideSensorEdgeMode.RisingLeftFallingRight;

		/// <summary>更新相机7/8的边缘模式以匹配当前IN12配置</summary>
		public static void ApplyIn12EdgeMode()
		{
			var cam7 = GetConfig(7);
			var cam8 = GetConfig(8);
			if (cam7 != null && cam8 != null)
			{
				if (In12EdgeMode == SideSensorEdgeMode.RisingLeftFallingRight)
				{
					cam7.EdgeMode = TriggerEdgeMode.RisingEdge;
					cam8.EdgeMode = TriggerEdgeMode.FallingEdge;
				}
				else
				{
					cam7.EdgeMode = TriggerEdgeMode.FallingEdge;
					cam8.EdgeMode = TriggerEdgeMode.RisingEdge;
				}
				Logger.Info($"IN12边缘模式已应用: {In12EdgeMode}, Cam7={cam7.EdgeMode}, Cam8={cam8.EdgeMode}");
			}
		}

		public static Dictionary<int, CameraTriggerInfo> TriggerConfigs = new Dictionary<int, CameraTriggerInfo>
		{
			// 相机ID, 输入端口(触发信号), 输出端口(光源触发) - IN4=正面, IN10=端面, IN11=反面, IN12=侧面轴上, IN13=侧面到位
			[1] = new CameraTriggerInfo { CameraId = 1, Name = "正面左", StationName = "Front", InputPort = 4, OutputPort = 9, EdgeMode = TriggerEdgeMode.RisingEdge, PulseWidthMs = 10 },
			[2] = new CameraTriggerInfo { CameraId = 2, Name = "正面右", StationName = "Front", InputPort = 4, OutputPort = 8, EdgeMode = TriggerEdgeMode.RisingEdge, PulseWidthMs = 10 },
			[3] = new CameraTriggerInfo { CameraId = 3, Name = "上端面", StationName = "EndFace", InputPort = 10, OutputPort = 10, EdgeMode = TriggerEdgeMode.RisingEdge, PulseWidthMs = 10 },
			[4] = new CameraTriggerInfo { CameraId = 4, Name = "下端面", StationName = "EndFace", InputPort = 10, OutputPort = 11, EdgeMode = TriggerEdgeMode.RisingEdge, PulseWidthMs = 10 },
			[5] = new CameraTriggerInfo { CameraId = 5, Name = "背面左", StationName = "Back", InputPort = 4, OutputPort = 12, EdgeMode = TriggerEdgeMode.RisingEdge, PulseWidthMs = 10 },
			[6] = new CameraTriggerInfo { CameraId = 6, Name = "背面右", StationName = "Back", InputPort = 4, OutputPort = 13, EdgeMode = TriggerEdgeMode.RisingEdge, PulseWidthMs = 10 },
			[7] = new CameraTriggerInfo { CameraId = 7, Name = "左侧面", StationName = "Side", InputPort = 13, OutputPort = 14, EdgeMode = TriggerEdgeMode.RisingEdge, PulseWidthMs = 10 },
			[8] = new CameraTriggerInfo { CameraId = 8, Name = "右侧面", StationName = "Side", InputPort = 13, OutputPort = 15, EdgeMode = TriggerEdgeMode.FallingEdge, PulseWidthMs = 10 },
		};

		public static CameraTriggerInfo GetConfig(int cameraId)
		{
			TriggerConfigs.TryGetValue(cameraId, out var config);
			return config;
		}

		public static void SetPorts(int cameraId, int inputPort, int outputPort, int pulseWidthMs = 50)
		{
			if (TriggerConfigs.TryGetValue(cameraId, out var config))
			{
				config.InputPort = inputPort;
				config.OutputPort = outputPort;
				config.PulseWidthMs = pulseWidthMs;
			}
		}

		public static List<CameraTriggerInfo> GetUnconfigured()
		{
			return TriggerConfigs.Values.Where(c => c.InputPort < 0 || c.OutputPort < 0).ToList();
		}

		public static bool IsAllConfigured()
		{
			return TriggerConfigs.Values.All(c => c.InputPort >= 0 && c.OutputPort >= 0);
		}
	}
}