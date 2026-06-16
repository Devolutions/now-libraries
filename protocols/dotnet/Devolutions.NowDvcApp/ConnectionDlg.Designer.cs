namespace Devolutions.NowDvcApp
{
    partial class ConnectionDlg
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
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
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            label1 = new Label();
            SuspendLayout();
            // 
            // label1
            // 
            label1.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            label1.Location = new Point(12, 9);
            label1.Name = "label1";
            label1.Size = new Size(366, 136);
            label1.TabIndex = 0;
            label1.Text = "Waiting for DVC connection...";
            label1.TextAlign = ContentAlignment.MiddleCenter;
            // 
            // ConnectionDlg
            // 
            AutoScaleDimensions = new SizeF(10F, 25F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(390, 154);
            Controls.Add(label1);
            Cursor = Cursors.WaitCursor;
            MaximizeBox = false;
            MinimizeBox = false;
            Name = "ConnectionDlg";
            SizeGripStyle = SizeGripStyle.Hide;
            Text = "NowProto DVC";
            FormClosing += ConnectionDlg_FormClosing;
            Load += ConnectionDlg_Load;
            ResumeLayout(false);
        }

        #endregion

        private Label label1;
    }
}
