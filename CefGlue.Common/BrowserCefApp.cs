using System;
using System.Collections.Generic;
using System.Diagnostics;
using Xilium.CefGlue.Common.Handlers;
using Xilium.CefGlue.Common.InternalHandlers;
using Xilium.CefGlue.Common.Shared;

namespace Xilium.CefGlue.Common
{
    internal class BrowserCefApp : CommonCefApp
    {
        private readonly CefBrowserProcessHandler _browserProcessHandler;
        private readonly KeyValuePair<string, string>[] _flags;

        internal BrowserCefApp(CustomScheme[] customSchemes = null, KeyValuePair<string, string>[] flags = null, BrowserProcessHandler browserProcessHandler = null) :
            base(customSchemes)
        {
            _browserProcessHandler = new CommonBrowserProcessHandler(browserProcessHandler, customSchemes);
            _flags = flags;
        }

        protected override void OnBeforeCommandLineProcessing(string processType, CefCommandLine commandLine)
        {
            if (string.IsNullOrEmpty(processType))
            {
                if (CefRuntime.Platform == CefRuntimePlatform.Linux)
                {
                    commandLine.AppendSwitch("no-zygote");
                }
                var disableGpu = string.Equals(Environment.GetEnvironmentVariable("CEFGLUE_DISABLE_GPU"), "1");
                if (disableGpu)
                {
                    commandLine.AppendSwitch("disable-gpu");
                    commandLine.AppendSwitch("disable-gpu-compositing");
                    commandLine.AppendSwitch("enable-media-stream");
                }

                if (long.TryParse(Environment.GetEnvironmentVariable("CEFGLUE_DISK_CACHE_SIZE_BYTES"), out var diskCacheSizeBytes) && diskCacheSizeBytes > 0)
                {
                    commandLine.AppendSwitch("disk-cache-size", diskCacheSizeBytes.ToString());
                }
                if (long.TryParse(Environment.GetEnvironmentVariable("CEFGLUE_MEDIA_CACHE_SIZE_BYTES"), out var mediaCacheSizeBytes) && mediaCacheSizeBytes > 0)
                {
                    commandLine.AppendSwitch("media-cache-size", mediaCacheSizeBytes.ToString());
                }

                commandLine.AppendSwitch("disable-smooth-scrolling", "1");

                var disabledFeatures = new List<string> { "BackForwardCache" };
                if (CefRuntime.Platform == CefRuntimePlatform.Windows)
                {
                    commandLine.AppendSwitch("disable-site-isolation-trials");
                    disabledFeatures.Add("IsolateOrigins");
                    disabledFeatures.Add("SitePerProcess");
                }
                commandLine.AppendSwitch("disable-features", string.Join(",", disabledFeatures));

                if (_flags != null)
                {
                    foreach (var flag in _flags)
                    {
                        commandLine.AppendSwitch(flag.Key, flag.Value);
                    }
                }

                // With either switch CEF paints via OnPaint even for shared-texture browsers
                // (verified on CEF 154), so a TextureControl would stay blank.
                CefRuntimeLoader.IsGpuCompositingDisabled =
                    commandLine.HasSwitch("disable-gpu") || commandLine.HasSwitch("disable-gpu-compositing");

            }
        }

        protected override CefBrowserProcessHandler GetBrowserProcessHandler()
        {
            return _browserProcessHandler;
        }
    }
}
