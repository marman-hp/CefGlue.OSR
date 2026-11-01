using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Gtk;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.GTK;

namespace Xilium.CefGlue.Demo.GTK
{
    internal static class Program
    {
        private static void Main(string[] args)
        {
            // Single source of truth for whether this run uses the accelerated/shared-texture path
            // (SharedTextureEnabled + GtkRenderSurface's mmap fast path) or plain software OnPaint -
            // passed to BOTH CefGlueGtkFlags.GetRecommendedFlags (below) and every CefWindowInfo this
            // process creates (see setupCefWindowInfo below). Do not set these independently - see
            // CefGlueGtkFlags' own remarks for why they have to agree (project memory, "Bug baru
            // ke-5": a mismatch doesn't crash or log anything, it manifests as OSR frames silently
            // freezing after a resize/page-navigation until a mouse hover/wheel scroll nudges CEF's
            // internal resize-repaint gating loose again).
            const bool useSharedTexture = false;

            var basePath = AppContext.BaseDirectory;

            // Unlike Windows, CEF's Linux redist isn't an official NuGet package (see project
            // memory) - these files are expected to be copied into the output directory manually
            // (see setup-cef-runtime.sh next to this file) from an already-downloaded
            // cef_binary_*_linux64 distribution, instead of arriving automatically via
            // CefGlue.CopyLocal.props the way the Windows demos get them. Mirrors the Windows
            // demos' own runtimes/win-x64/native convention (see CefGlue.CopyLocal.props/
            // CefRuntime.Load()) - runtimes/linux-x64/native here - rather than dumping everything
            // flat into the output root.
            var runtimeDir = Path.Combine(basePath, "runtimes", "linux-x64", "native");
            var cachePath = Path.Combine(Path.GetTempPath(), "CefGlue_" + Guid.NewGuid().ToString().Replace("-", null));

            var settings = new CefSettings
            {
                RootCachePath = cachePath,
                WindowlessRenderingEnabled = true,
                ResourcesDirPath = runtimeDir,
                LocalesDirPath = Path.Combine(runtimeDir, "locales"),
                NoSandbox = true,
            };

            AppDomain.CurrentDomain.ProcessExit += delegate { CefRuntimeLoader.Shutdown(); };

            // Encapsulates every Linux-specific CEF flag/env-var quirk this GTK head needs
            // (GSK_RENDERER workaround, WSLg/native GPU detection, Xorg/Wayland ozone-platform
            // detection, and the useSharedTexture<->disable-gpu coupling) - see CefGlueGtkFlags'
            // own remarks and project memory ("Bug baru ke-5") for the full rationale behind each
            // one. Must be called with the SAME useSharedTexture value passed to every
            // CefWindowInfo this process creates (see setupCefWindowInfo below).
            var flags = CefGlueGtkFlags.GetRecommendedFlags(useSharedTexture);

            CefRuntimeLoader.Initialize(settings, flags: flags);

            var application = Application.New("org.cefglue.demo.gtk", Gio.ApplicationFlags.FlagsNone);
            application.OnActivate += (sender, eventArgs) =>
            {
                var browser = new CefGlueBrowser(
                    setupCefBrowserSettings: () => new CefBrowserSettings
                    {
                        WindowlessFrameRate = 60,
                        BackgroundColor = new CefColor(255, 255, 255, 255),
                    },
                    setupCefWindowInfo: () =>
                    {
                        var windowInfo = CefWindowInfo.Create();
                        windowInfo.SharedTextureEnabled = useSharedTexture;
                        return windowInfo;
                    },
                    // EXPERIMENT tried and FAILED (2026-08-05): true GPU->GPU passthrough
                    // (TextureControl/GtkTextureRenderSurface) - dma-buf imported straight into a
                    // Gdk.Texture and handed to Gtk.Picture, gdk_texture_download() never called at
                    // all. Confirmed live: STILL hits the exact same
                    // "gsk_gl_driver_begin_frame: assertion 'self->in_frame == FALSE' failed" ->
                    // blank window -> hang/Killed as the old gdk_texture_download-based hang, even
                    // though this path never calls gdk_texture_download - proves the reentrancy issue
                    // isn't specific to that one call, it's broader (touching a dma-buf-backed
                    // Gdk.Texture at all from outside GSK's own render-cycle-safe call sites, from
                    // this codebase's Post-marshaled callback pattern). See project memory for the
                    // full writeup. Back to false - the mmap-based GtkRenderSurface CPU-readback
                    // path (BitmapControl) is what's actually proven stable, keep using that until
                    // there's a real fix for the GSK reentrancy itself.
                    useTextureControl: false);

                browser.Address = "https://www.google.com";

                var window = ApplicationWindow.New((Application)sender);
                window.Title = "CefGlue GTK Demo";

                var reloadButton = Button.NewWithLabel("Reload");
                reloadButton.OnClicked += (_, _) => browser.Reload();

                // Back/Forward + address bar (2026-08-17, at request).
                var backButton = Button.NewWithLabel("<");
                backButton.OnClicked += (_, _) => { if (browser.CanGoBack) browser.GoBack(); };

                var forwardButton = Button.NewWithLabel(">");
                forwardButton.OnClicked += (_, _) => { if (browser.CanGoForward) browser.GoForward(); };

                var addressEntry = Entry.New();
                addressEntry.SetText(browser.Address);
                addressEntry.Hexpand = true;
                addressEntry.OnActivate += (_, _) => browser.Address = addressEntry.GetText();

                var goButton = Button.NewWithLabel("Go");
                goButton.OnClicked += (_, _) => browser.Address = addressEntry.GetText();

                var toolbar = Box.New(Orientation.Horizontal, 4);
                toolbar.Append(backButton);
                toolbar.Append(forwardButton);
                toolbar.Append(reloadButton);
                toolbar.Append(addressEntry);
                toolbar.Append(goButton);
                toolbar.SetMarginTop(4);
                toolbar.SetMarginBottom(4);
                toolbar.SetMarginStart(4);
                toolbar.SetMarginEnd(4);

                var layout = Box.New(Orientation.Vertical, 0);
                layout.Append(toolbar);
                layout.Append(browser.Widget);
                browser.Widget.Vexpand = true;

                // Nothing here ever calls Maximize() - some window managers (confirmed on native
                // Linux, 2026-08-06) auto-maximize/snap a new top-level window whose requested size
                // is close to or exceeds the available screen area (e.g. 1280x800 vs a 14" laptop's
                // 1366x768 - taller than the actual work area). Unmaximize() right after Show() was
                // NOT enough (confirmed live - this isn't a real "maximized" WM state to undo, the
                // WM just fits the too-large request to the screen), so the actual fix is requesting
                // a size comfortably smaller than any common laptop panel instead.
                window.SetDefaultSize(1000, 650);
                window.Child = layout;
                window.Show();
            };

            application.RunWithSynchronizationContext(null);
        }
    }
}
