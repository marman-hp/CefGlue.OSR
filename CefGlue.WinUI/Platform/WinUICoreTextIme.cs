using System;
using System.Collections.Generic;
using Microsoft.UI.Dispatching;
using Windows.Foundation;
using Windows.UI.Text;
using Windows.UI.Text.Core;
using Xilium.CefGlue.Common.Platform;

namespace Xilium.CefGlue.WinUI.Platform
{
    internal sealed class WinUICoreTextIme
    {
        private readonly CoreTextEditContext _context;
        private readonly DispatcherQueue _queue;
        private readonly Func<int, int, (Rect Text, Rect Control)> _getLayout;
        private readonly Action<string, int, ImeCompositionClause[]> _onUpdated;
        private readonly Action<string> _onCommitted;
        private readonly Action<string> _onDirectText;

        private string _document = string.Empty;
        private CoreTextRange _selection;
        private bool _composing;
        private bool _compositionStartPending;
        private int _compositionStart;
        private bool _hasFocus;

        private readonly List<Format> _formats = new();
        private bool _formatsStale;
        private bool _updateScheduled;
        private bool _resetScheduled;
        private string _directText = string.Empty;
        private bool _directScheduled;

        private readonly struct Format
        {
            public Format(int start, int end, bool target, CefCompositionUnderlineStyle style, bool thick, uint colorArgb)
            {
                Start = start;
                End = end;
                Target = target;
                Style = style;
                Thick = thick;
                ColorArgb = colorArgb;
            }

            public int Start { get; }
            public int End { get; }
            public bool Target { get; }
            public CefCompositionUnderlineStyle Style { get; }
            public bool Thick { get; }
            public uint ColorArgb { get; }
        }

        public WinUICoreTextIme(
            DispatcherQueue queue,
            Func<int, int, (Rect Text, Rect Control)> getLayout,
            Action<string, int, ImeCompositionClause[]> onUpdated,
            Action<string> onCommitted,
            Action<string> onDirectText)
        {
            _queue = queue;
            _getLayout = getLayout;
            _onUpdated = onUpdated;
            _onCommitted = onCommitted;
            _onDirectText = onDirectText;

            var manager = CoreTextServicesManager.GetForCurrentView();
            _context = manager.CreateEditContext();
            _context.InputPaneDisplayPolicy = CoreTextInputPaneDisplayPolicy.Manual;
            _context.InputScope = CoreTextInputScope.Default;

            _context.TextRequested += OnTextRequested;
            _context.SelectionRequested += OnSelectionRequested;
            _context.LayoutRequested += OnLayoutRequested;
            _context.TextUpdating += OnTextUpdating;
            _context.SelectionUpdating += OnSelectionUpdating;
            _context.FormatUpdating += OnFormatUpdating;
            _context.CompositionStarted += OnCompositionStarted;
            _context.CompositionCompleted += OnCompositionCompleted;
            _context.FocusRemoved += OnFocusRemoved;
        }

        public bool IsComposing => _composing;

        public void SetActive(bool active)
        {
            if (active == _hasFocus)
            {
                return;
            }

            _hasFocus = active;
            if (active)
            {
                _context.NotifyFocusEnter();
            }
            else
            {
                AbandonComposition();
                _context.NotifyFocusLeave();
            }
        }

        public void NotifyLayoutChanged()
        {
            if (_hasFocus)
            {
                _context.NotifyLayoutChanged();
            }
        }

        public void AbandonComposition()
        {
            if (!_composing)
            {
                return;
            }

            _composing = false;
            _formats.Clear();
            _onUpdated(string.Empty, 0, null);
            ScheduleReset();
        }

        private void OnTextRequested(CoreTextEditContext sender, CoreTextTextRequestedEventArgs args)
        {
            var range = args.Request.Range;
            var start = Math.Clamp(range.StartCaretPosition, 0, _document.Length);
            var end = Math.Clamp(range.EndCaretPosition, start, _document.Length);
            args.Request.Text = _document.Substring(start, end - start);
        }

