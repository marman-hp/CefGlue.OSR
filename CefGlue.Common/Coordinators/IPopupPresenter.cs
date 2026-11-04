using System;

namespace Xilium.CefGlue.Common.Coordinators
{
    /// <summary>
    /// Decides WHERE a window.open popup (e.g. "Sign in with Google") appears: its own window, a tab, a dialog...
    /// The library still owns the popup's lifecycle - it creates the browser, closes it when the page calls
    /// window.close() and disposes it afterwards - so a presenter only puts the popup's control in the UI.
    /// <typeparamref name="TBrowser"/> is the platform's CefGlueBrowser. Each platform registers a default (an
    /// owned window); replace it with
    /// <c>builder.Services.ReplaceCoordinator&lt;IPopupPresenter&lt;CefGlueBrowser&gt;, YourPresenter&gt;()</c>.
    /// </summary>
    /// <remarks>
    /// Rules: <see cref="Show"/> and <see cref="IPopupHost.Close"/> are called on the UI thread; never dispose the
    /// popup browser yourself; raise <see cref="IPopupHost.Closed"/> once, also after the library called Close().
    /// Popups opened from inside a popup go through the same presenter.
    /// </remarks>
    public interface IPopupPresenter<TBrowser> where TBrowser : class
    {
        /// <summary>Called on the UI thread once CEF created the popup: put the popup's control in the UI.</summary>
        IPopupHost Show(PopupRequest<TBrowser> request);
    }

    /// <summary>The piece of UI holding one popup.</summary>
    public interface IPopupHost
    {
        /// <summary>The page closed itself (window.close): remove the popup from the UI. May be called more than once.</summary>
        void Close();

        /// <summary>The popup's UI is gone (the user closed it, or after <see cref="Close"/>). The library then closes the browser.</summary>
        event Action Closed;
    }

    public sealed class PopupRequest<TBrowser> where TBrowser : class
    {
        internal PopupRequest(TBrowser opener, TBrowser popup, CefPopupFeatures features)
        {
            Opener = opener;
            Popup = popup;
            X = features?.X;
            Y = features?.Y;
            Width = features?.Width;
            Height = features?.Height;
        }

        /// <summary>The browser that called window.open.</summary>
        public TBrowser Opener { get; }

        /// <summary>The popup's browser: put its Control in the UI.</summary>
        public TBrowser Popup { get; }

        /// <summary>Position and size the page asked for (window.open features); null when it didn't say.</summary>
        public int? X { get; }

        public int? Y { get; }

        public int? Width { get; }

        public int? Height { get; }
    }
}
