using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows.Forms;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Helpers;
using Xilium.CefGlue.Common.Platform;
using Xilium.CefGlue.WinForms.Common;

namespace Xilium.CefGlue.WinForms.Platform
{
    public class CefControl<TControl> : ITarget where TControl : BaseCefControl
    {
        protected readonly TControl _control;

        private IntPtr _browserHandle;

        private ContextMenuStrip _contextMenu;

        public event Action GotFocus;

        public event Action<CefSize> SizeChanged;

        public CefControl(TControl control)
        {
            _control = control;

            control.NotifyMouseDown += OnNotifyMouseDown;
            _control.GotFocus += Control_GotFocus;

            if( _control.UseSafeResize )
              _control.SafeResize += Control_Resize;
            else
              _control.Resize += Control_Resize;

            _control.Move += _control_Move;
            _control.HandleDestroyed += Control_HandleDestroyed;

        }

        private void OnNotifyMouseDown(object o, MouseEventArgs args)
        {
             CloseContextMenu();
        }

        bool ispopupwindow => _control?.Browser?.IsPopup ?? false;

        private void Control_GotFocus(object sender, EventArgs e)
        {

            if (!ispopupwindow)
               GotFocus?.Invoke();
        }

        public IntPtr? GetHostViewHandle(int initialWidth, int initialHeight)
        {
            IntPtr handle = IntPtr.Zero;
            if (_control.InvokeRequired)
               return _control.Invoke(new Func<IntPtr>(() => _control.Handle  ));
            else
                handle = _control.Handle;
            return handle;
        }

        public virtual void InitializeRender( IntPtr browserHandle)
        {
            _browserHandle = browserHandle;

            if (_browserHandle != nint.Zero)
            {
                _control.BeginInvoke(()=> {
                    _control.CreateControl();
                    NativeMethods.SetParent(browserHandle, _control.Handle);
                    NativeMethods.ShowWindow(browserHandle, 5);
                });

            }
        }

        public virtual void DestroyRender()
        {
            if (_browserHandle == IntPtr.Zero)
                return;

            if (_control.InvokeRequired)
            {
                _control.Invoke(new MethodInvoker(DestroyRender));

                return;
            }

            try
            {
                NativeMethods.DestroyWindow(_browserHandle);
            }
            catch
            {
            }

            _browserHandle = IntPtr.Zero;
        }

        private readonly object _menuLock = new object();
        public void OpenContextMenu(IEnumerable<MenuEntry> menuEntries, int x, int y, CefRunContextMenuCallback callback)
        {
            if (_control.InvokeRequired)
            {
                _control.BeginInvoke(new Action(() =>
                {
                    OpenContextMenu(menuEntries, x, y, callback);
                }));
                return;
            }

            lock (_menuLock)
            {
                CloseContextMenu();

                _contextMenu = new ContextMenuStrip();
                var isHandled = false;

                foreach (var item in menuEntries)
                {
                    if (item.IsSeparator)
                    {
                        _contextMenu.Items.Add(new ToolStripSeparator());
                    }
                    else
                    {
                        var menuItem = new ToolStripMenuItem(item.Label);
                        menuItem.Enabled = item.IsEnabled;
                        var commandId = item.CommandId;

                        menuItem.Click += (s, e) =>
                        {
                            lock (_menuLock)
                            {
                                if (isHandled) return;
                                isHandled = true;
                            }
                            callback.Continue(commandId, CefEventFlags.None);
                        };

                        _contextMenu.Items.Add(menuItem);
                    }
                }

                _contextMenu.Closed += (s, e) =>
                {
                    _control.BeginInvoke(new Action(() =>
                    {
                        lock (_menuLock)
                        {
                            if (!isHandled)
                            {
                                callback.Cancel();
                            }
                            _contextMenu = null;
                        }
                    }));
                };

                _contextMenu?.Show(_control, x, y);
            }
        }

        public virtual void CloseContextMenu()
        {
            if (_control.InvokeRequired)
            {
                _control.Invoke(() => CloseContextMenu());
                return;
            }

            lock (_menuLock)
            {
                if (_contextMenu == null) return;

                var menu = _contextMenu;
                _contextMenu = null;
                menu.Close();
                menu.Dispose();
            }
        }

        public virtual bool SetCursor(IntPtr cursorHandle, CefCursorType cursorType)
        {

            return false;
        }

        private void Control_Resize(object sender, EventArgs e)
        {

            SizeChanged?.Invoke(new CefSize(_control.Width, _control.Height));

            ResizeBrowser();

            if (_control.UseSafeResize)
            {
                _control.Focus();
            }
        }

        private void _control_Move(object sender, EventArgs e)
        {
            if (_control.UseSafeResize)
            {
                _control.Browser?.GetHost()?.NotifyMoveOrResizeStarted();
            }
        }

        private void Control_HandleDestroyed(object sender, EventArgs e)
        {
            DestroyRender();
        }

        protected virtual void ResizeBrowser()
        {

            if (_browserHandle == IntPtr.Zero )
            {
                return;
            }

            NativeMethods.SetWindowPos( _browserHandle, IntPtr.Zero,0,0, _control.Width, _control.Height,  0x0040);
        }
    }

}