        private void OnSelectionRequested(CoreTextEditContext sender, CoreTextSelectionRequestedEventArgs args)
        {
            args.Request.Selection = _selection;
        }

        private void OnLayoutRequested(CoreTextEditContext sender, CoreTextLayoutRequestedEventArgs args)
        {
            var range = args.Request.Range;
            var start = Math.Max(0, range.StartCaretPosition - _compositionStart);
            var end = Math.Max(start, range.EndCaretPosition - _compositionStart);

            var (text, control) = _getLayout(start, end);
            args.Request.LayoutBounds.TextBounds = text;
            args.Request.LayoutBounds.ControlBounds = control;
        }

        private void OnTextUpdating(CoreTextEditContext sender, CoreTextTextUpdatingEventArgs args)
        {
            var range = args.Range;
            var text = args.Text ?? string.Empty;
            var start = Math.Clamp(range.StartCaretPosition, 0, _document.Length);
            var end = Math.Clamp(range.EndCaretPosition, start, _document.Length);

            _document = string.Concat(_document.AsSpan(0, start), text, _document.AsSpan(end));
            _selection = ClampSelection(args.NewSelection);
            args.Result = CoreTextTextUpdatingResult.Succeeded;

            if (_composing)
            {
                if (_compositionStartPending)
                {
                    _compositionStart = start;
                    _compositionStartPending = false;
                }

                _formatsStale = true;
                ScheduleUpdate();
            }
            else if (text.Length > 0)
            {
                _directText += text;
                ScheduleDirect();
            }
        }

        private void OnSelectionUpdating(CoreTextEditContext sender, CoreTextSelectionUpdatingEventArgs args)
        {
            _selection = ClampSelection(args.Selection);
            args.Result = CoreTextSelectionUpdatingResult.Succeeded;
            if (_composing)
            {
                ScheduleUpdate();
            }
        }

        private void OnFormatUpdating(CoreTextEditContext sender, CoreTextFormatUpdatingEventArgs args)
        {
            args.Result = CoreTextFormatUpdatingResult.Succeeded;
            if (!_composing)
            {
                return;
            }

            if (_formatsStale)
            {
                _formats.Clear();
                _formatsStale = false;
            }

            var target = args.Reason == CoreTextFormatUpdatingReason.CompositionTargetConverted ||
                         args.Reason == CoreTextFormatUpdatingReason.CompositionTargetUnconverted;

            var style = CefCompositionUnderlineStyle.Solid;
            var thick = false;
            switch (args.UnderlineType)
            {
                case UnderlineType.None:
                    style = CefCompositionUnderlineStyle.None;
                    break;
                case UnderlineType.Dotted:
                    style = CefCompositionUnderlineStyle.Dot;
                    break;
                case UnderlineType.Dash:
                    style = CefCompositionUnderlineStyle.Dash;
                    break;
                case UnderlineType.Thick:
                    thick = true;
                    break;
            }

            var start = args.Range.StartCaretPosition - _compositionStart;
            var end = args.Range.EndCaretPosition - _compositionStart;
            ReplaceFormat(new Format(start, end, target, style, thick, 0));

            ScheduleUpdate();
        }

        private void ReplaceFormat(Format added)
        {
            for (var i = _formats.Count - 1; i >= 0; i--)
            {
                var old = _formats[i];
                if (old.End <= added.Start || old.Start >= added.End)
                {
                    continue;
                }

                _formats.RemoveAt(i);
                if (old.Start < added.Start)
                {
                    _formats.Add(new Format(old.Start, added.Start, old.Target, old.Style, old.Thick, old.ColorArgb));
                }

                if (old.End > added.End)
                {
                    _formats.Add(new Format(added.End, old.End, old.Target, old.Style, old.Thick, old.ColorArgb));
                }
            }

            _formats.Add(added);
        }

