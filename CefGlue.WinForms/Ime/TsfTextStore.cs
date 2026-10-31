using System;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Threading;

namespace Xilium.CefGlue.WinForms.Ime
{
    [GeneratedComClass]
    internal sealed unsafe partial class TsfTextStore : ITextStoreACP, ITfContextOwnerCompositionSink, ITfTextEditSink
    {
        private const uint ViewCookie = 1;

        private readonly IntPtr _hwnd;
        private readonly Func<int, int, TsfRect?> _getCompositionExt;
        private readonly Func<TsfRect?> _getScreenExt;
        private readonly SynchronizationContext _ui;

        private string _text = "";
        private int _selStart;
        private int _selEnd;

        private bool _composing;
        private int _compStart;
        private int _committedLen;

        private ITextStoreACPSink _sink;
        private uint _sinkMask;
        private uint _lockFlags;
        private uint _pendingLock;

        private bool _changedInLock;
        private bool _endedInLock;
        private bool _layoutPending;

        public event Action<string, int, TsfClause[]> CompositionUpdated;
        public event Action<string> CompositionCommitted;

        public bool IsComposing => _composing;

        private ITfContext _context;
        private TsfClause[] _clauses;

        public TsfDisplayAttributeReader AttributeReader { get; } = new();

        public void AttachContext(ITfContext context) => _context = context;

        public TsfTextStore(IntPtr hwnd, Func<int, int, TsfRect?> getCompositionExt, Func<TsfRect?> getScreenExt, SynchronizationContext ui)
        {
            _hwnd = hwnd;
            _getCompositionExt = getCompositionExt;
            _getScreenExt = getScreenExt;
            _ui = ui;
        }

        private bool HasReadLock => (_lockFlags & Tsf.TS_LF_READ) != 0;
        private bool HasWriteLock => (_lockFlags & 0x4) != 0;

        private string CompositionText() => _compStart >= _text.Length ? "" : _text.Substring(_compStart);

        public void NotifyLayoutChanged()
        {
            if (_sink == null || (_sinkMask & Tsf.TS_AS_LAYOUT_CHANGE) == 0)
            {
                return;
            }

            if (_lockFlags != 0)
            {
                _layoutPending = true;
                return;
            }

            _sink.OnLayoutChange(Tsf.TS_LC_CHANGE, ViewCookie);
        }

        public void AbandonComposition()
        {
            if (!_composing)
            {
                return;
            }

            _composing = false;
            _endedInLock = false;
            _changedInLock = false;
            _clauses = null;
            CompositionUpdated?.Invoke("", 0, null);
            ScheduleReset();
        }

        private void ScheduleReset() => _ui.Post(_ => ResetDocument(), null);

        private void ResetDocument()
        {
            if (_composing || _lockFlags != 0 || (_text.Length == 0 && _selStart == 0 && _selEnd == 0))
            {
                return;
            }

            var change = new TS_TEXTCHANGE { acpStart = 0, acpOldEnd = _text.Length, acpNewEnd = 0 };
            var hadText = _text.Length != 0;
            _text = "";
            _selStart = _selEnd = 0;
            _committedLen = 0;
            _compStart = 0;

            if (_sink == null)
            {
                return;
            }

            if (hadText && (_sinkMask & Tsf.TS_AS_TEXT_CHANGE) != 0)
            {
                _sink.OnTextChange(0, in change);
            }

            if ((_sinkMask & Tsf.TS_AS_SEL_CHANGE) != 0)
            {
                _sink.OnSelectionChange();
            }
        }

        public int AdviseSink(in Guid riid, IntPtr punk, uint dwMask)
        {
            if (riid != Tsf.IID_ITextStoreACPSink || punk == IntPtr.Zero)
            {
                return Tsf.E_INVALIDARG;
            }

            var sink = (ITextStoreACPSink)Tsf.ComWrappers.GetOrCreateObjectForComInstance(punk, CreateObjectFlags.None);
            if (_sink != null)
            {
                _sinkMask = dwMask;
                return Tsf.S_OK;
            }

            _sink = sink;
            _sinkMask = dwMask;
            return Tsf.S_OK;
        }

        public int UnadviseSink(IntPtr punk)
        {
            if (_sink == null)
            {
                return Tsf.CONNECT_E_ADVISELIMIT;
            }

            _sink = null;
            _sinkMask = 0;
            return Tsf.S_OK;
        }

