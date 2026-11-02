using System;

namespace Xilium.CefGlue.Common.Composition
{
    public sealed class CefGlueBrowserOptions
    {
        public Func<CefRequestContext> RequestContextFactory { get; set; }
        public Func<CefWindowInfo> SetupCefWindowInfo { get; set; }
        public Func<CefBrowserSettings> SetupCefBrowserSettings { get; set; }

        /// <summary>
        /// Avalonia only: with SharedTextureEnabled, present CEF's texture directly (TextureControl) instead of
        /// reading it back into a bitmap. Ignored elsewhere (WinUI picks TextureControl from SharedTextureEnabled alone).
        /// On Linux this is the only shared-texture mode (EGL dma-buf import), and it needs Avalonia's GL context to be
        /// EGL: put X11RenderingMode.Egl first in X11PlatformOptions.RenderingMode.
        /// </summary>
        public bool UseTextureControl { get; set; }
    }
}
