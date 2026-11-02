using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.Composition;
using Avalonia.Threading;
using System;
using System.Linq;
using System.Threading.Tasks;
using Xilium.CefGlue.Common.Helpers;

namespace Xilium.CefGlue.Avalonia.Common
{
    internal sealed class TextureControl : BaseCefControl
    {
        private readonly Border _focusProxy;
        private readonly Border _fpsOverlay;
        private readonly TextBlock _fpsText;

        public Control FpsOverlay => _fpsOverlay;

        private AvaloniaTextureRenderSurface _renderSurface;
        private readonly Grid _grid;
        private BitmapControl _cpuView;
        private string _pendingCompositorFailure;

        private Compositor _compositor;
        private CompositionSurfaceVisual _visual;
        private CompositionDrawingSurface _drawingSurface;
        private ICompositionGpuInterop _interop;
        private ICompositionImportedGpuImage _imported;
        private IntPtr _importedHandle;
        private bool _disposed;

        private readonly object _presentLock = new object();
        private bool _presentPending;

        private readonly System.Diagnostics.Stopwatch _fpsStopwatch = System.Diagnostics.Stopwatch.StartNew();
        private int _frameCount;
        private int _fps;
        private long _lastFpsElapsed;

        private bool _isResizeGhosting;
        private bool _resizeEnded;
        private int _preResizeWidth;
        private int _preResizeHeight;

        public TextureControl()
        {
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
            _grid.Children.Add(_focusProxy);
            Content = _grid;

            ResizeBegin += OnResizeBegin;
            SafeResize += OnResizeEnd;
        }

        public override void FocusContent() => _focusProxy.Focus();

        public void RefreshOverlay()
        {
            Browser?.GetHost()?.Invalidate(CefPaintElementType.View);
        }

        protected override void OnSizeChanged(SizeChangedEventArgs e)
        {
            base.OnSizeChanged(e);

            if (_isResizeGhosting && _visual != null)
            {
                _visual.Size = new Vector(Bounds.Width, Bounds.Height);
            }
        }

        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _ = InitializeCompositionAsync();
        }

        private async Task InitializeCompositionAsync()
        {
            var elementVisual = ElementComposition.GetElementVisual(this);
            if (elementVisual == null)
            {
                return;
            }

            _compositor = elementVisual.Compositor;

            _drawingSurface = _compositor.CreateDrawingSurface();
            _visual = _compositor.CreateSurfaceVisual();
            _visual.Size = new Vector(Bounds.Width, Bounds.Height);
            _visual.Surface = _drawingSurface;
            ElementComposition.SetElementChildVisual(this, _visual);

            _interop = await _compositor.TryGetCompositionGpuInterop();

            if (_interop == null)
            {
                ReportCompositorFailure("Avalonia's compositor has no GPU interop (software rendering?).");
            }
            else if (!_interop.SupportedImageHandleTypes.Contains(KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureGlobalSharedHandle))
            {
                ReportCompositorFailure("Avalonia's compositor can't import D3D11 shared textures.");
            }
        }

        public void AttachRenderSurface(AvaloniaTextureRenderSurface renderSurface)
        {
            _renderSurface = renderSurface;
            _renderSurface.FrameAccepted += OnFrameAccepted;
            _renderSurface.Degraded += OnRenderSurfaceDegraded;
            _renderSurface.RetryFrameRequested += RequestFrame;

            if (_pendingCompositorFailure != null)
            {
                ReportCompositorFailure(_pendingCompositorFailure);
            }
        }

        // Failures only visible on the compositor side; the render surface switches on its next frame.
        private void ReportCompositorFailure(string reason)
        {
            if (_renderSurface == null)
            {
                _pendingCompositorFailure = reason;
                return;
            }

            _pendingCompositorFailure = null;
            _renderSurface.RequestDegrade(reason);
            RequestFrame();
        }

        private void RequestFrame() => Browser?.GetHost()?.Invalidate(CefPaintElementType.View);

        // Called on the CEF thread. The control instance stays the same (input, IME, focus and popups are
        // wired to it); only the visual swaps from the composition surface to a bitmap fed by the CPU pipeline.
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

