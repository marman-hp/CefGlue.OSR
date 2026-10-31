using System;
using System.Windows.Forms;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.WinForms.Platform;

namespace Xilium.CefGlue.WinForms.Common
{
    public abstract partial class BaseCefControl : UserControl
    {
        public Action<Object, MouseEventArgs> GenericMouseDown;
        public Action<Object, MouseEventArgs> GenericMouseUp;
        public Action<object, MouseEventArgs> NotifyMouseDown;

        public Action<object, EventArgs> SafeResize;
        public Action<object, FormWindowState> SafeOnLayaoutState;
        private IntPtr _currentCursorHandler = IntPtr.Zero;

        public BaseCefControl()
        {
            InitializeComponent();

            SetStyle(ControlStyles.AllPaintingInWmPaint |
            ControlStyles.UserPaint |
            ControlStyles.Selectable, true);

            UpdateStyles();
            BorderStyle = System.Windows.Forms.BorderStyle.None;
            TabStop = true;
            UseSafeResize = false;

        }

        public virtual bool IsPopupSelect => false;

        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public bool UseSafeResize { get; set; }

        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        public System.Drawing.Size? PinnedSize { get; set; }

        public virtual void BeginResize() { }

        public virtual void EndResize() { }

        private CefBrowser _browser;

        public void AttachBrowser(CefBrowser browser) => _browser= browser;

        public CefBrowser Browser => _browser;

        public bool OSREnabled => CefRuntimeLoader.IsOSREnabled;

        public void SetCursorHandler(IntPtr cursorHandler) => _currentCursorHandler= cursorHandler;

        public IntPtr CursorHandler => _currentCursorHandler;

        [System.ComponentModel.Browsable(false)]
        [System.ComponentModel.DesignerSerializationVisibility(System.ComponentModel.DesignerSerializationVisibility.Hidden)]
        internal bool TsfImeAttached { get; set; }

        protected override void WndProc(ref Message m)
        {
            const int WM_PARENTNOTIFY = 0x0210;
            const int WM_IME_STARTCOMPOSITION = 0x010D;
            const int WM_IME_ENDCOMPOSITION = 0x010E;
            const int WM_IME_COMPOSITION = 0x010F;
            const int WM_IME_CHAR = 0x0286;

            if (TsfImeAttached &&
                (m.Msg == WM_IME_STARTCOMPOSITION || m.Msg == WM_IME_ENDCOMPOSITION ||
                 m.Msg == WM_IME_COMPOSITION || m.Msg == WM_IME_CHAR))
            {
                m.Result = IntPtr.Zero;
                return;
            }

            if (m.Msg == WM_PARENTNOTIFY)
            {
                int childMsg = m.WParam.ToInt32() & 0xFFFF;
                if (childMsg == NativeMethods.WM_LBUTTONDOWN || childMsg == NativeMethods.WM_RBUTTONDOWN)
                {
                    int x = (int)(short)(m.LParam.ToInt32() & 0xFFFF);
                    int y = (int)(short)((m.LParam.ToInt32() >> 16) & 0xFFFF);
                    var btn = childMsg == NativeMethods.WM_LBUTTONDOWN ? MouseButtons.Left : MouseButtons.Right;
                    NotifyMouseDown?.Invoke(this, new MouseEventArgs(btn, 1, x, y, 0));
                }
            }

            base.WndProc(ref m);
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (OSREnabled)
            {
                switch (keyData & Keys.KeyCode)
                {
                    case Keys.Tab:
                    case Keys.Left:
                    case Keys.Right:
                    case Keys.Up:
                    case Keys.Down:
                    case Keys.Escape:
                        return true;
                }
            }

            return base.IsInputKey(keyData);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (OSREnabled)
                GenericMouseDown?.Invoke(this, e);
            else
                base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            if (OSREnabled)
                GenericMouseUp?.Invoke(this, e);
            else
                base.OnMouseUp(e);
        }

    }
}
