using System;
using System.Globalization;
using System.Windows.Forms;

namespace Hdr2Sdr
{
    internal static class HotkeyParser
    {
        public static bool TryParse(string text, out uint modifiers, out Keys key)
        {
            modifiers = 0;
            key = Keys.None;
            if (string.IsNullOrEmpty(text))
                return false;

            string[] parts = text.Split(new char[] { '+' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return false;

            for (int i = 0; i < parts.Length - 1; i++)
            {
                string p = parts[i].Trim().ToLowerInvariant();
                if (p == "ctrl" || p == "control")
                    modifiers |= Native.MOD_CONTROL;
                else if (p == "alt")
                    modifiers |= Native.MOD_ALT;
                else if (p == "shift")
                    modifiers |= Native.MOD_SHIFT;
                else if (p == "win" || p == "windows")
                    modifiers |= Native.MOD_WIN;
                else
                    return false;
            }

            string keyName = parts[parts.Length - 1].Trim();
            if (keyName.Length == 1 && char.IsDigit(keyName[0]))
            {
                key = (Keys)Enum.Parse(typeof(Keys), "D" + keyName);
                return true;
            }
            if (string.Equals(keyName, "Up", StringComparison.OrdinalIgnoreCase))
            {
                key = Keys.Up;
                return true;
            }
            if (string.Equals(keyName, "Down", StringComparison.OrdinalIgnoreCase))
            {
                key = Keys.Down;
                return true;
            }
            if (string.Equals(keyName, "Left", StringComparison.OrdinalIgnoreCase))
            {
                key = Keys.Left;
                return true;
            }
            if (string.Equals(keyName, "Right", StringComparison.OrdinalIgnoreCase))
            {
                key = Keys.Right;
                return true;
            }

            try
            {
                key = (Keys)Enum.Parse(typeof(Keys), keyName, true);
                return key != Keys.None;
            }
            catch
            {
                return false;
            }
        }

        public static string Format(Keys modifiers, Keys key)
        {
            return Format(modifiers, key, false);
        }

        public static string Format(Keys modifiers, Keys key, bool win)
        {
            System.Text.StringBuilder sb = new System.Text.StringBuilder();
            if ((modifiers & Keys.Control) == Keys.Control)
                sb.Append("Ctrl+");
            if ((modifiers & Keys.Alt) == Keys.Alt)
                sb.Append("Alt+");
            if ((modifiers & Keys.Shift) == Keys.Shift)
                sb.Append("Shift+");
            if (win)
                sb.Append("Win+");

            Keys code = key;
            if (code == Keys.Up) sb.Append("Up");
            else if (code == Keys.Down) sb.Append("Down");
            else if (code == Keys.Left) sb.Append("Left");
            else if (code == Keys.Right) sb.Append("Right");
            else
            {
                string name = code.ToString();
                if (name.Length == 2 && name[0] == 'D' && char.IsDigit(name[1]))
                    sb.Append(name[1]);
                else
                    sb.Append(name);
            }
            return sb.ToString();
        }
    }

    internal sealed class HotkeyWindow : NativeWindow, IDisposable
    {
        public event Action<int> HotkeyPressed;

        public HotkeyWindow()
        {
            CreateHandle(new CreateParams());
        }

        public bool Register(int id, string chord)
        {
            return Register(id, chord, false);
        }

        public bool Register(int id, string chord, bool allowRepeat)
        {
            int err;
            return Register(id, chord, allowRepeat, out err);
        }

        public bool Register(int id, string chord, bool allowRepeat, out int win32Error)
        {
            win32Error = 0;
            uint mods;
            Keys key;
            if (!HotkeyParser.TryParse(chord, out mods, out key))
                return false;
            if (!allowRepeat)
                mods |= Native.MOD_NOREPEAT;
            // Au boot (session logon), le handle peut ne pas exister encore
            // ou avoir été détruit (recréation) : le recréer garantit que
            // RegisterHotKey s'applique sur un HWND vivant du thread UI.
            EnsureHandle();
            if (Handle == IntPtr.Zero)
                return false;
            // Nettoie un enregistrement précédent sur cet id avant de
            // ré-enregistrer (ré-assertion idempotente, sans doublon).
            try { Native.UnregisterHotKey(Handle, id); }
            catch { }
            bool ok = Native.RegisterHotKey(Handle, id, mods, (uint)key);
            if (!ok)
            {
                try { win32Error = System.Runtime.InteropServices.Marshal.GetLastWin32Error(); }
                catch { win32Error = -1; }
            }
            return ok;
        }

        /// <summary>
        /// Recrée le handle s'il a été détruit (boot, changement de
        /// bureau, recréation). Sans handle vivant, RegisterHotKey réussit
        /// parfois mais aucun WM_HOTKEY n'arrive jamais.
        /// </summary>
        public void EnsureHandle()
        {
            try
            {
                if (Handle == IntPtr.Zero)
                    CreateHandle(new CreateParams());
            }
            catch
            {
            }
        }

        public void Unregister(int id)
        {
            try
            {
                if (Handle != IntPtr.Zero)
                    Native.UnregisterHotKey(Handle, id);
            }
            catch
            {
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_HOTKEY)
            {
                Action<int> handler = HotkeyPressed;
                if (handler != null)
                    handler.Invoke(m.WParam.ToInt32());
            }
            base.WndProc(ref m);
        }

        public void Dispose()
        {
            DestroyHandle();
        }
    }
}
