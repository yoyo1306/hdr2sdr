using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace Hdr2Sdr
{
    /// <summary>Version affichée (titre, tooltip tray, CLI `version`). À bumper à chaque release.</summary>
    internal static class BuildInfo
    {
        public const string Version = "1.2";
        public const string Build = "2026-09-27";
        public const string ShortVersion = "v1.2";
    }

    internal static class Program
    {
        private static System.Threading.Mutex _instanceMutex;

        [STAThread]
        private static int Main(string[] args)
        {
            if (args != null && args.Length > 0)
            {
                string cmd = args[0].Trim().ToLowerInvariant();
                if (cmd != "--tray" && cmd != "tray")
                {
                    EnsureCliOutput();
                    return RunCli(args);
                }
                // "tray" / "--tray" : tombe dans le chemin GUI protégé par mutex ci-dessous.
            }

            // Default: tray app (no console window — winexe).
            // Une seule instance GUI : la 2e se ferme sans rien afficher
            // (sinon deux trays, hotkeys en conflit et DDC instable).
            bool createdNew = true;
            try
            {
                _instanceMutex = new System.Threading.Mutex(true, "hdr2sdr-single-instance", out createdNew);
            }
            catch (System.Threading.AbandonedMutexException)
            {
                createdNew = true;
            }
            catch (Exception)
            {
                createdNew = true;
            }
            if (!createdNew)
            {
                if (_instanceMutex != null)
                {
                    try { _instanceMutex.Close(); }
                    catch { }
                    _instanceMutex = null;
                }
                return 0;
            }

            int rc = RunTray();
            System.GC.KeepAlive(_instanceMutex);
            if (_instanceMutex != null)
            {
                try { _instanceMutex.ReleaseMutex(); }
                catch { }
                try { _instanceMutex.Close(); }
                catch { }
                _instanceMutex = null;
            }
            return rc;
        }

        private static void EnsureCliOutput()
        {
            try
            {
                // If launched from a terminal, attach to it.
                // If launched via .vbs/shortcut (no console), stay silent.
                if (AttachConsole(ATTACH_PARENT_PROCESS))
                {
                    Stream stdout = Console.OpenStandardOutput();
                    Encoding enc;
                    try { enc = Console.OutputEncoding; }
                    catch { enc = Encoding.UTF8; }
                    Console.SetOut(new StreamWriter(stdout, enc) { AutoFlush = true });
                }
            }
            catch
            {
            }
        }

        private static int RunTray()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            AppConfig cfg = AppConfig.Load();
            MonitorCatalog.SelectedDevice = cfg.MonitorDevice ?? "";
            ModernTheme.SetTheme(string.Equals(cfg.Theme, "light", StringComparison.OrdinalIgnoreCase) ? AppTheme.Light : AppTheme.Dark);
            if (cfg.StartWithWindows && !StartupHelper.IsEnabled())
                StartupHelper.SetEnabled(true);
            StartMenuHelper.InstallOrUpdate();
            Application.Run(new MainForm(cfg));
            return 0;
        }

        private static int RunCli(string[] args)
        {
            string cmd = args[0].Trim().ToLowerInvariant();
            try { MonitorCatalog.SelectedDevice = AppConfig.Load().MonitorDevice ?? ""; }
            catch { }

            if (cmd == "version" || cmd == "-v" || cmd == "--version")
            {
                string exe;
                try { exe = System.Reflection.Assembly.GetExecutingAssembly().Location; }
                catch { exe = "?"; }
                EnsureCliOutput();
                ConsoleWrite("hdr2sdr " + BuildInfo.ShortVersion + " (build " + BuildInfo.Build + ")");
                ConsoleWrite(exe);
                return 0;
            }

            if (cmd == "status")
            {
                ConsoleWrite(DisplayControl.StatusText());
                return 0;
            }

            if (cmd == "toggle" || cmd == "hdr-toggle")
            {
                if (DisplayControl.IsHdrOn())
                {
                    int pct;
                    if (!DisplayControl.LeaveHdrRestoringBrightness(out pct))
                    {
                        ConsoleWrite("ERROR: unable to toggle HDR" + (DisplayControl.LastError.Length > 0 ? " | " + DisplayControl.LastError : ""));
                        return 1;
                    }
                    ConsoleWrite("HDR OFF");
                    return 0;
                }
                DisplayControl.RememberSdrBrightness();
                bool on;
                if (!DisplayControl.ToggleHdr(out on))
                {
                    ConsoleWrite("ERROR: unable to toggle HDR" + (DisplayControl.LastError.Length > 0 ? " | " + DisplayControl.LastError : ""));
                    return 1;
                }
                ConsoleWrite(on ? "HDR ON" : "HDR OFF");
                return 0;
            }

            if (cmd == "on" || cmd == "hdr-on")
            {
                DisplayTarget t = DisplayControl.GetPrimaryHdrTarget();
                if (t == null || !t.HdrSupported)
                {
                    ConsoleWrite("ERROR: HDR not supported");
                    return 1;
                }
                if (!t.HdrEnabled)
                    DisplayControl.RememberSdrBrightness();
                if (!DisplayControl.SetHdr(t, true))
                {
                    ConsoleWrite("ERROR: set HDR on failed");
                    return 1;
                }
                ConsoleWrite("HDR ON");
                return 0;
            }

            if (cmd == "off" || cmd == "hdr-off")
            {
                DisplayTarget t = DisplayControl.GetPrimaryHdrTarget();
                if (t == null)
                {
                    ConsoleWrite("ERROR: no display");
                    return 1;
                }
                int pct;
                if (!DisplayControl.LeaveHdrRestoringBrightness(out pct))
                {
                    ConsoleWrite("ERROR: set HDR off failed");
                    return 1;
                }
                ConsoleWrite("HDR OFF");
                return 0;
            }

            if (cmd == "sdr" || cmd == "lum" || cmd == "brightness")
            {
                if (args.Length < 2)
                {
                    ConsoleWrite("Usage: hdr2sdr sdr <0-100|up|down>");
                    return 1;
                }
                string arg = args[1].Trim().ToLowerInvariant();
                AppConfig cfg = AppConfig.Load();
                if (arg == "up")
                {
                    int n = DisplayControl.AdjustBrightnessPercent(cfg.SdrStepPercent);
                    ConsoleWrite((DisplayControl.IsHdrOn() ? "SDR " : "Luminosit\u00e9 ") + n + "%");
                    return DisplayControl.LastError.Length > 0 ? 1 : 0;
                }
                if (arg == "down")
                {
                    int n = DisplayControl.AdjustBrightnessPercent(-cfg.SdrStepPercent);
                    ConsoleWrite((DisplayControl.IsHdrOn() ? "SDR " : "Luminosit\u00e9 ") + n + "%");
                    return DisplayControl.LastError.Length > 0 ? 1 : 0;
                }
                int percent;
                if (!int.TryParse(args[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out percent))
                {
                    ConsoleWrite("ERROR: invalid percent (0-100)");
                    return 1;
                }
                if (!DisplayControl.SetBrightnessPercent(percent))
                {
                    ConsoleWrite("ERROR: " + (DisplayControl.LastError.Length > 0 ? DisplayControl.LastError : "set brightness failed"));
                    return 1;
                }
                ConsoleWrite((DisplayControl.IsHdrOn() ? "SDR " : "Luminosit\u00e9 ") + DisplayControl.GetBrightnessPercent() + "%");
                return 0;
            }

            if (cmd == "profile" || cmd == "profil")
            {
                if (args.Length < 2)
                {
                    ConsoleWrite("Usage: hdr2sdr profile jour|soir|jeu");
                    return 1;
                }
                AppConfig cfg = AppConfig.Load();
                string p = args[1].Trim().ToLowerInvariant();
                string msg;
                bool ok;
                if (p == "jour" || p == "day")
                    ok = Profiles.ApplyJour(out msg);
                else if (p == "soir" || p == "night" || p == "evening")
                    ok = Profiles.ApplySoir(cfg, out msg);
                else if (p == "jeu" || p == "game")
                    ok = Profiles.ApplyJeu(cfg, out msg);
                else
                {
                    ConsoleWrite("ERROR: unknown profile");
                    return 1;
                }
                ConsoleWrite(msg);
                return ok ? 0 : 1;
            }

            ConsoleWrite("hdr2sdr commands:");
            ConsoleWrite("  version             show version + exe path");
            ConsoleWrite("  (no args)           start tray app");
            ConsoleWrite("  status              show HDR/SDR state");
            ConsoleWrite("  toggle | on | off   HDR control");
            ConsoleWrite("  sdr <0-100|up|down> SDR content brightness (comme Settings)");
            ConsoleWrite("  profile jour|soir|jeu");
            return 1;
        }

        private static void ConsoleWrite(string text)
        {
            try
            {
                Console.WriteLine(text);
            }
            catch
            {
            }

            try
            {
                string path = Path.Combine(Path.GetTempPath(), "hdr2sdr-last.txt");
                File.WriteAllText(path, text ?? string.Empty, Encoding.UTF8);
            }
            catch
            {
            }
        }

        private const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;

        [System.Runtime.InteropServices.DllImport("kernel32.dll")]
        private static extern bool AttachConsole(uint dwProcessId);
    }
}
