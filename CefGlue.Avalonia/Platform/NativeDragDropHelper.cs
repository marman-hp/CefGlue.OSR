using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
using System.Text;

namespace Xilium.CefGlue.Avalonia.Platform
{
    internal static class NativeDragDropHelper
    {

        [DllImport("ole32.dll")]
        private static extern int DoDragDrop(IDataObject pDataObj, IDropSource pDropSource, int dwOKEffects, out int pdwEffect);

        [DllImport("ole32.dll")]
        private static extern int OleInitialize(IntPtr pvReserved);

        private const int RPC_E_CHANGED_MODE = unchecked((int)0x80010106);

        [ThreadStatic]
        private static bool _oleInitialized;

        private static void EnsureOleInitialized()
        {
            if (_oleInitialized)
            {
                return;
            }

            var hr = OleInitialize(IntPtr.Zero);
            if (hr < 0 && hr != RPC_E_CHANGED_MODE)
            {
                Marshal.ThrowExceptionForHR(hr);
            }

            _oleInitialized = true;
        }

        [ComImport]
        [Guid("00000121-0000-0000-C000-000000000046")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDropSource
        {
            [PreserveSig]
            int QueryContinueDrag(bool fEscapePressed, int grfKeyState);

            [PreserveSig]
            int GiveFeedback(int dwEffect);
        }

        private const int DRAGDROP_S_DROP = 0x00040100;
        private const int DRAGDROP_S_CANCEL = 0x00040101;
        private const int DRAGDROP_S_USEDEFAULTCURSORS = 0x00040102;
        private const int MK_LBUTTON = 0x0001;

        private const int IDC_NO = 32648;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr LoadCursor(IntPtr hInstance, IntPtr lpCursorName);

        [DllImport("user32.dll")]
        private static extern IntPtr SetCursor(IntPtr hCursor);

        private static IntPtr _noCursor;

        private sealed class SimpleDropSource : IDropSource
        {
            private readonly Func<CefDragOperationsMask> _currentAllowedDragOpsProvider;
            private readonly Func<bool> _isDragOverOwnControlProvider;

            public SimpleDropSource(Func<CefDragOperationsMask> currentAllowedDragOpsProvider, Func<bool> isDragOverOwnControlProvider)
            {
                _currentAllowedDragOpsProvider = currentAllowedDragOpsProvider;
                _isDragOverOwnControlProvider = isDragOverOwnControlProvider;
            }

            public int QueryContinueDrag(bool fEscapePressed, int grfKeyState)
            {
                if (fEscapePressed)
                {
                    return DRAGDROP_S_CANCEL;
                }

                if ((grfKeyState & MK_LBUTTON) == 0)
                {
                    return DRAGDROP_S_DROP;
                }

                return 0;
            }

            public int GiveFeedback(int dwEffect)
            {
                if (_isDragOverOwnControlProvider() && _currentAllowedDragOpsProvider() == CefDragOperationsMask.None)
                {
                    if (_noCursor == IntPtr.Zero)
                    {
                        _noCursor = LoadCursor(IntPtr.Zero, (IntPtr)IDC_NO);
                    }

                    if (_noCursor != IntPtr.Zero)
                    {
                        SetCursor(_noCursor);
                        return 0;
                    }
                }

                return DRAGDROP_S_USEDEFAULTCURSORS;
            }
        }

        public const int DROPEFFECT_NONE = 0;
        public const int DROPEFFECT_COPY = 1;
        public const int DROPEFFECT_MOVE = 2;
        public const int DROPEFFECT_LINK = 4;

        public static int RunDoDragDrop(IDataObject dataObject, int allowedEffects, Func<CefDragOperationsMask> currentAllowedDragOpsProvider, Func<bool> isDragOverOwnControlProvider)
        {
            EnsureOleInitialized();

            var hr = DoDragDrop(dataObject, new SimpleDropSource(currentAllowedDragOpsProvider, isDragOverOwnControlProvider), allowedEffects, out var effect);
            if (hr < 0)
            {
                Marshal.ThrowExceptionForHR(hr);
            }

            return effect;
        }

        [ComImport]
        [Guid("4657278A-411B-11d2-839A-00C04FD918D0")]
        private class DragDropHelper
        {
        }

        [ComImport]
        [Guid("DE5BF786-477A-11D2-839D-00C04FD918D0")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDragSourceHelper
        {
            void InitializeFromBitmap(ref ShDragImage dragImage, IDataObject dataObject);

            void InitializeFromWindow(IntPtr hwnd, ref Win32Point pt, IDataObject dataObject);
        }

        [ComImport]
        [Guid("4657278B-411B-11D2-839A-00C04FD918D0")]
        [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        private interface IDropTargetHelper
        {
            void DragEnter(IntPtr hwndTarget, IDataObject dataObject, ref Win32Point pt, int effect);

            void DragLeave();

            void DragOver(ref Win32Point pt, int effect);

            void Drop(IDataObject dataObject, ref Win32Point pt, int effect);

            void Show(bool show);
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Win32Point
        {
            public int X;
            public int Y;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct Win32Size
        {
            public int Cx;
            public int Cy;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ShDragImage
        {
            public Win32Size SizeDragImage;
            public Win32Point PtOffset;
            public IntPtr HbmpDragImage;
            public int CrColorKey;
        }

        [DllImport("gdi32.dll")]
        private static extern bool DeleteObject(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        private struct BitmapInfoHeader
        {
            public uint Size;
            public int Width;
            public int Height;
            public ushort Planes;
            public ushort BitCount;
            public uint Compression;
            public uint SizeImage;
            public int XPelsPerMeter;
            public int YPelsPerMeter;
            public uint ClrUsed;
            public uint ClrImportant;
        }

        private const uint BI_RGB = 0;
        private const uint DIB_RGB_COLORS = 0;

        [DllImport("gdi32.dll", SetLastError = true)]
        private static extern IntPtr CreateDIBSection(IntPtr hdc, ref BitmapInfoHeader pbmi, uint usage, out IntPtr ppvBits, IntPtr hSection, uint offset);

        private static IntPtr CreateArgbHBitmap(byte[] bgraPixels, int width, int height)
        {
            var header = new BitmapInfoHeader
            {
                Size = (uint)Marshal.SizeOf<BitmapInfoHeader>(),
                Width = width,
                Height = -height,
                Planes = 1,
                BitCount = 32,
                Compression = BI_RGB,
            };

            var hBitmap = CreateDIBSection(IntPtr.Zero, ref header, DIB_RGB_COLORS, out var bits, IntPtr.Zero, 0);
            if (hBitmap == IntPtr.Zero || bits == IntPtr.Zero)
            {
                throw new InvalidOperationException("CreateDIBSection failed to create the drag image bitmap.");
            }

            Marshal.Copy(bgraPixels, 0, bits, bgraPixels.Length);
            return hBitmap;
        }

        private static void AttachDragImage(ComDataObject dataObject, IntPtr hBitmap, int width, int height, int hotspotX, int hotspotY)
        {
            EnsureOleInitialized();

            var shdi = new ShDragImage
            {
                SizeDragImage = new Win32Size { Cx = width, Cy = height },
                PtOffset = new Win32Point { X = hotspotX, Y = hotspotY },
                HbmpDragImage = hBitmap,
                CrColorKey = unchecked((int)0xFFFFFFFF),
            };

            try
            {
                var sourceHelper = (IDragSourceHelper)new DragDropHelper();
                sourceHelper.InitializeFromBitmap(ref shdi, dataObject);
            }
            catch
            {
                DeleteObject(hBitmap);
                throw;
            }
        }

        public static void AttachDragImageIfPresent(CefDragData dragData, ComDataObject comDataObject)
        {
            if (!dragData.HasImage)
            {
                return;
            }

            using (var cefImage = dragData.GetImage())
            {
                if (cefImage == null)
                {
                    return;
                }

                using (var binary = cefImage.GetAsBitmap(1f, CefColorType.Bgra8888, CefAlphaType.Opaque, out var width, out var height))
                {
                    if (binary == null || width <= 0 || height <= 0)
                    {
                        return;
                    }

                    var buffer = new byte[width * 4 * height];
                    binary.GetData(buffer, buffer.Length, 0);

                    var hotspot = dragData.GetImageHotspot();
                    var hBitmap = CreateArgbHBitmap(buffer, width, height);

                    AttachDragImage(comDataObject, hBitmap, width, height, hotspot.X, hotspot.Y);
                }
            }
        }

        private static IDataObject TryGetNativeDataObject(object avaloniaData)
        {
            try
            {
                if (avaloniaData is IDataObject native)
                {
                    return native;
                }

                var oleField = avaloniaData?.GetType().GetField("_oleDataObject", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var oleComObject = oleField?.GetValue(avaloniaData);

                if (oleComObject != null)
                {
                    var micoComNativePointerProp = oleComObject.GetType().GetProperty("NativePointer", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    var micoComNativePointer = micoComNativePointerProp?.GetValue(oleComObject);

#pragma warning disable CA1416
                    if (micoComNativePointer is IntPtr micoComPtr && micoComPtr != IntPtr.Zero)
                    {
                        var result = Marshal.GetObjectForIUnknown(micoComPtr) as IDataObject;
                        if (result != null)
                        {
                            return result;
                        }
                    }
#pragma warning restore CA1416

                    return null;
                }

                var wrappedField = avaloniaData?.GetType().GetField("_wrapped", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                var wrapped = wrappedField?.GetValue(avaloniaData);
                if (wrapped == null)
                {
                    return null;
                }

                var nativePointerProp = wrapped.GetType().GetProperty("NativePointer", System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (nativePointerProp?.GetValue(wrapped) is IntPtr nativePointer && nativePointer != IntPtr.Zero)
                {
#pragma warning disable CA1416
                    return Marshal.GetObjectForIUnknown(nativePointer) as IDataObject;
#pragma warning restore CA1416
                }

                return wrapped as IDataObject;
            }
            catch
            {
                return null;
            }
        }

        public static void NotifyDragEnter(IntPtr hwndTarget, object avaloniaData, int x, int y, int effect)
        {
            var data = TryGetNativeDataObject(avaloniaData);
            if (data == null)
            {
                return;
            }

            try
            {
                EnsureOleInitialized();
                var pt = new Win32Point { X = x, Y = y };
                var helper = (IDropTargetHelper)new DragDropHelper();
                helper.DragEnter(hwndTarget, data, ref pt, effect);
            }
            catch
            {
            }
        }

        public static void NotifyDragOver(int x, int y, int effect)
        {
            try
            {
                EnsureOleInitialized();
                var pt = new Win32Point { X = x, Y = y };
                var helper = (IDropTargetHelper)new DragDropHelper();
                helper.DragOver(ref pt, effect);
            }
            catch
            {
            }
        }

        public static void NotifyDragLeave()
        {
            try
            {
                EnsureOleInitialized();
                var helper = (IDropTargetHelper)new DragDropHelper();
                helper.DragLeave();
            }
            catch
            {
            }
        }

        public static void NotifyDrop(object avaloniaData, int x, int y, int effect)
        {
            var data = TryGetNativeDataObject(avaloniaData);
            if (data == null)
            {
                return;
            }

            try
            {
                EnsureOleInitialized();
                var pt = new Win32Point { X = x, Y = y };
                var helper = (IDropTargetHelper)new DragDropHelper();
                helper.Drop(data, ref pt, effect);
            }
            catch
            {
            }
        }
    }

    internal sealed class ComDataObject : IDataObject, IDisposable
    {
        private const int S_FALSE = 1;
        private const int OLE_E_ADVISENOTSUPPORTED = unchecked((int)0x80040003);
        private const int DV_E_FORMATETC = unchecked((int)0x80040064);
        private const int DV_E_TYMED = unchecked((int)0x80040069);
        private const int DV_E_CLIPFORMAT = unchecked((int)0x8004006A);
        private const int DV_E_DVASPECT = unchecked((int)0x8004006B);

        private const uint GMEM_MOVEABLE = 0x0002;
        private const uint GMEM_ZEROINIT = 0x0040;

        public const short CF_TEXT = 1;
        public const short CF_UNICODETEXT = 13;
        public const short CF_HDROP = 15;

        [DllImport("ole32.dll")]
        private static extern void ReleaseStgMedium(ref STGMEDIUM pmedium);

        [DllImport("urlmon.dll")]
        private static extern int CopyStgMedium(ref STGMEDIUM pcstgmedSrc, ref STGMEDIUM pstgmedDest);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalAlloc(uint uFlags, UIntPtr dwBytes);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GlobalLock(IntPtr hMem);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GlobalUnlock(IntPtr hMem);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern short RegisterClipboardFormat(string lpszFormat);

        public static short GetClipboardFormatId(string name) => RegisterClipboardFormat(name);

        private readonly List<KeyValuePair<FORMATETC, STGMEDIUM>> _storage = new List<KeyValuePair<FORMATETC, STGMEDIUM>>();

        public void SetHGlobalData(short cfFormat, byte[] bytes)
        {
            var hGlobal = GlobalAlloc(GMEM_MOVEABLE | GMEM_ZEROINIT, (UIntPtr)bytes.Length);
            var ptr = GlobalLock(hGlobal);
            try
            {
                Marshal.Copy(bytes, 0, ptr, bytes.Length);
            }
            finally
            {
                GlobalUnlock(hGlobal);
            }

            var formatEtc = new FORMATETC
            {
                cfFormat = cfFormat,
                ptd = IntPtr.Zero,
                dwAspect = DVASPECT.DVASPECT_CONTENT,
                lindex = -1,
                tymed = TYMED.TYMED_HGLOBAL,
            };
            var medium = new STGMEDIUM
            {
                tymed = TYMED.TYMED_HGLOBAL,
                unionmember = hGlobal,
                pUnkForRelease = null,
            };

            SetData(ref formatEtc, ref medium, true);
        }

        public void SetTextFormat(string formatName, byte[] bytes)
        {
            SetHGlobalData(GetClipboardFormatId(formatName), bytes);
        }

        public void SetFileDropList(string[] filePaths)
        {
            const int dropFilesHeaderSize = 20;

            var sb = new StringBuilder();
            foreach (var path in filePaths)
            {
                sb.Append(path);
                sb.Append('\0');
            }
            sb.Append('\0');

            var pathsBytes = Encoding.Unicode.GetBytes(sb.ToString());
            var buffer = new byte[dropFilesHeaderSize + pathsBytes.Length];

            BitConverter.GetBytes(dropFilesHeaderSize).CopyTo(buffer, 0);
            BitConverter.GetBytes(1).CopyTo(buffer, 16);
            Array.Copy(pathsBytes, 0, buffer, dropFilesHeaderSize, pathsBytes.Length);

            SetHGlobalData(CF_HDROP, buffer);
        }

        public void SetData(ref FORMATETC formatIn, ref STGMEDIUM medium, bool release)
        {
            for (var i = _storage.Count - 1; i >= 0; i--)
            {
                var key = _storage[i].Key;
                if ((key.tymed & formatIn.tymed) != 0 && key.dwAspect == formatIn.dwAspect && key.cfFormat == formatIn.cfFormat)
                {
                    var old = _storage[i].Value;
                    ReleaseStgMedium(ref old);
                    _storage.RemoveAt(i);
                    break;
                }
            }

            var stored = medium;
            if (!release)
            {
                var copy = new STGMEDIUM();
                var hr = CopyStgMedium(ref medium, ref copy);
                if (hr != 0)
                {
                    throw Marshal.GetExceptionForHR(hr);
                }
                stored = copy;
            }

            _storage.Add(new KeyValuePair<FORMATETC, STGMEDIUM>(formatIn, stored));
        }

        public int QueryGetData(ref FORMATETC format)
        {
            if ((DVASPECT.DVASPECT_CONTENT & format.dwAspect) == 0)
            {
                return DV_E_DVASPECT;
            }

            var result = DV_E_TYMED;

            foreach (var pair in _storage)
            {
                if ((pair.Key.tymed & format.tymed) == 0)
                {
                    continue;
                }

                if (pair.Key.cfFormat == format.cfFormat)
                {
                    return 0;
                }

                result = DV_E_CLIPFORMAT;
            }

            return result;
        }

        public void GetData(ref FORMATETC format, out STGMEDIUM medium)
        {
            foreach (var pair in _storage)
            {
                if ((pair.Key.tymed & format.tymed) != 0 && pair.Key.dwAspect == format.dwAspect && pair.Key.cfFormat == format.cfFormat)
                {
                    var source = pair.Value;
                    var copy = new STGMEDIUM();
                    var hr = CopyStgMedium(ref source, ref copy);
                    if (hr != 0)
                    {
                        throw Marshal.GetExceptionForHR(hr);
                    }

                    medium = copy;
                    return;
                }
            }

            medium = default;
        }

        public void GetDataHere(ref FORMATETC format, ref STGMEDIUM medium)
        {
            throw new NotSupportedException();
        }

        public int GetCanonicalFormatEtc(ref FORMATETC formatIn, out FORMATETC formatOut)
        {
            formatOut = formatIn;
            return DV_E_FORMATETC;
        }

        public IEnumFORMATETC EnumFormatEtc(DATADIR direction)
        {
            if (direction == DATADIR.DATADIR_GET)
            {
                var formats = new FORMATETC[_storage.Count];
                for (var i = 0; i < formats.Length; i++)
                {
                    formats[i] = _storage[i].Key;
                }

                return new FormatEtcEnumerator(formats);
            }

            throw new NotImplementedException();
        }

        public int DAdvise(ref FORMATETC pFormatetc, ADVF advf, IAdviseSink adviseSink, out int connection)
        {
            connection = 0;
            return OLE_E_ADVISENOTSUPPORTED;
        }

        public void DUnadvise(int connection)
        {
            throw Marshal.GetExceptionForHR(OLE_E_ADVISENOTSUPPORTED);
        }

        public int EnumDAdvise(out IEnumSTATDATA enumAdvise)
        {
            enumAdvise = null;
            return OLE_E_ADVISENOTSUPPORTED;
        }

        public void Dispose()
        {
            foreach (var pair in _storage)
            {
                var medium = pair.Value;
                ReleaseStgMedium(ref medium);
            }

            _storage.Clear();
        }

        private sealed class FormatEtcEnumerator : IEnumFORMATETC
        {
            private readonly FORMATETC[] _formats;
            private int _index;

            public FormatEtcEnumerator(FORMATETC[] formats)
            {
                _formats = formats;
            }

            public void Clone(out IEnumFORMATETC newEnum)
            {
                newEnum = new FormatEtcEnumerator(_formats) { _index = _index };
            }

            public int Reset()
            {
                _index = 0;
                return 0;
            }

            public int Skip(int celt)
            {
                if (_index + celt > _formats.Length)
                {
                    return S_FALSE;
                }

                _index += celt;
                return 0;
            }

            public int Next(int celt, FORMATETC[] rgelt, int[] pceltFetched)
            {
                if (pceltFetched != null && pceltFetched.Length > 0)
                {
                    pceltFetched[0] = 0;
                }

                if (celt <= 0 || rgelt == null || _index >= _formats.Length)
                {
                    return S_FALSE;
                }

                var remaining = celt;
                var i = 0;
                for (; _index < _formats.Length && remaining > 0; i++, remaining--, _index++)
                {
                    rgelt[i] = _formats[_index];
                }

                if (pceltFetched != null && pceltFetched.Length > 0)
                {
                    pceltFetched[0] = celt - remaining;
                }

                return remaining == 0 ? 0 : S_FALSE;
            }
        }
    }
}
