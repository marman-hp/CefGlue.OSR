using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;

namespace Xilium.CefGlue.Common.Helpers
{
    public static class OverlayDiagnostics
    {
        public static string CefVersion { get; } = CefRuntime.ChromeVersion;

        private const int MaxDisplayNameLength = 15;

        public static string ProcessorName { get; } = Truncate(GetProcessorName());

        public static string GpuName { get; } = Truncate(GetGpuName());

        private static string Truncate(string value)
        {
            if (string.IsNullOrEmpty(value) || value.Length <= MaxDisplayNameLength)
            {
                return value;
            }

            return value[..MaxDisplayNameLength] + "..";
        }

        public static double CpuUsagePercent { get; private set; }

        public static double GpuUsagePercent { get; private set; }

        public static double MemoryUsageMb { get; private set; }

        private static readonly object _samplingInitLock = new object();
        private static Timer _samplingTimer;

        public static void EnsureBackgroundSampling()
        {
            if (_samplingTimer != null)
            {
                return;
            }

            lock (_samplingInitLock)
            {
                if (_samplingTimer != null)
                {
                    return;
                }

                MemoryUsageMb = GetMemoryUsageMb();

                _samplingTimer = new Timer(_ =>
                {
                    try
                    {
                        CpuUsagePercent = GetCpuUsagePercent();
                        GpuUsagePercent = GetGpuUsagePercent();
                        MemoryUsageMb = GetMemoryUsageMb();
                    }
                    catch
                    {
                    }
                }, null, TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(1));
            }
        }

        internal static void StopBackgroundSampling()
        {
            lock (_samplingInitLock)
            {
                _samplingTimer?.Dispose();
                _samplingTimer = null;
            }
        }

        private static readonly object _cpuLock = new object();
        private static TimeSpan _lastCpuTime = Process.GetCurrentProcess().TotalProcessorTime;
        private static long _lastCpuTimestampMs = Environment.TickCount64;

        private static double GetCpuUsagePercent()
        {
            lock (_cpuLock)
            {
                var now = Environment.TickCount64;
                var cpuTime = Process.GetCurrentProcess().TotalProcessorTime;

                var elapsedMs = now - _lastCpuTimestampMs;
                if (elapsedMs <= 0)
                {
                    return 0;
                }

                var usage = (cpuTime - _lastCpuTime).TotalMilliseconds / (elapsedMs * Environment.ProcessorCount) * 100.0;

                _lastCpuTime = cpuTime;
                _lastCpuTimestampMs = now;

                return Math.Clamp(usage, 0, 100);
            }
        }

        private const string BrowserSubProcessName = "Xilium.CefGlue.BrowserProcess";

