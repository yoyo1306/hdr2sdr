using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Hdr2Sdr.Core;

public sealed class DisplayTarget
{
    public NativeMethods.LUID AdapterId { get; init; }
    public uint TargetId { get; init; }
    public uint SourceId { get; init; }
    public string DeviceName { get; init; } = string.Empty;
    public bool HdrSupported { get; init; }
    public bool HdrEnabled { get; init; }
    public int SdrNits { get; init; } = 80;
}

[SupportedOSPlatform("windows")]
public static class DisplayControl
{
    public const int MinSdrNits = 80;
    public const int MaxSdrNits = 480;

    private static int _lastSdrHardwarePercent = -1;
    public static string LastError { get; private set; } = string.Empty;

    public static int NitsToPercent(int nits) =>
        Math.Clamp((int)Math.Round((nits - MinSdrNits) / 4.0), 0, 100);

    public static int PercentToNits(int percent) =>
        MinSdrNits + Math.Clamp(percent, 0, 100) * 4;

    public static int GetSdrPercent()
    {
        var t = GetPrimaryHdrTarget();
        return t is null ? 0 : NitsToPercent(t.SdrNits);
    }

    public static bool SetSdrPercent(int percent) =>
        SetSdrNits(PercentToNits(percent));

    public static int AdjustSdrPercent(int deltaPercent)
    {
        int next = Math.Clamp(GetSdrPercent() + deltaPercent, 0, 100);
        SetSdrPercent(next);
        return next;
    }

    public static int GetBrightnessPercent()
    {
        var t = GetPrimaryHdrTarget();
        if (t is not null && t.HdrEnabled)
            return NitsToPercent(t.SdrNits);

        if (MonitorBrightness.TryGetPercent(out var p))
        {
            _lastSdrHardwarePercent = p;
            return p;
        }

        return _lastSdrHardwarePercent >= 0 ? _lastSdrHardwarePercent : 50;
    }

    public static int GetBrightnessPercentCached()
    {
        var t = GetPrimaryHdrTarget();
        if (t is not null && t.HdrEnabled)
            return NitsToPercent(t.SdrNits);

        if (MonitorBrightness.TryGetCachedPercent(out var p))
        {
            _lastSdrHardwarePercent = p;
            return p;
        }

        if (_lastSdrHardwarePercent >= 0)
            return _lastSdrHardwarePercent;

        return MonitorBrightness.TryGetPercent(out var fresh) ? fresh : 50;
    }

    public static bool TryGetBrightnessPercentFresh(out int percent)
    {
        var t = GetPrimaryHdrTarget();
        if (t is not null && t.HdrEnabled)
        {
            percent = NitsToPercent(t.SdrNits);
            return true;
        }

        if (MonitorBrightness.TryGetPercentFresh(out percent))
        {
            _lastSdrHardwarePercent = percent;
            return true;
        }

        return false;
    }

    public static bool SetBrightnessPercent(int percent)
    {
        LastError = string.Empty;
        percent = Math.Clamp(percent, 0, 100);
        var t = GetPrimaryHdrTarget();
        if (t is not null && t.HdrEnabled)
            return SetSdrPercent(percent);

        if (MonitorBrightness.TrySetPercent(percent))
        {
            _lastSdrHardwarePercent = percent;
            return true;
        }

        LastError = "DDC/CI indisponible (luminosité moniteur)";
        return false;
    }

    public static void RememberSdrBrightness()
    {
        if (IsHdrOn())
            return;

        if (MonitorBrightness.TryGetCachedPercent(out var cached))
        {
            _lastSdrHardwarePercent = cached;
            return;
        }

        if (MonitorBrightness.TryGetPercent(out var fresh))
            _lastSdrHardwarePercent = fresh;
    }

    public static int GetRememberedSdrBrightness() => _lastSdrHardwarePercent;

    public static bool LeaveHdrRestoringBrightness(out int restoredPercent)
    {
        restoredPercent = _lastSdrHardwarePercent;
        var target = GetPrimaryHdrTarget();
        if (target is null)
        {
            LastError = "aucun écran détecté";
            return false;
        }

        if (!target.HdrEnabled)
        {
            if (restoredPercent < 0 && MonitorBrightness.TryGetPercent(out var cur))
                restoredPercent = cur;
            return true;
        }

        bool ok = SetHdr(target, enabled: false, restoreBrightnessPercent: restoredPercent);
        if (ok && restoredPercent < 0 && MonitorBrightness.TryGetPercentFresh(out var curAfter))
        {
            restoredPercent = curAfter;
            _lastSdrHardwarePercent = curAfter;
        }

        return ok;
    }

