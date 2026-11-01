namespace Xilium.CefGlue.Common
{
    /// <summary>
    /// How an off-screen browser's frames reach the screen.
    /// </summary>
    public enum OsrRenderMode
    {
        /// <summary>CPU bitmap from CEF's software paint (OnPaint); shared textures off.</summary>
        Bitmap,

        /// <summary>CEF renders on the GPU (shared textures on); each frame is read back to a CPU bitmap.</summary>
        BitmapReadback,

        /// <summary>GPU passthrough: CEF's shared texture is presented without a CPU copy.</summary>
        Texture,

        /// <summary>
        /// Started as Texture, but the GPU path failed at runtime; frames now go through the CPU
        /// (read back, or OnPaint if CEF itself stopped GPU compositing).
        /// </summary>
        TextureDegraded,
    }
}
