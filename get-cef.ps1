<#
.SYNOPSIS
  Puts the CEF runtime into cef\runtimes\win-x64\native, where the demo builds pick it up.

.DESCRIPTION
  Downloads the official CEF "minimal" Windows x64 distribution matching CefBuildVersion in
  Directory.Build.props from https://cef-builds.spotifycdn.com and copies the runtime files
  (Release\*.dll, *.bin, *.json and Resources\*) into place. Run it once after cloning, and again
  whenever CefBuildVersion changes.

.PARAMETER FromDirectory
  Copy from an existing CEF runtime folder (one that already holds libcef.dll, the .pak files and
  locales\) instead of downloading. CEF's own LICENSE.txt (needed for THIRD-PARTY-NOTICES.txt) is
  looked for in that folder and up to 4 folders above it, as in an extracted cef_binary_* tree.

.PARAMETER LicenseFile
  CEF's LICENSE.txt, when the -FromDirectory folder tree doesn't hold it.

.PARAMETER FromArchive
  Use a cef_binary_<version>_windows64_minimal.tar.bz2 you downloaded yourself (e.g. from
  https://cef-builds.spotifycdn.com/index.html) instead of downloading it. It must be the exact
  version in Directory.Build.props (CefBuildVersion).

.EXAMPLE
  .\get-cef.ps1
.EXAMPLE
  .\get-cef.ps1 -FromDirectory C:\cef\runtimes\win-x64\native
.EXAMPLE
  .\get-cef.ps1 -FromDirectory C:\cef\runtimes\win-x64\native -LicenseFile C:\cef_binary\LICENSE.txt
.EXAMPLE
  .\get-cef.ps1 -FromArchive C:\Downloads\cef_binary_<version>_windows64_minimal.tar.bz2
#>
param(
    [string]$FromDirectory,
    [string]$LicenseFile,
    [string]$FromArchive
)

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$destination = Join-Path $root "cef\runtimes\win-x64\native"

if ($FromDirectory) {
    if (-not (Test-Path (Join-Path $FromDirectory "libcef.dll"))) {
        throw "No libcef.dll in $FromDirectory - point -FromDirectory at a CEF runtime folder."
    }
    New-Item -ItemType Directory -Force $destination | Out-Null
    Copy-Item (Join-Path $FromDirectory "*") $destination -Recurse -Force
    Write-Host "CEF runtime copied to $destination"

    if (-not $LicenseFile) {
        $dir = Get-Item $FromDirectory
        for ($i = 0; $i -le 4 -and $dir; $i++) {
            $candidate = Join-Path $dir.FullName "LICENSE.txt"
            if ((Test-Path $candidate) -and (Select-String -Path $candidate -Pattern "Chromium Embedded" -Quiet)) {
                $LicenseFile = $candidate
                break
            }
            $dir = $dir.Parent
        }
    }

    if ($LicenseFile) {
        Copy-Item $LicenseFile (Join-Path $root "cef\LICENSE-cef.txt") -Force
        Write-Host "CEF license copied from $LicenseFile"
    } else {
        Write-Warning "CEF's LICENSE.txt not found near $FromDirectory - pass -LicenseFile (CEF's license should travel with the runtime)."
    }
    return
}

$props = [xml](Get-Content (Join-Path $root "Directory.Build.props") -Raw)
$version = ($props.Project.PropertyGroup | Where-Object { $_.CefBuildVersion } | Select-Object -First 1).CefBuildVersion
if (-not $version) { throw "CefBuildVersion not found in Directory.Build.props." }

$name = "cef_binary_${version}_windows64_minimal"
$url = "https://cef-builds.spotifycdn.com/" + [uri]::EscapeDataString("$name.tar.bz2")
$work = Join-Path ([IO.Path]::GetTempPath()) "cefglue-osr-cef"
$archive = Join-Path $work "$name.tar.bz2"

New-Item -ItemType Directory -Force $work | Out-Null
if ($FromArchive) {
    if (-not (Test-Path $FromArchive)) { throw "Archive not found: $FromArchive" }
    # CefGlue's bindings are generated for one exact CEF version - a different one fails at runtime.
    if ((Split-Path $FromArchive -Leaf) -ne "$name.tar.bz2") {
        throw "This build needs $name.tar.bz2 (CefBuildVersion in Directory.Build.props), not $(Split-Path $FromArchive -Leaf)."
    }
    $archive = (Resolve-Path $FromArchive).Path
} elseif (-not (Test-Path $archive)) {
    Write-Host "Downloading $url (about 150 MB)..."
    Invoke-WebRequest -Uri $url -OutFile $archive -UseBasicParsing
}

Write-Host "Extracting..."
tar -xjf $archive -C $work
$extracted = Join-Path $work $name

New-Item -ItemType Directory -Force $destination | Out-Null
Get-ChildItem (Join-Path $extracted "Release") -File |
    Where-Object { $_.Extension -in ".dll", ".bin", ".json" } |
    Copy-Item -Destination $destination -Force
Copy-Item (Join-Path $extracted "Resources\*") $destination -Recurse -Force
Copy-Item (Join-Path $extracted "LICENSE.txt") (Join-Path $root "cef\LICENSE-cef.txt") -Force

Write-Host "CEF $version runtime is in $destination"
