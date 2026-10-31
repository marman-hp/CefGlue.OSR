namespace Xilium.CefGlue
{
    /// <summary>
    /// Result codes for CefMediaRouter::CreateRoute. Should be kept in sync with
    /// Chromium's media_router::mojom::RouteRequestResultCode type.
    /// renumbered.
    /// </summary>
    public enum CefMediaRouteCreateResult
    {
        UnknownError,
        Ok,
        TimedOut,
        RouteNotFound,
        SinkNotFound,
        InvalidOrigin,
        OffTheRecordMismatchDeprecated,
        NoSupportedProvider,
        Cancelled,
        RouteAlreadyExists,
        DesktopPickerFailed,
        RouteAlreadyTerminated,
        RedundantRequest,
        UserNotAllowed,
        NotificationDisabled,
        NumValues,
    }
}