                if (_visual != null)
                {
                    _visual.Visible = false;
                }
                _fpsOverlay.IsVisible = false;
                _grid.Children.Insert(0, _cpuView);

                // The page may be static: ask CEF for a fresh frame so the bitmap isn't empty.
                RequestFrame();
            });
        }

        private void OnResizeBegin()
        {
            _isResizeGhosting = true;
            _resizeEnded = false;
            _preResizeWidth = _renderSurface?.CurrentWidth ?? 0;
            _preResizeHeight = _renderSurface?.CurrentHeight ?? 0;
        }

        private void OnResizeEnd()
        {
            _resizeEnded = true;

            if (_isResizeGhosting &&
                (int)Bounds.Width == _preResizeWidth &&
                (int)Bounds.Height == _preResizeHeight)
            {
                _isResizeGhosting = false;
            }
        }

        private void OnFrameAccepted(int width, int height)
        {
            if (_renderSurface == null || _renderSurface.IsDegraded || _disposed || _interop == null || _drawingSurface == null || _visual == null)
            {
                return;
            }

            lock (_presentLock)
            {
                if (_presentPending)
                {
                    return;
                }

                _presentPending = true;
            }

            Dispatcher.UIThread.Post(() =>
            {
                lock (_presentLock)
                {
                    _presentPending = false;
                }

                RunFramePresent();
            }, DispatcherPriority.Render);
        }

        private void RunFramePresent()
        {
            if (_disposed || _interop == null || _drawingSurface == null || _renderSurface == null || _renderSurface.IsDegraded)
            {
                return;
            }

            var handle = _renderSurface.CurrentHandle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            var width = _renderSurface.CurrentWidth;
            var height = _renderSurface.CurrentHeight;
            var visual = _visual;
            if (visual == null)
            {
                return;
            }

            if (_imported == null || _importedHandle != handle)
            {
                var previous = _imported;
                try
                {
                    _imported = _interop.ImportImage(
                        new PlatformHandle(handle, KnownPlatformGraphicsExternalImageHandleTypes.D3D11TextureGlobalSharedHandle),
                        new PlatformGraphicsExternalImageProperties
                        {
                            Width = width,
                            Height = height,
                            Format = PlatformGraphicsExternalImageFormat.B8G8R8A8UNorm,

                            TopLeftOrigin = true,
                        });
                }
                catch (Exception ex)
                {
                    _imported = null;
                    ReportCompositorFailure($"Importing the shared texture failed (0x{ex.HResult:X8}: {ex.Message}).");
                    return;
                }
                _importedHandle = handle;
                _ = previous?.DisposeAsync();
            }

            var updateTask = _drawingSurface.UpdateWithKeyedMutexAsync(_imported, 1, 0);

            if (_isResizeGhosting && _resizeEnded && (width != _preResizeWidth || height != _preResizeHeight))
            {
                _isResizeGhosting = false;
            }

            var targetSize = _isResizeGhosting
                ? new Vector(Bounds.Width, Bounds.Height)
                : new Vector(width, height);

            if (_isResizeGhosting)
            {
                visual.Size = targetSize;
            }
            else if (updateTask.IsCompletedSuccessfully)
            {
                visual.Size = targetSize;
            }
            else
            {
                var capturedTargetSize = targetSize;
                updateTask.ContinueWith(t =>
                {
                    if (!t.IsCompletedSuccessfully)
                    {
                        return;
                    }

                    Dispatcher.UIThread.Post(() =>
                    {
                        if (!_disposed && _visual != null)
                        {
                            _visual.Size = capturedTargetSize;
                        }
                    });
                }, TaskContinuationOptions.ExecuteSynchronously);
            }

            var showOverlay = _renderSurface?.ShowOverlayInfo ?? false;
            if (showOverlay)
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
                $"Texture     : Enable\n" +
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
                _renderSurface.FrameAccepted -= OnFrameAccepted;
                _renderSurface.Degraded -= OnRenderSurfaceDegraded;
                _renderSurface.RetryFrameRequested -= RequestFrame;
            }

            var imported = _imported;
            _imported = null;
            _ = imported?.DisposeAsync();
        }
    }
}
