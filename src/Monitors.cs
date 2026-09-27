using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace Hdr2Sdr
{
    internal sealed class MonitorEntry
    {
        public string DeviceName;
        public string FriendlyName;
        public int Width;
        public int Height;
        public bool Primary;
        public string Label;

        public override string ToString()
        {
            return Label ?? DeviceName ?? "";
        }
    }

    /// <summary>
    /// Écrans Windows (GDI). SelectedDevice vide = écran principal.
    /// </summary>
    internal static class MonitorCatalog
    {
        public static string SelectedDevice = "";

        private static List<IntPtr> _enum;
        private static List<MonitorEntry> _found;

        public static List<MonitorEntry> List()
        {
            List<MonitorEntry> list = new List<MonitorEntry>();
            _enum = new List<IntPtr>();
            _found = list;
            try { Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, EnumAdd, IntPtr.Zero); }
            catch { }
            _enum = null;
            _found = null;

            list.Sort(delegate(MonitorEntry a, MonitorEntry b)
            {
                if (a.Primary != b.Primary)
                    return a.Primary ? -1 : 1;
                int areaA = a.Width * a.Height;
                int areaB = b.Width * b.Height;
                return areaB.CompareTo(areaA);
            });
            return list;
        }

        public static MonitorEntry Resolve()
        {
            List<MonitorEntry> all = List();
            if (!string.IsNullOrEmpty(SelectedDevice))
            {
                for (int i = 0; i < all.Count; i++)
                {
                    if (string.Equals(all[i].DeviceName, SelectedDevice, StringComparison.OrdinalIgnoreCase))
                        return all[i];
                }
            }
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].Primary)
                    return all[i];
            }
            if (all.Count > 0)
                return all[0];
            return null;
        }

        public static bool TryGetHandle(out IntPtr handle)
        {
            handle = IntPtr.Zero;
            MonitorEntry want = Resolve();
            _enum = new List<IntPtr>();
            _found = null;
            try { Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, EnumAdd, IntPtr.Zero); }
            catch { }
            List<IntPtr> all = _enum;
            _enum = null;

            IntPtr primary = IntPtr.Zero;
            IntPtr match = IntPtr.Zero;
            string wantName = want != null ? want.DeviceName : SelectedDevice;
            for (int i = 0; i < all.Count; i++)
            {
                Native.MONITORINFOEX info = new Native.MONITORINFOEX();
                info.cbSize = Marshal.SizeOf(typeof(Native.MONITORINFOEX));
                if (!Native.GetMonitorInfo(all[i], ref info))
                    continue;
                if ((info.dwFlags & Native.MONITORINFOF_PRIMARY) != 0)
                    primary = all[i];
                if (!string.IsNullOrEmpty(wantName) &&
                    string.Equals(info.szDevice, wantName, StringComparison.OrdinalIgnoreCase))
                    match = all[i];
            }
            handle = match != IntPtr.Zero ? match : primary;
            if (handle == IntPtr.Zero && all != null && all.Count > 0)
                handle = all[0];
            if (handle == IntPtr.Zero)
                handle = Native.MonitorFromWindow(IntPtr.Zero, Native.MONITOR_DEFAULTTOPRIMARY);
            return handle != IntPtr.Zero;
        }

        private static bool EnumAdd(IntPtr hMonitor, IntPtr hdc, IntPtr lprc, IntPtr data)
        {
            if (_enum != null && !_enum.Contains(hMonitor))
                _enum.Add(hMonitor);
            if (_found == null)
                return true;

            Native.MONITORINFOEX info = new Native.MONITORINFOEX();
            info.cbSize = Marshal.SizeOf(typeof(Native.MONITORINFOEX));
            if (!Native.GetMonitorInfo(hMonitor, ref info) || string.IsNullOrEmpty(info.szDevice))
                return true;

            MonitorEntry e = new MonitorEntry();
            e.DeviceName = info.szDevice;
            e.Primary = (info.dwFlags & Native.MONITORINFOF_PRIMARY) != 0;
            e.Width = info.rcMonitor.right - info.rcMonitor.left;
            e.Height = info.rcMonitor.bottom - info.rcMonitor.top;
            e.FriendlyName = FriendlyName(info.szDevice);
            e.Label = BuildLabel(e);
            _found.Add(e);
            return true;
        }

        private static string FriendlyName(string device)
        {
            try
            {
                Native.DISPLAY_DEVICE dd = new Native.DISPLAY_DEVICE();
                dd.cb = Marshal.SizeOf(typeof(Native.DISPLAY_DEVICE));
                if (!Native.EnumDisplayDevices(device, 0, ref dd, 0))
                    return "";
                string s = dd.DeviceString ?? "";
                if (s.Length == 0)
                    return "";
                if (s.IndexOf("Generic", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "";
                if (s.IndexOf("PnP", StringComparison.OrdinalIgnoreCase) >= 0)
                    return "";
                return s;
            }
            catch
            {
                return "";
            }
        }

        private static string BuildLabel(MonitorEntry e)
        {
            string id = e.DeviceName ?? "";
            if (id.StartsWith("\\\\.\\", StringComparison.OrdinalIgnoreCase))
                id = id.Substring(4);
            string label = id + " · " + e.Width + "×" + e.Height;
            if (e.Primary)
                label += " · principal";
            if (!string.IsNullOrEmpty(e.FriendlyName))
                label = e.FriendlyName + " · " + label;
            return label;
        }
    }
}
