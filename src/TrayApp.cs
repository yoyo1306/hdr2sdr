using System;
using System.Drawing;
using System.Windows.Forms;

namespace Hdr2Sdr
{
    internal static class Profiles
    {
        public static string ApplyJour()
        {
            DisplayTarget t = DisplayControl.GetPrimaryHdrTarget();
            if (t != null && t.HdrEnabled)
                DisplayControl.SetHdr(t, false);
            return "Profil Jour: HDR OFF (SDR natif)";
        }

        public static string ApplySoir(AppConfig cfg)
        {
            DisplayTarget t = DisplayControl.GetPrimaryHdrTarget();
            if (t != null && t.HdrSupported && !t.HdrEnabled)
                DisplayControl.SetHdr(t, true);
            DisplayControl.SetSdrPercent(cfg.ProfileSoirSdrPercent);
            return "Profil Soir: HDR ON + SDR " + cfg.ProfileSoirSdrPercent + "%";
        }

        public static string ApplyJeu(AppConfig cfg)
        {
            DisplayTarget t = DisplayControl.GetPrimaryHdrTarget();
            if (t != null && t.HdrSupported && !t.HdrEnabled)
                DisplayControl.SetHdr(t, true);
            DisplayControl.SetSdrPercent(cfg.ProfileJeuSdrPercent);
            return "Profil Jeu: HDR ON + SDR " + cfg.ProfileJeuSdrPercent + "%";
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly AppConfig _cfg;
        private readonly HotkeyWindow _hotkeys;
        private readonly NotifyIcon _tray;
        private readonly ContextMenuStrip _trayMenu;
        private readonly Label _status;
        private readonly TrackBar _brightness;
        private readonly Button _btnToggle;
        private readonly ToolStripMenuItem _trayToggleItem;
        private readonly Timer _brightApplyTimer;
        private bool _allowClose;
        private bool _updatingUi;
        private bool _hdrStateKnown;
        private bool _hdrOnCached;
        private int _pendingBright = -1;

        private const int HK_TOGGLE = 1;
        private const int HK_SDR_UP = 2;
        private const int HK_SDR_DOWN = 3;
        private const int HK_JOUR = 4;
        private const int HK_SOIR = 5;
        private const int HK_JEU = 6;

        public MainForm(AppConfig cfg)
        {
            _cfg = cfg;

            Text = "hdr2sdr";
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(360, 250);
            ShowInTaskbar = true;
            Icon = LoadIcon();

            _status = new Label();
            _status.Name = "status";
            _status.AutoSize = false;
            _status.SetBounds(16, 16, 328, 40);
            _status.Text = DisplayControl.StatusText();

            _btnToggle = new Button();
            _btnToggle.SetBounds(16, 70, 140, 32);
            _btnToggle.Click += delegate
            {
                bool on;
                DisplayControl.ToggleHdr(out on);
                RefreshStatus();
            };

            Button btnUp = new Button();
            btnUp.Text = "Lum +";
            btnUp.SetBounds(168, 70, 80, 32);
            btnUp.Click += delegate
            {
                DisplayControl.AdjustBrightnessPercent(_cfg.SdrStepPercent);
                RefreshStatus();
            };

            Button btnDown = new Button();
            btnDown.Text = "Lum -";
            btnDown.SetBounds(260, 70, 80, 32);
            btnDown.Click += delegate
            {
                DisplayControl.AdjustBrightnessPercent(-_cfg.SdrStepPercent);
                RefreshStatus();
            };

            Button btnJour = new Button();
            btnJour.Text = "Profil Jour";
            btnJour.SetBounds(16, 118, 100, 32);
            btnJour.Click += delegate { Profiles.ApplyJour(); RefreshStatus(); };

            Button btnSoir = new Button();
            btnSoir.Text = "Profil Soir";
            btnSoir.SetBounds(130, 118, 100, 32);
            btnSoir.Click += delegate { Profiles.ApplySoir(_cfg); RefreshStatus(); };

            Button btnJeu = new Button();
            btnJeu.Text = "Profil Jeu";
            btnJeu.SetBounds(244, 118, 96, 32);
            btnJeu.Click += delegate { Profiles.ApplyJeu(_cfg); RefreshStatus(); };

            _brightness = new TrackBar();
            _brightness.Name = "sdr";
            _brightness.Minimum = 0;
            _brightness.Maximum = 100;
            _brightness.TickFrequency = 10;
            _brightness.SetBounds(16, 165, 288, 45);
            _brightness.Value = Math.Max(0, Math.Min(100, DisplayControl.GetBrightnessPercent()));
            _brightness.Scroll += delegate
            {
                if (_updatingUi) return;
                DisplayControl.SetBrightnessPercent(_brightness.Value);
                RefreshStatus(false);
            };

            Button btnGear = new Button();
            btnGear.Text = "\u2699"; // ⚙
            btnGear.Font = new Font("Segoe UI Symbol", 14f, FontStyle.Regular);
            btnGear.SetBounds(312, 200, 36, 32);
            btnGear.FlatStyle = FlatStyle.Flat;
            btnGear.FlatAppearance.BorderSize = 0;
            btnGear.Cursor = Cursors.Hand;
            btnGear.Click += delegate { OpenOptions(); };

            Controls.Add(_status);
            Controls.Add(_btnToggle);
            Controls.Add(btnUp);
            Controls.Add(btnDown);
            Controls.Add(btnJour);
            Controls.Add(btnSoir);
            Controls.Add(btnJeu);
            Controls.Add(_brightness);
            Controls.Add(btnGear);

            _trayMenu = new ContextMenuStrip();
            _trayMenu.Items.Add("Ouvrir", null, delegate { RestoreFromTray(); });
            _trayToggleItem = new ToolStripMenuItem("Basculer HDR");
            _trayToggleItem.Click += delegate { DoToggleHdr(); };
            _trayMenu.Items.Add(_trayToggleItem);
            _trayMenu.Items.Add(new ToolStripSeparator());
            _trayMenu.Items.Add("Quitter", null, delegate { QuitApp(); });

            _tray = new NotifyIcon();
            _tray.Icon = Icon;
            _tray.Text = "hdr2sdr";
            _tray.Visible = false;
            _tray.ContextMenuStrip = _trayMenu;
            _tray.DoubleClick += delegate { RestoreFromTray(); };
            _tray.MouseUp += delegate(object sender, MouseEventArgs e)
            {
                if (e.Button == MouseButtons.Left)
                    RestoreFromTray();
            };

            _hotkeys = new HotkeyWindow();
            _hotkeys.HotkeyPressed += OnHotkey;
            RegisterHotkeys();

            _brightApplyTimer = new Timer();
            _brightApplyTimer.Interval = 50;
            _brightApplyTimer.Tick += BrightApplyTimer_Tick;

            Resize += MainForm_Resize;
            FormClosing += MainForm_FormClosing;
            RefreshStatus();
        }

        private void OpenOptions()
        {
            using (OptionsForm dlg = new OptionsForm(_cfg))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK)
                    return;
            }

