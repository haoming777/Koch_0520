namespace SetVision
{
	partial class MainFrm
	{
		/// <summary>
		/// Required designer variable.
		/// </summary>
		private System.ComponentModel.IContainer components = null;

		/// <summary>
		/// Clean up any resources being used.
		/// </summary>
		/// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
		protected override void Dispose(bool disposing)
		{
			if (disposing && (components != null))
			{
				components.Dispose();
			}
			base.Dispose(disposing);
		}

		#region Windows Form Designer generated code

		/// <summary>
		/// Required method for Designer support - do not modify
		/// the contents of this method with the code editor.
		/// </summary>
		private void InitializeComponent()
		{
			this.panel1 = new System.Windows.Forms.Panel();
			this.cogToolBlockEditV21 = new Cognex.VisionPro.ToolBlock.CogToolBlockEditV2();
			this.uiLabel1 = new Sunny.UI.UILabel();
			this.uiComboBox1 = new Sunny.UI.UIComboBox();
			this.saveBtn = new Sunny.UI.UIButton();
			this.panel1.SuspendLayout();
			((System.ComponentModel.ISupportInitialize)(this.cogToolBlockEditV21)).BeginInit();
			this.SuspendLayout();
			// 
			// panel1
			// 
			this.panel1.Controls.Add(this.saveBtn);
			this.panel1.Controls.Add(this.uiComboBox1);
			this.panel1.Controls.Add(this.uiLabel1);
			this.panel1.Dock = System.Windows.Forms.DockStyle.Top;
			this.panel1.Location = new System.Drawing.Point(0, 0);
			this.panel1.Margin = new System.Windows.Forms.Padding(5);
			this.panel1.Name = "panel1";
			this.panel1.Padding = new System.Windows.Forms.Padding(5);
			this.panel1.Size = new System.Drawing.Size(1101, 43);
			this.panel1.TabIndex = 0;
			// 
			// cogToolBlockEditV21
			// 
			this.cogToolBlockEditV21.AllowDrop = true;
			this.cogToolBlockEditV21.ContextMenuCustomizer = null;
			this.cogToolBlockEditV21.Dock = System.Windows.Forms.DockStyle.Fill;
			this.cogToolBlockEditV21.Location = new System.Drawing.Point(0, 43);
			this.cogToolBlockEditV21.Margin = new System.Windows.Forms.Padding(5);
			this.cogToolBlockEditV21.MinimumSize = new System.Drawing.Size(815, 0);
			this.cogToolBlockEditV21.Name = "cogToolBlockEditV21";
			this.cogToolBlockEditV21.ShowNodeToolTips = true;
			this.cogToolBlockEditV21.Size = new System.Drawing.Size(1101, 587);
			this.cogToolBlockEditV21.SuspendElectricRuns = false;
			this.cogToolBlockEditV21.TabIndex = 1;
			// 
			// uiLabel1
			// 
			this.uiLabel1.Dock = System.Windows.Forms.DockStyle.Left;
			this.uiLabel1.Font = new System.Drawing.Font("微软雅黑", 12F);
			this.uiLabel1.ForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(48)))), ((int)(((byte)(48)))), ((int)(((byte)(48)))));
			this.uiLabel1.Location = new System.Drawing.Point(5, 5);
			this.uiLabel1.Name = "uiLabel1";
			this.uiLabel1.Size = new System.Drawing.Size(83, 33);
			this.uiLabel1.TabIndex = 0;
			this.uiLabel1.Text = "算法：";
			this.uiLabel1.TextAlign = System.Drawing.ContentAlignment.MiddleCenter;
			// 
			// uiComboBox1
			// 
			this.uiComboBox1.DataSource = null;
			this.uiComboBox1.Dock = System.Windows.Forms.DockStyle.Left;
			this.uiComboBox1.FillColor = System.Drawing.Color.White;
			this.uiComboBox1.Font = new System.Drawing.Font("微软雅黑", 12F);
			this.uiComboBox1.ItemHoverColor = System.Drawing.Color.FromArgb(((int)(((byte)(155)))), ((int)(((byte)(200)))), ((int)(((byte)(255)))));
			this.uiComboBox1.Items.AddRange(new object[] {
            "相机一"});
			this.uiComboBox1.ItemSelectForeColor = System.Drawing.Color.FromArgb(((int)(((byte)(235)))), ((int)(((byte)(243)))), ((int)(((byte)(255)))));
			this.uiComboBox1.Location = new System.Drawing.Point(88, 5);
			this.uiComboBox1.Margin = new System.Windows.Forms.Padding(4, 5, 4, 5);
			this.uiComboBox1.MinimumSize = new System.Drawing.Size(63, 0);
			this.uiComboBox1.Name = "uiComboBox1";
			this.uiComboBox1.Padding = new System.Windows.Forms.Padding(0, 0, 30, 2);
			this.uiComboBox1.Size = new System.Drawing.Size(150, 33);
			this.uiComboBox1.SymbolSize = 24;
			this.uiComboBox1.TabIndex = 1;
			this.uiComboBox1.TextAlignment = System.Drawing.ContentAlignment.MiddleLeft;
			this.uiComboBox1.Watermark = "";
			this.uiComboBox1.SelectedIndexChanged += new System.EventHandler(this.uiComboBox1_SelectedIndexChanged);
			// 
			// saveBtn
			// 
			this.saveBtn.Cursor = System.Windows.Forms.Cursors.Hand;
			this.saveBtn.Dock = System.Windows.Forms.DockStyle.Right;
			this.saveBtn.Font = new System.Drawing.Font("微软雅黑", 12F);
			this.saveBtn.Location = new System.Drawing.Point(1013, 5);
			this.saveBtn.MinimumSize = new System.Drawing.Size(1, 1);
			this.saveBtn.Name = "saveBtn";
			this.saveBtn.Size = new System.Drawing.Size(83, 33);
			this.saveBtn.TabIndex = 2;
			this.saveBtn.Text = "保 存";
			this.saveBtn.TipsFont = new System.Drawing.Font("宋体", 9F, System.Drawing.FontStyle.Regular, System.Drawing.GraphicsUnit.Point, ((byte)(134)));
			this.saveBtn.Click += new System.EventHandler(this.saveBtn_Click);
			// 
			// MainFrm
			// 
			this.AutoScaleDimensions = new System.Drawing.SizeF(10F, 21F);
			this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
			this.BackColor = System.Drawing.Color.White;
			this.ClientSize = new System.Drawing.Size(1101, 630);
			this.Controls.Add(this.cogToolBlockEditV21);
			this.Controls.Add(this.panel1);
			this.Font = new System.Drawing.Font("微软雅黑", 12F);
			this.Margin = new System.Windows.Forms.Padding(5);
			this.Name = "MainFrm";
			this.Text = "MainFrm";
			this.Load += new System.EventHandler(this.MainFrm_Load);
			this.panel1.ResumeLayout(false);
			((System.ComponentModel.ISupportInitialize)(this.cogToolBlockEditV21)).EndInit();
			this.ResumeLayout(false);

		}

		#endregion

		private System.Windows.Forms.Panel panel1;
		private Cognex.VisionPro.ToolBlock.CogToolBlockEditV2 cogToolBlockEditV21;
		private Sunny.UI.UIButton saveBtn;
		private Sunny.UI.UIComboBox uiComboBox1;
		private Sunny.UI.UILabel uiLabel1;
	}
}