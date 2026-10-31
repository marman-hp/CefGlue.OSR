namespace Xilium.CefGlue
{
    using System;

    /// <summary>
    /// CEF supports both a Chrome runtime style (based on the Chrome UI layer) and
    /// an Alloy runtime style (based on the Chromium content layer). Chrome style
    /// provides the full Chrome UI and browser functionality whereas Alloy style
    /// provides less default browser functionality but adds additional client
    /// callbacks and support for windowless (off-screen) rendering. The style type
    /// is individually configured for each window/browser at creation time and
    /// different styles can be mixed during runtime. For additional comparative
    /// details on runtime styles see
    /// https://bitbucket.org/chromiumembedded/cef/wiki/Architecture.md#markdown-header-cef3
    /// </summary>
    public enum CefRuntimeStyle
    {
        Default,
        Chrome,
        Alloy,
    }
}
