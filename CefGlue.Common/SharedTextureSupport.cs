using System;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using Xilium.CefGlue.Common;

namespace Xilium.CefGlue.Common.Helpers
{
    /// <summary>
    /// Pre-flight check for TextureControl, run before the CEF browser is created (shared_texture_enabled
    /// can't be switched off afterwards). Covers what is knowable up front: GPU disabled for CEF, no hardware
    /// D3D11 / ID3D11Device1, or only the Microsoft Basic Render Driver (VMs, some RDP sessions).
    /// Linked into CefGlue.WinUI and CefGlue.Avalonia (they carry Silk.NET); not compiled into Common.
    /// </summary>
    internal static unsafe class SharedTextureSupport
    {
        private const uint MicrosoftVendorId = 0x1414;
        private const uint BasicRenderDriverDeviceId = 0x8C;

        private static readonly Lazy<string> _deviceFailure = new Lazy<string>(ProbeDevice);

        /// <returns>null when TextureControl can be used, otherwise the reason it can't.</returns>
        public static string GetUnsupportedReason()
        {
            if (CefRuntimeLoader.IsGpuCompositingDisabled)
            {
                return "GPU compositing is disabled for CEF (--disable-gpu / --disable-gpu-compositing).";
            }

            return _deviceFailure.Value;
        }

        private static string ProbeDevice()
        {
            ID3D11Device* device = null;
            ID3D11DeviceContext* context = null;
            IDXGIDevice* dxgiDevice = null;
            IDXGIAdapter* adapter = null;
            void* device1 = null;

            try
            {
                var hr = D3D11.GetApi().CreateDevice(
                    pAdapter: null,
                    D3DDriverType.D3DDriverTypeHardware,
                    Software: IntPtr.Zero,
                    Flags: (uint)CreateDeviceFlag.CreateDeviceBgraSupport,
                    pFeatureLevels: null,
                    FeatureLevels: 0,
                    SDKVersion: D3D11.SdkVersion,
                    ppDevice: &device,
                    pFeatureLevel: null,
                    ppImmediateContext: &context);

                if (HResult.IndicatesFailure(hr))
                {
                    return $"No hardware D3D11 device (0x{hr:X8}).";
                }

                var device1Guid = ID3D11Device1.Guid;
                if (HResult.IndicatesFailure(device->QueryInterface(&device1Guid, &device1)))
                {
                    return "ID3D11Device1 is not available (needed to open CEF's shared texture).";
                }

                var dxgiDeviceGuid = IDXGIDevice.Guid;
                if (HResult.IndicatesSuccess(device->QueryInterface(&dxgiDeviceGuid, (void**)&dxgiDevice))
                    && HResult.IndicatesSuccess(dxgiDevice->GetAdapter(&adapter)))
                {
                    AdapterDesc desc;
                    if (HResult.IndicatesSuccess(adapter->GetDesc(&desc))
                        && desc.VendorId == MicrosoftVendorId && desc.DeviceId == BasicRenderDriverDeviceId)
                    {
                        return "Only the Microsoft Basic Render Driver is available (no real GPU).";
                    }
                }

                return null;
            }
            catch (Exception ex)
            {
                return $"D3D11 probe failed: {ex.Message}";
            }
            finally
            {
                if (adapter != null) adapter->Release();
                if (dxgiDevice != null) dxgiDevice->Release();
                if (device1 != null) ((ID3D11Device1*)device1)->Release();
                if (context != null) context->Release();
                if (device != null) device->Release();
            }
        }
    }
}
