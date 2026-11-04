using System;
using System.Diagnostics;
using System.Threading;
using Gdk;
using Gtk;
using Xilium.CefGlue.Common.Helpers;

namespace Xilium.CefGlue.GTK.Common
{
    /// <summary>
    /// OSR hosting - owns the latest CEF frame and shows it via a Gtk.Picture/Gdk.MemoryTexture.
    /// Mirrors CefGlue.Avalonia's BitmapControl in role, but deliberately the SIMPLE, allocating
    /// GetFrameSurface path (a fresh byte[] snapshot per frame) rather than Avalonia's zero-copy
    /// native-buffer-ring approach - this is a v1 scaffold, prioritizing correctness over the
    /// per-frame allocation cost; revisit if that's ever actually a measured bottleneck.
    ///
    /// Originally built on Gtk.DrawingArea + Cairo.ImageSurface, painted by hand in a SetDrawFunc
    /// callback. Abandoned (2026-08-04) after this hit an unfixable-from-our-side lifetime problem:
    /// GTK4's DrawingArea draw callback isn't rasterized synchronously - it's recorded into a GSK
    /// render node that gets REPLAYED later (gsk_render_node_draw, on a subsequent compositor
    /// pass), an arbitrary and unbounded number of times/frames later. Every variant tried failed
    /// for a lifetime reason tied to that: disposing the ImageSurface right after use segfaulted
    /// (pixman_image_composite32 -> memcpy reading freed memory - use-after-free); reusing/mutating
    /// one persistent surface tripped cairo's own "cairo_surface_mark_dirty_rectangle: Assertion
    /// `!_cairo_surface_has_snapshots(surface)' failed" (cairo's replay-recording machinery
    /// snapshots a surface once used as a paint source, and refuses to let it be dirtied again
    /// afterwards); and even a fresh surface every frame with a genuinely-held 64-frame keep-alive
    /// queue (so the .NET GC couldn't finalize it early) still crashed.
    /// Gdk.Texture sidesteps the whole class of bugs by being IMMUTABLE by construction - there is
    /// no mutate-in-place, no MarkDirty, nothing for a delayed GSK replay to read after it's gone
    /// invalid, since a Texture object is never written to again after creation. This is also the
    /// idiomatic GTK4 way to show a frequently-updated raw pixel buffer (video, screen capture,
    /// OSR browsers) - Gtk.Picture takes a Gdk.Paintable (which Texture implements) purely via
    /// composition, matching this project's preference for composition over widget subclassing
    /// (see BaseCefControl's remarks).
    /// </summary>
    internal class BitmapControl : BaseCefControl, IFrameSurfaceHost<byte[]>
    {
        private readonly Picture _picture;
        private readonly SynchronizationContext _uiContext;

        private readonly object _frameLock = new object();
        private byte[] _frame;
        private int _width;
        private int _height;

        private IFrameSurface<byte[]> _surface;

        private readonly Stopwatch _fpsStopwatch = Stopwatch.StartNew();
        private int _frameCount;
        private int _fps;
        private long _lastFpsElapsed;

        public BitmapControl()
        {
            _uiContext = SynchronizationContext.Current;

            _picture = Picture.New();
            _picture.ContentFit = ContentFit.Fill;
            _picture.CanShrink = true;
            _picture.Focusable = true;

            _picture.AddTickCallback((widget, frameClock) =>
            {
                ReportAllocatedSize(widget.GetWidth(), widget.GetHeight());
                return true;
            });
        }

        public override Widget Widget => _picture;

        public override void FocusContent()
        {
            _picture.GrabFocus();
        }

        public void AttachSurface(IFrameSurface<byte[]> surface)
        {
            _surface = surface;
            _surface.FrameReady = OnFrameReady;
        }

        private void OnFrameReady(CefRectangle dirtyRect)
        {
            if (_surface == null)
            {
                return;
            }

            var region = new CefRectangle();
            var frame = _surface.GetFrameSurface(ref region);

            if (frame == null || region.Width <= 0 || region.Height <= 0)
            {
                return;
            }

            lock (_frameLock)
            {
                _frame = frame;
                _width = region.Width;
                _height = region.Height;
            }

            _uiContext?.Post(_ => UpdateTexture(), null);
        }

        private void UpdateTexture()
        {
            byte[] frame;
            int frameWidth, frameHeight;

            lock (_frameLock)
            {
                frame = _frame;
                frameWidth = _width;
                frameHeight = _height;
            }

            if (frame == null || frameWidth <= 0 || frameHeight <= 0)
            {
                return;
            }

            var stride = frameWidth * 4;

            if (_surface != null && _surface.ShowOverlayInfo)
            {
                DrawInfoOverlay(frame, frameWidth, frameHeight, stride);
            }

            var texture = MemoryTexture.New(frameWidth, frameHeight, MemoryFormat.B8g8r8a8Premultiplied, GLib.Bytes.New(frame), (UIntPtr)stride);

            _picture.SetPaintable(texture);
        }

