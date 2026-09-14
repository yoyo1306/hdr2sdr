using System;
using System.Runtime.InteropServices;

namespace Hdr2Sdr
{
    /// <summary>
    /// Hardware monitor brightness via DDC/CI (dxva2) — used when HDR is OFF (true SDR).
    /// Keeps the physical monitor handle open and caches min/max to avoid slow open/get/close on each step.
    /// </summary>
    internal static class MonitorBrightness
    {
        private static readonly object Sync = new object();
        private static IntPtr _hMonitor = IntPtr.Zero;
        private static Native.PHYSICAL_MONITOR[] _monitors;
        private static uint _count;
        private static uint _min;
        private static uint _max;
        private static int _cachedPercent = -1;
        private static bool _rangeReady;

        public static void Invalidate()
        {
            lock (Sync)
            {
                ClosePhysical_NoLock();
                _cachedPercent = -1;
                _rangeReady = false;
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

                percent = 0;
                if (!EnsureOpen_NoLock())
                    return false;

                uint min = 0, cur = 0, max = 0;
                if (!Native.GetMonitorBrightness(_monitors[0].hPhysicalMonitor, ref min, ref cur, ref max))
                {
                    ClosePhysical_NoLock();
                    return false;
                }
                if (max <= min)
                    return false;

                _min = min;
                _max = max;
                _rangeReady = true;
                percent = ToPercent(cur);
                _cachedPercent = percent;
                return true;
            }
        }

        public static bool TrySetPercent(int percent)
        {
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;

            lock (Sync)
            {
                if (!EnsureOpen_NoLock())
                    return false;

                if (!_rangeReady)
                {
                    uint min = 0, cur = 0, max = 0;
                    if (!Native.GetMonitorBrightness(_monitors[0].hPhysicalMonitor, ref min, ref cur, ref max) || max <= min)
                    {
                        ClosePhysical_NoLock();
                        return false;
                    }
                    _min = min;
                    _max = max;
                    _rangeReady = true;
                }

                if (_cachedPercent == percent && _rangeReady)
                    return true;

                uint value = _min + (uint)Math.Round(percent * (_max - _min) / 100.0);
                if (value < _min) value = _min;
                if (value > _max) value = _max;

                if (!Native.SetMonitorBrightness(_monitors[0].hPhysicalMonitor, value))
                {
                    // Handle may be stale after HDR toggle / sleep
                    ClosePhysical_NoLock();
                    if (!EnsureOpen_NoLock())
                        return false;
                    if (!Native.SetMonitorBrightness(_monitors[0].hPhysicalMonitor, value))
                        return false;
                }

                _cachedPercent = percent;
                return true;
            }
        }

        private static int ToPercent(uint cur)
        {
            int percent = (int)Math.Round((cur - _min) * 100.0 / (_max - _min));
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            return percent;
        }

        private static bool EnsureOpen_NoLock()
        {
            if (_monitors != null && _count > 0 && _monitors[0].hPhysicalMonitor != IntPtr.Zero)
                return true;

            ClosePhysical_NoLock();

            _hMonitor = IntPtr.Zero;
            Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, EnumCallback, IntPtr.Zero);
            if (_hMonitor == IntPtr.Zero)
                _hMonitor = Native.MonitorFromWindow(IntPtr.Zero, Native.MONITOR_DEFAULTTOPRIMARY);
            if (_hMonitor == IntPtr.Zero)
                return false;

            uint count = 0;
            if (!Native.GetNumberOfPhysicalMonitorsFromHMONITOR(_hMonitor, ref count) || count == 0)
                return false;

            Native.PHYSICAL_MONITOR[] monitors = new Native.PHYSICAL_MONITOR[count];
            if (!Native.GetPhysicalMonitorsFromHMONITOR(_hMonitor, count, monitors))
                return false;

            _monitors = monitors;
            _count = count;
            _rangeReady = false;
            return true;
        }

        private static void ClosePhysical_NoLock()
        {
            if (_monitors != null && _count > 0)
            {
                try { Native.DestroyPhysicalMonitors(_count, _monitors); }
                catch { }
            }
            _monitors = null;
            _count = 0;
            _hMonitor = IntPtr.Zero;
            _rangeReady = false;
        }

        private static bool EnumCallback(IntPtr hMonitor, IntPtr hdc, IntPtr lprc, IntPtr data)
        {
            Native.MONITORINFOEX info = new Native.MONITORINFOEX();
            info.cbSize = Marshal.SizeOf(typeof(Native.MONITORINFOEX));
            if (Native.GetMonitorInfo(hMonitor, ref info))
            {
                if ((info.dwFlags & Native.MONITORINFOF_PRIMARY) != 0)
                {
                    _hMonitor = hMonitor;
                    return false;
                }
            }
            if (_hMonitor == IntPtr.Zero)
                _hMonitor = hMonitor;
            return true;
        }
    }
}
