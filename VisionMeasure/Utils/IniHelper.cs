using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace Config
{
	/// <summary>
	/// INI配置文件辅助类
	/// </summary>
	public static class IniHelper
	{
		[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
		private static extern int GetPrivateProfileString(string lpAppName, string lpKeyName, string lpDefault,
			StringBuilder lpReturnedString, int nSize, string lpFileName);

		[DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
		private static extern int WritePrivateProfileString(string lpAppName, string lpKeyName, string lpString,
			string lpFileName);

		/// <summary>
		/// 读取INI文件中的字符串值
		/// </summary>
		public static string GetPrivateProfileString(string section, string key, string defaultValue, string iniPath)
		{
			if (!File.Exists(iniPath))
				return defaultValue;

			var sb = new StringBuilder(1024);
			GetPrivateProfileString(section, key, defaultValue, sb, 1024, iniPath);
			return sb.ToString();
		}

		/// <summary>
		/// 读取INI文件中的整数值
		/// </summary>
		public static int GetPrivateProfileInt(string section, string key, int defaultValue, string iniPath)
		{
			string value = GetPrivateProfileString(section, key, defaultValue.ToString(), iniPath);
			return int.TryParse(value, out int result) ? result : defaultValue;
		}

		/// <summary>
		/// 读取INI文件中的浮点数值
		/// </summary>
		public static double GetPrivateProfileDouble(string section, string key, double defaultValue, string iniPath)
		{
			string value = GetPrivateProfileString(section, key, defaultValue.ToString(), iniPath);
			return double.TryParse(value, out double result) ? result : defaultValue;
		}

		/// <summary>
		/// 读取INI文件中的布尔值
		/// </summary>
		public static bool GetPrivateProfileBool(string section, string key, bool defaultValue, string iniPath)
		{
			string value = GetPrivateProfileString(section, key, defaultValue.ToString(), iniPath);
			return bool.TryParse(value, out bool result) ? result : defaultValue;
		}

		/// <summary>
		/// 写入INI文件
		/// </summary>
		public static void INIWriteValue(string iniPath, string section, string key, string value)
		{
			string dir = Path.GetDirectoryName(iniPath);
			if (!Directory.Exists(dir))
				Directory.CreateDirectory(dir);

			WritePrivateProfileString(section, key, value, iniPath);
		}

		/// <summary>
		/// 删除INI文件中的节
		/// </summary>
		public static void INIDeleteSection(string iniPath, string section)
		{
			WritePrivateProfileString(section, null, null, iniPath);
		}

		/// <summary>
		/// 删除INI文件中的键
		/// </summary>
		public static void INIDeleteKey(string iniPath, string section, string key)
		{
			WritePrivateProfileString(section, key, null, iniPath);
		}
	}
}