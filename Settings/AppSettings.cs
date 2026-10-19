using System.Text.Json.Serialization;

namespace Sideclip.Settings;

internal sealed class AppSettings
{
    /// <summary>Cap value meaning "keep everything".</summary>
    public const int Unlimited = 0;

    /// <summary>
    /// Defaults for a brand-new profile only. Existing profiles are migrated to
    /// Unlimited (see SettingsManager.Load).
    /// </summary>
    public const int DefaultMaxTextClips = 100;
    public const int DefaultMaxImageClips = 25;

    public const int MinCap = 1;
    public const int MaxTextCap = 100000;
    public const int MaxImageCap = 10000;

    public int TtlSeconds { get; set; } = 12;
    public bool TtlEnabled { get; set; } = true;
    public bool Autostart { get; set; }
    public bool ShowDebugWindow { get; set; }
    public string PickerHotkey { get; set; } = "Ctrl+Shift+Win+V";
    public string ScreenshotHotkey { get; set; } = "Ctrl+Shift+Win+P";

    /// <summary>Max stored non-pinned text clips. 0 = Unlimited.</summary>
    public int MaxTextClips { get; set; } = DefaultMaxTextClips;

    /// <summary>Max stored non-pinned image clips. 0 = Unlimited.</summary>
    public int MaxImageClips { get; set; } = DefaultMaxImageClips;

    /// <summary>
    /// Set by Load when the cap values were decided there (fresh or migrated
    /// profile) and should be written to settings.json once, so the decision is
    /// made exactly once. Not serialized.
    /// </summary>
    [JsonIgnore]
    public bool CapsNeedPersist { get; set; }

    /// <summary>
    /// Out-of-range or non-positive stored values become Unlimited, never a
    /// small number: a bad value must not be able to delete clips.
    /// </summary>
    public static int NormalizeCap(int value, int max) =>
        value >= MinCap && value <= max ? value : Unlimited;
}