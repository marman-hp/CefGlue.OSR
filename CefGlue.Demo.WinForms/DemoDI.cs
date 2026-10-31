using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Microsoft.Extensions.DependencyInjection;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Composition;
using Xilium.CefGlue.Common.Coordinators;
using Xilium.CefGlue.Common.Events;
using Xilium.CefGlue.Common.Handlers;
using Xilium.CefGlue.Common.Helpers.Logger;
using Xilium.CefGlue.WinForms;
using Xilium.CefGlue.WinForms.Common;

namespace Xilium.CefGlue.Demo.WinForms
{
    public sealed class DemoDI : Form
    {
        private readonly MenuStrip _menuStrip;
        private readonly ToolStripMenuItem _showOverlayInfoMenuItem;
        private readonly ToolStripMenuItem _showActivityLogMenuItem;
        private readonly TabControl _tabControl;
        private readonly Panel _fullscreenHost;
        private readonly Splitter _activityListSplitter;
        private readonly ListBox _activityList;
        private readonly StatusStrip _statusStrip;
        private readonly ToolStripStatusLabel _statusLabel;
        private readonly TrackBar _zoomTrackBar;
        private readonly ToolStripStatusLabel _zoomValueLabel;
        private CefGlueHost _app;
        private int _lastActivityListHeight = 150;

        public DemoDI()
        {
            Text = "CefGlue WinForms Demo - DI (DemoDI)";
            ClientSize = new Size(1000, 650);

            var newTabItem = new ToolStripMenuItem("New Tab");
            newTabItem.Click += (s, e) => CreateNewTab();

            var closeTabItem = new ToolStripMenuItem("Close Tab");
            closeTabItem.Click += (s, e) => CloseActiveTab();

            var openDevToolsItem = new ToolStripMenuItem("Open Dev Tools...");
            openDevToolsItem.Click += (s, e) =>
            {
                if (ActiveTab == null)
                {
                    return;
                }

                if (ActiveTab.IsDevToolsOpen)
                {
                    ActiveTab.CloseDevTools();
                }
                else
                {
                    ActiveTab.OpenDevTools();
                }
            };

            var fileMenu = new ToolStripMenuItem("File");
            fileMenu.DropDownItems.Add(newTabItem);
            fileMenu.DropDownItems.Add(closeTabItem);
            fileMenu.DropDownItems.Add(openDevToolsItem);

            _showOverlayInfoMenuItem = new ToolStripMenuItem("Show Overlay Info")
            {
                CheckOnClick = true,
                Checked = false,
            };
            _showOverlayInfoMenuItem.Click += OnShowOverlayInfoMenuItemClick;

            _showActivityLogMenuItem = new ToolStripMenuItem("Show Activity Log")
            {
                CheckOnClick = true,
                Checked = false,
            };
            _showActivityLogMenuItem.Click += OnShowActivityLogMenuItemClick;

            var viewMenu = new ToolStripMenuItem("View");
            viewMenu.DropDownItems.Add(_showOverlayInfoMenuItem);
            viewMenu.DropDownItems.Add(_showActivityLogMenuItem);

            _menuStrip = new MenuStrip();
            _menuStrip.Items.Add(fileMenu);
            _menuStrip.Items.Add(viewMenu);
            MainMenuStrip = _menuStrip;

            _tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
            };
            _tabControl.SelectedIndexChanged += (s, e) =>
            {
                ActiveTab?.FocusBrowserContent();
                _statusLabel.Text = ActiveTab?.LastStatus ?? "";

                ActiveTab?.PerformLayout();
                (ActiveTab?.Browser.Control as BaseCefControl)?.EndResize();
            };

            _fullscreenHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Color.Black,
                Visible = false,
            };

            _activityList = new ListBox
            {
                Dock = DockStyle.Bottom,
                Height = _lastActivityListHeight,
                IntegralHeight = false,
            };

