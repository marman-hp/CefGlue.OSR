using System;
using System.Diagnostics;
using System.Threading.Tasks;

namespace Xilium.CefGlue.BrowserProcess.Helpers
{
    internal static class ParentProcessMonitor
    {
        public static void StartMonitoring(int parentProcessId)
        {
            Task.Factory.StartNew(() => AwaitParentProcessExit(parentProcessId), TaskCreationOptions.LongRunning);
        }

        private static async void AwaitParentProcessExit(int parentProcessId)
        {
            try
            {
                var parentProcess = Process.GetProcessById(parentProcessId);
                parentProcess.WaitForExit();
            }
            catch
            {
            }

            await Task.Delay(TimeSpan.FromSeconds(10));

            Environment.Exit(0);
        }
    }
}
