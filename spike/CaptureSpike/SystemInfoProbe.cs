using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace CaptureSpike;

/// <summary>One-shot system information for the `info` command and every report header.</summary>
internal static class SystemInfoProbe
{
    public static List<KeyValuePair<string, string>> Collect()
    {
        var facts = new List<KeyValuePair<string, string>>();
        void Add(string k, Func<string> f)
        {
            try { facts.Add(new(k, f())); }
            catch (Exception ex) { facts.Add(new(k, $"error: {ex.Message}")); }
        }

        Add("os_description", () => RuntimeInformation.OSDescription);
        Add("os_display_version", () => $"{RegGet("DisplayVersion") ?? "?"} (build {RegGet("CurrentBuild") ?? "?"}.{RegGet("UBR") ?? "0"})");
        Add("cpu", () => $"{RegGetCpu() ?? "?"}");
        Add("logical_cores", () => Environment.ProcessorCount.ToString());
        Add("ram_total_gb", () =>
        {
            var m = new Win32.MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<Win32.MEMORYSTATUSEX>() };
            return Win32.GlobalMemoryStatusEx(ref m)
                ? (m.ullTotalPhys / (1024.0 * 1024 * 1024)).ToString("0.0")
                : "?";
        });
        Add("display_adapters", () =>
        {
            var names = new List<string>();
            var d = new Win32.DISPLAY_DEVICE { cb = (uint)Marshal.SizeOf<Win32.DISPLAY_DEVICE>() };
            for (uint i = 0; Win32.EnumDisplayDevicesW(null, i, ref d, 0); i++)
            {
                if (!string.IsNullOrWhiteSpace(d.DeviceString))
                    names.Add(d.DeviceString);
                d = new Win32.DISPLAY_DEVICE { cb = (uint)Marshal.SizeOf<Win32.DISPLAY_DEVICE>() };
            }
            return names.Count > 0 ? string.Join("; ", names) : "none";
        });
        Add("dotnet", () => RuntimeInformation.FrameworkDescription);

        var s = SessionProbe.Current();
        Add("session_id", () => s.SessionId.ToString());
        Add("session_is_console", () => s.IsConsoleSession ? "yes" : "no");
        Add("session_is_remote", () => s.IsRemoteSession ? "yes" : "no");
        Add("wts_connect_state", () => s.WtsConnectState);
        Add("input_desktop_accessible", () => s.InputDesktopAccessible ? "yes" : "no");

        return facts;
    }

    private static string? RegGet(string name) =>
        Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion")
            ?.GetValue(name)?.ToString();

    private static string? RegGetCpu() =>
        Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0")
            ?.GetValue("ProcessorNameString")?.ToString()?.Trim();
}
