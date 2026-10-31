using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using Xilium.CefGlue.Common;

namespace Xilium.CefGlue.Demo.WinForms
{
    internal static class SelfTest
    {
        private static bool _attached;

        public sealed class SelfTestApi
        {
            public int Add(int a, int b) => a + b;
            public string Echo(string s) => "echo:" + s;
        }

        public static void AttachIfEnabled(BaseCefBrowser browser)
        {
            if (_attached || Environment.GetEnvironmentVariable("CEFGLUE_SELFTEST") != "1")
            {
                return;
            }
            _attached = true;

            browser.RegisterJavascriptObject(new SelfTestApi(), "selftest");

            var ran = false;
            browser.LoadEnd += (s, e) =>
            {
                if (!e.Frame.IsMain || ran)
                {
                    return;
                }
                ran = true;
                _ = Task.Run(() => RunAsync(browser));
            };
        }

        private static async Task RunAsync(BaseCefBrowser browser)
        {
            var log = new StringBuilder();
            log.AppendLine($"CEF {CefRuntime.ChromeVersion} - selftest {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
            var timeout = TimeSpan.FromSeconds(10);

            async Task Check<T>(string name, string script, Func<T, bool> ok)
            {
                try
                {
                    var value = await browser.EvaluateJavaScript<T>(script, timeout: timeout);
                    log.AppendLine($"{(ok(value) ? "PASS" : "FAIL")}  {name}: {value}");
                }
                catch (Exception ex)
                {
                    log.AppendLine($"FAIL  {name}: {ex.GetType().Name}: {ex.Message}");
                }
            }

            await Check<int>("number", "return 1 + 2;", v => v == 3);
            await Check<string>("string", "return 'abc' + document.readyState.length;", v => v?.StartsWith("abc") == true);
            await Check<bool>("cefglue global present", "return typeof cefglue === 'object';", v => v);
            await Check<string>("document.title", "return document.title;", v => v != null);
            await Check<bool>("binding: start calls",
                "window.__st = {}; cefglue.checkObjectBound('selftest')" +
                ".then(() => Promise.all([selftest.add(2, 3), selftest.echo('hi')]))" +
                ".then(r => { window.__st.add = r[0]; window.__st.echo = r[1]; }, e => { window.__st.err = String(e); });" +
                " return true;", v => v);
            await Task.Delay(1500);
            await Check<string>("binding: error", "return window.__st.err || 'none';", v => v == "none");
            await Check<int>("binding: add(2, 3)", "return window.__st.add;", v => v == 5);
            await Check<string>("binding: echo('hi')", "return window.__st.echo;", v => v == "echo:hi");

            browser.RegisterJavascriptObject(new SelfTestApi(), "selftest2");
            await Check<bool>("late binding: start calls",
                "window.__st2 = {}; cefglue.checkObjectBound('selftest2')" +
                ".then(() => Promise.all([selftest2.add(4, 5), selftest2.echo('yo')]))" +
                ".then(r => { window.__st2.add = r[0]; window.__st2.echo = r[1]; }, e => { window.__st2.err = String(e); });" +
                " return true;", v => v);
            await Task.Delay(1500);
            await Check<string>("late binding: error", "return window.__st2.err || 'none';", v => v == "none");
            await Check<int>("late binding: add(4, 5)", "return window.__st2.add;", v => v == 9);
            await Check<string>("late binding: echo('yo')", "return window.__st2.echo;", v => v == "echo:yo");

            try
            {
                await browser.EvaluateJavaScript<int>("throw new Error('boom');", timeout: timeout);
                log.AppendLine("FAIL  thrown error: no exception");
            }
            catch (Exception ex)
            {
                log.AppendLine($"PASS  thrown error surfaced: {ex.GetType().Name}: {ex.Message}");
            }

            File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "selftest.log"), log.ToString());
        }
    }
}
