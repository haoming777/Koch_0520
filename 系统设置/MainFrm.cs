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


			textBox3.Text = _Config.Astrict.ToString();
			//textBox4.Text = _Config.ModbusPort.ToString();
			textBox5.Text = _Config.Offset.ToString();
			textBox6.Text = _Config.K.ToString();
			textBox4.Text = _Config.totalArea_Camera1.ToString();   // 异物面积
			//textBox10.Text = _Config.Camera3LWRatio.ToString();  // 长宽比
			downTxt.Text = _Config.Camera3RoundnessDown.ToString();
			upTxt.Text = _Config.Camera3RoundnessUp.ToString();
			textBox8.Text = _Config.Camera3Thresh.ToString();   // 阈值
			textBox9.Text = _Config.Camera3Maxval.ToString();   // 最大值
			textBox10.Text = _Config.K_Cam3.ToString();
			textBox7.Text = _Config.Camera2StandChar.ToString();    // 反面数量
			textBox11.Text = _Config.Camera1StandChar.ToString();   // 正面数量
			pCodeTxt.Text = _Config.Standard_PCode .ToString() ;
			pipeDiameterTxt.Text = _Config.Camera3PipeDiameter.ToString();
			

			BaoGuanSwitch.Active = _Config.Camera5IFBaoGuan;
			SeBiaoSwitch.Active = _Config.Camera5IFSeBiao;
			WeiJianDuanSwitch.Active = _Config.Camera5IFWeiJianDuan;
			OcrSwitch.Active = _Config.Camera5IFOcr;
			XieKouSwitch.Active = _Config.Camera5IFXieKou;
			PCodeSwitch.Active = _Config.Camera5IFPCode;


		}

		private void button2_Click(object sender, EventArgs e)
		{
			if (!double.TryParse(textBox6.Text, out double k))
			{
				MessageBox.Show("格式错误！");
				return;
			}

			if (!double.TryParse(textBox5.Text, out double offset))
			{
				MessageBox.Show("格式错误！");
				return;
			}
			if (textBox1.Text== ""|| textBox1.Text ==string.Empty)
			{
				MessageBox.Show("存图路径为空！");
				return;
			}
			_Config.K = k;
			_Config.Offset = offset;
			_Config.ImagePath = textBox1.Text;
			_Config.ImageDays = Convert.ToInt16(textBox2.Text);
			_Config.IsSaveOkImage = checkBox1.Checked;
			_Config.IsSaveNgImage = checkBox2.Checked;
			_Config.IsSaveOkRawImage = checkBox4.Checked;
			_Config.IsSaveNgRawImage = checkBox3.Checked;
			_Config.Astrict = Convert.ToDouble(textBox3.Text);


			_Config.totalArea_Camera1 = Convert.ToInt32(textBox4.Text);
			//_Config.Camera3LWRatio = Convert.ToDouble(textBox10.Text);
			_Config.Camera3RoundnessUp = Convert.ToDouble(upTxt.Text);
			_Config.Camera3RoundnessDown = Convert.ToDouble(downTxt.Text);
			_Config.Camera3Thresh = Convert.ToDouble(textBox8.Text);
			_Config.Camera3Maxval = Convert.ToDouble(textBox9.Text);
			_Config.Camera2StandChar = Convert.ToInt32(textBox7.Text);
			_Config.Camera1StandChar = Convert.ToInt32(textBox11.Text);
			_Config.Camera3PipeDiameter = Convert.ToDouble(pipeDiameterTxt.Text);
			_Config.Standard_PCode = pCodeTxt.Text;
			//_Config.ModbusPort = Convert.ToInt16(textBox4.Text);

			//textBox10.Text = _Config.K_Cam3.ToString();
			_Config.K_Cam3 = Convert.ToDouble(textBox10.Text);


			_Config.Camera5IFBaoGuan = BaoGuanSwitch.Active;
			_Config.Camera5IFSeBiao = SeBiaoSwitch.Active;
			_Config.Camera5IFWeiJianDuan = WeiJianDuanSwitch.Active;
			_Config.Camera5IFOcr = OcrSwitch.Active;
			_Config.Camera5IFXieKou = XieKouSwitch.Active;
			_Config.Camera5IFPCode = PCodeSwitch.Active;
			MessageBox.Show("参数设置保存完成！", "系统提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
			this.Close();
		}
	}
}
