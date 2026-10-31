using System;

namespace Xilium.CefGlue.Common.InternalHandlers
{
    internal sealed class CommonCefAudioHandler : CefAudioHandler
    {
        private readonly ICefBrowserHost _owner;

        private int _channels;

        public CommonCefAudioHandler(ICefBrowserHost owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        protected override bool GetAudioParameters(CefBrowser browser, CefAudioParameters parameters)
            => _owner.CaptureAudio;

        protected override void OnAudioStreamStarted(CefBrowser browser, in CefAudioParameters parameters, int channels)
        {
            _channels = channels;
            _owner.HandleAudioStreamStarted(browser, parameters.SampleRate, channels);
        }

        protected override unsafe void OnAudioStreamPacket(CefBrowser browser, IntPtr data, int frames, long pts)
        {
            var channels = _channels;

            if (channels <= 0 || frames <= 0 || data == IntPtr.Zero)
            {
                return;
            }

            var channelPointers = (float**)data;
            var channelData = new float[channels][];

            for (var c = 0; c < channels; c++)
            {
                var buffer = new float[frames];
                new ReadOnlySpan<float>(channelPointers[c], frames).CopyTo(buffer);
                channelData[c] = buffer;
            }

            _owner.HandleAudioStreamPacket(browser, channelData, frames, pts);
        }

        protected override void OnAudioStreamStopped(CefBrowser browser)
        {
            _channels = 0;
            _owner.HandleAudioStreamStopped(browser);
        }

        protected override void OnAudioStreamError(CefBrowser browser, string message)
        {
            System.Diagnostics.Debug.WriteLine($"[CommonCefAudioHandler] Audio stream error: {message}");
        }
    }
}