        public int RequestLock(uint dwLockFlags, out int phrSession)
        {
            if (_sink == null)
            {
                phrSession = Tsf.E_UNEXPECTED;
                return Tsf.E_UNEXPECTED;
            }

            if (_lockFlags != 0)
            {
                if ((dwLockFlags & Tsf.TS_LF_SYNC) != 0)
                {
                    phrSession = Tsf.TS_E_SYNCHRONOUS;
                    return Tsf.S_OK;
                }

                _pendingLock = dwLockFlags;
                phrSession = Tsf.TS_S_ASYNC;
                return Tsf.S_OK;
            }

            _lockFlags = dwLockFlags & Tsf.TS_LF_READWRITE;
            phrSession = _sink.OnLockGranted(dwLockFlags);
            _lockFlags = 0;

            while (_pendingLock != 0)
            {
                var pending = _pendingLock;
                _pendingLock = 0;
                _lockFlags = pending & Tsf.TS_LF_READWRITE;
                _sink.OnLockGranted(pending);
                _lockFlags = 0;
            }

            FlushAfterLock();
            return Tsf.S_OK;
        }

        private void FlushAfterLock()
        {
            if (_endedInLock)
            {
                _endedInLock = false;
                _changedInLock = false;

                var committed = CompositionText();
                _clauses = null;
                if (committed.Length == 0)
                {
                    CompositionUpdated?.Invoke("", 0, null);
                }
                else
                {
                    CompositionCommitted?.Invoke(committed);
                }

                _committedLen = _text.Length;
                ScheduleReset();
            }
            else if (_composing)
            {
                if (_changedInLock)
                {
                    _changedInLock = false;
                    RaiseCompositionUpdate();
                }
            }
            else if (_changedInLock)
            {
                _changedInLock = false;
                if (_text.Length > _committedLen)
                {
                    CompositionCommitted?.Invoke(_text.Substring(_committedLen));
                    _committedLen = _text.Length;
                    ScheduleReset();
                }
            }

            if (_layoutPending)
            {
                _layoutPending = false;
                NotifyLayoutChanged();
            }
        }

        private void RaiseCompositionUpdate()
        {
            var text = CompositionText();
            var clauses = _clauses;

            if (clauses != null && (clauses.Length == 0 || clauses[^1].End != text.Length))
            {
                clauses = null;
            }

            CompositionUpdated?.Invoke(text, Math.Clamp(_selEnd - _compStart, 0, text.Length), clauses);
        }

        public int GetStatus(out TS_STATUS pdcs)
        {
            pdcs = new TS_STATUS { dwDynamicFlags = 0, dwStaticFlags = Tsf.TS_SS_NOHIDDENTEXT };
            return Tsf.S_OK;
        }

        public int QueryInsert(int acpTestStart, int acpTestEnd, uint cch, out int pacpResultStart, out int pacpResultEnd)
        {
            pacpResultStart = Math.Clamp(acpTestStart, 0, _text.Length);
            pacpResultEnd = Math.Clamp(acpTestEnd, pacpResultStart, _text.Length);
            return Tsf.S_OK;
        }

        public int GetSelection(uint ulIndex, uint ulCount, TS_SELECTION_ACP* pSelection, out uint pcFetched)
        {
            pcFetched = 0;
            if (!HasReadLock)
            {
                return Tsf.TS_E_NOLOCK;
            }

            if (ulCount == 0)
            {
                return Tsf.S_OK;
            }

            if (ulIndex != Tsf.TS_DEFAULT_SELECTION && ulIndex != 0)
            {
                return Tsf.E_INVALIDARG;
            }

            *pSelection = new TS_SELECTION_ACP { acpStart = _selStart, acpEnd = _selEnd, ase = 2  , fInterimChar = 0 };
            pcFetched = 1;
            return Tsf.S_OK;
        }

        public int SetSelection(uint ulCount, TS_SELECTION_ACP* pSelection)
        {
            if (!HasWriteLock)
            {
                return Tsf.TS_E_NOLOCK;
            }

            if (ulCount < 1)
            {
                return Tsf.E_INVALIDARG;
            }

            var start = pSelection->acpStart;
            var end = pSelection->acpEnd;
            if (start < 0 || end < start || end > _text.Length)
            {
                return Tsf.TS_E_INVALIDPOS;
            }

            if (start != _selStart || end != _selEnd)
            {
                _selStart = start;
                _selEnd = end;
                _changedInLock = true;
            }

            return Tsf.S_OK;
        }

