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
using static CommonLib.Class_Config;

namespace SetProduct
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

		public Vision vision;
		public delegate void ChangeModel(string Model);
		public event ChangeModel EventChangeMode;
		//public UserClass user;
		private void MainFrm_Load(object sender, EventArgs e)
		{
			dataGridView1.AutoGenerateColumns = false;
			LoadData();

			//if (int.Parse(user.Grade) == 2)
			//{
			//	this.addBtn.Enabled = false;
			//	this.editBtn.Enabled = false;
			//	this.delBtn.Enabled = false;
			//}
		}


		/// <summary>
		/// 切换产品
		/// </summary>
		/// <param name="sender"></param>
		/// <param name="e"></param>
		private void uiButton3_Click(object sender, EventArgs e)
		{
			if (MessageBox.Show($"确定将型号切换为 “{dataGridView1.CurrentRow.Cells[2].Value}？ 切换后拍照轴将自动运行至对应拍照位。” ", "系统提示", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No)
				return;
			bool state = vision.ChangeCheckProduct(dataGridView1.CurrentRow.Cells[0].Value.ToString(), dataGridView1.CurrentRow.Cells[2].Value.ToString());
			if (state)
			{
				EventChangeMode(dataGridView1.CurrentRow.Cells[2].Value.ToString());
				//MessageBox.Show("切换型号完成");
				this.Close();
			}
			else
			{
				MessageBox.Show("切换型号失败");
			}
		}

		private void addBtn_Click(object sender, EventArgs e)
		{
			EditPage editPage = new EditPage();
			editPage.modelVal = ModelEnum.Model.New;
			editPage.ShowDialog();
			LoadData();
		}

		private void editBtn_Click(object sender, EventArgs e)
		{
			if (dataGridView1.CurrentCell == null)
				return;
			EditPage editPage = new EditPage();
			editPage.modelVal = ModelEnum.Model.Rev;
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

			if (dataGridView1.CurrentRow.Cells[2].Value.ToString() == _Config.CurCheckSpec)
			{
				MessageBox.Show("当前型号正在检测，不能删除！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Exclamation);
				return;
			}

			if (MessageBox.Show("删除记录后将删除相关算法文件以及历史检测记录，确定删除选择记录吗?", "系统提示", MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.No)
				return;


			string sql = "";
			SQLiteParameter[] vparams = {
					new SQLiteParameter("@id",dataGridView1.CurrentRow.Cells[0].Value.ToString()),
			};

			sql = "delete from product_info WHERE ID=@id";
			SQLiteHelper.ExecuteNonQuery(sql, vparams);
			vision.DelVpp(dataGridView1.CurrentRow.Cells[2].Value + "", 3);

			dataGridView1.Rows.Remove(dataGridView1.CurrentRow);
		}

		private void searchBtn_Click(object sender, EventArgs e)
		{
			string sql = "select * from product_info where ProductID like  @val or ProductName like  @val or ProductSpec like  @val";
			SQLiteParameter[] paramId = {
				new SQLiteParameter("@val",$"%{searchTxt.Text}%"),
			};

			DataTable dt = SQLiteHelper.ExecuteQuery(sql, paramId);
			dataGridView1.DataSource = dt;
		}

		public void LoadData()
		{
			dataGridView1.DataSource = SQLiteHelper.GetAllList("product_info");
		}

		private void searchTxt_TextChanged(object sender, EventArgs e)
		{
			if (searchTxt.Text.Length == 0)
			{
				LoadData();
			}
		}
	}
}
