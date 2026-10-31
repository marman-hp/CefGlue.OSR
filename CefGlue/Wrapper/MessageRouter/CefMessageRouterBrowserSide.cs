namespace Xilium.CefGlue.Wrapper
{
    using System;
    using System.Collections.Generic;
    using System.Diagnostics;
    using System.Text;

    using BrowserInfoMap = CefBrowserInfoMap<long, Xilium.CefGlue.Wrapper.CefMessageRouterBrowserSide.QueryInfo>;

    /// <summary>
    /// Implements the browser side of query routing. The methods of this class may
    /// be called on any browser process thread unless otherwise indicated.
    /// </summary>
    public sealed class CefMessageRouterBrowserSide
    {
        #region Callback

        /// <summary>
        /// Callback associated with a single pending asynchronous query. Execute the
        /// Success or Failure method to send an asynchronous response to the
        /// associated JavaScript handler. It is a runtime error to destroy a Callback
        /// object associated with an uncanceled query without first executing one of
        /// the callback methods. The methods of this class may be called on any
        /// browser process thread.
        /// </summary>
        public sealed class Callback
        {
            private CefMessageRouterBrowserSide _router;
            private readonly int _browserId;
            private readonly long _queryId;
            private readonly bool _persistent;

            internal Callback(CefMessageRouterBrowserSide router, int browserId, long queryId, bool persistent)
            {
                _router = router;
                _browserId = browserId;
                _queryId = queryId;
                _persistent = persistent;
            }

            ~Callback()
            {
                Dispose();
            }

            internal void Dispose()
            {
                Debug.Assert(_router == null);
            }

            /// <summary>
            /// Notify the associated JavaScript onSuccess callback that the query has
            /// completed successfully with the specified |response|.
            /// </summary>
            public void Success(string response)
            {
                if (!CefRuntime.CurrentlyOn(CefThreadId.UI))
                {
                    Helpers.PostTask(CefThreadId.UI,
                        Helpers.Apply(this.Success, response)
                        );
                    return;
                }

                if (_router != null)
                {
                    Helpers.PostTaskUncertainty(CefThreadId.UI,
                        Helpers.Apply(_router.OnCallbackSuccess, _browserId, _queryId, response)
                        );

                    if (!_persistent)
                    {
                        _router = null;
                    }
                }
            }

            /// <summary>
            /// Notify the associated JavaScript onFailure callback that the query has
            /// failed with the specified |error_code| and |error_message|.
            /// </summary>
            public void Failure(int errorCode, string errorMessage)
            {
                if (!CefRuntime.CurrentlyOn(CefThreadId.UI))
                {
                    Helpers.PostTask(CefThreadId.UI,
                        Helpers.Apply(this.Failure, errorCode, errorMessage)
                        );
                    return;
                }

                if (_router != null)
                {
                    Helpers.PostTaskUncertainty(CefThreadId.UI,
                        Helpers.Apply(_router.OnCallbackFailure, _browserId, _queryId, errorCode, errorMessage)
                        );

                    _router = null;
                }
            }

            internal void Detach()
            {
                Helpers.RequireUIThread();
                _router = null;
            }
        };

        #endregion

        #region Handler

        /// <summary>
        /// Implement this interface to handle queries. All methods will be executed on
        /// the browser process UI thread.
        /// </summary>
        public class Handler
        {
            /// <summary>
            /// Executed when a new query is received. |query_id| uniquely identifies the
            /// query for the life span of the router. Return true to handle the query
            /// or false to propagate the query to other registered handlers, if any. If
            /// no handlers return true from this method then the query will be
            /// automatically canceled with an error code of -1 delivered to the
            /// JavaScript onFailure callback. If this method returns true then a
            /// Callback method must be executed either in this method or asynchronously
            /// to complete the query.
            /// </summary>
            public virtual bool OnQuery(CefBrowser browser, CefFrame frame, long queryId, string request, bool persistent, Callback callback)
            {
                return false;
            }

            /// <summary>
            /// Executed when a query has been canceled either explicitly using the
            /// JavaScript cancel function or implicitly due to browser destruction,
            /// navigation or renderer process termination. It will only be called for
            /// the single handler that returned true from OnQuery for the same
            /// |query_id|. No references to the associated Callback object should be
            /// kept after this method is called, nor should any Callback methods be
            /// executed.
            /// </summary>
            public virtual void OnQueryCanceled(CefBrowser browser, CefFrame frame, long queryId)
            {
            }
        };

        #endregion

        private readonly CefMessageRouterConfig _config;
        private readonly string _queryMessageName;
        private readonly string _cancelMessageName;

        private readonly List<Handler> _handlerSet = new List<Handler>(4);

        private readonly BrowserInfoMap _browserQueryInfoMap = new BrowserInfoMap();

        private readonly CefMessageRouter.IdGeneratorInt64 _queryIdGenerator = new CefMessageRouter.IdGeneratorInt64();

        /// <summary>
        /// Create a new router with the specified configuration.
        /// </summary>
        public CefMessageRouterBrowserSide(CefMessageRouterConfig config)
        {
            if (!config.Validate()) throw new ArgumentException("Invalid configuration.");

            _config = config;
            _queryMessageName = config.JSQueryFunction + CefMessageRouter.MessageSuffix;
            _cancelMessageName = config.JSCancelFunction + CefMessageRouter.MessageSuffix;
        }

        ~CefMessageRouterBrowserSide()
        {
            Debug.Assert(_browserQueryInfoMap.IsEmpty);
        }

        /// <summary>
        /// Create a new router with the specified configuration.
        /// </summary>
        public static CefMessageRouterBrowserSide Create(CefMessageRouterConfig config)
        {
            return new CefMessageRouterBrowserSide(config);
        }

        /// <summary>
        /// Add a new query handler. If |first| is true it will be added as the first
        /// handler, otherwise it will be added as the last handler. Returns true if
        /// the handler is added successfully or false if the handler has already been
        /// added. Must be called on the browser process UI thread. The Handler object
        /// must either outlive the router or be removed before deletion.
        /// </summary>
        public bool AddHandler(Handler handler, bool first = false)
        {
            if (handler == null) throw new ArgumentNullException("handler");

            Helpers.RequireUIThread();

            if (_handlerSet.Contains(handler)) return false;

            if (first) { _handlerSet.Insert(0, handler); }
            else { _handlerSet.Add(handler); }
            return true;
        }

        /// <summary>
        /// Remove an existing query handler. Any pending queries associated with the
        /// handler will be canceled. Handler::OnQueryCanceled will be called and the
        /// associated JavaScript onFailure callback will be executed with an error
        /// code of -1. Returns true if the handler is removed successfully or false
        /// if the handler is not found. Must be called on the browser process UI
        /// thread.
        /// </summary>
        public bool RemoveHandler(Handler handler)
        {
            if (handler == null) throw new ArgumentNullException("handler");

            Helpers.RequireUIThread();

            if (_handlerSet.Remove(handler))
            {
                CancelPendingFor(null, handler, true);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Cancel all pending queries associated with either |browser| or |handler|.
        /// If both |browser| and |handler| are NULL all pending queries will be
        /// canceled. Handler::OnQueryCanceled will be called and the associated
        /// JavaScript onFailure callback will be executed in all cases with an error
        /// code of -1.
        /// </summary>
        public void CancelPending(CefBrowser browser = null, Handler handler = null)
        {
            CancelPendingFor(browser, handler, true);
        }

        /// <summary>
        /// Returns the number of queries currently pending for the specified |browser|
        /// and/or |handler|. Either or both values may be empty. Must be called on the
        /// browser process UI thread.
        /// </summary>
        public int GetPendingCount(CefBrowser browser = null, Handler handler = null)
        {
            Helpers.RequireUIThread();

            if (_browserQueryInfoMap.IsEmpty) return 0;

            if (handler != null)
            {
                int count = 0;
                BrowserInfoMap.Visitor visitor = (int browserId, long key, QueryInfo value, ref bool remove) =>
                {
                    if (value.Handler == handler) count++;
                    return true;
                };

                if (browser != null)
                {
                    _browserQueryInfoMap.FindAll(browser.Identifier, visitor);
                }
                else
                {
                    _browserQueryInfoMap.FindAll(visitor);
                }

                return count;
            }
            else if (browser != null)
            {
                return _browserQueryInfoMap.Count(browser.Identifier);
            }
            else
            {
                return _browserQueryInfoMap.Count();
            }
        }

        #region The below methods should be called from other CEF handlers. They must be called exactly as documented for the router to function correctly.

        /// <summary>
        /// Call from CefLifeSpanHandler::OnBeforeClose. Any pending queries associated
        /// with |browser| will be canceled and Handler::OnQueryCanceled will be called.
        /// No JavaScript callbacks will be executed since this indicates destruction
        /// of the browser.
        /// </summary>
        public void OnBeforeClose(CefBrowser browser)
        {
            CancelPendingFor(browser, null, false);
        }

        /// <summary>
        /// Call from CefRequestHandler::OnRenderProcessTerminated. Any pending queries
        /// associated with |browser| will be canceled and Handler::OnQueryCanceled
        /// will be called. No JavaScript callbacks will be executed since this
        /// indicates destruction of the context.
        /// </summary>
        public void OnRenderProcessTerminated(CefBrowser browser)
        {
            CancelPendingFor(browser, null, false);
        }

        /// <summary>
        /// Call from CefRequestHandler::OnBeforeBrowse only if the navigation is
        /// allowed to proceed. If |frame| is the main frame then any pending queries
        /// associated with |browser| will be canceled and Handler::OnQueryCanceled
        /// will be called. No JavaScript callbacks will be executed since this
        /// indicates destruction of the context.
        /// </summary>
        public void OnBeforeBrowse(CefBrowser browser, CefFrame frame)
        {
            if (frame.IsMain) CancelPendingFor(browser, null, false);
        }

        /// <summary>
        /// Call from CefClient::OnProcessMessageReceived. Returns true if the message
        /// is handled by this router or false otherwise.
        /// </summary>
        public bool OnProcessMessageReceived(CefBrowser browser, CefFrame frame, CefProcessId sourceProcess, CefProcessMessage message)
        {
            Helpers.RequireUIThread();

            var messageName = message.Name;
            if (messageName == _queryMessageName)
            {
                var args = message.Arguments;
                Debug.Assert(args.Count == 6);

                var contextId = args.GetInt(2);
                var requestId = args.GetInt(3);
                var request = args.GetString(4);
                var persistent = args.GetBool(5);

                if (_handlerSet.Count == 0)
                {
                    CancelUnhandledQuery(frame, contextId, requestId);
                    return true;
                }

                var browserId = browser.Identifier;
                var queryId = _queryIdGenerator.GetNextId();

                var callback = new Callback(this, browserId, queryId, persistent);

                var handlers = _handlerSet.ToArray();

                var handled = false;
                Handler handler = null;
                foreach (var x in handlers)
                {
                    handled = x.OnQuery(browser, frame, queryId, request, persistent, callback);
                    if (handled)
                    {
                        handler = x;
                        break;
                    }
                }

                if (handled)
                {
                    var info = new QueryInfo
                    {
                        Browser = browser,
                        Frame = frame,
                        ContextId = contextId,
                        RequestId = requestId,
                        Persistent = persistent,
                        Callback = callback,
                        Handler = handler,
                    };
                    _browserQueryInfoMap.Add(browserId, queryId, info);
                }
                else
                {
                    callback.Detach();

                    CancelUnhandledQuery(frame, contextId, requestId);
                }

                return true;
            }
            else if (messageName == _cancelMessageName)
            {
                var args = message.Arguments;
                Debug.Assert(args.Count == 2);

                var browserId = browser.Identifier;
                var contextId = args.GetInt(0);
                var requestId = args.GetInt(1);

                CancelPendingRequest(browserId, contextId, requestId);
                return true;
            }

            return false;
        }

        #endregion

        internal sealed class QueryInfo
        {
            public CefBrowser Browser;
            public CefFrame Frame;

            public int ContextId;
            public int RequestId;

            public bool Persistent;

            public Callback Callback;

            public Handler Handler;

            public void Dispose()
            {
                Browser = null;
            }
        };

        private QueryInfo GetQueryInfo(int browserId, long queryId, bool alwaysRemove, ref bool removed)
        {
            bool removedTemp = false;
            BrowserInfoMap.Visitor visitor = (int browserId_, long key, QueryInfo value, ref bool remove) =>
            {
                remove = removedTemp = alwaysRemove || !value.Persistent;
                return true;
            };
            var info = _browserQueryInfoMap.Find(browserId, queryId, visitor);
            if (info != null) removed = removedTemp;
            return info;
        }

        private void OnCallbackSuccess(int browserId, long queryId, string response)
        {
            Helpers.RequireUIThread();

            bool removed = false;
            var info = GetQueryInfo(browserId, queryId, false, ref removed);
            if (info != null)
            {
                SendQuerySuccess(info, response);
                if (removed) info.Dispose();
            }
        }

        private void OnCallbackFailure(int browserId, long queryId, int errorCode, string errorMessage)
        {
            Helpers.RequireUIThread();

            bool removed = false;
            var info = GetQueryInfo(browserId, queryId, true, ref removed);
            if (info != null)
            {
                SendQueryFailure(info, errorCode, errorMessage);
                Debug.Assert(removed);
                info.Dispose();
            }
        }

        private void SendQuerySuccess(QueryInfo info, string response)
        {
            SendQuerySuccess(info.Frame, info.ContextId, info.RequestId, response);
        }

        private void SendQuerySuccess(CefFrame frame, int contextId, int requestId, string response)
        {
            if (!frame.IsValid)
                return;

            var message = CefProcessMessage.Create(_queryMessageName);
            var args = message.Arguments;
            args.SetInt(0, contextId);
            args.SetInt(1, requestId);
            args.SetBool(2, true);
            args.SetString(3, response);

            frame.SendProcessMessage(CefProcessId.Renderer, message);

            args.Dispose();
            message.Dispose();
        }

        private void SendQueryFailure(QueryInfo info, int errorCode, string errorMessage)
        {
            SendQueryFailure(info.Frame, info.ContextId, info.RequestId, errorCode, errorMessage);
        }

        private void SendQueryFailure(CefFrame frame, int contextId, int requestId, int errorCode, string errorMessage)
        {
            if (!frame.IsValid)
                return;

            var message = CefProcessMessage.Create(_queryMessageName);
            var args = message.Arguments;
            args.SetInt(0, contextId);
            args.SetInt(1, requestId);
            args.SetBool(2, false);
            args.SetInt(3, errorCode);
            args.SetString(4, errorMessage);

            frame.SendProcessMessage(CefProcessId.Renderer, message);

            args.Dispose();
            message.Dispose();
        }

        private void CancelUnhandledQuery(CefFrame frame, int contextId, int requestId)
        {
            SendQueryFailure(frame, contextId, requestId, CefMessageRouter.CanceledErrorCode, CefMessageRouter.CanceledErrorMessage);
        }

        private void CancelQuery(long queryId, QueryInfo info, bool notifyRenderer)
        {
            if (notifyRenderer)
                SendQueryFailure(info, CefMessageRouter.CanceledErrorCode, CefMessageRouter.CanceledErrorMessage);

            info.Handler.OnQueryCanceled(info.Browser, info.Frame, queryId);

            info.Callback.Detach();
        }

        private void CancelPendingFor(CefBrowser browser, Handler handler, bool notifyRenderer)
        {
            if (!CefRuntime.CurrentlyOn(CefThreadId.UI))
            {
                Helpers.PostTask(CefThreadId.UI,
                    Helpers.Apply(this.CancelPendingFor, browser, handler, notifyRenderer)
                    );
                return;
            }

            if (_browserQueryInfoMap.IsEmpty) return;

            BrowserInfoMap.Visitor visitor = (int browserId, long queryId, QueryInfo info, ref bool remove) =>
            {
                if (handler == null || info.Handler == handler)
                {
                    remove = true;
                    CancelQuery(queryId, info, notifyRenderer);
                    info.Dispose();
                }
                return true;
            };

            if (browser != null)
            {
                _browserQueryInfoMap.FindAll(browser.Identifier, visitor);
            }
            else
            {
                _browserQueryInfoMap.FindAll(visitor);
            }
        }

        private void CancelPendingRequest(int browserId, int contextId, int requestId)
        {
            BrowserInfoMap.Visitor visitor = (int vBrowserId, long queryId, QueryInfo info, ref bool remove) =>
            {
                if (info.ContextId == contextId
                    && (requestId == CefMessageRouter.ReservedId || info.RequestId == requestId))
                {
                    remove = true;
                    CancelQuery(queryId, info, false);
                    info.Dispose();

                    return requestId == CefMessageRouter.ReservedId;
                }
                return true;
            };

            _browserQueryInfoMap.FindAll(browserId, visitor);
        }
    }
}
