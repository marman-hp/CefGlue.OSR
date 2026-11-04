using System;
using System.Collections.Generic;
using System.IO;

namespace Xilium.CefGlue.GTK
{
    /// <summary>
    /// CEF command-line flags this GTK head needs to run correctly on Linux - encapsulates several
    /// environment-detection quirks (WSLg vs native, Xorg vs Wayland) and one non-obvious coupling
    /// that's easy to get wrong by hand: whether GPU compositing should be enabled has to agree with
    /// whether the browser(s) you're about to create will ask for <see cref="CefWindowInfo.
    /// SharedTextureEnabled"/> - these are two calls (this one at CEF init time, the other per-
    /// browser via setupCefWindowInfo) that used to live in two independently-hand-edited places in
    /// the demo app and could silently drift out of sync. Getting it wrong doesn't crash or log
    /// anything - it manifests as OSR frames appearing to freeze after a resize or page navigation
    /// until a mouse hover/wheel scroll nudges CEF's internal resize-repaint gating loose again (see
    /// project memory, "Bug baru ke-5", for the full investigation - confirmed live across native
    /// Xorg, native Wayland, and WSLg).
    ///
    /// Usage: call <see cref="GetRecommendedFlags"/> with the SAME useSharedTexture value you're
    /// about to pass to every CefWindowInfo you create for this process, and pass its result as the
    /// flags: argument to CefRuntimeLoader.Initialize - before creating any CefGlueBrowser.
    /// </summary>
    public static class CefGlueGtkFlags
    {
        /// <param name="useSharedTexture">
        /// Must match whatever you're about to set CefWindowInfo.SharedTextureEnabled to for every
        /// browser this process creates - this type doesn't support mixing accelerated and
        /// software-only browsers in the same process (CEF's GPU compositor is a single process-wide
        /// resource, not something that can be toggled per-browser).
        /// </param>
        public static KeyValuePair<string, string>[] GetRecommendedFlags(bool useSharedTexture)
        {
            Environment.SetEnvironmentVariable("GSK_RENDERER", "gl");

            var isWsl = File.Exists("/proc/version") && File.ReadAllText("/proc/version").Contains("microsoft", StringComparison.OrdinalIgnoreCase);

            var sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
            var ozonePlatform = string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase) ? "wayland" : "x11";

            var flags = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("use-angle", "gl-egl"),
                new KeyValuePair<string, string>("ozone-platform", ozonePlatform),
                new KeyValuePair<string, string>("disable-features", "SurfaceSyncThrottling"),
            };

            if (ozonePlatform == "x11")
            {
                flags.Add(new KeyValuePair<string, string>("disable-accelerated-video-decode", ""));
            }

            if (isWsl || !useSharedTexture)
            {
                flags.Add(new KeyValuePair<string, string>("disable-gpu", ""));
                flags.Add(new KeyValuePair<string, string>("disable-gpu-compositing", ""));
            }

            return flags.ToArray();
        }
    }
}
