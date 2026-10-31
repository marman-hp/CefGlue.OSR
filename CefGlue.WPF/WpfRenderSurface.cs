using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using System;
using System.Threading.Tasks;
using Xilium.CefGlue.Common.Helpers;

namespace Xilium.CefGlue.WPF
{
    internal class WpfRenderSurface<TSurface, TFrame> : CpuStagedRenderSurface
        where TSurface : IFrameSurface<TFrame>
    {
        private static readonly D3D11 D3D11Api = D3D11.GetApi();

        private readonly TSurface _surface;

        private unsafe ID3D11Device* _d3dDevice;
        private unsafe ID3D11DeviceContext* _d3dContext;
        private unsafe ID3D11Device1* _d3dDevice1;
        private unsafe ID3D11Texture2D* _stagingTexture;

        private int _stagingWidth;
        private int _stagingHeight;
        private bool _stagingMapped;

        public WpfRenderSurface(TSurface surface)
        {
            _surface = surface;
        }

        public override bool AllowsTransparency => true;

        protected override int BytesPerPixel => 4;

        protected override bool UpdateDirtyRegionsOnly => true;

        protected override int RenderedWidth => _surface?.Width ?? 0;

        protected override int RenderedHeight => _surface?.Height ?? 0;

        protected override void CreateSurface(int width, int height)
        {
            _surface.BeginFrame(width, height);
        }

        protected override Action BeginSurfaceUpdate()
        {
            return () => _surface.EndFrame();
        }

        protected override Task ExecuteInUIThread(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        protected override void UpdateBitmap(IntPtr sourceBuffer, int sourceBufferSize, int stride, CefRectangle updateRegion)
        {
            _surface.IsAccelerated = IsAccelerated;
            _surface.CopyDirtyFrame(sourceBuffer, stride, updateRegion);
        }

        protected override unsafe PixelBuffer ReadAcceleratedTexture(CefAcceleratedPaintInfo info)
        {
            EnsureD3DDevice();

            var textureGuid = ID3D11Texture2D.Guid;
            void* sharedTexturePointer = null;

            SilkMarshal.ThrowHResult(
                _d3dDevice1->OpenSharedResource1(
                    (void*)info.SharedTexture,
                    &textureGuid,
                    &sharedTexturePointer));

            var sharedTexture = (ID3D11Texture2D*)sharedTexturePointer;

            try
            {
                Texture2DDesc description;
                sharedTexture->GetDesc(&description);

                var width = (int)description.Width;
                var height = (int)description.Height;

                if (width <= 0 || height <= 0)
                {
                    return default;
                }

                EnsureStagingTexture(description);

                _d3dContext->CopyResource(
                    (ID3D11Resource*)_stagingTexture,
                    (ID3D11Resource*)sharedTexture);

                MappedSubresource mapped;

                SilkMarshal.ThrowHResult(
                    _d3dContext->Map(
                        (ID3D11Resource*)_stagingTexture,
                        0,
                        Map.MapRead,
                        0,
                        &mapped));

                _stagingMapped = true;

                var stride = (int)mapped.RowPitch;
                var byteCount = (long)stride * height;

                if (mapped.PData == null ||
                    stride <= 0 ||
                    byteCount <= 0 ||
                    byteCount > int.MaxValue)
                {
                    return default;
                }

                return new PixelBuffer(
                    (IntPtr)mapped.PData,
                    width,
                    height,
                    stride,
                    (int)byteCount);
            }
            finally
            {
                sharedTexture->Release();
            }
        }

        protected override unsafe void ReleaseAcceleratedTexture()
        {
            if (!_stagingMapped)
            {
                return;
            }

            _d3dContext->Unmap((ID3D11Resource*)_stagingTexture, 0);
            _stagingMapped = false;
        }

        private unsafe void EnsureD3DDevice()
        {
            if (_d3dDevice != null)
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

            _d3dDevice = device;
            _d3dContext = context;

            var device1Guid = ID3D11Device1.Guid;
            void* device1Pointer = null;

            SilkMarshal.ThrowHResult(
                _d3dDevice->QueryInterface(&device1Guid, &device1Pointer));

            _d3dDevice1 = (ID3D11Device1*)device1Pointer;
        }

        private unsafe void EnsureStagingTexture(Texture2DDesc description)
        {
            var width = (int)description.Width;
            var height = (int)description.Height;

            if (_stagingTexture != null &&
                _stagingWidth == width &&
                _stagingHeight == height)
            {
                return;
            }

            if (_stagingTexture != null)
            {
                _stagingTexture->Release();
                _stagingTexture = null;
            }

            var stagingDescription = description;
            stagingDescription.Usage = Usage.UsageStaging;
            stagingDescription.BindFlags = 0;
            stagingDescription.CPUAccessFlags = (uint)CpuAccessFlag.CpuAccessRead;
            stagingDescription.MiscFlags = 0;

            ID3D11Texture2D* stagingTexture = null;

            SilkMarshal.ThrowHResult(
                _d3dDevice->CreateTexture2D(
                    &stagingDescription,
                    null,
                    &stagingTexture));

            _stagingTexture = stagingTexture;
            _stagingWidth = width;
            _stagingHeight = height;
        }

        public override void Dispose()
        {
            unsafe
            {
                if (_stagingMapped)
                {
                    _d3dContext->Unmap((ID3D11Resource*)_stagingTexture, 0);
                    _stagingMapped = false;
                }
                if (_stagingTexture != null) { _stagingTexture->Release(); _stagingTexture = null; }
                if (_d3dDevice1 != null) { _d3dDevice1->Release(); _d3dDevice1 = null; }
                if (_d3dContext != null) { _d3dContext->Release(); _d3dContext = null; }
                if (_d3dDevice != null) { _d3dDevice->Release(); _d3dDevice = null; }
            }
            base.Dispose();
        }
    }
}
