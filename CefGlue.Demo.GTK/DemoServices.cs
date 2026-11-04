using System;
using System.Threading;
using Xilium.CefGlue.Common.Coordinators;

namespace Xilium.CefGlue.Demo.GTK
{
    /// <summary>App service registered in the CefGlueHost container; coordinators get it injected.</summary>
    public interface IActivityLog
    {
        void Log(string message);
    }

    /// <summary>Marshals log lines onto the GTK thread (coordinators run on CEF and GTK threads).</summary>
    internal sealed class WindowActivityLog : IActivityLog
    {
        private readonly SynchronizationContext _uiContext;
        private readonly Action<string> _append;

        public WindowActivityLog(SynchronizationContext uiContext, Action<string> append)
        {
            _uiContext = uiContext;
            _append = append;
        }

        public void Log(string message) => _uiContext.Post(_ => _append(message), null);
    }

    /// <summary>
    /// DI example: replaces the default keyboard coordinator (builder.Services.ReplaceCoordinator), keeps
    /// its behaviour and logs every key through the injected IActivityLog.
    /// </summary>
    internal sealed class LoggingKeyboardCoordinator : DefaultKeyboardCoordinator
    {
        private readonly IActivityLog _activityLog;

        public LoggingKeyboardCoordinator(IActivityLog activityLog)
        {
            _activityLog = activityLog;
        }

        public override void HandleKeyPress(CefKeyEvent keyEvent, out bool handled)
        {
            base.HandleKeyPress(keyEvent, out handled);
            if (keyEvent.EventType == CefKeyEventType.RawKeyDown)
            {
                _activityLog.Log($"[LoggingKeyboardCoordinator] KeyDown: WindowsKeyCode={keyEvent.WindowsKeyCode}");
            }
        }

        public override void HandleTextInput(string text, out bool handled)
        {
            base.HandleTextInput(text, out handled);
            _activityLog.Log($"[LoggingKeyboardCoordinator] Text input: \"{text}\"");
        }

        public override void HandleLostFocus()
        {
            base.HandleLostFocus();
            _activityLog.Log("[LoggingKeyboardCoordinator] Lost focus");
        }
    }
}
