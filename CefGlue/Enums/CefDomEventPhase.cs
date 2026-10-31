namespace Xilium.CefGlue
{
    using System;

    /// <summary>
    /// DOM event processing phases.
    /// </summary>
    public enum CefDomEventPhase
    {
        Unknown = 0,
        Capturing,
        AtTarget,
        Bubbling,
        NumValues,
    }
}
