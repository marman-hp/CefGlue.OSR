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
            // GTK4's default "ngl" GSK renderer has a confirmed-upstream bug specifically in virtual
            // machines (Ubuntu bugs #2061118/#2061584/#2061079) - segfaults deep in libpixman's
            // pixman_blt during cairo_paint. "gl" (the older, pre-ngl renderer) is the documented
            // workaround. This is an env var, not a CEF flag, so it's set here as a side effect
            // rather than returned - callers still need to call GetRecommendedFlags() before any
            // Gtk/Application call for it to take effect (GSK reads it at first use, not re-checked
            // later), same requirement as before this helper existed.
            Environment.SetEnvironmentVariable("GSK_RENDERER", "gl");

            var isWsl = File.Exists("/proc/version") && File.ReadAllText("/proc/version").Contains("microsoft", StringComparison.OrdinalIgnoreCase);

            // ozone-platform must match the REAL compositor session, not be hardcoded to either
            // value - a mismatch breaks native video-decode shared-image registration (confirmed
            // live: forcing "x11" on an actual Wayland session crashes the GPU process repeatedly on
            // 2K/4K video - "Could not find SharedImageBackingFactory ... MailboxVideoFrameConverter"
            // -> "FATAL: GPU process isn't usable"). XDG_SESSION_TYPE is the standard env var the
            // session/display manager sets for this - falls back to "x11" when unset/anything else,
            // matching both CEF/Chromium's own default and WSLg/VMware (which report "x11" here
            // despite being virtualized).
            var sessionType = Environment.GetEnvironmentVariable("XDG_SESSION_TYPE");
            var ozonePlatform = string.Equals(sessionType, "wayland", StringComparison.OrdinalIgnoreCase) ? "wayland" : "x11";

            var flags = new List<KeyValuePair<string, string>>
            {
                new KeyValuePair<string, string>("use-angle", "gl-egl"),
                new KeyValuePair<string, string>("ozone-platform", ozonePlatform),
                // EXPERIMENT (2026-08-06): SurfaceSyncThrottling gates BeginFrame delivery on the
                // compositor's own surface-sync bookkeeping being "caught up" - directly adjacent to
                // (possibly the same mechanism as) the hold_resize_ gating this whole investigation
                // has been chasing (project memory, "Bug baru ke-5"). Disabling it removes that
                // throttle unconditionally - not gated on useSharedTexture since it's a general
                // surface-sync behavior, not specific to either path.
                // UNCONFIRMED live yet - test alongside (and independent of) the other experiments.
                new KeyValuePair<string, string>("disable-features", "SurfaceSyncThrottling"),
            };

            // Ozone/X11 (native Xorg or XWayland - doesn't matter which) lacks a working
            // SharedImageBackingFactory for hardware-decoded Y_UV/NV12 video frames in this
            // CEF/Chromium build - only Ozone/Wayland has it wired up correctly, and there's no way
            // to force Wayland on a genuine Xorg session (no compositor socket to connect to). Only
            // safe option under the x11 branch: drop hardware video decode entirely (CPU/software
            // decode instead). Wayland keeps hardware decode (confirmed stable through 4K60/4K HDR).
            if (ozonePlatform == "x11")
            {
                flags.Add(new KeyValuePair<string, string>("disable-accelerated-video-decode", ""));
            }

            // The coupling this whole helper exists to enforce - see class remarks. WSLg's GPU stack
            // is independently broken regardless of texture mode, so isWsl always disables GPU; when
            // NOT using shared texture, CEF's GPU compositor has nothing to do for us (every frame
            // comes back through plain software OnPaint) but staying active anyway is what triggers
            // the resize-repaint-gating stall - so it goes too. When useSharedTexture is true, GPU
            // obviously must stay on outside the isWsl case - the whole point of that path is the
            // GPU-resident texture.
            //
            // Confirmed live on v146, native Linux (Xorg, non-WSL, 2026-08-06): unlike the earlier
            // v150 assumption ("tanpa GPU enable dia sama aja kek windowed, flicker di smua os"),
            // --disable-gpu does NOT introduce flicker on this setup, and does fix the stuck-after-
            // resize/lost-focus bug reliably (matching every other environment already confirmed:
            // native Xorg, native Wayland, WSLg). So the !useSharedTexture case is back to disabling
            // GPU too, not just isWsl - re-test on other native Linux setups if the flicker concern
            // resurfaces there.
            if (isWsl || !useSharedTexture)
            {
                flags.Add(new KeyValuePair<string, string>("disable-gpu", ""));
                flags.Add(new KeyValuePair<string, string>("disable-gpu-compositing", ""));
            }

            return flags.ToArray();
        }
    }
}
