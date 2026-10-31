namespace Xilium.CefGlue
{
    /// <summary>
    /// Preferences type passed to
    /// CefBrowserProcessHandler::OnRegisterCustomPreferences.
    /// </summary>
    public enum CefPreferencesType
    {
        /// <summary>
        /// Global preferences registered a single time at application startup.
        /// </summary>
        Global,

        /// <summary>
        /// Request context preferences registered each time a new CefRequestContext
        /// is created.
        /// </summary>
        RequestContext,

        NumValues,
    }
}