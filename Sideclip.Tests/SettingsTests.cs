using System.IO;
using Microsoft.Win32;
using Sideclip.Settings;
using Xunit;

namespace Sideclip.Tests;

public class SettingsTests
{
    // settings.json exactly as a build from before caps existed wrote it.
    private const string OldFormat = """
        {
          "TtlSeconds": 20,
          "TtlEnabled": false,
          "Autostart": false,
          "ShowDebugWindow": false,
          "PickerHotkey": "Ctrl+Alt+K",
          "ScreenshotHotkey": "Ctrl+Alt+J"
        }
        """;

    [Fact]
    public void Brand_new_profile_gets_the_default_caps_and_decides_them_once()
    {
        using var p = new TestProfile();
        var s = SettingsManager.Load();
        Assert.Equal(100, s.MaxTextClips);
        Assert.Equal(25, s.MaxImageClips);
        Assert.True(s.CapsNeedPersist);
        Assert.True(s.Autostart);
    }

    [Fact]
    public void Empty_history_file_still_counts_as_a_brand_new_profile()
    {
        using var p = new TestProfile();
        File.WriteAllText(Path.Combine(p.Root, "history.json"), "[]");
        var s = SettingsManager.Load();
        Assert.Equal(100, s.MaxTextClips);
    }

    [Fact]
    public void Old_format_settings_file_upgrades_to_unlimited_and_keeps_everything_else()
    {
        using var p = new TestProfile();
        File.WriteAllText(Path.Combine(p.Root, "settings.json"), OldFormat);
        var s = SettingsManager.Load();
        Assert.Equal(AppSettings.Unlimited, s.MaxTextClips);
        Assert.Equal(AppSettings.Unlimited, s.MaxImageClips);
        Assert.True(s.CapsNeedPersist);
        Assert.Equal(20, s.TtlSeconds);
        Assert.False(s.TtlEnabled);
        Assert.Equal("Ctrl+Alt+K", s.PickerHotkey);
        Assert.Equal("Ctrl+Alt+J", s.ScreenshotHotkey);
        // Loading never rewrites the file.
        Assert.Equal(OldFormat, File.ReadAllText(Path.Combine(p.Root, "settings.json")));
    }

    [Fact]
    public void Existing_history_without_a_settings_file_is_unlimited()
    {
        using var p = new TestProfile();
        File.WriteAllText(Path.Combine(p.Root, "history.json"), "[ { \"Text\": \"x\" } ]");
        var s = SettingsManager.Load();
        Assert.Equal(AppSettings.Unlimited, s.MaxTextClips);
        Assert.Equal(AppSettings.Unlimited, s.MaxImageClips);
    }

    [Fact]
    public void Stored_images_without_a_history_file_are_unlimited()
    {
        using var p = new TestProfile();
        Directory.CreateDirectory(p.Images);
        File.WriteAllBytes(Path.Combine(p.Images, "20260101000000000.png"), TestProfile.Png(1));
        var s = SettingsManager.Load();
        Assert.Equal(AppSettings.Unlimited, s.MaxImageClips);
    }

    [Fact]
    public void Unreadable_settings_with_history_is_unlimited_and_the_file_is_not_overwritten()
    {
        using var p = new TestProfile();
        File.WriteAllText(Path.Combine(p.Root, "settings.json"), "{ not json");
        File.WriteAllText(Path.Combine(p.Root, "history.json"), "[ { \"Text\": \"x\" } ]");
        var s = SettingsManager.Load();
        Assert.Equal(AppSettings.Unlimited, s.MaxTextClips);
        Assert.Equal(AppSettings.Unlimited, s.MaxImageClips);
        Assert.False(s.CapsNeedPersist);
    }

    [Fact]
    public void Caps_are_independent_and_persist()
    {
        using var p = new TestProfile();
        var s = SettingsManager.Load();
        s.MaxTextClips = 30;
        s.MaxImageClips = AppSettings.Unlimited;
        SettingsManager.Save(s);

        var back = SettingsManager.Load();
        Assert.Equal(30, back.MaxTextClips);
        Assert.Equal(AppSettings.Unlimited, back.MaxImageClips);
        Assert.False(back.CapsNeedPersist);

        back.MaxTextClips = AppSettings.Unlimited;
        back.MaxImageClips = 7;
        SettingsManager.Save(back);
        var again = SettingsManager.Load();
        Assert.Equal(AppSettings.Unlimited, again.MaxTextClips);
        Assert.Equal(7, again.MaxImageClips);
    }

    [Fact]
    public void A_saved_default_is_not_mistaken_for_an_old_profile_once_history_exists()
    {
        using var p = new TestProfile();
        var s = SettingsManager.Load();
        SettingsManager.Save(s); // what App does once at startup
        File.WriteAllText(Path.Combine(p.Root, "history.json"), "[ { \"Text\": \"x\" } ]");
        var next = SettingsManager.Load();
        Assert.Equal(100, next.MaxTextClips);
        Assert.Equal(25, next.MaxImageClips);
    }

    [Theory]
    [InlineData(-5)]
    [InlineData(0)]
    [InlineData(100001)]
    [InlineData(int.MaxValue)]
    public void Out_of_range_stored_values_become_unlimited_never_a_small_cap(int bad)
    {
        using var p = new TestProfile();
        File.WriteAllText(Path.Combine(p.Root, "settings.json"),
            "{ \"MaxTextClips\": " + bad + ", \"MaxImageClips\": " + bad + " }");
        var s = SettingsManager.Load();
        Assert.Equal(AppSettings.Unlimited, s.MaxTextClips);
        Assert.Equal(AppSettings.Unlimited, s.MaxImageClips);
    }

    [Fact]
    public void A_redirected_data_folder_never_touches_the_autostart_registry_value()
    {
        using var p = new TestProfile();
        static object? Read()
        {
            using var k = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run");
            return k?.GetValue("Sideclip");
        }

        var before = Read();
        Assert.True(SettingsManager.IsDataDirOverridden);
        SettingsManager.Save(new AppSettings { Autostart = !(before is not null) });
        SettingsManager.ApplyAutostart(false);
        SettingsManager.ApplyAutostart(true);
        Assert.Equal(before, Read());
    }

    [Fact]
    public void Data_folder_override_redirects_every_path()
    {
        using var p = new TestProfile();
        Assert.StartsWith(p.Root, SettingsManager.SettingsPath);
        Assert.StartsWith(p.Root, SettingsManager.HistoryPath);
        Assert.StartsWith(p.Root, SettingsManager.ImagesDir);
    }
}