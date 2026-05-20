using CommonLib;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Data.SQLite;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static SetUser.ModelClass;

namespace SetUser
{
	public partial class EditPage : Form
	{
		public EditPage()
		{
			InitializeComponent();
			this.FormBorderStyle = FormBorderStyle.FixedSingle;
			this.MaximizeBox = false;
			this.MinimizeBox = false;
		}

		public Model modelVal;
		public DataGridViewRow row;
		SQLiteHelper SQLiteHelper = new SQLiteHelper();

		private void EditPage_Load(object sender, EventArgs e)
		{
			switch (modelVal)
			{
				case Model.New:
					this.Text = "添加型号";

					break;
				case Model.Rev:
					this.Text = "修改内容";
					bianHaoTxt.Text = row.Cells[0].Value.ToString();
					mingChengTxt.Text = row.Cells[1].Value.ToString();
					zhiWeiTxt.SelectedIndex = zhiWeiTxt.Items.IndexOf(row.Cells[2].Value.ToString());
					beiZhuTxt.Text = row.Cells[3].Value.ToString();

					bianHaoTxt.Enabled = false;
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
					new SQLiteParameter("@id",bianHaoTxt.Text.ToString()),
					new SQLiteParameter("@name",mingChengTxt.Text.ToString()),
					new SQLiteParameter("@zhiwei",zhiWeiTxt.Text.ToString()),
					new SQLiteParameter("@mima",SQLiteHelper.MD5Encrypt16("000")),
					new SQLiteParameter("@grade",zhiWeiTxt.SelectedIndex),
					new SQLiteParameter("@beizhu",beiZhuTxt.Text.ToString().Length > 0 ? beiZhuTxt.Text.ToString(): "")
			};

			switch (modelVal)
			{
				case Model.New:
					sql = "select * from user_info where UserID=@id";
					SQLiteParameter[] paramId = {
					new SQLiteParameter("@id",bianHaoTxt.Text.ToString())
					};

					DataTable dt = SQLiteHelper.ExecuteQuery(sql, paramId);
					if (dt.Rows.Count > 0)
					{
						MessageBox.Show("用户编号！", "系统提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
						return;
					}

					sql = "select * from user_info where UserName=@name";
					SQLiteParameter[] paramSpec = {
					new SQLiteParameter("@name",mingChengTxt.Text.ToString())
					};

					dt = SQLiteHelper.ExecuteQuery(sql, paramSpec);
					if (dt.Rows.Count > 0)
					{
						MessageBox.Show("用户名已存在！", "系统提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
						return;
					}

					sql = "insert into user_info(UserID,UserName,PassWord,UserGrade,UserRole,Remark) values(@id,@name,@mima,@grade,@zhiwei,@beizhu)";
					SQLiteHelper.ExecuteNonQuery(sql, vparams);
					MessageBox.Show("新增成功");

					break;
				case Model.Rev:
					sql = "update user_info SET UserName = @name, UserGrade = @grade, UserRole = @zhiwei, UserRole = @zhiwei, Remark = @beizhu  WHERE  UserID = @id;";
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
			if (mingChengTxt.Text.Length > 0 && bianHaoTxt.Text.Length > 0 && zhiWeiTxt.SelectedIndex != -1)
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
