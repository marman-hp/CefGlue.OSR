namespace Xilium.CefGlue
{
    /// <summary>
    /// XML node types.
    /// </summary>
    public enum CefXmlNodeType
    {
        Unsupported,
        ProcessingInstruction,
        DocumentType,
        ElementStart,
        ElementEnd,
        Attribute,
        Text,
        CData,
        EntityReference,
        WhiteSpace,
        Comment,
        NumValues,
    }
}
