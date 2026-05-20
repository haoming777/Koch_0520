using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Data.SQLite;
using CommonLib;



namespace SetUser
{
	public partial class MainFrm : Form
	{
		public MainFrm()
		{
			InitializeComponent();
			this.FormBorderStyle = FormBorderStyle.FixedSingle;
			this.MaximizeBox = false;
			this.MinimizeBox = false;
		}
		SQLiteHelper SQLiteHelper = new SQLiteHelper();
		private void MainFrm_Load(object sender, EventArgs e)
		{
			dataGridView1.AutoGenerateColumns = false;
			LoadData();
		}

		public void LoadData()
		{
			dataGridView1.DataSource = SQLiteHelper.GetAllList("user_info");
		}

		private void addBtn_Click(object sender, EventArgs e)
		{
			EditPage editPage = new EditPage();
			editPage.modelVal = Model.New;
			editPage.ShowDialog();
			LoadData();
		}

		private void editBtn_Click(object sender, EventArgs e)
		{
			if (dataGridView1.CurrentCell == null)
				return;
			EditPage editPage = new EditPage();
			editPage.modelVal = Model.Rev;
			editPage.row = dataGridView1.CurrentRow;
			editPage.ShowDialog();
			LoadData();
		}

		private void delBtn_Click(object sender, EventArgs e)
		{

			if (dataGridView1.SelectedRows.Count == 0)
			{
				MessageBox.Show("请选择一条要删除的项！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
				return;
			}


			if (MessageBox.Show("确定删除选择记录吗?", "系统提示", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No)
				return;


			string sql = "";
			SQLiteParameter[] vparams = {
					new SQLiteParameter("@id",dataGridView1.CurrentRow.Cells[0].Value.ToString()),
			};

			sql = "delete from user_info WHERE UserID=@id";
			SQLiteHelper.ExecuteNonQuery(sql, vparams);

			dataGridView1.Rows.Remove(dataGridView1.CurrentRow);
		}
	}
	
}
