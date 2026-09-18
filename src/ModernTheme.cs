using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Hdr2Sdr
{
    internal enum AppTheme
    {
        Dark,
        Light
    }

    internal static class ModernTheme
    {
        private static AppTheme _current = AppTheme.Dark;

        public static AppTheme Current
        {
            get { return _current; }
        }

        public static void SetTheme(AppTheme t)
        {
            _current = t;
        }

        public static bool IsLight()
        {
            return _current == AppTheme.Light;
        }

        private static bool L()
        {
            return _current == AppTheme.Light;
        }

        private static Color C(int r, int g, int b)
        {
            return Color.FromArgb(r, g, b);
        }

        private static Color C(int a, int r, int g, int b)
        {
            return Color.FromArgb(a, r, g, b);
        }

        // ---------- palette (propriétés dynamiques : la peinture custom suit le thème) ----------

        public static Color Bg { get { return L() ? C(242, 242, 246) : C(15, 15, 19); } }
        public static Color BgAlt { get { return L() ? C(232, 232, 238) : C(20, 20, 25); } }
        public static Color Card { get { return L() ? C(255, 255, 255) : C(28, 28, 34); } }
        public static Color Card2 { get { return L() ? C(238, 238, 243) : C(34, 34, 42); } }
        public static Color Border { get { return L() ? C(222, 222, 230) : C(48, 48, 58); } }
        public static Color BorderLight { get { return L() ? C(200, 200, 212) : C(66, 66, 80); } }

        public static Color Text { get { return L() ? C(24, 24, 32) : C(242, 242, 247); } }
        public static Color TextDim { get { return L() ? C(92, 92, 104) : C(165, 165, 178); } }
        public static Color TextFaint { get { return L() ? C(140, 140, 152) : C(115, 115, 130); } }

        public static Color Accent { get { return C(124, 108, 255); } }
        public static Color AccentSoft { get { return L() ? C(228, 223, 255) : C(46, 40, 90); } }

        public static Color HdrOn { get { return C(255, 178, 36); } }
        public static Color HdrBadgeText { get { return L() ? C(150, 95, 0) : C(255, 178, 36); } }
        public static Color HdrOnBg { get { return L() ? C(255, 235, 195) : C(66, 48, 14); } }
        public static Color SdrBlue { get { return C(76, 194, 255); } }
        public static Color SdrBadgeText { get { return L() ? C(0, 110, 190) : C(76, 194, 255); } }
        public static Color SdrBlueBg { get { return L() ? C(213, 237, 255) : C(22, 52, 70); } }
        public static Color Success { get { return C(63, 185, 132); } }
        public static Color Danger { get { return C(255, 92, 108); } }

        public static Color TrackOff { get { return L() ? C(205, 205, 216) : C(58, 58, 70); } }
        public static Color SliderTrack { get { return L() ? C(220, 220, 229) : C(52, 52, 64); } }
        public static Color ThumbRing { get { return L() ? C(148, 148, 163) : C(200, 200, 210); } }
        public static Color BubbleNormal { get { return L() ? C(228, 228, 236) : C(50, 50, 62); } }

        public static Color SecondaryBtn { get { return L() ? C(232, 232, 238) : C(44, 44, 54); } }
        public static Color IconBtnHover { get { return L() ? C(225, 225, 233) : C(45, 45, 55); } }
        public static Color IconBtnDown { get { return L() ? C(210, 210, 221) : C(55, 55, 68); } }

        public static Font FontTitle
        {
            get { return new Font("Segoe UI Variable Display", 15f, FontStyle.Bold); }
        }

        public static Font FontSubtitle
        {
            get { return new Font("Segoe UI", 8.5f, FontStyle.Regular); }
        }

        public static Font FontCardTitle
        {
            get { return new Font("Segoe UI", 9f, FontStyle.Bold); }
        }

        public static Font FontBody
        {
            get { return new Font("Segoe UI", 9f, FontStyle.Regular); }
        }

        public static Font FontSmall
        {
            get { return new Font("Segoe UI", 8f, FontStyle.Regular); }
        }

        public static Font FontBigNumber
        {
            get { return new Font("Segoe UI Variable Display", 34f, FontStyle.Bold); }
        }

        public static GraphicsPath RoundedRect(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            if (d > bounds.Width) d = bounds.Width;
            if (d > bounds.Height) d = bounds.Height;
            if (d <= 0)
            {
                path.AddRectangle(bounds);
                return path;
            }
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void ApplyRounded(Control c, int radius)
        {
            ApplyRounded(c, radius, 0);
        }

        /// <summary>
        /// inset=1 pour une fenêtre : la région polygonalisée crénèle les arcs
        /// et laisse passer le fond (encoches noires sur fond sombre). En
        /// retrait d'1px, le bord tombe toujours sur du peint opaque.
        /// </summary>
        public static void ApplyRounded(Control c, int radius, int inset)
        {
            try
            {
                Rectangle r = new Rectangle(inset, inset, c.Width - inset * 2, c.Height - inset * 2);
                using (GraphicsPath p = RoundedRect(r, radius))
                {
                    c.Region = new Region(p);
                }
            }
            catch
            {
            }
        }

        public static Button MakeIconButton(string text, int size)
        {
            Button b = new Button();
            b.Text = text;
            b.Font = new Font("Segoe UI Symbol", (float)size, FontStyle.Regular);
            b.ForeColor = TextDim;
            b.BackColor = Color.Transparent;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = IconBtnHover;
            b.FlatAppearance.MouseDownBackColor = IconBtnDown;
            b.Cursor = Cursors.Hand;
            b.TabStop = false;
            return b;
        }

        public static Button MakePillButton(string text, bool primary)
        {
            Button b = new Button();
            b.Text = text;
            b.Font = new Font("Segoe UI", 9f, FontStyle.Bold);
            b.Cursor = Cursors.Hand;
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.TabStop = false;
            if (primary)
            {
                b.ForeColor = Color.White;
                b.BackColor = Accent;
            }
            else
            {
                b.ForeColor = Text;
                b.BackColor = SecondaryBtn;
            }
            return b;
        }

        // ---------- bascule de thème : recolorie les snapshots pris à la création ----------

        private static Color DarkBgAlt() { return C(20, 20, 25); }
        private static Color LightBgAlt() { return C(232, 232, 238); }
        private static Color DarkCard() { return C(28, 28, 34); }
        private static Color LightCard() { return C(255, 255, 255); }
        private static Color DarkCard2() { return C(34, 34, 42); }
        private static Color LightCard2() { return C(238, 238, 243); }
        private static Color DarkBorder() { return C(48, 48, 58); }
        private static Color LightBorder() { return C(222, 222, 230); }
        private static Color DarkText() { return C(242, 242, 247); }
        private static Color LightText() { return C(24, 24, 32); }
        private static Color DarkTextDim() { return C(165, 165, 178); }
        private static Color LightTextDim() { return C(92, 92, 104); }
        private static Color DarkTextFaint() { return C(115, 115, 130); }
        private static Color LightTextFaint() { return C(140, 140, 152); }
        private static Color DarkAccentSoft() { return C(46, 40, 90); }
        private static Color LightAccentSoft() { return C(228, 223, 255); }
        private static Color DarkHdrBg() { return C(66, 48, 14); }
        private static Color LightHdrBg() { return C(255, 235, 195); }
        private static Color DarkSdrBg() { return C(22, 52, 70); }
        private static Color LightSdrBg() { return C(213, 237, 255); }
        private static Color DarkHdrTxt() { return C(255, 178, 36); }
        private static Color LightHdrTxt() { return C(150, 95, 0); }
        private static Color DarkSdrTxt() { return C(76, 194, 255); }
        private static Color LightSdrTxt() { return C(0, 110, 190); }
        private static Color DarkSecBtn() { return C(44, 44, 54); }
        private static Color LightSecBtn() { return C(232, 232, 238); }
        private static Color DarkHover() { return C(45, 45, 55); }
        private static Color LightHover() { return C(225, 225, 233); }
        private static Color DarkDown() { return C(55, 55, 68); }
        private static Color LightDown() { return C(210, 210, 221); }

        private static Color MapOne(Color c, Color dark, Color light, Color now)
        {
            if (c.ToArgb() == dark.ToArgb() || c.ToArgb() == light.ToArgb())
                return now;
            return c;
        }

        public static Color MapColor(Color c)
        {
            if (c.ToArgb() == Color.Transparent.ToArgb()) return c;
            Color r = c;
            // Blanc pur = carte claire (les contrôles custom figent aussi du blanc
            // via l'ambient). Le seul blanc à préserver (texte bouton primaire)
            // est géré explicitement dans RemapControl.
            r = MapOne(r, C(255, 255, 255), C(255, 255, 255), Card);
            r = MapOne(r, C(15, 15, 19), C(242, 242, 246), Bg);
            r = MapOne(r, DarkBgAlt(), LightBgAlt(), BgAlt);
            r = MapOne(r, DarkCard(), LightCard(), Card);
            r = MapOne(r, DarkCard2(), LightCard2(), Card2);
            r = MapOne(r, DarkBorder(), LightBorder(), Border);
            r = MapOne(r, C(66, 66, 80), C(200, 200, 212), BorderLight);
            r = MapOne(r, DarkText(), LightText(), Text);
            r = MapOne(r, DarkTextDim(), LightTextDim(), TextDim);
            r = MapOne(r, DarkTextFaint(), LightTextFaint(), TextFaint);
            r = MapOne(r, DarkAccentSoft(), LightAccentSoft(), AccentSoft);
            r = MapOne(r, DarkHdrBg(), LightHdrBg(), HdrOnBg);
            r = MapOne(r, DarkSdrBg(), LightSdrBg(), SdrBlueBg);
            r = MapOne(r, DarkHdrTxt(), LightHdrTxt(), HdrBadgeText);
            r = MapOne(r, DarkSdrTxt(), LightSdrTxt(), SdrBadgeText);
            r = MapOne(r, DarkSecBtn(), LightSecBtn(), SecondaryBtn);
            r = MapOne(r, DarkHover(), LightHover(), IconBtnHover);
            r = MapOne(r, DarkDown(), LightDown(), IconBtnDown);
            return r;
        }

        public static void RemapControlTree(Control root)
        {
            RemapControl(root);
        }

        private static void RemapControl(Control c)
        {
            Button b = c as Button;
            bool primary = b != null && b.BackColor.ToArgb() == Accent.ToArgb();
            if (primary)
            {
                // Bouton primaire : fond Accent + texte blanc dans les deux thèmes
                // (on ne touche pas aux survols par défaut).
                b.ForeColor = Color.White;
            }
            else
            {
                c.ForeColor = MapColor(c.ForeColor);
                if (c.BackColor.ToArgb() != Color.Transparent.ToArgb())
                    c.BackColor = MapColor(c.BackColor);
                if (b != null)
                {
                    b.FlatAppearance.MouseOverBackColor = MapColor(b.FlatAppearance.MouseOverBackColor);
                    b.FlatAppearance.MouseDownBackColor = MapColor(b.FlatAppearance.MouseDownBackColor);
                }
            }
            CardPanel cp = c as CardPanel;
            if (cp != null)
                cp.BorderColor = MapColor(cp.BorderColor);
            foreach (Control ch in c.Controls)
                RemapControl(ch);
            c.Invalidate();
        }
    }
}
