namespace Xilium.CefGlue.Common.Coordinators
{
    public interface IContextMenuPresenter
    {
        void HandleOpenContextMenu(CefContextMenuParams parameters, CefMenuModel model, CefRunContextMenuCallback callback);

        void HandleCloseContextMenu();
    }
}
