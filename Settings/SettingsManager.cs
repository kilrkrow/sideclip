using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace Sideclip.Settings;

internal static class SettingsManager
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>
    /// Set this environment variable to redirect every file Sideclip reads or
    /// writes (settings.json, history.json, images) to another folder, e.g. for
    /// testing against a throwaway profile. When set, Sideclip also leaves the
    /// Windows "Start with Windows" registry value alone.
    /// </summary>
    public const string DataDirEnvVar = "SIDECLIP_DATA_DIR";

    /// <summary>Test hook; takes precedence over the environment variable.</summary>
    internal static string? DirOverride { get; set; }

    private static string? ResolveOverride()
    {
        var o = DirOverride ?? Environment.GetEnvironmentVariable(DataDirEnvVar);
        // Always fully resolved: a relative value must never silently fall
        // back to the real profile.
        return string.IsNullOrWhiteSpace(o) ? null : Path.GetFullPath(o.Trim());
    }

    public static bool IsDataDirOverridden => ResolveOverride() is not null;

    public static string Dir =>
        ResolveOverride()
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sideclip");

    public static string SettingsPath => Path.Combine(Dir, "settings.json");
    public static string HistoryPath => Path.Combine(Dir, "history.json");
    public static string ImagesDir => Path.Combine(Dir, "images");

    public static AppSettings Load()
    {
        // Decided before anything else: does this profile already hold clips?
        // If yes (or if we cannot tell) caps must not be set from defaults.
        var hadHistory = HasStoredHistory();
        var settingsFileExists = false;
        try
        {
            // Fresh install only: Start with Windows defaults ON.
            // Existing settings.json is never rewritten to flip Autostart.
            if (!File.Exists(SettingsPath))
                return FreshOrLegacy(hadHistory, persist: true);

            settingsFileExists = true;
            var json = File.ReadAllText(SettingsPath);
            var s = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts);
            if (s is not null)
            {
                if (json.IndexOf("TtlEnabled", StringComparison.OrdinalIgnoreCase) < 0)
                    s.TtlEnabled = true;
                if (s.TtlSeconds < 3) s.TtlSeconds = 12;
                if (s.TtlSeconds > 300) s.TtlSeconds = 300;
                ResolveCaps(s, json);
                return s;
            }
        }
        catch
        {
        }

        // Unreadable settings: treat as fresh defaults, but never let defaults
        // delete an existing history, and never overwrite the unreadable file.
        return FreshOrLegacy(hadHistory, persist: !settingsFileExists);
    }

    /// <summary>
    /// No usable settings file. A profile with no stored clips is brand new and
    /// gets the default caps. A profile that already holds clips (an install
    /// from before caps existed that never changed a setting) keeps everything:
    /// Unlimited.
    /// </summary>
    private static AppSettings FreshOrLegacy(bool hadHistory, bool persist)
    {
        var s = new AppSettings { Autostart = true, CapsNeedPersist = persist };
        if (hadHistory)
        {
            s.MaxTextClips = AppSettings.Unlimited;
            s.MaxImageClips = AppSettings.Unlimited;
        }

        return s;
    }

    /// <summary>
    /// An existing settings.json written before caps existed has no cap keys:
    /// Unlimited for each missing key (upgrade never trims). Present keys are
    /// honored; out-of-range values become Unlimited.
    /// </summary>
    private static void ResolveCaps(AppSettings s, string json)
    {
        if (json.IndexOf(nameof(AppSettings.MaxTextClips), StringComparison.OrdinalIgnoreCase) < 0)
        {
            s.MaxTextClips = AppSettings.Unlimited;
            s.CapsNeedPersist = true;
        }
        else
        {
            s.MaxTextClips = AppSettings.NormalizeCap(s.MaxTextClips, AppSettings.MaxTextCap);
        }

        if (json.IndexOf(nameof(AppSettings.MaxImageClips), StringComparison.OrdinalIgnoreCase) < 0)
        {
            s.MaxImageClips = AppSettings.Unlimited;
            s.CapsNeedPersist = true;
        }
        else
        {
            s.MaxImageClips = AppSettings.NormalizeCap(s.MaxImageClips, AppSettings.MaxImageCap);
        }
    }

    /// <summary>
    /// True if history.json holds clips or the images folder holds PNGs. Any
    /// error answers true: when unsure, assume there is data to protect.
    /// </summary>
    internal static bool HasStoredHistory()
    {
        try
        {
            if (File.Exists(HistoryPath))
            {
                var text = File.ReadAllText(HistoryPath).Trim().TrimStart('\uFEFF');
                if (text.Length > 0 && text != "[]" && text != "null")
                    return true;
            }

            if (Directory.Exists(ImagesDir)
                && Directory.EnumerateFiles(ImagesDir, "*.png").Any())
                return true;

            return false;
        }
        catch
        {
            return true;
        }
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOpts));
        ApplyAutostart(settings.Autostart);
    }

    public static void ApplyAutostart(bool enabled)
    {
        // A redirected data folder means a throwaway/test profile: never touch
        // the machine-wide Run key from it.
        if (IsDataDirOverridden)
            return;

        const string keyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        try
        {
            using var key = Registry.CurrentUser.CreateSubKey(keyPath);
            if (key is null)
                return;

            if (enabled)
            {
                var exe = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exe))
                    key.SetValue("Sideclip", "\"" + exe + "\"");
            }
            else
            {
                key.DeleteValue("Sideclip", false);
            }
        }
        catch
        {
        }
    }
}
