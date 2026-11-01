using Gtk;
using Xilium.CefGlue.GTK;

namespace Xilium.CefGlue.GTK.Common
{
    /// <summary>
    /// True GPU-passthrough OSR hosting for GTK - mirrors CefGlue.WinUI's TextureControl's role
    /// (own the presentation widget, forward it to the render surface once ready), but hosts a
    /// GtkGLArea and draws CEF's dma-buf via a real GL texture + shader quad in its own "render"
    /// signal, instead of handing a Gdk.Texture to Gtk.Picture.SetPaintable() for GSK's own scene
    /// graph to composite. See GtkTextureRenderSurface's own remarks for the full rationale/
    /// tradeoffs of this choice over the Picture-based one.
    ///
    /// AutoRender is off - GTK's default for GLArea renders on every frame-clock tick regardless
    /// of whether anything changed; this control only wants to redraw when CEF actually delivered
    /// a new frame (GtkTextureRenderSurface.DeliverAcceleratedFrame calls QueueRender explicitly),
    /// matching every other control in this project's push-based repaint model.
    ///
    /// Selected only as a cautious opt-in (see CefGlueBrowser.ResolveControl) - unlike WinUI, where
    /// SharedTextureEnabled alone is enough to pick TextureControl, GTK's dma-buf import path is
    /// fresh and the mmap-based GtkRenderSurface accelerated-CPU-readback path is what's actually
    /// proven stable so far - SharedTextureEnabled alone still selects that one.
    /// </summary>
    internal class TextureControl : BaseCefControl
    {
        private readonly GLArea _glArea;

        public TextureControl()
        {
            _glArea = GLArea.New();
            _glArea.Focusable = true;
            _glArea.AutoRender = false;
            // No HasAlpha/SetHasAlpha binding in GirCore 0.8.1's Gtk.GLArea wrapper (confirmed via
            // compiler - not present on the type at all). Page transparency still round-trips
            // through the shader's own alpha-blended draw (DrawFrame's premultiplied GL_BLEND) -
            // this only controls whether the GdkGLContext's own framebuffer allocates an alpha
            // channel at the GL level; if transparent-background pages don't show the surroundings
            // correctly, that's the next thing to check (may need raw P/Invoke like EglInterop/
            // GlInterop if GirCore never adds this binding).

            // See BaseCefControl.ReportAllocatedSize's remarks (same rationale as BitmapControl's
            // identical tick callback).
            _glArea.AddTickCallback((widget, frameClock) =>
            {
                ReportAllocatedSize(widget.GetWidth(), widget.GetHeight());
                return true;
            });
        }

        public override Widget Widget => _glArea;

        public override void FocusContent() => _glArea.GrabFocus();

        public void AttachRenderSurface(GtkTextureRenderSurface renderSurface)
        {
            renderSurface.AttachGLArea(_glArea);
        }
    }
}
