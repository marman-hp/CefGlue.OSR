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
