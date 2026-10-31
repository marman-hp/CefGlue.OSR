using System;
using System.IO;
using System.IO.Pipes;
using System.Threading;

namespace Xilium.CefGlue.Common.Shared.RendererProcessCommunication
{
    internal class PipeServer : IDisposable
    {
        private const int MaxErrorsAllowed = 5;

        private volatile bool _stopping;
        private readonly object _disposeLock = new object();
        private readonly string _pipeName;
        private readonly Thread _listenerThread;
        private bool _disposed;

        public event Action<string> MessageReceived;

        public PipeServer(string pipeName)
        {
            _pipeName = pipeName;

            _listenerThread = new Thread(Listen)
            {
                IsBackground = true,
                Name = $"PipeServer[{pipeName}]",
            };
            _listenerThread.Start();
        }

        private void Listen()
        {
            var errorCount = 0;
            while (!_stopping)
            {
                try
                {
                    using (var serverPipe = new NamedPipeServerStream(_pipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.None))
                    {
                        serverPipe.WaitForConnection();

                        if (_stopping)
                        {
                            break;
                        }

                        HandleClientConnected(serverPipe);
                    }

                    errorCount = 0;
                }
                catch
                {
                    errorCount++;
                    if (errorCount > MaxErrorsAllowed)
                    {
                        break;
                    }
                }
            }
        }

        public void Dispose()
        {
            lock (_disposeLock)
            {
                if (_disposed)
                {
                    return;
                }
                _disposed = true;
                _stopping = true;

                try
                {
                    using var nudge = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
                    nudge.Connect(200);
                }
                catch
                {
                }
            }

            MessageReceived = null;
        }

        private void HandleClientConnected(Stream pipe)
        {
            var messageReceivedHandler = MessageReceived;
            if (messageReceivedHandler == null)
            {
                return;
            }

            var stream = new PipeStream(pipe);
            var message = stream.ReadString();

            try
            {
                messageReceivedHandler(message);
            }
            catch
            {
            }
        }
    }
}
