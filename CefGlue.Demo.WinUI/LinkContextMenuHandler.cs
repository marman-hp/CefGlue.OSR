using System;
using Xilium.CefGlue.Common.Handlers;

namespace Xilium.CefGlue.Demo.WinUI
{
    /// <summary>Adds "Open link in new tab" at the top of the context menu when right-clicking a link.</summary>
    internal sealed class LinkContextMenuHandler : ContextMenuHandler
    {
        private const int OpenLinkInNewTabCommand = (int)CefMenuId.UserFirst;

        private readonly Action<string> _openInNewTab;

        public LinkContextMenuHandler(Action<string> openInNewTab) => _openInNewTab = openInNewTab;

        protected override void OnBeforeContextMenu(CefBrowser browser, CefFrame frame, CefContextMenuParams state, CefMenuModel model)
        {
            if (_openInNewTab != null && (state.ContextMenuType & CefContextMenuTypeFlags.Link) != 0 && !string.IsNullOrEmpty(state.LinkUrl))
            {
                model.InsertItemAt(0, OpenLinkInNewTabCommand, "Open link in new tab");
                model.InsertSeparatorAt(1);
            }
        }

        protected override bool OnContextMenuCommand(CefBrowser browser, CefFrame frame, CefContextMenuParams state, int commandId, CefEventFlags eventFlags)
        {
            if (commandId != OpenLinkInNewTabCommand)
            {
                return false;
            }

            _openInNewTab(state.LinkUrl);
            return true;
        }
    }
}
