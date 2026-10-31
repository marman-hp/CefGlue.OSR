namespace Xilium.CefGlue
{
    using System;

    /// <summary>
    /// Chrome toolbar button types. Should be kept in sync with CEF's internal
    /// ToolbarButtonType type.
    /// </summary>
    public enum CefChromeToolbarButtonType
    {
        Cast,
        Download,
        SendTabToSelf,
        SidePanel,
        NumValues,
    }
}
