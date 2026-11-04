using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using Gtk;
using Microsoft.Extensions.DependencyInjection;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Composition;
using Xilium.CefGlue.Common.Coordinators;
using Xilium.CefGlue.GTK;

namespace Xilium.CefGlue.Demo.GTK
{
    /// <summary>
    /// GTK counterpart of the Avalonia DemoDI window: menubar, tabs, status bar with zoom, activity log, and
    /// browsers created through a CefGlueHost (DI) with a replaced keyboard coordinator.
    /// </summary>
    internal sealed class MainWindow
    {
        private const int MaxLogLines = 200;

        private readonly CefGlueHost _app;
        private readonly bool _useTexture;
        private readonly SynchronizationContext _uiContext;
        private readonly ApplicationWindow _window;
        private readonly PopoverMenuBar _menuBar;
        private readonly Notebook _notebook;
        private readonly Box _statusBar;
        private readonly Label _statusLabel;
        private readonly Scale _zoomScale;
        private readonly Label _zoomValueLabel;
        private readonly ScrolledWindow _logScroll;
        private readonly ListBox _logList;
        private readonly Dictionary<Widget, BrowserTab> _tabs = new Dictionary<Widget, BrowserTab>();
        private bool _showOverlayInfo = true;
        private bool _showActivityLog;

        public MainWindow(Application application, bool useTexture)
        {
            _useTexture = useTexture;
            _uiContext = SynchronizationContext.Current;

            var builder = CefGlueHost.CreateBuilder();
            builder.Services.AddSingleton<IActivityLog>(new WindowActivityLog(_uiContext, AppendActivity));
            builder.Services.ReplaceCoordinator<IKeyboardCoordinator, LoggingKeyboardCoordinator>();
            builder.UseGtk();
            _app = builder.Build();

            _window = ApplicationWindow.New(application);
            _window.Title = $"CefGlue GTK Demo - DI ({(useTexture ? "Texture" : "Bitmap")})";
            _window.SetDefaultSize(1000, 650);

            _menuBar = PopoverMenuBar.NewFromModel(BuildMenu(application));

            _notebook = Notebook.New();
            _notebook.Scrollable = true;
            _notebook.Vexpand = true;
            _notebook.OnSwitchPage += (_, e) => _uiContext.Post(_ => OnActiveTabChanged(e.Page), null);

            _logList = ListBox.New();
            _logList.SelectionMode = SelectionMode.None;
            _logScroll = ScrolledWindow.New();
            _logScroll.Child = _logList;
            _logScroll.SetSizeRequest(-1, 150);
            _logScroll.Visible = false;

            _statusLabel = Label.New("Done");
            _statusLabel.Hexpand = true;
            _statusLabel.Xalign = 0;
            _statusLabel.Ellipsize = Pango.EllipsizeMode.End;

            _zoomScale = Scale.NewWithRange(Orientation.Horizontal, -5, 5, 0.1);
            _zoomScale.SetValue(0);
            _zoomScale.DrawValue = false;
            _zoomScale.SetSizeRequest(220, -1);
            _zoomScale.OnValueChanged += (_, _) => OnZoomChanged(_zoomScale.GetValue());
            _zoomValueLabel = Label.New("0.0");
            _zoomValueLabel.SetSizeRequest(32, -1);
            var resetZoomButton = Button.NewWithLabel("Reset");
            resetZoomButton.OnClicked += (_, _) => _zoomScale.SetValue(0);

            _statusBar = Box.New(Orientation.Horizontal, 6);
            _statusBar.Append(_statusLabel);
            _statusBar.Append(Label.New("Zoom:"));
            _statusBar.Append(_zoomScale);
            _statusBar.Append(_zoomValueLabel);
            _statusBar.Append(resetZoomButton);
            _statusBar.SetMarginTop(4);
            _statusBar.SetMarginBottom(4);
            _statusBar.SetMarginStart(6);
            _statusBar.SetMarginEnd(6);

            var layout = Box.New(Orientation.Vertical, 0);
            layout.Append(_menuBar);
            layout.Append(_notebook);
            layout.Append(_logScroll);
            layout.Append(_statusBar);
            _window.Child = layout;

            _window.OnCloseRequest += (_, _) =>
            {
                foreach (var tab in _tabs.Values)
                {
                    tab.Dispose();
                }
                _tabs.Clear();
                return false;
            };

            CreateTab();
        }

