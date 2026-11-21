namespace Hdr2Sdr.Core;

/// <summary>
/// Pure Win32 virtual-key chord parser and formatter (no WinForms dependency).
/// </summary>
public static class HotkeyParser
{
    public static bool TryParse(string? text, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var parts = text.Split('+', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
            return false;

        foreach (var raw in parts)
        {
            switch (raw.ToLowerInvariant())
            {
                case "ctrl":
                case "control":
                    modifiers |= NativeMethods.MOD_CONTROL;
                    continue;
                case "alt":
                    modifiers |= NativeMethods.MOD_ALT;
                    continue;
                case "shift":
                    modifiers |= NativeMethods.MOD_SHIFT;
                    continue;
                case "win":
                case "windows":
                    modifiers |= NativeMethods.MOD_WIN;
                    continue;
            }

            if (!TryParseKey(raw, out var parsedVk))
                return false;

            vk = parsedVk;
        }

        return vk != 0;
    }

    public static string Format(bool ctrl, bool alt, bool shift, bool win, uint vk)
    {
        var keyName = FormatKey(vk);
        if (string.IsNullOrEmpty(keyName))
            return string.Empty;

        var parts = new List<string>(5);
        if (ctrl) parts.Add("Ctrl");
        if (alt) parts.Add("Alt");
        if (shift) parts.Add("Shift");
        if (win) parts.Add("Win");
        parts.Add(keyName);
        return string.Join("+", parts);
    }

    public static bool IsModifierVk(uint vk) =>
        vk is 0x10 or 0x11 or 0x12 or 0xA0 or 0xA1 or 0xA2 or 0xA3 or 0xA4 or 0xA5 or 0x5B or 0x5C;

    private static bool TryParseKey(string token, out uint vk)
    {
        vk = 0;
        if (token.Length == 1)
        {
            char c = char.ToUpperInvariant(token[0]);
            if (c is >= 'A' and <= 'Z')
            {
                vk = c;
                return true;
            }
            if (c is >= '0' and <= '9')
            {
                vk = c;
                return true;
            }
        }

        if (token.Length == 2 && (token[0] == 'D' || token[0] == 'd') && char.IsAsciiDigit(token[1]))
        {
            vk = token[1];
            return true;
        }

        if (token.StartsWith("F", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(token[1..], out int fn) && fn is >= 1 and <= 24)
        {
            vk = (uint)(0x70 + fn - 1);
            return true;
        }

        if (token.StartsWith("NumPad", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(token[6..], out int np) && np is >= 0 and <= 9)
        {
            vk = (uint)(0x60 + np);
            return true;
        }

        vk = token.ToLowerInvariant() switch
        {
            "up" => 0x26,
            "down" => 0x28,
            "left" => 0x25,
            "right" => 0x27,
            "space" => 0x20,
            "enter" or "return" => 0x0D,
            "tab" => 0x09,
            "home" => 0x24,
            "end" => 0x23,
            "pageup" or "prior" => 0x21,
            "pagedown" or "next" => 0x22,
            "insert" => 0x2D,
            "delete" => 0x2E,
            "add" or "plus" => 0x6B,
            "subtract" or "minus" => 0x6D,
            "oemPlus" or "oemplus" => 0xBB,
            "oemMinus" or "oemminus" => 0xBD,
            _ => 0
        };

        return vk != 0;
    }

    private static string FormatKey(uint vk)
    {
        if (vk is >= (uint)'A' and <= (uint)'Z')
            return ((char)vk).ToString();

        if (vk is >= (uint)'0' and <= (uint)'9')
            return ((char)vk).ToString();

        if (vk is >= 0x70 and <= 0x87)
            return $"F{vk - 0x70 + 1}";

        if (vk is >= 0x60 and <= 0x69)
            return $"NumPad{vk - 0x60}";

        return vk switch
        {
            0x26 => "Up",
            0x28 => "Down",
            0x25 => "Left",
            0x27 => "Right",
            0x20 => "Space",
            0x0D => "Enter",
            0x09 => "Tab",
            0x24 => "Home",
            0x23 => "End",
            0x21 => "PageUp",
            0x22 => "PageDown",
            0x2D => "Insert",
            0x2E => "Delete",
            0x6B => "Add",
            0x6D => "Subtract",
            _ => string.Empty
        };
    }
}
