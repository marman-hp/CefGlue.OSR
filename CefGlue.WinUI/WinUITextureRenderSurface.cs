using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml.Controls;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using System;
using System.Threading.Tasks;
using Xilium.CefGlue.Common.Helpers;
using Xilium.CefGlue.WinUI.Platform;

namespace Xilium.CefGlue.WinUI
{
    internal unsafe class WinUITextureRenderSurface : OffScreenRenderSurface
    {
        private static readonly D3D11 D3D11Api = D3D11.GetApi();

        private ID3D11Device* _device;
        private ID3D11Device1* _device1;
        private ID3D11DeviceContext* _context;
        private IDXGISwapChain1* _swapChain;

        private int _swapChainWidth;
        private int _swapChainHeight;

        private ID3D11Texture2D* _accumulatorTexture;

        private int _accumulatorCapacityWidth;
        private int _accumulatorCapacityHeight;

        private SwapChainPanel _panel;
        private DispatcherQueue _dispatcherQueue;

        private readonly object _deviceLock = new object();
        private bool _disposed;

        public override bool AllowsTransparency => true;

        public bool ShowOverlayInfo { get; set; } = false;

        public bool UseOSRDirtyRect { get; set; } = true;

        public int CurrentWidth => _swapChainWidth;

        public int CurrentHeight => _swapChainHeight;

