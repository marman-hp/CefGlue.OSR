using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Interop;
using System.Windows.Threading;
using Xilium.CefGlue.Common.Helpers;
using Xilium.CefGlue.Common.Platform;
using Xilium.CefGlue.WPF.Common;

namespace Xilium.CefGlue.WPF.Platform
{
    public class CefControl<TControl> : ITarget where TControl : BaseCefControl
    {
        protected readonly TControl _control;

        private ExtendedWpfNativeControlHost _nativeControl;

        public event Action GotFocus;
        public event Action<CefSize> SizeChanged;

        public CefControl(TControl control)
        {
            _control = control;

            control.GotKeyboardFocus += OnGotFocus;
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

            _control.Focus();
        }

        private void FireSizeChanged()
        {
            if (_control.RenderSize.Width > 0 && _control.RenderSize.Height > 0)
            {
                SizeChanged?.Invoke(new CefSize((int)_control.RenderSize.Width, (int)_control.RenderSize.Height));
            }
        }

        public IntPtr? GetHostViewHandle(int initialWidth, int initialHeight)
        {
            var window = Window.GetWindow(_control);
            if (window != null)
            {
                return new WindowInteropHelper(window).Handle;
            }

            return IntPtr.Zero;
        }

        public void OpenContextMenu(IEnumerable<MenuEntry> menuEntries, int x, int y, CefRunContextMenuCallback callback)
        {
            _control.Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                new Action(() =>
                {
                    _control.IsContextMenuOpen = true;

                    var menu = new ContextMenu
                    {
                        Focusable = false,
                    };
                    var menuLock = new object();
                    var isHandled = false;

                    foreach (var menuEntry in menuEntries)
                    {
                        if (menuEntry.IsSeparator)
                        {
                            menu.Items.Add(new Separator());
                        }
                        else
                        {
                            var menuItem = new MenuItem()
                            {
                                Header = menuEntry.Label.Replace("&", "_"),
                                IsEnabled = menuEntry.IsEnabled,
                                IsChecked = menuEntry.IsChecked ?? false,
                                IsCheckable = menuEntry.IsChecked != null,
                            };
                            var commandId = menuEntry.CommandId;
                            menuItem.Click += delegate
                            {
                                lock (menuLock)
                                {
                                    if (isHandled) return;
                                    isHandled = true;
                                }
                                callback.Continue(commandId, CefEventFlags.None);
                            };
                            menu.Items.Add(menuItem);
                        }
                    }

                    menu.Closed += delegate {
                        _control.Dispatcher.BeginInvoke(
                            DispatcherPriority.Input,
                            new Action(() =>
                            {
                                lock (menuLock)
                                {
                                    if (!isHandled)
                                    {
                                        callback.Cancel();
                                    }
                                }
                                _control.ContextMenu = null;

                                _control.IsContextMenuOpen = false;
                                _control.Focus();
                            }));
                    };

                    _control.ContextMenu = menu;
                    menu.HorizontalOffset = x;
                    menu.VerticalOffset = y;
                    menu.Placement = PlacementMode.Relative;
                    menu.PlacementTarget = _control;
                    menu.IsOpen = true;
                })
            );
        }

        public void CloseContextMenu()
        {
            _control.Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                new Action(() =>
                {
                    _control.ContextMenu = null;
                })
            );
        }

        public virtual bool SetCursor(IntPtr cursorHandle, CefCursorType cursorType)
        {
            return false;
        }

        public void InitializeRender(IntPtr browserHandle)
        {
            _control.Dispatcher.BeginInvoke(
                DispatcherPriority.Input,
                new Action(() =>
                {
                    _nativeControl = new ExtendedWpfNativeControlHost(browserHandle);
                    _control.Content = _nativeControl;
                })
            );
        }

        public void DestroyRender()
        {
            _nativeControl.DestroyWindow();
            _control.Dispatcher.BeginInvoke(
                DispatcherPriority.Normal,
                new Action(() =>
                {
                    _nativeControl.Dispose();
                })
            );
        }
    }
}
