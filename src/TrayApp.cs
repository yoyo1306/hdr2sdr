using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;
using Microsoft.Win32;

namespace Hdr2Sdr
{
    internal static class Profiles
    {
        public static bool ApplyJour(out string msg)
        {
            DisplayTarget t = DisplayControl.GetPrimaryHdrTarget();
            bool wasHdr = t != null && t.HdrEnabled;
            if (!wasHdr)
            {
                DisplayControl.RememberSdrBrightness();
                msg = "Profil Jour: HDR OFF (SDR natif)";
                return true;
            }

            int pct;
            bool ok = DisplayControl.LeaveHdrRestoringBrightness(out pct);
            msg = "Profil Jour: HDR OFF (SDR natif)";
            return ok;
        }

        public static bool ApplySoir(AppConfig cfg, out string msg)
        {
            DisplayTarget t = DisplayControl.GetPrimaryHdrTarget();
            if (t != null && t.HdrSupported && !t.HdrEnabled)
            {
                if (!DisplayControl.SetHdr(t, true))
                {
                    msg = "ERROR: activation HDR impossible (" + DisplayControl.LastError + ")";
                    return false;
                }
            }
            if (!DisplayControl.SetSdrPercent(cfg.ProfileSoirSdrPercent))
            {
                msg = "ERROR: réglage SDR impossible";
                return false;
            }
            msg = "Profil Soir: HDR ON + SDR " + cfg.ProfileSoirSdrPercent + "%";
            return true;
        }

        public static bool ApplyJeu(AppConfig cfg, out string msg)
        {
            DisplayTarget t = DisplayControl.GetPrimaryHdrTarget();
            if (t != null && t.HdrSupported && !t.HdrEnabled)
            {
                if (!DisplayControl.SetHdr(t, true))
                {
                    msg = "ERROR: activation HDR impossible (" + DisplayControl.LastError + ")";
                    return false;
                }
            }
            if (!DisplayControl.SetSdrPercent(cfg.ProfileJeuSdrPercent))
            {
                msg = "ERROR: réglage SDR impossible";
                return false;
            }
            msg = "Profil Jeu: HDR ON + SDR " + cfg.ProfileJeuSdrPercent + "%";
            return true;
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly AppConfig _cfg;
        private readonly HotkeyWindow _hotkeys;
        private readonly NotifyIcon _tray;
        private readonly ContextMenuStrip _trayMenu;
        private readonly ToolStripMenuItem _trayToggleItem;

        private Label _badge;
        private Label _bigValue;
        private Label _modeTitle;
        private Label _modeDesc;
        private Label _statusLine;
        private Label _brightValueLabel;
        private ToggleSwitch _toggle;
        private ModernSlider _slider;
        private ProfileCard _cardJour;
        private ProfileCard _cardSoir;
        private ProfileCard _cardJeu;
        private Panel _header;

        // Écritures DDC sur thread dédié : l'UI ne bloque jamais.
        private readonly object _writeSync = new object();
        private readonly System.Threading.AutoResetEvent _writeEvent = new System.Threading.AutoResetEvent(false);
        private System.Threading.Thread _writeThread;
        private volatile bool _writeStop;
        private int _writePending = -1;
        private int _lastWriteTick;
        private int _targetBright = -1;
        private int _reinforceGen;
        private bool _hdrBusy;
        private bool _launchResyncStarted;
        private readonly System.Collections.Generic.List<int> _hotkeyFailed = new System.Collections.Generic.List<int>();
        private readonly Timer _hotkeyAssertTimer;
        private bool _hotkeySystemEventsHooked;
        private int _hotkeyStartupAssertsDone;
        private bool _allowClose;
        private bool _updatingUi;
        private bool _hdrStateKnown;
        private bool _hdrOnCached;
        private bool _badgeHasState;
        private bool _badgeHdrState;

        private bool _dragging;
        private Point _dragOffset;

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
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            // true SANS menu visible (borderless) mais requis pour que le clic
            // sur l'icône de la barre des tâches minimise/restaure la fenêtre.
            MinimizeBox = true;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(400, 568);
            MinimumSize = new Size(400, 568);
            BackColor = ModernTheme.Bg;
            // Mise à l'échelle désactivée : layout fixe dessiné au pixel près.
            // (AutoScaleMode.Font agrandissait la fenêtre à 467x757 et cassait
            // les proportions prévues.)
            AutoScaleMode = AutoScaleMode.None;
            Font = ModernTheme.FontBody;
            Icon = LoadIcon();
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);

            BuildHeader();
            BuildBody();

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
            _hotkeyAssertTimer = new Timer();
            _hotkeyAssertTimer.Interval = 20000;
            _hotkeyAssertTimer.Tick += HotkeyAssertTimer_Tick;
            RegisterHotkeys();

            _lastWriteTick = System.Environment.TickCount;
            _writeThread = new System.Threading.Thread(BrightnessWriter);
            _writeThread.IsBackground = true;
            _writeThread.Name = "hdr2sdr-brightness";
            _writeThread.Start();

