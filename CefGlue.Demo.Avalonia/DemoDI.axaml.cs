using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Threading;
using Microsoft.Extensions.DependencyInjection;
using Xilium.CefGlue.Avalonia;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Composition;
using Xilium.CefGlue.Common.Coordinators;

namespace Xilium.CefGlue.Demo.Avalonia
{
    public partial class DemoDI : Window
    {
        private readonly CefGlueHost _app;

        public DemoDI()
        {
            InitializeComponent();
            Title = "CefGlue.Avalonia Demo - DI (DemoDI)";

            var builder = CefGlueHost.CreateBuilder();

            builder.Services.AddSingleton<IActivityLog>(new WindowActivityLog(this, AppendActivity));

            builder.Services.ReplaceCoordinator<IKeyboardCoordinator, DiAwareKeyboardCoordinator>();

            builder.UseAvalonia();

            _app = builder.Build();

            ZoomSlider.PropertyChanged += (s, e) =>
            {
                if (e.Property == RangeBase.ValueProperty)
                {
                    ZoomSlider_ValueChanged((double)e.NewValue);
                }
            };

            BrowserTabs.SelectionChanged += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                ActiveTab?.FocusBrowserContent();
                StatusText.Text = ActiveTab?.LastStatus ?? "";
            });

            Opened += (_, _) => ActiveTab?.FocusBrowserContent();

            CreateNewTab();

            Closed += (_, _) =>
            {
                foreach (var item in BrowserTabs.Items)
                {
                    (((TabItem)item).Content as BrowserTabView)?.Dispose();
                }
            };
        }

        private BrowserTabView ActiveTab => (BrowserTabs.SelectedItem as TabItem)?.Content as BrowserTabView;

        private RowDefinition ActivityLogRow => RootGrid.RowDefinitions[3];

        private void ZoomSlider_ValueChanged(double newValue)
        {
            var zoomLevel = Math.Round(newValue, 1);
            ZoomValueText.Text = zoomLevel.ToString("F1");

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
            ZoomSlider.Value = 0;
        }

        private void AppendActivity(string message)
        {
            ActivityList.Items.Insert(0, $"{DateTime.Now:HH:mm:ss.fff}  {message}");
            while (ActivityList.Items.Count > 200)
            {
                ActivityList.Items.RemoveAt(ActivityList.Items.Count - 1);
            }
        }

        private void CreateNewTab(string initialUrl = null)
        {
            var view = new BrowserTabView(_app, initialUrl, openInNewTab: url => Dispatcher.UIThread.Post(() => CreateNewTab(url)));
            view.Browser.ShowOverlayInfo = ShowOverlayInfoMenuItem.IsChecked == true;

            var tab = new TabItem();

            var headerPanel = new DockPanel();
            var tabTitle = new TextBlock { Text = "New Tab", VerticalAlignment = VerticalAlignment.Center };
            var closeButton = new Button { Content = "X", Padding = new Thickness(6, 0, 6, 0), Margin = new Thickness(6, 0, 0, 0) };
            closeButton.Click += (s, e) =>
            {
                view.Dispose();
                BrowserTabs.Items.Remove(tab);
            };
            DockPanel.SetDock(closeButton, Dock.Right);
            headerPanel.Children.Add(tabTitle);
            headerPanel.Children.Add(closeButton);
            tab.Header = headerPanel;

            view.TitleChanged += title => Dispatcher.UIThread.Post(() =>
            {
                tabTitle.Text = title;
                ToolTip.SetTip(tab, title);
            });

            view.StatusTextChanged += status => Dispatcher.UIThread.Post(() =>
            {
                if (ActiveTab == view)
                {
                    StatusText.Text = status;
                }
            });

            view.FullscreenModeChanged += fullscreen =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (fullscreen)
                    {
                        view.DetachControl();
                        FullscreenHost.Children.Add(view.Control);
                        FullscreenHost.IsVisible = true;
                        BrowserTabs.IsVisible = false;
                    }
                    else
                    {
                        FullscreenHost.Children.Clear();
                        view.ReattachControl();
                        FullscreenHost.IsVisible = false;
                        BrowserTabs.IsVisible = true;
                    }

                    Dispatcher.UIThread.Post(() => view.FocusBrowserContent());
                });
            };

            tab.Content = view;
            BrowserTabs.Items.Add(tab);
            BrowserTabs.SelectedItem = tab;

            AppendActivity("[DemoDI] New tab via app.CreateBrowser()");
        }

        private void CloseActiveTab()
        {
            var tab = BrowserTabs.SelectedItem as TabItem;
            if (tab == null)
            {
                return;
            }

            BrowserTabs.Items.Remove(tab);
            (tab.Content as BrowserTabView)?.Dispose();
        }

        private void OnShowOverlayInfoMenuItemClick(object sender, RoutedEventArgs e)
        {
            var isChecked = ShowOverlayInfoMenuItem.IsChecked == true;
            foreach (var item in BrowserTabs.Items)
            {
                if (((TabItem)item).Content is BrowserTabView view)
                {
                    view.Browser.ShowOverlayInfo = isChecked;
                }
            }
        }

        private GridLength _lastActivityLogRowHeight = new GridLength(150);

        private void OnShowActivityLogMenuItemClick(object sender, RoutedEventArgs e)
        {
            var show = ShowActivityLogMenuItem.IsChecked == true;

            ActivityLogSplitter.IsVisible = show;
            ActivityList.IsVisible = show;

            if (show)
            {
                ActivityLogRow.Height = _lastActivityLogRowHeight;
            }
            else
            {
                _lastActivityLogRowHeight = ActivityLogRow.Height;
                ActivityLogRow.Height = new GridLength(0);
            }
        }

        private void OnNewTabMenuItemClick(object sender, RoutedEventArgs e) => CreateNewTab();

        private void OnCloseTabMenuItemClick(object sender, RoutedEventArgs e) => CloseActiveTab();

        private void OnOpenDevToolsMenuItemClick(object sender, RoutedEventArgs e) => ActiveTab?.OpenDevTools();

        private void TabControl_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
        }
    }

    public interface IActivityLog
    {
        void Log(string message);
    }

    internal sealed class WindowActivityLog : IActivityLog
    {
        private readonly Action<string> _append;

        public WindowActivityLog(Window window, Action<string> append)
        {
            _append = append;
        }

        public void Log(string message)
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                _append(message);
            }
            else
            {
                Dispatcher.UIThread.Post(() => _append(message));
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

        public void HandleImeComposition(string text, int cursorPosition)
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

                var underline = new CefCompositionUnderline
                {
                    Range = new CefRange(0, text.Length),
                    Color = new CefColor(255, 0, 0, 0),
                    BackgroundColor = new CefColor(0, 0, 0, 0),
                    Thick = false,
                    Style = CefCompositionUnderlineStyle.Solid
                };

                browserHost.ImeSetComposition(
                    text,
                    1,
                    underline,
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
