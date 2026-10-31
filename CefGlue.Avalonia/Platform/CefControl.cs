using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives.PopupPositioning;
using Avalonia.Input;
using Avalonia.Platform;
using Avalonia.Threading;
using Xilium.CefGlue.Avalonia.Common;
using Xilium.CefGlue.Avalonia.Platform.Windows;
using Xilium.CefGlue.Common.Helpers;

namespace Xilium.CefGlue.Avalonia.Platform
{
    internal class CefControl<TControl> : Xilium.CefGlue.Common.Platform.ITarget where TControl : BaseCefControl
    {
        private static IPlatformHandle _hostWindowPlatformHandle;

        private IHandleHolder _browserView;

        protected readonly TControl _control;

        public event Action GotFocus;
        public event Action<CefSize> SizeChanged;

        public CefControl(TControl control)
        {
            _control = control;

            _control.GotFocus += OnGotFocus;
            _control.LayoutUpdated += OnLayoutUpdated;

            if (control.UseSafeResize)
            {
                control.SafeResize += OnSafeResize;
            }
        }

        private void OnLayoutUpdated(object sender, EventArgs e)
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
            SizeChanged?.Invoke(new CefSize((int)_control.Bounds.Width, (int)_control.Bounds.Height));
        }

        private void OnGotFocus(object sender, FocusChangedEventArgs e)
        {
            GotFocus?.Invoke();
        }

        protected IPlatformHandle GetHostWindowPlatformHandle()
        {
            Dispatcher.UIThread.VerifyAccess();
            if (_hostWindowPlatformHandle == null)
            {
                _hostWindowPlatformHandle = new Window().TryGetPlatformHandle();
            }
            return _hostWindowPlatformHandle;
        }

        public virtual IntPtr? GetHostViewHandle(int initialWidth, int initialHeight)
        {
            return GetHostWindowPlatformHandle()?.Handle;
        }

        public void OpenContextMenu(IEnumerable<MenuEntry> menuEntries, int x, int y, CefRunContextMenuCallback callback)
        {
            Dispatcher.UIThread.Post(
                () =>
                {
                    _control.IsContextMenuOpen = true;

                    var menu = new ContextMenu
                    {
                        Focusable = false,
                    };
                    var menuLock = new object();
                    var isHandled = false;

                    menu.Items.Clear();

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

                    menu.Closed += delegate
                    {
                        Dispatcher.UIThread.Post(() =>
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
                        }, DispatcherPriority.Input);
                    };

                    _control.ContextMenu = menu;

                    menu.PlacementAnchor = PopupAnchor.TopLeft;
                    menu.PlacementGravity = PopupGravity.BottomRight;
                    menu.Placement = PlacementMode.AnchorAndGravity;
                    menu.PlacementRect = new Rect(x, y, 1, 1);
                    menu.Open(_control);
                },
                DispatcherPriority.Input);
        }

        public void CloseContextMenu()
        {
            Dispatcher.UIThread.Post(
               () =>
               {
                   _control.ContextMenu?.Close();
                   _control.ContextMenu = null;
               },
               DispatcherPriority.Input);
        }

        public virtual bool SetCursor(IntPtr cursorHandle, CefCursorType cursorType)
        {
            return false;
        }

        public void InitializeRender(IntPtr browserHandle)
        {
            _browserView = new HostWindow(browserHandle);

            Dispatcher.UIThread.Post(() =>
            {
                var browserView = _browserView;
                if (browserView != null)
                {
                    lock (browserView)
                    {
                        if (_browserView != null)
                        {
                            _control.Content = new ExtendedAvaloniaNativeControlHost(_browserView.Handle);
                        }
                    }
                }
            });
        }

        public void DestroyRender()
        {
            var browserView = _browserView;
            if (browserView != null)
            {
                lock (browserView)
                {
                    if (_browserView != null)
                    {
                        _browserView.Dispose();
                        _browserView = null;
                    }
                }
            }
        }

    }
}
