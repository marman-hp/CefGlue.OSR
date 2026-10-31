using System;
using System.Collections.Generic;
using Microsoft.UI.Input;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Controls.Primitives;
using WinRT.Interop;
using Xilium.CefGlue.Common.Helpers;
using Xilium.CefGlue.Common.Platform;
using Xilium.CefGlue.WinUI.Common;

namespace Xilium.CefGlue.WinUI.Platform
{
    internal class CefControl<TControl> : ITarget where TControl : BaseCefControl
    {
        protected readonly TControl _control;

        private MenuFlyout _currentContextMenu;

        public event Action GotFocus;
        public event Action<CefSize> SizeChanged;

        public CefControl(TControl control)
        {
            _control = control;

            control.GotFocus += OnGotFocus;
            control.SizeChanged += OnControlSizeChanged;

            if (control.UseSafeResize)
            {
                control.SafeResize += OnSafeResize;
            }
        }

        private void OnGotFocus(object sender, RoutedEventArgs e)
        {
            GotFocus?.Invoke();
        }

        protected virtual float ContextMenuScaleFactor => 1f;

        private void OnControlSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (_control.UseSafeResize && _control.IsResizing)
            {
                return;
            }

            FireSizeChanged();
        }

        private void OnSafeResize()
        {
            FireSizeChanged();

            _control.Focus(FocusState.Programmatic);
        }

        private void FireSizeChanged()
        {
            if (_control.ActualWidth > 0 && _control.ActualHeight > 0)
            {
                SizeChanged?.Invoke(new CefSize((int)_control.ActualWidth, (int)_control.ActualHeight));
            }
        }

        public IntPtr? GetHostViewHandle(int initialWidth, int initialHeight)
        {
            if (_control.HostWindow == null)
            {
                return IntPtr.Zero;
            }

            return WindowNative.GetWindowHandle(_control.HostWindow);
        }

        public void OpenContextMenu(IEnumerable<MenuEntry> menuEntries, int x, int y, CefRunContextMenuCallback callback)
        {
            _control.DispatcherQueue.TryEnqueue(() =>
            {
                _currentContextMenu?.Hide();
                _currentContextMenu = null;

                _control.IsContextMenuOpen = true;

                var menu = new MenuFlyout();
                var menuLock = new object();
                var isHandled = false;

                foreach (var entry in menuEntries)
                {
                    if (entry.IsSeparator)
                    {
                        menu.Items.Add(new MenuFlyoutSeparator());
                        continue;
                    }

                    var (text, accessKey) = SplitMnemonic(entry.Label);
                    var item = new MenuFlyoutItem
                    {
                        Text = text,
                        IsEnabled = ResolveIsEnabled(entry),
                    };
                    if (!string.IsNullOrEmpty(accessKey))
                    {
                        item.AccessKey = accessKey;
                    }
                    var commandId = entry.CommandId;
                    item.Click += (_, _) =>
                    {
                        lock (menuLock)
                        {
                            if (isHandled) return;
                            isHandled = true;
                        }
                        callback.Continue(commandId, CefEventFlags.None);
                    };
                    menu.Items.Add(item);
                }

                menu.Closed += (_, _) =>
                {
                    lock (menuLock)
                    {
                        if (!isHandled)
                        {
                            callback.Cancel();
                        }
                    }

                    if (_currentContextMenu == menu)
                    {
                        _currentContextMenu = null;
                    }

                    _control.DispatcherQueue.TryEnqueue(() =>
                    {
                        _control.IsContextMenuOpen = false;
                        _control.Focus(FocusState.Programmatic);
                    });
                };

                _currentContextMenu = menu;
                var scale = ContextMenuScaleFactor;
                menu.ShowAt(_control, new FlyoutShowOptions
                {
                    Position = new Windows.Foundation.Point(x * scale, y * scale),
                });
            });
        }

        private bool ResolveIsEnabled(MenuEntry entry)
        {
            var browser = _control.Browser;
            if (browser == null)
            {
                return entry.IsEnabled;
            }

            if (entry.CommandId == (int)CefMenuId.Back)
            {
                return entry.IsEnabled && browser.CanGoBack;
            }

            if (entry.CommandId == (int)CefMenuId.Forward)
            {
                return entry.IsEnabled && browser.CanGoForward;
            }

            return entry.IsEnabled;
        }

        private static (string Text, string AccessKey) SplitMnemonic(string label)
        {
            if (string.IsNullOrEmpty(label) || label.IndexOf('&') < 0)
            {
                return (label, null);
            }

            var builder = new System.Text.StringBuilder(label.Length);
            string accessKey = null;

            for (var i = 0; i < label.Length; i++)
            {
                if (label[i] == '&' && i + 1 < label.Length)
                {
                    if (label[i + 1] == '&')
                    {
                        builder.Append('&');
                        i++;
                        continue;
                    }

                    accessKey ??= label[i + 1].ToString();
                    continue;
                }

                builder.Append(label[i]);
            }

            return (builder.ToString(), accessKey);
        }

        public void CloseContextMenu()
        {
            _control.DispatcherQueue.TryEnqueue(() =>
            {
                _currentContextMenu?.Hide();
            });
        }

        public virtual bool SetCursor(IntPtr cursorHandle, CefCursorType cursorType)
        {
            var shape = CursorMapper.GetShape(cursorType);

            _control.DispatcherQueue.TryEnqueue(() =>
            {
                _control.ApplyCursor(InputSystemCursor.Create(shape));
            });

            return true;
        }

        public void InitializeRender(IntPtr browserHandle)
        {
        }

        public void DestroyRender()
        {
        }
    }
}
