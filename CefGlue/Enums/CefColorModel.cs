namespace Xilium.CefGlue
{
    /// <summary>
    /// Print job color mode values.
    /// </summary>
    public enum CefColorModel
    {
        Unknown,
        Gray,
        Color,
        Cmyk,
        Cmy,
        Kcmy,
        Cmy_K,
        Black,
        Grayscale,
        Rgb,
        Rgb16,
        Rgba,

        ColorMode_Color,
        ColorMode_Monochrome,

        HP_Color_Color,
        HP_Color_Black,

        PrintoutMode_Normal,
        PrintoutMode_Normal_Gray,

        ProcessColorModel_Cmyk,
        ProcessColorModel_Greyscale,
        ProcessColorModel_Rgb,
        NumValues,
    }
}
