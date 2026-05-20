using CommonLib;
using Sunny.UI;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Xml.Linq;
using System.Data.SQLite;
using static SetProduct.ModelEnum;

namespace SetProduct
{
	public partial class EditPage : Form
	{
		public EditPage()
		{
			InitializeComponent();
		}
		string userChartName = "product_info";
		SQLiteHelper SQLiteHelper = new SQLiteHelper();

		public Model modelVal;
		public DataGridViewRow row;

		Vision vision = new Vision();
		private void EditPage_Load(object sender, EventArgs e)
		{
			switch (modelVal)
			{
				case Model.New:
					this.Text = "添加型号";

					break;
				case Model.Rev:
					this.Text = "修改内容";
					bianHao.Text = row.Cells[0].Value.ToString();
					mingCheng.Text = row.Cells[1].Value.ToString();
					xingHao.Text = row.Cells[2].Value.ToString();

					bianHao.Enabled = false;
					xingHao.Enabled = false;
					break;
				default:
					break;
			}
		}
		private void saveBtn_Click(object sender, EventArgs e)
		{
			if (!VerifyMethod()) return;

			string sql = "";
			SQLiteParameter[] vparams = {
					new SQLiteParameter("@id",bianHao.Text.ToString()),
					new SQLiteParameter("@name",mingCheng.Text.ToString()),
					new SQLiteParameter("@spec",xingHao.Text.ToString()),
			};

			switch (modelVal)
			{
				case Model.New:
					sql = "select * from product_info where ProductID=@id";
					SQLiteParameter[] paramId = {
					new SQLiteParameter("@id",bianHao.Text.ToString())
					};

					DataTable dt = SQLiteHelper.ExecuteQuery(sql, paramId);
					if (dt.Rows.Count > 0)
					{
						MessageBox.Show("产品编号已存在！", "系统提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
						return;
					}

					sql = "select * from product_info where ProductSpec=@spec";
					SQLiteParameter[] paramSpec = {
					new SQLiteParameter("@spec",xingHao.Text.ToString())
					};

					dt = SQLiteHelper.ExecuteQuery(sql, paramSpec);
					if (dt.Rows.Count > 0)
					{
						MessageBox.Show("规格型号已存在！", "系统提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
						return;
					}

					sql = "insert into product_info(ProductID,ProductName,ProductSpec) values(@id,@name,@spec)";
					SQLiteHelper.ExecuteNonQuery(sql, vparams);
					vision.CopyVpp(xingHao.Text.ToString(), 1);//我这里只有一个相机就复制一次就行了
					MessageBox.Show("新增成功");

					break;
				case Model.Rev:
					sql = "update product_info SET ProductName = @name, ProductSpec = @spec  WHERE ProductID = @id;";
					SQLiteHelper.ExecuteNonQuery(sql, vparams);
					MessageBox.Show("修改成功");
					break;
				default:
					break;
			}
			this.Close();
		}

		private void offBtn_Click(object sender, EventArgs e)
		{
			this.Close();
		}

		private bool VerifyMethod()
		{
			if (mingCheng.Text.Length > 0 && xingHao.Text.Length > 0 && bianHao.Text.Length > 0)
			{
				return true;
			}
			else
			{
				MessageBox.Show("请补全所需内容");
				return false;
			}
		}
	}
}
