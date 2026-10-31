namespace Xilium.CefGlue
{
    /// <summary>
    /// Component update error codes. Added in CEF 146.
    /// Maps to update_client::Error values from
    /// components/update_client/update_client_errors.h.
    /// </summary>
    public enum CefComponentUpdateError
    {
        None = 0,
        UpdateInProgress = 1,
        UpdateCanceled = 2,
        RetryLater = 3,
        ServiceError = 4,
        UpdateCheckError = 5,
        CrxNotFound = 6,
        InvalidArgument = 7,
        BadCrxDataCallback = 8,
    }
}
