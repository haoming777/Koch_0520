using System.Collections.Generic;
using System.Linq;

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

		public static Dictionary<int, CameraTriggerInfo> TriggerConfigs = new Dictionary<int, CameraTriggerInfo>
		{
			// 相机ID, 输入端口(触发信号), 输出端口(光源触发)
			[1] = new CameraTriggerInfo { CameraId = 1, Name = "正面左", StationName = "Front", InputPort = 9, OutputPort = 8, EdgeMode = TriggerEdgeMode.RisingEdge, PulseWidthMs = 10 },
			[2] = new CameraTriggerInfo { CameraId = 2, Name = "正面右", StationName = "Front", InputPort = 9, OutputPort = 9, EdgeMode = TriggerEdgeMode.RisingEdge, PulseWidthMs = 10 },
			[3] = new CameraTriggerInfo { CameraId = 3, Name = "背面左", StationName = "Back", InputPort = 11, OutputPort = 10, EdgeMode = TriggerEdgeMode.RisingEdge, PulseWidthMs = 10 },
			[4] = new CameraTriggerInfo { CameraId = 4, Name = "背面右", StationName = "Back", InputPort = 11, OutputPort = 11, EdgeMode = TriggerEdgeMode.RisingEdge, PulseWidthMs = 10 },
			[5] = new CameraTriggerInfo { CameraId = 5, Name = "上端面", StationName = "EndFace", InputPort = 10, OutputPort = 12, EdgeMode = TriggerEdgeMode.RisingEdge, PulseWidthMs = 10 },
			[6] = new CameraTriggerInfo { CameraId = 6, Name = "下端面", StationName = "EndFace", InputPort = 10, OutputPort = 13, EdgeMode = TriggerEdgeMode.RisingEdge, PulseWidthMs = 10 },
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