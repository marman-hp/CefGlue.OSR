#!/usr/bin/env bash
# Run this INSIDE WSL, from the CefGlue.OSR repo root, after building CefGlue.Demo.GTK once
# (so its output directory exists). Copies the CEF native runtime + builds/copies CefGlue's own
# BrowserProcess subprocess into the demo's output directory - see CefGlue.Common.targets'
# linux-x64 branch for why this is manual: cef.redist.linux64 isn't an official NuGet package (see
# project memory), so the automatic MSBuild copy mechanism the Windows demos get for free doesn't
# apply here yet.
#
# Layout mirrors the Windows demos' own convention (see CefGlue.CopyLocal.props/
# CefRuntime.Load()'s runtimes/win-x64/native path, and CefGlueBrowserProcess/ for the subprocess)
# instead of dumping everything flat into the output root.
set -euo pipefail

CEF_BINARY_DIR="${1:?Usage: setup-cef-runtime.sh <path-to-extracted-cef_binary_*_linux64-dir> [output-dir]}"
OUTPUT_DIR="${2:-CefGlue.Demo.GTK/bin/x64/Debug/net10.0}"
RUNTIME_DIR="$OUTPUT_DIR/runtimes/linux-x64/native"
BROWSER_PROCESS_DIR="$OUTPUT_DIR/CefGlueBrowserProcess"

if [ ! -d "$CEF_BINARY_DIR/Release" ] || [ ! -d "$CEF_BINARY_DIR/Resources" ]; then
    echo "Error: $CEF_BINARY_DIR doesn't look like an extracted cef_binary_*_linux64 distribution (expected Release/ and Resources/ subdirs)." >&2
    exit 1
fi

echo "Publishing CefGlue.BrowserProcess for linux-x64..."
dotnet publish CefGlue.BrowserProcess/CefGlue.BrowserProcess.csproj -r linux-x64 --self-contained -c Debug -o /tmp/cefglue-browserprocess-publish

mkdir -p "$RUNTIME_DIR" "$BROWSER_PROCESS_DIR"

echo "Copying CEF native runtime (libcef.so, icu, snapshots) into runtimes/linux-x64/native..."
cp "$CEF_BINARY_DIR"/Release/*.so* "$RUNTIME_DIR"/ 2>/dev/null || true
cp "$CEF_BINARY_DIR"/Release/*.bin "$RUNTIME_DIR"/ 2>/dev/null || true
cp "$CEF_BINARY_DIR"/Release/chrome-sandbox "$RUNTIME_DIR"/ 2>/dev/null || true

echo "Copying resources (pak files, icudtl.dat, locales) into runtimes/linux-x64/native..."
cp "$CEF_BINARY_DIR"/Resources/*.pak "$RUNTIME_DIR"/ 2>/dev/null || true
cp "$CEF_BINARY_DIR"/Resources/icudtl.dat "$RUNTIME_DIR"/ 2>/dev/null || true
mkdir -p "$RUNTIME_DIR/locales"
cp "$CEF_BINARY_DIR"/Resources/locales/*.pak "$RUNTIME_DIR/locales"/ 2>/dev/null || true

echo "Copying CefGlue.BrowserProcess publish output into CefGlueBrowserProcess/..."
cp -r /tmp/cefglue-browserprocess-publish/* "$BROWSER_PROCESS_DIR"/

echo "Setting SUID on chrome-sandbox (needed unless CefSettings.NoSandbox stays true - it does here, but harmless either way)..."
sudo chown root:root "$RUNTIME_DIR/chrome-sandbox" 2>/dev/null || true
sudo chmod 4755 "$RUNTIME_DIR/chrome-sandbox" 2>/dev/null || true

echo "Done. Run: (cd $OUTPUT_DIR && ./Xilium.CefGlue.Demo.GTK)"
