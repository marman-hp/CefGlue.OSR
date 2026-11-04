using System;
using System.Collections.Generic;
using System.IO;
using Avalonia;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Shared;

namespace Xilium.CefGlue.Demo.Avalonia
{
    class Program
    {
        [STAThread]
        static int Main(string[] args)
        {
            var cachePath = Path.Combine(AppContext.BaseDirectory, "cef_temp");

            AppDomain.CurrentDomain.ProcessExit += delegate { Cleanup(cachePath); };

            var settings = new CefSettings()
            {
                RootCachePath = cachePath,
                WindowlessRenderingEnabled = true,
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36",
                RemoteDebuggingPort = 9222,
            };

            var cefFlags = new[]
            {
                new KeyValuePair<string, string>("remote-allow-origins", "devtools://devtools"),
            };

            // Texture-fallback test hooks: CEFGLUE_DEMO_GPU_SWITCH=disable-gpu (or disable-gpu-compositing),
            // CEFGLUE_DEMO_NO_TEXTURE_FALLBACK=1.
            var gpuSwitch = Environment.GetEnvironmentVariable("CEFGLUE_DEMO_GPU_SWITCH")?.Trim();
            if (!string.IsNullOrEmpty(gpuSwitch))
            {
                cefFlags = [.. cefFlags, new KeyValuePair<string, string>(gpuSwitch, null)];
            }
            // Same Linux flags as CefGlue.GTK's CefGlueGtkFlags: ozone platform from the session type, no
            // accelerated video decode on X11, and the GL backend its dma-buf texture path uses.
            if (OperatingSystem.IsLinux())
            {
                var wayland = string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase);
                cefFlags = [.. cefFlags, new KeyValuePair<string, string>("ozone-platform", wayland ? "wayland" : "x11")];
                if (!wayland)
                {
                    cefFlags = [.. cefFlags, new KeyValuePair<string, string>("disable-accelerated-video-decode", null)];
                }
                if (Environment.GetEnvironmentVariable("CEFGLUE_DEMO_TEXTURE_CONTROL")?.Trim() == "1")
                {
                    cefFlags = [.. cefFlags, new KeyValuePair<string, string>("use-angle", "gl-egl")];
                }
            }
            if (Environment.GetEnvironmentVariable("CEFGLUE_DEMO_NO_TEXTURE_FALLBACK")?.Trim() == "1")
            {
                Xilium.CefGlue.Avalonia.CefGlueBrowser.AllowTextureFallback = false;
            }

            var appBuilder = AppBuilder.Configure<App>().UsePlatformDetect();

            if (OperatingSystem.IsLinux())
            {
                // The Linux texture path imports CEF's dma-bufs through EGL; a GLX context can't do that.
                appBuilder = appBuilder.With(new X11PlatformOptions
                {
                    RenderingMode = new[] { X11RenderingMode.Egl, X11RenderingMode.Glx, X11RenderingMode.Software },
                    // Avalonia only turns IME (IBus/Fcitx) on by itself for a Chinese, Japanese, Vietnamese or
                    // Korean user language; an English desktop with Mozc/Pinyin installed would get none.
                    EnableIme = true,
                });
            }

            appBuilder.AfterSetup(_ => CefRuntimeLoader.Initialize(settings, flags: cefFlags))
                      .StartWithClassicDesktopLifetime(args);

            return 0;
        }

        private static void Cleanup(string cachePath)
        {
            CefRuntimeLoader.Shutdown();

            try
            {
                var dirInfo = new DirectoryInfo(cachePath);
                if (dirInfo.Exists)
                {
                    dirInfo.Delete(true);
                }
            }
            catch (UnauthorizedAccessException)
            {
            }
            catch (IOException)
            {
            }
        }
    }
}
