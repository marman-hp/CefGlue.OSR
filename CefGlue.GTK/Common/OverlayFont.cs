namespace Xilium.CefGlue.GTK.Common
{
    /// <summary>
    /// Minimal 5x7 dot-matrix bitmap font, used to burn the debug info overlay directly into the
    /// BGRA frame byte[] buffer (see BitmapControl.DrawInfoOverlay) - deliberately NOT Cairo/Pango
    /// text rendering. This project doesn't currently reference any Cairo C# binding (only the
    /// native cairo-1.0.dll comes along transitively via GirCore.Gtk-4.0, with no GirCore.Cairo
    /// package to call it from), and GtkRenderSurface's own history (see its class remarks) is
    /// full of GSK render-node-replay lifetime crashes from Cairo surfaces that ever touched GTK's
    /// own draw/compositor pipeline. Writing plain pixels into a plain byte[] before it's ever
    /// wrapped into an (immutable) Gdk.Texture sidesteps that whole class of bug entirely - same
    /// reasoning that already led this file's sibling (BitmapControl) to prefer Gdk.Texture over
    /// Cairo.ImageSurface for the frame itself.
    ///
    /// Only supports the fixed character set the overlay actually prints (see
    /// OverlayDiagnostics/BitmapControl.DrawInfoOverlay) - upper/lowercase letters, digits, and a
    /// handful of punctuation marks. Any character outside that set is skipped (rendered as blank)
    /// rather than throwing - ProcessorName/GpuName come from the registry and could in principle
    /// contain something unexpected (e.g. a trademark glyph), and a missing pixel is a far better
    /// failure mode there than an exception on the render path.
    /// </summary>
    internal static class OverlayFont
    {
        public const int GlyphWidth = 5;
        public const int GlyphHeight = 7;

        // Each row is a byte; bit 4 = leftmost column, bit 0 = rightmost column of the 5-wide glyph.
        private static readonly System.Collections.Generic.Dictionary<char, byte[]> Glyphs = new()
        {
            ['0'] = new byte[] { 0b01110, 0b10001, 0b10011, 0b10101, 0b11001, 0b10001, 0b01110 },
            ['1'] = new byte[] { 0b00100, 0b01100, 0b00100, 0b00100, 0b00100, 0b00100, 0b01110 },
            ['2'] = new byte[] { 0b01110, 0b10001, 0b00001, 0b00010, 0b00100, 0b01000, 0b11111 },
            ['3'] = new byte[] { 0b11111, 0b00010, 0b00100, 0b00010, 0b00001, 0b10001, 0b01110 },
            ['4'] = new byte[] { 0b00010, 0b00110, 0b01010, 0b10010, 0b11111, 0b00010, 0b00010 },
            ['5'] = new byte[] { 0b11111, 0b10000, 0b11110, 0b00001, 0b00001, 0b10001, 0b01110 },
            ['6'] = new byte[] { 0b00110, 0b01000, 0b10000, 0b11110, 0b10001, 0b10001, 0b01110 },
            ['7'] = new byte[] { 0b11111, 0b00001, 0b00010, 0b00100, 0b01000, 0b01000, 0b01000 },
            ['8'] = new byte[] { 0b01110, 0b10001, 0b10001, 0b01110, 0b10001, 0b10001, 0b01110 },
            ['9'] = new byte[] { 0b01110, 0b10001, 0b10001, 0b01111, 0b00001, 0b00010, 0b01100 },

            ['A'] = new byte[] { 0b01110, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001 },
            ['B'] = new byte[] { 0b11110, 0b10001, 0b10001, 0b11110, 0b10001, 0b10001, 0b11110 },
            ['C'] = new byte[] { 0b01110, 0b10001, 0b10000, 0b10000, 0b10000, 0b10001, 0b01110 },
            ['D'] = new byte[] { 0b11100, 0b10010, 0b10001, 0b10001, 0b10001, 0b10010, 0b11100 },
            ['E'] = new byte[] { 0b11111, 0b10000, 0b10000, 0b11110, 0b10000, 0b10000, 0b11111 },
            ['F'] = new byte[] { 0b11111, 0b10000, 0b10000, 0b11110, 0b10000, 0b10000, 0b10000 },
            ['G'] = new byte[] { 0b01110, 0b10001, 0b10000, 0b10111, 0b10001, 0b10001, 0b01111 },
            ['H'] = new byte[] { 0b10001, 0b10001, 0b10001, 0b11111, 0b10001, 0b10001, 0b10001 },
            ['I'] = new byte[] { 0b01110, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b01110 },
            ['J'] = new byte[] { 0b00111, 0b00010, 0b00010, 0b00010, 0b00010, 0b10010, 0b01100 },
            ['K'] = new byte[] { 0b10001, 0b10010, 0b10100, 0b11000, 0b10100, 0b10010, 0b10001 },
            ['L'] = new byte[] { 0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b10000, 0b11111 },
            ['M'] = new byte[] { 0b10001, 0b11011, 0b10101, 0b10101, 0b10001, 0b10001, 0b10001 },
            ['N'] = new byte[] { 0b10001, 0b11001, 0b10101, 0b10101, 0b10011, 0b10001, 0b10001 },
            ['O'] = new byte[] { 0b01110, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110 },
            ['P'] = new byte[] { 0b11110, 0b10001, 0b10001, 0b11110, 0b10000, 0b10000, 0b10000 },
            ['Q'] = new byte[] { 0b01110, 0b10001, 0b10001, 0b10001, 0b10101, 0b10010, 0b01101 },
            ['R'] = new byte[] { 0b11110, 0b10001, 0b10001, 0b11110, 0b10100, 0b10010, 0b10001 },
            ['S'] = new byte[] { 0b01111, 0b10000, 0b10000, 0b01110, 0b00001, 0b00001, 0b11110 },
            ['T'] = new byte[] { 0b11111, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100 },
            ['U'] = new byte[] { 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01110 },
            ['V'] = new byte[] { 0b10001, 0b10001, 0b10001, 0b10001, 0b10001, 0b01010, 0b00100 },
            ['W'] = new byte[] { 0b10001, 0b10001, 0b10001, 0b10101, 0b10101, 0b10101, 0b01010 },
            ['X'] = new byte[] { 0b10001, 0b10001, 0b01010, 0b00100, 0b01010, 0b10001, 0b10001 },
            ['Y'] = new byte[] { 0b10001, 0b10001, 0b01010, 0b00100, 0b00100, 0b00100, 0b00100 },
            ['Z'] = new byte[] { 0b11111, 0b00001, 0b00010, 0b00100, 0b01000, 0b10000, 0b11111 },

            ['a'] = new byte[] { 0b00000, 0b00000, 0b01110, 0b00001, 0b01111, 0b10001, 0b01111 },
            ['b'] = new byte[] { 0b10000, 0b10000, 0b10110, 0b11001, 0b10001, 0b10001, 0b11110 },
            ['c'] = new byte[] { 0b00000, 0b00000, 0b01111, 0b10000, 0b10000, 0b10000, 0b01111 },
            ['d'] = new byte[] { 0b00001, 0b00001, 0b01101, 0b10011, 0b10001, 0b10001, 0b01111 },
            ['e'] = new byte[] { 0b00000, 0b00000, 0b01110, 0b10001, 0b11111, 0b10000, 0b01110 },
            ['f'] = new byte[] { 0b00110, 0b01001, 0b01000, 0b11110, 0b01000, 0b01000, 0b01000 },
            ['g'] = new byte[] { 0b00000, 0b01111, 0b10001, 0b10001, 0b01111, 0b00001, 0b01110 },
            ['h'] = new byte[] { 0b10000, 0b10000, 0b10110, 0b11001, 0b10001, 0b10001, 0b10001 },
            ['i'] = new byte[] { 0b00100, 0b00000, 0b01100, 0b00100, 0b00100, 0b00100, 0b01110 },
            ['j'] = new byte[] { 0b00010, 0b00000, 0b00110, 0b00010, 0b00010, 0b10010, 0b01100 },
            ['k'] = new byte[] { 0b10000, 0b10000, 0b10010, 0b10100, 0b11000, 0b10100, 0b10010 },
            ['l'] = new byte[] { 0b01100, 0b00100, 0b00100, 0b00100, 0b00100, 0b00100, 0b01110 },
            ['m'] = new byte[] { 0b00000, 0b00000, 0b11010, 0b10101, 0b10101, 0b10101, 0b10101 },
            ['n'] = new byte[] { 0b00000, 0b00000, 0b10110, 0b11001, 0b10001, 0b10001, 0b10001 },
            ['o'] = new byte[] { 0b00000, 0b00000, 0b01110, 0b10001, 0b10001, 0b10001, 0b01110 },
            ['p'] = new byte[] { 0b00000, 0b11110, 0b10001, 0b10001, 0b11110, 0b10000, 0b10000 },
            ['q'] = new byte[] { 0b00000, 0b01101, 0b10011, 0b10001, 0b01111, 0b00001, 0b00001 },
            ['r'] = new byte[] { 0b00000, 0b00000, 0b10110, 0b11001, 0b10000, 0b10000, 0b10000 },
            ['s'] = new byte[] { 0b00000, 0b00000, 0b01111, 0b10000, 0b01110, 0b00001, 0b11110 },
            ['t'] = new byte[] { 0b01000, 0b01000, 0b11110, 0b01000, 0b01000, 0b01001, 0b00110 },
            ['u'] = new byte[] { 0b00000, 0b00000, 0b10001, 0b10001, 0b10001, 0b10011, 0b01101 },
            ['v'] = new byte[] { 0b00000, 0b00000, 0b10001, 0b10001, 0b10001, 0b01010, 0b00100 },
            ['w'] = new byte[] { 0b00000, 0b00000, 0b10001, 0b10101, 0b10101, 0b10101, 0b01010 },
            ['x'] = new byte[] { 0b00000, 0b00000, 0b10001, 0b01010, 0b00100, 0b01010, 0b10001 },
            ['y'] = new byte[] { 0b00000, 0b10001, 0b10001, 0b10001, 0b01111, 0b00001, 0b01110 },
            ['z'] = new byte[] { 0b00000, 0b00000, 0b11111, 0b00010, 0b00100, 0b01000, 0b11111 },

            [' '] = new byte[] { 0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b00000 },
            [':'] = new byte[] { 0b00000, 0b00100, 0b00000, 0b00000, 0b00000, 0b00100, 0b00000 },
            ['.'] = new byte[] { 0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b01100, 0b01100 },
            [','] = new byte[] { 0b00000, 0b00000, 0b00000, 0b00000, 0b00000, 0b00100, 0b01000 },
            ['%'] = new byte[] { 0b11001, 0b11010, 0b00010, 0b00100, 0b01000, 0b01011, 0b10011 },
            ['-'] = new byte[] { 0b00000, 0b00000, 0b00000, 0b11111, 0b00000, 0b00000, 0b00000 },
            ['('] = new byte[] { 0b00010, 0b00100, 0b01000, 0b01000, 0b01000, 0b00100, 0b00010 },
            [')'] = new byte[] { 0b01000, 0b00100, 0b00010, 0b00010, 0b00010, 0b00100, 0b01000 },
            ['/'] = new byte[] { 0b00001, 0b00010, 0b00010, 0b00100, 0b01000, 0b01000, 0b10000 },
        };

        /// <summary>
        /// Returns the 7-row glyph bitmap for <paramref name="c"/>, or null if this font doesn't
        /// have that character (caller should skip it, not throw - see class remarks).
        /// </summary>
        public static byte[] GetGlyph(char c) => Glyphs.TryGetValue(c, out var rows) ? rows : null;
    }
}
