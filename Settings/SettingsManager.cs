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

    public static string Dir =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sideclip");

    public static string SettingsPath => Path.Combine(Dir, "settings.json");
    public static string HistoryPath => Path.Combine(Dir, "history.json");
    public static string ImagesDir => Path.Combine(Dir, "images");

    public static AppSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
            {
                var json = File.ReadAllText(SettingsPath);
                var s = JsonSerializer.Deserialize<AppSettings>(json, JsonOpts);
                if (s is not null)
                {
                    if (json.IndexOf("TtlEnabled", StringComparison.OrdinalIgnoreCase) < 0)
                        s.TtlEnabled = true;
                    if (s.TtlSeconds < 3) s.TtlSeconds = 12;
                    if (s.TtlSeconds > 300) s.TtlSeconds = 300;
                    return s;
                }
            }
        }
        catch
        {
        }

        return new AppSettings();
    }

    public static void Save(AppSettings settings)
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(SettingsPath, JsonSerializer.Serialize(settings, JsonOpts));
        ApplyAutostart(settings.Autostart);
    }

    public static void ApplyAutostart(bool enabled)
    {
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
