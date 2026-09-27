using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace Hdr2Sdr
{
    /// <summary>
    /// Hardware monitor brightness via DDC/CI (dxva2) — used when HDR is OFF (true SDR).
    /// Ne garde JAMAIS le handle ouvert : open -&gt; action -&gt; close à chaque
    /// opération (sinon conflit avec les autres process DDC : CLI, Stream Deck,
    /// et handle coincé après veille/boot). Seuls min/max/valeur sont cachés.
    /// </summary>
    internal static class MonitorBrightness
    {
        private static readonly object Sync = new object();
        private static Native.PHYSICAL_MONITOR[] _monitors;
        private static uint _count;
        private static uint _min;
        private static uint _max;
        private static int _cachedPercent = -1;
        private static bool _rangeReady;
        [DllImport("dxva2.dll", EntryPoint = "GetPhysicalMonitorsFromHMONITOR", SetLastError = true)]
        private static extern bool GetPhysicalRaw(IntPtr hMonitor, uint count, IntPtr buffer);

        private static int _hold;

        public static void Invalidate()
        {
            lock (Sync)
            {
                // Ne pas toucher _hold : un changement d'écran (0x7E) peut arriver
                // pendant une rafale de sortie HDR ; casser le hold fermerait le
                // handle en pleine écriture. On invalide juste le cache.
                ClosePhysical_NoLock();
                _cachedPercent = -1;
                // Keep _min/_max/_rangeReady so we can Set immediately after HDR toggle
                // without a slow Get (which often sees the panel already at 100%).
            }
        }

        /// <summary>Lecture du cache seul, sans aucun accès DDC (sûr sur thread UI).</summary>
        public static bool TryGetCachedPercent(out int percent)
        {
            lock (Sync)
            {
                if (_cachedPercent >= 0)
                {
                    percent = _cachedPercent;
                    return true;
                }
                percent = 0;
                return false;
            }
        }

        /// <summary>Maintient le handle ouvert le temps d'une rafale (sortie HDR).</summary>
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
                if (_hold == 0)
                    ClosePhysical_NoLock();
            }
        }

        private static void CloseUnlessHeld_NoLock()
        {
            if (_hold == 0)
                ClosePhysical_NoLock();
        }

        /// <summary>Open DDC and cache min/max while the link is still stable (e.g. still in HDR).</summary>
        public static bool EnsureRangeCached()
        {
            lock (Sync)
            {
                if (_rangeReady && _max > _min)
                    return true;
                if (!EnsureOpen_NoLock())
                    return false;
                uint min = 0, cur = 0, max = 0;
                if (!Native.GetMonitorBrightness(_monitors[0].hPhysicalMonitor, ref min, ref cur, ref max) || max <= min)
                {
                    CloseUnlessHeld_NoLock();
                    return false;
                }
                _min = min;
                _max = max;
                _rangeReady = true;
                CloseUnlessHeld_NoLock();
                return true;
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
                int previous = _cachedPercent;
                _cachedPercent = -1;
                ClosePhysical_NoLock();
                bool ok = TryGetPercentFresh_NoLock(out percent, previous);
                if (!ok)
                    _cachedPercent = previous;
                return ok;
            }
        }

        private static bool TryGetPercentFresh_NoLock(out int percent)
        {
            return TryGetPercentFresh_NoLock(out percent, _cachedPercent);
        }

        private static bool TryGetPercentFresh_NoLock(out int percent, int previous)
        {
            percent = 0;
            if (!EnsureOpen_NoLock())
                return false;

            uint min = 0, cur = 0, max = 0;
            if (!Native.GetMonitorBrightness(_monitors[0].hPhysicalMonitor, ref min, ref cur, ref max))
            {
                CloseUnlessHeld_NoLock();
                return false;
            }
            if (max <= min)
            {
                CloseUnlessHeld_NoLock();
                return false;
            }

            if (cur >= max && previous >= 0 && previous < 97)
            {
                CloseUnlessHeld_NoLock();
                percent = previous;
                _cachedPercent = previous;
                return true;
            }

            _min = min;
            _max = max;
            _rangeReady = true;
            percent = ToPercent(cur);
            _cachedPercent = percent;
            CloseUnlessHeld_NoLock();
            return true;
        }

        public static bool TrySetPercent(int percent)
        {
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;

            lock (Sync)
            {
                if (!EnsureOpen_NoLock())
                    return false;

                if (!_rangeReady || _max <= _min)
                {
                    uint min = 0, cur = 0, max = 0;
                    if (!Native.GetMonitorBrightness(_monitors[0].hPhysicalMonitor, ref min, ref cur, ref max) || max <= min)
                    {
                        CloseUnlessHeld_NoLock();
                        return false;
                    }
                    _min = min;
                    _max = max;
                    _rangeReady = true;
                }

                // Always write after mode changes — do not skip even if cache matches
                uint value = _min + (uint)Math.Round(percent * (_max - _min) / 100.0);
                if (value < _min) value = _min;
                if (value > _max) value = _max;

                if (!WriteWithRetry_NoLock(value, percent))
                {
                    CloseUnlessHeld_NoLock();
                    return false;
                }

                _cachedPercent = percent;
                CloseUnlessHeld_NoLock();
                return true;
            }
        }

        /// <summary>
        /// Écrit + vérifie par relecture (les écritures DDC silencieusement
        /// ignorées sont fréquentes quand un autre handle est ouvert).
        /// </summary>
        private static bool WriteWithRetry_NoLock(uint rawValue, int percent)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                if (attempt > 0)
                {
                    // Sous hold on garde le handle ; sinon on rouvre proprement.
                    if (_hold == 0)
                        ClosePhysical_NoLock();
                    if (!EnsureOpen_NoLock())
                        return false;
                }

                bool anyOk = false;
                for (int m = 0; m < _monitors.Length; m++)
                {
                    if (_monitors[m].hPhysicalMonitor == IntPtr.Zero)
                        continue;
                    if (Native.SetMonitorBrightness(_monitors[m].hPhysicalMonitor, rawValue))
                        anyOk = true;
                }
                if (!anyOk)
                {
                    if (_hold == 0)
                        ClosePhysical_NoLock();
                    continue;
                }

                Thread.Sleep(120);
                uint min = 0, cur = 0, max = 0;
                if (!Native.GetMonitorBrightness(_monitors[0].hPhysicalMonitor, ref min, ref cur, ref max) || max <= min)
                    return true;

                // Readout coincé au max : l'écriture a été acceptée, la relecture
                // ne peut pas la confirmer. On ne la compte pas comme un échec
                // (sinon chaque pas du slider est rejeté).
                if (cur >= max && percent < 97)
                    return true;

                int got = (int)Math.Round((cur - min) * 100.0 / (max - min));
                if (got < 0) got = 0;
                if (got > 100) got = 100;
                if (Math.Abs(got - percent) <= 3)
                    return true;
            }
            return false;
        }

        /// <summary>Force-write brightness, reopening the handle if needed. Never skips.</summary>
        public static bool ForceSetPercent(int percent)
        {
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;

            lock (Sync)
            {
                // Ne plus fermer systématiquement : sous BeginHold le handle reste
                // ouvert pour toute la rafale (c'était le but du hold).
                if (!EnsureOpen_NoLock())
                    return false;

                if (!_rangeReady || _max <= _min)
                {
                    uint min = 0, cur = 0, max = 0;
                    if (!Native.GetMonitorBrightness(_monitors[0].hPhysicalMonitor, ref min, ref cur, ref max) || max <= min)
                    {
                        CloseUnlessHeld_NoLock();
                        return false;
                    }
                    _min = min;
                    _max = max;
                    _rangeReady = true;
                }

                uint value = _min + (uint)Math.Round(percent * (_max - _min) / 100.0);
                if (value < _min) value = _min;
                if (value > _max) value = _max;

                bool anyOk = false;
                for (int m = 0; m < _monitors.Length; m++)
                {
                    if (_monitors[m].hPhysicalMonitor == IntPtr.Zero)
                        continue;
                    if (Native.SetMonitorBrightness(_monitors[m].hPhysicalMonitor, value))
                        anyOk = true;
                }
                if (!anyOk)
                {
                    CloseUnlessHeld_NoLock();
                    return false;
                }

                _cachedPercent = percent;
                CloseUnlessHeld_NoLock();
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

        private static string _openWant;
        private static Native.PHYSICAL_MONITOR[] _openResult;

        private static bool EnsureOpen_NoLock()
        {
            if (_monitors != null && _count > 0 && _monitors[0].hPhysicalMonitor != IntPtr.Zero)
                return true;

            ClosePhysical_NoLock();

            MonitorEntry want = null;
            try { want = MonitorCatalog.Resolve(); }
            catch { }
            _openWant = want != null ? want.DeviceName : MonitorCatalog.SelectedDevice;
            _openResult = null;
            try { Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, EnumOpenPhysical, IntPtr.Zero); }
            catch { }
            if (_openResult == null && !string.IsNullOrEmpty(_openWant))
            {
                _openWant = "";
                try { Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, EnumOpenPhysical, IntPtr.Zero); }
                catch { }
            }
            if (_openResult == null || _openResult.Length == 0)
                return false;

            _monitors = _openResult;
            _count = (uint)_openResult.Length;
            _openResult = null;
            return true;
        }

        private static bool EnumOpenPhysical(IntPtr hMonitor, IntPtr hdc, IntPtr lprc, IntPtr data)
        {
            Native.MONITORINFOEX info = new Native.MONITORINFOEX();
            info.cbSize = Marshal.SizeOf(typeof(Native.MONITORINFOEX));
            if (!Native.GetMonitorInfo(hMonitor, ref info))
                return true;

            bool match = string.IsNullOrEmpty(_openWant)
                ? (info.dwFlags & Native.MONITORINFOF_PRIMARY) != 0
                : string.Equals(info.szDevice, _openWant, StringComparison.OrdinalIgnoreCase);
            if (!match)
                return true;

            uint count = 0;
            if (!Native.GetNumberOfPhysicalMonitorsFromHMONITOR(hMonitor, ref count) || count == 0)
                return true;

            // Le marshalling du struct renvoie souvent un handle nul en 64 bits.
            // On lit le HANDLE brut (8 octets + nom WCHAR[128]).
            int stride = IntPtr.Size + 256;
            IntPtr buf = Marshal.AllocHGlobal(stride * (int)count);
            bool got = false;
            try { got = GetPhysicalRaw(hMonitor, count, buf); }
            catch { got = false; }
            if (!got)
            {
                Marshal.FreeHGlobal(buf);
                return true;
            }

            System.Collections.Generic.List<Native.PHYSICAL_MONITOR> kept =
                new System.Collections.Generic.List<Native.PHYSICAL_MONITOR>();
            for (int i = 0; i < count; i++)
            {
                IntPtr phys = Marshal.ReadIntPtr(buf, i * stride);
                if (phys == IntPtr.Zero)
                    continue;
                Native.PHYSICAL_MONITOR pm = new Native.PHYSICAL_MONITOR();
                pm.hPhysicalMonitor = phys;
                kept.Add(pm);
            }
            Marshal.FreeHGlobal(buf);
            if (kept.Count == 0)
                return true;

            _openResult = kept.ToArray();
            return false;
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
        }
    }
}