            Resize += MainForm_Resize;
            FormClosing += MainForm_FormClosing;
            Load += delegate
            {
                // Hook + asserts planifiés dans tous les cas (finally) : si
                // RefreshStatus() lève au boot (driver pas prêt), on ne doit
                // jamais se retrouver sans ré-assertion des hotkeys.
                try
                {
                    ModernTheme.ApplyRounded(this, 18, 1);
                }
                catch { }
                // Lecture DDC de l'écran principal avant le premier affichage.
                // Ne pas poser _targetBright : ça bloquait toute correction
                // si la première lecture était un 100 % faux.
                try
                {
                    int fresh;
                    DisplayControl.TryGetBrightnessPercentFresh(out fresh);
                }
                catch { }
                try { RefreshStatus(); }
                catch { }
                // Démarrage discret : masqué directement (le Resize ne part
                // pas toujours avant le premier affichage, la fenêtre
                // apparaîtrait sinon dans Alt+Tab).
                try
                {
                    if (_cfg.StartMinimized)
                    {
                        if (_cfg.MinimizeToTray)
                            HideToTray();
                        else
                            WindowState = FormWindowState.Minimized;
                    }
                }
                catch { }
                try
                {
                    // Au boot, RegisterHotKey peut réussir puis être invalidé
                    // (driver/GPU pas prêt, topologie d'écrans qui change juste
                    // après le logon, Explorer qui se réinitialise) sans aucun
                    // échec détecté. On ré-affirme donc systématiquement après
                    // le démarrage, même si aucun échec n'est connu : c'est ce
                    // qui rendait les raccourcis muets jusqu'à la première
                    // ouverture manuelle de la fenêtre.
                    HookHotkeySystemEvents();
                }
                catch { }
                try { ScheduleStartupHotkeyAsserts(); }
                catch { }
                try { hkLog("boot minimized=" + _cfg.StartMinimized + " tray=" + _cfg.MinimizeToTray + " hotkeys=" + _cfg.HotkeysEnabled); }
                catch { }
            };
            SizeChanged += delegate
            {
                ModernTheme.ApplyRounded(this, 18, 1);
            };
        }

        protected override CreateParams CreateParams
        {
            get
            {
                // WS_MINIMIZEBOX forcé : WinForms l'omet en borderless malgré
                // MinimizeBox=true, et sans lui le clic taskbar ne minimise pas.
                CreateParams p = base.CreateParams;
                p.Style |= 0x20000;
                return p;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            // Fenêtre arrondie (région en retrait d'1px) + liseré DWM à la
            // couleur du thème. La région est réappliquée car le handle est
            // recréé (ex. minimise/restore via le tray).
            ModernTheme.ApplyRounded(this, 18, 1);
            Native.SetBorderColor(Handle, ModernTheme.Bg);
            // Recréation de handle (veille, changement d'écran, reboot
            // du driver) : la table des hotkeys peut avoir été vidée.
            // Ré-affirme sans bloquer la création du handle.
            try { BeginAssertHotkeys(); }
            catch { }
        }

        protected override void WndProc(ref Message m)
        {
            // La topologie d'affichage a changé (boot, veille, dock, HDR) :
            // le handle DDC peut être périmé, on le rouvrira au prochain accès.
            if (m.Msg == 0x007E)
            {
                try
                {
                    MonitorBrightness.Invalidate();
                }
                catch
                {
                }
                // Le driver peut libérer/reprendre des touches à ce moment :
                // ré-affirme les raccourcis (cas typique : boot).
                // Fenêtre masquée en tray : le broadcast arrive quand même,
                // mais on passe par Begin (post) pour ne jamais bloquer WndProc.
                try { BeginAssertHotkeys(); }
                catch { }
            }
            base.WndProc(ref m);
        }

        // ---------- layout ----------

        private void BuildHeader()
        {
            _header = new Panel();
            _header.SetBounds(0, 0, 400, 62);
            _header.BackColor = ModernTheme.Bg;
            _header.MouseDown += HeaderMouseDown;
            _header.MouseMove += HeaderMouseMove;
            _header.MouseUp += HeaderMouseUp;
            Controls.Add(_header);

            Panel dot = new Panel();
            dot.SetBounds(18, 20, 12, 12);
            dot.BackColor = ModernTheme.Bg;
            dot.Paint += delegate(object s, PaintEventArgs e)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                using (SolidBrush glow = new SolidBrush(Color.FromArgb(50, 124, 108, 255)))
                {
                    e.Graphics.FillEllipse(glow, 0, 0, 12, 12);
                }
                using (SolidBrush b = new SolidBrush(ModernTheme.Accent))
                {
                    e.Graphics.FillEllipse(b, 2, 2, 8, 8);
                }
            };
            dot.MouseDown += HeaderMouseDown;
            dot.MouseMove += HeaderMouseMove;
            dot.MouseUp += HeaderMouseUp;
            _header.Controls.Add(dot);

            Label title = new Label();
            title.Text = "hdr2sdr";
            title.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            title.ForeColor = ModernTheme.Text;
            title.BackColor = Color.Transparent;
            title.SetBounds(38, 12, 200, 22);
            title.MouseDown += HeaderMouseDown;
            title.MouseMove += HeaderMouseMove;
            title.MouseUp += HeaderMouseUp;
            _header.Controls.Add(title);

            Label sub = new Label();
            sub.Text = "HDR  •  SDR  •  Luminosité";
            sub.Font = new Font("Segoe UI", 7.5f, FontStyle.Regular);
            sub.ForeColor = ModernTheme.TextFaint;
            sub.BackColor = Color.Transparent;
            sub.SetBounds(39, 32, 200, 16);
            sub.MouseDown += HeaderMouseDown;
            sub.MouseMove += HeaderMouseMove;
            sub.MouseUp += HeaderMouseUp;
            _header.Controls.Add(sub);

            Button btnTheme = ModernTheme.MakeIconButton(ModernTheme.IsLight() ? "☾" : "☀", 11);
            btnTheme.SetBounds(246, 14, 34, 34);
            btnTheme.Click += delegate
            {
                ToggleTheme();
                btnTheme.Text = ModernTheme.IsLight() ? "☾" : "☀";
            };
            _header.Controls.Add(btnTheme);
            ToolTip tipTheme = new ToolTip();
            tipTheme.SetToolTip(btnTheme, "Thème clair / sombre");

            Button btnGear = ModernTheme.MakeIconButton("⚙", 11);
            btnGear.SetBounds(282, 14, 34, 34);
            btnGear.Click += delegate { OpenOptions(); };
            ToolTip tip = new ToolTip();
            tip.SetToolTip(btnGear, "Options");
            _header.Controls.Add(btnGear);

            Button btnMin = ModernTheme.MakeIconButton("–", 12);
            btnMin.SetBounds(318, 14, 34, 34);
            btnMin.Click += delegate { WindowState = FormWindowState.Minimized; };
            tip.SetToolTip(btnMin, "Réduire");
            _header.Controls.Add(btnMin);

            Button btnClose = ModernTheme.MakeIconButton("✕", 9);
            btnClose.SetBounds(352, 14, 34, 34);
            btnClose.Click += delegate { Close(); };
            btnClose.MouseEnter += delegate { btnClose.ForeColor = ModernTheme.Danger; };
            btnClose.MouseLeave += delegate { btnClose.ForeColor = ModernTheme.TextDim; };
            tip.SetToolTip(btnClose, "Quitter");
            _header.Controls.Add(btnClose);
        }

