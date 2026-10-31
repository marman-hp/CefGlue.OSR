using Microsoft.VisualBasic.ApplicationServices;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.Eventing.Reader;
using System.Drawing;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.ConstrainedExecution;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Handlers;
using Xilium.CefGlue.Common.Helpers.Logger;
using Xilium.CefGlue.WinForms;

namespace Xilium.CefGlue.Demo.WinForms
{
    public partial class MainForm : Form
    {

        public static MainForm Instance { get; private set; }

        public MainForm()
        {
            InitializeComponent();

            this.Load += MainForm_Load;

            Instance = this;

        }

        private void MainForm_Load(object sender, System.EventArgs e)
        {
            InitBrowser();
        }

        CefGlueBrowser browser;
        private System.Windows.Forms.Timer _cefTimer;

        private void InitBrowser()
        {
            browser = new CefGlueBrowser(setupCefBrowserSettings: () =>
            {
                CefBrowserSettings settings = new CefBrowserSettings();
                settings.WindowlessFrameRate = 60;
                return settings;
            },setupCefWindowInfo: () =>
            {
                CefWindowInfo windowInfo =  CefWindowInfo.Create();
                windowInfo.SharedTextureEnabled = false;
                return windowInfo;
            });

            browser.Control.Dock = DockStyle.Fill;
            panelBrowser.Controls.Add(browser.Control);
            browser.LifeSpanHandler = new BrowserLifeSpanHandler(browser);

            browser.Address = "https://google.com";

            browser.FullscreenModeChanged += (sender, fullscreen) =>
            {
                if (InvokeRequired)
                {
                    BeginInvoke(() => menuStrip1.Visible = !fullscreen);
                }
                else
                {
                    menuStrip1.Visible = !fullscreen;
                }
            };

        }

        private void showDevToolsToolStripMenuItem_Click(object sender, System.EventArgs e)
        {
            browser?.ShowDeveloperTools();
        }

        private void showInfoToolStripMenuItem_Click(object sender, System.EventArgs e)
        {
            if (browser != null)
            {
                browser.ShowOverlayInfo = showInfoToolStripMenuItem.Checked;
            }
        }

        private void button1_Click(object sender, EventArgs e)
        {
            if (browser.CanGoBack)
                browser.GoBack();
        }

        private void button2_Click(object sender, EventArgs e)
        {
            if (browser.CanGoForward)
                browser.GoForward();
        }
    }

    internal class BrowserLifeSpanHandler : LifeSpanHandler
    {

        readonly CefGlueBrowser _owner;
        readonly Action<string> _openInNewTab;

        public BrowserLifeSpanHandler(CefGlueBrowser owner, Action<string> openInNewTab = null)
        {
            _owner = owner;
            _openInNewTab = openInNewTab;
        }

        protected override bool OnBeforePopup(

              CefBrowser browser,
              CefFrame frame,
              int popuId,
              string targetUrl,
              string targetFrameName,
              CefWindowOpenDisposition targetDisposition,
              bool userGesture,
              CefPopupFeatures popupFeatures,
              CefWindowInfo windowInfo,
              ref CefClient client,
              CefBrowserSettings settings,
              ref CefDictionaryValue extraInfo,
              ref bool noJavascriptAccess)
        {

            if (targetDisposition != CefWindowOpenDisposition.NewPopup)
            {
                if (_openInNewTab != null)
                {
                    _openInNewTab(targetUrl);
                }
                else
                {
                    _owner.Address = targetUrl;
                }
                return true;
            }

            var parentForm = _owner.Control.FindForm();
            CefClient outClient = null;
            CefDictionaryValue outExtra = null;
            parentForm.Invoke(() =>
            {
                _owner.CreatePopupBrowser(ref outClient, ref outExtra, windowInfo, popupFeatures, targetDisposition);
            });
            client = outClient;
            extraInfo = outExtra;
            return false;

        }

        private bool IsDevTools(CefBrowser browser)
        {
            if(browser.GetMainFrame().Url != null )
              return browser.GetMainFrame().Url.StartsWith("devtools://");
            else
              return false;
        }
    }

    class NativeContextMenuHandler : ContextMenuHandler
    {
        protected override void OnBeforeContextMenu(CefBrowser browser, CefFrame frame, CefContextMenuParams state, CefMenuModel model)
        {
        }

        protected override bool OnContextMenuCommand(CefBrowser browser, CefFrame frame, CefContextMenuParams state, int commandId, CefEventFlags eventFlags)
        {
            return false;
        }
    }

}
