using System;
using System.IO.MemoryMappedFiles;

namespace Xilium.CefGlue.Common.Helpers
{
    internal readonly struct PixelBuffer
    {
        public PixelBuffer(IntPtr pointer, int width, int height, int stride, int byteCount)
        {
            Pointer = pointer;
            Width = width;
            Height = height;
            Stride = stride;
            ByteCount = byteCount;
        }

        public IntPtr Pointer { get; }

        public int Width { get; }

        public int Height { get; }

        public int Stride { get; }

        public int ByteCount { get; }
    }

    internal sealed class CpuFrameStagingBuffer : IDisposable
    {
        private MemoryMappedFile _mappedFile;
        private MemoryMappedViewAccessor _viewAccessor;

        public unsafe PixelBuffer? CopyIn(IntPtr source, int width, int height, int stride, int byteCount)
        {
            if (source == IntPtr.Zero || width <= 0 || height <= 0 || stride <= 0 || byteCount <= 0)
            {
                return null;
            }

            if (_viewAccessor == null || _viewAccessor.Capacity < byteCount)
            {
                Release();

                _mappedFile = MemoryMappedFile.CreateNew(null, byteCount, MemoryMappedFileAccess.ReadWrite);
                _viewAccessor = _mappedFile.CreateViewAccessor();
            }

            var handle = _viewAccessor.SafeMemoryMappedViewHandle;
            if (handle.IsInvalid || handle.IsClosed)
            {
                return null;
            }

            byte* pointer = null;
            handle.AcquirePointer(ref pointer);
            try
            {
                if (pointer == null)
                {
                    return null;
                }

                Buffer.MemoryCopy(source.ToPointer(), pointer, (long)handle.ByteLength, byteCount);
            }
            finally
            {
                handle.ReleasePointer();
            }

            return new PixelBuffer((IntPtr)pointer, width, height, stride, byteCount);
        }

        public void Release()
        {
            _viewAccessor?.Dispose();
            _viewAccessor = null;

            _mappedFile?.Dispose();
            _mappedFile = null;
        }

        public void Dispose() => Release();
    }
}
