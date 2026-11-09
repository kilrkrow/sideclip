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

    public static string Dir => AppProfile.DataDir;

    public static string SettingsPath => Path.Combine(Dir, "settings.json");
    public static string HistoryPath => Path.Combine(Dir, "history.json");
    public static string ImagesDir => Path.Combine(Dir, "images");

    public static AppSettings Load()
    {
        try
        {
            // Fresh install only: Start with Windows defaults ON.
            // Existing settings.json is never rewritten to flip Autostart.
            if (!File.Exists(SettingsPath))
                return new AppSettings { Autostart = true };

            var json = File.ReadAllText(SettingsPath);
            var s = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts);
            if (s is not null)
            {
                if (json.IndexOf("TtlEnabled", StringComparison.OrdinalIgnoreCase) < 0)
                    s.TtlEnabled = true;
                if (s.TtlSeconds < 3) s.TtlSeconds = 12;
                if (s.TtlSeconds > 300) s.TtlSeconds = 300;
                if (s.TypeOutDelayMs < 0) s.TypeOutDelayMs = 0;
                if (s.TypeOutDelayMs > 250) s.TypeOutDelayMs = 250;
                return s;
            }
        }
        catch
        {
        }

        // Unreadable settings: treat as fresh defaults.
        return new AppSettings { Autostart = true };
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Dir);
        var temporary = SettingsPath + ".tmp";
        try
        {
            File.WriteAllText(temporary, JsonSerializer.Serialize(settings, JsonOpts));
            File.Move(temporary, SettingsPath, overwrite: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
        ApplyAutostart(settings.Autostart);
    }

    public static void ApplyAutostart(bool enabled)
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("SIDECLIP_DATA_DIR"))) return;
        // An isolated profile (SIDECLIP_DATA_DIR) must never rewrite the real autostart entry.
        if (AppProfile.IsIsolated)
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