    public static int AdjustBrightnessPercent(int deltaPercent)
    {
        int next = Math.Clamp(GetBrightnessPercent() + deltaPercent, 0, 100);
        SetBrightnessPercent(next);
        return next;
    }

    public static bool IsHdrOn()
    {
        var t = GetPrimaryHdrTarget();
        return t is not null && t.HdrEnabled;
    }

    public static void OnHdrModeChanging()
    {
        MonitorBrightness.Invalidate();
    }

    public static List<DisplayTarget> GetTargets()
    {
        var result = new List<DisplayTarget>();

        if (NativeMethods.GetDisplayConfigBufferSizes(NativeMethods.QDC_ONLY_ACTIVE_PATHS, out var pathCount, out var modeCount) != NativeMethods.ERROR_SUCCESS)
            return result;

        var paths = new NativeMethods.DISPLAYCONFIG_PATH_INFO[pathCount];
        var modes = new NativeMethods.DISPLAYCONFIG_MODE_INFO[modeCount];

        if (NativeMethods.QueryDisplayConfig(NativeMethods.QDC_ONLY_ACTIVE_PATHS, ref pathCount, paths, ref modeCount, modes, IntPtr.Zero) != NativeMethods.ERROR_SUCCESS)
            return result;

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < pathCount; i++)
        {
            var path = paths[i];
            if (path.targetInfo.targetAvailable == 0)
                continue;

            var srcName = new NativeMethods.DISPLAYCONFIG_SOURCE_DEVICE_NAME
            {
                header = new NativeMethods.DISPLAYCONFIG_DEVICE_INFO_HEADER
                {
                    type = NativeMethods.DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME,
                    size = Marshal.SizeOf<NativeMethods.DISPLAYCONFIG_SOURCE_DEVICE_NAME>(),
                    adapterId = path.sourceInfo.adapterId,
                    id = path.sourceInfo.id
                }
            };

            string deviceName = string.Empty;
            if (NativeMethods.DisplayConfigGetDeviceInfo(ref srcName) == NativeMethods.ERROR_SUCCESS)
                deviceName = srcName.viewGdiDeviceName ?? string.Empty;

            var key = $"{path.targetInfo.adapterId.HighPart}:{path.targetInfo.adapterId.LowPart}:{path.targetInfo.id}";
            if (!seen.Add(key))
                continue;

            bool hdrSupported = false;
            bool hdrEnabled = false;

            var info2 = new NativeMethods.DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2
            {
                header = new NativeMethods.DISPLAYCONFIG_DEVICE_INFO_HEADER
                {
                    type = NativeMethods.DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO_2,
                    size = Marshal.SizeOf<NativeMethods.DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO_2>(),
                    adapterId = path.targetInfo.adapterId,
                    id = path.targetInfo.id
                }
            };

            if (NativeMethods.DisplayConfigGetDeviceInfo(ref info2) == NativeMethods.ERROR_SUCCESS)
            {
                // Bit 3 = highDynamicRangeSupported (Win11 24H2+), bit 0 = advancedColorSupported
                hdrSupported = (info2.value & 0x8u) != 0 || (info2.value & 0x1u) != 0;
                hdrEnabled = info2.activeColorMode == NativeMethods.DISPLAYCONFIG_ADVANCED_COLOR_MODE_HDR;
            }
            else
            {
                var info1 = new NativeMethods.DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO
                {
                    header = new NativeMethods.DISPLAYCONFIG_DEVICE_INFO_HEADER
                    {
                        type = NativeMethods.DISPLAYCONFIG_DEVICE_INFO_GET_ADVANCED_COLOR_INFO,
                        size = Marshal.SizeOf<NativeMethods.DISPLAYCONFIG_GET_ADVANCED_COLOR_INFO>(),
                        adapterId = path.targetInfo.adapterId,
                        id = path.targetInfo.id
                    }
                };

                if (NativeMethods.DisplayConfigGetDeviceInfo(ref info1) == NativeMethods.ERROR_SUCCESS)
                {
                    hdrSupported = (info1.value & 0x1u) != 0;
                    hdrEnabled = (info1.value & 0x2u) != 0;
                }
            }

            int sdrNits = MinSdrNits;
            var white = new NativeMethods.DISPLAYCONFIG_GET_SDR_WHITE_LEVEL
            {
                header = new NativeMethods.DISPLAYCONFIG_DEVICE_INFO_HEADER
                {
                    type = NativeMethods.DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL,
                    size = Marshal.SizeOf<NativeMethods.DISPLAYCONFIG_GET_SDR_WHITE_LEVEL>(),
                    adapterId = path.targetInfo.adapterId,
                    id = path.targetInfo.id
                }
            };

            if (NativeMethods.DisplayConfigGetDeviceInfo(ref white) == NativeMethods.ERROR_SUCCESS && white.SDRWhiteLevel > 0)
            {
                sdrNits = Math.Clamp((int)Math.Round(white.SDRWhiteLevel * 80.0 / 1000.0), MinSdrNits, MaxSdrNits);
            }

            result.Add(new DisplayTarget
            {
                AdapterId = path.targetInfo.adapterId,
                TargetId = path.targetInfo.id,
                SourceId = path.sourceInfo.id,
                DeviceName = deviceName,
                HdrSupported = hdrSupported,
                HdrEnabled = hdrEnabled,
                SdrNits = sdrNits
            });
        }

