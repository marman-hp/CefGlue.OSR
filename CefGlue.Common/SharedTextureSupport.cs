using System;
using System.IO;
using System.Runtime.InteropServices;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D11;
using Silk.NET.DXGI;
using Xilium.CefGlue.Common;

namespace Xilium.CefGlue.Common.Helpers
{
    /// <summary>
    /// Pre-flight check for shared-texture rendering, run before the CEF browser is created (shared_texture_enabled
    /// can't be switched off afterwards). Covers what is knowable up front:
    /// GPU disabled for CEF; on Windows no hardware D3D11 / ID3D11Device1, or only the Microsoft Basic Render Driver
    /// (VMs, some RDP sessions); on Linux no DRM render node, or no system EGL with EGL_EXT_image_dma_buf_import. Linked into the host projects (they carry Silk.NET); not compiled into Common.
    /// </summary>
    internal static unsafe class SharedTextureSupport
    {
        private const uint MicrosoftVendorId = 0x1414;
        private const uint BasicRenderDriverDeviceId = 0x8C;

        private static readonly Lazy<string> _deviceFailure = new Lazy<string>(ProbeDevice);

        /// <returns>null when shared textures can be used, otherwise the reason they can't.</returns>
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
            if (OperatingSystem.IsWindows())
            {
                return ProbeD3D11();
            }

            if (OperatingSystem.IsLinux())
            {
                return ProbeEglDmaBuf();
            }

            return "Shared textures are only supported on Windows and Linux.";
        }

        private static string ProbeD3D11()
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

        private const int EGL_EXTENSIONS = 0x3055;

        // CEF exports its frames as dma-bufs, which the host imports through EGL_EXT_image_dma_buf_import.
        // The display is left initialized: it is the same EGLDisplay the UI toolkit may already be using,
        // and eglTerminate would tear it down for everyone.
        private static string ProbeEglDmaBuf()
        {
            // CEF's Linux builds ship no libEGL.so / libGLESv2.so; its GPU process uses the system libEGL.so.1 /
            // libGLESv2.so.2, so only the system library is checked below.

            // CEF allocates its dma-buf frames through GBM on a DRM render node. Without one (e.g. WSL) it sends
            // no frames at all, not even OnPaint, so nothing could trigger the runtime fallback.
            if (!Directory.Exists("/dev/dri") || !Directory.EnumerateFileSystemEntries("/dev/dri", "renderD*").GetEnumerator().MoveNext())
            {
                return "No DRM render node (/dev/dri/renderD*), so CEF can't allocate dma-buf frames.";
            }

            if (!NativeLibrary.TryLoad("libEGL.so.1", out var egl))
            {
                return "libEGL.so.1 is not installed.";
            }

            try
            {
                var getDisplay = (delegate* unmanaged<IntPtr, IntPtr>)NativeLibrary.GetExport(egl, "eglGetDisplay");
                var initialize = (delegate* unmanaged<IntPtr, int*, int*, int>)NativeLibrary.GetExport(egl, "eglInitialize");
                var queryString = (delegate* unmanaged<IntPtr, int, IntPtr>)NativeLibrary.GetExport(egl, "eglQueryString");

                var display = getDisplay(IntPtr.Zero);
                int major, minor;
                if (display == IntPtr.Zero || initialize(display, &major, &minor) == 0)
                {
                    return "No EGL display could be initialized.";
                }

                var extensions = Marshal.PtrToStringAnsi(queryString(display, EGL_EXTENSIONS)) ?? string.Empty;
                if (!extensions.Contains("EGL_EXT_image_dma_buf_import"))
                {
                    return "EGL has no EGL_EXT_image_dma_buf_import (needed to import CEF's dma-buf frames).";
                }

                return null;
            }
            catch (Exception ex)
            {
                return $"EGL probe failed: {ex.Message}";
            }
        }
    }
}