        public void Show() => _window.Show();

        private BrowserTab ActiveTab
        {
            get
            {
                var page = _notebook.GetNthPage(_notebook.GetCurrentPage());
                return page != null && _tabs.TryGetValue(page, out var tab) ? tab : null;
            }
        }

        private Gio.MenuModel BuildMenu(Application application)
        {
            AddAction("new-tab", () => CreateTab());
            AddAction("close-tab", CloseActiveTab);
            AddAction("devtools", OpenDevTools);
            AddAction("quit", () => _window.Close());
            AddToggleAction("overlay", _showOverlayInfo, SetShowOverlayInfo);
            AddToggleAction("activity-log", _showActivityLog, SetShowActivityLog);

            application.SetAccelsForAction("win.new-tab", new[] { "<Control>t" });
            application.SetAccelsForAction("win.close-tab", new[] { "<Control>w" });
            application.SetAccelsForAction("win.devtools", new[] { "F12" });
            application.SetAccelsForAction("win.quit", new[] { "<Control>q" });

            var fileMenu = Gio.Menu.New();
            fileMenu.Append("_New Tab", "win.new-tab");
            fileMenu.Append("_Close Tab", "win.close-tab");
            fileMenu.Append("Toggle _Dev Tools", "win.devtools");
            fileMenu.Append("_Quit", "win.quit");

            var viewMenu = Gio.Menu.New();
            viewMenu.Append("Show _Overlay Info", "win.overlay");
            viewMenu.Append("Show _Activity Log", "win.activity-log");

            var menu = Gio.Menu.New();
            menu.AppendSubmenu("_File", fileMenu);
            menu.AppendSubmenu("_View", viewMenu);
            return menu;
        }

        private void AddAction(string name, Action onActivate)
        {
            var action = Gio.SimpleAction.New(name, null);
            action.OnActivate += (_, _) => onActivate();
            _window.AddAction(action);
        }

        private void AddToggleAction(string name, bool initial, Action<bool> onChanged)
        {
            var action = Gio.SimpleAction.NewStateful(name, null, GLib.Variant.NewBoolean(initial));
            action.OnChangeState += (sender, e) =>
            {
                sender.SetState(e.Value);
                onChanged(e.Value.GetBoolean());
            };
            _window.AddAction(action);
        }

        private void CreateTab(string url = null)
        {
            var tab = new BrowserTab(_app, _useTexture, url, openInNewTab: newUrl => _uiContext.Post(_ => CreateTab(newUrl), null));
            tab.ShowOverlayInfo = _showOverlayInfo;

            var title = Label.New("New Tab");
            title.Ellipsize = Pango.EllipsizeMode.End;
            title.WidthChars = 18;
            title.MaxWidthChars = 24;
            title.Xalign = 0;
            var closeButton = Button.NewFromIconName("window-close-symbolic");
            closeButton.HasFrame = false;
            closeButton.OnClicked += (_, _) => CloseTab(tab);
            var header = Box.New(Orientation.Horizontal, 4);
            header.Append(title);
            header.Append(closeButton);

            tab.TitleChanged += text =>
            {
                title.SetText(text);
                header.TooltipText = text;
                if (tab == ActiveTab)
                {
                    _window.Title = text;
                }
            };
            tab.StatusChanged += status =>
            {
                if (tab == ActiveTab)
                {
                    _statusLabel.SetText(status);
                }
            };
            tab.FullscreenChanged += SetPageFullscreen;

            _tabs[tab.Page] = tab;
            var index = _notebook.AppendPage(tab.Page, header);
            _notebook.SetTabReorderable(tab.Page, true);
            _notebook.SetCurrentPage(index);

            AppendActivity("[DemoGTK] New tab via app.CreateBrowser()");
        }

