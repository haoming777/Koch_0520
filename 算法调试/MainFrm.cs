using CommonLib;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace SetVision
{
	
	public partial class MainFrm : Form
	{
		public MainFrm()
		{
			InitializeComponent();
		}
		IMainListener listener = null;
		public Vision vision = new Vision();

		private void MainFrm_Load(object sender, EventArgs e)
		{
		}

		private void saveBtn_Click(object sender, EventArgs e)
		{
			if (uiComboBox1.SelectedIndex == -1)
			{
				MessageBox.Show("请选择视觉算法", "系统提示", MessageBoxButtons.OK, MessageBoxIcon.Stop);
				return;
			}

			if (vision.SaveVpp((int)uiComboBox1.SelectedIndex))
			{
				MessageBox.Show("算法保存成功");
			}
			else
			{
				MessageBox.Show("算法保存失败");
			}

		}

		private void uiComboBox1_SelectedIndexChanged(object sender, EventArgs e)
		{
			cogToolBlockEditV21.Subject = vision.GetVpp((int)uiComboBox1.SelectedIndex);

		}
	}
}
