namespace Xilium.CefGlue.Common.Coordinators
{
    public interface IKeyboardCoordinator
    {
        void Attach(CefBrowserContext context);

        void HandleLostFocus();

        void HandleKeyPress(CefKeyEvent keyEvent, out bool handled);

        void HandleTextInput(string text, out bool handled);

        void HandleImeComposition(string text, int cursorPosition);

        void HandleImeComposition(string text, int cursorPosition, Xilium.CefGlue.Common.Platform.ImeCompositionClause[] clauses)
            => HandleImeComposition(text, cursorPosition);

        void HandleImeCommitText(string text);
    }
}
