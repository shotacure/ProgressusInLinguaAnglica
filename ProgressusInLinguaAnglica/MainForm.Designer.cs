using System.Windows.Forms;

namespace ProgressusInLinguaAnglica
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null!;
        private MenuStrip menuStrip1;
        private ToolStripMenuItem menuFile;
        private ToolStripMenuItem menuFileOpenFolder;
        private ToolStripMenuItem menuFileExit;
        private StatusStrip statusStrip1;
        private ToolStripStatusLabel statusLabel;
        private TextBox txtRootPath;
        private Button btnBrowseFolder;
        private ListBox lstChapters;
        private Button btnPlaySelected;
        private Label lblRoot;
        private Label lblChapters;
        private System.Windows.Forms.Timer tmrPlayBack;
        private ContextMenuStrip cmnSegment;
        private ToolStripMenuItem menuSave;

        protected override void Dispose(bool disposing)
        {
            if (disposing && components is not null)
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            menuStrip1 = new MenuStrip();
            menuFile = new ToolStripMenuItem();
            menuFileOpenFolder = new ToolStripMenuItem();
            menuFileExit = new ToolStripMenuItem();
            statusStrip1 = new StatusStrip();
            statusLabel = new ToolStripStatusLabel();
            txtRootPath = new TextBox();
            btnBrowseFolder = new Button();
            lstChapters = new ListBox();
            btnPlaySelected = new Button();
            lblRoot = new Label();
            lblChapters = new Label();
            tmrPlayBack = new System.Windows.Forms.Timer();
            cmnSegment = new ContextMenuStrip();
            menuSave = new ToolStripMenuItem();
            menuStrip1.SuspendLayout();
            statusStrip1.SuspendLayout();
            SuspendLayout();
            // 
            // menuStrip1
            // 
            menuStrip1.ImageScalingSize = new Size(20, 20);
            menuStrip1.Items.AddRange(new ToolStripItem[] { menuFile });
            menuStrip1.Location = new Point(0, 0);
            menuStrip1.Name = "menuStrip1";
            menuStrip1.Padding = new Padding(7, 3, 0, 3);
            menuStrip1.Size = new Size(914, 30);
            menuStrip1.TabIndex = 0;
            menuStrip1.Text = "menuStrip1";
            // 
            // menuFile
            // 
            menuFile.DropDownItems.AddRange(new ToolStripItem[] { menuFileOpenFolder, menuFileExit });
            menuFile.Name = "menuFile";
            menuFile.Size = new Size(63, 24);
            menuFile.Text = "&File(&F)";
            // 
            // menuFileOpenFolder
            // 
            menuFileOpenFolder.Name = "menuFileOpenFolder";
            menuFileOpenFolder.Size = new Size(201, 26);
            menuFileOpenFolder.Text = "フォルダを開く(&O)...";
            menuFileOpenFolder.Click += menuFileOpenFolder_Click;
            // 
            // menuFileExit
            // 
            menuFileExit.Name = "menuFileExit";
            menuFileExit.Size = new Size(201, 26);
            menuFileExit.Text = "終了(&X)";
            menuFileExit.Click += menuFileExit_Click;
            // 
            // statusStrip1
            // 
            statusStrip1.ImageScalingSize = new Size(20, 20);
            statusStrip1.Items.AddRange(new ToolStripItem[] { statusLabel });
            statusStrip1.Location = new Point(0, 574);
            statusStrip1.Name = "statusStrip1";
            statusStrip1.Padding = new Padding(1, 0, 16, 0);
            statusStrip1.Size = new Size(914, 26);
            statusStrip1.TabIndex = 1;
            statusStrip1.Text = "statusStrip1";
            // 
            // statusLabel
            // 
            statusLabel.Name = "statusLabel";
            statusLabel.Size = new Size(50, 20);
            statusLabel.Text = "Ready";
            // 
            // txtRootPath
            // 
            txtRootPath.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtRootPath.Location = new Point(14, 72);
            txtRootPath.Margin = new Padding(3, 4, 3, 4);
            txtRootPath.Name = "txtRootPath";
            txtRootPath.ReadOnly = true;
            txtRootPath.Size = new Size(742, 27);
            txtRootPath.TabIndex = 3;
            // 
            // btnBrowseFolder
            // 
            btnBrowseFolder.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnBrowseFolder.Location = new Point(763, 71);
            btnBrowseFolder.Margin = new Padding(3, 4, 3, 4);
            btnBrowseFolder.Name = "btnBrowseFolder";
            btnBrowseFolder.Size = new Size(137, 33);
            btnBrowseFolder.TabIndex = 4;
            btnBrowseFolder.Text = "参照(&B)...";
            btnBrowseFolder.UseVisualStyleBackColor = true;
            btnBrowseFolder.Click += btnBrowseFolder_Click;
            // 
            // lstChapters
            // 
            lstChapters.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            lstChapters.FormattingEnabled = true;
            lstChapters.IntegralHeight = false;
            lstChapters.Location = new Point(14, 144);
            lstChapters.Margin = new Padding(3, 4, 3, 4);
            lstChapters.Name = "lstChapters";
            lstChapters.Size = new Size(886, 372);
            lstChapters.TabIndex = 6;
            lstChapters.DoubleClick += lstChapters_DoubleClick;
            lstChapters.MouseDown += LstChapters_MouseDown;
            // 
            // btnPlaySelected
            // 
            btnPlaySelected.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnPlaySelected.Location = new Point(763, 525);
            btnPlaySelected.Margin = new Padding(3, 4, 3, 4);
            btnPlaySelected.Name = "btnPlaySelected";
            btnPlaySelected.Size = new Size(137, 36);
            btnPlaySelected.TabIndex = 7;
            btnPlaySelected.Text = "再生(&P)";
            btnPlaySelected.UseVisualStyleBackColor = true;
            btnPlaySelected.Click += btnPlaySelected_Click;
            // 
            // lblRoot
            // 
            lblRoot.AutoSize = true;
            lblRoot.Location = new Point(14, 48);
            lblRoot.Name = "lblRoot";
            lblRoot.Size = new Size(112, 20);
            lblRoot.TabIndex = 2;
            lblRoot.Text = "CD / フォルダパス:";
            // 
            // lblChapters
            // 
            lblChapters.AutoSize = true;
            lblChapters.Location = new Point(14, 120);
            lblChapters.Name = "lblChapters";
            lblChapters.Size = new Size(64, 20);
            lblChapters.TabIndex = 5;
            lblChapters.Text = "チャプター:";
            //
            // playbackTimer
            //
            tmrPlayBack.Interval = 500;
            tmrPlayBack.Tick += PlaybackTimer_Tick;
            //
            // cmnSegment
            //
            cmnSegment.Items.AddRange(new ToolStripMenuItem[] { menuSave });
            //
            // menuSave
            //
            menuSave.Name = "menuSave";
            menuSave.Text = "保存(&S)";
            menuSave.Click += SegmentSaveMenuItem_Click;
            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(8F, 20F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(914, 600);
            Controls.Add(btnPlaySelected);
            Controls.Add(lstChapters);
            Controls.Add(lblChapters);
            Controls.Add(btnBrowseFolder);
            Controls.Add(txtRootPath);
            Controls.Add(lblRoot);
            Controls.Add(statusStrip1);
            Controls.Add(menuStrip1);
            MainMenuStrip = menuStrip1;
            Margin = new Padding(3, 4, 3, 4);
            Name = "MainForm";
            Text = "Progressus in Lingua Anglica";
            menuStrip1.ResumeLayout(false);
            menuStrip1.PerformLayout();
            statusStrip1.ResumeLayout(false);
            statusStrip1.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }
    }
}
