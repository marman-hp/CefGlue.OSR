using System;
using Avalonia;
using Avalonia.Input.TextInput;
using Avalonia.VisualTree;

namespace Xilium.CefGlue.Avalonia.Platform
{
    internal sealed class AvaloniaImeClient : TextInputMethodClient
    {
        private readonly Visual _textView;
        private readonly Func<Rect> _getCursorRect;

        public event Action<string, int> PreeditChanged;

        public AvaloniaImeClient(Visual textView, Func<Rect> getCursorRect)
        {
            _textView = textView;
            _getCursorRect = getCursorRect;
        }

        public override Visual TextViewVisual => _textView;

        public override bool SupportsPreedit => true;

        public override bool SupportsSurroundingText => false;

        public override string SurroundingText => string.Empty;

        public override Rect CursorRectangle => _getCursorRect();

        public override TextSelection Selection
        {
            get => default;
            set { }
        }

        public override void SetPreeditText(string preeditText) => SetPreeditText(preeditText, null);

        public override void SetPreeditText(string preeditText, int? cursorPos)
        {
            var text = preeditText ?? string.Empty;
            PreeditChanged?.Invoke(text, Math.Clamp(cursorPos ?? text.Length, 0, text.Length));
        }

        public void NotifyCursorRectangleChanged() => RaiseCursorRectangleChanged();
    }
}
