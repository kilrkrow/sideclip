namespace Sideclip.Settings;

internal sealed class AppSettings
{
    public int TtlSeconds { get; set; } = 12;
    public bool TtlEnabled { get; set; } = true;
    public bool Autostart { get; set; }
    public bool ShowDebugWindow { get; set; }
    public string PickerHotkey { get; set; } = "Ctrl+Shift+Win+V";
    public string ScreenshotHotkey { get; set; } = "Ctrl+Shift+Win+P";

    /// <summary>Also copy every new file in the Screenshots folder to <see cref="ScreenshotCopyFolder"/>.</summary>
    public bool ScreenshotCopyEnabled { get; set; }
    public string ScreenshotCopyFolder { get; set; } = "";

    /// <summary>Add each copied screenshot to today's Obsidian daily note. Only active while the copy is on.</summary>
    public bool DailyNoteEnabled { get; set; }
    public string DailyNoteVault { get; set; } = "";
}