        private void DrawInfoOverlay(byte[] frame, int width, int height, int stride)
        {
            OverlayDiagnostics.EnsureBackgroundSampling();

            _frameCount++;
            var elapsed = _fpsStopwatch.ElapsedMilliseconds;
            if (elapsed - _lastFpsElapsed >= 1000)
            {
                _fps = _frameCount;
                _frameCount = 0;
                _lastFpsElapsed = elapsed;
            }

            var lines = new[]
            {
                $"FPS         : {_fps}",
                $"Texture     : {(_surface.IsAccelerated ? "Enable" : "Disable")}",
                $"CEF version : {OverlayDiagnostics.CefVersion}",
                $"Processor   : {OverlayDiagnostics.ProcessorName}",
                $"GPU         : {OverlayDiagnostics.GpuName}",
                $"Mem Usage   : {OverlayDiagnostics.MemoryUsageMb:F1} MB",
                $"CPU Usage   : {OverlayDiagnostics.CpuUsagePercent:F1} %",
                $"GPU Usage   : {OverlayDiagnostics.GpuUsagePercent:F1} %",
            };

            const int padding = 6;
            const int charAdvance = OverlayFont.GlyphWidth + 1;
            const int lineAdvance = OverlayFont.GlyphHeight + 2;

            var maxChars = 0;
            foreach (var line in lines)
            {
                maxChars = Math.Max(maxChars, line.Length);
            }

            var boxWidth = Math.Min(width, padding * 2 + maxChars * charAdvance);
            var boxHeight = Math.Min(height, padding * 2 + lines.Length * lineAdvance);

            var boxOriginX = Math.Max(0, width - boxWidth);

            for (var y = 0; y < boxHeight; y++)
            {
                var rowOffset = y * stride;
                for (var x = 0; x < boxWidth; x++)
                {
                    BlendPixel(frame, rowOffset + (boxOriginX + x) * 4, 128, 0, 128, 150);
                }
            }

            for (var i = 0; i < lines.Length; i++)
            {
                DrawText(frame, stride, width, height, boxOriginX + padding, padding + i * lineAdvance, lines[i]);
            }
        }

        private static void DrawText(byte[] frame, int stride, int bufferWidth, int bufferHeight, int startX, int startY, string text)
        {
            var x = startX;
            foreach (var c in text)
            {
                var glyph = OverlayFont.GetGlyph(c);
                if (glyph != null)
                {
                    DrawGlyph(frame, stride, bufferWidth, bufferHeight, x, startY, glyph);
                }
                x += OverlayFont.GlyphWidth + 1;
            }
        }

        private static void DrawGlyph(byte[] frame, int stride, int bufferWidth, int bufferHeight, int originX, int originY, byte[] glyph)
        {
            for (var row = 0; row < glyph.Length; row++)
            {
                var py = originY + row;
                if (py < 0 || py >= bufferHeight)
                {
                    continue;
                }

                var bits = glyph[row];
                var rowOffset = py * stride;

                for (var col = 0; col < OverlayFont.GlyphWidth; col++)
                {
                    var bit = (bits >> (OverlayFont.GlyphWidth - 1 - col)) & 1;
                    if (bit == 0)
                    {
                        continue;
                    }

                    var px = originX + col;
                    if (px < 0 || px >= bufferWidth)
                    {
                        continue;
                    }

                    BlendPixel(frame, rowOffset + px * 4, 255, 255, 255, 255);
                }
            }
        }

        private static void BlendPixel(byte[] buffer, int offset, byte r, byte g, byte b, byte a)
        {
            if (a == 0)
            {
                return;
            }

            if (a == 255)
            {
                buffer[offset] = b;
                buffer[offset + 1] = g;
                buffer[offset + 2] = r;
                buffer[offset + 3] = 255;
                return;
            }

            var srcA = a / 255f;
            var invA = 1f - srcA;

            buffer[offset] = (byte)(b * srcA + buffer[offset] * invA);
            buffer[offset + 1] = (byte)(g * srcA + buffer[offset + 1] * invA);
            buffer[offset + 2] = (byte)(r * srcA + buffer[offset + 2] * invA);
            buffer[offset + 3] = (byte)(255 * srcA + buffer[offset + 3] * invA);
        }
    }
}
