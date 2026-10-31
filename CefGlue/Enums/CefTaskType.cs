namespace Xilium.CefGlue
{
    using System;

    /// <summary>
    /// Specifies the task type variants supported by CefTaskManager.
    /// Should be kept in sync with Chromium's task_manager::Task::Type type.
    /// </summary>
    public enum CefTaskType
    {
        Unknown,
        Browser,
        Gpu,
        Zygote,
        Utility,
        Renderer,
        Extension,
        Guest,
        Plugin,
        SandboxHelper,
        DedicatedWorker,
        SharedWorker,
        ServiceWorker,
        NumValues,
    }
}
