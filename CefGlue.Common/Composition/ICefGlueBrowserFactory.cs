using System;

namespace Xilium.CefGlue.Common.Composition
{
    public interface ICefGlueBrowserFactory
    {
        object CreateBrowser(CefGlueHost host, CefGlueBrowserOptions options);

        Type BrowserType { get; }
    }
}
