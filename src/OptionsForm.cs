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

        public OptionsForm(AppConfig cfg)
        {
            _cfg = cfg;
            Text = "Options";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            Font = SystemFonts.MessageBoxFont;

            const int left = 16;
            const int width = 420;
            int y = 16;

            _chkMinimizeTray = new CheckBox();
            _chkMinimizeTray.Text = "Minimiser dans la zone de notification";
            _chkMinimizeTray.AutoSize = true;
            _chkMinimizeTray.Location = new Point(left, y);
            _chkMinimizeTray.Checked = cfg.MinimizeToTray;
            y += 28;

            _chkStartup = new CheckBox();
            _chkStartup.Text = "Lancer au demarrage de Windows";
            _chkStartup.AutoSize = true;
            _chkStartup.Location = new Point(left, y);
            _chkStartup.Checked = StartupHelper.IsEnabled();
            y += 28;

            _chkHotkeys = new CheckBox();
            _chkHotkeys.Text = "Activer les raccourcis clavier";
            _chkHotkeys.AutoSize = true;
            _chkHotkeys.Location = new Point(left, y);
            _chkHotkeys.Checked = cfg.HotkeysEnabled;
            _chkHotkeys.CheckedChanged += delegate { UpdateHotkeyFieldsEnabled(); };
            y += 36;

            int row = 0;
            _txtToggle = AddHotkeyRow(row++, "Basculer HDR / SDR", cfg.HotkeyToggleHdr, left, ref y);
            _txtUp = AddHotkeyRow(row++, "Luminosite +", cfg.HotkeySdrUp, left, ref y);
            _txtDown = AddHotkeyRow(row++, "Luminosite -", cfg.HotkeySdrDown, left, ref y);
            _txtJour = AddHotkeyRow(row++, "Profil Jour", cfg.HotkeyProfileJour, left, ref y);
            _txtSoir = AddHotkeyRow(row++, "Profil Soir", cfg.HotkeyProfileSoir, left, ref y);
            _txtJeu = AddHotkeyRow(row++, "Profil Jeu", cfg.HotkeyProfileJeu, left, ref y);

            y += 8;
            Label tip = new Label();
            tip.AutoSize = false;
            tip.SetBounds(left, y, width - 32, 36);
            tip.Text = "Clique un champ puis appuie sur la combinaison.\r\nEchap = effacer.";
            y += 44;

            Button btnOk = new Button();
            btnOk.Text = "OK";
            btnOk.Size = new Size(88, 30);
            btnOk.Location = new Point(width - 32 - 88 - 12 - 88, y);
            btnOk.DialogResult = DialogResult.OK;
            btnOk.Click += delegate { Apply(); };

            Button btnCancel = new Button();
            btnCancel.Text = "Annuler";
            btnCancel.Size = new Size(88, 30);
            btnCancel.Location = new Point(width - 32 - 88, y);
            btnCancel.DialogResult = DialogResult.Cancel;

            y += 42;

            AcceptButton = btnOk;
            // Don't bind Escape to Cancel while editing hotkeys — handled in ProcessDialogKey
            CancelButton = null;
            btnCancel.Click += delegate { DialogResult = DialogResult.Cancel; Close(); };
            ClientSize = new Size(width, y + 8);

            Controls.Add(_chkMinimizeTray);
            Controls.Add(_chkStartup);
            Controls.Add(_chkHotkeys);
            Controls.Add(tip);
            Controls.Add(btnOk);
            Controls.Add(btnCancel);

            UpdateHotkeyFieldsEnabled();
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

        private TextBox AddHotkeyRow(int index, string label, string value, int left, ref int y)
        {
            Label lb = new Label();
            lb.Text = label;
            lb.AutoSize = false;
            lb.TextAlign = ContentAlignment.MiddleLeft;
            lb.SetBounds(left, y, 140, 26);
            _hotkeyLabels[index] = lb;
            Controls.Add(lb);

            TextBox tb = new TextBox();
            tb.BorderStyle = BorderStyle.FixedSingle;
            tb.SetBounds(left + 148, y, 240, 26);
            tb.ReadOnly = true;
            tb.BackColor = SystemColors.Window;
            tb.Text = value ?? "";
            tb.ShortcutsEnabled = false;
            tb.TabStop = true;
            tb.KeyDown += HotkeyField_KeyDown;
            Controls.Add(tb);

            y += 32;
            return tb;
        }

        private void UpdateHotkeyFieldsEnabled()
        {
            bool on = _chkHotkeys.Checked;
            TextBox[] boxes = new TextBox[] { _txtToggle, _txtUp, _txtDown, _txtJour, _txtSoir, _txtJeu };
            for (int i = 0; i < boxes.Length; i++)
            {
                boxes[i].Enabled = on;
                boxes[i].BackColor = on ? SystemColors.Window : SystemColors.Control;
                if (_hotkeyLabels[i] != null)
                    _hotkeyLabels[i].Enabled = on;
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