        private void CloseTab(BrowserTab tab)
        {
            var index = _notebook.PageNum(tab.Page);
            if (index >= 0)
            {
                _notebook.RemovePage(index);
            }

            _tabs.Remove(tab.Page);
            tab.Dispose();

            if (_tabs.Count == 0)
            {
                _window.Close();
            }
        }

        private void CloseActiveTab()
        {
            var tab = ActiveTab;
            if (tab != null)
            {
                CloseTab(tab);
            }
        }

        private void OnActiveTabChanged(Widget page)
        {
            if (!_tabs.TryGetValue(page, out var tab))
            {
                return;
            }

            _statusLabel.SetText(tab.LastStatus);
            _window.Title = string.IsNullOrEmpty(tab.Browser.Title) ? "CefGlue GTK Demo" : tab.Browser.Title;
            _zoomScale.SetValue(tab.ZoomLevel);
            tab.Focus();
        }

        private void OnZoomChanged(double value)
        {
            var zoomLevel = Math.Round(value, 1);
            _zoomValueLabel.SetText(zoomLevel.ToString("F1"));

            var tab = ActiveTab;
            if (tab != null && Math.Abs(tab.ZoomLevel - zoomLevel) > 0.01)
            {
                tab.SetZoomLevel(zoomLevel);
                AppendActivity($"[DemoGTK] ZoomLevel = {zoomLevel:F1} (active tab)");
            }
        }

        private void SetPageFullscreen(bool fullscreen)
        {
            _menuBar.Visible = !fullscreen;
            _statusBar.Visible = !fullscreen;
            _notebook.ShowTabs = !fullscreen;
            _logScroll.Visible = !fullscreen && _showActivityLog;
            ActiveTab?.Focus();
        }

        private void SetShowOverlayInfo(bool show)
        {
            _showOverlayInfo = show;
            foreach (var tab in _tabs.Values)
            {
                tab.ShowOverlayInfo = show;
            }
        }

        private void SetShowActivityLog(bool show)
        {
            _showActivityLog = show;
            _logScroll.Visible = show;
        }

        private void AppendActivity(string message)
        {
            var line = Label.New($"{DateTime.Now:HH:mm:ss.fff}  {message}");
            line.Xalign = 0;
            line.Selectable = true;
            _logList.Prepend(line);

            var extra = _logList.GetRowAtIndex(MaxLogLines);
            if (extra != null)
            {
                _logList.Remove(extra);
            }
        }

        private static readonly HttpClient DevToolsHttpClient = new HttpClient();

        private async void OpenDevTools()
        {
            var tab = ActiveTab;
            if (tab == null)
            {
                return;
            }

            if (tab.IsDevToolsOpen)
            {
                tab.CloseDevTools();
                return;
            }

            var url = "devtools://devtools/bundled/inspector.html";
            var port = CefRuntimeLoader.RemoteDebuggingPort;
            if (port > 0)
            {
                try
                {
                    var json = await DevToolsHttpClient.GetStringAsync($"http://127.0.0.1:{port}/json/list");
                    using var targets = JsonDocument.Parse(json);
                    var target = targets.RootElement.EnumerateArray()
                        .Where(t => t.GetProperty("type").GetString() == "page"
                            && t.GetProperty("url").GetString()?.StartsWith("devtools://") != true)
                        .OrderByDescending(t => t.GetProperty("url").GetString() == tab.Browser.Address)
                        .Select(t => t.GetProperty("id").GetString())
                        .FirstOrDefault();
                    if (target != null)
                    {
                        url = $"devtools://devtools/bundled/inspector.html?ws=127.0.0.1:{port}/devtools/page/{target}";
                    }
                }
                catch (Exception ex) when (ex is HttpRequestException or JsonException)
                {
                    AppendActivity($"[DemoGTK] DevTools target lookup failed: {ex.Message}");
                }
            }

            tab.OpenDevTools(url);
            AppendActivity("[DemoGTK] DevTools docked");
        }
    }
}
