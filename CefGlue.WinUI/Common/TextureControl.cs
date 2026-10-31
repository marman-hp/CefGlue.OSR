using System;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Xilium.CefGlue.WinUI;
using Xilium.CefGlue.Common.Helpers;

namespace Xilium.CefGlue.WinUI.Common
{
    internal sealed class TextureControl : BaseCefControl
    {
        private readonly Grid _root;
        private readonly SwapChainPanel _panel;
        private readonly Border _fpsOverlay;
        private readonly TextBlock _fpsText;
        private WinUITextureRenderSurface _renderSurface;

        private bool _isResizeGhosting;
        private bool _resizeEnded;
        private int _preResizeWidth;
        private int _preResizeHeight;
        private double _preResizeActualWidth;
        private double _preResizeActualHeight;

        private readonly System.Diagnostics.Stopwatch _fpsStopwatch = System.Diagnostics.Stopwatch.StartNew();
        private int _frameCount;
        private int _fps;
        private long _lastFpsElapsed;

        public TextureControl()
        {
            _panel = new SwapChainPanel
            {
                HorizontalAlignment = HorizontalAlignment.Stretch,
                VerticalAlignment = VerticalAlignment.Stretch,
            };

            IsTabStop = true;

            _fpsText = new TextBlock
            {
                Foreground = new SolidColorBrush(Microsoft.UI.Colors.White),
                FontFamily = new FontFamily("Consolas"),
                FontSize = 11,
            };

            _fpsOverlay = new Border
            {
                Background = new SolidColorBrush(Microsoft.UI.ColorHelper.FromArgb(150, 128, 0, 128)),
                HorizontalAlignment = HorizontalAlignment.Right,
                VerticalAlignment = VerticalAlignment.Top,
                Margin = new Thickness(0, 10, 10, 0),
                Padding = new Thickness(6, 4, 6, 4),
                Child = _fpsText,
                Visibility = Visibility.Collapsed,
            };

            _root = new Grid
            {
                Background = new SolidColorBrush(Microsoft.UI.Colors.Transparent),
            };
            _root.Children.Add(_panel);
            _root.Children.Add(_fpsOverlay);
            Content = _root;

            Loaded += OnLoaded;

            ResizeBegin += OnResizeBegin;
            SafeResize += OnResizeEnd;
            SizeChanged += OnControlSizeChanged;
        }

        public SwapChainPanel Panel => _panel;

        public void AttachRenderSurface(WinUITextureRenderSurface renderSurface)
        {
            if (_renderSurface != null)
            {
                _renderSurface.FrameAccepted -= OnFrameAccepted;
            }

            _renderSurface = renderSurface;
            _renderSurface.FrameAccepted += OnFrameAccepted;

            if (_panel.XamlRoot != null)
            {
                _renderSurface.AttachPanel(_panel);
            }
        }

        private void OnLoaded(object sender, RoutedEventArgs e)
        {
            _renderSurface?.AttachPanel(_panel);
        }

        private void OnResizeBegin()
        {
            _isResizeGhosting = true;
            _resizeEnded = false;
            _preResizeWidth = _renderSurface?.CurrentWidth ?? 0;
            _preResizeHeight = _renderSurface?.CurrentHeight ?? 0;
            _preResizeActualWidth = ActualWidth;
            _preResizeActualHeight = ActualHeight;
        }

        private void OnResizeEnd()
        {
            _resizeEnded = true;

            if (_isResizeGhosting &&
                ActualWidth == _preResizeActualWidth &&
                ActualHeight == _preResizeActualHeight)
            {
                _isResizeGhosting = false;
                RunOnUiIfLive(ClearTransform);
            }
        }

        private void OnControlSizeChanged(object sender, SizeChangedEventArgs e)
        {
            if (IsResizing)
            {
                if (_isResizeGhosting)
                {
                    ApplyTransform(true, _renderSurface?.CurrentWidth ?? 0, _renderSurface?.CurrentHeight ?? 0);
                }
                return;
            }

            OnResizeBegin();
            OnResizeEnd();
        }

        private void OnFrameAccepted(int width, int height)
        {
            if (_isResizeGhosting && _resizeEnded &&
                (width != _preResizeWidth || height != _preResizeHeight))
            {
                _isResizeGhosting = false;
            }

            var isGhosting = _isResizeGhosting;
            RunOnUiIfLive(() =>
            {
                ApplyTransform(isGhosting, width, height);

                if (_renderSurface != null && _renderSurface.ShowOverlayInfo)
                {
                    UpdateInfoOverlay();
                }
                else if (_fpsOverlay.Visibility == Visibility.Visible)
                {
                    _fpsOverlay.Visibility = Visibility.Collapsed;
                }
            });
        }

        private void RunOnUiIfLive(Action update)
        {
            DispatcherQueue?.TryEnqueue(() =>
            {
                if (!IsLoaded || XamlRoot == null)
                {
                    return;
                }

                try
                {
                    update();
                }
                catch (System.Runtime.InteropServices.COMException ex) when (ex.HResult == unchecked((int)0x8000FFFF))
                {
                }
            });
        }

        private void UpdateInfoOverlay()
        {
            OverlayDiagnostics.EnsureBackgroundSampling();

            _fpsOverlay.Visibility = Visibility.Visible;

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

        public void RefreshOverlay()
        {
            Browser?.GetHost()?.Invalidate(CefPaintElementType.View);
        }

        private void ApplyZoomSize()
        {
            _panel.RenderTransform = null;
            _panel.Width = double.NaN;
            _panel.Height = double.NaN;
            _panel.HorizontalAlignment = HorizontalAlignment.Stretch;
            _panel.VerticalAlignment = VerticalAlignment.Stretch;
        }

        private void ApplyTransform(bool isGhosting, int contentWidth, int contentHeight)
        {
            var scale = XamlRoot?.RasterizationScale ?? 0;

            if (!isGhosting || contentWidth <= 0 || contentHeight <= 0 ||
                ActualWidth <= 0 || ActualHeight <= 0 || scale <= 0)
            {
                ClearTransform();
                return;
            }

            var contentDipWidth = contentWidth / scale;
            var contentDipHeight = contentHeight / scale;

            _panel.HorizontalAlignment = HorizontalAlignment.Left;
            _panel.VerticalAlignment = VerticalAlignment.Top;
            _panel.Width = contentDipWidth;
            _panel.Height = contentDipHeight;

            _panel.RenderTransform = new ScaleTransform
            {
                ScaleX = ActualWidth / contentDipWidth,
                ScaleY = ActualHeight / contentDipHeight,
            };
        }

        private void ClearTransform()
        {
            ApplyZoomSize();
        }
    }
}