        protected override Task ExecuteInUIThread(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        protected override (int width, int height) GetAcceleratedFrameSize(CefAcceleratedPaintInfo info)
            => (info.Extra.CodedSize.Width, info.Extra.CodedSize.Height);

        protected override void DeliverSoftwareFrame(IntPtr buffer, int width, int height, CefRectangle[] dirtyRects) { }

        public void AttachPanel(SwapChainPanel panel)
        {
            lock (_deviceLock)
            {
                _panel = panel;
                _dispatcherQueue = panel.DispatcherQueue;
            }
        }

        protected override void DeliverAcceleratedFrame(CefAcceleratedPaintInfo info, CefRectangle[] dirtyRects)
        {
            lock (_deviceLock)
            {
                if (_disposed || _panel == null)
                {
                    return;
                }

                EnsureDevice();

                var textureGuid = ID3D11Texture2D.Guid;
                void* openedPtr = null;

                SilkMarshal.ThrowHResult(
                    _device1->OpenSharedResource1((void*)info.SharedTexture, &textureGuid, &openedPtr));

                var cefTexture = (ID3D11Texture2D*)openedPtr;

                try
                {
                    Texture2DDesc desc;
                    cefTexture->GetDesc(&desc);

                    var width = (int)desc.Width;
                    var height = (int)desc.Height;

                    if (width <= 0 || height <= 0)
                    {
                        return;
                    }

                    var swapChainJustCreatedOrResized = EnsureSwapChain(width, height);
                    var willUsePartialCopy = UseOSRDirtyRect && !swapChainJustCreatedOrResized && dirtyRects != null && dirtyRects.Length > 0;

                    EnsureAccumulatorTexture(width, height);

                    var liveBox = new Box { Left = 0, Top = 0, Front = 0, Right = (uint)width, Bottom = (uint)height, Back = 1 };

                    if (willUsePartialCopy)
                    {
                        foreach (var rect in dirtyRects)
                        {
                            var left = (uint)Math.Clamp(rect.X, 0, width);
                            var top = (uint)Math.Clamp(rect.Y, 0, height);
                            var right = (uint)Math.Clamp(rect.X + rect.Width, 0, width);
                            var bottom = (uint)Math.Clamp(rect.Y + rect.Height, 0, height);

                            if (right <= left || bottom <= top)
                            {
                                continue;
                            }

                            var box = new Box { Left = left, Top = top, Front = 0, Right = right, Bottom = bottom, Back = 1 };
                            _context->CopySubresourceRegion(
                                (ID3D11Resource*)_accumulatorTexture, 0, left, top, 0,
                                (ID3D11Resource*)cefTexture, 0, &box);
                        }
                    }
                    else
                    {
                        _context->CopySubresourceRegion(
                            (ID3D11Resource*)_accumulatorTexture, 0, 0, 0, 0,
                            (ID3D11Resource*)cefTexture, 0, &liveBox);
                    }

                    ID3D11Texture2D* backBuffer = null;
                    var backBufferGuid = ID3D11Texture2D.Guid;
                    SilkMarshal.ThrowHResult(_swapChain->GetBuffer(0, &backBufferGuid, (void**)&backBuffer));

                    try
                    {
                        _context->CopySubresourceRegion(
                            (ID3D11Resource*)backBuffer, 0, 0, 0, 0,
                            (ID3D11Resource*)_accumulatorTexture, 0, &liveBox);
                    }
                    finally
                    {
                        backBuffer->Release();
                    }

                    SilkMarshal.ThrowHResult(_swapChain->Present(1, 0));
                }
                finally
                {
                    cefTexture->Release();
                }
            }
        }

        private void EnsureDevice()
        {
            if (_device != null)
            {
                return;
            }

            ID3D11Device* device = null;
            ID3D11DeviceContext* context = null;

            SilkMarshal.ThrowHResult(
                D3D11Api.CreateDevice(
                    pAdapter: null,
                    D3DDriverType.D3DDriverTypeHardware,
                    Software: IntPtr.Zero,
                    Flags: (uint)CreateDeviceFlag.CreateDeviceBgraSupport,
                    pFeatureLevels: null,
                    FeatureLevels: 0,
                    SDKVersion: D3D11.SdkVersion,
                    ppDevice: &device,
                    pFeatureLevel: null,
                    ppImmediateContext: &context));

            var device1Guid = ID3D11Device1.Guid;
            void* device1Pointer = null;
            SilkMarshal.ThrowHResult(device->QueryInterface(&device1Guid, &device1Pointer));

            _device = device;
            _context = context;
            _device1 = (ID3D11Device1*)device1Pointer;
        }

        private bool EnsureSwapChain(int width, int height)
        {
            if (_swapChain != null && _swapChainWidth == width && _swapChainHeight == height)
            {
                return false;
            }

            if (_swapChain != null)
            {
                SilkMarshal.ThrowHResult(
                    _swapChain->ResizeBuffers(2, (uint)width, (uint)height, Format.FormatB8G8R8A8Unorm, 0));
                _swapChainWidth = width;
                _swapChainHeight = height;

                var resizedPanel = _panel;
                var resizedSwapChain = (IntPtr)_swapChain;
                _dispatcherQueue?.TryEnqueue(() =>
                {
                    NativeInterop.SetSwapChainOnPanel(resizedPanel, resizedSwapChain);
                });

                return true;
            }

            var dxgiDeviceGuid = IDXGIDevice.Guid;
            void* dxgiDevicePtr = null;
            SilkMarshal.ThrowHResult(_device->QueryInterface(&dxgiDeviceGuid, &dxgiDevicePtr));
            var dxgiDevice = (IDXGIDevice*)dxgiDevicePtr;

            IDXGIAdapter* adapter = null;
            SilkMarshal.ThrowHResult(dxgiDevice->GetAdapter(&adapter));

            var factoryGuid = IDXGIFactory2.Guid;
            void* factoryPtr = null;
            SilkMarshal.ThrowHResult(adapter->GetParent(&factoryGuid, &factoryPtr));
            var factory = (IDXGIFactory2*)factoryPtr;

            var desc = new SwapChainDesc1
            {
                Width = (uint)width,
                Height = (uint)height,
                Format = Format.FormatB8G8R8A8Unorm,
                Stereo = 0,
                SampleDesc = new SampleDesc(1, 0),
                BufferUsage = DXGI.UsageRenderTargetOutput,
                BufferCount = 2,
                Scaling = Scaling.Stretch,
                SwapEffect = SwapEffect.FlipSequential,
                AlphaMode = Silk.NET.DXGI.AlphaMode.Premultiplied,
                Flags = 0,
            };

            IDXGISwapChain1* swapChain = null;
            SilkMarshal.ThrowHResult(
                factory->CreateSwapChainForComposition((IUnknown*)_device, &desc, null, &swapChain));

            factory->Release();
            adapter->Release();
            dxgiDevice->Release();

            _swapChain = swapChain;
            _swapChainWidth = width;
            _swapChainHeight = height;

            var panel = _panel;
            var swapChainToBind = (IntPtr)swapChain;
            _dispatcherQueue?.TryEnqueue(() =>
            {
                NativeInterop.SetSwapChainOnPanel(panel, swapChainToBind);
            });

            return true;
        }

        private void EnsureAccumulatorTexture(int width, int height)
        {
            if (_accumulatorTexture != null && _accumulatorCapacityWidth >= width && _accumulatorCapacityHeight >= height)
            {
                return;
            }

            var newCapacityWidth = Math.Max(_accumulatorCapacityWidth, width);
            var newCapacityHeight = Math.Max(_accumulatorCapacityHeight, height);

            if (_accumulatorTexture != null)
            {
                _accumulatorTexture->Release();
                _accumulatorTexture = null;
            }

            var desc = new Texture2DDesc
            {
                Width = (uint)newCapacityWidth,
                Height = (uint)newCapacityHeight,
                MipLevels = 1,
                ArraySize = 1,
                Format = Format.FormatB8G8R8A8Unorm,
                SampleDesc = new SampleDesc(1, 0),
                Usage = Usage.Default,
                BindFlags = 0,
                CPUAccessFlags = 0,
                MiscFlags = 0,
            };

            ID3D11Texture2D* texture = null;
            SilkMarshal.ThrowHResult(_device->CreateTexture2D(&desc, null, &texture));

            _accumulatorTexture = texture;
            _accumulatorCapacityWidth = newCapacityWidth;
            _accumulatorCapacityHeight = newCapacityHeight;
        }

        public override void Dispose()
        {
            lock (_deviceLock)
            {
                _disposed = true;

                if (_accumulatorTexture != null) { _accumulatorTexture->Release(); _accumulatorTexture = null; }
                if (_swapChain != null) { _swapChain->Release(); _swapChain = null; }
                if (_device1 != null) { _device1->Release(); _device1 = null; }
                if (_context != null) { _context->Release(); _context = null; }
                if (_device != null) { _device->Release(); _device = null; }
            }

            base.Dispose();
        }
    }
}
