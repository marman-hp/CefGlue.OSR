using Microsoft.Extensions.DependencyInjection;
using System;
using System.Windows;
using System.Windows.Controls;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Composition;
using Xilium.CefGlue.Common.Coordinators;
using Xilium.CefGlue.WPF;
using Xilium.CefGlue.WPF.Common;

namespace Xilium.CefGlue.Demo.WPF
{
    public partial class DemoDI : Window
    {
        private CefGlueHost _app;

        public DemoDI()
        {
            InitializeComponent();

            tabControl.SelectionChanged += (_, _) =>
                Dispatcher.BeginInvoke((Action)(() =>
                {
                    ActiveTab?.FocusBrowserContent();
                    statusText.Text = ActiveTab?.LastStatus ?? "";
                }));

            ContentRendered += (_, _) => ActiveTab?.FocusBrowserContent();
        }

        private BrowserTabView ActiveTab => (tabControl.SelectedItem as TabItem)?.Content as BrowserTabView;

        private void ZoomSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            var zoomLevel = Math.Round(e.NewValue, 1);
            zoomValueText.Text = zoomLevel.ToString("F1");

            var activeTab = ActiveTab;
            if (activeTab == null)
            {
                return;
            }

            activeTab.Browser.ZoomLevel = zoomLevel;
            AppendActivity($"[DemoDI] ZoomLevel = {zoomLevel:F1} (active tab)");
        }

        private void OnResetZoomClick(object sender, RoutedEventArgs e)
        {
            zoomSlider.Value = 0;
        }

        private void AppendActivity(string message)
        {
            activityList.Items.Insert(0, $"{DateTime.Now:HH:mm:ss.fff}  {message}");
            if (activityList.Items.Count > 200)
            {
                activityList.Items.RemoveAt(activityList.Items.Count - 1);
            }
        }

        private void DemoDI_Loaded(object sender, RoutedEventArgs e)
        {
            var builder = CefGlueHost.CreateBuilder();

            builder.Services.AddSingleton<IActivityLog>(new WindowActivityLog(this, AppendActivity));

            builder.Services.ReplaceCoordinator<IKeyboardCoordinator, DiAwareKeyboardCoordinator>();

            builder.UseWpf();

            _app = builder.Build();

            CreateNewTab();
        }

        private void CreateNewTab(string initialUrl = null)
        {
            if (_app == null)
            {
                return;
            }

            var view = new BrowserTabView(_app, initialUrl, openInNewTab: url => Dispatcher.BeginInvoke((Action)(() => CreateNewTab(url))));
            view.Browser.ShowOverlayInfo = showOverlayInfoMenuItem.IsChecked;

            var tab = new TabItem();

            tabControl.Items.Add(tab);

            var headerPanel = new DockPanel();
            var tabTitle = new TextBlock { Text = "New Tab" };
            var closeButton = new Button { Content = "X", Padding = new Thickness(2), Margin = new Thickness(5, 0, 0, 0) };
            closeButton.Click += (s, e) =>
            {
                view.Dispose();
                tabControl.Items.Remove(tab);
            };
            DockPanel.SetDock(closeButton, Dock.Right);
            headerPanel.Children.Add(tabTitle);
            headerPanel.Children.Add(closeButton);
            tab.Header = headerPanel;

            view.TitleChanged += title => Dispatcher.BeginInvoke((Action)(() =>
            {
                tabTitle.Text = title;
                tab.ToolTip = title;
            }));

            view.StatusTextChanged += status => Dispatcher.BeginInvoke((Action)(() =>
            {
                if (ActiveTab == view)
                {
                    statusText.Text = status;
                }
            }));

            view.FullscreenModeChanged += fullscreen =>
            {
                mainMenu.Visibility = fullscreen ? Visibility.Collapsed : Visibility.Visible;

                if (fullscreen)
                {
                    view.DetachControl();
                    fullscreenHost.Children.Add(view.Control);
                    fullscreenHost.Visibility = Visibility.Visible;
                    tabControl.Visibility = Visibility.Collapsed;
                }
                else
                {
                    fullscreenHost.Children.Clear();
                    view.ReattachControl();
                    fullscreenHost.Visibility = Visibility.Collapsed;
                    tabControl.Visibility = Visibility.Visible;
                }

                Dispatcher.BeginInvoke((Action)(() => view.FocusBrowserContent()));
            };

            tab.Content = view;
            tabControl.SelectedItem = tab;

            AppendActivity("[DemoDI] New tab via app.CreateBrowser()");
        }

