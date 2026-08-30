namespace Sideclip.Settings;

internal sealed class AppSettings
{
    public int TtlSeconds { get; set; } = 12;
    public bool Autostart { get; set; }
    public bool ShowDebugWindow { get; set; }
    public string PickerHotkey { get; set; } = "Ctrl+Shift+Win+V";
    public string ScreenshotHotkey { get; set; } = "Ctrl+Shift+Win+P";
}

