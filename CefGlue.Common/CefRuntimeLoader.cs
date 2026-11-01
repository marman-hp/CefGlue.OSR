using System;
using System.Linq;
using System.Collections.Generic;
using System.IO;
using System.Diagnostics;
using System.Reflection;
using Xilium.CefGlue.Common.Handlers;
using Xilium.CefGlue.Common.Helpers;
using Xilium.CefGlue.Common.Shared;

namespace Xilium.CefGlue.Common
{
    public static class CefRuntimeLoader
    {
        private const string DefaultBrowserProcessDirectory = "CefGlueBrowserProcess";

        public static bool SkipShutdownOnExit { get; set; }

        public static void Shutdown()
        {
            OverlayDiagnostics.StopBackgroundSampling();
            CefRuntime.Shutdown();
        }

        private static Action<BrowserProcessHandler> _delayedInitialization;

        public static int RemoteDebuggingPort { get; private set; }

        public static void Initialize(CefSettings settings = null, KeyValuePair<string, string>[] flags = null, CustomScheme[] customSchemes = null)
        {
            RemoteDebuggingPort = settings?.RemoteDebuggingPort ?? 0;
            _delayedInitialization = (browserProcessHandler) => InternalInitialize(settings, flags, customSchemes, browserProcessHandler);
        }

        private static void InternalInitialize(CefSettings settings = null, KeyValuePair<string, string>[] flags = null, CustomScheme[] customSchemes = null, BrowserProcessHandler browserProcessHandler = null)
        {
            CefRuntime.Load();

            if (settings == null)
            {
                settings = new CefSettings();
            }

            settings.UncaughtExceptionStackSize = 100;

            var basePath = AppContext.BaseDirectory;
            var probingPaths = GetSubProcessPaths(basePath);
            var subProcessPath = probingPaths.FirstOrDefault(p => File.Exists(p));
            if (subProcessPath == null)
                throw new FileNotFoundException($"Unable to find SubProcess. Probed locations: {string.Join(Environment.NewLine, probingPaths)}");

            settings.BrowserSubprocessPath = subProcessPath;

            switch (CefRuntime.Platform)
            {
                case CefRuntimePlatform.Windows:
                    settings.NoSandbox = true;
                    settings.MultiThreadedMessageLoop = true;
                    break;

                case CefRuntimePlatform.MacOS:
                    var resourcesPath = Path.Combine(basePath, "Resources");
                    if (!Directory.Exists(resourcesPath))
                    {
                        throw new FileNotFoundException($"Unable to find Resources folder");
                    }

                    settings.NoSandbox = true;
                    settings.MultiThreadedMessageLoop = false;
                    settings.ExternalMessagePump = true;
                    settings.MainBundlePath = basePath;
                    settings.FrameworkDirPath = basePath;
                    settings.ResourcesDirPath = resourcesPath;
                    break;

                case CefRuntimePlatform.Linux:
                    settings.NoSandbox = true;
                    settings.MultiThreadedMessageLoop = true;
                    break;
            }

            AppDomain.CurrentDomain.ProcessExit += delegate
            {
                if (!SkipShutdownOnExit)
                {
                    Shutdown();
                }
            };

            IsOSREnabled = settings.WindowlessRenderingEnabled;

            var exeFileName = Process.GetCurrentProcess().MainModule.FileName;
            if (string.IsNullOrEmpty(exeFileName))
            {
                exeFileName = "CefGlue";
            }

            {
#if DEBUG
#endif
            }
            var args = new CefMainArgs(new[] { exeFileName  });

            CefRuntime.Initialize(args, settings, new BrowserCefApp(customSchemes, flags, browserProcessHandler), IntPtr.Zero);

            if (customSchemes != null)
            {
                foreach (var scheme in customSchemes)
                {
                    CefRuntime.RegisterSchemeHandlerFactory(scheme.SchemeName, scheme.DomainName, scheme.SchemeHandlerFactory);
                }
            }
        }

        private static IEnumerable<string> GetSubProcessPaths(string baseDirectory)
        {
            yield return Path.Combine(baseDirectory, DefaultBrowserProcessDirectory, BrowserProcessFileName);
            yield return Path.Combine(baseDirectory, BrowserProcessFileName);

            baseDirectory = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
            yield return Path.Combine(baseDirectory, DefaultBrowserProcessDirectory, BrowserProcessFileName);
            yield return Path.Combine(baseDirectory, BrowserProcessFileName);
        }

        internal static void Load(BrowserProcessHandler browserProcessHandler = null)
        {
            if (_delayedInitialization != null)
            {
                _delayedInitialization.Invoke(browserProcessHandler);
                _delayedInitialization = null;
            }
            else
            {
                InternalInitialize(browserProcessHandler: browserProcessHandler);
            }
        }

        public static bool IsLoaded => CefRuntime.IsInitialized;

        internal static bool IsOSREnabled { get; private set; }

        /// <summary>
        /// True when the browser process runs with --disable-gpu or --disable-gpu-compositing
        /// (set while CEF initializes). Shared-texture rendering can't work then.
        /// </summary>
        public static bool IsGpuCompositingDisabled { get; internal set; }

        private static string BrowserProcessFileName
        {
            get
            {
                const string Filename = "Xilium.CefGlue.BrowserProcess";
                switch (CefRuntime.Platform)
                {
                    case CefRuntimePlatform.Windows:
                        return Filename + ".exe";
                    default:
                        return Filename;
                }
            }
        }
    }
}
