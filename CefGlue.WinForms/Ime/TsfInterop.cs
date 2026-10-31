using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

[assembly: InternalsVisibleTo("CefGlue.Ime.Harness")]

namespace Xilium.CefGlue.WinForms.Ime
{

    internal static class Tsf
    {
        public static readonly Guid CLSID_TF_ThreadMgr = new("529a9e6b-6587-4f23-ab9e-9c7d683e3c50");
        public static readonly Guid IID_ITfThreadMgr = new("aa80e801-2021-11d2-93e0-0060b067b86e");
        public static readonly Guid IID_ITextStoreACPSink = new("22d44c94-a419-4542-a272-ae26093ececf");
        public static readonly Guid IID_ITfTextEditSink = new("8127d409-ccd3-4683-967a-b43d5b482bf7");

        public const int S_OK = 0;
        public const int S_FALSE = 1;
        public const int E_NOTIMPL = unchecked((int)0x80004001);
        public const int E_INVALIDARG = unchecked((int)0x80070057);
        public const int E_FAIL = unchecked((int)0x80004005);
        public const int E_UNEXPECTED = unchecked((int)0x8000FFFF);
        public const int CONNECT_E_ADVISELIMIT = unchecked((int)0x80040201);
        public const int TS_E_INVALIDPOS = unchecked((int)0x80040200);
        public const int TS_E_NOLOCK = unchecked((int)0x80040201);
        public const int TS_E_NOLAYOUT = unchecked((int)0x80040206);
        public const int TS_E_SYNCHRONOUS = unchecked((int)0x80040208);
        public const int TS_S_ASYNC = 0x00040300;

        public const uint TS_LF_SYNC = 0x1;
        public const uint TS_LF_READ = 0x2;
        public const uint TS_LF_READWRITE = 0x6;

        public const uint TS_AS_TEXT_CHANGE = 0x1;
        public const uint TS_AS_SEL_CHANGE = 0x2;
        public const uint TS_AS_LAYOUT_CHANGE = 0x4;
        public const uint TS_AS_STATUS_CHANGE = 0x10;

        public const uint TS_SS_NOHIDDENTEXT = 0x8;
        public const uint TS_IAS_QUERYONLY = 0x2;
        public const uint TS_DEFAULT_SELECTION = 0xFFFFFFFF;

        public const int TS_LC_CHANGE = 1;

        public const uint TF_ES_SYNC = 0x1;
        public const uint TF_ES_READWRITE = 0x6;
        public const uint TF_IAS_NO_DEFAULT_COMPOSITION = 0x80000000;

        public static readonly StrategyBasedComWrappers ComWrappers = new();

        [DllImport("ole32.dll")]
        private static extern int CoCreateInstance(in Guid rclsid, IntPtr pUnkOuter, uint dwClsContext, in Guid riid, out IntPtr ppv);

        public static T CreateInstance<T>(in Guid clsid, in Guid iid) where T : class
        {
            const uint CLSCTX_INPROC_SERVER = 0x1;
            var hr = CoCreateInstance(clsid, IntPtr.Zero, CLSCTX_INPROC_SERVER, iid, out var ppv);
            Marshal.ThrowExceptionForHR(hr);
            try
            {
                return (T)ComWrappers.GetOrCreateObjectForComInstance(ppv, CreateObjectFlags.None);
            }
            finally
            {
                Marshal.Release(ppv);
            }
        }

