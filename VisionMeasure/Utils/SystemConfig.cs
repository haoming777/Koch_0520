using static CommonLib.Class_Config;

namespace Utils
{
	public static class SystemConfig
	{
		public static string GetValue(string key, string defaultValue = "")
		{
			switch (key)
			{
				//case "ModelRootPath": return _Config.ModelRootPath ?? defaultValue;
				case "Camera1SN": return _Config.Camera1SN ?? defaultValue;
				case "Camera2SN": return _Config.Camera2SN ?? defaultValue;
				case "Camera3SN": return _Config.Camera3SN ?? defaultValue;
				case "Camera4SN": return _Config.Camera4SN ?? defaultValue;
				case "Camera5SN": return _Config.Camera5SN ?? defaultValue;
				case "Camera6SN": return _Config.Camera6SN ?? defaultValue;
				case "Camera7SN": return _Config.Camera7SN ?? defaultValue;
				case "Camera8SN": return _Config.Camera8SN ?? defaultValue;
				//case "PlcIp": return _Config.PlcIp ?? defaultValue;
				case "PlcPort": return _Config.PlcPort.ToString();
				case "ImagePath": return _Config.ImagePath ?? defaultValue;
				case "ControlIP": return _Config.ControlIP ?? defaultValue;
				//case "UseGpu": return _Config.UseGpu.ToString();
				//case "GpuDeviceId": return _Config.GpuDeviceId.ToString();
				default: return defaultValue;
			}
		}

		public static int GetInt(string key, int defaultValue = 0)
		{
			if (int.TryParse(GetValue(key), out int val)) return val;
			return defaultValue;
		}

		public static bool GetBool(string key, bool defaultValue = false)
		{
			if (bool.TryParse(GetValue(key), out bool val)) return val;
			return defaultValue;
		}
	}
}