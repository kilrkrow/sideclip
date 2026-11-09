using System.Windows.Forms;
using Sideclip.Hotkeys;

namespace Sideclip.Settings;

internal readonly record struct HotkeyAssignments(string Picker, string Screenshot, string TypeOut, bool Enabled)
{
    public static bool TryCreate(string? picker, string? screenshot, string? typeOut, bool enabled,
        out HotkeyAssignments assignments, out string? error)
    {
        assignments = default;
        error = null;
        var values = new[] { picker, screenshot, typeOut };
        var names = new[] { "Picker", "Screenshot path", "Type-out" };
        for (var i = 0; i < values.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(values[i])) continue;
            if (!StrictParse(values[i]!, out var key)) { error = names[i] + " needs a valid non-modifier key, or be blank."; return false; }
            if (HotkeyParser.IsForbidden(values[i]) || key == (uint)Keys.F12 || HotkeyParser.Normalize(values[i]) == "Escape")
            { error = names[i] + " uses a reserved hotkey. Win+V, Ctrl+Win+V, Ctrl+Win+P, F12 and bare Esc cannot be assigned."; return false; }
        }
        var normalized = values.Select(HotkeyParser.Normalize).ToArray();
        var nonempty = normalized.Where(value => value.Length > 0).ToArray();
        if (nonempty.Distinct(StringComparer.OrdinalIgnoreCase).Count() != nonempty.Length)
        { error = "Picker, screenshot path and type-out must use distinct hotkeys."; return false; }
        assignments = new(normalized[0], normalized[1], normalized[2], enabled);
        return true;
    }

    private static bool StrictParse(string text, out uint vk)
    {
        vk = 0;
        var keys = 0;
        foreach (var raw in text.Split('+'))
        {
            var token = raw.Trim().ToUpperInvariant();
            if (token is "CTRL" or "CONTROL" or "STRG" or "SHIFT" or "ALT" or "WIN" or "WINDOWS" or "LWIN" or "RWIN") continue;
            if (token.Length == 0) return false;
            if (token.Length == 1 && char.IsDigit(token[0])) { keys++; continue; }
            if (!Enum.TryParse(token, true, out Keys key) || int.TryParse(token, out _) || (uint)key > 255
                || key is Keys.None or Keys.ControlKey or Keys.ShiftKey or Keys.Menu or Keys.LControlKey or Keys.RControlKey or Keys.LShiftKey or Keys.RShiftKey or Keys.LMenu or Keys.RMenu) return false;
            keys++;
        }
        return keys == 1 && HotkeyParser.TryParse(text, out _, out vk) && vk is > 0 and < 256;
    }
}
