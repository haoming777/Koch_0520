using CommonLib;
using Sunny.UI;
using System;
using System.Windows.Forms;
using UI;
using VisionMeasure.Utils;using CommonLib;
using System.Drawing;

namespace VisionMeasure.From
{
	public partial class TabFrm : Form
	{
		private MainFrm _mainFrm;

		public TabFrm(Point point, Form form)
		{
			InitializeComponent();
			this.Top = point.Y + 20;
			this.Left = point.X + 20;
			_mainFrm = form as MainFrm;
		}

		private void switchTabBtn_Click(object sender, EventArgs e)
		{
			var label = sender as UIPanel;
			if (label == null) return;

			try
			{
				switch (label.Text)
				{
					case "用户设置":
						SetUser.MainFrm SetUserMainFrm = new SetUser.MainFrm();
						SetUserMainFrm.ShowDialog();
						break;

					case "相机设置":
						if (_mainFrm == null) return;

						var setCameraForm = new SetCamera.MainFrm(_mainFrm.GetMotionHandle(), _mainFrm.GetModbusClass());
						setCameraForm.cam1 = _mainFrm.GetCamera1();
						setCameraForm.cam2 = _mainFrm.GetCamera2();
						setCameraForm.cam3 = _mainFrm.GetCamera3();
						setCameraForm.cam4 = _mainFrm.GetCamera4();
						setCameraForm.cam5 = _mainFrm.GetCamera5();
						setCameraForm.cam6 = _mainFrm.GetCamera6();
						setCameraForm.cam7 = _mainFrm.GetCamera7();
						setCameraForm.cam8 = _mainFrm.GetCamera8();
						setCameraForm.ShowDialog();
						break;

					case "产品设置":
						SetProduct.MainFrm SetProductMainFrm = new SetProduct.MainFrm();
						SetProductMainFrm.ShowDialog();
						break;

					case "算法调试":
						OpenTestForm();
						break;

					case "系统设置":
						SetSystem.MainFrm SetSystemMainFrm = new SetSystem.MainFrm();
						SetSystemMainFrm.ShowDialog();
						break;

					case "手动调试":
						if (_mainFrm != null && _mainFrm.GetMotionHandle() != IntPtr.Zero)
						{
							var controlFrm = new PLC监控.ControlFrm();
							//var controlFrm = new PLC监控.ControlFrm(_mainFrm.GetMotionHandle());
							controlFrm.ShowDialog();
						}
						break;
				}
			}
			catch (Exception ex)
			{
				Logger.Error($"打开设置窗口失败: {ex.Message}");
				MessageBox.Show($"打开失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
			}
		}

		private void OpenTestForm()
{
    try
    {
        if (_mainFrm != null)
        {
            var motionMgr = _mainFrm.GetMotionControlManager();
            var cameraMgr = _mainFrm.GetCameraManager();
            var aiModels = _mainFrm.GetAiModelManager();
            var testForm = new UI.TestForm(motionMgr, cameraMgr, aiModels);
            testForm.ShowDialog();
        }
    }
    catch (Exception ex)
    {
        Logger.Error($"打开测试工具失败: {ex.Message}");
        MessageBox.Show($"打开测试工具失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
    }
}

		private void closeBtn_Click(object sender, EventArgs e)
		{
			this.Close();
		}

		private void TabFrm_Deactivate(object sender, EventArgs e)
		{
			this.Hide();
		}
	}
}