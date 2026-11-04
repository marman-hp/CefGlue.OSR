using CefGlue;

namespace Xilium.CefGlue
{
    using System;
    using System.IO;
    using System.Linq;
    using System.Globalization;
    using System.Runtime.InteropServices;
    using Xilium.CefGlue.Interop;

    public static unsafe class CefRuntime
    {
        private static readonly CefRuntimePlatform _platform;

        private static bool _loaded;
        private static bool _initialized;

        static CefRuntime()
        {
            _platform = DetectPlatform();
            NativeLibsLoader.Install();
        }

        #region Platform Detection
        private static CefRuntimePlatform DetectPlatform()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                return CefRuntimePlatform.Windows;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                return CefRuntimePlatform.MacOS;
            }

            if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                return CefRuntimePlatform.Linux;
            }

            throw new PlatformNotSupportedException();
        }

        public static CefRuntimePlatform Platform
        {
            get { return _platform; }
        }
        #endregion

        /// <summary>
        /// Gets whether the CEF runtime has been initialized;
        /// </summary>
        public static bool IsInitialized => _initialized;

        /// <summary>
        /// Loads CEF runtime.
        /// </summary>
        /// <exception cref="DllNotFoundException"></exception>
        /// <exception cref="CefVersionMismatchException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        public static void Load()
        {
            if (Platform != CefRuntimePlatform.Windows)
            {
                Load(string.Empty);
                return;
            }

            var libcefDir = AppDomain.CurrentDomain.BaseDirectory;
            Load(System.IO.Path.Combine(libcefDir, @"runtimes\win-x64\native"));
        }

        /// <summary>
        /// Loads CEF runtime from specified path.
        /// </summary>
        /// <exception cref="DllNotFoundException"></exception>
        /// <exception cref="CefVersionMismatchException"></exception>
        /// <exception cref="InvalidOperationException"></exception>
        public static void Load(string path)
        {
            if (_loaded) return;

            if (!string.IsNullOrEmpty(path))
            {
                if (Platform == CefRuntimePlatform.Windows)
                    LoadLibraryWindows(path);
                else
                    throw new PlatformNotSupportedException("CEF Runtime can't be initialized on altered path on this platform. Use CefRuntime.Load() instead.");
            }

            CheckVersion();

            _loaded = true;
        }

        private static void LoadLibraryWindows(string path)
        {
            Xilium.CefGlue.Platform.Windows.NativeMethods.LoadLibraryEx(
                System.IO.Path.Combine(path, "libcef.dll"),
                IntPtr.Zero,
                Xilium.CefGlue.Platform.Windows.LoadLibraryFlags.LOAD_WITH_ALTERED_SEARCH_PATH
                );
        }

        #region cef_version

        /// <summary>
        /// The CEF version, e.g. "154.0.33" (without the git hash and Chromium suffix).
        /// </summary>
        public static string CefVersion
        {
            get
            {
                var plus = libcef.CEF_VERSION.IndexOf('+');
                return plus < 0 ? libcef.CEF_VERSION : libcef.CEF_VERSION.Substring(0, plus);
            }
        }

        public static string ChromeVersion
        {
            get
            {
                return string.Format("{0}.{1}.{2}.{3}", libcef.CHROME_VERSION_MAJOR, libcef.CHROME_VERSION_MINOR, libcef.CHROME_VERSION_BUILD, libcef.CHROME_VERSION_PATCH);
            }
        }

        private static void CheckVersion()
        {
            CheckVersionByApiHash();
        }

        private static void CheckVersionByApiHash()
        {
            var libCefFile = CefRuntimeLocator.FindLibrary();
            if (libCefFile != null)
            {
                NativeLibrary.TryLoad(libCefFile, out _);
            }

            string actual;
            try
            {
                var n_actual = libcef.api_hash(libcef.CEF_API_VERSION, 0);
                actual = n_actual != null ? new string(n_actual) : null;
            }
            catch (EntryPointNotFoundException ex)
            {
                throw new NotSupportedException("cef_api_hash call is not supported.", ex);
            }
            catch (DllNotFoundException dllEx)
            {
                throw new NotSupportedException($"Can't find CEF in \"{AppDomain.CurrentDomain.BaseDirectory}\"", dllEx);
            }
            if (string.IsNullOrEmpty(actual)) throw new NotSupportedException();

            string expected;
            switch (Platform)
            {
                case CefRuntimePlatform.Windows: expected = libcef.CEF_API_HASH_PLATFORM_WIN; break;
                case CefRuntimePlatform.MacOS: expected = libcef.CEF_API_HASH_PLATFORM_MACOS; break;
                case CefRuntimePlatform.Linux: expected = libcef.CEF_API_HASH_PLATFORM_LINUX; break;
                default: throw new PlatformNotSupportedException();
            }

            if (string.Compare(actual, expected, StringComparison.OrdinalIgnoreCase) != 0)
            {
                var expectedVersion = libcef.CEF_VERSION;
                throw ExceptionBuilder.RuntimeVersionApiHashMismatch(actual, expected, expectedVersion);
            }
        }

        #endregion

        #region cef_app

        /// <summary>
        /// This function should be called from the application entry point function to
        /// execute a secondary process. It can be used to run secondary processes from
        /// the browser client executable (default behavior) or from a separate
        /// executable specified by the cef_settings_t.browser_subprocess_path value. If
        /// called for the browser process (identified by no "type" command-line value)
        /// it will return immediately with a value of -1. If called for a recognized
        /// secondary process it will block until the process should exit and then
        /// return the process exit code. The |application| parameter may be empty. The
        /// |windows_sandbox_info| parameter is only used on Windows and may be NULL
        /// (see cef_sandbox_win.h for details).
        /// </summary>
        public static int ExecuteProcess(CefMainArgs args, CefApp application, IntPtr windowsSandboxInfo)
        {
            LoadIfNeed();

            var n_args = args.ToNative();
            var n_app = application != null ? application.ToNative() : null;

            try
            {
                return libcef.execute_process(n_args, n_app, (void*)windowsSandboxInfo);
            }
            finally
            {
                CefMainArgs.Free(n_args);
            }
        }

        [Obsolete]
        public static int ExecuteProcess(CefMainArgs args, CefApp application)
        {
            return ExecuteProcess(args, application, IntPtr.Zero);
        }

        /// <summary>
        /// This function should be called on the main application thread to initialize
        /// the CEF browser process. The |application| parameter may be empty. A return
        /// value of true indicates that it succeeded and false indicates that it
        /// failed. The |windows_sandbox_info| parameter is only used on Windows and may
        /// be NULL (see cef_sandbox_win.h for details).
        /// </summary>
        public static void Initialize(CefMainArgs args, CefSettings settings, CefApp application, IntPtr windowsSandboxInfo)
        {
            LoadIfNeed();

            if (args == null) throw new ArgumentNullException("args");
            if (settings == null) throw new ArgumentNullException("settings");

            if (_initialized) throw ExceptionBuilder.CefRuntimeAlreadyInitialized();

            var n_main_args = args.ToNative();
            var n_settings = settings.ToNative();
            var n_app = application != null ? application.ToNative() : null;

            try
            {
                if (libcef.initialize(n_main_args, n_settings, n_app, (void*)windowsSandboxInfo) != 0)
                {
                    _initialized = true;
                }
                else
                {
                    throw ExceptionBuilder.CefRuntimeFailedToInitialize();
                }
            }
            finally
            {
                CefMainArgs.Free(n_main_args);
                CefSettings.Free(n_settings);
            }
        }

        [Obsolete("Use Initialize(CefMainArgs,CefSettings,CefApp,IntPtr) overload instead.")]
        public static void Initialize(CefMainArgs args, CefSettings settings, CefApp application)
        {
            Initialize(args, settings, application, IntPtr.Zero);
        }

        /// <summary>
        /// This function should be called on the main application thread to shut down
        /// the CEF browser process before the application exits.
        /// </summary>
        /// <param name="skipGC">If set to <see langword="false"/> perform GC
        /// and wait for pending finalizers.</param>
        public static void Shutdown(bool skipGC = false)
        {
            if (!_initialized) return;

            if (!skipGC)
            {
                GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced);
                GC.WaitForPendingFinalizers();
            }

            libcef.shutdown();
        }

        /// <summary>
        /// Perform a single iteration of CEF message loop processing. This function is
        /// provided for cases where the CEF message loop must be integrated into an
        /// existing application message loop. Use of this function is not recommended
        /// for most users; use either the CefRunMessageLoop() function or
        /// cef_settings_t.multi_threaded_message_loop if possible. When using this
        /// function care must be taken to balance performance against excessive CPU
        /// usage. It is recommended to enable the cef_settings_t.external_message_pump
        /// option when using this function so that
        /// CefBrowserProcessHandler::OnScheduleMessagePumpWork() callbacks can
        /// facilitate the scheduling process. This function should only be called on
        /// the main application thread and only if CefInitialize() is called with a
        /// cef_settings_t.multi_threaded_message_loop value of false. This function
        /// will not block.
        /// </summary>
        public static void DoMessageLoopWork()
        {
            libcef.do_message_loop_work();
        }

        /// <summary>
        /// Run the CEF message loop. Use this function instead of an application-
        /// provided message loop to get the best balance between performance and CPU
        /// usage. This function should only be called on the main application thread
        /// and only if CefInitialize() is called with a
        /// cef_settings_t.multi_threaded_message_loop value of false. This function
        /// will block until a quit message is received by the system.
        /// </summary>
        public static void RunMessageLoop()
        {
            libcef.run_message_loop();
        }

        /// <summary>
        /// Quit the CEF message loop that was started by calling CefRunMessageLoop().
        /// This function should only be called on the main application thread and only
        /// if CefRunMessageLoop() was used.
        /// </summary>
        public static void QuitMessageLoop()
        {
            libcef.quit_message_loop();
        }

        /// <summary>
        /// Set to true before calling Windows APIs like TrackPopupMenu that enter a
        /// modal message loop. Set to false after exiting the modal message loop.
        /// </summary>
        public static void SetOSModalLoop(bool osModalLoop)
        {
            libcef.set_osmodal_loop(osModalLoop ? 1 : 0);
        }

        #endregion

        #region cef_task

        /// <summary>
        /// CEF maintains multiple internal threads that are used for handling different
        /// types of tasks in different processes. See the cef_thread_id_t definitions in
        /// cef_types.h for more information. This function will return true if called on
        /// the specified thread. It is an error to request a thread from the wrong
        /// process.
        /// </summary>
        public static bool CurrentlyOn(CefThreadId threadId)
        {
            return libcef.currently_on(threadId) != 0;
        }

        /// <summary>
        /// Post a task for execution on the specified thread. This function may be
        /// called on any thread. It is an error to request a thread from the wrong
        /// process.
        /// </summary>
        public static bool PostTask(CefThreadId threadId, CefTask task)
        {
            if (task == null) throw new ArgumentNullException("task");

            return libcef.post_task(threadId, task.ToNative()) != 0;
        }

        /// <summary>
        /// Post a task for delayed execution on the specified thread. This function may
        /// be called on any thread. It is an error to request a thread from the wrong
        /// process.
        /// </summary>
        public static bool PostTask(CefThreadId threadId, CefTask task, long delay)
        {
            if (task == null) throw new ArgumentNullException("task");

            return libcef.post_delayed_task(threadId, task.ToNative(), delay) != 0;
        }
        #endregion

        #region cef_origin_whitelist

        /// <summary>
        /// Add an entry to the cross-origin access whitelist.
        /// </summary>
        public static bool AddCrossOriginWhitelistEntry(string sourceOrigin, string targetProtocol, string targetDomain, bool allowTargetSubdomains)
        {
            if (string.IsNullOrEmpty("sourceOrigin")) throw new ArgumentNullException("sourceOrigin");
            if (string.IsNullOrEmpty("targetProtocol")) throw new ArgumentNullException("targetProtocol");

            fixed (char* sourceOrigin_ptr = sourceOrigin)
            fixed (char* targetProtocol_ptr = targetProtocol)
            fixed (char* targetDomain_ptr = targetDomain)
            {
                var n_sourceOrigin = new cef_string_t(sourceOrigin_ptr, sourceOrigin.Length);
                var n_targetProtocol = new cef_string_t(targetProtocol_ptr, targetProtocol.Length);
                var n_targetDomain = new cef_string_t(targetDomain_ptr, targetDomain != null ? targetDomain.Length : 0);

                return libcef.add_cross_origin_whitelist_entry(
                    &n_sourceOrigin,
                    &n_targetProtocol,
                    &n_targetDomain,
                    allowTargetSubdomains ? 1 : 0
                    ) != 0;
            }
        }

        /// <summary>
        /// Remove an entry from the cross-origin access whitelist. Returns false if
        /// |source_origin| is invalid or the whitelist cannot be accessed.
        /// </summary>
        public static bool RemoveCrossOriginWhitelistEntry(string sourceOrigin, string targetProtocol, string targetDomain, bool allowTargetSubdomains)
        {
            if (string.IsNullOrEmpty("sourceOrigin")) throw new ArgumentNullException("sourceOrigin");
            if (string.IsNullOrEmpty("targetProtocol")) throw new ArgumentNullException("targetProtocol");

            fixed (char* sourceOrigin_ptr = sourceOrigin)
            fixed (char* targetProtocol_ptr = targetProtocol)
            fixed (char* targetDomain_ptr = targetDomain)
            {
                var n_sourceOrigin = new cef_string_t(sourceOrigin_ptr, sourceOrigin.Length);
                var n_targetProtocol = new cef_string_t(targetProtocol_ptr, targetProtocol.Length);
                var n_targetDomain = new cef_string_t(targetDomain_ptr, targetDomain != null ? targetDomain.Length : 0);

                return libcef.remove_cross_origin_whitelist_entry(
                    &n_sourceOrigin,
                    &n_targetProtocol,
                    &n_targetDomain,
                    allowTargetSubdomains ? 1 : 0
                    ) != 0;
            }
        }

        /// <summary>
        /// Remove all entries from the cross-origin access whitelist. Returns false if
        /// the whitelist cannot be accessed.
        /// </summary>
        public static bool ClearCrossOriginWhitelist()
        {
            return libcef.clear_cross_origin_whitelist() != 0;
        }

        #endregion

        #region cef_scheme

        /// <summary>
        /// Register a scheme handler factory for the specified |scheme_name| and
        /// optional |domain_name|. An empty |domain_name| value for a standard scheme
        /// will cause the factory to match all domain names. The |domain_name| value
        /// will be ignored for non-standard schemes. If |scheme_name| is a built-in
        /// scheme and no handler is returned by |factory| then the built-in scheme
        /// handler factory will be called. If |scheme_name| is a custom scheme then
        /// also implement the CefApp::OnRegisterCustomSchemes() method in all processes.
        /// This function may be called multiple times to change or remove the factory
        /// that matches the specified |scheme_name| and optional |domain_name|.
        /// Returns false if an error occurs. This function may be called on any thread
        /// in the browser process.
        /// </summary>
        public static bool RegisterSchemeHandlerFactory(string schemeName, string domainName, CefSchemeHandlerFactory factory)
        {
            if (string.IsNullOrEmpty(schemeName)) throw new ArgumentNullException("schemeName");
            if (factory == null) throw new ArgumentNullException("factory");

            fixed (char* schemeName_str = schemeName)
            fixed (char* domainName_str = domainName)
            {
                var n_schemeName = new cef_string_t(schemeName_str, schemeName.Length);
                var n_domainName = new cef_string_t(domainName_str, domainName != null ? domainName.Length : 0);

                return libcef.register_scheme_handler_factory(&n_schemeName, &n_domainName, factory.ToNative()) != 0;
            }
        }

        /// <summary>
        /// Clear all registered scheme handler factories. Returns false on error. This
        /// function may be called on any thread in the browser process.
        /// </summary>
        public static bool ClearSchemeHandlerFactories()
        {
            return libcef.clear_scheme_handler_factories() != 0;
        }

        #endregion

        #region cef_trace

        /// <summary>
        /// Start tracing events on all processes. Tracing is initialized asynchronously
        /// and |callback| will be executed on the UI thread after initialization is
        /// complete.
        /// </summary>
        public static bool BeginTracing(string categories = null, CefCompletionCallback callback = null)
        {
            fixed (char* categories_str = categories)
            {
                var n_categories = new cef_string_t(categories_str, categories != null ? categories.Length : 0);
                var n_callback = callback != null ? callback.ToNative() : null;
                return libcef.begin_tracing(&n_categories, n_callback) != 0;
            }
        }

        /// <summary>
        /// Stop tracing events on all processes.
        /// </summary>
        public static bool EndTracing(string tracingFile = null, CefEndTracingCallback callback = null)
        {
            fixed (char* tracingFile_str = tracingFile)
            {
                var n_tracingFile = new cef_string_t(tracingFile_str, tracingFile != null ? tracingFile.Length : 0);
                var n_callback = callback != null ? callback.ToNative() : null;
                return libcef.end_tracing(&n_tracingFile, n_callback) != 0;
            }
        }

        /// <summary>
        /// Returns the current system trace time or, if none is defined, the current
        /// high-res time. Can be used by clients to synchronize with the time
        /// information in trace events.
        /// </summary>
        public static long NowFromSystemTraceTime()
        {
            return libcef.now_from_system_trace_time();
        }

        #endregion

        #region cef_parser

        /// <summary>
        /// Parse the specified |url| into its component parts.
        /// Returns false if the URL is empty or invalid.
        /// </summary>
        public static bool ParseUrl(string url, out CefUrlParts parts)
        {
            fixed (char* url_str = url)
            {
                var n_url = new cef_string_t(url_str, url != null ? url.Length : 0);
                var n_parts = cef_urlparts_t.Alloc();

                var result = libcef.parse_url(&n_url, n_parts) != 0;

                parts = result ? CefUrlParts.FromNative(n_parts) : null;
                cef_urlparts_t.Free(n_parts);
                return result;
            }
        }

        public static bool CreateUrl(CefUrlParts parts, out string url)
        {
            if (parts == null) throw new ArgumentNullException("parts");

            var n_parts = parts.ToNative();
            var n_url = new cef_string_t();

            var result = libcef.create_url(n_parts, &n_url) != 0;

            url = result ? cef_string_t.ToString(&n_url) : null;

            cef_urlparts_t.Free(n_parts);
            libcef.string_clear(&n_url);

            return result;
        }

        /// <summary>
        /// Returns the mime type for the specified file extension or an empty string if
        /// unknown.
        /// </summary>
        public static string GetMimeType(string extension)
        {
            fixed (char* extension_str = extension)
            {
                var n_extension = new cef_string_t(extension_str, extension != null ? extension.Length : 0);

                var n_result = libcef.get_mime_type(&n_extension);
                return cef_string_userfree.ToString(n_result);
            }
        }

        /// <summary>
        /// Get the extensions associated with the given mime type. This should be passed
        /// in lower case. There could be multiple extensions for a given mime type, like
        /// "html,htm" for "text/html", or "txt,text,html,..." for "text/*". Any existing
        /// elements in the provided vector will not be erased.
        /// </summary>
        public static string[] GetExtensionsForMimeType(string mimeType)
        {
            fixed (char* mimeType_str = mimeType)
            {
                var n_mimeType = new cef_string_t(mimeType_str, mimeType != null ? mimeType.Length : 0);

                var n_list = libcef.string_list_alloc();
                libcef.get_extensions_for_mime_type(&n_mimeType, n_list);
                var result = cef_string_list.ToArray(n_list);
                libcef.string_list_free(n_list);
                return result;
            }
        }

        /// <summary>
        /// Encodes |data| as a base64 string.
        /// </summary>
        public static unsafe string Base64Encode(void* data, int size)
        {
            var n_result = libcef.base64_encode(data, (UIntPtr)size);
            return cef_string_userfree.ToString(n_result);
        }

        public static string Base64Encode(byte[] bytes, int offset, int length)
        {

            fixed (byte* bytes_ptr = &bytes[offset])
            {
                return Base64Encode(bytes_ptr, length);
            }
        }

        public static string Base64Encode(byte[] bytes)
        {
            return Base64Encode(bytes, 0, bytes.Length);
        }

        /// <summary>
        /// Decodes the base64 encoded string |data|. The returned value will be NULL if
        /// the decoding fails.
        /// </summary>
        public static CefBinaryValue Base64Decode(string data)
        {
            fixed (char* data_str = data)
            {
                var n_data = new cef_string_t(data_str, data != null ? data.Length : 0);
                return CefBinaryValue.FromNative(libcef.base64_decode(&n_data));
            }
        }

        /// <summary>
        /// Escapes characters in |text| which are unsuitable for use as a query
        /// parameter value. Everything except alphanumerics and -_.!~*'() will be
        /// converted to "%XX". If |use_plus| is true spaces will change to "+". The
        /// result is basically the same as encodeURIComponent in Javacript.
        /// </summary>
        public static string UriEncode(string text, bool usePlus)
        {
            fixed (char* text_str = text)
            {
                var n_text = new cef_string_t(text_str, text != null ? text.Length : 0);

                var n_result = libcef.uriencode(&n_text, usePlus ? 1 : 0);
                return cef_string_userfree.ToString(n_result);
            }
        }

        /// <summary>
        /// Unescapes |text| and returns the result. Unescaping consists of looking for
        /// the exact pattern "%XX" where each X is a hex digit and converting to the
        /// character with the numerical value of those digits (e.g. "i%20=%203%3b"
        /// unescapes to "i = 3;"). If |convert_to_utf8| is true this function will
        /// attempt to interpret the initial decoded result as UTF-8. If the result is
        /// convertable into UTF-8 it will be returned as converted. Otherwise the
        /// initial decoded result will be returned.  The |unescape_rule| parameter
        /// supports further customization the decoding process.
        /// </summary>
        public static string UriDecode(string text, bool convertToUtf8, CefUriUnescapeRules unescapeRule)
        {
            fixed (char* text_str = text)
            {
                var n_text = new cef_string_t(text_str, text != null ? text.Length : 0);

                var n_result = libcef.uridecode(&n_text, convertToUtf8 ? 1 : 0, unescapeRule);
                return cef_string_userfree.ToString(n_result);
            }
        }

        /// <summary>
        /// Parses the specified |json_string| and returns a dictionary or list
        /// representation. If JSON parsing fails this method returns NULL.
        /// </summary>
        public static CefValue ParseJson(string value, CefJsonParserOptions options)
        {
            fixed (char* value_str = value)
            {
                var n_value = new cef_string_t(value_str, value != null ? value.Length : 0);
                var n_result = libcef.parse_json(&n_value, options);
                return CefValue.FromNativeOrNull(n_result);
            }
        }

        public static CefValue ParseJson(IntPtr json, int jsonSize, CefJsonParserOptions options)
        {
            var n_result = libcef.parse_json_buffer((void*)json, checked((UIntPtr)jsonSize), options);
            return CefValue.FromNativeOrNull(n_result);
        }

        /// <summary>
        /// Parses the specified |json_string| and returns a dictionary or list
        /// representation. If JSON parsing fails this method returns NULL and populates
        /// |error_msg_out| with a formatted error message.
        /// </summary>
        public static CefValue ParseJsonAndReturnError(string value, CefJsonParserOptions options, out string errorMessage)
        {
            fixed (char* value_str = value)
            {
                var n_value = new cef_string_t(value_str, value != null ? value.Length : 0);

                cef_string_t n_error_msg;
                var n_result = libcef.parse_jsonand_return_error(&n_value, options, &n_error_msg);

                var result = CefValue.FromNativeOrNull(n_result);
                errorMessage = cef_string_userfree.ToString((cef_string_userfree*)&n_error_msg);
                return result;
            }
        }

        /// <summary>
        /// Generates a JSON string from the specified root |node| which should be a
        /// dictionary or list value. Returns an empty string on failure. This method
        /// requires exclusive access to |node| including any underlying data.
        /// </summary>
        public static string WriteJson(CefValue value, CefJsonWriterOptions options)
        {
            if (value == null) throw new ArgumentNullException("value");
            var n_result = libcef.write_json(value.ToNative(), options);
            return cef_string_userfree.ToString(n_result);
        }

        /// <summary>
        /// Combines specified |base_url| and |relative_url| into |resolved_url|.
        /// Returns false if one of the URLs is empty or invalid.
        /// </summary>
        public static bool ResolveUrl(string baseUrl,
            string relativeUrl,
            out string? resolvedUrl)
        {
            fixed (char* baseUrl_str = baseUrl)
            fixed (char* relativeUrl_str = relativeUrl)
            {
                var n_baseUrl = new cef_string_t(baseUrl_str, baseUrl != null ? baseUrl.Length : 0);
                var n_relativeUrl = new cef_string_t(relativeUrl_str, relativeUrl != null ? relativeUrl.Length : 0);

                cef_string_t n_resolvedUrl = default;
                var result = libcef.resolve_url(&n_baseUrl, &n_relativeUrl, &n_resolvedUrl) != 0;

                if (result)
                {
                    resolvedUrl = cef_string_t.ToString(&n_resolvedUrl);
                }
                else
                {
                    resolvedUrl = null;
                }

                libcef.string_clear(&n_resolvedUrl);
                return result;
            }
        }

        #endregion

        #region cef_v8

        /// <summary>
        /// Register a new V8 extension with the specified JavaScript extension code and
        /// handler. Functions implemented by the handler are prototyped using the
        /// keyword 'native'. The calling of a native function is restricted to the scope
        /// in which the prototype of the native function is defined. This function may
        /// only be called on the render process main thread.
        /// </summary>
        [Obsolete("V8 extensions were removed in CEF 154 (API version 15400); this call does nothing and returns false. Inject script from CefRenderProcessHandler.OnContextCreated instead.")]
        public static bool RegisterExtension(string extensionName, string javascriptCode, CefV8Handler handler)
        {
            if (string.IsNullOrEmpty(extensionName)) throw new ArgumentNullException("extensionName");
            if (string.IsNullOrEmpty(javascriptCode)) throw new ArgumentNullException("javascriptCode");

            fixed (char* extensionName_str = extensionName)
            fixed (char* javascriptCode_str = javascriptCode)
            {
                var n_extensionName = new cef_string_t(extensionName_str, extensionName.Length);
                var n_javascriptCode = new cef_string_t(javascriptCode_str, javascriptCode.Length);

                return libcef.register_extension(&n_extensionName, &n_javascriptCode, handler != null ? handler.ToNative() : null) != 0;
            }
        }

        #endregion

        #region cef_path_util

        /// <summary>
        /// Retrieve the path associated with the specified |key|. Returns true on
        /// success. Can be called on any thread in the browser process.
        /// </summary>
        public static string GetPath(CefPathKey pathKey)
        {
            var n_value = new cef_string_t();
            var success = libcef.get_path(pathKey, &n_value) != 0;
            var value = cef_string_t.ToString(&n_value);
            libcef.string_clear(&n_value);
            if (!success)
            {
                throw new InvalidOperationException(
                    string.Format(CultureInfo.InvariantCulture, "Failed to get path for key {0}.", pathKey)
                    );
            }
            return value;
        }

        #endregion

        #region cef_process_util

        /// <summary>
        /// Launches the process specified via |command_line|. Returns true upon
        /// success. Must be called on the browser process TID_PROCESS_LAUNCHER thread.
        /// </summary>
        public static bool LaunchProcess(CefCommandLine commandLine)
        {
            if (commandLine == null) throw new ArgumentNullException("commandLine");

            return libcef.launch_process(commandLine.ToNative()) != 0;
        }

        #endregion

        #region cef_sandbox_win
        #endregion

        #region cef_ssl_info

        /// <summary>
        /// Returns true if the certificate status represents an error.
        /// </summary>
        public static bool IsCertStatusError(CefCertStatus status)
        {
            return libcef.is_cert_status_error(status) != 0;
        }

        #endregion

        #region cef_crash_util

        /// <summary>
        /// Crash reporting is configured using an INI-style config file named
        /// "crash_reporter.cfg". On Windows and Linux this file must be placed next to
        /// the main application executable. On macOS this file must be placed in the
        /// top-level app bundle Resources directory (e.g.
        /// "&lt;appname&gt;.app/Contents/Resources"). File contents are as follows:
        /// </summary>
        public static bool CrashReportingEnabled()
        {
            return libcef.crash_reporting_enabled() != 0;
        }

        /// <summary>
        /// Sets or clears a specific key-value pair from the crash metadata.
        /// </summary>
        public static void SetCrashKeyValue(string key, string value)
        {
            fixed (char* key_ptr = key)
            fixed (char* value_ptr = value)
            {
                var n_key = new cef_string_t(key_ptr, key.Length);
                var n_value = new cef_string_t(value_ptr, value != null ? value.Length : 0);
                libcef.set_crash_key_value(&n_key, &n_value);
            }
        }

        #endregion

        #region file_util

        /// <summary>
        /// Loads the existing "Certificate Revocation Lists" file that is managed by
        /// Google Chrome. This file can generally be found in Chrome's User Data
        /// directory (e.g. "C:\Users\[User]\AppData\Local\Google\Chrome\User Data\" on
        /// Windows) and is updated periodically by Chrome's component updater service.
        /// Must be called in the browser process after the context has been initialized.
        /// See https://dev.chromium.org/Home/chromium-security/crlsets for background.
        /// </summary>
        public static void LoadCrlSetsFile(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));

            fixed (char* path_ptr = path)
            {
                var n_path = new cef_string_t(path_ptr, path.Length);
                libcef.load_crlsets_file(&n_path);
            }
        }

        #endregion

        #region i18n

        /// <summary>
        /// Returns true if the application text direction is right-to-left.
        /// </summary>
        public static bool IsRtl() => libcef.is_rtl() != 0;

        #endregion

        private static void LoadIfNeed()
        {
            if (!_loaded) Load();
        }
    }
}