        public int GetText(int acpStart, int acpEnd, char* pchPlain, uint cchPlainReq, out uint pcchPlainRet,
                           TS_RUNINFO* prgRunInfo, uint cRunInfoReq, out uint pcRunInfoRet, out int pacpNext)
        {
            pcchPlainRet = 0;
            pcRunInfoRet = 0;
            pacpNext = acpStart;

            if (!HasReadLock)
            {
                return Tsf.TS_E_NOLOCK;
            }

            if (acpEnd == -1)
            {
                acpEnd = _text.Length;
            }

            if (acpStart < 0 || acpStart > _text.Length || acpEnd < acpStart || acpEnd > _text.Length)
            {
                return Tsf.TS_E_INVALIDPOS;
            }

            var count = Math.Min(acpEnd - acpStart, (int)cchPlainReq);
            if (pchPlain != null)
            {
                for (var i = 0; i < count; i++)
                {
                    pchPlain[i] = _text[acpStart + i];
                }
            }

            pcchPlainRet = (uint)count;
            pacpNext = acpStart + count;

            if (prgRunInfo != null && cRunInfoReq > 0 && count > 0)
            {
                prgRunInfo[0] = new TS_RUNINFO { uCount = (uint)count, type = 0   };
                pcRunInfoRet = 1;
            }

            return Tsf.S_OK;
        }

        public int SetText(uint dwFlags, int acpStart, int acpEnd, char* pchText, uint cch, out TS_TEXTCHANGE pChange)
        {
            pChange = default;
            if (!HasWriteLock)
            {
                return Tsf.TS_E_NOLOCK;
            }

            if (acpEnd == -1)
            {
                acpEnd = _text.Length;
            }

            if (acpStart < 0 || acpEnd < acpStart || acpEnd > _text.Length)
            {
                return Tsf.TS_E_INVALIDPOS;
            }

            Replace(acpStart, acpEnd, pchText, (int)cch);
            pChange = new TS_TEXTCHANGE { acpStart = acpStart, acpOldEnd = acpEnd, acpNewEnd = acpStart + (int)cch };
            return Tsf.S_OK;
        }

        public int InsertTextAtSelection(uint dwFlags, char* pchText, uint cch, out int pacpStart, out int pacpEnd, out TS_TEXTCHANGE pChange)
        {
            pChange = default;
            pacpStart = _selStart;
            pacpEnd = _selEnd;

            if ((dwFlags & Tsf.TS_IAS_QUERYONLY) != 0)
            {
                return HasReadLock ? Tsf.S_OK : Tsf.TS_E_NOLOCK;
            }

            if (!HasWriteLock)
            {
                return Tsf.TS_E_NOLOCK;
            }

            var start = _selStart;
            var oldEnd = _selEnd;
            Replace(start, oldEnd, pchText, (int)cch);
            pacpStart = start;
            pacpEnd = start + (int)cch;
            pChange = new TS_TEXTCHANGE { acpStart = start, acpOldEnd = oldEnd, acpNewEnd = start + (int)cch };
            return Tsf.S_OK;
        }

        private void Replace(int start, int end, char* pch, int cch)
        {
            var inserted = cch > 0 && pch != null ? new string(pch, 0, cch) : "";
            _text = string.Concat(_text.AsSpan(0, start), inserted, _text.AsSpan(end));
            _selStart = _selEnd = start + inserted.Length;
            _changedInLock = true;
        }

        public int GetEndACP(out int pacp)
        {
            pacp = _text.Length;
            return HasReadLock ? Tsf.S_OK : Tsf.TS_E_NOLOCK;
        }

        public int GetFormattedText(int acpStart, int acpEnd, out IntPtr ppDataObject) { ppDataObject = IntPtr.Zero; return Tsf.E_NOTIMPL; }
        public int GetEmbedded(int acpPos, in Guid rguidService, in Guid riid, out IntPtr ppunk) { ppunk = IntPtr.Zero; return Tsf.E_NOTIMPL; }
        public int QueryInsertEmbedded(IntPtr pguidService, IntPtr pFormatEtc, out int pfInsertable) { pfInsertable = 0; return Tsf.S_OK; }
        public int InsertEmbedded(uint dwFlags, int acpStart, int acpEnd, IntPtr pDataObject, out TS_TEXTCHANGE pChange) { pChange = default; return Tsf.E_NOTIMPL; }
        public int InsertEmbeddedAtSelection(uint dwFlags, IntPtr pDataObject, out int pacpStart, out int pacpEnd, out TS_TEXTCHANGE pChange)
        { pacpStart = pacpEnd = 0; pChange = default; return Tsf.E_NOTIMPL; }