        private void OnCompositionStarted(CoreTextEditContext sender, CoreTextCompositionStartedEventArgs args)
        {
            _composing = true;
            _compositionStartPending = true;
            _compositionStart = _selection.StartCaretPosition;
            _formats.Clear();
            _formatsStale = false;
        }

        private void OnCompositionCompleted(CoreTextEditContext sender, CoreTextCompositionCompletedEventArgs args)
        {
            var text = _compositionStart >= _document.Length ? string.Empty : _document.Substring(_compositionStart);

            _composing = false;
            _formats.Clear();

            if (text.Length == 0)
            {
                _onUpdated(string.Empty, 0, null);
            }
            else
            {
                _onCommitted(text);
            }

            ScheduleReset();
        }

        private void OnFocusRemoved(CoreTextEditContext sender, object args)
        {
            _hasFocus = false;
            AbandonComposition();
        }

        private CoreTextRange ClampSelection(CoreTextRange selection)
        {
            var start = Math.Clamp(selection.StartCaretPosition, 0, _document.Length);
            var end = Math.Clamp(selection.EndCaretPosition, start, _document.Length);
            return new CoreTextRange { StartCaretPosition = start, EndCaretPosition = end };
        }

        private void ScheduleUpdate()
        {
            if (_updateScheduled)
            {
                return;
            }

            _updateScheduled = true;
            _queue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                _updateScheduled = false;
                if (!_composing)
                {
                    return;
                }

                var text = _compositionStart >= _document.Length ? string.Empty : _document.Substring(_compositionStart);
                var caret = Math.Clamp(_selection.EndCaretPosition - _compositionStart, 0, text.Length);
                _onUpdated(text, caret, BuildClauses(text.Length));
            });
        }

        private ImeCompositionClause[] BuildClauses(int length)
        {
            if (_formats.Count == 0 || length == 0)
            {
                return null;
            }

            var sorted = new List<Format>(_formats);
            sorted.Sort((a, b) => a.Start.CompareTo(b.Start));

            var result = new List<ImeCompositionClause>();
            var cursor = 0;
            foreach (var f in sorted)
            {
                var start = Math.Clamp(f.Start, 0, length);
                var end = Math.Clamp(f.End, start, length);
                if (end <= start || start < cursor)
                {
                    continue;
                }

                if (start > cursor)
                {
                    result.Add(new ImeCompositionClause(cursor, start, false, CefCompositionUnderlineStyle.Solid, 0));
                }

                result.Add(new ImeCompositionClause(start, end, f.Target || f.Thick, f.Style, f.ColorArgb));
                cursor = end;
            }

            if (cursor < length)
            {
                result.Add(new ImeCompositionClause(cursor, length, false, CefCompositionUnderlineStyle.Solid, 0));
            }

            return result.Count == 0 ? null : result.ToArray();
        }

        private void ScheduleDirect()
        {
            if (_directScheduled)
            {
                return;
            }

            _directScheduled = true;
            _queue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                _directScheduled = false;
                var text = _directText;
                _directText = string.Empty;
                if (text.Length > 0)
                {
                    _onDirectText(text);
                }

                ScheduleReset();
            });
        }

        private void ScheduleReset()
        {
            if (_resetScheduled)
            {
                return;
            }

            _resetScheduled = true;
            _queue.TryEnqueue(DispatcherQueuePriority.Low, () =>
            {
                _resetScheduled = false;
                if (_composing || _document.Length == 0)
                {
                    return;
                }

                var oldLength = _document.Length;
                _document = string.Empty;
                _selection = new CoreTextRange { StartCaretPosition = 0, EndCaretPosition = 0 };
                _compositionStart = 0;
                _context.NotifyTextChanged(
                    new CoreTextRange { StartCaretPosition = 0, EndCaretPosition = oldLength },
                    0,
                    _selection);
            });
        }
    }
}
