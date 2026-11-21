using System.Runtime.Versioning;

namespace Hdr2Sdr.Core;

[SupportedOSPlatform("windows")]
public static class Profiles
{
    public static bool ApplyJour(out string message)
    {
        bool ok = DisplayControl.LeaveHdrRestoringBrightness(out _);
        if (!ok)
        {
            message = $"ERROR: désactivation HDR impossible ({DisplayControl.LastError})";
            return false;
        }

        message = "Profil Jour : HDR OFF (SDR natif)";
        return true;
    }

    public static bool ApplySoir(AppConfig cfg, out string message)
    {
        return ApplyHdrWithSdrPercent("Soir", cfg.ProfileSoirSdrPercent, out message);
    }

    public static bool ApplyJeu(AppConfig cfg, out string message)
    {
        return ApplyHdrWithSdrPercent("Jeu", cfg.ProfileJeuSdrPercent, out message);
    }

    private static bool ApplyHdrWithSdrPercent(string profileName, int sdrPercent, out string message)
    {
        var target = DisplayControl.GetPrimaryHdrTarget();
        if (target is null)
        {
            message = "ERROR: activation HDR impossible (aucun écran détecté)";
            return false;
        }

        if (!target.HdrSupported)
        {
            message = "ERROR: activation HDR impossible (HDR non supporté sur cet écran)";
            return false;
        }

        if (!target.HdrEnabled)
        {
            DisplayControl.RememberSdrBrightness();
            if (!DisplayControl.SetHdr(target, enabled: true))
            {
                message = $"ERROR: activation HDR impossible ({DisplayControl.LastError})";
                return false;
            }
        }

        int clamped = Math.Clamp(sdrPercent, 0, 100);
        if (!DisplayControl.SetSdrPercent(clamped))
        {
            message = "ERROR: réglage SDR impossible";
            return false;
        }

        message = $"Profil {profileName} : HDR ON + SDR {clamped}%";
        return true;
    }
}