        private void CloseActiveTab()
        {
            if (tabControl.SelectedIndex < 0)
            {
                return;
            }

            var view = ActiveTab;
            tabControl.Items.RemoveAt(tabControl.SelectedIndex);
            view?.Dispose();
        }

        private void OnShowOverlayInfoMenuItemClick(object sender, RoutedEventArgs e)
        {
            var isChecked = showOverlayInfoMenuItem.IsChecked;
            foreach (TabItem tab in tabControl.Items)
            {
                if (tab.Content is BrowserTabView view)
                {
                    view.Browser.ShowOverlayInfo = isChecked;
                }
            }
        }

        private GridLength _lastActivityLogRowHeight = new GridLength(150);

        private void OnShowActivityLogMenuItemClick(object sender, RoutedEventArgs e)
        {
            var show = showActivityLogMenuItem.IsChecked;

            listSplitter.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            activityList.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

            if (show)
            {
                activityLogRow.Height = _lastActivityLogRowHeight;
            }
            else
            {
                _lastActivityLogRowHeight = activityLogRow.Height;
                activityLogRow.Height = new GridLength(0);
            }
        }

        private void OnNewTabMenuItemClick(object sender, RoutedEventArgs e) => CreateNewTab();

        private void OnCloseTabMenuItemClick(object sender, RoutedEventArgs e) => CloseActiveTab();

        private void OnOpenDevToolsMenuItemClick(object sender, RoutedEventArgs e) => ActiveTab?.ToggleDevTools();

        // F12 toggles DevTools, also while the page has focus (PreviewKeyDown runs before the browser sees the key).
        private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.F12 && System.Windows.Input.Keyboard.Modifiers == System.Windows.Input.ModifierKeys.None)
            {
                ActiveTab?.ToggleDevTools();
                e.Handled = true;
            }
        }

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }

        protected override void OnClosed(EventArgs e)
        {
            base.OnClosed(e);
            foreach (TabItem tab in tabControl.Items)
            {
                (tab.Content as BrowserTabView)?.Dispose();
            }
        }
    }

    public interface IActivityLog
    {
        void Log(string message);
    }

    internal sealed class WindowActivityLog : IActivityLog
    {
        private readonly System.Windows.Threading.Dispatcher _dispatcher;
        private readonly Action<string> _append;

        public WindowActivityLog(Window window, Action<string> append)
        {
            _dispatcher = window.Dispatcher;
            _append = append;
        }

        public void Log(string message)
        {
            if (_dispatcher.CheckAccess())
            {
                _append(message);
            }
            else
            {
                _dispatcher.BeginInvoke(new Action(() => _append(message)));
            }
        }
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

                browserHost.ImeSetComposition(
                    text,
                    underlines,
                    new CefRange(-1, -1),
                    new CefRange(cursorPosition, cursorPosition));
            });
            _activityLog.Log($"[DiAwareKeyboardCoordinator] IME composing: \"{text}\"");
        }

        public void HandleImeCommitText(string text)
        {
            _context.WithErrorHandling(nameof(HandleImeCommitText), () =>
            {
                _context.GetBrowserHost()?.ImeCommitText(text, new CefRange(-1, -1), 0);
            });
            _activityLog.Log($"[DiAwareKeyboardCoordinator] IME committed: \"{text}\"");
        }
    }
}
