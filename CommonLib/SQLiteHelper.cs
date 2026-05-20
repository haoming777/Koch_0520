using System;
using System.Collections.Generic;
using System.Data;
using System.Data.SQLite;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Policy;
using System.Text;
using System.Threading.Tasks;
using System.Xml.Linq;
using XL.Tool;
using static System.Net.Mime.MediaTypeNames;

namespace CommonLib
{
	public class SQLiteHelper
	{

		XLToolClass toolClass = new XLToolClass();
		/// <summary>
		/// 数据库地址
		/// </summary>
		private string dataSource = Directory.GetCurrentDirectory() + @"\data\data.db";

		/// <summary>
		/// 用户 表名
		/// </summary>
		private string userChartName = "user_info";
		public DataTable GetAllList(string ChartName)
		{
			var SQLiteConnectionTest = new SQLiteConnection(@"Data Source = " + dataSource);
			var DataTable = new DataTable();
			var adp = new SQLiteDataAdapter("select * from " + ChartName, SQLiteConnectionTest);
			adp.Fill(DataTable);
			return DataTable;
		}

		/// <summary>
		/// 返回查询的数据
		/// </summary>
		/// <param name="SearchContent"></param>
		/// <returns></returns>
		public DataTable GetAllListTest(string SearchContent)
		{
			var SQLiteConnection = new SQLiteConnection(@"Data Source = " + dataSource);
			var DataTable = new DataTable();
			var adp = new SQLiteDataAdapter($"select* from {userChartName} where username = 123 or password = 123 or id=2",
						SQLiteConnection);
			adp.Fill(DataTable);
			return DataTable;
		}

		public bool FindPassWord(string name, string psw)
		{
			var SQLiteConnection = new SQLiteConnection(@"Data Source = " + dataSource);
			var DataTable = new DataTable();
			var adp = new SQLiteDataAdapter($"select * from user_info where UserName = \"{name}\" and PassWord = \"{MD5Decode16(psw)}\"", SQLiteConnection);
			adp.Fill(DataTable);

			return DataTable.Rows.Count > 0;
		}

		public UserClass FindUserName(string name)
		{
			string sql = "select * from user_info where UserName=@name";
			SQLiteParameter[] paramId = {
					new SQLiteParameter("@name",name.ToString())
					};
			DataTable dt = ExecuteQuery(sql, paramId);
			Console.WriteLine(dt.Rows[0].ItemArray[5].ToString());
			var item = dt.Rows[0];
			UserClass user = new UserClass()
			{
				id = item.ItemArray[1].ToString(),
				name = item.ItemArray[2].ToString(),
				Grade = item.ItemArray[4].ToString(),
				Role = item.ItemArray[5].ToString(),
			};
            return user;
		}


		public bool ExecuteNonQuery(string sql, params SQLiteParameter[] parameters)
		{
			using (SQLiteConnection sQLiteConnection = new SQLiteConnection(@"Data Source = " + dataSource))
			{
				using (SQLiteCommand sQLiteCommand = new SQLiteCommand(sQLiteConnection))
				{
					try
					{
						sQLiteConnection.Open();
						sQLiteCommand.CommandText = sql;
						if (parameters.Length != 0)
						{
							sQLiteCommand.Parameters.AddRange(parameters);
						}
						int test = sQLiteCommand.ExecuteNonQuery();
						return true;
					}
					catch (Exception ex)
					{
						toolClass.SaveLog($"数据库操作错误...\r\n {ex.Message} \r\n {ex.StackTrace}");
						return false;
					}
				}
			}
		}

		public DataTable ExecuteQuery(string sql, params SQLiteParameter[] parameters)
		{
			using (SQLiteConnection connection = new SQLiteConnection(@"Data Source = " + dataSource))
			{
				using (SQLiteCommand sQLiteCommand = new SQLiteCommand(sql, connection))
				{

					if (parameters.Length != 0)
					{
						sQLiteCommand.Parameters.AddRange(parameters);
					}

					SQLiteDataAdapter sQLiteDataAdapter = new SQLiteDataAdapter(sQLiteCommand);
					DataTable dataTable = new DataTable();
					try
					{
						sQLiteDataAdapter.Fill(dataTable);
					}
					catch (Exception)
					{
						throw;
					}

					return dataTable;
				}
			}
		}

		private static string MD5Decode16(string value)
		{
			var md5 = new MD5CryptoServiceProvider();
			string t2 = BitConverter.ToString(md5.ComputeHash(Encoding.Default.GetBytes(value)), 4, 8);
			t2 = t2.Replace("-", "");
			return t2;
		}

		public static string MD5Encrypt16(string value)
		{
			return BitConverter.ToString(new MD5CryptoServiceProvider().ComputeHash(Encoding.Default.GetBytes(value)), 4, 8).Replace("-", "");
		}
	}
}
