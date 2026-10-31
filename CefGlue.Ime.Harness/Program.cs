using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Threading;
using System.Windows.Forms;
using Xilium.CefGlue.WinForms.Ime;

[GeneratedComClass]
internal sealed partial class EditSession : ITfEditSession
{
    private readonly Func<uint, int> _body;
    public EditSession(Func<uint, int> body) => _body = body;
    public int DoEditSession(uint ec) => _body(ec);
}

[GeneratedComClass]
internal sealed partial class CompSink : ITfCompositionSink
{
    public int OnCompositionTerminated(uint ecWrite, IntPtr pComposition) => 0;
}

internal static unsafe class Program
{
    private static int _failures;
    private static readonly List<string> Log = new();

    private static void Expect(string what, bool ok)
    {
        Console.WriteLine($"  [{(ok ? "PASS" : "FAIL")}] {what}");
        if (!ok) _failures++;
    }

    private static void Pump()
    {
        for (var i = 0; i < 5; i++) { Application.DoEvents(); Thread.Sleep(10); }
    }

    [STAThread]
    private static int Main()
    {
        Application.EnableVisualStyles();
        using var form = new Form { Width = 400, Height = 300 };
        form.Show();
        Application.DoEvents();

        var ui = SynchronizationContext.Current ?? throw new InvalidOperationException("no UI sync context");
        var rect = new TsfRect { left = 100, top = 100, right = 116, bottom = 120 };
        using var ime = new TsfImeController(form.Handle, (s, e) => rect, () => rect, ui);
        TsfClause[] lastClauses = null;
        ime.CompositionUpdated += (t, c, clauses) => { lastClauses = clauses; Log.Add($"U:{t}|{c}"); };
        ime.CompositionCommitted += t => Log.Add($"C:{t}");

        ime.SetActive(true, controlHasFocus: true);
        Console.WriteLine("controller up, doc associated");

        var clientIdSvc = (ITfClientId)(object)ime.ThreadMgr;
        Marshal.ThrowExceptionForHR(clientIdSvc.GetClientId(Guid.NewGuid(), out var tipId));
        Console.WriteLine($"owner client {ime.ClientId}, tip client {tipId}");

        var ctx = ime.Context;
        ITfComposition comp = null;
        var sink = new CompSink();

        int Session(Func<uint, int> body, string name)
        {
            var es = new EditSession(body);
            var hr = ctx.RequestEditSession(tipId, es, Tsf.TF_ES_SYNC | Tsf.TF_ES_READWRITE, out var hrSession);
            Console.WriteLine($"  session '{name}': hr=0x{hr:X8} hrSession=0x{hrSession:X8}");
            return hrSession;
        }

        void SetCompText(uint ec, string s)
        {
            Marshal.ThrowExceptionForHR(comp.GetRange(out var r));
            fixed (char* p = s) Marshal.ThrowExceptionForHR(r.SetText(ec, 0, p, s.Length));
        }

        Console.WriteLine("scenario 1: compose 'に' -> 'にほん' -> commit '日本'");
        Log.Clear();
        Session(ec =>
        {
            var ins = (ITfInsertAtSelection)(object)ctx;
            Marshal.ThrowExceptionForHR(ins.InsertTextAtSelection(ec, Tsf.TS_IAS_QUERYONLY, null, 0, out var range));
            var cc = (ITfContextComposition)(object)ctx;
            Marshal.ThrowExceptionForHR(cc.StartComposition(ec, range, sink, out comp));
            SetCompText(ec, "に");
            return 0;
        }, "start+に");
        Expect("update 'に' reported", Log.Contains("U:に|1"));

        Session(ec => { SetCompText(ec, "にほん"); return 0; }, "にほん");
        Expect("update 'にほん' reported", Log.Contains("U:にほん|3"));

        Session(ec => { SetCompText(ec, "日本"); Marshal.ThrowExceptionForHR(comp.EndComposition(ec)); return 0; }, "commit 日本");
        Expect("commit '日本' reported", Log.Contains("C:日本"));
        Pump();

        Console.WriteLine("scenario 2: compose 'あ' then cancel (empty text, end)");
        Log.Clear();
        Session(ec =>
        {
            var ins = (ITfInsertAtSelection)(object)ctx;
            Marshal.ThrowExceptionForHR(ins.InsertTextAtSelection(ec, Tsf.TS_IAS_QUERYONLY, null, 0, out var range));
            var cc = (ITfContextComposition)(object)ctx;
            Marshal.ThrowExceptionForHR(cc.StartComposition(ec, range, sink, out comp));
            SetCompText(ec, "あ");
            return 0;
        }, "start+あ");
        Session(ec => { SetCompText(ec, ""); Marshal.ThrowExceptionForHR(comp.EndComposition(ec)); return 0; }, "cancel");
        Expect("cancel reported as empty update", Log.Contains("U:|0"));
        Expect("no commit on cancel", !Log.Exists(l => l.StartsWith("C:")));
        Pump();

        Console.WriteLine("scenario 3: second composition after document reset");
        Log.Clear();
        Session(ec =>
        {
            var ins = (ITfInsertAtSelection)(object)ctx;
            Marshal.ThrowExceptionForHR(ins.InsertTextAtSelection(ec, Tsf.TS_IAS_QUERYONLY, null, 0, out var range));
            var cc = (ITfContextComposition)(object)ctx;
            Marshal.ThrowExceptionForHR(cc.StartComposition(ec, range, sink, out comp));
            SetCompText(ec, "か");
            return 0;
        }, "start+か");
        Session(ec => { SetCompText(ec, "漢字"); Marshal.ThrowExceptionForHR(comp.EndComposition(ec)); return 0; }, "commit 漢字");
        Expect("update 'か'", Log.Contains("U:か|1"));
        Expect("commit '漢字' only (not earlier text)", Log.Contains("C:漢字"));
        Pump();

        Console.WriteLine("scenario 4: direct insert (no composition)");
        Log.Clear();
        Session(ec =>
        {
            var ins = (ITfInsertAtSelection)(object)ctx;
            fixed (char* p = "😀")
                Marshal.ThrowExceptionForHR(ins.InsertTextAtSelection(ec, Tsf.TF_IAS_NO_DEFAULT_COMPOSITION, p, 2, out _));
            return 0;
        }, "insert 😀");
        Console.WriteLine("  log: " + string.Join(", ", Log));
        Expect("direct insert committed", Log.Contains("C:😀"));
        Pump();

        Console.WriteLine("scenario 5: two clauses, the second one is the active (target) clause");
        var cat = Tsf.CreateInstance<ITfCategoryMgr>(new Guid("a4b544a1-438d-4b41-9325-869523e2d6c7"), new Guid("c3acefb5-f69d-4905-938f-fcadcf4be830"));
        Marshal.ThrowExceptionForHR(cat.RegisterGUID(Guid.NewGuid(), out var atomPlain));
        Marshal.ThrowExceptionForHR(cat.RegisterGUID(Guid.NewGuid(), out var atomTarget));
        ime.Store.AttributeReader.Resolver = atom =>
            atom == atomTarget ? new TsfDisplayAttr(true, 1, true, 0xFF112233u) :
            atom == atomPlain ? new TsfDisplayAttr(false, 1, false, 0) : null;

        Log.Clear();
        lastClauses = null;
        Session(ec =>
        {
            var ins = (ITfInsertAtSelection)(object)ctx;
            Marshal.ThrowExceptionForHR(ins.InsertTextAtSelection(ec, Tsf.TS_IAS_QUERYONLY, null, 0, out var range));
            var cc = (ITfContextComposition)(object)ctx;
            Marshal.ThrowExceptionForHR(cc.StartComposition(ec, range, sink, out comp));
            SetCompText(ec, "あいうえおか");

            Marshal.ThrowExceptionForHR(ctx.GetProperty(TsfDisplayAttributeReader.GUID_PROP_ATTRIBUTE, out var prop));
            Marshal.ThrowExceptionForHR(comp.GetRange(out var whole));

            Marshal.ThrowExceptionForHR(whole.Clone(out var first));
            Marshal.ThrowExceptionForHR(first.ShiftEnd(ec, -3, out _, IntPtr.Zero));
            var plain = new TfVariant { vt = 3, data1 = atomPlain };
            Marshal.ThrowExceptionForHR(prop.SetValue(ec, first, in plain));

            Marshal.ThrowExceptionForHR(whole.Clone(out var second));
            Marshal.ThrowExceptionForHR(second.ShiftStart(ec, 3, out _, IntPtr.Zero));
            var target = new TfVariant { vt = 3, data1 = atomTarget };
            Marshal.ThrowExceptionForHR(prop.SetValue(ec, second, in target));
            return 0;
        }, "compose + tag clauses");

        Console.WriteLine("  clauses: " + (lastClauses == null ? "null" : string.Join(" ", Array.ConvertAll(lastClauses, c => $"[{c.Start}-{c.End} target={c.Target} bold={c.BoldLine} color=0x{c.LineColorArgb:X8}]"))));
        Expect("update carries clauses", lastClauses != null);
        Expect("two clauses", lastClauses is { Length: 2 });
        Expect("first [0-3) not target", lastClauses is { Length: 2 } && lastClauses[0].Start == 0 && lastClauses[0].End == 3 && !lastClauses[0].Target);
        Expect("second [3-6) target+bold+color", lastClauses is { Length: 2 } && lastClauses[1].Start == 3 && lastClauses[1].End == 6 && lastClauses[1].Target && lastClauses[1].BoldLine && lastClauses[1].LineColorArgb == 0xFF112233u);

        Session(ec => { SetCompText(ec, ""); Marshal.ThrowExceptionForHR(comp.EndComposition(ec)); return 0; }, "cancel");
        Pump();

        Console.WriteLine(_failures == 0 ? "ALL PASS" : $"{_failures} FAILURE(S)");
        return _failures;
    }
}