        private static double GetMemoryUsageMb()
        {
            long totalBytes;
            using (var currentProcess = Process.GetCurrentProcess())
            {
                totalBytes = GetPrivateWorkingSetBytes(currentProcess);
            }

            try
            {
                foreach (var process in Process.GetProcessesByName(BrowserSubProcessName))
                {
                    using (process)
                    {
                        try
                        {
                            totalBytes += GetPrivateWorkingSetBytes(process);
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }

            return totalBytes / 1024.0 / 1024.0;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct ProcessMemoryCountersEx2
        {
            public uint Cb;
            public uint PageFaultCount;
            public UIntPtr PeakWorkingSetSize;
            public UIntPtr WorkingSetSize;
            public UIntPtr QuotaPeakPagedPoolUsage;
            public UIntPtr QuotaPagedPoolUsage;
            public UIntPtr QuotaPeakNonPagedPoolUsage;
            public UIntPtr QuotaNonPagedPoolUsage;
            public UIntPtr PagefileUsage;
            public UIntPtr PeakPagefileUsage;
            public UIntPtr PrivateUsage;
            public UIntPtr PrivateWorkingSetSize;
            public ulong SharedCommitUsage;
        }

        [DllImport("psapi.dll", SetLastError = true)]
        private static extern bool GetProcessMemoryInfo(IntPtr hProcess, out ProcessMemoryCountersEx2 counters, uint cb);

        private static long GetPrivateWorkingSetBytes(Process process)
        {
            if (OperatingSystem.IsWindows())
            {
                try
                {
                    var counters = new ProcessMemoryCountersEx2();
                    var size = (uint)Marshal.SizeOf<ProcessMemoryCountersEx2>();
                    if (GetProcessMemoryInfo(process.Handle, out counters, size))
                    {
                        if (counters.PrivateWorkingSetSize != UIntPtr.Zero)
                        {
                            return (long)counters.PrivateWorkingSetSize;
                        }

                        if (counters.PrivateUsage != UIntPtr.Zero)
                        {
                            return (long)counters.PrivateUsage;
                        }
                    }
                }
                catch
                {
                }
            }
            else if (OperatingSystem.IsLinux())
            {
                var pss = GetLinuxPssBytes(process.Id);
                if (pss.HasValue)
                {
                    return pss.Value;
                }

                var rss = GetLinuxVmRssBytes(process.Id);
                if (rss.HasValue)
                {
                    return rss.Value;
                }
            }

            try
            {
                return process.PrivateMemorySize64;
            }
            catch
            {
                return 0;
            }
        }

        private static long? GetLinuxPssBytes(int pid)
        {
            try
            {
                foreach (var line in File.ReadLines($"/proc/{pid}/smaps_rollup"))
                {
                    if (!line.StartsWith("Pss:", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && long.TryParse(parts[1], out var kb))
                    {
                        return kb * 1024;
                    }

                    break;
                }
            }
            catch
            {
            }

            return null;
        }

        private static long? GetLinuxVmRssBytes(int pid)
        {
            try
            {
                foreach (var line in File.ReadLines($"/proc/{pid}/status"))
                {
                    if (!line.StartsWith("VmRSS:", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var parts = line.Split((char[])null, StringSplitOptions.RemoveEmptyEntries);
                    if (parts.Length >= 2 && long.TryParse(parts[1], out var kb))
                    {
                        return kb * 1024;
                    }

                    break;
                }
            }
            catch
            {
            }

            return null;
        }

        private static readonly object _gpuUsageLock = new object();
        private static readonly Dictionary<string, PerformanceCounter> _gpuEngineCounters = new Dictionary<string, PerformanceCounter>();
        private static bool? _gpuEngineCategoryAvailable;

        private static double GetGpuUsagePercent()
        {
            if (!OperatingSystem.IsWindows())
            {
                return 0;
            }

            lock (_gpuUsageLock)
            {
                try
                {
                    _gpuEngineCategoryAvailable ??= PerformanceCounterCategory.Exists("GPU Engine");

                    if (_gpuEngineCategoryAvailable != true)
                    {
                        return 0;
                    }

                    var currentInstances = new PerformanceCounterCategory("GPU Engine")
                        .GetInstanceNames()
                        .Where(name => name.Contains("engtype_3D", StringComparison.OrdinalIgnoreCase))
                        .ToHashSet();

                    foreach (var staleName in _gpuEngineCounters.Keys.Except(currentInstances).ToArray())
                    {
                        _gpuEngineCounters[staleName].Dispose();
                        _gpuEngineCounters.Remove(staleName);
                    }

                    foreach (var name in currentInstances)
                    {
                        if (!_gpuEngineCounters.ContainsKey(name))
                        {
                            _gpuEngineCounters[name] = new PerformanceCounter(
                                "GPU Engine", "Utilization Percentage", name, readOnly: true);
                        }
                    }

                    double total = 0;

                    foreach (var kv in _gpuEngineCounters)
                    {
                        if (!PerformanceCounterCategory.InstanceExists(kv.Key, "GPU Engine"))
                        {
                            continue;
                        }

                        try
                        {
                            total += kv.Value.NextValue();
                        }
                        catch (InvalidOperationException)
                        {
                        }
                    }

                    return Math.Clamp(total, 0, 100);
                }
                catch
                {
                    return 0;
                }
            }
        }

        private static string GetProcessorName()
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    using var key = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                        @"HARDWARE\DESCRIPTION\System\CentralProcessor\0");

                    return (key?.GetValue("ProcessorNameString") as string)?.Trim() ?? "Unknown";
                }

                if (OperatingSystem.IsLinux())
                {
                    foreach (var line in File.ReadLines("/proc/cpuinfo"))
                    {
                        if (!line.StartsWith("model name", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        var colonIndex = line.IndexOf(':');
                        if (colonIndex >= 0 && colonIndex + 1 < line.Length)
                        {
                            return line[(colonIndex + 1)..].Trim();
                        }

                        break;
                    }
                }
            }
            catch
            {
            }

            return "Unknown";
        }

        private static string GetGpuName()
        {
            try
            {
                if (OperatingSystem.IsWindows())
                {
                    using var classKey = Microsoft.Win32.Registry.LocalMachine.OpenSubKey(
                        @"SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}");

                    if (classKey == null)
                    {
                        return "Unknown";
                    }

                    foreach (var subKeyName in classKey.GetSubKeyNames())
                    {
                        if (!char.IsDigit(subKeyName.Length > 0 ? subKeyName[0] : ' '))
                        {
                            continue;
                        }

                        using var adapterKey = classKey.OpenSubKey(subKeyName);
                        var description = adapterKey?.GetValue("DriverDesc") as string;

                        if (!string.IsNullOrEmpty(description))
                        {
                            return ClassifyGpuVendor(description);
                        }
                    }

                    return "Unknown";
                }

                if (OperatingSystem.IsLinux())
                {
                    return GetLinuxGpuName();
                }
            }
            catch
            {
            }

            return "Unknown";
        }

        private static string GetLinuxGpuName()
        {
            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "lspci",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                };

                using var process = Process.Start(startInfo);
                if (process == null)
                {
                    return "Unknown";
                }

                string line;
                while ((line = process.StandardOutput.ReadLine()) != null)
                {
                    var marker = line.Contains("VGA compatible controller:") ? "VGA compatible controller:"
                        : line.Contains("3D controller:") ? "3D controller:"
                        : null;

                    if (marker == null)
                    {
                        continue;
                    }

                    var name = line[(line.IndexOf(marker, StringComparison.Ordinal) + marker.Length)..].Trim();

                    var revIndex = name.LastIndexOf(" (rev ", StringComparison.Ordinal);
                    if (revIndex >= 0)
                    {
                        name = name[..revIndex];
                    }

                    process.StandardOutput.ReadToEnd();
                    process.WaitForExit(1000);
                    return ClassifyGpuVendor(name);
                }

                process.WaitForExit(1000);
            }
            catch
            {
            }

            return "Unknown";
        }

        private static string ClassifyGpuVendor(string description)
        {
            if (description.Contains("Basic Render", StringComparison.OrdinalIgnoreCase) ||
                description.Contains("WARP", StringComparison.OrdinalIgnoreCase))
            {
                return "Software Rendering";
            }

            return description;
        }
    }
}
