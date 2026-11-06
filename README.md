# CefGlue.OSR

**Cross-platform Chromium Off-Screen Rendering for .NET**

A CEF-based browser implementation with GPU-accelerated rendering, native input handling, and ready-to-use browser controls for **WinForms, WPF, WinUI 3, Avalonia and GTK4**.

The same shared codebase is also used to develop **CAS (CEF as a Service)**.

---

## Overview

CefGlue.OSR brings Chromium's Off-Screen Rendering (OSR) capabilities to the .NET ecosystem.

CefGlue.OSR started as a fork of [OutSystems/CefGlue](https://github.com/OutSystems/CefGlue), itself derived from the original Xilium CefGlue project. The codebase has since been modified and extended with OSR support and browser controls for multiple .NET UI frameworks.

Browser lifecycle, rendering coordination, and event handling are implemented in framework-independent coordinators within `CefGlue.Common`.

The architecture uses **Microsoft.Extensions.DependencyInjection** to compose browser services and coordinators through `IServiceCollection`. This keeps the shared browser implementation independent of individual UI frameworks while allowing applications to provide their own services and integrations.

Each UI framework provides a thin platform-specific integration layer over the shared implementation.

**The result:** consistent browser behavior across multiple .NET UI frameworks, backed by a shared implementation and an extensible dependency injection architecture.

## Platform Support

| Platform | WinForms | WPF | WinUI 3 | Avalonia | GTK4 |
|---|:---:|:---:|:---:|:---:|:---:|
| Windows x64 | ✓ | ✓ | ✓ | ✓ | — |
| Linux x64 | — | — | — | ✓ | ✓ |

**Status:** No NuGet packages yet. Build from source.

> **Important: CEF binaries are not included.**
>
> This repository contains source code only. You must download the matching CEF distribution separately and deploy its runtime files alongside your application.
>
> See [Build](#build) for instructions.

## Features

### Rendering

- **CPU rendering** — bitmap-based rendering path.
- **GPU rendering** — shared texture path for supported hosts.
- **Fallback support** — WinForms and WPF fall back to CPU rendering when GPU texture sharing is unavailable.
- **Linux zero-copy rendering** — EGL DMA-BUF support for Avalonia and GTK4.

### Native Input & IME

Full IME composition support, including preedit, commit, cancel, and candidate-window positioning.

| Platform | Implementation |
|---|---|
| WinForms / WPF | Windows TSF text store |
| WinUI 3 | CoreTextEditContext |
| Avalonia (Windows) | Avalonia text input API |
| Avalonia (Linux) | Not supported yet — keys are sent as plain text |
| GTK4 | Not supported yet — keys are sent as plain text |

Windows implementations support per-clause composition underlines.

Linux IME is not supported yet. Avalonia 12.1's IBus/Fcitx5 integration drops the "the app draws the preedit" capability when it is sent before its D-Bus connection is ready, so the composition stays in the IME's own window. It will be revisited once that is fixed upstream.

### Browser Integration

- Docked DevTools with `ShowDevToolOSR()`.
- Drag and drop.
- Fullscreen support.
- Browser zoom.
- JavaScript bindings.
- Standard CEF event handlers.
- Popup and new-window handling.
- Permission prompts through `browser.PermissionHandler`. Browsers are Alloy style, where CEF answers an unhandled prompt with *ignore* and the request stays pending (e.g. Local Network Access holding a page's WebSocket to `localhost`).

### Architecture & Dependency Injection

CefGlue.OSR uses a shared coordinator architecture built around **Microsoft.Extensions.DependencyInjection**.

- **Shared coordinators** — browser lifecycle, rendering coordination, and event handling are implemented independently of UI frameworks.
- **IServiceCollection integration** — browser services and coordinators can be registered and resolved through the standard .NET dependency injection container.
- **Framework-specific adapters** — each UI framework provides a thin integration layer over the shared implementation.
- **Custom service integration** — applications can provide their own services and coordinator implementations.
- **DI and non-DI usage** — dependency injection is supported without removing the traditional API.
- **Your app's own container** — `services.AddCefGlue(b => b.UseAvalonia())` registers CefGlue in an existing `IServiceCollection` (ASP.NET Core, Generic Host), so the app and the coordinators share one set of singletons; `CefGlueHost.CreateBuilder()` remains for a container of CefGlue's own.

The `DemoDI` projects demonstrate how to integrate CefGlue.OSR using `IServiceCollection`.

## Demos

Every demo provides a consistent browser experience:

- Tabbed browsing.
- Back, forward, and reload navigation.
- Address bar.
- Docked DevTools in a resizable right-side panel.
- F12 to toggle DevTools.
- Open links in new tabs through the context menu.
- Popups, including Google sign-in, in their own windows.

The CEF cache is stored in `cef_temp` next to the demo executable.

## Build

**Requirements**

- .NET 10 SDK
- x64 environment
- CEF 154 (154.0.33)

**CEF downloads** (154.0.33, minimal distribution, from the official [CEF builds](https://cef-builds.spotifycdn.com/index.html) site):

- Windows x64: [cef_binary_154.0.33+ga03e714+chromium-154.0.8037.94_windows64_minimal.tar.bz2](https://cef-builds.spotifycdn.com/cef_binary_154.0.33%2Bga03e714%2Bchromium-154.0.8037.94_windows64_minimal.tar.bz2) (~165 MB)
- Linux x64: [cef_binary_154.0.33+ga03e714+chromium-154.0.8037.94_linux64_minimal.tar.bz2](https://cef-builds.spotifycdn.com/cef_binary_154.0.33%2Bga03e714%2Bchromium-154.0.8037.94_linux64_minimal.tar.bz2) (~311 MB)

The CEF version is defined by `CefBuildVersion` in `Directory.Build.props`. The CefGlue bindings are generated for that exact version.

### Windows

Download the matching Windows x64 minimal CEF distribution:

```powershell
powershell -ExecutionPolicy Bypass -File .\get-cef.ps1

dotnet build CefGlue.OSR.sln -c Release -p:Platform=x64
```

Already have the CEF archive?

```powershell
.\get-cef.ps1 -FromArchive <path-to-cef-archive>
```

The script downloads and extracts CEF into:

```text
cef\runtimes\win-x64\native\
```

Run the demos from their respective `bin\x64\Release\net10.0*` directories.

### Linux

Build the Avalonia or GTK4 demo:

```bash
dotnet build CefGlue.Demo.Avalonia/CefGlue.Demo.Avalonia.csproj -c Release -p:Platform=x64

dotnet build CefGlue.Demo.GTK/CefGlue.Demo.GTK.csproj -c Release -p:Platform=x64
```

Download the matching Linux x64 minimal CEF distribution:

```text
cef_binary_<version>_linux64_minimal.tar.bz2
```

Place the runtime files next to the demo:

```text
runtimes/linux-x64/native/
```

Include:

- Files from the CEF `Release` directory.
- `Resources/*.pak`
- `Resources/icudtl.dat`
- `Resources/locales/*.pak` copied into `locales`.
- `LICENSE.txt` from the root of the CEF distribution (CEF's license).

**System requirements**

- GTK 4.14+ for the GTK4 host (`libgtk-4-1` on Ubuntu 24.04).

Tested on Ubuntu 22.04 and 24.04 (WSLg). Avalonia is also tested on Ubuntu 24.04 GNOME using Wayland through XWayland.

## Project Structure

| Project | Description |
|---|---|
| `CefGlue` | Low-level CEF bindings, generated interop and proxies |
| `CefGlue.Common` | Shared browser coordinators, handlers and OSR adapter |
| `CefGlue.Common.Shared` | Code shared by the host and the CEF subprocess: CEF app, custom schemes, renderer-process messaging |
| `CefGlue.BrowserProcess` | CEF subprocess executable |
| `CefGlue.WinForms` | WinForms browser control |
| `CefGlue.WPF` | WPF browser control |
| `CefGlue.WinUI` | WinUI 3 browser control |
| `CefGlue.Avalonia` | Avalonia browser control |
| `CefGlue.GTK` | GTK4 browser control |
| `CefGlue.Demo.*` | Framework-specific demo applications |
| `CefGlue.Ime.Harness` | Console test for the TSF text store |
| `CefGlue.Tests` | Automated tests |

## License

MIT License. See `LICENSE` (Xilium CefGlue authors) and individual project license files.

CEF/Chromium is distributed under its BSD license: `LICENSE.txt` at the root of the CEF distribution. On Windows, `get-cef.ps1` copies it to `cef/LICENSE-cef.txt` (this folder is not in git). Ship it with your application together with the CEF runtime.