        public static IntPtr GetComPointer(object managed) =>
            ComWrappers.GetOrCreateComInterfaceForObject(managed, CreateComInterfaceFlags.None);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TS_STATUS { public uint dwDynamicFlags; public uint dwStaticFlags; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TS_TEXTCHANGE { public int acpStart; public int acpOldEnd; public int acpNewEnd; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TS_SELECTION_ACP { public int acpStart; public int acpEnd; public int ase; public int fInterimChar; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TS_RUNINFO { public uint uCount; public int type; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TsfRect { public int left, top, right, bottom; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TsfPoint { public int x, y; }

    [GeneratedComInterface, Guid("aa80e801-2021-11d2-93e0-0060b067b86e")]
    internal partial interface ITfThreadMgr
    {
        [PreserveSig] int Activate(out uint ptid);
        [PreserveSig] int Deactivate();
        [PreserveSig] int CreateDocumentMgr(out ITfDocumentMgr ppdim);
        [PreserveSig] int EnumDocumentMgrs(out IntPtr ppEnum);
        [PreserveSig] int GetFocus(out ITfDocumentMgr ppdimFocus);
        [PreserveSig] int SetFocus(ITfDocumentMgr pdimFocus);
        [PreserveSig] int AssociateFocus(IntPtr hwnd, ITfDocumentMgr pdimNew, out ITfDocumentMgr ppdimPrev);
    }

    [GeneratedComInterface, Guid("aa80e7f4-2021-11d2-93e0-0060b067b86e")]
    internal partial interface ITfDocumentMgr
    {
        [PreserveSig] int CreateContext(uint tidOwner, uint dwFlags, IntPtr punk, out ITfContext ppic, out uint pecTextStore);
        [PreserveSig] int Push(ITfContext pic);
        [PreserveSig] int Pop(uint dwFlags);
    }

    [GeneratedComInterface, Guid("aa80e7fd-2021-11d2-93e0-0060b067b86e")]
    internal partial interface ITfContext
    {
        [PreserveSig] int RequestEditSession(uint tid, ITfEditSession pes, uint dwFlags, out int phrSession);
        [PreserveSig] int InWriteSession(uint tid, out int pfWriteSession);
        [PreserveSig] int GetSelection(uint ec, uint ulIndex, uint ulCount, IntPtr pSelection, out uint pcFetched);
        [PreserveSig] int SetSelection(uint ec, uint ulCount, IntPtr pSelection);
        [PreserveSig] int GetStart(uint ec, out ITfRange ppStart);
        [PreserveSig] int GetEnd(uint ec, out ITfRange ppEnd);
        [PreserveSig] int GetActiveView(out IntPtr ppView);
        [PreserveSig] int EnumViews(out IntPtr ppEnum);
        [PreserveSig] int GetStatus(out TS_STATUS pdcs);
        [PreserveSig] int GetProperty(in Guid guidProp, out ITfProperty ppProp);
    }

    [GeneratedComInterface, Guid("d60a7b49-1b9f-4be2-b702-47e9dc05dec3")]
    internal partial interface ITfClientId
    {
        [PreserveSig] int GetClientId(in Guid rclsid, out uint ptid);
    }

    [GeneratedComInterface, Guid("aa80e7ff-2021-11d2-93e0-0060b067b86e")]
    internal unsafe partial interface ITfRange
    {
        [PreserveSig] int GetText(uint ec, uint dwFlags, char* pchText, uint cchMax, out uint pcch);
        [PreserveSig] int SetText(uint ec, uint dwFlags, char* pchText, int cch);
        [PreserveSig] int GetFormattedText(uint ec, out IntPtr ppDataObject);
        [PreserveSig] int GetEmbedded(uint ec, IntPtr rguidService, IntPtr riid, out IntPtr ppunk);
        [PreserveSig] int InsertEmbedded(uint ec, uint dwFlags, IntPtr pDataObject);
        [PreserveSig] int ShiftStart(uint ec, int cchReq, out int pcch, IntPtr pHalt);
        [PreserveSig] int ShiftEnd(uint ec, int cchReq, out int pcch, IntPtr pHalt);
        [PreserveSig] int ShiftStartToRange(uint ec, ITfRange pRange, int aPos);
        [PreserveSig] int ShiftEndToRange(uint ec, ITfRange pRange, int aPos);
        [PreserveSig] int ShiftStartRegion(uint ec, int dir, out int pfNoRegion);
        [PreserveSig] int ShiftEndRegion(uint ec, int dir, out int pfNoRegion);
        [PreserveSig] int IsEmpty(uint ec, out int pfEmpty);
        [PreserveSig] int Collapse(uint ec, int aPos);
        [PreserveSig] int IsEqualStart(uint ec, ITfRange pWith, int aPos, out int pfEqual);
        [PreserveSig] int IsEqualEnd(uint ec, ITfRange pWith, int aPos, out int pfEqual);
        [PreserveSig] int CompareStart(uint ec, ITfRange pWith, int aPos, out int plResult);
        [PreserveSig] int CompareEnd(uint ec, ITfRange pWith, int aPos, out int plResult);
        [PreserveSig] int AdjustForInsert(uint ec, uint cchInsert, out int pfInsertOk, out int pcchRead);
        [PreserveSig] int GetGravity(out int pgStart, out int pgEnd);
        [PreserveSig] int SetGravity(uint ec, int gStart, int gEnd);
        [PreserveSig] int Clone(out ITfRange ppClone);
        [PreserveSig] int GetContext(out ITfContext ppContext);
    }

    [GeneratedComInterface, Guid("55ce16ba-3014-41c1-9ceb-fade1446ac6c")]
    internal unsafe partial interface ITfInsertAtSelection
    {
        [PreserveSig] int InsertTextAtSelection(uint ec, uint dwFlags, char* pchText, int cch, out ITfRange ppRange);
    }

    [GeneratedComInterface, Guid("D40C8AAE-AC92-4FC7-9A11-0EE0E23AA39B")]
    internal partial interface ITfContextComposition
    {
        [PreserveSig] int StartComposition(uint ecWrite, ITfRange pCompositionRange, ITfCompositionSink pSink, out ITfComposition ppComposition);
    }

    [GeneratedComInterface, Guid("20168D64-5A8F-4A5A-B7BD-CFA29F4D0FD9")]
    internal partial interface ITfComposition
    {
        [PreserveSig] int GetRange(out ITfRange ppRange);
        [PreserveSig] int ShiftStart(uint ecWrite, ITfRange pNewStart);
        [PreserveSig] int ShiftEnd(uint ecWrite, ITfRange pNewEnd);
        [PreserveSig] int EndComposition(uint ecWrite);
    }

    [GeneratedComInterface, Guid("aa80e803-2021-11d2-93e0-0060b067b86e")]
    internal partial interface ITfEditSession
    {
        [PreserveSig] int DoEditSession(uint ec);
    }

    [GeneratedComInterface, Guid("A781718C-579A-4B15-A280-32B8577ACC5E")]
    internal partial interface ITfCompositionSink
    {
        [PreserveSig] int OnCompositionTerminated(uint ecWrite, IntPtr pComposition);
    }

    [GeneratedComInterface, Guid("5F20AA40-B57A-4F34-96AB-3576F377CC79")]
    internal partial interface ITfContextOwnerCompositionSink
    {
        [PreserveSig] int OnStartComposition(IntPtr pComposition, out int pfOk);
        [PreserveSig] int OnUpdateComposition(IntPtr pComposition, IntPtr pRangeNew);
        [PreserveSig] int OnEndComposition(IntPtr pComposition);
    }

    [GeneratedComInterface, Guid("22d44c94-a419-4542-a272-ae26093ececf")]
    internal partial interface ITextStoreACPSink
    {
        [PreserveSig] int OnTextChange(uint dwFlags, in TS_TEXTCHANGE pChange);
        [PreserveSig] int OnSelectionChange();
        [PreserveSig] int OnLayoutChange(int lcode, uint vcView);
        [PreserveSig] int OnStatusChange(uint dwFlags);
        [PreserveSig] unsafe int OnAttrsChange(int acpStart, int acpEnd, uint cAttrs, Guid* paAttrs);
        [PreserveSig] int OnLockGranted(uint dwLockFlags);
        [PreserveSig] int OnStartEditTransaction();
        [PreserveSig] int OnEndEditTransaction();
    }

    [GeneratedComInterface, Guid("28888fe3-c2a0-483a-a3ea-8cb1ce51ff3d")]
    internal unsafe partial interface ITextStoreACP
    {
        [PreserveSig] int AdviseSink(in Guid riid, IntPtr punk, uint dwMask);
        [PreserveSig] int UnadviseSink(IntPtr punk);
        [PreserveSig] int RequestLock(uint dwLockFlags, out int phrSession);
        [PreserveSig] int GetStatus(out TS_STATUS pdcs);
        [PreserveSig] int QueryInsert(int acpTestStart, int acpTestEnd, uint cch, out int pacpResultStart, out int pacpResultEnd);
        [PreserveSig] int GetSelection(uint ulIndex, uint ulCount, TS_SELECTION_ACP* pSelection, out uint pcFetched);
        [PreserveSig] int SetSelection(uint ulCount, TS_SELECTION_ACP* pSelection);
        [PreserveSig] int GetText(int acpStart, int acpEnd, char* pchPlain, uint cchPlainReq, out uint pcchPlainRet,
                                  TS_RUNINFO* prgRunInfo, uint cRunInfoReq, out uint pcRunInfoRet, out int pacpNext);
        [PreserveSig] int SetText(uint dwFlags, int acpStart, int acpEnd, char* pchText, uint cch, out TS_TEXTCHANGE pChange);
        [PreserveSig] int GetFormattedText(int acpStart, int acpEnd, out IntPtr ppDataObject);
        [PreserveSig] int GetEmbedded(int acpPos, in Guid rguidService, in Guid riid, out IntPtr ppunk);
        [PreserveSig] int QueryInsertEmbedded(IntPtr pguidService, IntPtr pFormatEtc, out int pfInsertable);
        [PreserveSig] int InsertEmbedded(uint dwFlags, int acpStart, int acpEnd, IntPtr pDataObject, out TS_TEXTCHANGE pChange);
        [PreserveSig] int InsertTextAtSelection(uint dwFlags, char* pchText, uint cch, out int pacpStart, out int pacpEnd, out TS_TEXTCHANGE pChange);
        [PreserveSig] int InsertEmbeddedAtSelection(uint dwFlags, IntPtr pDataObject, out int pacpStart, out int pacpEnd, out TS_TEXTCHANGE pChange);
        [PreserveSig] int RequestSupportedAttrs(uint dwFlags, uint cFilterAttrs, Guid* paFilterAttrs);
        [PreserveSig] int RequestAttrsAtPosition(int acpPos, uint cFilterAttrs, Guid* paFilterAttrs, uint dwFlags);
        [PreserveSig] int RequestAttrsTransitioningAtPosition(int acpPos, uint cFilterAttrs, Guid* paFilterAttrs, uint dwFlags);
        [PreserveSig] int FindNextAttrTransition(int acpStart, int acpHalt, uint cFilterAttrs, Guid* paFilterAttrs, uint dwFlags,
                                                 out int pacpNext, out int pfFound, out int plFoundOffset);
        [PreserveSig] int RetrieveRequestedAttrs(uint ulCount, IntPtr paAttrVals, out uint pcFetched);
        [PreserveSig] int GetEndACP(out int pacp);
        [PreserveSig] int GetActiveView(out uint pvcView);
        [PreserveSig] int GetACPFromPoint(uint vcView, in TsfPoint ptScreen, uint dwFlags, out int pacp);
        [PreserveSig] int GetTextExt(uint vcView, int acpStart, int acpEnd, out TsfRect prc, out int pfClipped);
        [PreserveSig] int GetScreenExt(uint vcView, out TsfRect prc);
        [PreserveSig] int GetWnd(uint vcView, out IntPtr phwnd);
    }
}
