# CefGlue.OSR

**.NET bindings for the Chromium Embedded Framework (CEF), focused on off-screen rendering (OSR)**,
with ready-to-use browser controls for **WinForms, WPF, WinUI 3 and Avalonia**.

CefGlue.OSR started from [CefGlue](https://github.com/OutSystems/CefGlue) (OutSystems), itself a
continuation of the original Xilium CefGlue. The shared host logic was reworked into coordinators
in `CefGlue.Common`, so every UI framework gets the same behaviour from a thin platform layer.

> Status: **Windows x64**. Linux support comes later. No NuGet packages yet - build from source.

## Features

- **Off-screen rendering on all four hosts** - WinForms, WPF, WinUI 3, Avalonia - with a CPU bitmap
  path and a GPU texture path (WinUI, Avalonia).
- **IME (Japanese, Chinese, Korean, ...)** - real composition, commit and cancel, and the candidate
  window follows the caret and the window. WinForms/WPF use a TSF text store and WinUI 3 uses
  `CoreTextEditContext` (both with per-clause underlines); Avalonia uses its own text input API.
- **DevTools in OSR** - `ShowDevToolOSR()` docks DevTools into your own panel.
- **Popups, fullscreen, zoom, JavaScript bindings** and the usual CEF handlers.
- **Dependency-injection friendly** - the demos (`DemoDI`) show how to plug in your own coordinators.

## Build

Requirements: **Windows x64**, **.NET 10 SDK**.

The CEF version is fixed by `CefBuildVersion` in `Directory.Build.props` (CefGlue's bindings are
generated for that exact version). `get-cef.ps1` downloads the matching Windows 64-bit *minimal*
distribution from [cef-builds.spotifycdn.com](https://cef-builds.spotifycdn.com/index.html) into
`cef\runtimes\win-x64\native`:

```powershell
powershell -ExecutionPolicy Bypass -File .\get-cef.ps1      # once, and after a CEF upgrade
dotnet build CefGlue.OSR.sln -c Release -p:Platform=x64
```

Already have the archive? `.\get-cef.ps1 -FromArchive <path to cef_binary_..._windows64_minimal.tar.bz2>`.

Then run one of the demos from its `bin\x64\Release\net10.0*` folder.

## Projects

| Folder | What it is |
|---|---|
| `CefGlue` | Low-level CEF bindings (generated interop + proxies) |
| `CefGlue.Common`, `CefGlue.Common.Shared` | Framework-independent browser logic: coordinators, handlers, OSR adapter |
| `CefGlue.BrowserProcess` | CEF sub-process executable |
| `CefGlue.WinForms`, `CefGlue.WPF`, `CefGlue.WinUI`, `CefGlue.Avalonia` | Browser controls per UI framework |
| `CefGlue.Demo.*` | Demo apps, one per framework |
| `CefGlue.Ime.Harness` | Console test for the TSF text store |

## License

MIT - see `LICENSE` (Xilium CefGlue authors) and the `LICENSE` files in the project folders.
CEF/Chromium: BSD (`cef\LICENSE-cef.txt` after running `get-cef.ps1`).
