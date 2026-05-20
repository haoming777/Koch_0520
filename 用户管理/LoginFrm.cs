using CommonLib;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using XL.Tool;
using static CommonLib.Class_Config;

namespace SetUser
{
	public partial class LoginFrm : Form
	{
		public LoginFrm()
		{
			InitializeComponent();

		}

		string SQLiteFileName = Application.StartupPath + @"/data/data.db";
		SQLiteHelper SQLiteHelper = new SQLiteHelper();
		XLToolClass tool = new XLToolClass();
		public UserClass user;
		private void LoginFrm_Load(object sender, EventArgs e)
		{
			if (!(this.Text == "用户设置"))
			{
				changeBtn.Hide();
			}
			this.PasswordTxt.PasswordChar = '*';
			DataTable db = SQLiteHelper.GetAllList("user_info");
			foreach (DataRow item in db.Rows)
			{
				comboBox1.Items.Add(item["UserName"]);
			}
		}

		private void LoginFrm_FormClosing(object sender, FormClosingEventArgs e)
		{


		}

		private void offBtn_Click(object sender, EventArgs e)
		{
			this.Close();
		}

		private void loginBtn_Click(object sender, EventArgs e)
		{
			if (comboBox1.SelectedIndex == -1)
			{
				MessageBox.Show("请选择操作员！", "系统提示", MessageBoxButtons.OK, MessageBoxIcon.Stop);
				return;
			}
			try
			{
				if (SQLiteHelper.FindPassWord(comboBox1.SelectedItem.ToString(), PasswordTxt.Text))
				{
					user = SQLiteHelper.FindUserName(comboBox1.SelectedItem.ToString());
					_Config.test = false;
					int grade = int.Parse(user.Grade);
					switch (this.Text)
					{
						case "系统设置":
							_Config.test = true;
							break;
						case "产品设置":
								_Config.test = true;
							break;
						case "用户设置":
							if (grade < 1)
							{
								_Config.test = true;
							}
							break;
						case "相机设置":
							_Config.test = true;
							break;
						case "算法调试":
							if (grade < 1)
							{
								_Config.test = true;
							}
							break;
						case "PLC监控":
							if (grade == 0)
							{
								_Config.test = true;
							}
							break;
						default:
							break;
					}
					this.Close();
				}
				else
				{
					MessageBox.Show("密码错误！！");
				}
			}
			catch (Exception ex)
			{
				tool.SaveLog($"数据库操作错误...\r\n {ex.Message} \r\n {ex.StackTrace}");
			}
		}

		private void button1_Click(object sender, EventArgs e)
		{

		}

		private void changeBtn_Click(object sender, EventArgs e)
		{

			if (comboBox1.SelectedIndex == -1)
			{
				MessageBox.Show("请选择操作员！", "系统提示", MessageBoxButtons.OK, MessageBoxIcon.Stop);
				return;
			}
			try
			{
				if (SQLiteHelper.FindPassWord(comboBox1.SelectedItem.ToString(), PasswordTxt.Text))
				{
					ChangeCodeFrm changeCodeFrm = new ChangeCodeFrm();
					changeCodeFrm.UserName = comboBox1.SelectedItem.ToString();
					changeCodeFrm.oldPassword = PasswordTxt.Text;
					changeCodeFrm.ShowDialog();
				}
				else
				{
					MessageBox.Show("密码错误");
				}
			}
			catch (Exception ex)
			{
				MessageBox.Show(ex.Message);
			}
		}
	}
}
