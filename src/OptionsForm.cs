using System;
using System.Drawing;
using System.Windows.Forms;

namespace Hdr2Sdr
{
    internal sealed class OptionsForm : Form
    {
        private readonly AppConfig _cfg;
        private readonly CheckBox _chkHotkeys;
        private readonly CheckBox _chkMinimizeTray;
        private readonly CheckBox _chkStartup;
        private readonly TextBox _txtToggle;
        private readonly TextBox _txtUp;
        private readonly TextBox _txtDown;
        private readonly TextBox _txtJour;
        private readonly TextBox _txtSoir;
        private readonly TextBox _txtJeu;
        private readonly Label[] _hotkeyLabels = new Label[6];
        private Panel _header;
        private bool _dragging;
        private Point _dragOffset;

        public OptionsForm(AppConfig cfg)
        {
            _cfg = cfg;
            Text = "Options";
            FormBorderStyle = FormBorderStyle.None;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.None;
            MinimumSize = new Size(452, 592);
            Font = ModernTheme.FontBody;
            BackColor = ModernTheme.Bg;
            ClientSize = new Size(452, 592);
            DoubleBuffered = true;

            BuildHeader();

            int y = 70;

            CardPanel general = new CardPanel();
            general.SetBounds(16, y, 420, 132);
            Controls.Add(general);

            Label gTitle = SectionTitle("Général");
            gTitle.SetBounds(16, 12, 200, 20);
            general.Controls.Add(gTitle);

            _chkMinimizeTray = MakeCheck("Minimiser dans la zone de notification", cfg.MinimizeToTray);
            _chkMinimizeTray.SetBounds(16, 40, 388, 22);
            general.Controls.Add(_chkMinimizeTray);

            _chkStartup = MakeCheck("Lancer au démarrage de Windows", StartupHelper.IsEnabled());
            _chkStartup.SetBounds(16, 66, 388, 22);
            general.Controls.Add(_chkStartup);

            _chkHotkeys = MakeCheck("Activer les raccourcis clavier", cfg.HotkeysEnabled);
            _chkHotkeys.SetBounds(16, 92, 388, 22);
            _chkHotkeys.CheckedChanged += delegate { UpdateHotkeyFieldsEnabled(); };
            general.Controls.Add(_chkHotkeys);

            y += 144;

            CardPanel hk = new CardPanel();
            hk.SetBounds(16, y, 420, 288);
            Controls.Add(hk);

            Label hkTitle = SectionTitle("Raccourcis clavier");
            hkTitle.SetBounds(16, 12, 250, 20);
            hk.Controls.Add(hkTitle);

            int ry = 40;
            int row = 0;
            _txtToggle = AddHotkeyRow(hk, row++, "Basculer HDR / SDR", cfg.HotkeyToggleHdr, ref ry);
            _txtUp = AddHotkeyRow(hk, row++, "Luminosité +", cfg.HotkeySdrUp, ref ry);
            _txtDown = AddHotkeyRow(hk, row++, "Luminosité −", cfg.HotkeySdrDown, ref ry);
            _txtJour = AddHotkeyRow(hk, row++, "Profil Jour", cfg.HotkeyProfileJour, ref ry);
            _txtSoir = AddHotkeyRow(hk, row++, "Profil Soir", cfg.HotkeyProfileSoir, ref ry);
            _txtJeu = AddHotkeyRow(hk, row++, "Profil Jeu", cfg.HotkeyProfileJeu, ref ry);

            y += 300;

            Label tip = new Label();
            tip.Text = "Clique un champ puis appuie sur la combinaison. Échap = effacer.";
            tip.Font = ModernTheme.FontSmall;
            tip.ForeColor = ModernTheme.TextFaint;
            tip.BackColor = Color.Transparent;
            tip.SetBounds(20, y, 412, 18);
            Controls.Add(tip);
            y += 26;

            Button btnCancel = ModernTheme.MakePillButton("Annuler", false);
            btnCancel.Size = new Size(150, 36);
            btnCancel.Location = new Point(130, y);
            btnCancel.DialogResult = DialogResult.Cancel;
            btnCancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            Controls.Add(btnCancel);

            Button btnOk = ModernTheme.MakePillButton("Enregistrer", true);
            btnOk.Size = new Size(150, 36);
            btnOk.Location = new Point(288, y);
            btnOk.DialogResult = DialogResult.OK;
            btnOk.Click += delegate { Apply(); };
            Controls.Add(btnOk);

            AcceptButton = btnOk;
            CancelButton = null;

            Load += delegate { ModernTheme.ApplyRounded(this, 16, 1); };
            SizeChanged += delegate { ModernTheme.ApplyRounded(this, 16, 1); };

            UpdateHotkeyFieldsEnabled();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ModernTheme.ApplyRounded(this, 16, 1);
            Native.SetBorderColor(Handle, ModernTheme.Bg);
        }