            _activityListSplitter = new Splitter
            {
                Dock = DockStyle.Bottom,
                Height = 4,
            };
            _activityListSplitter.SplitterMoved += (s, e) => SyncAllTabsToCurrentLayout();

            _zoomTrackBar = new TrackBar
            {
                Minimum = -50,
                Maximum = 50,
                Value = 0,
                TickFrequency = 10,
                Width = 220,
                AutoSize = false,
                Height = 28,
                BackColor = SystemColors.Control,
            };
            _zoomTrackBar.ValueChanged += ZoomTrackBar_ValueChanged;

            _zoomValueLabel = new ToolStripStatusLabel("0.0");

            var resetButton = new ToolStripStatusLabel("Reset")
            {
                IsLink = true,
            };
            resetButton.Click += (s, e) => _zoomTrackBar.Value = 0;

            _statusLabel = new ToolStripStatusLabel("Done")
            {
                Spring = true,
                TextAlign = ContentAlignment.MiddleLeft,
            };

            _statusStrip = new StatusStrip();
            _statusStrip.Items.Add(_statusLabel);
            _statusStrip.Items.Add(new ToolStripStatusLabel("Zoom:"));
            _statusStrip.Items.Add(new ToolStripControlHost(_zoomTrackBar));
            _statusStrip.Items.Add(_zoomValueLabel);
            _statusStrip.Items.Add(resetButton);

            Controls.Add(_tabControl);
            Controls.Add(_fullscreenHost);
            Controls.Add(_activityListSplitter);
            Controls.Add(_activityList);
            Controls.Add(_statusStrip);
            Controls.Add(_menuStrip);

            ResizeEnd += (s, e) => SyncAllTabsToCurrentLayout();

            _previousWindowState = WindowState;
            Resize += (s, e) =>
            {
                if (WindowState != _previousWindowState)
                {
                    _previousWindowState = WindowState;
                    SyncAllTabsToCurrentLayout();
                }
            };

