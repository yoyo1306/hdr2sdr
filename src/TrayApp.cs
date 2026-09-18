using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Hdr2Sdr
{
    internal static class Profiles
    {
        public static string ApplyJour()
        {
            DisplayTarget t = DisplayControl.GetPrimaryHdrTarget();
            bool wasHdr = t != null && t.HdrEnabled;
            if (!wasHdr)
            {
                DisplayControl.RememberSdrBrightness();
                return "Profil Jour: HDR OFF (SDR natif)";
            }

            int pct;
            DisplayControl.LeaveHdrRestoringBrightness(out pct);
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
            ClientSize = new Size(400, 656);
            MinimumSize = new Size(400, 656);
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
                ModernTheme.ApplyRounded(this, 18, 1);
                RefreshStatus();
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
            title.SetBounds(38, 12, 120, 22);
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
            // Écriture DDC bridée : l'UI (thumb + %) suit instantanément,
            // le hardware reçoit au max une écriture toutes les 80 ms avec
            // la dernière valeur + flush immédiat au relâcher.
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
            _slider.MouseUp += delegate
            {
                FlushPendingBrightness();
                RefreshStatus();
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
            _cardJour.Click += delegate { Profiles.ApplyJour(); RefreshStatus(); };
            Controls.Add(_cardJour);

            _cardSoir = MakeProfileCard("☾", "Soir", "HDR + doux", 140, 440);
            _cardSoir.Click += delegate { Profiles.ApplySoir(_cfg); RefreshStatus(); };
            Controls.Add(_cardSoir);

            _cardJeu = MakeProfileCard("🎮", "Jeu", "HDR + punchy", 264, 440);
            _cardJeu.Click += delegate { Profiles.ApplyJeu(_cfg); RefreshStatus(); };
            Controls.Add(_cardJeu);

            // Footer (ancré en bas : garde ses marges si la fenêtre est mise à l'échelle)
            Label footer = new Label();
            footer.Text = "Ctrl+Alt+H : HDR/SDR   •   Ctrl+Alt+Haut/Bas : luminosité";
            footer.Font = new Font("Segoe UI", 7.5f, FontStyle.Regular);
            footer.ForeColor = ModernTheme.TextFaint;
            footer.BackColor = Color.Transparent;
            footer.TextAlign = ContentAlignment.MiddleCenter;
            footer.SetBounds(16, 556, 368, 16);
            footer.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            Controls.Add(footer);

            Label footer2 = new Label();
            footer2.Text = "Réduire minimise en zone de notification si activé dans Options.";
            footer2.Font = new Font("Segoe UI", 7.5f, FontStyle.Regular);
            footer2.ForeColor = ModernTheme.TextFaint;
            footer2.BackColor = Color.Transparent;
            footer2.TextAlign = ContentAlignment.MiddleCenter;
            footer2.SetBounds(16, 574, 368, 16);
            footer2.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            Controls.Add(footer2);

            Button toggleBtn = ModernTheme.MakePillButton("Basculer HDR / SDR", true);
            toggleBtn.SetBounds(16, 598, 368, 34);
            toggleBtn.Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom;
            toggleBtn.Click += delegate { DoToggleHdr(); };
            Controls.Add(toggleBtn);
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
            RefreshStatus();
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
            int value = _targetBright;
            _targetBright = -1;
            if (value < 0)
                return;
            lock (_writeSync)
            {
                _writePending = -1;
            }
            DisplayControl.SetBrightnessPercent(value);
            UpdateBrightnessStatusText(value);
        }

        private void PaintBadge(Label badge, bool hdrOn)
        {
            // Pas de région arrondie : fenêtre et contrôles restent carrés et
            // opaques (zéro risque de ticks sombres aux coins).
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
            FlushPendingBrightness();
            bool isOn = DisplayControl.IsHdrOn();
            if (isOn == wantOn)
            {
                RefreshStatus();
                return;
            }
            if (!wantOn)
            {
                int pct;
                DisplayControl.LeaveHdrRestoringBrightness(out pct);
                _hdrStateKnown = false;
                _targetBright = -1;
                RefreshStatus(false);
                ScheduleBrightnessReinforce(pct);
                return;
            }
            DisplayControl.RememberSdrBrightness();
            DisplayTarget t = DisplayControl.GetPrimaryHdrTarget();
            if (t != null)
                DisplayControl.SetHdr(t, true);
            _hdrStateKnown = false;
            _targetBright = -1;
            RefreshStatus();
        }

        private void DoToggleHdr()
        {
            FlushPendingBrightness();
            bool wasHdr = DisplayControl.IsHdrOn();
            if (!wasHdr)
            {
                DisplayControl.RememberSdrBrightness();
                bool enabled;
                if (!DisplayControl.ToggleHdr(out enabled))
                {
                    _hdrStateKnown = false;
                    RefreshStatus();
                    return;
                }
                _hdrStateKnown = false;
                _targetBright = -1;
                RefreshStatus();
                return;
            }

            int pct;
            DisplayControl.LeaveHdrRestoringBrightness(out pct);
            _hdrStateKnown = false;
            _targetBright = -1;

            _updatingUi = true;
            try
            {
                _slider.SetValueSilent(pct);
            }
            finally
            {
                _updatingUi = false;
            }
            RefreshStatus(false);
            ScheduleBrightnessReinforce(pct);
        }

        private void ScheduleBrightnessReinforce(int percent)
        {
            int left = 8;
            Timer t = new Timer();
            t.Interval = 200;
            t.Tick += delegate
            {
                DisplayControl.SetBrightnessPercent(percent);
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
            FlushPendingBrightness();
            string text = DisplayControl.StatusText();
            _hdrOnCached = DisplayControl.IsHdrOn();
            _hdrStateKnown = true;
            int v = Math.Max(0, Math.Min(100, DisplayControl.GetBrightnessPercent()));

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
                    _tray.Text = tipText;
                }
                if (syncSlider)
                    _slider.SetValueSilent(v);
                else
                    _slider.SetValueSilent(_slider.Value);
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
