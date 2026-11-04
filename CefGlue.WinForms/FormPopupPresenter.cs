using System;
using System.Drawing;
using System.Windows.Forms;
using Xilium.CefGlue.Common.Coordinators;

namespace Xilium.CefGlue.WinForms
{
    /// <summary>
    /// Default <see cref="IPopupPresenter{TBrowser}"/> for WinForms: shows a window.open popup in its own Form, owned by
    /// the opener's form, at the size the page asked for (808x600 when it didn't).
    /// </summary>
    public sealed class FormPopupPresenter : IPopupPresenter<CefGlueBrowser>
    {
        public IPopupHost Show(PopupRequest<CefGlueBrowser> request)
        {
            var popup = request.Popup;
            var opener = request.Opener;

            var form = new Form
            {
                Owner = opener.Control?.FindForm(),
                Size = request.Width is int width && request.Height is int height ? new Size(width, height) : new Size(808, 600),
            };
            form.CreateControl();

            popup.Control.Dock = DockStyle.Fill;
            form.Controls.Add(popup.Control);

            var host = new FormPopupHost(form);
            form.Show();
            form.Activate();
            return host;
        }

        private sealed class FormPopupHost : IPopupHost
        {
            private readonly Form _form;
            private bool _closed;

            public FormPopupHost(Form form)
            {
                _form = form;
                // FormClosing, not FormClosed: the browser is closed while its control still exists.
                _form.FormClosing += (s, e) =>
                {
                    if (!e.Cancel)
                    {
                        RaiseClosed();
                    }
                };
            }

            public event Action Closed;

            public void Close()
            {
                if (!_form.IsDisposed)
                {
                    _form.Close();
                }
            }

            private void RaiseClosed()
            {
                if (_closed)
                {
                    return;
                }

                _closed = true;
                Closed?.Invoke();
            }
        }
    }
}
