using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Hdr2Sdr.Core;

public sealed class MonitorEntry
{
    public string DeviceName { get; init; } = string.Empty;
    public string FriendlyName { get; init; } = string.Empty;
    public int Width { get; init; }
    public int Height { get; init; }
    public bool Primary { get; init; }
    public string Label { get; init; } = string.Empty;

    public override string ToString() => Label;
}

[SupportedOSPlatform("windows")]
public static class MonitorCatalog
{
    private static readonly object Sync = new();
    public static string SelectedDevice { get; set; } = string.Empty;

    public static List<MonitorEntry> List()
    {
        var list = new List<MonitorEntry>();
        lock (Sync)
        {
            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMonitor, _, _, _) =>
            {
                var mi = new NativeMethods.MONITORINFOEX { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
                if (!NativeMethods.GetMonitorInfo(hMonitor, ref mi))
                    return true;

                var device = mi.szDevice ?? string.Empty;
                var width = Math.Abs(mi.rcMonitor.right - mi.rcMonitor.left);
                var height = Math.Abs(mi.rcMonitor.bottom - mi.rcMonitor.top);
                var primary = (mi.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0;
                var friendly = FriendlyName(device);

                var entry = new MonitorEntry
                {
                    DeviceName = device,
                    FriendlyName = friendly,
                    Width = width,
                    Height = height,
                    Primary = primary,
                    Label = BuildLabel(device, friendly, width, height, primary)
                };
                list.Add(entry);
                return true;
            }, IntPtr.Zero);
        }

        list.Sort((a, b) =>
        {
            if (a.Primary != b.Primary)
                return a.Primary ? -1 : 1;
            return string.Compare(a.DeviceName, b.DeviceName, StringComparison.OrdinalIgnoreCase);
        });

        return list;
    }

    public static MonitorEntry? Resolve()
    {
        var list = List();
        if (list.Count == 0)
            return null;

        if (!string.IsNullOrWhiteSpace(SelectedDevice))
        {
            var match = list.FirstOrDefault(m => string.Equals(m.DeviceName, SelectedDevice, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match;
        }

        return list.FirstOrDefault(m => m.Primary) ?? list[0];
    }

    public static bool TryGetHandle(out IntPtr handle)
    {
        IntPtr found = IntPtr.Zero;
        IntPtr primaryFallback = IntPtr.Zero;
        var wanted = Resolve()?.DeviceName ?? SelectedDevice;

        lock (Sync)
        {
            NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMonitor, _, _, _) =>
            {
                var mi = new NativeMethods.MONITORINFOEX { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
                if (!NativeMethods.GetMonitorInfo(hMonitor, ref mi))
                    return true;

                if ((mi.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) != 0 && primaryFallback == IntPtr.Zero)
                    primaryFallback = hMonitor;

                if (!string.IsNullOrEmpty(wanted) && string.Equals(mi.szDevice, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    found = hMonitor;
                    return false;
                }

                return true;
            }, IntPtr.Zero);
        }

        handle = found != IntPtr.Zero ? found : primaryFallback;
        return handle != IntPtr.Zero;
    }

    private static string FriendlyName(string device)
    {
        try
        {
            var dd = new NativeMethods.DISPLAY_DEVICE { cb = Marshal.SizeOf<NativeMethods.DISPLAY_DEVICE>() };
            if (NativeMethods.EnumDisplayDevices(device, 0, ref dd, 0))
            {
                var name = (dd.DeviceString ?? string.Empty).Trim();
                if (!string.IsNullOrEmpty(name)
                    && !name.Contains("Generic", StringComparison.OrdinalIgnoreCase)
                    && !name.Contains("PnP", StringComparison.OrdinalIgnoreCase))
                {
                    return name;
                }
            }
        }
        catch
        {
            // Fall back to short device name
        }

        return string.Empty;
    }

    private static string BuildLabel(string device, string friendly, int width, int height, bool primary)
    {
        var shortDev = device.StartsWith(@"\\.\", StringComparison.Ordinal) ? device[4..] : device;
        var head = string.IsNullOrEmpty(friendly) ? shortDev : $"{friendly} ({shortDev})";
        var res = width > 0 && height > 0 ? $" · {width}×{height}" : string.Empty;
        var tag = primary ? " · principal" : string.Empty;
        return $"{head}{res}{tag}";
    }
}
