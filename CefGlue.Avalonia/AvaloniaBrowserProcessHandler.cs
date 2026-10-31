using System;
using System.Threading;
using Avalonia.Threading;
using Xilium.CefGlue.Common.Handlers;

namespace Xilium.CefGlue.Avalonia
{
    internal class AvaloniaBrowserProcessHandler : BrowserProcessHandler
    {
        private Timer _current;
        private readonly object _schedule = new object();

        protected override void OnScheduleMessagePumpWork(long delayMs)
        {
            lock (_schedule)
            {
                _current?.Dispose();

                if (delayMs <= 0)
                {
                    delayMs = 1;
                }

                _current = new Timer(_ =>
                {
                    Dispatcher.UIThread.Post(() => CefRuntime.DoMessageLoopWork());
                }, null, delayMs, Timeout.Infinite);
            }
        }
    }
}
