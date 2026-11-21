using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Hdr2Sdr.Core;

[SupportedOSPlatform("windows")]
public static class MonitorBrightness
{
    private static readonly object Sync = new();
    private static NativeMethods.PHYSICAL_MONITOR[]? _monitors;
    private static uint _count;
    private static uint _min;
    private static uint _max = 100;
    private static int _cachedPercent = -1;
    private static bool _rangeReady;
    private static int _hold;

    public static void Invalidate()
    {
        lock (Sync)
        {
            ClosePhysical_NoLock();
            _rangeReady = false;
        }
    }

    public static bool TryGetCachedPercent(out int percent)
    {
        lock (Sync)
        {
            if (_cachedPercent >= 0)
            {
                percent = _cachedPercent;
                return true;
            }
        }

        percent = 0;
        return false;
    }

    public static void BeginHold()
    {
        lock (Sync)
        {
            _hold++;
        }
    }

    public static void EndHold()
    {
        lock (Sync)
        {
            if (_hold > 0)
                _hold--;
            CloseUnlessHeld_NoLock();
        }
    }

    public static bool TryGetPercent(out int percent)
    {
        lock (Sync)
        {
            if (_cachedPercent >= 0)
            {
                percent = _cachedPercent;
                return true;
            }
            return TryGetPercentFresh_NoLock(out percent);
        }
    }

    public static bool TryGetPercentFresh(out int percent)
    {
        lock (Sync)
        {
            return TryGetPercentFresh_NoLock(out percent);
        }
    }

    public static bool TryReadVcpPercent(out int percent)
    {
        lock (Sync)
        {
            ClosePhysical_NoLock();
            return TryGetPercentFresh_NoLock(out percent);
        }
    }

    public static bool TrySetPercent(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        lock (Sync)
        {
            if (!EnsureRangeCached_NoLock())
                return false;

            uint raw = _min + (uint)Math.Round((_max - _min) * (percent / 100.0));
            bool ok = WriteWithRetry_NoLock(raw, percent);
            CloseUnlessHeld_NoLock();
            return ok;
        }
    }

    public static bool ForceSetPercent(int percent)
    {
        percent = Math.Clamp(percent, 0, 100);
        lock (Sync)
        {
            ClosePhysical_NoLock();
            _rangeReady = false;

            if (!EnsureRangeCached_NoLock())
                return false;

            uint raw = _min + (uint)Math.Round((_max - _min) * (percent / 100.0));
            bool ok = WriteWithRetry_NoLock(raw, percent);
            CloseUnlessHeld_NoLock();
            return ok;
        }
    }

    private static bool EnsureRangeCached_NoLock()
    {
        if (!EnsureOpen_NoLock())
            return false;

        if (_rangeReady)
            return true;

        var h = _monitors![0].hPhysicalMonitor;
        if (NativeMethods.GetMonitorBrightness(h, out var min, out var cur, out var max) && max > min)
        {
            _min = min;
            _max = max;
            _rangeReady = true;
            _cachedPercent = ToPercent(cur);
            return true;
        }

        if (NativeMethods.GetVCPFeatureAndVCPFeatureReply(h, NativeMethods.VCP_LUMINANCE, out _, out var vcpCur, out var vcpMax) && vcpMax > 0)
        {
            _min = 0;
            _max = vcpMax;
            _rangeReady = true;
            _cachedPercent = ToPercent(vcpCur);
            return true;
        }

        _min = 0;
        _max = 100;
        _rangeReady = true;
        return true;
    }

    private static bool TryGetPercentFresh_NoLock(out int percent)
    {
        percent = _cachedPercent >= 0 ? _cachedPercent : 50;

        if (!EnsureOpen_NoLock())
            return false;

        var h = _monitors![0].hPhysicalMonitor;

        if (NativeMethods.GetVCPFeatureAndVCPFeatureReply(h, NativeMethods.VCP_LUMINANCE, out _, out var vcpCur, out var vcpMax) && vcpMax > 0)
        {
            _min = 0;
            _max = vcpMax;
            _rangeReady = true;
            percent = ToPercent(vcpCur);
            _cachedPercent = percent;
            CloseUnlessHeld_NoLock();
            return true;
        }

        if (NativeMethods.GetMonitorBrightness(h, out var min, out var cur, out var max) && max > min)
        {
            _min = min;
            _max = max;
            _rangeReady = true;
            percent = ToPercent(cur);
            _cachedPercent = percent;
            CloseUnlessHeld_NoLock();
            return true;
        }

        CloseUnlessHeld_NoLock();
        return false;
    }

    private static bool WriteWithRetry_NoLock(uint rawValue, int percent)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            if (!EnsureOpen_NoLock())
                return false;

            if (NativeMethods.SetMonitorBrightness(_monitors![0].hPhysicalMonitor, rawValue))
            {
                _cachedPercent = percent;
                return true;
            }

            ClosePhysical_NoLock();
            Thread.Sleep(40);
        }

        return false;
    }

    private static int ToPercent(uint cur)
    {
        if (_max <= _min)
            return Math.Clamp((int)cur, 0, 100);
        double ratio = (cur - (double)_min) / (_max - (double)_min);
        return Math.Clamp((int)Math.Round(ratio * 100.0), 0, 100);
    }

    private static bool EnsureOpen_NoLock()
    {
        if (_monitors is not null && _count > 0 && _monitors[0].hPhysicalMonitor != IntPtr.Zero)
            return true;

        ClosePhysical_NoLock();

        var resolved = MonitorCatalog.Resolve();
        var wantedDevice = resolved?.DeviceName ?? MonitorCatalog.SelectedDevice;

        if (TryOpenForDevice_NoLock(wantedDevice))
            return true;

        if (!string.IsNullOrEmpty(wantedDevice) && TryOpenForDevice_NoLock(string.Empty))
            return true;

        return false;
    }

    private static bool TryOpenForDevice_NoLock(string wantedDevice)
    {
        NativeMethods.PHYSICAL_MONITOR[]? opened = null;
        uint openedCount = 0;

        NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (hMonitor, _, _, _) =>
        {
            var mi = new NativeMethods.MONITORINFOEX { cbSize = Marshal.SizeOf<NativeMethods.MONITORINFOEX>() };
            if (!NativeMethods.GetMonitorInfo(hMonitor, ref mi))
                return true;

            if (!string.IsNullOrEmpty(wantedDevice))
            {
                if (!string.Equals(mi.szDevice, wantedDevice, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            else
            {
                if ((mi.dwFlags & NativeMethods.MONITORINFOF_PRIMARY) == 0)
                    return true;
            }

            if (!NativeMethods.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, out var count) || count == 0)
                return false;

            var arr = new NativeMethods.PHYSICAL_MONITOR[count];
            if (NativeMethods.GetPhysicalMonitorsFromHMONITOR(hMonitor, count, arr))
            {
                opened = arr;
                openedCount = count;
            }

            return false;
        }, IntPtr.Zero);

        if (opened is not null && openedCount > 0)
        {
            _monitors = opened;
            _count = openedCount;
            return true;
        }

        return false;
    }

    private static void CloseUnlessHeld_NoLock()
    {
        if (_hold <= 0)
            ClosePhysical_NoLock();
    }

    private static void ClosePhysical_NoLock()
    {
        if (_monitors is not null && _count > 0)
        {
            try
            {
                NativeMethods.DestroyPhysicalMonitors(_count, _monitors);
            }
            catch
            {
                // Ignore cleanup failure
            }
        }
        _monitors = null;
        _count = 0;
    }
}
