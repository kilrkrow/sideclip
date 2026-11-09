namespace Sideclip.Picker;

/// <summary>
/// Verified picker keyboard/copy help for the no-Arm redesign.
/// Strings are derived from the actual OnPreviewKeyDown / activation paths,
/// not from assumed standard ListBox behavior.
/// </summary>
internal static class PickerNoArmHelp
{
    // Verified in PickerWindow.OnPreviewKeyDown / MoveSel:
    // Up/Down move one row with wrap; PageUp/PageDown move six with wrap;
    // Home/End first/last; type filters; Backspace removes one filter char;
    // Esc clears filter first, otherwise closes.
    public const string NavigationLine =
        "Nav: Up/Down one row (wrap); PageUp/PageDown six (wrap); Home/End; type to filter; Backspace; Esc clears filter, then closes.";

    // Verified: Enter and Shift+Enter map to Copy; mouse double-click (row/preview)
    // and the Copy button commit clipboard-only with paste:false. No Ctrl+V.
    public const string CopyLine =
        "Copy: Enter, Shift+Enter, double-click, or Copy (clipboard only; no Ctrl+V).";

    /// <summary>
    /// Formats the text-only type-out help line.
    /// When enabled: non-empty chord = configured shortcut; empty/whitespace = unassigned;
    /// null = unavailable (for example registration failed). Disabled ignores the chord.
    /// </summary>
    public static string FormatTypeOutLine(bool enabled, string? shortcutDisplay)
    {
        if (!enabled)
            return "Text-only type out: disabled.";
        if (shortcutDisplay is null)
            return "Text-only type out: unavailable.";
        if (string.IsNullOrWhiteSpace(shortcutDisplay))
            return "Text-only type out: unassigned.";
        return "Text-only type out: " + shortcutDisplay.Trim()
            + ". Select text, focus destination, then press shortcut.";
    }

    public static string ComposeCompactHelp(bool enabled, string? shortcutDisplay) =>
        NavigationLine + " " + CopyLine + " " + FormatTypeOutLine(enabled, shortcutDisplay);

    public static bool IsAscii(string text)
    {
        foreach (var ch in text)
        {
            if (ch > 0x7F)
                return false;
        }
        return true;
    }
}
