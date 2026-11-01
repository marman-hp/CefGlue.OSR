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
        /// </summary>
        public bool UseTextureControl { get; set; }
    }
}
