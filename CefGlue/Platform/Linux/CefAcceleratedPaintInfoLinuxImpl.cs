using System;
using Xilium.CefGlue.Interop;

namespace Xilium.CefGlue.Platform;

internal sealed unsafe class CefAcceleratedPaintInfoLinuxImpl : CefAcceleratedPaintInfo
{
    private cef_accelerated_paint_info_t_linux* _self;

    internal CefAcceleratedPaintInfoLinuxImpl(cef_accelerated_paint_info_t* ptr)
    {
        _self = (cef_accelerated_paint_info_t_linux*)ptr;

        Modifier = _self->modifier;
        PlaneCount = _self->plane_count;

        var planes = new CefAcceleratedPaintNativePixmapPlane[PlaneCount];
        if (PlaneCount > 0) planes[0] = CefAcceleratedPaintNativePixmapPlane.FromNative(_self->plane0);
        if (PlaneCount > 1) planes[1] = CefAcceleratedPaintNativePixmapPlane.FromNative(_self->plane1);
        if (PlaneCount > 2) planes[2] = CefAcceleratedPaintNativePixmapPlane.FromNative(_self->plane2);
        if (PlaneCount > 3) planes[3] = CefAcceleratedPaintNativePixmapPlane.FromNative(_self->plane3);
        Planes = planes;

        Format = _self->format;
        Extra = CefAcceleratedPaintInfoCommon.FromNative(_self->extra);
    }

    public override CefColorType Format { get; }
    public override CefAcceleratedPaintInfoCommon Extra { get; }

    public override ulong Modifier { get; }
    public override int PlaneCount { get; }
    public override CefAcceleratedPaintNativePixmapPlane[] Planes { get; }

    public override IntPtr SharedTexture => throw new PlatformNotSupportedException();
}
