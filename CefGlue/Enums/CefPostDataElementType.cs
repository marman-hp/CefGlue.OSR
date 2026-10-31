namespace Xilium.CefGlue
{
    /// <summary>
    /// Post data elements may represent either bytes or files.
    /// </summary>
    public enum CefPostDataElementType
    {
        Empty = 0,
        Bytes,
        File,
        NumValues,
    }
}
