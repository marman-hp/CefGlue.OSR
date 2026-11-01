using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Xilium.CefGlue.Common;

namespace Xilium.CefGlue.Demo.WinUI
{
    internal static class Program
    {
        [STAThread]
        private static void Main(string[] args)
        {
            var cachePath = Path.Combine(Path.GetTempPath(), "CefGlue_" + Guid.NewGuid().ToString().Replace("-", null));

            AppDomain.CurrentDomain.ProcessExit += delegate { Cleanup(cachePath); };

            var settings = new CefSettings
            {
                RootCachePath = cachePath,
                WindowlessRenderingEnabled = true,
                RemoteDebuggingPort = 9222,
            };

            var cefFlags = new[]
            {
                new KeyValuePair<string, string>("remote-allow-origins","devtools://devtools"),
            };

            // Texture-fallback test hook: CEFGLUE_DEMO_GPU_SWITCH=disable-gpu (or disable-gpu-compositing)
            var gpuSwitch = Environment.GetEnvironmentVariable("CEFGLUE_DEMO_GPU_SWITCH")?.Trim();
            if (!string.IsNullOrEmpty(gpuSwitch))
            {
                cefFlags = [.. cefFlags, new KeyValuePair<string, string>(gpuSwitch, null)];
            }
            if (Environment.GetEnvironmentVariable("CEFGLUE_DEMO_NO_TEXTURE_FALLBACK")?.Trim() == "1")
            {
                Xilium.CefGlue.WinUI.CefGlueBrowser.AllowTextureFallback = false;
            }

            CefRuntimeLoader.Initialize(settings, flags: cefFlags);

            Application.Start(p =>
            {
                var context = new DispatcherQueueSynchronizationContext(DispatcherQueue.GetForCurrentThread());
                SynchronizationContext.SetSynchronizationContext(context);
                new App();
            });
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
