using System.Drawing;
using System.Windows.Forms;

namespace Xilium.CefGlue.Demo.WinForms
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            menuStrip1 = new MenuStrip();
            fileToolStripMenuItem = new ToolStripMenuItem();
            showDevToolsToolStripMenuItem = new ToolStripMenuItem();
            showInfoToolStripMenuItem = new ToolStripMenuItem();
            panelBrowser = new Panel();
            menuStrip1.SuspendLayout();
            SuspendLayout();
            menuStrip1.Items.AddRange(new ToolStripItem[] { fileToolStripMenuItem });
            menuStrip1.Location = new Point(0, 0);
            menuStrip1.Name = "menuStrip1";
            menuStrip1.Size = new Size(800, 24);
            menuStrip1.TabIndex = 0;
            menuStrip1.Text = "menuStrip1";
            fileToolStripMenuItem.DropDownItems.AddRange(new ToolStripItem[] { showDevToolsToolStripMenuItem, showInfoToolStripMenuItem });
            fileToolStripMenuItem.Name = "fileToolStripMenuItem";
            fileToolStripMenuItem.Size = new Size(37, 20);
            fileToolStripMenuItem.Text = "&File";
            showDevToolsToolStripMenuItem.Name = "showDevToolsToolStripMenuItem";
            showDevToolsToolStripMenuItem.Size = new Size(154, 22);
            showDevToolsToolStripMenuItem.Text = "Show Dev&Tools";
            showDevToolsToolStripMenuItem.Click += showDevToolsToolStripMenuItem_Click;
            showInfoToolStripMenuItem.CheckOnClick = true;
            showInfoToolStripMenuItem.Checked = false;
            showInfoToolStripMenuItem.CheckState = CheckState.Unchecked;
            showInfoToolStripMenuItem.Name = "showInfoToolStripMenuItem";
            showInfoToolStripMenuItem.Size = new Size(154, 22);
            showInfoToolStripMenuItem.Text = "Show &Info";
            showInfoToolStripMenuItem.Click += showInfoToolStripMenuItem_Click;
            panelBrowser.Dock = DockStyle.Fill;
            panelBrowser.Location = new Point(0, 24);
            panelBrowser.Margin = new Padding(0);
            panelBrowser.Name = "panelBrowser";
            panelBrowser.Size = new Size(800, 426);
            panelBrowser.TabIndex = 1;
            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 450);
            Controls.Add(panelBrowser);
            Controls.Add(menuStrip1);
            MainMenuStrip = menuStrip1;
            Name = "MainForm";
            Text = "Cefglue WinForms Demo";
            menuStrip1.ResumeLayout(false);
            menuStrip1.PerformLayout();
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private MenuStrip menuStrip1;
        private ToolStripMenuItem fileToolStripMenuItem;
        private ToolStripMenuItem showDevToolsToolStripMenuItem;
        private ToolStripMenuItem showInfoToolStripMenuItem;
        private Panel panelBrowser;
    }
}
