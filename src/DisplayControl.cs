using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace Hdr2Sdr
{
    internal sealed class DisplayTarget
    {
        public Native.LUID AdapterId;
        public uint TargetId;
        public uint SourceId;
        public bool HdrSupported;
        public bool HdrEnabled;
        public int SdrNits;
    }

    internal static class DisplayControl
    {
        public const int MinSdrNits = 80;
        public const int MaxSdrNits = 480;

        /// <summary>Windows Settings-style scale: 0 = 80 nits, 100 = 480 nits.</summary>
        public static int NitsToPercent(int nits)
        {
            if (nits <= MinSdrNits) return 0;
            if (nits >= MaxSdrNits) return 100;
            return (int)Math.Round((nits - MinSdrNits) * 100.0 / (MaxSdrNits - MinSdrNits));
        }

        public static int PercentToNits(int percent)
        {
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            return MinSdrNits + (int)Math.Round(percent * (MaxSdrNits - MinSdrNits) / 100.0);
        }

        public static int GetSdrPercent()
        {
            DisplayTarget t = GetPrimaryHdrTarget();
            if (t == null) return 0;
            return NitsToPercent(t.SdrNits);
        }

        public static bool SetSdrPercent(int percent)
        {
            return SetSdrNits(PercentToNits(percent));
        }

        public static int AdjustSdrPercent(int deltaPercent)
        {
            int next = GetSdrPercent() + deltaPercent;
            if (next < 0) next = 0;
            if (next > 100) next = 100;
            SetSdrPercent(next);
            return next;
        }

        /// <summary>
        /// Unified brightness 0-100:
        /// HDR ON  → Windows "SDR content brightness"
        /// HDR OFF → monitor hardware brightness (DDC/CI)
        /// </summary>
        public static int GetBrightnessPercent()
        {
            DisplayTarget t = GetPrimaryHdrTarget();
            if (t != null && t.HdrEnabled)
                return GetSdrPercent();

            int pct;
            if (MonitorBrightness.TryGetPercent(out pct))
                return pct;
            return 0;
        }

        public static bool SetBrightnessPercent(int percent)
        {
            LastError = "";
            DisplayTarget t = GetPrimaryHdrTarget();
            if (t != null && t.HdrEnabled)
            {
                MonitorBrightness.Invalidate();
                return SetSdrPercent(percent);
            }

            if (MonitorBrightness.TrySetPercent(percent))
                return true;

            LastError = "DDC/CI indisponible (luminosite moniteur)";
            return false;
        }

        public static int AdjustBrightnessPercent(int deltaPercent)
        {
            int next = GetBrightnessPercent() + deltaPercent;
            if (next < 0) next = 0;
            if (next > 100) next = 100;
            // One set only — do not re-query DDC afterwards (slow / can stall UI)
            SetBrightnessPercent(next);
            return next;
        }

        public static bool IsHdrOn()
        {
            DisplayTarget t = GetPrimaryHdrTarget();
            return t != null && t.HdrEnabled;
        }

        public static void OnHdrModeChanging()
        {
            MonitorBrightness.Invalidate();
        }

        public static List<DisplayTarget> GetTargets()
        {
            List<DisplayTarget> list = new List<DisplayTarget>();
            uint pathCount;
            uint modeCount;
            if (Native.GetDisplayConfigBufferSizes(Native.QDC_ONLY_ACTIVE_PATHS, out pathCount, out modeCount) != Native.ERROR_SUCCESS)
                return list;
            if (pathCount == 0)
                return list;

            Native.DISPLAYCONFIG_PATH_INFO[] paths = new Native.DISPLAYCONFIG_PATH_INFO[pathCount];
            Native.DISPLAYCONFIG_MODE_INFO[] modes = new Native.DISPLAYCONFIG_MODE_INFO[Math.Max(modeCount, 1)];
            if (Native.QueryDisplayConfig(Native.QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != Native.ERROR_SUCCESS)
                return list;

            for (uint i = 0; i < pathCount; i++)
            {
                Native.DISPLAYCONFIG_PATH_INFO path = paths[i];
                DisplayTarget target = new DisplayTarget();
                target.AdapterId = path.targetInfo.adapterId;
                target.TargetId = path.targetInfo.id;
                target.SourceId = path.sourceInfo.id;
                target.SdrNits = MinSdrNits;

                Native.DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2 color2 = new Native.DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2();
                color2.header.type = Native.DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO_2;
                color2.header.size = Marshal.SizeOf(typeof(Native.DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2));
                color2.header.adapterId = target.AdapterId;
                color2.header.id = target.TargetId;

                if (Native.DisplayConfigGetDeviceInfo(ref color2) == Native.ERROR_SUCCESS)
                {
                    // INFO_2 bit4 = highDynamicRangeSupported; activeColorMode == HDR
                    target.HdrSupported = (color2.value & 0x10) != 0;
                    target.HdrEnabled = color2.activeColorMode == Native.DISPLAYCONFIG_ADVANCED_COLOR_MODE_HDR;
                    if (!target.HdrSupported && color2.activeColorMode == Native.DISPLAYCONFIG_ADVANCED_COLOR_MODE_HDR)
                        target.HdrSupported = true;
                }
                else
                {
                    Native.DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO color = new Native.DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO();
                    color.header.type = Native.DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO;
                    color.header.size = Marshal.SizeOf(typeof(Native.DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO));
                    color.header.adapterId = target.AdapterId;
                    color.header.id = target.TargetId;

                    if (Native.DisplayConfigGetDeviceInfo(ref color) == Native.ERROR_SUCCESS)
                    {
                        target.HdrSupported = (color.value & 0x1) != 0;
                        target.HdrEnabled = (color.value & 0x2) != 0;
                    }
                }

                Native.DISPLAYCONFIG_GET_SDR_WHITE_LEVEL sdr = new Native.DISPLAYCONFIG_GET_SDR_WHITE_LEVEL();
                sdr.header.type = Native.DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL;
                sdr.header.size = Marshal.SizeOf(typeof(Native.DISPLAYCONFIG_GET_SDR_WHITE_LEVEL));
                sdr.header.adapterId = target.AdapterId;
                sdr.header.id = target.TargetId;
                if (Native.DisplayConfigGetDeviceInfo(ref sdr) == Native.ERROR_SUCCESS && sdr.SDRWhiteLevel > 0)
                    target.SdrNits = (int)Math.Round(sdr.SDRWhiteLevel * 80.0 / 1000.0);

                list.Add(target);
            }

            return list;
        }

        public static DisplayTarget GetPrimaryHdrTarget()
        {
            List<DisplayTarget> targets = GetTargets();
            for (int i = 0; i < targets.Count; i++)
            {
                if (targets[i].HdrSupported)
                    return targets[i];
            }
            if (targets.Count > 0)
                return targets[0];
            return null;
        }

        public static bool SetHdr(DisplayTarget target, bool enabled)
        {
            if (target == null)
                return false;

            bool before = target.HdrEnabled;
            if (before == enabled)
                return true;

            MonitorBrightness.Invalidate();

            // Prefer Win11 24H2 SET_HDR_STATE
            Native.DISPLAYCONFIG_SET_HDR_STATE hdr = new Native.DISPLAYCONFIG_SET_HDR_STATE();
            hdr.header.type = Native.DISPLAYCONFIG_DEVICE_INFO_SET_HDR_STATE;
            hdr.header.size = Marshal.SizeOf(typeof(Native.DISPLAYCONFIG_SET_HDR_STATE));
            hdr.header.adapterId = target.AdapterId;
            hdr.header.id = target.TargetId;
            hdr.enableHdr = enabled ? 1u : 0u;

            int rc = Native.DisplayConfigSetDeviceInfo(ref hdr);
            Thread.Sleep(250);

            DisplayTarget after = GetPrimaryHdrTarget();
            if (after != null && after.HdrEnabled == enabled)
                return true;

            // Legacy fallback
            Native.DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE packet = new Native.DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE();
            packet.header.type = Native.DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE;
            packet.header.size = Marshal.SizeOf(typeof(Native.DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE));
            packet.header.adapterId = target.AdapterId;
            packet.header.id = target.TargetId;
            packet.enableAdvancedColorState = enabled ? 1u : 0u;

            int rcLegacy = Native.DisplayConfigSetDeviceInfo(ref packet);
            Thread.Sleep(250);

            after = GetPrimaryHdrTarget();
            if (after != null && after.HdrEnabled == enabled)
                return true;

            // Fallback: Xbox Game Bar shortcut Win+Alt+B
            SendHdrHotkey();
            Thread.Sleep(400);
            after = GetPrimaryHdrTarget();
            if (after != null && after.HdrEnabled == enabled)
                return true;

            LastError = "HDR set hdrRc=" + rc + " legacyRc=" + rcLegacy + " (avant=" + before + " cible=" + enabled + ")";
            return false;
        }

        public static string LastError = "";

        private static void SendHdrHotkey()
        {
            // Win + Alt + B
            Native.keybd_event(Native.VK_LWIN, 0, 0, UIntPtr.Zero);
            Native.keybd_event(Native.VK_MENU, 0, 0, UIntPtr.Zero);
            Native.keybd_event(Native.VK_B, 0, 0, UIntPtr.Zero);
            Thread.Sleep(30);
            Native.keybd_event(Native.VK_B, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
            Native.keybd_event(Native.VK_MENU, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
            Native.keybd_event(Native.VK_LWIN, 0, Native.KEYEVENTF_KEYUP, UIntPtr.Zero);
        }

        public static bool ToggleHdr(out bool nowEnabled)
        {
            nowEnabled = false;
            DisplayTarget target = GetPrimaryHdrTarget();
            if (target == null || !target.HdrSupported)
                return false;

            bool next = !target.HdrEnabled;
            if (!SetHdr(target, next))
                return false;

            DisplayTarget verify = GetPrimaryHdrTarget();
            nowEnabled = verify != null ? verify.HdrEnabled : next;
            return true;
        }

        public static bool SetSdrNits(int nits)
        {
            if (nits < MinSdrNits) nits = MinSdrNits;
            if (nits > MaxSdrNits) nits = MaxSdrNits;

            DisplayTarget target = GetPrimaryHdrTarget();
            if (target == null)
                return false;

            Native.DISPLAYCONFIG_SET_SDR_WHITE_LEVEL packet = new Native.DISPLAYCONFIG_SET_SDR_WHITE_LEVEL();
            packet.header.type = Native.DISPLAYCONFIG_DEVICE_INFO_SET_SDR_WHITE_LEVEL;
            packet.header.size = Marshal.SizeOf(typeof(Native.DISPLAYCONFIG_SET_SDR_WHITE_LEVEL));
            packet.header.adapterId = target.AdapterId;
            packet.header.id = target.TargetId;
            packet.SDRWhiteLevel = (uint)(nits * 1000 / 80);
            packet.finalValue = 1;

            bool ok = Native.DisplayConfigSetDeviceInfo(ref packet) == Native.ERROR_SUCCESS;
            if (!ok)
            {
                double boost = nits / 80.0;
                IntPtr monitor = Native.MonitorFromWindow(IntPtr.Zero, Native.MONITOR_DEFAULTTOPRIMARY);
                try
                {
                    ok = Native.DwmpSDRToHDRBoost(monitor, boost) >= 0;
                }
                catch
                {
                    ok = false;
                }
            }
            return ok;
        }

        public static int AdjustSdrNits(int delta)
        {
            DisplayTarget target = GetPrimaryHdrTarget();
            int current = target != null ? target.SdrNits : MinSdrNits;
            int next = current + delta;
            if (next < MinSdrNits) next = MinSdrNits;
            if (next > MaxSdrNits) next = MaxSdrNits;
            SetSdrNits(next);
            return next;
        }

        public static string StatusText()
        {
            DisplayTarget t = GetPrimaryHdrTarget();
            if (t == null)
                return "Aucun ecran detecte";

            if (!t.HdrSupported)
                return "SDR | luminosite " + GetBrightnessPercent() + "%";

            if (t.HdrEnabled)
                return "HDR ON | SDR " + GetSdrPercent() + "%";

            return "SDR | luminosite " + GetBrightnessPercent() + "%";
        }
    }
}
