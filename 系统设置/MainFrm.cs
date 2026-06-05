using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using static CommonLib.Class_Config;
using static System.Windows.Forms.VisualStyles.VisualStyleElement;

namespace SetSystem
{
	public partial class MainFrm : Form
	{
		public MainFrm()
		{
			InitializeComponent();
		}

		private void button1_Click(object sender, EventArgs e)
		{
			textBox1.Text = OpenDir();
		}
		private string OpenDir()
		{
			string folderPath = string.Empty;
			FolderBrowserDialog folder = new FolderBrowserDialog();
			folder.Description = "选择目录";
			folder.ShowNewFolderButton = false;
			if (folder.ShowDialog() == DialogResult.OK)
			{
				//文件夹路径
				folderPath = folder.SelectedPath;
			}
			return folderPath;
		}

		private void MainFrm_Load(object sender, EventArgs e)
		{
			textBox1.Text = _Config.ImagePath;
			textBox2.Text = _Config.ImageDays.ToString();
			checkBox1.Checked = _Config.IsSaveOkImage;
			checkBox2.Checked = _Config.IsSaveNgImage;
			checkBox4.Checked = _Config.IsSaveOkRawImage;
			checkBox3.Checked = _Config.IsSaveNgRawImage;
		}

		private void button2_Click(object sender, EventArgs e)
		{

			if (textBox1.Text== ""|| textBox1.Text ==string.Empty)
			{
				MessageBox.Show("存图路径为空！");
				return;
			}

			_Config.ImagePath = textBox1.Text;
			_Config.ImageDays = Convert.ToInt16(textBox2.Text);
			_Config.IsSaveOkImage = checkBox1.Checked;
			_Config.IsSaveNgImage = checkBox2.Checked;
			_Config.IsSaveOkRawImage = checkBox4.Checked;
			_Config.IsSaveNgRawImage = checkBox3.Checked;
			
			MessageBox.Show("参数设置保存完成！", "系统提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
			this.Close();
		}
	}
}
