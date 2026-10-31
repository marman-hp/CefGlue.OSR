using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;

namespace Xilium.CefGlue.WinForms.Ime
{

    internal readonly struct TsfClause
    {
        public TsfClause(int start, int end, bool target, int lineStyle, bool boldLine, uint lineColorArgb)
        {
            Start = start;
            End = end;
            Target = target;
            LineStyle = lineStyle;
            BoldLine = boldLine;
            LineColorArgb = lineColorArgb;
        }

        public int Start { get; }
        public int End { get; }
        public bool Target { get; }
        public int LineStyle { get; }
        public bool BoldLine { get; }
        public uint LineColorArgb { get; }

        public bool SameAs(in TsfClause o) =>
            Start == o.Start && End == o.End && Target == o.Target && LineStyle == o.LineStyle &&
            BoldLine == o.BoldLine && LineColorArgb == o.LineColorArgb;
    }

    internal readonly struct TsfDisplayAttr
    {
        public TsfDisplayAttr(bool target, int lineStyle, bool boldLine, uint lineColorArgb)
        {
            Target = target;
            LineStyle = lineStyle;
            BoldLine = boldLine;
            LineColorArgb = lineColorArgb;
        }

        public bool Target { get; }
        public int LineStyle { get; }
        public bool BoldLine { get; }
        public uint LineColorArgb { get; }
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TfDaColor { public int type; public int value; }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TfDisplayAttribute
    {
        public TfDaColor crText;
        public TfDaColor crBk;
        public int lsStyle;
        public int fBoldLine;
        public TfDaColor crLine;
        public int bAttr;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct TfVariant
    {
        public ushort vt;
        public ushort r1, r2, r3;
        public long data1;
        public long data2;
    }

    [GeneratedComInterface, Guid("4ea48a35-60ae-446f-8fd6-e6a8d82459f7")]
    internal partial interface ITfSource
    {
        [PreserveSig] int AdviseSink(in Guid riid, IntPtr punk, out uint pdwCookie);
        [PreserveSig] int UnadviseSink(uint dwCookie);
    }

    [GeneratedComInterface, Guid("8127d409-ccd3-4683-967a-b43d5b482bf7")]
    internal partial interface ITfTextEditSink
    {
        [PreserveSig] int OnEndEdit(IntPtr pic, uint ecReadOnly, IntPtr pEditRecord);
    }

    [GeneratedComInterface, Guid("e2449660-9542-11d2-bf46-00105a2799b5")]
    internal partial interface ITfProperty
    {
        [PreserveSig] int GetType(out Guid pguid);
        [PreserveSig] int EnumRanges(uint ec, out IEnumTfRanges ppEnum, ITfRange pTargetRange);
        [PreserveSig] int GetValue(uint ec, ITfRange pRange, out TfVariant pvarValue);
        [PreserveSig] int GetContext(out IntPtr ppContext);
        [PreserveSig] int FindRange(uint ec, ITfRange pRange, out ITfRange ppRange, int aPos);
        [PreserveSig] int SetValueStore(uint ec, ITfRange pRange, IntPtr pPropStore);
        [PreserveSig] int SetValue(uint ec, ITfRange pRange, in TfVariant pvarValue);
        [PreserveSig] int Clear(uint ec, ITfRange pRange);
    }

    [GeneratedComInterface, Guid("f99d3f40-8e32-11d2-bf46-00105a2799b5")]
    internal partial interface IEnumTfRanges
    {
        [PreserveSig] int Clone(out IEnumTfRanges ppEnum);
        [PreserveSig] int Next(uint ulCount, out ITfRange ppRange, out uint pcFetched);
        [PreserveSig] int Reset();
        [PreserveSig] int Skip(uint ulCount);
    }

    [GeneratedComInterface, Guid("057a6296-029b-4154-b79a-0d461d4ea94c")]
    internal unsafe partial interface ITfRangeACP : ITfRange
    {
        [PreserveSig] int GetExtent(out int pacpAnchor, out int pcch);
        [PreserveSig] int SetExtent(int acpAnchor, int cch);
    }

    [GeneratedComInterface, Guid("c3acefb5-f69d-4905-938f-fcadcf4be830")]
    internal partial interface ITfCategoryMgr
    {
        [PreserveSig] int RegisterCategory(IntPtr rclsid, IntPtr rcatid, IntPtr rguid);
        [PreserveSig] int UnregisterCategory(IntPtr rclsid, IntPtr rcatid, IntPtr rguid);
        [PreserveSig] int EnumCategoriesInItem(IntPtr rguid, out IntPtr ppEnum);
        [PreserveSig] int EnumItemsInCategory(IntPtr rcatid, out IntPtr ppEnum);
        [PreserveSig] int FindClosestCategory(IntPtr rguid, IntPtr pcatid, IntPtr ppcatidList, uint ulCount);
        [PreserveSig] int RegisterGUIDDescription(IntPtr rclsid, IntPtr rguid, IntPtr pchDesc, uint cch);
        [PreserveSig] int UnregisterGUIDDescription(IntPtr rclsid, IntPtr rguid);
        [PreserveSig] int GetGUIDDescription(IntPtr rguid, out IntPtr pbstrDesc);
        [PreserveSig] int RegisterGUIDDWORD(IntPtr rclsid, IntPtr rguid, uint dw);
        [PreserveSig] int UnregisterGUIDDWORD(IntPtr rclsid, IntPtr rguid);
        [PreserveSig] int GetGUIDDWORD(IntPtr rguid, out uint pdw);
        [PreserveSig] int RegisterGUID(in Guid rguid, out uint pguidatom);
        [PreserveSig] int GetGUID(uint guidatom, out Guid pguid);
    }

    [GeneratedComInterface, Guid("8ded7393-5db1-475c-9e71-a39111b0ff67")]
    internal partial interface ITfDisplayAttributeMgr
    {
        [PreserveSig] int OnUpdateInfo();
        [PreserveSig] int EnumDisplayAttributeInfo(out IntPtr ppEnum);
        [PreserveSig] int GetDisplayAttributeInfo(in Guid guid, out ITfDisplayAttributeInfo ppInfo, out Guid pclsidOwner);
    }

    [GeneratedComInterface, Guid("70528852-2f26-4aea-8c96-215150578932")]
    internal partial interface ITfDisplayAttributeInfo
    {
        [PreserveSig] int GetGUID(out Guid pguid);
        [PreserveSig] int GetDescription(out IntPtr pbstrDesc);
        [PreserveSig] int GetAttributeInfo(out TfDisplayAttribute pda);
    }

    internal sealed class TsfDisplayAttributeReader
    {
        public static readonly Guid GUID_PROP_ATTRIBUTE = new("34b45670-7526-11d2-a147-00105a2799b5");
        private static readonly Guid CLSID_TF_CategoryMgr = new("a4b544a1-438d-4b41-9325-869523e2d6c7");
        private static readonly Guid IID_ITfCategoryMgr = new("c3acefb5-f69d-4905-938f-fcadcf4be830");
        private static readonly Guid CLSID_TF_DisplayAttributeMgr = new("3ce74de4-53d3-4d74-8b83-431b3828ba53");
        private static readonly Guid IID_ITfDisplayAttributeMgr = new("8ded7393-5db1-475c-9e71-a39111b0ff67");

        private const ushort VT_I4 = 3;
        private const int TF_ATTR_TARGET_CONVERTED = 1;
        private const int TF_ATTR_TARGET_NOTCONVERTED = 3;
        private const int TF_CT_COLORREF = 2;
        private const int TF_ANCHOR_END = 1;

        private ITfCategoryMgr _categoryMgr;
        private ITfDisplayAttributeMgr _displayAttributeMgr;
        private readonly Dictionary<uint, TsfDisplayAttr?> _cache = new();

        public Func<uint, TsfDisplayAttr?> Resolver { get; set; }

        private TsfDisplayAttr? Resolve(uint atom)
        {
            if (Resolver != null)
            {
                return Resolver(atom);
            }

            if (_cache.TryGetValue(atom, out var cached))
            {
                return cached;
            }

            TsfDisplayAttr? result = null;
            try
            {
                _categoryMgr ??= Tsf.CreateInstance<ITfCategoryMgr>(CLSID_TF_CategoryMgr, IID_ITfCategoryMgr);
                _displayAttributeMgr ??= Tsf.CreateInstance<ITfDisplayAttributeMgr>(CLSID_TF_DisplayAttributeMgr, IID_ITfDisplayAttributeMgr);

                if (_categoryMgr.GetGUID(atom, out var guid) >= 0 &&
                    _displayAttributeMgr.GetDisplayAttributeInfo(in guid, out var info, out _) >= 0 &&
                    info != null &&
                    info.GetAttributeInfo(out var da) >= 0)
                {
                    uint argb = 0;
                    if (da.crLine.type == TF_CT_COLORREF)
                    {
                        var c = (uint)da.crLine.value;
                        argb = 0xFF000000u | ((c & 0xFF) << 16) | (c & 0xFF00) | ((c >> 16) & 0xFF);
                    }

                    result = new TsfDisplayAttr(da.bAttr == TF_ATTR_TARGET_CONVERTED || da.bAttr == TF_ATTR_TARGET_NOTCONVERTED,
                        da.lsStyle, da.fBoldLine != 0, argb);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"display attribute lookup failed for atom {atom}: {ex.Message}");
            }

            _cache[atom] = result;
            return result;
        }

        public TsfClause[] Read(ITfContext context, uint ec, int compStart, int docLength)
        {
            if (docLength <= compStart)
            {
                return null;
            }

            if (context.GetProperty(GUID_PROP_ATTRIBUTE, out var property) < 0 || property == null ||
                context.GetStart(ec, out var start) < 0 || context.GetEnd(ec, out var end) < 0 ||
                start.ShiftEndToRange(ec, end, TF_ANCHOR_END) < 0 ||
                property.EnumRanges(ec, out var enumerator, start) < 0 || enumerator == null)
            {
                return null;
            }

            var tagged = new List<TsfClause>();
            while (enumerator.Next(1, out var range, out var fetched) == 0 && fetched == 1 && range != null)
            {
                if (((ITfRangeACP)(object)range).GetExtent(out var acp, out var cch) < 0 || cch <= 0)
                {
                    continue;
                }

                if (property.GetValue(ec, range, out var value) < 0 || value.vt != VT_I4)
                {
                    continue;
                }

                var attr = Resolve((uint)value.data1);
                if (attr == null)
                {
                    continue;
                }

                var s = Math.Max(acp, compStart) - compStart;
                var e = Math.Min(acp + cch, docLength) - compStart;
                if (e > s)
                {
                    tagged.Add(new TsfClause(s, e, attr.Value.Target, attr.Value.LineStyle, attr.Value.BoldLine, attr.Value.LineColorArgb));
                }
            }

            tagged.Sort((a, b) => a.Start.CompareTo(b.Start));

            var length = docLength - compStart;
            var result = new List<TsfClause>();
            var cursor = 0;
            foreach (var clause in tagged)
            {
                if (clause.Start > cursor)
                {
                    result.Add(new TsfClause(cursor, clause.Start, false, 1, false, 0));
                }

                if (clause.Start >= cursor)
                {
                    result.Add(clause);
                    cursor = clause.End;
                }
            }

            if (cursor < length)
            {
                result.Add(new TsfClause(cursor, length, false, 1, false, 0));
            }

            return result.ToArray();
        }
    }
}
