namespace Xilium.CefGlue.Common.Platform
{
    public readonly struct ImeCompositionClause
    {
        public ImeCompositionClause(int start, int end, bool thick, CefCompositionUnderlineStyle style, uint colorArgb)
        {
            Start = start;
            End = end;
            Thick = thick;
            Style = style;
            ColorArgb = colorArgb;
        }

        public int Start { get; }

        public int End { get; }

        public bool Thick { get; }

        public CefCompositionUnderlineStyle Style { get; }

        public uint ColorArgb { get; }
    }
}
