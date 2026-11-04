using Gtk;
using Xilium.CefGlue.Common.Helpers;
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

            _glArea.AddTickCallback((widget, frameClock) =>
            {
                ReportAllocatedSize(widget.GetWidth(), widget.GetHeight());
                UpdateInfoLabel();
                return true;
            });
        }

        public override Widget Widget => _glArea;

        public override void FocusContent() => _glArea.GrabFocus();

        private Label _infoLabel;
        private GtkTextureRenderSurface _renderSurface;
        private readonly System.Diagnostics.Stopwatch _fpsStopwatch = System.Diagnostics.Stopwatch.StartNew();
        private int _frameCount;
        private int _fps;
        private long _lastFpsElapsed;
        private string _infoText;

        public Widget InfoOverlay => _infoLabel ??= CreateInfoLabel();

        private static Label CreateInfoLabel()
        {
            var label = Label.New(string.Empty);
            label.Halign = Align.End;
            label.Valign = Align.Start;
            label.MarginTop = 10;
            label.MarginEnd = 10;
            label.CanTarget = false;
            label.Visible = false;

            const uint StyleProviderPriorityApplication = 600;
            var css = CssProvider.New();
            css.LoadFromString(
                "label { background-color: rgba(128, 0, 128, 0.59); color: white; padding: 4px 6px;" +
                " font-family: 'DejaVu Sans Mono', 'Liberation Mono', 'Ubuntu Mono', monospace; font-size: 11px; }");
            label.GetStyleContext().AddProvider(css, StyleProviderPriorityApplication);
            return label;
        }

        public void AttachRenderSurface(GtkTextureRenderSurface renderSurface)
        {
            _renderSurface = renderSurface;
            renderSurface.FrameRendered += OnFrameRendered;
            renderSurface.AttachGLArea(_glArea);
        }

        private void OnFrameRendered() => _frameCount++;

        private void UpdateInfoLabel()
        {
            if (_infoLabel == null)
            {
                return;
            }

            if (_renderSurface?.ShowOverlayInfo != true)
            {
                if (_infoLabel.Visible)
                {
                    _infoLabel.Visible = false;
                }
                return;
            }

            OverlayDiagnostics.EnsureBackgroundSampling();

            var elapsed = _fpsStopwatch.ElapsedMilliseconds;
            if (elapsed - _lastFpsElapsed >= 1000)
            {
                _fps = _frameCount;
                _frameCount = 0;
                _lastFpsElapsed = elapsed;
            }

            var text =
                $"FPS         : {_fps}\n" +
                $"Texture     : Enable (GL)\n" +
                $"CEF version : {OverlayDiagnostics.CefVersion}\n" +
                $"Processor   : {OverlayDiagnostics.ProcessorName}\n" +
                $"GPU         : {OverlayDiagnostics.GpuName}\n" +
                $"Mem Usage   : {OverlayDiagnostics.MemoryUsageMb:F1} MB\n" +
                $"CPU Usage   : {OverlayDiagnostics.CpuUsagePercent:F1} %\n" +
                $"GPU Usage   : {OverlayDiagnostics.GpuUsagePercent:F1} %";
            if (text != _infoText)
            {
                _infoText = text;
                _infoLabel.SetText(text);
            }

            if (!_infoLabel.Visible)
            {
                _infoLabel.Visible = true;
            }
        }
    }
}
