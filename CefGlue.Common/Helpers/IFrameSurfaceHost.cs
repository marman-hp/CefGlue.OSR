namespace Xilium.CefGlue.Common.Helpers
{
    public interface IFrameSurfaceHost<T>
    {
        void AttachSurface(IFrameSurface<T> surface);
    }
}
