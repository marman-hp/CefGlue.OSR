using System;
using System.Collections.Specialized;
using System.IO;
using System.Threading;

namespace Xilium.CefGlue.Common.Handlers
{
    public class DefaultResourceHandler : CefResourceHandler
    {
        private Stream _responseStream;
        private long _responseStreamReadPosition = -1;

        public DefaultResourceHandler()
        {
            Headers.Add("Access-Control-Allow-Origin", "*");
        }

        public string MimeType { get; set; }

        public Stream Response
        {
            get => _responseStream;
            set
            {
                if (_responseStreamReadPosition > -1)
                {
                    throw new Exception($"Cannot set {nameof(Response)} Stream after request handling started");
                }
                _responseStream = value;
            }
        }

        public CefErrorCode ErrorCode { get; set; }

        public int Status { get; set; } = 200;

        public string StatusText { get; set; } = "OK";

        public NameValueCollection Headers { get; set; } = new NameValueCollection();

        public string RedirectUrl { get; set; }

        protected override void Cancel()
        {
        }

        protected override void GetResponseHeaders(CefResponse response, out long responseLength, out string outRedirectUrl)
        {
            outRedirectUrl = null;

            var headers = Headers;
            if (headers != null)
            {
                response.SetHeaderMap(headers);
            }

            var errorCode = ErrorCode;
            if (errorCode != CefErrorCode.None)
            {
                response.Error = errorCode;
                responseLength = 0;
                return;
            }

            responseLength = -1;

            response.MimeType = MimeType ?? "text/html";
            response.Status = Status;
            response.StatusText = StatusText;

            var redirectUrl = RedirectUrl;
            if (redirectUrl != null)
            {
                outRedirectUrl = redirectUrl;
                return;
            }

            var responseStream = _responseStream;
            if (responseStream?.CanSeek == true)
            {
                responseLength = responseStream.Length;
            }
        }

        protected override bool Open(CefRequest request, out bool handleRequest, CefCallback callback)
        {
            var fashion = ProcessRequestAsync(request, callback);
            switch (fashion)
            {
                case RequestHandlingFashion.Continue:
                    handleRequest = true;
                    return true;

                case RequestHandlingFashion.ContinueAsync:
                    handleRequest = false;
                    return true;

                default:
                    handleRequest = true;
                    return false;
            }
        }

        protected virtual RequestHandlingFashion ProcessRequestAsync(CefRequest request, CefCallback callback)
        {
            return RequestHandlingFashion.Continue;
        }

        protected override bool Skip(long bytesToSkip, out long bytesSkipped, CefResourceSkipCallback callback)
        {
            InitializeStreamPositionIfNeeded();

            var responseStream = _responseStream;
            if (responseStream?.CanSeek != true)
            {
                bytesSkipped = -2;
                return false;
            }

            bytesSkipped = bytesToSkip;
            lock (responseStream)
            {
                _responseStreamReadPosition += bytesToSkip;
            }
            return true;
        }

        protected override bool Read(Stream outResponse, int bytesToRead, out int bytesRead, CefResourceReadCallback callback)
        {
            callback?.Dispose();

            InitializeStreamPositionIfNeeded();

            var responseStream = _responseStream;
            if (responseStream == null)
            {
                bytesRead = -2;
                return false;
            }

            var buffer = new byte[bytesToRead];

            lock (responseStream)
            {
                if (responseStream.Position != _responseStreamReadPosition)
                {
                    if (!responseStream.CanSeek)
                    {
                        bytesRead = -2;
                        return false;
                    }
                    responseStream.Position = _responseStreamReadPosition;
                }

                bytesRead = responseStream.Read(buffer, 0, buffer.Length);
                _responseStreamReadPosition = responseStream.Position;
            }

            if (bytesRead == 0)
            {
                return false;
            }

            outResponse.Write(buffer, 0, bytesRead);

            return bytesRead > 0;
        }

        private void InitializeStreamPositionIfNeeded() => Interlocked.CompareExchange(ref _responseStreamReadPosition, 0, -1);
    }
}
