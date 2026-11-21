using System.Globalization;
using System.Runtime.Versioning;
using System.Text;

namespace Hdr2Sdr.Core;

[SupportedOSPlatform("windows")]
public sealed class AppConfig
{
    public bool StartWithWindows { get; set; } = false;
    public bool ShowNotifications { get; set; } = false;
    public bool MinimizeToTray { get; set; } = false;
    public bool StartMinimized { get; set; } = false;
    public string Theme { get; set; } = "dark";
    public string MonitorDevice { get; set; } = string.Empty;

    public int SdrStepPercent { get; set; } = 5;
    public int ProfileJeuSdrPercent { get; set; } = 80;
    public int ProfileSoirSdrPercent { get; set; } = 0;

    public bool HotkeysEnabled { get; set; } = true;
    public string HotkeyToggleHdr { get; set; } = "Ctrl+Alt+H";
    public string HotkeySdrUp { get; set; } = "Ctrl+Alt+Up";
    public string HotkeySdrDown { get; set; } = "Ctrl+Alt+Down";
    public string HotkeyProfileJour { get; set; } = "Ctrl+Alt+1";
    public string HotkeyProfileSoir { get; set; } = "Ctrl+Alt+2";
    public string HotkeyProfileJeu { get; set; } = "Ctrl+Alt+3";

    public bool IsLightTheme => string.Equals(Theme, "light", StringComparison.OrdinalIgnoreCase);

    public static AppConfig Load()
    {
        var cfg = new AppConfig();
        var path = AppPaths.ConfigPath;
        if (!File.Exists(path))
            return cfg;

        try
        {
            foreach (var rawLine in File.ReadAllLines(path, Encoding.UTF8))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
                    continue;

                var eq = line.IndexOf('=');
                if (eq <= 0)
                    continue;

                var key = line[..eq].Trim();
                var val = line[(eq + 1)..].Trim();
                Apply(cfg, key, val);
            }
        }
        catch
        {
            // Keep defaults on unreadable config
        }

        return cfg;
    }

    public void Save()
    {
        try
        {
            var dir = AppPaths.DataDir;
            Directory.CreateDirectory(dir);

            var sb = new StringBuilder();
            sb.AppendLine("# hdr2sdr config (SDR = 0-100 comme Settings Windows)");
            sb.AppendLine($"StartWithWindows={BoolStr(StartWithWindows)}");
            sb.AppendLine($"Theme={(IsLightTheme ? "light" : "dark")}");
            sb.AppendLine($"MonitorDevice={MonitorDevice}");
            sb.AppendLine($"ShowNotifications={BoolStr(ShowNotifications)}");
            sb.AppendLine($"MinimizeToTray={BoolStr(MinimizeToTray)}");
            sb.AppendLine($"StartMinimized={BoolStr(StartMinimized)}");
            sb.AppendLine($"HotkeysEnabled={BoolStr(HotkeysEnabled)}");
            sb.AppendLine($"SdrStepPercent={Math.Clamp(SdrStepPercent, 1, 25)}");
            sb.AppendLine($"ProfileJeuSdrPercent={Math.Clamp(ProfileJeuSdrPercent, 0, 100)}");
            sb.AppendLine($"ProfileSoirSdrPercent={Math.Clamp(ProfileSoirSdrPercent, 0, 100)}");
            sb.AppendLine($"HotkeyToggleHdr={HotkeyToggleHdr}");
            sb.AppendLine($"HotkeySdrUp={HotkeySdrUp}");
            sb.AppendLine($"HotkeySdrDown={HotkeySdrDown}");
            sb.AppendLine($"HotkeyProfileJour={HotkeyProfileJour}");
            sb.AppendLine($"HotkeyProfileSoir={HotkeyProfileSoir}");
            sb.AppendLine($"HotkeyProfileJeu={HotkeyProfileJeu}");

            File.WriteAllText(AppPaths.ConfigPath, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch
        {
            // Ignore write errors
        }
    }

    public void ResetHotkeysToDefaults()
    {
        HotkeyToggleHdr = "Ctrl+Alt+H";
        HotkeySdrUp = "Ctrl+Alt+Up";
        HotkeySdrDown = "Ctrl+Alt+Down";
        HotkeyProfileJour = "Ctrl+Alt+1";
        HotkeyProfileSoir = "Ctrl+Alt+2";
        HotkeyProfileJeu = "Ctrl+Alt+3";
    }

    private static void Apply(AppConfig cfg, string key, string val)
    {
        switch (key.ToLowerInvariant())
        {
            case "startwithwindows":
                cfg.StartWithWindows = IsTrue(val);
                break;
            case "theme":
                cfg.Theme = string.Equals(val, "light", StringComparison.OrdinalIgnoreCase) ? "light" : "dark";
                break;
            case "monitordevice":
                cfg.MonitorDevice = val;
                break;
            case "shownotifications":
                cfg.ShowNotifications = IsTrue(val);
                break;
            case "minimizetotray":
                cfg.MinimizeToTray = IsTrue(val);
                break;
            case "startminimized":
                cfg.StartMinimized = IsTrue(val);
                break;
            case "sdrsteppercent":
                cfg.SdrStepPercent = Math.Clamp(ParseInt(val, 5), 1, 25);
                break;
            case "profilejeusdrpercent":
                cfg.ProfileJeuSdrPercent = Math.Clamp(ParseInt(val, 80), 0, 100);
                break;
            case "profilesoirsdrpercent":
                cfg.ProfileSoirSdrPercent = Math.Clamp(ParseInt(val, 0), 0, 100);
                break;
            case "sdrstepnits":
                cfg.SdrStepPercent = Math.Clamp(ParseInt(val, 20) / 4, 1, 25);
                break;
            case "profilejeusdrnits":
                cfg.ProfileJeuSdrPercent = DisplayControl.NitsToPercent(ParseInt(val, 400));
                break;
            case "profilesoirsdrnits":
                cfg.ProfileSoirSdrPercent = DisplayControl.NitsToPercent(ParseInt(val, 80));
                break;
            case "hotkeysenabled":
                cfg.HotkeysEnabled = IsTrue(val);
                break;
            case "hotkeytogglehdr":
                cfg.HotkeyToggleHdr = val;
                break;
            case "hotkeysdrup":
                cfg.HotkeySdrUp = val;
                break;
            case "hotkeysdrdown":
                cfg.HotkeySdrDown = val;
                break;
            case "hotkeyprofilejour":
                cfg.HotkeyProfileJour = val;
                break;
            case "hotkeyprofilesoir":
                cfg.HotkeyProfileSoir = val;
                break;
            case "hotkeyprofilejeu":
                cfg.HotkeyProfileJeu = val;
                break;
        }
    }

    private static string BoolStr(bool v) => v ? "true" : "false";

    private static bool IsTrue(string val) =>
        val.Equals("1", StringComparison.OrdinalIgnoreCase)
        || val.Equals("true", StringComparison.OrdinalIgnoreCase)
        || val.Equals("yes", StringComparison.OrdinalIgnoreCase)
        || val.Equals("oui", StringComparison.OrdinalIgnoreCase);

    private static int ParseInt(string val, int fallback) =>
        int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed) ? parsed : fallback;
}
