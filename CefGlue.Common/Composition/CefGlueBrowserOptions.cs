using System;

namespace Xilium.CefGlue.Common.Composition
{
    public sealed class CefGlueBrowserOptions
    {
        public Func<CefRequestContext> RequestContextFactory { get; set; }
        public Func<CefWindowInfo> SetupCefWindowInfo { get; set; }
        public Func<CefBrowserSettings> SetupCefBrowserSettings { get; set; }
    }
}
