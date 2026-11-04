using System;
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
            // "texture" argument (./run.sh texture) or CEFGLUE_DEMO_TEXTURE_CONTROL=1: GtkGLArea + EGL dma-buf
            // import (TextureControl). Default: software OnPaint (BitmapControl). The same value has to go to
            // CefGlueGtkFlags.GetRecommendedFlags and to every browser's SharedTextureEnabled (see
            // CefGlueGtkFlags: a mismatch silently freezes frames after a resize).
            var useSharedTexture = args.Contains("texture")
                || Environment.GetEnvironmentVariable("CEFGLUE_DEMO_TEXTURE_CONTROL")?.Trim() == "1";
            Console.WriteLine($"[demo] {(useSharedTexture ? "TextureControl (GL dma-buf)" : "BitmapControl (software)")}");

            // CEF's Linux redist isn't a NuGet package: run.sh's package (or setup-cef-runtime.sh) puts it
            // in runtimes/linux-x64/native.
            var runtimeDir = Path.Combine(AppContext.BaseDirectory, "runtimes", "linux-x64", "native");
            var cachePath = Path.Combine(AppContext.BaseDirectory, "cef_temp");

            var settings = new CefSettings
            {
                RootCachePath = cachePath,
                WindowlessRenderingEnabled = true,
                ResourcesDirPath = runtimeDir,
                LocalesDirPath = Path.Combine(runtimeDir, "locales"),
                NoSandbox = true,
                // File > Open Dev Tools attaches the inspector to the page through this port.
                RemoteDebuggingPort = 9222,
            };

            AppDomain.CurrentDomain.ProcessExit += delegate
            {
                CefRuntimeLoader.Shutdown();
                try
                {
                    Directory.Delete(cachePath, true);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            };

            // GSK_RENDERER workaround, ozone platform from the session type, no accelerated video decode on
            // X11, and GPU on/off to match useSharedTexture - see CefGlueGtkFlags.
            var flags = CefGlueGtkFlags.GetRecommendedFlags(useSharedTexture)
                .Append(new System.Collections.Generic.KeyValuePair<string, string>("remote-allow-origins", "devtools://devtools"))
                .ToArray();

            CefRuntimeLoader.Initialize(settings, flags: flags);

            var application = Application.New("org.cefglue.demo.gtk", Gio.ApplicationFlags.FlagsNone);
            application.OnActivate += (sender, _) => new MainWindow((Application)sender, useSharedTexture).Show();
            application.RunWithSynchronizationContext(null);
        }
    }
}
