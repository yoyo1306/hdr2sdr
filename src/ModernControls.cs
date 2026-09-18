using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace Hdr2Sdr
{
    internal sealed class CardPanel : Panel
    {
        private int _radius = 14;
        private Color _border = ModernTheme.Border;

        public int CornerRadius
        {
            get { return _radius; }
            set { _radius = value; }
        }

        public Color BorderColor
        {
            get { return _border; }
            set { _border = value; }
        }

        public CardPanel()
        {
            // Peinture de fond NATIF conservée (pas de suppression) + arrondi
            // dessiné par-dessus : aucun pixel non peint possible.
            BackColor = ModernTheme.Card;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Padding = new Padding(16);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color behind = Parent != null ? Parent.BackColor : ModernTheme.Bg;
            using (SolidBrush bgfill = new SolidBrush(behind))
            {
                e.Graphics.FillRectangle(bgfill, new Rectangle(0, 0, Width, Height));
            }
            using (GraphicsPath path = ModernTheme.RoundedRect(new Rectangle(0, 0, Width, Height), _radius))
            using (SolidBrush brush = new SolidBrush(BackColor))
            {
                e.Graphics.FillPath(brush, path);
            }
            using (GraphicsPath path = ModernTheme.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), _radius))
            using (Pen pen = new Pen(_border, 1f))
            {
                e.Graphics.DrawPath(pen, path);
            }
        }
    }

    internal sealed class ToggleSwitch : Control
    {
        private bool _checked;
        private float _anim;
        private Timer _timer;
        private bool _target;

        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get { return _checked; }
            set
            {
                if (_checked == value)
                {
                    _anim = value ? 1f : 0f;
                    Invalidate();
                    return;
                }
                _checked = value;
                _target = value;
                StartAnim();
                if (CheckedChanged != null)
                    CheckedChanged(this, EventArgs.Empty);
                Invalidate();
            }
        }

        public ToggleSwitch()
        {
            Size = new Size(52, 28);
            MinimumSize = new Size(52, 28);
            MaximumSize = new Size(52, 28);
            Cursor = Cursors.Hand;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer, true);
            _timer = new Timer();
            _timer.Interval = 12;
            _timer.Tick += delegate
            {
                float goal = _target ? 1f : 0f;
                if (Math.Abs(_anim - goal) < 0.08f)
                {
                    _anim = goal;
                    _timer.Stop();
                }
                else if (_anim < goal)
                {
                    _anim += 0.14f;
                }
                else
                {
                    _anim -= 0.14f;
                }
                Invalidate();
            };
        }

        public void SetCheckedSilent(bool value)
        {
            _checked = value;
            _target = value;
            _anim = value ? 1f : 0f;
            _timer.Stop();
            Invalidate();
        }

        private void StartAnim()
        {
            if (!_timer.Enabled)
                _timer.Start();
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Checked = !_checked;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent != null ? Parent.BackColor : ModernTheme.Bg);
            Rectangle track = new Rectangle(0, 0, Width - 1, Height - 1);
            Color on1 = ModernTheme.HdrOn;
            Color off1 = ModernTheme.TrackOff;
            Color c0 = off1;
            if (_anim > 0f)
            {
                int r = (int)(off1.R + (on1.R - off1.R) * _anim);
                int g = (int)(off1.G + (on1.G - off1.G) * _anim);
                int b = (int)(off1.B + (on1.B - off1.B) * _anim);
                c0 = Color.FromArgb(r, g, b);
            }
            using (GraphicsPath path = ModernTheme.RoundedRect(new Rectangle(0, 0, Width, Height), Height / 2))
            using (SolidBrush brush = new SolidBrush(c0))
            {
                e.Graphics.FillPath(brush, path);
            }
            int knobSize = Height - 8;
            int x = 4 + (int)((Width - knobSize - 8) * _anim);
            Rectangle knob = new Rectangle(x, 4, knobSize, knobSize);
            using (SolidBrush brush = new SolidBrush(Color.White))
            {
                e.Graphics.FillEllipse(brush, knob);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _timer != null)
            {
                _timer.Stop();
                _timer.Dispose();
                _timer = null;
            }
            base.Dispose(disposing);
        }
    }

    internal sealed class ModernSlider : Control
    {
        private int _min = 0;
        private int _max = 100;
        private int _value = 50;
        private bool _dragging;
        private bool _hover;

        public event EventHandler ValueChanged;

        public int Minimum
        {
            get { return _min; }
            set { _min = value; Invalidate(); }
        }

        public int Maximum
        {
            get { return _max; }
            set { _max = value; Invalidate(); }
        }

        public int Value
        {
            get { return _value; }
            set
            {
                int v = value;
                if (v < _min) v = _min;
                if (v > _max) v = _max;
                if (v == _value)
                {
                    Invalidate();
                    return;
                }
                _value = v;
                Invalidate();
                if (ValueChanged != null)
                    ValueChanged(this, EventArgs.Empty);
            }
        }

        public void SetValueSilent(int v)
        {
            if (v < _min) v = _min;
            if (v > _max) v = _max;
            _value = v;
            Invalidate();
        }

        public ModernSlider()
        {
            Height = 38;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            Cursor = Cursors.Hand;
            TabStop = true;
        }

        private float Ratio()
        {
            if (_max <= _min) return 0f;
            return (float)(_value - _min) / (float)(_max - _min);
        }

        private void SetFromX(int x)
        {
            int pad = 12;
            int w = Width - pad * 2;
            if (w <= 0) return;
            float r = (float)(x - pad) / (float)w;
            if (r < 0f) r = 0f;
            if (r > 1f) r = 1f;
            Value = _min + (int)Math.Round(r * (_max - _min));
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _dragging = true;
                Capture = true;
                SetFromX(e.X);
                Focus();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_dragging)
                SetFromX(e.X);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _dragging = false;
            Capture = false;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int step = 5;
            if (e.Delta > 0) Value = _value + step;
            else Value = _value - step;
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (keyData == Keys.Left || keyData == Keys.Right ||
                keyData == Keys.Up || keyData == Keys.Down)
                return true;
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (e.KeyCode == Keys.Left || e.KeyCode == Keys.Down)
            {
                Value = _value - 2;
                e.Handled = true;
            }
            else if (e.KeyCode == Keys.Right || e.KeyCode == Keys.Up)
            {
                Value = _value + 2;
                e.Handled = true;
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color parentBg = Parent != null ? Parent.BackColor : ModernTheme.Bg;
            e.Graphics.Clear(parentBg);

            int pad = 12;
            int cy = Height / 2;
            Rectangle back = new Rectangle(pad, cy - 3, Width - pad * 2, 6);
            using (GraphicsPath path = ModernTheme.RoundedRect(back, 3))
            using (SolidBrush brush = new SolidBrush(ModernTheme.SliderTrack))
            {
                e.Graphics.FillPath(brush, path);
            }

            float r = Ratio();
            int fillW = (int)Math.Round(back.Width * r);
            if (fillW > 0)
            {
                Rectangle fill = new Rectangle(back.X, back.Y, fillW, back.Height);
                using (GraphicsPath path = ModernTheme.RoundedRect(fill, 3))
                using (LinearGradientBrush brush = new LinearGradientBrush(
                    back, ModernTheme.Accent, Color.FromArgb(76, 194, 255),
                    LinearGradientMode.Horizontal))
                {
                    e.Graphics.FillPath(brush, path);
                }
            }

            int thumbX = back.X + fillW;
            int thumbR = _hover || _dragging ? 10 : 8;
            Rectangle thumb = new Rectangle(thumbX - thumbR, cy - thumbR, thumbR * 2, thumbR * 2);
            using (SolidBrush shadow = new SolidBrush(Color.FromArgb(60, 0, 0, 0)))
            {
                e.Graphics.FillEllipse(shadow, new Rectangle(thumb.X, thumb.Y + 2, thumb.Width, thumb.Height));
            }
            using (SolidBrush brush = new SolidBrush(Color.White))
            {
                e.Graphics.FillEllipse(brush, thumb);
            }
            using (Pen pen = new Pen(_dragging ? ModernTheme.Accent : ModernTheme.ThumbRing, 2f))
            {
                e.Graphics.DrawEllipse(pen, thumb);
            }
        }
    }

    internal sealed class ProfileCard : Control
    {
        private bool _selected;
        private bool _hover;
        private string _title = "";
        private string _desc = "";
        private string _icon = "";

        public string CardTitle
        {
            get { return _title; }
            set { _title = value; Invalidate(); }
        }

        public string CardDesc
        {
            get { return _desc; }
            set { _desc = value; Invalidate(); }
        }

        public string IconChar
        {
            get { return _icon; }
            set { _icon = value; Invalidate(); }
        }

        public bool Selected
        {
            get { return _selected; }
            set { _selected = value; Invalidate(); }
        }

        public ProfileCard()
        {
            Cursor = Cursors.Hand;
            DoubleBuffered = true;
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.Selectable, true);
            TabStop = true;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _hover = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _hover = false;
            Invalidate();
        }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            Focus();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(Parent != null ? Parent.BackColor : ModernTheme.Bg);

            Color bg = ModernTheme.Card;
            Color border = ModernTheme.Border;
            if (_selected)
            {
                bg = ModernTheme.AccentSoft;
                border = ModernTheme.Accent;
            }
            else if (_hover)
            {
                bg = ModernTheme.Card2;
                border = ModernTheme.BorderLight;
            }

            using (GraphicsPath path = ModernTheme.RoundedRect(new Rectangle(0, 0, Width, Height), 12))
            using (SolidBrush brush = new SolidBrush(bg))
            {
                e.Graphics.FillPath(brush, path);
            }
            using (GraphicsPath path = ModernTheme.RoundedRect(new Rectangle(0, 0, Width - 1, Height - 1), 12))
            using (Pen pen = new Pen(border, _selected ? 1.6f : 1f))
            {
                e.Graphics.DrawPath(pen, path);
            }

            // Icon bubble
            Rectangle iconBox = new Rectangle(10, 10, 30, 30);
            Color bubble = _selected ? ModernTheme.Accent : ModernTheme.BubbleNormal;
            using (SolidBrush brush = new SolidBrush(bubble))
            {
                e.Graphics.FillEllipse(brush, iconBox);
            }
            using (SolidBrush brush = new SolidBrush(_selected ? Color.White : ModernTheme.Text))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                using (Font f = new Font("Segoe UI Symbol", 13f, FontStyle.Regular))
                {
                    e.Graphics.DrawString(_icon ?? "", f, brush, iconBox, sf);
                }
            }

            using (SolidBrush tb = new SolidBrush(ModernTheme.Text))
            using (SolidBrush db = new SolidBrush(ModernTheme.TextDim))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Near;
                sf.LineAlignment = StringAlignment.Near;
                sf.Trimming = StringTrimming.EllipsisCharacter;
                Rectangle titleR = new Rectangle(10, 46, Width - 20, 20);
                Rectangle descR = new Rectangle(10, 64, Width - 20, 30);
                using (Font f = new Font("Segoe UI", 9f, FontStyle.Bold))
                {
                    e.Graphics.DrawString(_title ?? "", f, tb, titleR, sf);
                }
                using (Font f = new Font("Segoe UI", 7.5f, FontStyle.Regular))
                {
                    e.Graphics.DrawString(_desc ?? "", f, db, descR, sf);
                }
            }
        }
    }
}
