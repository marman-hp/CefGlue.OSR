using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Xilium.CefGlue.WinForms.Ime
{
    internal sealed class TsfImeController : IDisposable
    {
        private readonly IntPtr _hwnd;
        private ITfThreadMgr _threadMgr;
        private ITfDocumentMgr _docMgr;
        private ITfContext _context;
        private uint _clientId;
        private uint _textEditSinkCookie;
        private TsfTextStore _store;
        private bool _active;
        private bool _disposed;

        public event Action<string, int, TsfClause[]> CompositionUpdated;
        public event Action<string> CompositionCommitted;

        internal ITfContext Context => _context;
        internal ITfThreadMgr ThreadMgr => _threadMgr;
        internal uint ClientId => _clientId;
        internal TsfTextStore Store => _store;

        public TsfImeController(IntPtr hwnd, Func<int, int, TsfRect?> getCompositionExt, Func<TsfRect?> getScreenExt, SynchronizationContext ui)
        {
            _hwnd = hwnd;

            _threadMgr = Tsf.CreateInstance<ITfThreadMgr>(Tsf.CLSID_TF_ThreadMgr, Tsf.IID_ITfThreadMgr);
            Check(_threadMgr.Activate(out _clientId), nameof(ITfThreadMgr.Activate));
            Check(_threadMgr.CreateDocumentMgr(out _docMgr), nameof(ITfThreadMgr.CreateDocumentMgr));

            _store = new TsfTextStore(hwnd, getCompositionExt, getScreenExt, ui);
            _store.CompositionUpdated += (t, c, clauses) => CompositionUpdated?.Invoke(t, c, clauses);
            _store.CompositionCommitted += t => CompositionCommitted?.Invoke(t);

            var punk = Tsf.GetComPointer(_store);
            try
            {
                Check(_docMgr.CreateContext(_clientId, 0, punk, out _context, out _), nameof(ITfDocumentMgr.CreateContext));
            }
            finally
            {
                Marshal.Release(punk);
            }

            _store.AttachContext(_context);

            var sinkPunk = Tsf.GetComPointer(_store);
            try
            {
                Check(((ITfSource)(object)_context).AdviseSink(Tsf.IID_ITfTextEditSink, sinkPunk, out _textEditSinkCookie), "ITfSource.AdviseSink(ITfTextEditSink)");
            }
            finally
            {
                Marshal.Release(sinkPunk);
            }

            Check(_docMgr.Push(_context), nameof(ITfDocumentMgr.Push));
        }

        private static void Check(int hr, string what)
        {
            if (hr < 0)
            {
                throw new COMException($"TSF {what} failed", hr);
            }
        }

        public void SetActive(bool active, bool controlHasFocus)
        {
            if (_disposed || active == _active)
            {
                return;
            }

            _active = active;

            if (!active)
            {
                _store.AbandonComposition();
            }

            _threadMgr.AssociateFocus(_hwnd, active ? _docMgr : null, out _);

            if (controlHasFocus)
            {
                _threadMgr.SetFocus(active ? _docMgr : null);
            }
        }

        public bool IsComposing => _store?.IsComposing == true;

        public void ReassertFocus()
        {
            if (_disposed || !_active)
            {
                return;
            }

            _threadMgr.SetFocus(_docMgr);
        }

        public void NotifyLayoutChanged() => _store?.NotifyLayoutChanged();

        public void AbandonComposition() => _store?.AbandonComposition();

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            try
            {
                if (_context != null && _textEditSinkCookie != 0)
                {
                    ((ITfSource)(object)_context).UnadviseSink(_textEditSinkCookie);
                }

                _threadMgr?.AssociateFocus(_hwnd, null, out _);
                _docMgr?.Pop(1  );
                _threadMgr?.Deactivate();
            }
            catch (COMException)
            {
            }

            _context = null;
            _docMgr = null;
            _threadMgr = null;
            _store = null;
        }
    }
}
