using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Config;
using CommonLib;

namespace VisionMeasure.Utils
{
	public class SkuDatabase
	{
		private List<SkuData> _skuList = new List<SkuData>();
		private readonly object _lock = new object();

		public event Action OnDataChanged;

		public enum DataSourceType
		{
			LocalCsv,
			SqlServer
		}

		public DataSourceType CurrentDataSource { get; set; } = DataSourceType.LocalCsv;
		public string SqlConnectionString { get; set; } = "";
		public string CsvPath { get; set; } = "";

		public SkuDatabase()
		{
			string baseDir = AppDomain.CurrentDomain.BaseDirectory;
			CsvPath = Path.Combine(baseDir, "Config", "主数据.csv");
			Logger.Info($"SKU数据库初始化，CSV路径: {CsvPath}");
		}

		public bool LoadData()
		{
			try
			{
				bool ok = false;
				if (CurrentDataSource == DataSourceType.SqlServer && !string.IsNullOrEmpty(SqlConnectionString))
					ok = LoadFromSqlServer();
				else
					ok = LoadFromCsv();
				if (ok) LoadCropData();
				return ok;
			}
			catch (Exception ex)
			{
				Logger.Error($"加载SKU数据失败: {ex.Message}");
				return false;
			}
		}

		/// <summary>加载裁图比例CSV，合并到已有SKU数据</summary>
		private void LoadCropData()
		{
			string cropPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "裁图比例.csv");
			if (!File.Exists(cropPath))
			{
				cropPath = Path.Combine(Path.GetDirectoryName(CsvPath) ?? ".", "裁图比例.csv");
				if (!File.Exists(cropPath)) { Logger.Info("裁图比例CSV未找到，跳过"); return; }
			}
			try
			{
				var lines = File.ReadAllLines(cropPath, Encoding.GetEncoding("GB2312"));
				if (lines.Length <= 1) return;
				var headers = lines[0].Split(',');
				// 列索引映射
				int idxSpec = -1, idxFL_L = -1, idxFL_R = -1, idxFR_L = -1, idxFR_R = -1;
				int idxUp_L = -1, idxLo_L = -1, idxBL_L = -1, idxBL_R = -1, idxBR_L = -1, idxBR_R = -1;
				for (int i = 0; i < headers.Length; i++)
				{
					string h = headers[i].Trim();
					if (h.Contains("几P")) idxSpec = i;
					if (h.Contains("正面左") && h.Contains("左侧")) idxFL_L = i;
					if (h.Contains("正面左") && h.Contains("右侧")) idxFL_R = i;
					if (h.Contains("正面右") && h.Contains("左侧")) idxFR_L = i;
					if (h.Contains("正面右") && h.Contains("右侧")) idxFR_R = i;
					if (h.Contains("上端面")) idxUp_L = i;
					if (h.Contains("下端面")) idxLo_L = i;
					if (h.Contains("背面左") && h.Contains("左侧")) idxBL_L = i;
					if (h.Contains("背面左") && h.Contains("右侧")) idxBL_R = i;
					if (h.Contains("背面右") && h.Contains("左侧")) idxBR_L = i;
					if (h.Contains("背面右") && h.Contains("右侧")) idxBR_R = i;
				}
				Logger.Info($"裁图CSV加载: {lines.Length - 1}行, 列索引 spec={idxSpec}");
				int merged = 0;
				for (int i = 1; i < lines.Length; i++)
				{
					if (string.IsNullOrWhiteSpace(lines[i])) continue;
					var vals = lines[i].Split(',');
					string spec = idxSpec >= 0 && idxSpec < vals.Length ? vals[idxSpec].Trim() : "";
					if (string.IsNullOrEmpty(spec)) continue;
					// 查找匹配的SKU
					foreach (var sku in _skuList)
					{
						string skuSpec = sku.P + "P" + sku.MM + "mm";
						if (skuSpec != spec) continue;
						sku.FrontLeft_LeftPx = ParseInt(vals, idxFL_L);
						sku.FrontLeft_RightPx = ParseInt(vals, idxFL_R);
						sku.FrontRight_LeftPx = ParseInt(vals, idxFR_L);
						sku.FrontRight_RightPx = ParseInt(vals, idxFR_R);
						sku.UpperEndFace_LeftPx = ParseInt(vals, idxUp_L);
						sku.LowerEndFace_LeftPx = ParseInt(vals, idxLo_L);
						sku.BackLeft_LeftPx = ParseInt(vals, idxBL_L);
						sku.BackLeft_RightPx = ParseInt(vals, idxBL_R);
						sku.BackRight_LeftPx = ParseInt(vals, idxBR_L);
						sku.BackRight_RightPx = ParseInt(vals, idxBR_R);
						merged++;
					}
				}
				Logger.Info($"裁图参数已合并: {merged}个SKU");
			}
			catch (Exception ex) { Logger.Error($"裁图CSV加载失败: {ex.Message}"); }
		}

		private int ParseInt(string[] vals, int idx) { if (idx < 0 || idx >= vals.Length) return 0; int.TryParse(vals[idx].Trim(), out int r); return r; }

