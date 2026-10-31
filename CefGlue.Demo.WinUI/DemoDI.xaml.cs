using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.Extensions.DependencyInjection;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Composition;
using Xilium.CefGlue.Common.Coordinators;
using Xilium.CefGlue.WinUI;

namespace Xilium.CefGlue.Demo.WinUI
{
    public sealed partial class DemoDI : Window
    {
        private CefGlueHost _app;

        public DemoDI()
        {
            InitializeComponent();
            Title = "CefGlue.WinUI Demo - DI (DemoDI)";

            var builder = CefGlueHost.CreateBuilder();

            builder.Services.AddSingleton<IActivityLog>(new WindowActivityLog(this, AppendActivity));

            builder.Services.ReplaceCoordinator<IKeyboardCoordinator, DiAwareKeyboardCoordinator>();

            builder.UseWinUI();

            _app = builder.Build();

            CreateNewTab();

            Closed += (s, e) => Application.Current.Exit();
        }

        private BrowserTabView ActiveTab => (BrowserTabs.SelectedItem as TabViewItem)?.Content as BrowserTabView;

        private void OnZoomSliderValueChanged(object sender, RangeBaseValueChangedEventArgs e)
        {
            var zoomLevel = Math.Round(e.NewValue, 1);
            ZoomValueText.Text = zoomLevel.ToString("F1");

            var activeTab = ActiveTab;
            if (activeTab == null)
            {
                return;
            }

            activeTab.Browser.ZoomLevel = zoomLevel;
            AppendActivity($"[DemoDI] ZoomLevel = {zoomLevel:F1} (active tab)");
        }

        private void OnResetZoomButtonClick(object sender, RoutedEventArgs e)
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
            var view = new BrowserTabView(_app, this, initialUrl, openInNewTab: url => DispatcherQueue.TryEnqueue(() => CreateNewTab(url)));
            view.Browser.ShowOverlayInfo = ShowOverlayInfoMenuItem.IsChecked;

            var tab = new TabViewItem
            {
                Header = "New Tab",
                Content = view,
                IconSource = new SymbolIconSource { Symbol = Symbol.Globe },
                HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch,
                VerticalContentAlignment = Microsoft.UI.Xaml.VerticalAlignment.Stretch,
            };

            view.TitleChanged += title => DispatcherQueue.TryEnqueue(() =>
            {
                tab.Header = title;
            });

            view.StatusTextChanged += status => DispatcherQueue.TryEnqueue(() =>
            {
                if (ActiveTab == view)
                {
                    StatusText.Text = status;
                }
            });

            view.FullscreenModeChanged += fullscreen =>
            {
                DispatcherQueue.TryEnqueue(() =>
                {
                    if (fullscreen)
                    {
                        view.DetachControl();
                        FullscreenHost.Children.Add(view.Control);
                        FullscreenHost.Visibility = Visibility.Visible;
                        BrowserTabs.Visibility = Visibility.Collapsed;
                    }
                    else
                    {
                        FullscreenHost.Children.Clear();
                        view.ReattachControl();
                        FullscreenHost.Visibility = Visibility.Collapsed;
                        BrowserTabs.Visibility = Visibility.Visible;
                    }

                    DispatcherQueue.TryEnqueue(() => view.FocusBrowserContent());
                });
            };

            BrowserTabs.TabItems.Add(tab);
            BrowserTabs.SelectedItem = tab;

            AppendActivity("[DemoDI] New tab via app.CreateBrowser()");
        }

        private void CloseTab(TabViewItem tab)
        {
            BrowserTabs.TabItems.Remove(tab);
            (tab.Content as BrowserTabView)?.Dispose();
        }

        private void OnTabViewAddTabButtonClick(TabView sender, object args) => CreateNewTab();

        private void OnTabViewTabCloseRequested(TabView sender, TabViewTabCloseRequestedEventArgs args)
        {
            CloseTab(args.Tab);
        }

        private void OnTabViewSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            DispatcherQueue.TryEnqueue(() =>
            {
                ActiveTab?.FocusBrowserContent();
                StatusText.Text = ActiveTab?.LastStatus ?? "";
            });
        }

        private void OnShowOverlayInfoMenuItemClick(object sender, RoutedEventArgs e)
        {
            var isChecked = ShowOverlayInfoMenuItem.IsChecked;
            foreach (var item in BrowserTabs.TabItems)
            {
                if (item is TabViewItem tab && tab.Content is BrowserTabView view)
                {
                    view.Browser.ShowOverlayInfo = isChecked;
                }
            }
        }

        private GridLength _lastActivityLogRowHeight = new GridLength(150);

        private void OnShowActivityLogMenuItemClick(object sender, RoutedEventArgs e)
        {
            var show = ShowActivityLogMenuItem.IsChecked;

            ActivityLogSplitter.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            ActivityList.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

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

        private bool _isDraggingSplitter;
        private double _dragStartPointerY;
        private double _dragStartRowHeight;

        private void OnSplitterPointerPressed(object sender, PointerRoutedEventArgs e)
        {
            _isDraggingSplitter = true;
            _dragStartPointerY = e.GetCurrentPoint(RootGrid).Position.Y;
            _dragStartRowHeight = ActivityLogRow.Height.Value;
            ((UIElement)sender).CapturePointer(e.Pointer);
        }

        private void OnSplitterPointerMoved(object sender, PointerRoutedEventArgs e)
        {
            if (!_isDraggingSplitter)
            {
                return;
            }

            var currentY = e.GetCurrentPoint(RootGrid).Position.Y;
            var delta = currentY - _dragStartPointerY;
            var newHeight = Math.Max(0, _dragStartRowHeight - delta);
            ActivityLogRow.Height = new GridLength(newHeight);
        }

        private void OnSplitterPointerReleased(object sender, PointerRoutedEventArgs e)
        {
            _isDraggingSplitter = false;
            ((UIElement)sender).ReleasePointerCapture(e.Pointer);
        }

        private void OnNewTabMenuItemClick(object sender, RoutedEventArgs e) => CreateNewTab();

        private void OnCloseTabMenuItemClick(object sender, RoutedEventArgs e)
        {
            if (BrowserTabs.SelectedItem is TabViewItem tab)
            {
                CloseTab(tab);
            }
        }

        private void OnOpenDevToolsMenuItemClick(object sender, RoutedEventArgs e) => ActiveTab?.OpenDevTools();
    }

    public interface IActivityLog
    {
        void Log(string message);
    }

    internal sealed class WindowActivityLog : IActivityLog
    {
        private readonly Microsoft.UI.Dispatching.DispatcherQueue _dispatcherQueue;
        private readonly Action<string> _append;

        public WindowActivityLog(Window window, Action<string> append)
        {
            _dispatcherQueue = window.DispatcherQueue;
            _append = append;
        }

        public void Log(string message)
        {
            if (_dispatcherQueue.HasThreadAccess)
            {
                _append(message);
            }
            else
            {
                _dispatcherQueue.TryEnqueue(() => _append(message));
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
