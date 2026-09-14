using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace Hdr2Sdr
{
    internal sealed class AppConfig
    {
        public bool StartWithWindows = false;
        public bool ShowNotifications = false;
        public bool MinimizeToTray = false;
        // Windows Settings-style 0-100
        public int SdrStepPercent = 5;
        public int ProfileJeuSdrPercent = 80;
        public int ProfileSoirSdrPercent = 0;

        public bool HotkeysEnabled = true;
        // Hotkeys: Ctrl+Alt+...
        public string HotkeyToggleHdr = "Ctrl+Alt+H";
        public string HotkeySdrUp = "Ctrl+Alt+Up";
        public string HotkeySdrDown = "Ctrl+Alt+Down";
        public string HotkeyProfileJour = "Ctrl+Alt+1";
        public string HotkeyProfileSoir = "Ctrl+Alt+2";
        public string HotkeyProfileJeu = "Ctrl+Alt+3";

        public static string ConfigDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "hdr2sdr");
            }
        }

        public static string ConfigPath
        {
            get { return Path.Combine(ConfigDir, "config.ini"); }
        }

        public static AppConfig Load()
        {
            AppConfig cfg = new AppConfig();
            try
            {
                if (!File.Exists(ConfigPath))
                {
                    cfg.Save();
                    return cfg;
                }

                string[] lines = File.ReadAllLines(ConfigPath, Encoding.UTF8);
                for (int i = 0; i < lines.Length; i++)
                {
                    string line = lines[i].Trim();
                    if (line.Length == 0 || line[0] == '#' || line[0] == ';')
                        continue;
                    int eq = line.IndexOf('=');
                    if (eq <= 0)
                        continue;
                    string key = line.Substring(0, eq).Trim();
                    string val = line.Substring(eq + 1).Trim();
                    Apply(cfg, key, val);
                }
            }
            catch
            {
            }
            return cfg;
        }

        private static void Apply(AppConfig cfg, string key, string val)
        {
            string k = key.ToLowerInvariant();
            if (k == "startwithwindows")
                cfg.StartWithWindows = IsTrue(val);
            else if (k == "shownotifications")
                cfg.ShowNotifications = IsTrue(val);
            else if (k == "minimizetotray")
                cfg.MinimizeToTray = IsTrue(val);
            else if (k == "sdrsteppercent")
                cfg.SdrStepPercent = Clamp01(ParseInt(val, cfg.SdrStepPercent), 1, 50);
            else if (k == "profilejeusdrpercent")
                cfg.ProfileJeuSdrPercent = Clamp01(ParseInt(val, cfg.ProfileJeuSdrPercent), 0, 100);
            else if (k == "profilesoirsdrpercent")
                cfg.ProfileSoirSdrPercent = Clamp01(ParseInt(val, cfg.ProfileSoirSdrPercent), 0, 100);
            // Migrate old nits-based keys if present
            else if (k == "sdrstepnits")
                cfg.SdrStepPercent = Clamp01((int)Math.Round(ParseInt(val, 20) * 100.0 / 400.0), 1, 50);
            else if (k == "profilejeusdrnits")
                cfg.ProfileJeuSdrPercent = DisplayControl.NitsToPercent(ParseInt(val, 400));
            else if (k == "profilesoirsdrnits")
                cfg.ProfileSoirSdrPercent = DisplayControl.NitsToPercent(ParseInt(val, 160));
            else if (k == "hotkeysenabled")
                cfg.HotkeysEnabled = IsTrue(val);
            else if (k == "hotkeytogglehdr")
                cfg.HotkeyToggleHdr = val;
            else if (k == "hotkeysdrup")
                cfg.HotkeySdrUp = val;
            else if (k == "hotkeysdrdown")
                cfg.HotkeySdrDown = val;
            else if (k == "hotkeyprofilejour")
                cfg.HotkeyProfileJour = val;
            else if (k == "hotkeyprofilesoir")
                cfg.HotkeyProfileSoir = val;
            else if (k == "hotkeyprofilejeu")
                cfg.HotkeyProfileJeu = val;
        }

        private static int Clamp01(int v, int min, int max)
        {
            if (v < min) return min;
            if (v > max) return max;
            return v;
        }

        private static bool IsTrue(string val)
        {
            string v = val.ToLowerInvariant();
            return v == "1" || v == "true" || v == "yes" || v == "oui";
        }

        private static int ParseInt(string val, int fallback)
        {
            int n;
            if (int.TryParse(val, NumberStyles.Integer, CultureInfo.InvariantCulture, out n))
                return n;
            return fallback;
        }

        public void Save()
        {
            if (!Directory.Exists(ConfigDir))
                Directory.CreateDirectory(ConfigDir);

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# hdr2sdr config (SDR = 0-100 comme Settings Windows)");
            sb.AppendLine("StartWithWindows=" + (StartWithWindows ? "true" : "false"));
            sb.AppendLine("ShowNotifications=" + (ShowNotifications ? "true" : "false"));
            sb.AppendLine("MinimizeToTray=" + (MinimizeToTray ? "true" : "false"));
            sb.AppendLine("HotkeysEnabled=" + (HotkeysEnabled ? "true" : "false"));
            sb.AppendLine("SdrStepPercent=" + SdrStepPercent.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("ProfileJeuSdrPercent=" + ProfileJeuSdrPercent.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("ProfileSoirSdrPercent=" + ProfileSoirSdrPercent.ToString(CultureInfo.InvariantCulture));
            sb.AppendLine("HotkeyToggleHdr=" + HotkeyToggleHdr);
            sb.AppendLine("HotkeySdrUp=" + HotkeySdrUp);
            sb.AppendLine("HotkeySdrDown=" + HotkeySdrDown);
            sb.AppendLine("HotkeyProfileJour=" + HotkeyProfileJour);
            sb.AppendLine("HotkeyProfileSoir=" + HotkeyProfileSoir);
            sb.AppendLine("HotkeyProfileJeu=" + HotkeyProfileJeu);
            File.WriteAllText(ConfigPath, sb.ToString(), Encoding.UTF8);
        }
    }
}
