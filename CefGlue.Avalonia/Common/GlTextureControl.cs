using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using System;
using Xilium.CefGlue.Avalonia.Platform.Linux;
using Xilium.CefGlue.Common.Helpers;

namespace Xilium.CefGlue.Avalonia.Common
{
    /// <summary>
    /// Linux zero-copy control: a GlQuadPresenter drawing CEF's dma-buf frames, with the same focus proxy,
    /// info overlay and CPU fallback as TextureControl.
    /// </summary>
    internal sealed class GlTextureControl : BaseCefControl
    {
        private readonly Border _focusProxy;
        private readonly GlQuadPresenter _presenter;
        private readonly Border _fpsOverlay;
        private readonly TextBlock _fpsText;
        private readonly Grid _grid;

        private AvaloniaGlTextureRenderSurface _renderSurface;
        private BitmapControl _cpuView;
        private bool _disposed;

        private readonly System.Diagnostics.Stopwatch _fpsStopwatch = System.Diagnostics.Stopwatch.StartNew();
        private int _frameCount;
        private int _fps;
        private long _lastFpsElapsed;

        public Control FpsOverlay => _fpsOverlay;

        /// <summary>Raised once, on the UI thread, when the GL path can't show frames at all.</summary>
        public event Action<string> Failed;

        public GlTextureControl()
        {
            _presenter = new GlQuadPresenter
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };
            _presenter.FrameRendered += OnFrameRendered;
            _presenter.Failed += reason => Failed?.Invoke(reason);

            _focusProxy = new Border
            {
                Background = Brushes.Transparent,
                Focusable = true,
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };

            _fpsText = new TextBlock
            {
                Foreground = Brushes.White,
                FontFamily = new FontFamily("Consolas,Menlo,DejaVu Sans Mono,Liberation Mono,Noto Sans Mono,Ubuntu Mono,monospace"),
                FontWeight = FontWeight.Normal,
                FontSize = 11,
            };

            _fpsOverlay = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(150, 128, 0, 128)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 10, 10, 0),
                Padding = new Thickness(6, 4, 6, 4),
                Child = _fpsText,
                IsVisible = false,
            };

            _grid = new Grid();
            _grid.Children.Add(_presenter);
            _grid.Children.Add(_focusProxy);
            Content = _grid;
        }

        public override void FocusContent() => _focusProxy.Focus();

        public void RefreshOverlay()
        {
            Browser?.GetHost()?.Invalidate(CefPaintElementType.View);
        }

        public void AttachRenderSurface(AvaloniaGlTextureRenderSurface renderSurface)
        {
            _renderSurface = renderSurface;
            _renderSurface.Degraded += OnRenderSurfaceDegraded;
            _presenter.AttachRenderSurface(renderSurface);
        }

        public void AttachPopupSurface(AvaloniaGlTextureRenderSurface popupSurface) => _presenter.AttachPopupSurface(popupSurface);

        public void SetPopup(bool visible, Rect rect) => _presenter.SetPopup(visible, rect);

        // Called on the CEF thread. Same swap as TextureControl: the control stays, only the visual changes.
        private void OnRenderSurfaceDegraded(AvaloniaBitmapSurface cpuFrames, string reason)
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (_cpuView != null || _disposed)
                {
                    return;
                }

                _cpuView = new BitmapControl
                {
                    UseSafeResize = true,
                    IsHitTestVisible = false,
                    Focusable = false,
                };
                KeyboardNavigation.SetTabNavigation(_cpuView, KeyboardNavigationMode.None);
                _cpuView.AttachSurface(cpuFrames);

                _presenter.IsVisible = false;
                _fpsOverlay.IsVisible = false;
                _grid.Children.Insert(0, _cpuView);

                // The page may be static: ask CEF for a fresh frame so the bitmap isn't empty.
                RefreshOverlay();
            });
        }

        private void OnFrameRendered()
        {
            if (_renderSurface?.ShowOverlayInfo == true && !_renderSurface.IsDegraded)
            {
                UpdateInfoOverlay();
            }
            else if (_fpsOverlay.IsVisible)
            {
                _fpsOverlay.IsVisible = false;
            }
        }

        private void UpdateInfoOverlay()
        {
            OverlayDiagnostics.EnsureBackgroundSampling();

            _fpsOverlay.IsVisible = true;

            _frameCount++;
            var currentElapsed = _fpsStopwatch.ElapsedMilliseconds;

            if (currentElapsed - _lastFpsElapsed >= 1000)
            {
                _fps = _frameCount;
                _frameCount = 0;
                _lastFpsElapsed = currentElapsed;
            }

            _fpsText.Text =
                $"FPS         : {_fps}\n" +
                $"Texture     : Enable (GL)\n" +
                $"CEF version : {OverlayDiagnostics.CefVersion}\n" +
                $"Processor   : {OverlayDiagnostics.ProcessorName}\n" +
                $"GPU         : {OverlayDiagnostics.GpuName}\n" +
                $"Mem Usage   : {OverlayDiagnostics.MemoryUsageMb:F1} MB\n" +
                $"CPU Usage   : {OverlayDiagnostics.CpuUsagePercent:F1} %\n" +
                $"GPU Usage   : {OverlayDiagnostics.GpuUsagePercent:F1} %";
        }

        public void ReleaseSurface()
        {
            _disposed = true;

            if (_renderSurface != null)
            {
                _renderSurface.Degraded -= OnRenderSurfaceDegraded;
            }

            _presenter.DetachRenderSurface();
        }
    }
}
