using System;

namespace CommonLib
{
	public class SystemConfig
	{
		public static SystemConfig Instance { get; set; } = new SystemConfig();

		public bool IsSaveOkImage { get; set; } = true;
		public bool IsSaveNgImage { get; set; } = true;
		public bool IsSaveOkRawImage { get; set; } = false;
		public bool IsSaveNgRawImage { get; set; } = true;
		public int FlyingShootDurationMs { get; set; } = 50;

		// ================== 运动与拍照参数配置 ==================
		public float AxisSpeed { get; set; } = 100f;     // 运行速度
		public float AxisAccel { get; set; } = 1000f;    // 加速度
		public float AxisDecel { get; set; } = 1000f;    // 减速度
		public float PhotoStartPos { get; set; } = 0f;   // 拍照起始位置
		public float PhotoEndPos { get; set; } = 100f;   // 拍照结束位置

		// ================== 兼容旧代码 ==================

		// 【新增补丁】兼容 Program.cs 里调用的 Load()
		public static SystemConfig Load(string path = "")
		{
			return Instance;
		}

		public void Save(string path = "") { }

		public static string GetValue(string section, string key, string defaultVal = "") => defaultVal;
		public static int GetInt(string section, string key, int defaultVal = 0) => defaultVal;
		public static int GetInt(string key, int defaultVal) => defaultVal;
		public static string GetValue(string key, string defaultVal) => defaultVal;
	}
}