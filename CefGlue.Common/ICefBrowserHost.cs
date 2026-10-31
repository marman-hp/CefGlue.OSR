using System;
using Xilium.CefGlue.Common.Handlers;

namespace Xilium.CefGlue.Common
{
    public interface ICefBrowserHost
    {
        void HandleBrowserCreated(CefBrowser browser);
        void HandleBrowserDestroyed(CefBrowser browser);
        bool HandleBrowserClose(CefBrowser browser);

        void HandleAddressChange(CefBrowser browser, CefFrame frame, string url);
        void HandleTitleChange(CefBrowser browser, string title);
        void HandleFaviconUrlChange(CefBrowser browser, string[] iconUrls);
        bool HandleTooltip(CefBrowser browser, string text);
        void HandleStatusMessage(CefBrowser browser, string value);
        bool HandleConsoleMessage(CefBrowser browser, CefLogSeverity level, string message, string source, int line);

        void HandleLoadStart(CefBrowser browser, CefFrame frame, CefTransitionType transitionType);
        void HandleLoadEnd(CefBrowser browser, CefFrame frame, int httpStatusCode);
        void HandleLoadError(CefBrowser browser, CefFrame frame, CefErrorCode errorCode, string errorText, string failedUrl);
        void HandleLoadingStateChange(CefBrowser browser, bool isLoading, bool canGoBack, bool canGoForward);

        void HandleFrameDetached(CefBrowser browser, CefFrame frame);

        void HandleFullscreenModeChange(CefBrowser browser, bool fullscreen);

        void HandleOpenContextMenu(CefContextMenuParams parameters, CefMenuModel model, CefRunContextMenuCallback callback);
        void HandleCloseContextMenu();

        void HandleException(Exception exception);

        bool HandleCursorChange(IntPtr cursorHandle, CefCursorType cursorType);

        bool CaptureAudio { get; set; }
        void HandleAudioStreamStarted(CefBrowser browser, int sampleRate, int channels);
        void HandleAudioStreamPacket(CefBrowser browser, float[][] channelData, int frames, long pts);
        void HandleAudioStreamStopped(CefBrowser browser);

        bool UseNativeContextMenu { get; set; }
        ContextMenuHandler ContextMenuHandler { get; set; }
        DialogHandler DialogHandler { get; set; }
        DownloadHandler DownloadHandler { get; set; }
        DragHandler DragHandler { get; set; }
        FindHandler FindHandler { get; set; }
        FocusHandler FocusHandler { get; set; }
        KeyboardHandler KeyboardHandler { get; set; }
        RequestHandler RequestHandler { get; set; }
        LifeSpanHandler LifeSpanHandler { get; set; }
        DisplayHandler DisplayHandler { get; set; }
        RenderHandler RenderHandler { get; set; }
        JSDialogHandler JSDialogHandler { get; set; }

    }
}
