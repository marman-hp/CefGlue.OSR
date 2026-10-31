namespace Xilium.CefGlue.Common.Coordinators
{
    public interface IResizeCoordinator
    {
        void Attach(CefBrowserContext context);

        void RequestResize(int width, int height);

        void HandleFrameAccepted(int width, int height);

        void ArmStallWatchdog();
    }
}
