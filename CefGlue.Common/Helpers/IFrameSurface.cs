using System;

namespace Xilium.CefGlue.Common.Helpers
{
    public interface IFrameSurface<T>
    {
        void BeginFrame(int width, int height);

        void CopyFrame(IntPtr buffer, int stride, CefRectangle region);
        void CopyDirtyFrame(IntPtr buffer, int stride, CefRectangle region);

        void CopyFrame(CefAcceleratedPaintInfo info, CefRectangle[] dirtyRects);

        void EndFrame();

        T GetFrameSurface(ref CefRectangle dirtyRect);

        void CopyFrameSurfaceInto(IntPtr destination, int destinationStride, int destinationWidth, int destinationHeight, ref CefRectangle dirtyRect);

        int Width { get; }
        int Height { get; }

        Action<CefRectangle> FrameReady { get; set; }

        bool ShowOverlayInfo { get; set; }

        bool IsAccelerated { get; set; }
    }
}