        return result;
    }

    public static DisplayTarget? GetPrimaryHdrTarget()
    {
        var targets = GetTargets();
        if (targets.Count == 0)
            return null;

        var wantedDevice = MonitorCatalog.Resolve()?.DeviceName ?? MonitorCatalog.SelectedDevice;
        if (!string.IsNullOrWhiteSpace(wantedDevice))
        {
            var match = targets.FirstOrDefault(t => string.Equals(t.DeviceName, wantedDevice, StringComparison.OrdinalIgnoreCase));
            if (match is not null)
                return match;
        }

        return targets.FirstOrDefault(t => t.HdrSupported) ?? targets[0];
    }

    public static bool SetHdr(DisplayTarget target, bool enabled, int restoreBrightnessPercent = -1, Action? pulse = null)
    {
        LastError = string.Empty;
        bool wasEnabled = target.HdrEnabled;
        if (wasEnabled == enabled)
            return true;

        MonitorBrightness.Invalidate();

        // 1. Windows 11 24H2+ DISPLAYCONFIG_SET_HDR_STATE
        var setHdr = new NativeMethods.DISPLAYCONFIG_SET_HDR_STATE
        {
            header = new NativeMethods.DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = NativeMethods.DISPLAYCONFIG_DEVICE_INFO_SET_HDR_STATE,
                size = Marshal.SizeOf<NativeMethods.DISPLAYCONFIG_SET_HDR_STATE>(),
                adapterId = target.AdapterId,
                id = target.TargetId
            },
            enableHdr = enabled ? 1u : 0u
        };

        int hdrRc = NativeMethods.DisplayConfigSetDeviceInfo(ref setHdr);
        if (hdrRc == NativeMethods.ERROR_SUCCESS)
        {
            if (!enabled && restoreBrightnessPercent >= 0)
            {
                MonitorBrightness.ForceSetPercent(restoreBrightnessPercent);
                pulse?.Invoke();
            }
            Thread.Sleep(250);
            var after = GetPrimaryHdrTarget();
            if (after is not null && after.HdrEnabled == enabled)
                return true;
        }

        // 2. Legacy DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE
        var setAdv = new NativeMethods.DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE
        {
            header = new NativeMethods.DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = NativeMethods.DISPLAYCONFIG_DEVICE_INFO_SET_ADVANCED_COLOR_STATE,
                size = Marshal.SizeOf<NativeMethods.DISPLAYCONFIG_SET_ADVANCED_COLOR_STATE>(),
                adapterId = target.AdapterId,
                id = target.TargetId
            },
            enableAdvancedColorState = enabled ? 1u : 0u
        };

        int legacyRc = NativeMethods.DisplayConfigSetDeviceInfo(ref setAdv);
        if (legacyRc == NativeMethods.ERROR_SUCCESS)
        {
            if (!enabled && restoreBrightnessPercent >= 0)
            {
                MonitorBrightness.ForceSetPercent(restoreBrightnessPercent);
                pulse?.Invoke();
            }
            Thread.Sleep(250);
            var after = GetPrimaryHdrTarget();
            if (after is not null && after.HdrEnabled == enabled)
                return true;
        }

        // 3. Fallback Win+Alt+B
        SendHdrHotkey();
        if (!enabled && restoreBrightnessPercent >= 0)
        {
            Thread.Sleep(150);
            MonitorBrightness.ForceSetPercent(restoreBrightnessPercent);
            pulse?.Invoke();
        }
        Thread.Sleep(400);
        var finalTarget = GetPrimaryHdrTarget();
        if (finalTarget is not null && finalTarget.HdrEnabled == enabled)
            return true;

        LastError = $"HDR set hdrRc={hdrRc} legacyRc={legacyRc} (avant={wasEnabled} cible={enabled})";
        return false;
    }

    public static bool ToggleHdr(out bool nowEnabled)
    {
        LastError = string.Empty;
        var target = GetPrimaryHdrTarget();
        if (target is null)
        {
            LastError = "aucun écran détecté";
            nowEnabled = false;
            return false;
        }

        if (!target.HdrSupported)
        {
            LastError = "HDR non supporté sur cet écran";
            nowEnabled = false;
            return false;
        }

        bool want = !target.HdrEnabled;
        if (want)
            RememberSdrBrightness();

        int restore = !want ? _lastSdrHardwarePercent : -1;
        bool ok = SetHdr(target, want, restore);
        nowEnabled = IsHdrOn();
        return ok;
    }

    public static bool SetSdrNits(int nits)
    {
        nits = Math.Clamp(nits, MinSdrNits, MaxSdrNits);
        var target = GetPrimaryHdrTarget();
        if (target is null)
            return false;

        var setWhite = new NativeMethods.DISPLAYCONFIG_SET_SDR_WHITE_LEVEL
        {
            header = new NativeMethods.DISPLAYCONFIG_DEVICE_INFO_HEADER
            {
                type = NativeMethods.DISPLAYCONFIG_DEVICE_INFO_SET_SDR_WHITE_LEVEL,
                size = Marshal.SizeOf<NativeMethods.DISPLAYCONFIG_SET_SDR_WHITE_LEVEL>(),
                adapterId = target.AdapterId,
                id = target.TargetId
            },
            SDRWhiteLevel = (uint)Math.Round(nits * 1000.0 / 80.0),
            finalValue = 1
        };

        int rc = NativeMethods.DisplayConfigSetDeviceInfo(ref setWhite);

        try
        {
            if (!MonitorCatalog.TryGetHandle(out var hMon))
                hMon = NativeMethods.MonitorFromWindow(IntPtr.Zero, NativeMethods.MONITOR_DEFAULTTOPRIMARY);

            if (hMon != IntPtr.Zero)
                NativeMethods.DwmpSDRToHDRBoost(hMon, nits / 80.0);
        }
        catch
        {
            // DwmpSDRToHDRBoost is best-effort for instant visual update
        }

        return rc == NativeMethods.ERROR_SUCCESS;
    }

    public static string StatusText()
    {
        var t = GetPrimaryHdrTarget();
        if (t is null)
            return "Aucun écran détecté";

        if (t.HdrEnabled)
            return $"HDR ON | SDR {NitsToPercent(t.SdrNits)}%";

        return $"SDR | luminosité {GetBrightnessPercent()}%";
    }

    public static string StatusTextCached(out bool hdrOn, out int percent)
    {
        var t = GetPrimaryHdrTarget();
        if (t is null)
        {
            hdrOn = false;
            percent = 0;
            return "Aucun écran détecté";
        }

        hdrOn = t.HdrEnabled;
        if (hdrOn)
        {
            percent = NitsToPercent(t.SdrNits);
            return $"HDR ON | SDR {percent}%";
        }

        percent = GetBrightnessPercentCached();
        return $"SDR | luminosité {percent}%";
    }

    private static void SendHdrHotkey()
    {
        NativeMethods.keybd_event(NativeMethods.VK_LWIN, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_MENU, 0, 0, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_B, 0, 0, UIntPtr.Zero);
        Thread.Sleep(50);
        NativeMethods.keybd_event(NativeMethods.VK_B, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_MENU, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
        NativeMethods.keybd_event(NativeMethods.VK_LWIN, 0, NativeMethods.KEYEVENTF_KEYUP, UIntPtr.Zero);
    }
}