            if (!_cfg.MinimizeToTray)
                HideTrayIcon();

            UnregisterHotkeys();
            RegisterHotkeys();
        }

        private void MainForm_Resize(object sender, EventArgs e)
        {
            if (WindowState != FormWindowState.Minimized)
                return;
            if (!_cfg.MinimizeToTray)
                return;

            Hide();
            ShowInTaskbar = false;
            _tray.Visible = true;
            string tip = DisplayControl.StatusText();
            if (tip.Length > 60) tip = tip.Substring(0, 60);
            _tray.Text = tip;
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_allowClose)
                return;

            _allowClose = true;
            _brightApplyTimer.Stop();
            _brightApplyTimer.Dispose();
            FlushPendingBrightness();
            UnregisterHotkeys();
            _hotkeys.Dispose();
            MonitorBrightness.Invalidate();
            _tray.Visible = false;
            _tray.Dispose();
        }

        private void RestoreFromTray()
        {
            ShowInTaskbar = true;
            Show();
            WindowState = FormWindowState.Normal;
            HideTrayIcon();
            BringToFront();
            Activate();
            RefreshStatus();
        }

        private void HideTrayIcon()
        {
            _tray.Visible = false;
        }

        private void QuitApp()
        {
            Close();
        }

        private void UnregisterHotkeys()
        {
            for (int i = 1; i <= 6; i++)
                _hotkeys.Unregister(i);
        }

        private void RegisterHotkeys()
        {
            UnregisterHotkeys();
            if (!_cfg.HotkeysEnabled)
                return;

            TryRegister(HK_TOGGLE, _cfg.HotkeyToggleHdr, false);
            TryRegister(HK_SDR_UP, _cfg.HotkeySdrUp, true);
            TryRegister(HK_SDR_DOWN, _cfg.HotkeySdrDown, true);
            TryRegister(HK_JOUR, _cfg.HotkeyProfileJour, false);
            TryRegister(HK_SOIR, _cfg.HotkeyProfileSoir, false);
            TryRegister(HK_JEU, _cfg.HotkeyProfileJeu, false);
        }

        private void TryRegister(int id, string chord, bool allowRepeat)
        {
            if (string.IsNullOrEmpty(chord))
                return;
            _hotkeys.Register(id, chord, allowRepeat);
        }

        private void OnHotkey(int id)
        {
            if (!_cfg.HotkeysEnabled)
                return;

            if (id == HK_TOGGLE) DoToggleHdr();
            else if (id == HK_SDR_UP)
                NudgeBrightnessHold(_cfg.SdrStepPercent);
            else if (id == HK_SDR_DOWN)
                NudgeBrightnessHold(-_cfg.SdrStepPercent);
            else if (id == HK_JOUR) { Profiles.ApplyJour(); RefreshStatus(); }
            else if (id == HK_SOIR) { Profiles.ApplySoir(_cfg); RefreshStatus(); }
            else if (id == HK_JEU) { Profiles.ApplyJeu(_cfg); RefreshStatus(); }
        }

        private void NudgeBrightnessHold(int delta)
        {
            int cur = _pendingBright >= 0 ? _pendingBright : _brightness.Value;
            int next = cur + delta;
            if (next < 0) next = 0;
            if (next > 100) next = 100;
            _pendingBright = next;

            _updatingUi = true;
            try
            {
                if (_brightness.Value != next)
                    _brightness.Value = next;
                UpdateBrightnessStatusText(next);
            }
            finally
            {
                _updatingUi = false;
            }

            if (!_brightApplyTimer.Enabled)
                _brightApplyTimer.Start();
        }

        private void BrightApplyTimer_Tick(object sender, EventArgs e)
        {
            if (_pendingBright < 0)
            {
                _brightApplyTimer.Stop();
                return;
            }

            int value = _pendingBright;
            DisplayControl.SetBrightnessPercent(value);
            if (_pendingBright == value)
                _pendingBright = -1;
            UpdateBrightnessStatusText(value);
        }

        private void FlushPendingBrightness()
        {
            if (_pendingBright < 0)
                return;
            int value = _pendingBright;
            _pendingBright = -1;
            DisplayControl.SetBrightnessPercent(value);
            UpdateBrightnessStatusText(value);
        }

        private void UpdateBrightnessStatusText(int percent)
        {
            if (!_hdrStateKnown)
            {
                _hdrOnCached = DisplayControl.IsHdrOn();
                _hdrStateKnown = true;
            }
            _status.Text = _hdrOnCached
                ? ("HDR ON | SDR " + percent + "%")
                : ("SDR | luminosit\u00e9 " + percent + "%");
            string toggleLabel = _hdrOnCached ? "Basculer SDR" : "Basculer HDR";
            _btnToggle.Text = toggleLabel;
            _trayToggleItem.Text = toggleLabel;
        }

        private void DoToggleHdr()
        {
            FlushPendingBrightness();
            bool enabled;
            DisplayControl.ToggleHdr(out enabled);
            _hdrStateKnown = false;
            RefreshStatus();
        }

        private void RefreshStatus()
        {
            RefreshStatus(true);
        }

        private void RefreshStatus(bool syncSlider)
        {
            FlushPendingBrightness();
            string text = DisplayControl.StatusText();
            _status.Text = text;
            _hdrOnCached = DisplayControl.IsHdrOn();
            _hdrStateKnown = true;
            string toggleLabel = _hdrOnCached ? "Basculer SDR" : "Basculer HDR";
            _btnToggle.Text = toggleLabel;
            _trayToggleItem.Text = toggleLabel;
            if (_tray.Visible)
            {
                string tip = text;
                if (tip.Length > 60) tip = tip.Substring(0, 60);
                _tray.Text = tip;
            }
            if (!syncSlider)
                return;
            _updatingUi = true;
            try
            {
                int v = Math.Max(0, Math.Min(100, DisplayControl.GetBrightnessPercent()));
                if (_brightness.Value != v)
                    _brightness.Value = v;
            }
            finally
            {
                _updatingUi = false;
            }
        }

        private static Icon LoadIcon()
        {
            try
            {
                string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "hdr2sdr.ico");
                if (System.IO.File.Exists(path))
                    return new Icon(path);
            }
            catch
            {
            }
            return SystemIcons.Application;
        }
    }
}