            Load += DemoDI_Load;
        }

        private FormWindowState _previousWindowState;

        private void SyncAllTabsToCurrentLayout()
        {
            var activeTab = ActiveTab;
            if (activeTab == null)
            {
                return;
            }

            activeTab.PerformLayout();
            (activeTab.Browser.Control as BaseCefControl)?.EndResize();

            var b = activeTab.Bounds;

            foreach (BrowserTabPage tab in _tabControl.TabPages)
            {
                if (tab == activeTab)
                {
                    continue;
                }

                tab.SetBounds(b.X, b.Y, b.Width, b.Height);
            }
        }

        private BrowserTabPage ActiveTab => _tabControl.SelectedTab as BrowserTabPage;

        private void ZoomTrackBar_ValueChanged(object sender, EventArgs e)
        {
            var zoomLevel = _zoomTrackBar.Value / 10.0;
            _zoomValueLabel.Text = zoomLevel.ToString("F1");

            var activeTab = ActiveTab;
            if (activeTab == null)
            {
                return;
            }

            activeTab.Browser.ZoomLevel = zoomLevel;
            AppendActivity($"[DemoDI] ZoomLevel = {zoomLevel:F1} (active tab)");
        }

        private void AppendActivity(string message)
        {
            _activityList.Items.Insert(0, $"{DateTime.Now:HH:mm:ss.fff}  {message}");
            if (_activityList.Items.Count > 200)
            {
                _activityList.Items.RemoveAt(_activityList.Items.Count - 1);
            }
        }

        private void DemoDI_Load(object sender, EventArgs e)
        {
            _activityListSplitter.Visible = false;
            _activityList.Visible = false;

            var builder = CefGlueHost.CreateBuilder();

            builder.Services.AddSingleton<IActivityLog>(new FormActivityLog(this, AppendActivity));

            builder.Services.AddSingleton<ILogger>(new FormLogger(this, AppendActivity));

            builder.Services.ReplaceCoordinator<IKeyboardCoordinator, DiAwareKeyboardCoordinator>();

            builder.UseWinForms();

            _app = builder.Build();

            CreateNewTab();
        }

        private void CreateNewTab(string initialUrl = null)
        {
            if (_app == null)
            {
                return;
            }

            var addressBar = new TextBox
            {
                Dock = DockStyle.Fill,
            };

            var addressRow = new Panel
            {
                Dock = DockStyle.Top,
                Height = 26,
            };
            addressRow.Controls.Add(addressBar);

            var browserPanel = new Panel
            {
                Dock = DockStyle.Fill,
            };

            var browser = _app.CreateBrowser(new CefGlueBrowserOptions
            {
                SetupCefBrowserSettings = () =>
                {
                    var settings = new CefBrowserSettings();
                    settings.WindowlessFrameRate = 60;
                    return settings;
                },
                SetupCefWindowInfo = () =>
                {
                    var windowInfo = CefWindowInfo.Create();
                    windowInfo.SharedTextureEnabled = true;
                    return windowInfo;
                }
            });

            var b = new BitmapControl { Dock = DockStyle.Fill };

            browser.AttachTarget(b);
            browser.ShowOverlayInfo = _showOverlayInfoMenuItem.Checked;
            browserPanel.Controls.Add(browser.Control);

            var tab = new BrowserTabPage(browser, addressBar)
            {
                Text = "New Tab",
            };
            tab.Controls.Add(browserPanel);
            tab.Controls.Add(addressRow);

            tab.AddDevToolsControls();

            _tabControl.TabPages.Add(tab);
            _tabControl.SelectedTab = tab;

            BeginInvoke(new Action(() => b.Focus()));

            _tabControl.PerformLayout();
            tab.PerformLayout();
            browserPanel.PerformLayout();
            (browser.Control as BaseCefControl)?.EndResize();

            addressBar.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    browser.Address = addressBar.Text;
                    e.SuppressKeyPress = true;
                }
            };

            browser.LifeSpanHandler = new BrowserLifeSpanHandler(browser, openInNewTab: url => RunOnUiThread(() => CreateNewTab(url)));
            SelfTest.AttachIfEnabled(browser);

            browser.LoadStart += (s, e) =>
            {
                if (!e.Frame.IsMain || e.Frame.Url.StartsWith("devtools://", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                RunOnUiThread(() =>
                {
                    addressBar.Text = e.Frame.Url;
                    tab.LastStatus = $"Loading {e.Frame.Url}...";
                    if (ActiveTab == tab)
                    {
                        _statusLabel.Text = tab.LastStatus;
                    }
                });
            };

            browser.LoadEnd += (s, e) =>
            {
                if (!e.Frame.IsMain)
                {
                    return;
                }

                RunOnUiThread(() =>
                {
                    tab.LastStatus = "Done";
                    if (ActiveTab == tab)
                    {
                        _statusLabel.Text = tab.LastStatus;
                    }
                });
            };

            browser.TitleChanged += (s, title) =>
            {
                RunOnUiThread(() =>
                {
                    tab.Text = string.IsNullOrEmpty(title) ? "New Tab" : title;
                });
            };

            browser.FullscreenModeChanged += (s, fullscreen) =>
            {
                RunOnUiThread(() =>
                {
                    _menuStrip.Visible = !fullscreen;
                    _statusStrip.Visible = !fullscreen;
                    _activityListSplitter.Visible = !fullscreen && _showActivityLogMenuItem.Checked;
                    _activityList.Visible = !fullscreen && _showActivityLogMenuItem.Checked;

                    if (fullscreen)
                    {
                        browserPanel.Controls.Remove(browser.Control);
                        _fullscreenHost.Controls.Add(browser.Control);
                        _fullscreenHost.Visible = true;
                        _tabControl.Visible = false;
                    }
                    else
                    {
                        _fullscreenHost.Controls.Remove(browser.Control);
                        browserPanel.Controls.Add(browser.Control);
                        _fullscreenHost.Visible = false;
                        _tabControl.Visible = true;
                    }

                    ActiveTab?.FocusBrowserContent();
                });
            };

            browser.Address = initialUrl ?? "https://google.com";

            AppendActivity("[DemoDI] New tab via app.CreateBrowser()");
        }

        private void CloseActiveTab()
        {
            var tab = ActiveTab;
            if (tab == null)
            {
                return;
            }

            _tabControl.TabPages.Remove(tab);
            tab.Browser.Dispose();
            tab.Dispose();
        }

        private void RunOnUiThread(Action action)
        {
            if (IsHandleCreated && InvokeRequired)
            {
                BeginInvoke(action);
            }
            else
            {
                action();
            }
        }

        private void OnShowOverlayInfoMenuItemClick(object sender, EventArgs e)
        {
            var isChecked = _showOverlayInfoMenuItem.Checked;
            foreach (BrowserTabPage tab in _tabControl.TabPages)
            {
                tab.Browser.ShowOverlayInfo = isChecked;
            }
        }

        private void OnShowActivityLogMenuItemClick(object sender, EventArgs e)
        {
            var show = _showActivityLogMenuItem.Checked;

            if (show)
            {
                _activityList.Height = _lastActivityListHeight;
                _activityList.Visible = true;
                _activityListSplitter.Visible = true;
            }
            else
            {
                _lastActivityListHeight = _activityList.Height;
                _activityList.Visible = false;
                _activityListSplitter.Visible = false;
            }

            SyncAllTabsToCurrentLayout();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            foreach (BrowserTabPage tab in _tabControl.TabPages)
            {
                tab.Browser?.Dispose();
            }
        }
    }

    internal sealed class BrowserTabPage : TabPage
    {
        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public CefGlueBrowser Browser { get; }

        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public TextBox AddressBar { get; }

        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public string LastStatus { get; set; } = "Done";

        private readonly Panel _devToolsPanel;
        private readonly Panel _devToolsContentHost;
        private readonly Splitter _devToolsSplitter;
        private Control _devToolsControl;
        private int _lastDevToolsHeight = 300;

        public bool IsDevToolsOpen { get; private set; }

        public BrowserTabPage(CefGlueBrowser browser, TextBox addressBar)
        {
            Browser = browser;
            AddressBar = addressBar;

            _devToolsContentHost = new Panel { Dock = DockStyle.Fill };

            var closeButton = new Button { Text = "X", Dock = DockStyle.Right, Width = 28 };
            closeButton.Click += (s, e) => CloseDevTools();

            var headerLabel = new Label
            {
                Text = "DevTools",
                Dock = DockStyle.Left,
                AutoSize = true,
                Padding = new Padding(4, 5, 0, 0),
                Font = new Font(Font, FontStyle.Bold),
            };

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 24,
                BackColor = Color.FromArgb(0xFF, 0xE8, 0xE8, 0xE8),
            };
            header.Controls.Add(headerLabel);
            header.Controls.Add(closeButton);

            _devToolsSplitter = new Splitter { Dock = DockStyle.Bottom, Height = 4 };

            _devToolsPanel = new Panel { Dock = DockStyle.Bottom, Height = 0, Visible = false };
            _devToolsPanel.Controls.Add(_devToolsContentHost);
            _devToolsPanel.Controls.Add(header);

            Browser.DevToolsOpened += control =>
            {
                IsDevToolsOpen = true;
                _devToolsControl = control;

                _devToolsPanel.Height = _lastDevToolsHeight;
                _devToolsPanel.Visible = true;
                _devToolsSplitter.Visible = true;
                _devToolsContentHost.Controls.Add(control);

                PerformLayout();
                (Browser.Control as BaseCefControl)?.EndResize();
            };

            Browser.DevToolsClosed += () =>
            {
                IsDevToolsOpen = false;
                if (_devToolsPanel.Height > 0)
                {
                    _lastDevToolsHeight = _devToolsPanel.Height;
                }
                _devToolsContentHost.Controls.Clear();
                _devToolsPanel.Visible = false;
                _devToolsSplitter.Visible = false;
                _devToolsControl = null;

                PerformLayout();
                (Browser.Control as BaseCefControl)?.EndResize();
            };

            _devToolsSplitter.SplitterMoved += (s, e) =>
            {
                PerformLayout();
                (Browser.Control as BaseCefControl)?.EndResize();
                (_devToolsControl as BaseCefControl)?.EndResize();
            };

        }

        public void AddDevToolsControls()
        {
            Controls.Add(_devToolsSplitter);
            Controls.Add(_devToolsPanel);
        }

        public void OpenDevTools() => Browser.ShowDevToolOSR();
        public void CloseDevTools() => Browser.CloseDevToolOSR();

        public void FocusBrowserContent() => Browser.Control.Focus();
    }

    public interface IActivityLog
    {
        void Log(string message);
    }

    internal sealed class FormActivityLog : IActivityLog
    {
        private readonly Control _uiControl;
        private readonly Action<string> _append;

        public FormActivityLog(Control uiControl, Action<string> append)
        {
            _uiControl = uiControl;
            _append = append;
        }

        public void Log(string message)
        {
            if (_uiControl.IsHandleCreated && _uiControl.InvokeRequired)
            {
                _uiControl.BeginInvoke(new Action(() => _append(message)));
            }
            else
            {
                _append(message);
            }
        }
    }

    internal sealed class FormLogger : ILogger
    {
        private readonly Control _uiControl;
        private readonly Action<string> _append;

        public FormLogger(Control uiControl, Action<string> append)
        {
            _uiControl = uiControl;
            _append = append;
        }

        public bool IsTraceEnabled => true;
        public bool IsDebugEnabled => true;
        public bool IsInfoEnabled => true;
        public bool IsWarnEnabled => true;
        public bool IsErrorEnabled => true;
        public bool IsFatalEnabled => true;

        private void Log(string level, string message)
        {
            var line = $"[ILogger:{level}] {message}";

            if (_uiControl.IsHandleCreated && _uiControl.InvokeRequired)
            {
                _uiControl.BeginInvoke(new Action(() => _append(line)));
            }
            else
            {
                _append(line);
            }
        }

        public void Trace(string message) => Log("Trace", message);
        public void TraceException(string message, Exception exception) => Log("Trace", $"{message}: {exception}");
        public void Debug(string message) => Log("Debug", message);
        public void DebugException(string message, Exception exception) => Log("Debug", $"{message}: {exception}");
        public void Info(string message) => Log("Info", message);
        public void InfoException(string message, Exception exception) => Log("Info", $"{message}: {exception}");
        public void Warn(string message) => Log("Warn", message);
        public void WarnException(string message, Exception exception) => Log("Warn", $"{message}: {exception}");
        public void Error(string message) => Log("Error", message);
        public void ErrorException(string message, Exception exception) => Log("Error", $"{message}: {exception}");
        public void Fatal(string message) => Log("Fatal", message);
        public void FatalException(string message, Exception exception) => Log("Fatal", $"{message}: {exception}");
    }

    internal sealed class DiAwareKeyboardCoordinator : IKeyboardCoordinator
    {
        private const int VK_ESCAPE = 0x1B;

        private readonly IActivityLog _activityLog;
        private CefBrowserContext _context;

        public DiAwareKeyboardCoordinator(IActivityLog activityLog)
        {
            _activityLog = activityLog;
        }

        public void Attach(CefBrowserContext context)
        {
            _context = context;
        }

        public void HandleLostFocus()
        {
            _context.WithErrorHandling(nameof(HandleLostFocus), () =>
            {
                _context.GetBrowserHost()?.SetFocus(false);
            });
            _activityLog.Log("[DiAwareKeyboardCoordinator] Lost focus");
        }

        public void HandleTextInput(string text, out bool handled)
        {
            var _handled = false;

            _context.WithErrorHandling(nameof(HandleTextInput), () =>
            {
                var browserHost = _context.GetBrowserHost();
                if (browserHost != null)
                {
                    foreach (var c in text)
                    {
                        browserHost.SendKeyEvent(new CefKeyEvent
                        {
                            EventType = CefKeyEventType.Char,
                            WindowsKeyCode = c,
                            Character = c
                        });
                    }

                    _handled = true;
                    _activityLog.Log($"[DiAwareKeyboardCoordinator] Text input: \"{text}\"");
                }
            });

            handled = _handled;
        }

        public void HandleKeyPress(CefKeyEvent keyEvent, out bool handled)
        {
            _context.WithErrorHandling(nameof(HandleKeyPress), () =>
            {
                var browserHost = _context.GetBrowserHost();
                if (browserHost != null)
                {
                    browserHost.SendKeyEvent(keyEvent);
                    _activityLog.Log($"[DiAwareKeyboardCoordinator] {keyEvent.EventType}: WindowsKeyCode={keyEvent.WindowsKeyCode}");

                    if (keyEvent.WindowsKeyCode == VK_ESCAPE &&
                        keyEvent.EventType == CefKeyEventType.RawKeyDown &&
                        _context.IsFullscreen())
                    {
                        browserHost.ExitFullscreen(true);
                    }
                }
            });
            handled = false;
        }

        public void HandleImeComposition(string text, int cursorPosition) =>
            HandleImeComposition(text, cursorPosition, null);

        public void HandleImeComposition(string text, int cursorPosition, Xilium.CefGlue.Common.Platform.ImeCompositionClause[] clauses)
        {
            _activityLog.Log($"[DiAwareKeyboardCoordinator] IME composing: \"{text}\" ({clauses?.Length ?? 0} clauses)");

            _context.WithErrorHandling(nameof(HandleImeComposition), () =>
            {
                var browserHost = _context.GetBrowserHost();
                if (browserHost == null)
                {
                    return;
                }

                if (string.IsNullOrEmpty(text))
                {
                    browserHost.ImeCancelComposition();
                    return;
                }

                CefCompositionUnderline[] underlines;
                if (clauses != null && clauses.Length > 0)
                {
                    underlines = new CefCompositionUnderline[clauses.Length];
                    for (var i = 0; i < clauses.Length; i++)
                    {
                        var c = clauses[i];
                        underlines[i] = new CefCompositionUnderline
                        {
                            Range = new CefRange(c.Start, c.End),
                            Color = c.ColorArgb == 0 ? new CefColor(255, 0, 0, 0) : new CefColor(c.ColorArgb),
                            BackgroundColor = new CefColor(0, 0, 0, 0),
                            Thick = c.Thick,
                            Style = c.Style
                        };
                    }
                }
                else
                {
                    underlines = new[]
                    {
                        new CefCompositionUnderline
                        {
                            Range = new CefRange(0, text.Length),
                            Color = new CefColor(255, 0, 0, 0),
                            BackgroundColor = new CefColor(0, 0, 0, 0),
                            Thick = false,
                            Style = CefCompositionUnderlineStyle.Solid
                        }
                    };
                }

                browserHost.ImeSetComposition(text, underlines, new CefRange(-1, -1), new CefRange(cursorPosition, cursorPosition));
            });
        }

        public void HandleImeCommitText(string text)
        {
            _activityLog.Log($"[DiAwareKeyboardCoordinator] IME committed: \"{text}\"");

            _context.WithErrorHandling(nameof(HandleImeCommitText), () =>
            {
                _context.GetBrowserHost()?.ImeCommitText(text, new CefRange(-1, -1), 0);
            });
        }
    }
}
