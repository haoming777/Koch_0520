using System;

namespace CommonLib
{
	/// <summary>
	/// SKU产品数据模型 — 每个SKU对应一条生产线上的产品规格
	/// 数据来源: Config/主数据.csv (由SkuDatabase.LoadData加载)
	/// 用户可在UI手动修改P/Z/MM等字段，修改后的值保存到 Config/sku_params.json
	/// </summary>
	public class SkuData
	{
		/// <summary>SKU编号(如"181712303")，唯一标识</summary>
		public string SkuNumber { get; set; }

		/// <summary>每排盒数(P值)，决定各工位分盒数量</summary>
		public int P { get; set; }

		/// <summary>排数(Z值)</summary>
		public int Z { get; set; }

		/// <summary>产品宽度(MM)，单位毫米</summary>
		public int MM { get; set; }

		/// <summary>PZ组合信息(显示用)</summary>
		public string PZInfo { get; set; }

		// ── 裁图坐标(像素) ——
		// 这些值定义各工位图像的裁剪范围，从"裁图比例.csv"加载
		// LeftPx=左边界(裁掉左侧像素数)，RightPx=右边界

		/// <summary>正面左图-左裁边(px)</summary>
		public int FrontLeft_LeftPx { get; set; }
		/// <summary>正面左图-右裁边(px)</summary>
		public int FrontLeft_RightPx { get; set; }
		/// <summary>正面右图-左裁边(px)</summary>
		public int FrontRight_LeftPx { get; set; }
		/// <summary>正面右图-右裁边(px)</summary>
		public int FrontRight_RightPx { get; set; }
		/// <summary>上端面-左裁边(px)</summary>
		public int UpperEndFace_LeftPx { get; set; }
		/// <summary>下端面-左裁边(px)</summary>
		public int LowerEndFace_LeftPx { get; set; }
		/// <summary>背面左图-左裁边(px)</summary>
		public int BackLeft_LeftPx { get; set; }
		/// <summary>背面左图-右裁边(px)</summary>
		public int BackLeft_RightPx { get; set; }
		/// <summary>背面右图-左裁边(px)</summary>
		public int BackRight_LeftPx { get; set; }
		/// <summary>背面右图-右裁边(px)</summary>
		public int BackRight_RightPx { get; set; }

		/// <summary>正面P号码标准值(用于OCR比对)，如"P181712303"</summary>
		public string FrontPCode { get; set; }
		/// <summary>背面条形码标准值(用于ZXing比对)</summary>
		public string BackBarcode { get; set; }
		/// <summary>日期码打码格式(MFG/LOT/双排)</summary>
		public string CodingFormat { get; set; }

		public override string ToString() => $"{P}P{Z}Z{MM}mm";
	}

	/// <summary>
	/// 运动轴参数配置 — 与PLC监控界面(ControlFrm)共用
	/// 保存位置: Config/AxisParams.json
	/// MainFrm.InitStations() 和 ControlFrm.ControlFrm_Load() 都会加载此配置
	/// </summary>
	public class AxisParamConfig
	{
		// ── 基础轴参数 ──
		/// <summary>轴号(0~N)，ZMC控制器上的物理轴编号</summary>
		public int Axis = 0;
		/// <summary>轴类型(1=伺服, 其他值见ZMC文档)</summary>
		public int Atype = 1;
		/// <summary>脉冲当量(Units/圈)</summary>
		public float Units = 1f;
		/// <summary>默认运行速度</summary>
		public float Speed = 50f;
		/// <summary>加速度</summary>
		public float Accel = 5000f;
		/// <summary>减速度</summary>
		public float Decel = 5000f;
		/// <summary>起始速度(低速)</summary>
		public float Lspeed = 10f;
		/// <summary>S曲线平滑参数(0=梯形)</summary>
		public float Sramp = 0f;
		/// <summary>爬行速度(回零时用)</summary>
		public float CreepSpeed = 10f;
		/// <summary>最大速度限制</summary>
		public int MaxSpeed = 10000;

		// ── 限位IO端口 ──
		/// <summary>正限位输入端口(IN)</summary>
		public int FwdIn = 14;
		/// <summary>负限位输入端口(IN)</summary>
		public int RevIn = 15;
		/// <summary>原点输入端口(IN)</summary>
		public int DatumIn = 16;

		// ── 侧面工位运动范围 ──
		/// <summary>起始位置(回原点后的工作起点)</summary>
		public float StartPos = 0f;
		/// <summary>结束位置(前进终点)</summary>
		public float EndPos = 100f;
		/// <summary>前进速度</summary>
		public float FwdSpeed = 50f;
		/// <summary>返回速度(通常比前进快)</summary>
		public float RetSpeed = 100f;

		// ── 侧面工位拍照控制 ──
		/// <summary>最大拍照数量(一周期最多拍多少张)</summary>
		public int MaxPhotoCount = 12;
		/// <summary>循环周期延时(ms)</summary>
		public int CycleDelayMs = 500;

		/// <summary>条码检测启用(ControlFrm用)</summary>
		public bool EnableBarcodeCheck = true;
		/// <summary>日期码检测启用(ControlFrm用)</summary>
		public bool EnableDateCodeCheck = true;

		// ── 安全锁配置 (IN8传感器) ──
		/// <summary>安全锁输入端口(IN8)，0=禁用</summary>
		public int SafetyLockPort = 8;
		/// <summary>true=高电平有效(IN8=1→安全), false=低电平有效</summary>
		public bool SafetyLockActiveHigh = true;
		/// <summary>安全锁恢复模式: 0=继续执行, 1=返回起始位</summary>
		public int SafetyLockRecovery = 0;

		/// <summary>JSON配置文件路径: Config/AxisParams.json</summary>
		private static string _jsonPath => System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Config", "AxisParams.json");

		/// <summary>保存配置到JSON文件</summary>
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

		/// <summary>从JSON文件加载配置，文件不存在则返回默认值</summary>
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