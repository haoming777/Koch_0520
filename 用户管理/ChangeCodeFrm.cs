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

namespace SetUser
{
	public partial class ChangeCodeFrm : Form
	{
		public ChangeCodeFrm()
		{
			InitializeComponent();
			this.StartPosition = FormStartPosition.CenterParent;
			this.FormBorderStyle = FormBorderStyle.FixedSingle;
			this.MaximizeBox = false;
			this.MinimizeBox = false;
			this.oldTxt.PasswordChar = '*';
			this.newTxt.PasswordChar = '*';
			this.new1Txt.PasswordChar = '*';

			this.oldHintTxt.Hide();
			this.newHintTxt.Hide();
			this.new1HintTxt.Hide();

		}
		public string UserName;

		public string oldPassword;

		SQLiteHelper SQLiteHelper = new SQLiteHelper();

		private void ChangeCodeFrm_Load(object sender, EventArgs e)
		{
			nameTxt.Text = UserName;
			nameTxt.Enabled = false;
		}

		private void offBtn_Click(object sender, EventArgs e)
		{
			this.Close();
		}

		private void saveBtn_Click(object sender, EventArgs e)
		{
			if (oldTxt.Text == string.Empty || new1Txt.Text == string.Empty || newTxt.Text == string.Empty)
			{
				MessageBox.Show("请补全所有内容");
				return;
			}
			if (oldTxt.Text != oldPassword)
			{
				oldHintTxt.Show();
				return;
			}

			if (newTxt.Text == oldPassword)
			{
				newHintTxt.Show();
				return;
			}

			if (new1Txt.Text != newTxt.Text)
			{
				new1HintTxt.Show();
				return;
			}

			string sql = "";
            SQLiteParameter[] vparams = {
					new SQLiteParameter("@psw",SQLiteHelper.MD5Encrypt16(new1Txt.Text.ToString())),
					new SQLiteParameter("@name",UserName.ToString()),
			};

			sql = "update user_info SET PassWord = @psw WHERE UserName = @name;";
			SQLiteHelper.ExecuteNonQuery(sql, vparams);
			MessageBox.Show("修改成功");
			this.Close();
		}

		private void oldTxt_TextChanged(object sender, EventArgs e)
		{
			oldHintTxt.Hide();
		}

		private void newTxt_TextChanged(object sender, EventArgs e)
		{
			newHintTxt.Hide();
		}

		private void new1Txt_TextChanged(object sender, EventArgs e)
		{
			new1HintTxt.Hide();
		}
	}
}