        private void BuildBody()
        {
            // Hero card
            CardPanel hero = new CardPanel();
            hero.SetBounds(16, 66, 368, 196);
            Controls.Add(hero);

            _badge = new Label();
            _badge.AutoSize = false;
            _badge.TextAlign = ContentAlignment.MiddleCenter;
            _badge.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            _badge.SetBounds(16, 14, 92, 26);
            hero.Controls.Add(_badge);

            _toggle = new ToggleSwitch();
            _toggle.SetBounds(300, 14, 52, 28);
            _toggle.CheckedChanged += delegate
            {
                if (_updatingUi) return;
                DoSetHdr(_toggle.Checked);
            };
            hero.Controls.Add(_toggle);

            _bigValue = new Label();
            _bigValue.AutoSize = false;
            _bigValue.Font = ModernTheme.FontBigNumber;
            _bigValue.ForeColor = ModernTheme.Text;
            _bigValue.BackColor = Color.Transparent;
            _bigValue.SetBounds(14, 44, 220, 52);
            hero.Controls.Add(_bigValue);

            _modeTitle = new Label();
            _modeTitle.AutoSize = false;
            _modeTitle.Font = new Font("Segoe UI", 10f, FontStyle.Bold);
            _modeTitle.ForeColor = ModernTheme.Text;
            _modeTitle.BackColor = Color.Transparent;
            _modeTitle.SetBounds(16, 100, 336, 22);
            hero.Controls.Add(_modeTitle);

            _modeDesc = new Label();
            _modeDesc.AutoSize = false;
            _modeDesc.Font = ModernTheme.FontSmall;
            _modeDesc.ForeColor = ModernTheme.TextDim;
            _modeDesc.BackColor = Color.Transparent;
            _modeDesc.SetBounds(16, 122, 336, 32);
            hero.Controls.Add(_modeDesc);

            _statusLine = new Label();
            _statusLine.AutoSize = false;
            _statusLine.Font = new Font("Segoe UI", 7.5f, FontStyle.Regular);
            _statusLine.ForeColor = ModernTheme.TextFaint;
            _statusLine.BackColor = Color.Transparent;
            _statusLine.SetBounds(16, 158, 336, 18);
            hero.Controls.Add(_statusLine);

            // Brightness card
            CardPanel bright = new CardPanel();
            bright.SetBounds(16, 270, 368, 138);
            Controls.Add(bright);

            Label brightTitle = new Label();
            brightTitle.Text = "Luminosité";
            brightTitle.Font = ModernTheme.FontCardTitle;
            brightTitle.ForeColor = ModernTheme.Text;
            brightTitle.BackColor = Color.Transparent;
            brightTitle.SetBounds(16, 12, 160, 20);
            bright.Controls.Add(brightTitle);

            _brightValueLabel = new Label();
            _brightValueLabel.TextAlign = ContentAlignment.MiddleRight;
            _brightValueLabel.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            _brightValueLabel.ForeColor = ModernTheme.TextDim;
            _brightValueLabel.BackColor = Color.Transparent;
            _brightValueLabel.SetBounds(252, 12, 100, 20);
            bright.Controls.Add(_brightValueLabel);

            _slider = new ModernSlider();
            _slider.SetBounds(8, 36, 352, 38);
            // 100 % non-bloquant : labels + peinture synchrone immédiats,
            // écriture DDC déléguée au thread dédié. Aucun appel DDC sur
            // le thread UI ici (un DDC qui accroche figeait toute l'UI).
            _slider.ValueChanged += delegate
            {
                if (_updatingUi) return;
                int v = _slider.Value;
                UpdateBrightnessStatusText(v);
                _slider.Update();
                _bigValue.Update();
                _brightValueLabel.Update();
                RequestBrightnessWrite(v);
            };
            bright.Controls.Add(_slider);

            Button btnDown = ModernTheme.MakePillButton("−  Moins", false);
            btnDown.SetBounds(16, 82, 164, 34);
            btnDown.Click += delegate
            {
                NudgeBrightnessHold(-_cfg.SdrStepPercent);
            };
            bright.Controls.Add(btnDown);

            Button btnUp = ModernTheme.MakePillButton("+  Plus", false);
            btnUp.SetBounds(188, 82, 164, 34);
            btnUp.Click += delegate
            {
                NudgeBrightnessHold(_cfg.SdrStepPercent);
            };
            bright.Controls.Add(btnUp);

            // Profiles label
            Label profLabel = new Label();
            profLabel.Text = "PROFILS";
            profLabel.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            profLabel.ForeColor = ModernTheme.TextFaint;
            profLabel.BackColor = Color.Transparent;
            profLabel.SetBounds(20, 418, 200, 18);
            Controls.Add(profLabel);

            _cardJour = MakeProfileCard("☀", "Jour", "SDR natif", 16, 440);
            _cardJour.Click += delegate { ApplyProfileAsync(4); };
            Controls.Add(_cardJour);

            _cardSoir = MakeProfileCard("☾", "Soir", "HDR + doux", 140, 440);
            _cardSoir.Click += delegate { ApplyProfileAsync(5); };
            Controls.Add(_cardSoir);

            _cardJeu = MakeProfileCard("🎮", "Jeu", "HDR + punchy", 264, 440);
            _cardJeu.Click += delegate { ApplyProfileAsync(6); };
            Controls.Add(_cardJeu);

            Label version = new Label();
            version.Text = BuildInfo.ShortVersion;
            version.Font = new Font("Segoe UI", 7.5f, FontStyle.Regular);
            version.ForeColor = ModernTheme.TextFaint;
            version.BackColor = Color.Transparent;
            version.TextAlign = ContentAlignment.MiddleRight;
            version.SetBounds(200, 548, 184, 16);
            version.Anchor = AnchorStyles.Right | AnchorStyles.Bottom;
            Controls.Add(version);
        }

