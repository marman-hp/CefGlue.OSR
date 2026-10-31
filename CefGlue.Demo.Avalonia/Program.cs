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
            var cachePath = Path.Combine(Path.GetTempPath(), "CefGlue_" + Guid.NewGuid().ToString().Replace("-", null));

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

            var appBuilder = AppBuilder.Configure<App>().UsePlatformDetect();

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