        private void BuildHeader()
        {
            _header = new Panel();
            _header.SetBounds(0, 0, 452, 58);
            _header.BackColor = ModernTheme.Bg;
            _header.MouseDown += HeaderDown;
            _header.MouseMove += HeaderMove;
            _header.MouseUp += HeaderUp;
            Controls.Add(_header);

            Label title = new Label();
            title.Text = "Options";
            title.Font = new Font("Segoe UI", 11f, FontStyle.Bold);
            title.ForeColor = ModernTheme.Text;
            title.BackColor = Color.Transparent;
            title.SetBounds(20, 12, 200, 22);
            title.MouseDown += HeaderDown;
            title.MouseMove += HeaderMove;
            title.MouseUp += HeaderUp;
            _header.Controls.Add(title);

            Label sub = new Label();
            sub.Text = "Démarrage, zone de notification, raccourcis";
            sub.Font = new Font("Segoe UI", 7.5f, FontStyle.Regular);
            sub.ForeColor = ModernTheme.TextFaint;
            sub.BackColor = Color.Transparent;
            sub.SetBounds(21, 33, 320, 16);
            sub.MouseDown += HeaderDown;
            sub.MouseMove += HeaderMove;
            sub.MouseUp += HeaderUp;
            _header.Controls.Add(sub);

            Button btnClose = ModernTheme.MakeIconButton("✕", 9);
            btnClose.SetBounds(400, 12, 34, 34);
            btnClose.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            _header.Controls.Add(btnClose);
        }