        private ProfileCard MakeProfileCard(string icon, string title, string desc, int x, int y)
        {
            ProfileCard c = new ProfileCard();
            c.IconChar = icon;
            c.CardTitle = title;
            c.CardDesc = desc;
            c.SetBounds(x, y, 120, 104);
            return c;
        }

        private void HeaderMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                // Référentiel écran (Cursor.Position) : les coordonnées de
                // l'événement dépendent du contrôle survolé et faisaient
                // sauter la fenêtre au premier mouvement.
                _dragging = true;
                _dragOffset = new Point(Cursor.Position.X - Location.X, Cursor.Position.Y - Location.Y);
                _header.Capture = true;
            }
        }

        private void HeaderMouseMove(object sender, MouseEventArgs e)
        {
            if (_dragging)
            {
                // Si le bouton a été relâché hors fenêtre, stoppe le drag
                // au lieu de téléporter la fenêtre au curseur.
                if ((Control.MouseButtons & MouseButtons.Left) != MouseButtons.Left)
                {
                    _dragging = false;
                    _header.Capture = false;
                    return;
                }
                Location = new Point(Cursor.Position.X - _dragOffset.X, Cursor.Position.Y - _dragOffset.Y);
            }
        }

        private void HeaderMouseUp(object sender, MouseEventArgs e)
        {
            _dragging = false;
            _header.Capture = false;
        }

        // ---------- thème ----------

        private void ToggleTheme()
        {
            AppTheme next = ModernTheme.Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark;
            ModernTheme.SetTheme(next);
            _cfg.Theme = next == AppTheme.Light ? "light" : "dark";
            _cfg.Save();
            ApplyTheme();
        }

        private void ApplyTheme()
        {
            _badgeHasState = false;
            BackColor = ModernTheme.Bg;
            ModernTheme.RemapControlTree(this);
            Native.SetBorderColor(Handle, ModernTheme.Bg);
            RefreshStatus();
        }

        // ---------- options / tray ----------

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
            MonitorCatalog.SelectedDevice = _cfg.MonitorDevice ?? "";
            MonitorBrightness.Invalidate();
            _targetBright = -1;
            _launchResyncStarted = false;
            try
            {
                int fresh;
                DisplayControl.TryGetBrightnessPercentFresh(out fresh);
            }
            catch { }
            RefreshStatus();
        }

        private void MainForm_Resize(object sender, EventArgs e)
        {
            if (WindowState != FormWindowState.Minimized)
                return;
            if (!_cfg.MinimizeToTray)
                return;

            HideToTray();
        }

        private void HideToTray()
        {
            Hide();
            ShowInTaskbar = false;
            _tray.Visible = true;
            try
            {
                // Au boot, StatusText() fait du DDC synchrone sur le thread UI
                // (lent, peut figer la pompe à messages juste quand les
                // hotkeys doivent se stabiliser). Version cache seul ici ;
                // RefreshStatus() en fond resync peu après.
                bool hdr;
                int v;
                string tip = DisplayControl.StatusTextCached(out hdr, out v);
                if (tip.Length > 60) tip = tip.Substring(0, 60);
                _tray.Text = tip;
            }
            catch
            {
            }
        }

        private void MainForm_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_allowClose)
                return;

            _allowClose = true;
            UnhookHotkeySystemEvents();
            _hotkeyAssertTimer.Stop();
            _hotkeyAssertTimer.Dispose();
            _writeStop = true;
            _writeEvent.Set();
            if (_writeThread != null)
            {
                _writeThread.Join(1500);
                _writeThread = null;
            }
            _writeEvent.Close();
            FlushPendingBrightness();
            UnregisterHotkeys();
            _hotkeys.Dispose();
            MonitorBrightness.Invalidate();
            _tray.Visible = false;
            _tray.Dispose();
            try { _trayMenu.Dispose(); } catch { }
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
            // C'est exactement le geste qui réparait les raccourcis à la
            // main : on le fait désormais automatiquement à chaque retour.
            try { BeginAssertHotkeys(); }
            catch { }
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
            _hotkeyFailed.Clear();
            _hotkeyAssertTimer.Stop();
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

            if (!_hotkeyAssertTimer.Enabled)
                _hotkeyAssertTimer.Start();
        }

        private bool TryRegister(int id, string chord, bool allowRepeat)
        {
            if (string.IsNullOrEmpty(chord))
                return true;
            int err;
            bool ok = _hotkeys.Register(id, chord, allowRepeat, out err);
            if (ok)
            {
                _hotkeyFailed.Remove(id);
                return true;
            }
            // Échec : retenté automatiquement par AssertHotkeys.
            // err 1409 = déjà pris (conflit transitoire au boot typique),
            // err 0 = chord invalide.
            hkLog(id + " '" + chord + "' -> FAIL err=" + err);
            if (!_hotkeyFailed.Contains(id))
                _hotkeyFailed.Add(id);
            return false;
        }

        private static void hkLog(string msg)
        {
            try
            {
                string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "hdr-hk-log.txt");
                try
                {
                    System.IO.FileInfo fi = new System.IO.FileInfo(path);
                    if (fi.Exists && fi.Length > 102400)
                        System.IO.File.WriteAllText(path, string.Empty);
                }
                catch { }
                System.IO.File.AppendAllText(
                    path,
                    System.DateTime.Now.ToString("HH:mm:ss") + " " + msg + "\r\n");
            }
            catch
            {
            }
        }

        private void HotkeyAssertTimer_Tick(object sender, EventArgs e)
        {
            // Ne ré-affirme périodiquement que s'il y a des échecs connus :
            // sinon on laisserait un trou de 6 unregister/register toutes les 20 s.
            if (_hotkeyFailed.Count == 0)
                return;
            AssertHotkeys();
        }

        /// <summary>
        /// Ré-affirme tous les raccourcis configurés : répare les touches
        /// perdues (conflit transitoire au boot, table hotkey réinitialisée…)
        /// sans aucune intervention. Désenregistre les cases vidées.
        /// Toujours exécuté sur le thread UI (le HWND des hotkeys y est lié).
        /// </summary>
        private void AssertHotkeys()
        {
            if (IsDisposed)
                return;
            if (InvokeRequired)
            {
                try { BeginInvoke((MethodInvoker)delegate { AssertHotkeys(); }); }
                catch { }
                return;
            }
            if (!_cfg.HotkeysEnabled)
                return;
            try { _hotkeys.EnsureHandle(); }
            catch { }
            int before = _hotkeyFailed.Count;
            for (int id = 1; id <= 6; id++)
            {
                string chord = HotkeyChord(id);
                if (string.IsNullOrEmpty(chord))
                {
                    _hotkeys.Unregister(id);
                    _hotkeyFailed.Remove(id);
                    continue;
                }
                // Register() nettoie déjà l'id avant de ré-enregistrer.
                TryRegister(id, chord, HotkeyRepeat(id));
            }
            if (_hotkeyFailed.Count != before)
                RefreshStatus();
        }

        /// <summary>
        /// Version postée (non-bloquante) pour WndProc / SystemEvents /
        /// threads pool : ne bloque jamais l'appelant.
        /// </summary>
        private void BeginAssertHotkeys()
        {
            if (IsDisposed)
                return;
            try
            {
                if (!IsHandleCreated)
                    return;
                BeginInvoke((MethodInvoker)delegate
                {
                    try { AssertHotkeys(); }
                    catch { }
                });
            }
            catch
            {
            }
        }

        private void HookHotkeySystemEvents()
        {
            if (_hotkeySystemEventsHooked)
                return;
            _hotkeySystemEventsHooked = true;
            try
            {
                // WndProc 0x7E ne suffit pas quand la fenêtre est masquée en
                // tray au boot : ces événements arrivent même sans fenêtre
                // visible et couvrent boot / changement d'écran / veille.
                SystemEvents.DisplaySettingsChanged += SystemEvents_HotkeysChanged;
                SystemEvents.SessionSwitch += SystemEvents_HotkeysChanged;
                SystemEvents.PowerModeChanged += SystemEvents_HotkeysChanged;
            }
            catch
            {
            }
        }

        private void UnhookHotkeySystemEvents()
        {
            if (!_hotkeySystemEventsHooked)
                return;
            _hotkeySystemEventsHooked = false;
            try
            {
                SystemEvents.DisplaySettingsChanged -= SystemEvents_HotkeysChanged;
                SystemEvents.SessionSwitch -= SystemEvents_HotkeysChanged;
                SystemEvents.PowerModeChanged -= SystemEvents_HotkeysChanged;
            }
            catch
            {
            }
        }

        private void SystemEvents_HotkeysChanged(object sender, EventArgs e)
        {
            try { BeginAssertHotkeys(); }
            catch { }
        }

        /// <summary>
        /// Rafale de ré-assertions après le démarrage : au boot, l'enregis-
        /// trement initial peut être invalidé quelques secondes plus tard
        /// (driver GPU, Explorer) sans échec détecté. On ré-affirme donc à
        /// 3 s / 10 s / 30 s / 60 s, même si tout semblait OK. Inoffensif en
        /// usage normal (idempotent, Sur thread UI via BeginInvoke).
        /// </summary>
        private void ScheduleStartupHotkeyAsserts()
        {
            int[] delays = new int[] { 3000, 10000, 30000, 60000 };
            for (int i = 0; i < delays.Length; i++)
            {
                int d = delays[i];
                System.Threading.ThreadPool.QueueUserWorkItem(delegate
                {
                    try { System.Threading.Thread.Sleep(d); }
                    catch { }
                    try
                    {
                        if (IsDisposed || !IsHandleCreated)
                            return;
                        BeginInvoke((MethodInvoker)delegate
                        {
                            try
                            {
                                _hotkeyStartupAssertsDone++;
                                AssertHotkeys();
                                if (_hotkeyStartupAssertsDone == 1)
                                    hkLog("startup assert (3s) failed=" + _hotkeyFailed.Count);
                            }
                            catch { }
                        });
                    }
                    catch { }
                });
            }
        }

        private string HotkeyChord(int id)
        {
            if (id == HK_TOGGLE) return _cfg.HotkeyToggleHdr;
            if (id == HK_SDR_UP) return _cfg.HotkeySdrUp;
            if (id == HK_SDR_DOWN) return _cfg.HotkeySdrDown;
            if (id == HK_JOUR) return _cfg.HotkeyProfileJour;
            if (id == HK_SOIR) return _cfg.HotkeyProfileSoir;
            if (id == HK_JEU) return _cfg.HotkeyProfileJeu;
            return null;
        }

        private static bool HotkeyRepeat(int id)
        {
            return id == HK_SDR_UP || id == HK_SDR_DOWN;
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
            else if (id == HK_JOUR) ApplyProfileAsync(4);
            else if (id == HK_SOIR) ApplyProfileAsync(5);
            else if (id == HK_JEU) ApplyProfileAsync(6);
        }

        /// <summary>Profils sur thread pool : LeaveHdr/SetHdr bloquent 1-4 s (DDC).</summary>
        private void ApplyProfileAsync(int which)
        {
            if (_hdrBusy)
                return;
            _hdrBusy = true;
            CancelPendingBrightness();
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                bool leftToSdr = false;
                int restored = -1;
                try
                {
                    string dummy;
                    if (which == 4)
                        Profiles.ApplyJour(out dummy);
                    else if (which == 5)
                        Profiles.ApplySoir(_cfg, out dummy);
                    else
                        Profiles.ApplyJeu(_cfg, out dummy);
                    leftToSdr = which == 4 && !DisplayControl.IsHdrOn();
                    if (leftToSdr)
                        restored = DisplayControl.GetRememberedSdrBrightness();
                }
                catch { }
                int shown = restored;
                try
                {
                    if (!IsDisposed && IsHandleCreated)
                        BeginInvoke((MethodInvoker)delegate
                        {
                            _hdrBusy = false;
                            _hdrStateKnown = false;
                            if (shown >= 0)
                                _targetBright = shown;
                            else
                                _targetBright = -1;
                            RefreshStatus();
                        });
                    else
                        _hdrBusy = false;
                }
                catch { _hdrBusy = false; }
            });
        }

        private void NudgeBrightnessHold(int delta)
        {
            int cur = _targetBright >= 0 ? _targetBright : _slider.Value;
            int next = cur + delta;
            if (next < 0) next = 0;
            if (next > 100) next = 100;

            _updatingUi = true;
            try
            {
                _slider.SetValueSilent(next);
                UpdateBrightnessStatusText(next);
            }
            finally
            {
                _updatingUi = false;
            }

            RequestBrightnessWrite(next);
        }

        private void RequestBrightnessWrite(int percent)
        {
            _targetBright = percent;
            lock (_writeSync)
            {
                _writePending = percent;
            }
            _writeEvent.Set();
        }

        private void BrightnessWriter()
        {
            for (; ; )
            {
                _writeEvent.WaitOne();
                if (_writeStop)
                    return;
                for (; ; )
                {
                    int value;
                    lock (_writeSync)
                    {
                        value = _writePending;
                        _writePending = -1;
                    }
                    if (value < 0)
                        break;

                    // Espace les écritures DDC (lentes) : 30 ms mini.
                    int wait = 30 - (System.Environment.TickCount - _lastWriteTick);
                    if (wait > 0)
                        System.Threading.Thread.Sleep(wait);
                    if (_writeStop)
                        return;

                    DisplayControl.SetBrightnessPercent(value);
                    _lastWriteTick = System.Environment.TickCount;
                }
            }
        }

        private void FlushPendingBrightness()
        {
            int value;
            lock (_writeSync)
            {
                value = _writePending;
                _writePending = -1;
            }
            if (value < 0)
                return;
            // Seulement une écriture pas encore partie. SetBrightnessPercent
            // choisit DDC (SDR) ou le curseur Windows (HDR) : forcer le DDC
            // avec le % affiché en HDR envoyait la luminosité SDR Windows
            // au moniteur (souvent haut) juste avant le retour SDR.
            try { DisplayControl.SetBrightnessPercent(value); }
            catch { }
        }

        private void CancelPendingBrightness()
        {
            _reinforceGen++;
            _targetBright = -1;
            lock (_writeSync)
            {
                _writePending = -1;
            }
        }

        private void PaintBadge(Label badge, bool hdrOn)
        {
            // Ne recrée la région qu'en cas de changement d'état.
            if (_badgeHasState && _badgeHdrState == hdrOn)
                return;
            _badgeHasState = true;
            _badgeHdrState = hdrOn;
            badge.Text = hdrOn ? "● HDR ON" : "○ SDR";
            badge.ForeColor = hdrOn ? ModernTheme.HdrBadgeText : ModernTheme.SdrBadgeText;
            badge.BackColor = hdrOn ? ModernTheme.HdrOnBg : ModernTheme.SdrBlueBg;
            try
            {
                using (GraphicsPath p = ModernTheme.RoundedRect(new Rectangle(0, 0, badge.Width, badge.Height), 13))
                {
                    badge.Region = new Region(p);
                }
            }
            catch
            {
            }
        }

        private void UpdateBrightnessStatusText(int percent)
        {
            if (!_hdrStateKnown)
            {
                _hdrOnCached = DisplayControl.IsHdrOn();
                _hdrStateKnown = true;
            }
            _updatingUi = true;
            try
            {
                _bigValue.Text = percent + "%";
                _brightValueLabel.Text = percent + " %";
                _slider.SetValueSilent(percent);
                PaintBadge(_badge, _hdrOnCached);
                _modeTitle.Text = _hdrOnCached ? "HDR activé" : "SDR natif";
                _modeDesc.Text = _hdrOnCached
                    ? "Curseur Windows « luminosité du contenu SDR »."
                    : "Luminosité matérielle via DDC/CI.";
                _statusLine.Text = _hdrOnCached
                    ? ("HDR ON  •  SDR " + percent + "%")
                    : ("SDR  •  luminosité " + percent + "%");
                _toggle.SetCheckedSilent(_hdrOnCached);
                UpdateProfileSelection(percent);
                string toggleLabel = _hdrOnCached ? "Basculer SDR" : "Basculer HDR";
                _trayToggleItem.Text = toggleLabel;
                // Le tooltip du tray suivait seulement RefreshStatus : il restait
                // figé après chaque drag. Même format que StatusText().
                try
                {
                    if (_tray.Visible)
                    {
                        string tip = _hdrOnCached
                            ? ("HDR ON | SDR " + percent + "%")
                            : ("SDR | luminosité " + percent + "%");
                        if (tip.Length > 60) tip = tip.Substring(0, 60);
                        if (_hotkeyFailed.Count > 0)
                            tip += " ⚠";
                        if (_tray.Text != tip)
                            _tray.Text = tip;
                    }
                }
                catch
                {
                }
            }
            finally
            {
                _updatingUi = false;
            }
        }

        private void UpdateProfileSelection(int percent)
        {
            bool jour = !_hdrOnCached;
            bool soir = _hdrOnCached && percent == _cfg.ProfileSoirSdrPercent;
            bool jeu = _hdrOnCached && percent == _cfg.ProfileJeuSdrPercent;
            if (soir && jeu)
            {
                soir = false;
                jeu = false;
            }
            _cardJour.Selected = jour;
            _cardSoir.Selected = soir;
            _cardJeu.Selected = jeu;
            _cardSoir.CardDesc = "HDR + " + _cfg.ProfileSoirSdrPercent + "%";
            _cardJeu.CardDesc = "HDR + " + _cfg.ProfileJeuSdrPercent + "%";
        }

        private void DoSetHdr(bool wantOn)
        {
            if (_hdrBusy)
                return;
            FlushPendingBrightness();
            bool isOn = DisplayControl.IsHdrOn();
            if (isOn == wantOn)
            {
                RefreshStatus();
                return;
            }
            _hdrBusy = true;
            // Optimiste : reflète l'état voulu tout de suite, le worker confirme.
            _hdrOnCached = wantOn;
            _hdrStateKnown = true;
            _updatingUi = true;
            try { _toggle.SetCheckedSilent(wantOn); }
            finally { _updatingUi = false; }
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                int pct = -1;
                try
                {
                    if (!wantOn)
                    {
                        DisplayControl.LeaveHdrRestoringBrightness(out pct);
                    }
                    else
                    {
                        DisplayControl.RememberSdrBrightness();
                        DisplayTarget t = DisplayControl.GetPrimaryHdrTarget();
                        if (t != null)
                            DisplayControl.SetHdr(t, true);
                    }
                }
                catch { }
                int shown = pct;
                try
                {
                    if (!IsDisposed && IsHandleCreated)
                        BeginInvoke((MethodInvoker)delegate
                        {
                            _hdrBusy = false;
                            _hdrStateKnown = false;
                            if (!wantOn && shown >= 0)
                            {
                                _targetBright = shown;
                                _updatingUi = true;
                                try { _slider.SetValueSilent(shown); }
                                finally { _updatingUi = false; }
                                RefreshStatus(false);
                                ScheduleBrightnessReinforce(shown);
                            }
                            else
                            {
                                _targetBright = -1;
                                RefreshStatus();
                            }
                        });
                    else
                        _hdrBusy = false;
                }
                catch { _hdrBusy = false; }
            });
        }

        private void DoToggleHdr()
        {
            if (_hdrBusy)
                return;
            FlushPendingBrightness();
            bool wasHdr = DisplayControl.IsHdrOn();
            _hdrBusy = true;
            _hdrOnCached = !wasHdr;
            _hdrStateKnown = true;
            _updatingUi = true;
            try { _toggle.SetCheckedSilent(!wasHdr); }
            finally { _updatingUi = false; }
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                int pct = -1;
                try
                {
                    if (!wasHdr)
                    {
                        DisplayControl.RememberSdrBrightness();
                        bool enabled;
                        DisplayControl.ToggleHdr(out enabled);
                    }
                    else
                    {
                        DisplayControl.LeaveHdrRestoringBrightness(out pct);
                    }
                }
                catch { }
                int shown = pct;
                try
                {
                    if (!IsDisposed && IsHandleCreated)
                        BeginInvoke((MethodInvoker)delegate
                        {
                            _hdrBusy = false;
                            _hdrStateKnown = false;
                            if (wasHdr && shown >= 0)
                            {
                                _targetBright = shown;
                                _updatingUi = true;
                                try { _slider.SetValueSilent(shown); }
                                finally { _updatingUi = false; }
                                RefreshStatus(false);
                                ScheduleBrightnessReinforce(shown);
                            }
                            else
                            {
                                _targetBright = -1;
                                RefreshStatus();
                            }
                        });
                    else
                        _hdrBusy = false;
                }
                catch { _hdrBusy = false; }
            });
        }

        private void ScheduleBrightnessReinforce(int percent)
        {
            // Renforce via le thread dédié (non-bloquant) : 8 écritures
            // espacées de 200 ms. S'arrête si l'utilisateur change la valeur
            // entre-temps (sinon on écraserait son réglage pendant 1.6 s).
            _reinforceGen++;
            int gen = _reinforceGen;
            int left = 8;
            Timer t = new Timer();
            t.Interval = 200;
            t.Tick += delegate
            {
                if (gen != _reinforceGen)
                {
                    t.Stop();
                    t.Dispose();
                    return;
                }
                if (_targetBright >= 0 && _targetBright != percent)
                {
                    t.Stop();
                    t.Dispose();
                    return;
                }
                RequestBrightnessWrite(percent);
                left--;
                if (left <= 0)
                {
                    t.Stop();
                    t.Dispose();
                }
            };
            t.Start();
        }

        private void RefreshStatus()
        {
            RefreshStatus(true);
        }

        private void RefreshStatus(bool syncSlider)
        {
            // Chemin UI : 100 % cache, aucun DDC (un GetMonitorBrightness sur
            // le thread UI figeait la fenêtre). Le frais arrive en fond.
            bool hdr;
            int v;
            string text = DisplayControl.StatusTextCached(out hdr, out v);
            v = Math.Max(0, Math.Min(100, v));
            _hdrOnCached = hdr;
            _hdrStateKnown = true;

            _updatingUi = true;
            try
            {
                _bigValue.Text = v + "%";
                _brightValueLabel.Text = v + " %";
                PaintBadge(_badge, _hdrOnCached);
                _modeTitle.Text = _hdrOnCached ? "HDR activé" : "SDR natif";
                _modeDesc.Text = _hdrOnCached
                    ? "Curseur Windows « luminosité du contenu SDR »."
                    : "Luminosité matérielle via DDC/CI.";
                _statusLine.Text = text;
                _toggle.SetCheckedSilent(_hdrOnCached);
                UpdateProfileSelection(v);
                string toggleLabel = _hdrOnCached ? "Basculer SDR" : "Basculer HDR";
                _trayToggleItem.Text = toggleLabel;
                if (_tray.Visible)
                {
                    string tipText = text;
                    if (tipText.Length > 60) tipText = tipText.Substring(0, 60);
                    if (_hotkeyFailed.Count > 0)
                        tipText += " ⚠";
                    try
                    {
                        if (_tray.Text != tipText)
                            _tray.Text = tipText;
                    }
                    catch
                    {
                    }
                }
                if (syncSlider)
                    _slider.SetValueSilent(v);
                // sinon : ne pas toucher au slider (valeur du drag en cours).
            }
            finally
            {
                _updatingUi = false;
            }

            // Relecture au lancement même si un premier cache existe : un 100 %
            // pris sur le mauvais écran ne doit pas rester affiché.
            // _targetBright >= 0 = l'utilisateur ou le retour HDR tient la valeur.
            if (!hdr && _targetBright < 0 && !_hdrBusy && !_launchResyncStarted)
            {
                _launchResyncStarted = true;
                QueueSdrBrightnessResync(syncSlider);
            }
        }

        /// <summary>
        /// Relit la vraie luminosité DDC en fond (lancement / retour SDR).
        /// Plusieurs tentatives espacées car le DDC est souvent indisponible
        /// juste au boot ou après un changement de mode. Ne touche à l'UI
        /// qu'en cas de lecture réussie (jamais de 0 % d'échec).
        /// </summary>
        private void QueueSdrBrightnessResync(bool syncSlider)
        {
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                // Délais entre tentatives (la 1re est immédiate).
                int[] delays = new int[] { 0, 600, 1500, 3000, 6000 };
                for (int i = 0; i < delays.Length; i++)
                {
                    if (delays[i] > 0)
                    {
                        try { System.Threading.Thread.Sleep(delays[i]); }
                        catch { return; }
                    }
                    if (_writeStop)
                        return;
                    // L'utilisateur a pris la main entre-temps : on s'efface.
                    if (_targetBright >= 0 || _hdrBusy)
                        return;
                    int fresh;
                    bool ok;
                    try { ok = DisplayControl.TryGetBrightnessPercentFresh(out fresh); }
                    catch { return; }
                    if (!ok)
                        continue;
                    try
                    {
                        if (IsDisposed || !IsHandleCreated)
                            return;
                        int copy = fresh;
                        BeginInvoke((MethodInvoker)delegate
                        {
                            if (_targetBright >= 0 || _hdrBusy)
                                return;
                            _updatingUi = true;
                            try
                            {
                                _bigValue.Text = copy + "%";
                                _brightValueLabel.Text = copy + " %";
                                if (syncSlider)
                                    _slider.SetValueSilent(copy);
                                _statusLine.Text = "SDR  •  luminosité " + copy + "%";
                                UpdateProfileSelection(copy);
                                if (_tray.Visible)
                                {
                                    try
                                    {
                                        string tip = "SDR | luminosité " + copy + "%";
                                        if (tip.Length > 60) tip = tip.Substring(0, 60);
                                        if (_hotkeyFailed.Count > 0)
                                            tip += " ⚠";
                                        if (_tray.Text != tip)
                                            _tray.Text = tip;
                                    }
                                    catch { }
                                }
                            }
                            finally { _updatingUi = false; }
                        });
                    }
                    catch { }
                    return;
                }
                // Toutes les tentatives ont échoué : on garde le 50 % "inconnu"
                // plutôt qu'afficher un 0 % trompeur. Le prochain RefreshStatus
                // retentera.
            });
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
