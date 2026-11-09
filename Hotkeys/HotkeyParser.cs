using System.Windows.Forms;
using Sideclip.Native;

namespace Sideclip.Hotkeys;

internal static class HotkeyParser
{
    public static bool TryParse(string? hotkey, out uint modifiers, out uint vk)
    {
        modifiers = 0;
        vk = 0;
        if (string.IsNullOrWhiteSpace(hotkey))
            return false;

        Keys key = Keys.None;
        foreach (var raw in hotkey.Split('+'))
        {
            var p = raw.Trim().ToUpperInvariant();
            if (p is "CTRL" or "CONTROL" or "STRG")
                modifiers |= NativeMethods.ModControl;
            else if (p == "SHIFT")
                modifiers |= NativeMethods.ModShift;
            else if (p == "ALT")
                modifiers |= NativeMethods.ModAlt;
            else if (p is "WIN" or "WINDOWS" or "LWIN" or "RWIN")
                modifiers |= NativeMethods.ModWin;
            else if (p.Length == 1 && char.IsDigit(p[0]) && Enum.TryParse("D" + p, true, out Keys dKey))
                key = dKey;
            else if (Enum.TryParse(p, true, out Keys k) && !int.TryParse(p, out _))
                key = k;
        }

        if (key == Keys.None)
            return false;

        vk = (uint)key;
        return true;
    }

    public static bool IsForbidden(string? hotkey) =>
        Normalize(hotkey) is "Win+V" or "Ctrl+Win+V" or "Ctrl+Win+P";

    public static string? NormalizedOrError(string? hotkey)
    {
        if (string.IsNullOrWhiteSpace(hotkey)) return null;
        if (IsForbidden(hotkey)) return "Reserved Windows hotkeys cannot be assigned.";
        return hotkey;
    }

    public static string Normalize(string? hotkey)
    {
        if (!TryParse(hotkey, out var mods, out var vk)) return string.Empty;
        var parts = new List<string>();
        if ((mods & NativeMethods.ModControl) != 0) parts.Add("Ctrl");
        if ((mods & NativeMethods.ModShift) != 0) parts.Add("Shift");
        if ((mods & NativeMethods.ModAlt) != 0) parts.Add("Alt");
        if ((mods & NativeMethods.ModWin) != 0) parts.Add("Win");
        var key = ((System.Windows.Forms.Keys)vk).ToString();
        if (key.Length == 2 && key[0] == 'D' && char.IsDigit(key[1])) key = key[1].ToString();
        parts.Add(key);
        return string.Join("+", parts);
    }

    public static string FromKeyEvent(KeyEventArgs e, bool winDown)
    {
        if (e.KeyCode is Keys.Menu or Keys.ControlKey or Keys.ShiftKey or Keys.LWin or Keys.RWin)
            return string.Empty;

        var parts = new List<string>();
        if (e.Control) parts.Add("Ctrl");
        if (e.Shift) parts.Add("Shift");
        if (e.Alt) parts.Add("Alt");
        if (winDown) parts.Add("Win");

        var keyStr = e.KeyCode.ToString();
        if (e.KeyCode >= Keys.D0 && e.KeyCode <= Keys.D9)
            keyStr = keyStr[1..];

        if (parts.Count == 0)
            return keyStr;
        return string.Join("+", parts) + "+" + keyStr;
    }
}
