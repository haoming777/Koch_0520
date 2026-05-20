using System;
using System.Drawing;
using System.Windows.Forms;

namespace VisionMeasure.From
{
	public partial class Loading : Form
	{
		private Label _statusLabel;
		private ProgressBar _progressBar;
		private Label _progressLabel;

		public Loading()
		{
			InitializeComponent();
			this.StartPosition = FormStartPosition.CenterScreen;
			this.TopMost = true;
			this.FormBorderStyle = FormBorderStyle.None;
			this.ShowInTaskbar = false;
		}

		private void InitializeComponent()
		{
			this.Size = new Size(450, 200);
			this.BackColor = Color.FromArgb(47, 60, 76);

			var titleLabel = new Label
			{
				Text = "高露洁KOCH机缺陷检测系统",
				Font = new Font("微软雅黑", 16F, FontStyle.Bold),
				ForeColor = Color.White,
				Location = new Point(50, 30),
				AutoSize = true,
				BackColor = Color.Transparent
			};

			_statusLabel = new Label
			{
				Text = "正在初始化...",
				Font = new Font("微软雅黑", 10F),
				ForeColor = Color.FromArgb(200, 200, 200),
				Location = new Point(50, 80),
				AutoSize = true,
				BackColor = Color.Transparent
			};

			_progressBar = new ProgressBar
			{
				Location = new Point(50, 120),
				Size = new Size(350, 10),
				Style = ProgressBarStyle.Continuous,
				Value = 0
			};

			_progressLabel = new Label
			{
				Text = "0%",
				Font = new Font("微软雅黑", 9F),
				ForeColor = Color.FromArgb(180, 180, 180),
				Location = new Point(410, 115),
				AutoSize = true,
				BackColor = Color.Transparent
			};

			this.Controls.Add(titleLabel);
			this.Controls.Add(_statusLabel);
			this.Controls.Add(_progressBar);
			this.Controls.Add(_progressLabel);
		}

		public void UpdateProgress(int percent, string message)
		{
			if (this.IsDisposed) return;

			if (this.InvokeRequired)
			{
				this.Invoke(new Action(() => UpdateProgress(percent, message)));
				return;
			}

			try
			{
				_progressBar.Value = Math.Min(100, Math.Max(0, percent));
				_progressLabel.Text = $"{percent}%";
				_statusLabel.Text = message;
				Application.DoEvents();
			}
			catch { }
		}

		public static Loading ShowLoadingScreen()
		{
			var form = new Loading();
			form.Show();
			Application.DoEvents();
			return form;
		}

		public static void CloseLoadingScreen(Loading form)
		{
			if (form != null && !form.IsDisposed)
			{
				try
				{
					form.Close();
					form.Dispose();
				}
				catch { }
			}
		}
	}
}