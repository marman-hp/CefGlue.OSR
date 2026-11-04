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
            var useSharedTexture = args.Contains("texture")
                || Environment.GetEnvironmentVariable("CEFGLUE_DEMO_TEXTURE_CONTROL")?.Trim() == "1";
            Console.WriteLine($"[demo] {(useSharedTexture ? "TextureControl (GL dma-buf)" : "BitmapControl (software)")}");

            var runtimeDir = Path.Combine(AppContext.BaseDirectory, "runtimes", "linux-x64", "native");
            var cachePath = Path.Combine(AppContext.BaseDirectory, "cef_temp");

            var settings = new CefSettings
            {
                RootCachePath = cachePath,
                WindowlessRenderingEnabled = true,
                ResourcesDirPath = runtimeDir,
                LocalesDirPath = Path.Combine(runtimeDir, "locales"),
                NoSandbox = true,
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
