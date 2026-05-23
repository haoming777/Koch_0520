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
			CsvPath = Path.Combine(baseDir, "主数据.csv");
			Logger.Info($"SKU数据库初始化，CSV路径: {CsvPath}");
		}

		public bool LoadData()
		{
			try
			{
				if (CurrentDataSource == DataSourceType.SqlServer && !string.IsNullOrEmpty(SqlConnectionString))
				{
					return LoadFromSqlServer();
				}
				else
				{
					return LoadFromCsv();
				}
			}
			catch (Exception ex)
			{
				Logger.Error($"加载SKU数据失败: {ex.Message}");
				return false;
			}
		}

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