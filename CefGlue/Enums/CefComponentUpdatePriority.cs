namespace Xilium.CefGlue
{
    /// <summary>
    /// Component update priority. Added in CEF 146.
    /// Maps to component_updater::OnDemandUpdater::Priority.
    /// </summary>
    public enum CefComponentUpdatePriority
    {
        Background = 0,
        Foreground = 1,
    }
}
