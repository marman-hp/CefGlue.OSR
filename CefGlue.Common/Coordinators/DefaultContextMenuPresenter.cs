using Xilium.CefGlue.Common.Helpers;
using Xilium.CefGlue.Common.Platform;

namespace Xilium.CefGlue.Common.Coordinators
{
    public class DefaultContextMenuPresenter : IContextMenuPresenter
    {
        private readonly ITarget _control;

        public DefaultContextMenuPresenter(ITarget control)
        {
            _control = control;
        }

        public virtual void HandleOpenContextMenu(CefContextMenuParams parameters, CefMenuModel model, CefRunContextMenuCallback callback)
        {
            _control.OpenContextMenu(MenuEntry.FromCefModel(model), parameters.X, parameters.Y, callback);
        }

        public virtual void HandleCloseContextMenu()
        {
            _control.CloseContextMenu();
        }
    }
}
