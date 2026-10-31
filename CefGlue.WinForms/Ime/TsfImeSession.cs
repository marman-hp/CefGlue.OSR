using System;
using System.Collections.Generic;
using System.Threading;

namespace Xilium.CefGlue.WinForms.Ime
{
    internal interface ITsfImeClient
    {
        TsfRect? GetCompositionExt(int start, int end);

        TsfRect? GetScreenExt();

        void OnCompositionUpdated(string text, int caret, TsfClause[] clauses);

        void OnCompositionCommitted(string text);
    }

    internal sealed class TsfImeSession
    {
        private static readonly Dictionary<IntPtr, TsfImeSession> Sessions = new();

        private readonly IntPtr _hwnd;
        private readonly TsfImeController _controller;
        private ITsfImeClient _active;

        private TsfImeSession(IntPtr hwnd, SynchronizationContext ui)
        {
            _hwnd = hwnd;
            _controller = new TsfImeController(hwnd, GetCompositionExt, GetScreenExt, ui);
            _controller.CompositionUpdated += (text, caret, clauses) => _active?.OnCompositionUpdated(text, caret, clauses);
            _controller.CompositionCommitted += text => _active?.OnCompositionCommitted(text);
        }

        public static TsfImeSession GetOrCreate(IntPtr hwnd, SynchronizationContext ui)
        {
            if (hwnd == IntPtr.Zero)
            {
                return null;
            }

            lock (Sessions)
            {
                if (!Sessions.TryGetValue(hwnd, out var session))
                {
                    session = new TsfImeSession(hwnd, ui);
                    Sessions[hwnd] = session;
                }

                return session;
            }
        }

        public static void Close(IntPtr hwnd)
        {
            TsfImeSession session;
            lock (Sessions)
            {
                if (!Sessions.Remove(hwnd, out session))
                {
                    return;
                }
            }

            session._active = null;
            session._controller.Dispose();
        }

        public bool IsComposing => _controller.IsComposing;

        public bool IsActiveClient(ITsfImeClient client) => ReferenceEquals(_active, client);

        public void SetActive(ITsfImeClient client, bool active, bool clientHasKeyboardFocus)
        {
            if (active)
            {
                if (!ReferenceEquals(_active, client))
                {
                    _controller.AbandonComposition();
                    _active = client;
                }

                _controller.SetActive(true, clientHasKeyboardFocus);
                _controller.ReassertFocus();
            }
            else if (ReferenceEquals(_active, client))
            {
                _active = null;
                _controller.SetActive(false, clientHasKeyboardFocus);
            }
        }

        public void ReassertFocus(ITsfImeClient client)
        {
            if (ReferenceEquals(_active, client))
            {
                _controller.ReassertFocus();
            }
        }

        public void NotifyLayoutChanged(ITsfImeClient client)
        {
            if (ReferenceEquals(_active, client))
            {
                _controller.NotifyLayoutChanged();
            }
        }

        private TsfRect? GetCompositionExt(int start, int end) => _active?.GetCompositionExt(start, end);

        private TsfRect? GetScreenExt() => _active?.GetScreenExt();
    }
}
