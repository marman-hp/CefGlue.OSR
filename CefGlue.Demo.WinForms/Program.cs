using System;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using Xilium.CefGlue.Common;

namespace  Xilium.CefGlue.Demo.WinForms
{
    internal static class Program
    {
        [STAThread]
        static void Main()
        {
            var cachePath = Path.Combine(AppContext.BaseDirectory, "cef_temp");

            AppDomain.CurrentDomain.ProcessExit += delegate { Cleanup(cachePath); };

            var settings = new CefSettings()
            {

                RootCachePath = cachePath,
                WindowlessRenderingEnabled = true,
                LogFile = Path.Combine(AppContext.BaseDirectory, "cef.log"),
                UserAgent = "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/126.0.0.0 Safari/537.36",
                RemoteDebuggingPort = 9222,

            };

            settings.CommandLineArgsDisabled = false;

            var cefFlags = new System.Collections.Generic.Dictionary<string, string>
            {
                ["remote-allow-origins"] = "devtools://devtools",
            };
            CefRuntimeLoader.Initialize(settings, flags: cefFlags.ToArray());
            ApplicationConfiguration.Initialize();

            Application.SetCompatibleTextRenderingDefault(false);

           Application.Run(new DemoDI());
        }

        private static void Cleanup(string cachePath)
        {
            CefRuntimeLoader.Shutdown();

            try {
                var dirInfo = new DirectoryInfo(cachePath);
                if (dirInfo.Exists) {
                    dirInfo.Delete(true);
                }
            } catch (UnauthorizedAccessException) {
            } catch (IOException) {
            }
        }
    }
}