namespace Xilium.CefGlue
{
    using System;

    /// <summary>
    /// DOM node types.
    /// </summary>
    public enum CefDomNodeType
    {
       Unsupported,
       Element,
       Attribute,
       Text,
       CDataSection,
       ProcessingInstruction,
       Comment,
       Document,
       DocumentType,
       DocumentFragment,
       NumValues,
    }
}