		private bool LoadFromCsv()
		{
			if (!File.Exists(CsvPath))
			{
				Logger.Warning($"SKU CSV文件不存在: {CsvPath}");
				return false;
			}

			lock (_lock)
			{
				_skuList.Clear();

				var lines = File.ReadAllLines(CsvPath, Encoding.UTF8);
				Logger.Info($"读取CSV文件: {CsvPath}, 共 {lines.Length} 行");

				if (lines.Length <= 1) return false;

				// 解析表头，找到正确的列索引
				var headers = lines[0].Split(',');
				int skuIndex = -1;
				int pzIndex = -1;
				int codeIndex = -1;
				int pNumberIndex = -1;      // P号码 - 列名可能是 "背卡P号(正面)" 或 "P号码"
				int barcodeIndex = -1;      // 条形码

				for (int i = 0; i < headers.Length; i++)
				{
					string header = headers[i].Trim();
					Logger.Debug($"表头列{i}: '{header}'");

					if (header == "SKU" || header == "SKU号" || header.Contains("SKU"))
						skuIndex = i;
					else if (header.Contains("几P") || header.Contains("PZ") || header == "几P几Z几mm")
						pzIndex = i;
					else if (header == "打码格式" || header.Contains("打码"))
						codeIndex = i;
					else if (header == "P号码" || header.Contains("背卡P号") || header.Contains("P号"))
						pNumberIndex = i;
					else if (header == "条形码" || header.Contains("条码") || header == "背卡条码")
						barcodeIndex = i;
				}

				Logger.Info($"列索引: SKU={skuIndex}, PZ={pzIndex}, 打码格式={codeIndex}, P号码={pNumberIndex}, 条形码={barcodeIndex}");

				for (int i = 1; i < lines.Length; i++)
				{
					if (string.IsNullOrWhiteSpace(lines[i])) continue;

					var values = lines[i].Split(',');
					var sku = new SkuData();

					// 读取SKU号
					if (skuIndex >= 0 && skuIndex < values.Length)
						sku.SkuNumber = values[skuIndex].Trim();

					// 读取PZ信息并解析P、Z、MM
					if (pzIndex >= 0 && pzIndex < values.Length)
					{
						sku.PZInfo = values[pzIndex].Trim();
						ParsePZInfo(sku);
					}

					// 读取打码格式
					if (codeIndex >= 0 && codeIndex < values.Length)
						sku.CodingFormat = values[codeIndex].Trim();

					// 读取P号码 (背卡P号(正面))
					if (pNumberIndex >= 0 && pNumberIndex < values.Length)
						sku.FrontPCode = values[pNumberIndex].Trim();

					// 读取条形码 (背卡条码)
					if (barcodeIndex >= 0 && barcodeIndex < values.Length)
						sku.BackBarcode = values[barcodeIndex].Trim();

					if (!string.IsNullOrEmpty(sku.SkuNumber))
					{
						_skuList.Add(sku);

						// 打印前几个SKU的数据用于调试
						if (_skuList.Count <= 5)
						{
							Logger.Debug($"SKU {sku.SkuNumber}: P={sku.P}, Z={sku.Z}, MM={sku.MM}, P号码={sku.FrontPCode}, 条形码={sku.BackBarcode}");
						}
					}
				}
			}

			Logger.Info($"从CSV加载了 {_skuList.Count} 个SKU");
			OnDataChanged?.Invoke();
			return true;
		}

		private void ParsePZInfo(SkuData sku)
		{
			if (string.IsNullOrEmpty(sku.PZInfo)) return;

			try
			{
				// 格式如 "8P2Z42mm" 或 "8P2Z42"
				string pz = sku.PZInfo;
				int pIndex = pz.IndexOf('P');
				int zIndex = pz.IndexOf('Z');

				if (pIndex > 0)
				{
					string pStr = pz.Substring(0, pIndex);
					if (int.TryParse(pStr, out int p))
						sku.P = p;
				}

				if (zIndex > pIndex)
				{
					string zStr = pz.Substring(pIndex + 1, zIndex - pIndex - 1);
					if (int.TryParse(zStr, out int z))
						sku.Z = z;
				}

				// 解析MM - 从Z后面到结束或到'mm'
				int startPos = zIndex + 1;
				int endPos = pz.Length;
				if (pz.IndexOf('m') > 0)
					endPos = pz.IndexOf('m');

				string mmStr = pz.Substring(startPos, endPos - startPos);
				if (int.TryParse(mmStr, out int mm))
					sku.MM = mm;
			}
			catch (Exception ex)
			{
				Logger.Error($"解析PZ信息失败: {sku.PZInfo}, {ex.Message}");
			}
		}

		private bool LoadFromSqlServer()
		{
			Logger.Info("SQL Server数据加载接口预留");
			return false;
		}

		private Dictionary<string, List<SkuData>> _searchCache = new Dictionary<string, List<SkuData>>();

		public List<SkuData> Search(string keyword)
		{
			lock (_lock)
			{
				if (_skuList.Count == 0)
				{
					return new List<SkuData>();
				}

				if (string.IsNullOrWhiteSpace(keyword))
				{
					return _skuList.Take(50).ToList();
				}

				string cacheKey = keyword.ToLower();
				if (_searchCache.ContainsKey(cacheKey))
				{
					return _searchCache[cacheKey];
				}

				var results = _skuList
					.Where(s => s.SkuNumber != null &&
						   s.SkuNumber.IndexOf(keyword, StringComparison.OrdinalIgnoreCase) >= 0)
					.Take(30)
					.ToList();

				if (_searchCache.Count > 100)
				{
					_searchCache.Clear();
				}
				_searchCache[cacheKey] = results;

				return results;
			}
		}

		public SkuData GetBySkuNumber(string skuNumber)
		{
			lock (_lock)
			{
				return _skuList.FirstOrDefault(s => s.SkuNumber == skuNumber);
			}
		}

		public void Refresh() => LoadData();
	}
}