        private void HeaderDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                _dragOffset = new Point(Cursor.Position.X - Location.X, Cursor.Position.Y - Location.Y);
                _header.Capture = true;
            }
        }

        private void HeaderMove(object sender, MouseEventArgs e)
        {
            if (_dragging)
            {
                if ((Control.MouseButtons & MouseButtons.Left) != MouseButtons.Left)
                {
                    _dragging = false;
                    _header.Capture = false;
                    return;
                }
                Location = new Point(Cursor.Position.X - _dragOffset.X, Cursor.Position.Y - _dragOffset.Y);
            }
        }

        private void HeaderUp(object sender, MouseEventArgs e)
        {
            _dragging = false;
            _header.Capture = false;
        }

        private static Label SectionTitle(string text)
        {
            Label l = new Label();
            l.Text = text.ToUpperInvariant();
            l.Font = new Font("Segoe UI", 8f, FontStyle.Bold);
            l.ForeColor = ModernTheme.TextFaint;
            l.BackColor = Color.Transparent;
            l.AutoSize = false;
            return l;
        }

        private static CheckBox MakeCheck(string text, bool value)
        {
            CheckBox c = new CheckBox();
            c.Text = text;
            c.Checked = value;
            c.AutoSize = false;
            c.Size = new Size(388, 22);
            c.ForeColor = ModernTheme.Text;
            c.BackColor = Color.Transparent;
            c.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            return c;
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                TextBox tb = ActiveControl as TextBox;
                if (tb != null && IsHotkeyBox(tb))
                {
                    tb.Text = "";
                    return true;
                }
                DialogResult = DialogResult.Cancel;
                Close();
                return true;
            }
            return base.ProcessDialogKey(keyData);
        }

        private bool IsHotkeyBox(TextBox tb)
        {
            return tb == _txtToggle || tb == _txtUp || tb == _txtDown ||
                   tb == _txtJour || tb == _txtSoir || tb == _txtJeu;
        }

        private TextBox AddHotkeyRow(Control parent, int index, string label, string value, ref int y)
        {
            Label lb = new Label();
            lb.Text = label;
            lb.AutoSize = false;
            lb.ForeColor = ModernTheme.TextDim;
            lb.BackColor = Color.Transparent;
            lb.Font = new Font("Segoe UI", 9f, FontStyle.Regular);
            lb.TextAlign = ContentAlignment.MiddleLeft;
            lb.SetBounds(16, y, 140, 28);
            _hotkeyLabels[index] = lb;
            parent.Controls.Add(lb);

            TextBox tb = new TextBox();
            tb.BorderStyle = BorderStyle.FixedSingle;
            tb.BackColor = ModernTheme.Card2;
            tb.ForeColor = ModernTheme.Text;
            tb.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            tb.TextAlign = HorizontalAlignment.Center;
            tb.SetBounds(164, y, 240, 28);
            tb.ReadOnly = true;
            tb.Text = value ?? "";
            tb.ShortcutsEnabled = false;
            tb.TabStop = true;
            tb.KeyDown += HotkeyField_KeyDown;
            tb.GotFocus += delegate { tb.BackColor = ModernTheme.AccentSoft; };
            tb.LostFocus += delegate { tb.BackColor = ModernTheme.Card2; };
            parent.Controls.Add(tb);

            y += 36;
            return tb;
        }

        private void UpdateHotkeyFieldsEnabled()
        {
            bool on = _chkHotkeys.Checked;
            TextBox[] boxes = new TextBox[] { _txtToggle, _txtUp, _txtDown, _txtJour, _txtSoir, _txtJeu };
            for (int i = 0; i < boxes.Length; i++)
            {
                boxes[i].Enabled = on;
                boxes[i].BackColor = on ? ModernTheme.Card2 : ModernTheme.BgAlt;
                boxes[i].ForeColor = on ? ModernTheme.Text : ModernTheme.TextFaint;
                if (_hotkeyLabels[i] != null)
                    _hotkeyLabels[i].ForeColor = on ? ModernTheme.TextDim : ModernTheme.TextFaint;
            }
        }

        private static void HotkeyField_KeyDown(object sender, KeyEventArgs e)
        {
            TextBox tb = sender as TextBox;
            if (tb == null) return;

            e.SuppressKeyPress = true;
            e.Handled = true;

            if (e.KeyCode == Keys.Escape)
            {
                tb.Text = "";
                return;
            }

            if (e.KeyCode == Keys.ControlKey || e.KeyCode == Keys.ShiftKey ||
                e.KeyCode == Keys.Menu || e.KeyCode == Keys.LWin || e.KeyCode == Keys.RWin)
                return;

            tb.Text = HotkeyParser.Format(e.Modifiers, e.KeyCode);
        }

        private void Apply()
        {
            _cfg.MinimizeToTray = _chkMinimizeTray.Checked;
            _cfg.StartWithWindows = _chkStartup.Checked;
            _cfg.HotkeysEnabled = _chkHotkeys.Checked;
            _cfg.HotkeyToggleHdr = _txtToggle.Text.Trim();
            _cfg.HotkeySdrUp = _txtUp.Text.Trim();
            _cfg.HotkeySdrDown = _txtDown.Text.Trim();
            _cfg.HotkeyProfileJour = _txtJour.Text.Trim();
            _cfg.HotkeyProfileSoir = _txtSoir.Text.Trim();
            _cfg.HotkeyProfileJeu = _txtJeu.Text.Trim();
            StartupHelper.SetEnabled(_cfg.StartWithWindows);
            _cfg.Save();
        }
    }
}
