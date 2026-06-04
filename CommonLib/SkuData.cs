using System;

namespace CommonLib
{
	public class SkuData
	{
		public string SkuNumber { get; set; }
		public int P { get; set; }
		public int Z { get; set; }
		public int MM { get; set; }
		public string PZInfo { get; set; }

		public int FrontLeft_LeftPx { get; set; }
		public int FrontLeft_RightPx { get; set; }
		public int FrontRight_LeftPx { get; set; }
		public int FrontRight_RightPx { get; set; }
		public int UpperEndFace_LeftPx { get; set; }
		public int LowerEndFace_LeftPx { get; set; }
		public int BackLeft_LeftPx { get; set; }
		public int BackLeft_RightPx { get; set; }
		public int BackRight_LeftPx { get; set; }
		public int BackRight_RightPx { get; set; }

		public string FrontPCode { get; set; }
		public string BackBarcode { get; set; }
		public string CodingFormat { get; set; }

		public override string ToString() => $"{P}P{Z}Z{MM}mm";
	}

	public class AxisParamConfig
	{
		public int Axis = 0, Atype = 1;
		public float Units = 1f, Speed = 50f, Accel = 5000f, Decel = 5000f;
		public float Lspeed = 10f, Sramp = 0f, CreepSpeed = 10f;
		public int MaxSpeed = 10000;
		public int FwdIn = 14, RevIn = 15, DatumIn = 16;
		public float StartPos = 0f, EndPos = 100f;
		public float FwdSpeed = 50f, RetSpeed = 100f;
		public int MaxPhotoCount = 12;
		public int CycleDelayMs = 500;
		public bool EnableBarcodeCheck = true;
		public bool EnableDateCodeCheck = true;
		// 安全锁: IN8=关门(1=安全) / 开门(0=不安全)
		public int SafetyLockPort = 8;
		public bool SafetyLockActiveHigh = true;
		public int SafetyLockRecovery = 0;  // 0=继续执行, 1=返回起始位

		private static string _jsonPath => System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "AxisParams.json");

		public void Save()
		{
			try
			{
				var dir = System.IO.Path.GetDirectoryName(_jsonPath);
				if (!System.IO.Directory.Exists(dir)) System.IO.Directory.CreateDirectory(dir);
				var json = Newtonsoft.Json.JsonConvert.SerializeObject(this, Newtonsoft.Json.Formatting.Indented);
				System.IO.File.WriteAllText(_jsonPath, json);
			}
			catch { }
		}

		public static AxisParamConfig Load()
		{
			try
			{
				if (!System.IO.File.Exists(_jsonPath)) return new AxisParamConfig();
				var json = System.IO.File.ReadAllText(_jsonPath);
				return Newtonsoft.Json.JsonConvert.DeserializeObject<AxisParamConfig>(json) ?? new AxisParamConfig();
			}
			catch { return new AxisParamConfig(); }
		}
	}
}