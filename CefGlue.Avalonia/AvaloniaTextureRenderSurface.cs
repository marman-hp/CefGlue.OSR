using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using System;
using System.Threading.Tasks;
using Xilium.CefGlue.Common.Helpers;

namespace Xilium.CefGlue.Avalonia
{
    internal unsafe class AvaloniaTextureRenderSurface : OffScreenRenderSurface
    {
        private static readonly D3D11 D3D11Api = D3D11.GetApi();

        private const int SlotCount = 2;
        private readonly TextureSlot[] _slots = new TextureSlot[SlotCount];
        private int _currentSlot = -1;

        private ID3D11Device* _device;
        private ID3D11Device1* _device1;
        private ID3D11DeviceContext* _context;

        private readonly object _deviceLock = new object();
        private bool _disposed;

        private const uint AcquireSyncTimeoutMs = 2000;

        public override bool AllowsTransparency => true;

        public bool UseOSRDirtyRect { get; set; } = true;

        public bool ShowOverlayInfo { get; set; } = false;

        public IntPtr CurrentHandle => _currentSlot >= 0 ? _slots[_currentSlot].Handle : IntPtr.Zero;

        public int CurrentWidth => _currentSlot >= 0 ? _slots[_currentSlot].Width : 0;

        public int CurrentHeight => _currentSlot >= 0 ? _slots[_currentSlot].Height : 0;

        protected override Task ExecuteInUIThread(Action action)
        {
            action();
            return Task.CompletedTask;
        }

        protected override (int width, int height) GetAcceleratedFrameSize(CefAcceleratedPaintInfo info)
            => (info.Extra.CodedSize.Width, info.Extra.CodedSize.Height);

        protected override void DeliverSoftwareFrame(IntPtr buffer, int width, int height, CefRectangle[] dirtyRects) { }

        protected override void DeliverAcceleratedFrame(CefAcceleratedPaintInfo info, CefRectangle[] dirtyRects)
        {
            if (CefRuntime.Platform != CefRuntimePlatform.Windows)
            {
                throw new PlatformNotSupportedException(
                    "TextureControl (GPU passthrough) is only supported on Windows for CefGlue.Avalonia currently.");
            }

            lock (_deviceLock)
            {
                if (_disposed)
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

                    var slotChanged = EnsureCurrentSlot(width, height);
                    ref var slot = ref _slots[_currentSlot];
                    var willUsePartialCopy = UseOSRDirtyRect && !slotChanged && dirtyRects != null && dirtyRects.Length > 0;

                    const int WaitTimeout = 0x102;
                    var acquireResult = slot.KeyedMutex->AcquireSync(0, AcquireSyncTimeoutMs);
                    if (acquireResult == WaitTimeout)
                    {
                        return;
                    }
                    SilkMarshal.ThrowHResult(acquireResult);
                    try
                    {
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
                                    (ID3D11Resource*)slot.Texture, 0, left, top, 0,
                                    (ID3D11Resource*)cefTexture, 0, &box);
                            }
                        }
                        else
                        {
                            _context->CopyResource((ID3D11Resource*)slot.Texture, (ID3D11Resource*)cefTexture);
                        }
                    }
                    finally
                    {
                        SilkMarshal.ThrowHResult(slot.KeyedMutex->ReleaseSync(1));
                    }
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
                    D3DDriverType.Hardware,
                    Software: IntPtr.Zero,
                    Flags: (uint)CreateDeviceFlag.BgraSupport,
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

        private bool EnsureCurrentSlot(int width, int height)
        {
            if (_currentSlot >= 0 && _slots[_currentSlot].Width == width && _slots[_currentSlot].Height == height)
            {
                return false;
            }

            var nextSlot = (_currentSlot + 1) % SlotCount;

            ReleaseSlot(nextSlot);
            CreateSlot(nextSlot, width, height);

            _currentSlot = nextSlot;
            return true;
        }

        private void CreateSlot(int index, int width, int height)
        {
            var desc = new Texture2DDesc
            {
                Width = (uint)width,
                Height = (uint)height,
                MipLevels = 1,
                ArraySize = 1,
                Format = Silk.NET.DXGI.Format.FormatB8G8R8A8Unorm,
                SampleDesc = new SampleDesc(1, 0),
                Usage = Usage.Default,
                BindFlags = (uint)(BindFlag.RenderTarget | BindFlag.ShaderResource),
                MiscFlags = (uint)ResourceMiscFlag.SharedKeyedmutex,
            };

            ID3D11Texture2D* texture = null;
            SilkMarshal.ThrowHResult(_device->CreateTexture2D(&desc, null, &texture));

            var mutexGuid = IDXGIKeyedMutex.Guid;
            void* mutexPtr = null;
            SilkMarshal.ThrowHResult(texture->QueryInterface(&mutexGuid, &mutexPtr));

            var resourceGuid = IDXGIResource.Guid;
            void* resourcePtr = null;
            SilkMarshal.ThrowHResult(texture->QueryInterface(&resourceGuid, &resourcePtr));
            var dxgiResource = (IDXGIResource*)resourcePtr;

            void* handle;
            SilkMarshal.ThrowHResult(dxgiResource->GetSharedHandle(&handle));
            dxgiResource->Release();

            _slots[index] = new TextureSlot
            {
                Texture = texture,
                KeyedMutex = (IDXGIKeyedMutex*)mutexPtr,
                Handle = (IntPtr)handle,
                Width = width,
                Height = height,
            };
        }

        private void ReleaseSlot(int index)
        {
            ref var slot = ref _slots[index];
            if (slot.KeyedMutex != null) { slot.KeyedMutex->Release(); slot.KeyedMutex = null; }
            if (slot.Texture != null) { slot.Texture->Release(); slot.Texture = null; }
            slot.Handle = IntPtr.Zero;
            slot.Width = 0;
            slot.Height = 0;
        }

        public override void Dispose()
        {
            lock (_deviceLock)
            {
                _disposed = true;

                for (var i = 0; i < SlotCount; i++)
                {
                    ReleaseSlot(i);
                }

                if (_device1 != null) { _device1->Release(); _device1 = null; }
                if (_context != null) { _context->Release(); _context = null; }
                if (_device != null) { _device->Release(); _device = null; }
            }

            base.Dispose();
        }

        private struct TextureSlot
        {
            public ID3D11Texture2D* Texture;
            public IDXGIKeyedMutex* KeyedMutex;
            public IntPtr Handle;
            public int Width;
            public int Height;
        }
    }
}
