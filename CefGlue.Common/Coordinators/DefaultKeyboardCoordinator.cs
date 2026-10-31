namespace Xilium.CefGlue.Common.Coordinators
{
    public class DefaultKeyboardCoordinator : IKeyboardCoordinator
    {
        private const int VK_ESCAPE = 0x1B;

        protected CefBrowserContext Context { get; private set; }

        public virtual void Attach(CefBrowserContext context)
        {
            Context = context;
        }

        public virtual void HandleLostFocus()
        {
            Context.WithErrorHandling(nameof(HandleLostFocus), () =>
            {
                Context.GetBrowserHost()?.SetFocus(false);
            });
        }

        public virtual void HandleTextInput(string text, out bool handled)
        {
            var _handled = false;

            Context.WithErrorHandling(nameof(HandleTextInput), () =>
            {
                var browserHost = Context.GetBrowserHost();
                if (browserHost != null)
                {
                    foreach (var c in text)
                    {
                        var keyEvent = new CefKeyEvent()
                        {
                            EventType = CefKeyEventType.Char,
                            WindowsKeyCode = c,
                            Character = c
                        };

                        browserHost.SendKeyEvent(keyEvent);
                    }

                    _handled = true;
                }
            });

            handled = _handled;
        }

        public virtual void HandleImeComposition(string text, int cursorPosition) =>
            HandleImeComposition(text, cursorPosition, null);

        public virtual void HandleImeComposition(string text, int cursorPosition, Xilium.CefGlue.Common.Platform.ImeCompositionClause[] clauses)
        {
            Context.WithErrorHandling(nameof(HandleImeComposition), () =>
            {
                var browserHost = Context.GetBrowserHost();
                if (browserHost == null)
                {
                    return;
                }

                if (string.IsNullOrEmpty(text))
                {
                    browserHost.ImeCancelComposition();
                    return;
                }

                CefCompositionUnderline[] underlines;
                if (clauses != null && clauses.Length > 0)
                {
                    underlines = new CefCompositionUnderline[clauses.Length];
                    for (var i = 0; i < clauses.Length; i++)
                    {
                        var c = clauses[i];
                        underlines[i] = new CefCompositionUnderline
                        {
                            Range = new CefRange(c.Start, c.End),
                            Color = c.ColorArgb == 0
                                ? new CefColor(255, 0, 0, 0)
                                : new CefColor(c.ColorArgb),
                            BackgroundColor = new CefColor(0, 0, 0, 0),
                            Thick = c.Thick,
                            Style = c.Style
                        };
                    }
                }
                else
                {
                    underlines = new[]
                    {
                        new CefCompositionUnderline
                        {
                            Range = new CefRange(0, text.Length),
                            Color = new CefColor(255, 0, 0, 0),
                            BackgroundColor = new CefColor(0, 0, 0, 0),
                            Thick = false,
                            Style = CefCompositionUnderlineStyle.Solid
                        }
                    };
                }

                browserHost.ImeSetComposition(
                    text,
                    underlines,
                    new CefRange(-1, -1),
                    new CefRange(cursorPosition, cursorPosition));
            });
        }

        public virtual void HandleImeCommitText(string text)
        {
            Context.WithErrorHandling(nameof(HandleImeCommitText), () =>
            {
                Context.GetBrowserHost()?.ImeCommitText(text, new CefRange(-1, -1), 0);
            });
        }

        public virtual void HandleKeyPress(CefKeyEvent keyEvent, out bool handled)
        {
            Context.WithErrorHandling(nameof(HandleKeyPress), () =>
            {
                var browserHost = Context.GetBrowserHost();
                if (browserHost != null)
                {
                    browserHost.SendKeyEvent(keyEvent);

                    if (keyEvent.WindowsKeyCode == VK_ESCAPE &&
                        keyEvent.EventType == CefKeyEventType.RawKeyDown &&
                        Context.IsFullscreen())
                    {
                        browserHost.ExitFullscreen(true);
                    }
                }
            });
            handled = false;
        }
    }
}
