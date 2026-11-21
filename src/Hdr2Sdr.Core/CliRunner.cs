using System.Globalization;
using System.Runtime.Versioning;
using System.Text;

namespace Hdr2Sdr.Core;

[SupportedOSPlatform("windows")]
public static class CliRunner
{
    public static bool IsGuiLaunch(string[] args)
    {
        if (args.Length == 0)
            return true;

        var first = args[0].Trim().ToLowerInvariant();
        return first is "--tray" or "tray";
    }

    public static int Run(string[] args)
    {
        NativeMethods.AttachConsole(NativeMethods.ATTACH_PARENT_PROCESS);

        var cfg = AppConfig.Load();
        MonitorCatalog.SelectedDevice = cfg.MonitorDevice;

        var cmd = args[0].Trim().ToLowerInvariant();
        bool stateChanged = false;
        int exitCode = 0;

        switch (cmd)
        {
            case "version":
            case "-v":
            case "--version":
            {
                var exe = Environment.ProcessPath ?? "?";
                WriteOutput($"hdr2sdr {BuildInfo.ShortVersion} ({exe})");
                break;
            }

            case "status":
            {
                WriteOutput(DisplayControl.StatusText());
                break;
            }

            case "toggle":
            case "hdr-toggle":
            {
                if (!DisplayControl.ToggleHdr(out var nowEnabled))
                {
                    WriteOutput($"ERROR: unable to toggle HDR | {DisplayControl.LastError}");
                    exitCode = 1;
                }
                else
                {
                    WriteOutput(nowEnabled ? "HDR ON" : "HDR OFF");
                    stateChanged = true;
                }
                break;
            }

            case "on":
            case "hdr-on":
            {
                var target = DisplayControl.GetPrimaryHdrTarget();
                if (target is null || !target.HdrSupported)
                {
                    WriteOutput("ERROR: HDR not supported");
                    exitCode = 1;
                }
                else
                {
                    DisplayControl.RememberSdrBrightness();
                    if (!DisplayControl.SetHdr(target, enabled: true))
                    {
                        WriteOutput($"ERROR: set HDR on failed | {DisplayControl.LastError}");
                        exitCode = 1;
                    }
                    else
                    {
                        WriteOutput("HDR ON");
                        stateChanged = true;
                    }
                }
                break;
            }

            case "off":
            case "hdr-off":
            {
                var target = DisplayControl.GetPrimaryHdrTarget();
                if (target is null)
                {
                    WriteOutput("ERROR: no display");
                    exitCode = 1;
                }
                else if (!DisplayControl.LeaveHdrRestoringBrightness(out _))
                {
                    WriteOutput($"ERROR: set HDR off failed | {DisplayControl.LastError}");
                    exitCode = 1;
                }
                else
                {
                    WriteOutput("HDR OFF");
                    stateChanged = true;
                }
                break;
            }

            case "sdr":
            case "lum":
            case "brightness":
            {
                if (args.Length < 2)
                {
                    WriteOutput("Usage: hdr2sdr sdr <0-100|up|down>");
                    exitCode = 1;
                    break;
                }

                var sub = args[1].Trim().ToLowerInvariant();
                bool hdrOn = DisplayControl.IsHdrOn();
                string prefix = hdrOn ? "SDR" : "Luminosité";

                if (sub == "up")
                {
                    int v = DisplayControl.AdjustBrightnessPercent(cfg.SdrStepPercent);
                    WriteOutput($"{prefix} {v}%");
                    stateChanged = true;
                }
                else if (sub == "down")
                {
                    int v = DisplayControl.AdjustBrightnessPercent(-cfg.SdrStepPercent);
                    WriteOutput($"{prefix} {v}%");
                    stateChanged = true;
                }
                else if (int.TryParse(sub, NumberStyles.Integer, CultureInfo.InvariantCulture, out int pct) && pct is >= 0 and <= 100)
                {
                    if (!DisplayControl.SetBrightnessPercent(pct))
                    {
                        WriteOutput($"ERROR: {(string.IsNullOrEmpty(DisplayControl.LastError) ? "set brightness failed" : DisplayControl.LastError)}");
                        exitCode = 1;
                    }
                    else
                    {
                        WriteOutput($"{prefix} {pct}%");
                        stateChanged = true;
                    }
                }
                else
                {
                    WriteOutput("ERROR: invalid percent (0-100)");
                    exitCode = 1;
                }
                break;
            }

            case "profile":
            case "profil":
            {
                if (args.Length < 2)
                {
                    WriteOutput("Usage: hdr2sdr profile jour|soir|jeu");
                    exitCode = 1;
                    break;
                }

                var name = args[1].Trim().ToLowerInvariant();
                bool ok;
                string msg;
                switch (name)
                {
                    case "jour":
                    case "day":
                        ok = Profiles.ApplyJour(out msg);
                        break;
                    case "soir":
                    case "night":
                    case "evening":
                        ok = Profiles.ApplySoir(cfg, out msg);
                        break;
                    case "jeu":
                    case "game":
                        ok = Profiles.ApplyJeu(cfg, out msg);
                        break;
                    default:
                        WriteOutput("ERROR: unknown profile");
                        return 1;
                }

                WriteOutput(msg);
                if (ok) stateChanged = true;
                else exitCode = 1;
                break;
            }

            default:
            {
                WriteOutput(
                    "hdr2sdr commands:\n" +
                    "  version             show version + exe path\n" +
                    "  (no args)           start GUI / tray app\n" +
                    "  --tray              start minimized in tray\n" +
                    "  status              show HDR/SDR state\n" +
                    "  toggle | on | off   HDR control\n" +
                    "  sdr <0-100|up|down> SDR content brightness (HDR) or DDC/CI (SDR)\n" +
                    "  profile jour|soir|jeu");
                break;
            }
        }

        if (stateChanged)
        {
            // Notify running GUI instance (if any) to sync immediately
            uint syncMsg = NativeMethods.RegisterWindowMessage(BuildInfo.SyncStateMessageName);
            if (syncMsg != 0)
                NativeMethods.PostMessage(NativeMethods.HWND_BROADCAST, syncMsg, IntPtr.Zero, IntPtr.Zero);
        }

        return exitCode;
    }

    private static void WriteOutput(string text)
    {
        try
        {
            Console.WriteLine(text);
        }
        catch
        {
            // No attached console
        }

        try
        {
            Directory.CreateDirectory(AppPaths.DataDir);
            File.WriteAllText(AppPaths.CliOutputLogPath, text + Environment.NewLine, Encoding.UTF8);
        }
        catch
        {
            // Ignore log write failures
        }
    }
}
