# CefGlue.OSR

**.NET bindings for the Chromium Embedded Framework (CEF), focused on off-screen rendering (OSR)**,
with ready-to-use browser controls for **WinForms, WPF, WinUI 3, Avalonia and GTK4**.

CefGlue.OSR started from [CefGlue](https://github.com/OutSystems/CefGlue) (OutSystems), itself a
continuation of the original Xilium CefGlue. The shared host logic was reworked into coordinators
in `CefGlue.Common`, so every UI framework gets the same behaviour from a thin platform layer.

> Status: **Windows x64** (WinForms, WPF, WinUI 3, Avalonia) and **Linux x64** (Avalonia, GTK4).
> No NuGet packages yet - build from source.
>
> **No CEF binaries are included.** This repository holds only source code; download the CEF
> distribution of the matching version yourself (see [Build](#build)). Apps built on CefGlue.OSR must
> ship CEF's files next to their executable.

## Features

- **Off-screen rendering on every host** - a CPU bitmap path and a GPU texture path (WinUI, Avalonia,
  WinForms/WPF with fallback to CPU when the GPU can't share textures; zero-copy EGL dma-buf on Linux
  for Avalonia and GTK4).
- **IME (Japanese, Chinese, Korean, ...)** - real composition, commit and cancel; the candidate window
  follows the caret and the window.
  - Windows: WinForms/WPF use a TSF text store, WinUI 3 uses `CoreTextEditContext` (both with per-clause
    underlines), Avalonia uses its own text input API.
  - Linux (Avalonia): IBus and Fcitx5 through Avalonia's D-Bus input method, with the preedit drawn in the
    page. Enable it with `X11PlatformOptions { EnableIme = true }` (Avalonia turns it on by default only
    for CJK/VI system languages).
- **Popups in the library** - `window.open` popups (e.g. "Sign in with Google") are created, shown, closed
  and disposed by the library on every host, including popups that close themselves. Where a popup appears
  is decided by an `IPopupPresenter<CefGlueBrowser>`; the default is an owned window, and you can replace it
  (a tab, a dialog, ...):

  ```csharp
  builder.Services.ReplaceCoordinator<IPopupPresenter<CefGlueBrowser>, MyPopupPresenter>();
  ```

- **DevTools in OSR** - `ShowDevToolOSR()` docks DevTools into your own panel.
- **Drag and drop, fullscreen, zoom, JavaScript bindings** and the usual CEF handlers.
- **Dependency-injection friendly** - the demos (`DemoDI`) show how to plug in your own coordinators.

## Demos

Every demo has the same layout and behaviour:

- tabs, with back / forward / reload buttons left of the address box
- DevTools docked on the right in a resizable splitter, toggled with **F12**
- **Open link in new tab** in the context menu when right-clicking a link
- popups (Google sign-in and similar) in their own window

The CEF cache goes to `cef_temp` next to the demo's executable.

## Build

Requirements: **.NET 10 SDK**, x64.

The CEF version is fixed by `CefBuildVersion` in `Directory.Build.props` (CefGlue's bindings are
generated for that exact version).

### Windows

`get-cef.ps1` downloads the matching Windows 64-bit *minimal* distribution from
[cef-builds.spotifycdn.com](https://cef-builds.spotifycdn.com/index.html) into
`cef\runtimes\win-x64\native`:

```powershell
powershell -ExecutionPolicy Bypass -File .\get-cef.ps1      # once, and after a CEF upgrade
dotnet build CefGlue.OSR.sln -c Release -p:Platform=x64
```

Already have the archive? `.\get-cef.ps1 -FromArchive <path to cef_binary_..._windows64_minimal.tar.bz2>`.

Then run one of the demos from its `bin\x64\Release\net10.0*` folder.

### Linux

Build the Avalonia or GTK4 demo on Linux:

```bash
dotnet build CefGlue.Demo.Avalonia/CefGlue.Demo.Avalonia.csproj -c Release -p:Platform=x64
dotnet build CefGlue.Demo.GTK/CefGlue.Demo.GTK.csproj -c Release -p:Platform=x64
```

Then download the CEF **Linux 64-bit minimal** distribution of the same version
(`cef_binary_<version>_linux64_minimal.tar.bz2`, same site) and put it next to the demo, in
`runtimes/linux-x64/native`: the files from its `Release` folder, plus `Resources/*.pak`,
`Resources/icudtl.dat` and `Resources/locales/*.pak` (into `locales`).

- GTK4 host: GTK **4.14+** (Ubuntu 24.04: `libgtk-4-1`).
- IME: an input method framework (IBus or Fcitx5) with an engine such as Mozc.
- Tested on Ubuntu 22.04/24.04 (WSLg); Avalonia also on Ubuntu 24.04 GNOME (Wayland, via XWayland).

## Projects

| Folder | What it is |
|---|---|
| `CefGlue` | Low-level CEF bindings (generated interop + proxies) |
| `CefGlue.Common`, `CefGlue.Common.Shared` | Framework-independent browser logic: coordinators, handlers, OSR adapter |
| `CefGlue.BrowserProcess` | CEF sub-process executable |
| `CefGlue.WinForms`, `CefGlue.WPF`, `CefGlue.WinUI`, `CefGlue.Avalonia`, `CefGlue.GTK` | Browser controls per UI framework |
| `CefGlue.Demo.*` | Demo apps, one per framework |
| `CefGlue.Ime.Harness` | Console test for the TSF text store |
| `CefGlue.Tests` | Tests |

## License

MIT - see `LICENSE` (Xilium CefGlue authors) and the `LICENSE` files in the project folders.
CEF/Chromium: BSD (`cef\LICENSE-cef.txt` after running `get-cef.ps1`).