        public int RequestSupportedAttrs(uint dwFlags, uint cFilterAttrs, Guid* paFilterAttrs) => Tsf.S_OK;
        public int RequestAttrsAtPosition(int acpPos, uint cFilterAttrs, Guid* paFilterAttrs, uint dwFlags) => Tsf.S_OK;
        public int RequestAttrsTransitioningAtPosition(int acpPos, uint cFilterAttrs, Guid* paFilterAttrs, uint dwFlags) => Tsf.S_OK;
        public int FindNextAttrTransition(int acpStart, int acpHalt, uint cFilterAttrs, Guid* paFilterAttrs, uint dwFlags,
                                          out int pacpNext, out int pfFound, out int plFoundOffset)
        { pacpNext = 0; pfFound = 0; plFoundOffset = 0; return Tsf.S_OK; }
        public int RetrieveRequestedAttrs(uint ulCount, IntPtr paAttrVals, out uint pcFetched) { pcFetched = 0; return Tsf.S_OK; }

        public int GetActiveView(out uint pvcView) { pvcView = ViewCookie; return Tsf.S_OK; }

        public int GetACPFromPoint(uint vcView, in TsfPoint ptScreen, uint dwFlags, out int pacp)
        {
            pacp = 0;
            return Tsf.S_OK;
        }

        public int GetTextExt(uint vcView, int acpStart, int acpEnd, out TsfRect prc, out int pfClipped)
        {
            pfClipped = 0;
            prc = default;
            if (!HasReadLock)
            {
                return Tsf.TS_E_NOLOCK;
            }

            var rect = _getCompositionExt(Math.Max(0, acpStart - _compStart), Math.Max(0, acpEnd - _compStart));
            if (rect == null)
            {
                return Tsf.TS_E_NOLAYOUT;
            }

            prc = rect.Value;
            return Tsf.S_OK;
        }

        public int GetScreenExt(uint vcView, out TsfRect prc)
        {
            prc = default;
            var rect = _getScreenExt();
            if (rect == null)
            {
                return Tsf.TS_E_NOLAYOUT;
            }

            prc = rect.Value;
            return Tsf.S_OK;
        }

        public int GetWnd(uint vcView, out IntPtr phwnd)
        {
            phwnd = _hwnd;
            return Tsf.S_OK;
        }

        public int OnStartComposition(IntPtr pComposition, out int pfOk)
        {
            _composing = true;
            _clauses = null;
            _compStart = Math.Max(_selStart, _committedLen);
            _endedInLock = false;
            pfOk = 1;
            return Tsf.S_OK;
        }

        public int OnUpdateComposition(IntPtr pComposition, IntPtr pRangeNew)
        {
            _changedInLock = true;
            return Tsf.S_OK;
        }

        public int OnEndEdit(IntPtr pic, uint ecReadOnly, IntPtr pEditRecord)
        {
            if (!_composing || _context == null)
            {
                return Tsf.S_OK;
            }

            TsfClause[] read;
            try
            {
                read = AttributeReader.Read(_context, ecReadOnly, _compStart, _text.Length);
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"OnEndEdit read failed: {ex.Message}");
                return Tsf.S_OK;
            }

            if (SameClauses(read, _clauses))
            {
                return Tsf.S_OK;
            }

            _clauses = read;
            if (_lockFlags == 0)
            {
                RaiseCompositionUpdate();
            }
            else
            {
                _changedInLock = true;
            }

            return Tsf.S_OK;
        }

        private static bool SameClauses(TsfClause[] a, TsfClause[] b)
        {
            if (ReferenceEquals(a, b))
            {
                return true;
            }

            if (a == null || b == null || a.Length != b.Length)
            {
                return false;
            }

            for (var i = 0; i < a.Length; i++)
            {
                if (!a[i].SameAs(b[i]))
                {
                    return false;
                }
            }

            return true;
        }

        public int OnEndComposition(IntPtr pComposition)
        {
            if (_composing)
            {
                _composing = false;
                _endedInLock = true;

                if (_lockFlags == 0)
                {
                    FlushAfterLock();
                }
            }

            return Tsf.S_OK;
        }
    }
}
