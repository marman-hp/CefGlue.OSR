using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Documents;
using Xilium.CefGlue.Common;

namespace Xilium.CefGlue.Demo.WPF
{
    internal static class Program
    {
        private const bool UseDiDemo = true;

        [STAThread]
        private static void Main()
        {
            var cachePath = Path.Combine(AppContext.BaseDirectory, "cef_temp");

            AppDomain.CurrentDomain.ProcessExit += delegate { Cleanup(cachePath); };

            var settings = new CefSettings
            {
                RootCachePath = cachePath,
                WindowlessRenderingEnabled = true,
                LogFile = Path.Combine(AppContext.BaseDirectory, "cef.log"),
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36",
                CommandLineArgsDisabled = false,
                RemoteDebuggingPort = 9222,

            };

            var cefFlags = new[]
            {
                new KeyValuePair<string, string>("remote-allow-origins","devtools://devtools"),
            };

            CefRuntimeLoader.Initialize(settings, flags: cefFlags);

            var app = new Application();
            app.Run(UseDiDemo ? new DemoDI() : new MainWindow());
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
            catch (UnauthorizedAccessException) { }
            catch (IOException) { }
        }
    }
}
