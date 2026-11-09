using System.IO;
using System.Windows.Forms;
using Sideclip.Hotkeys;
using Sideclip.Settings;
using Sideclip.Screenshot;
using Sideclip.Obsidian;

namespace Sideclip.Tests;

public sealed class SettingsIntegrationTests
{
    private static void Sta(Action body)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { body(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }
    private static T Control<T>(Form form, string name) where T : Control => (T)form.Controls.Find(name, true).Single();

    [Theory]
    [InlineData(null, false, "")]
    [InlineData("", true, "")]
    [InlineData("   ", false, "")]
    [InlineData("Ctrl+Shift+Alt+F9", true, "Ctrl+Shift+Alt+F9")]
    [InlineData("Ctrl+Shift+Alt+F9", false, null)]
    public void Picker_creation_and_live_refresh_use_same_registered_unassigned_unavailable_formatter(
        string? configured, bool registered, string? expected)
    {
        Assert.Equal(expected, App.TypeOutShortcutDisplay(configured, registered));
    }

    [Theory]
    [InlineData("Win+V")]
    [InlineData("Ctrl+Win+V")]
    [InlineData("Ctrl+Win+P")]
    [InlineData("Ctrl+F12")]
    [InlineData("Escape")]
    [InlineData("Ctrl+NotAKey+T")]
    [InlineData("Ctrl+A+B")]
    [InlineData("Ctrl+")]
    [InlineData("ControlKey")]
    public void Reserved_and_malformed_hotkeys_are_refused_without_hidden_fallback(string value)
    {
        Assert.False(HotkeyAssignments.TryCreate("", "", value, true, out _, out var error));
        Assert.False(string.IsNullOrWhiteSpace(error));
    }

    [Fact]
    public void Duplicate_aliases_are_refused_even_while_feature_off_and_blank_enabled_is_unassigned()
    {
        Assert.False(HotkeyAssignments.TryCreate("Control+Windows+T", "", "Ctrl+Win+T", false, out _, out var error));
        Assert.Contains("distinct", error);
        Assert.True(HotkeyAssignments.TryCreate("", "", "", true, out var empty, out _));
        Assert.Equal("", empty.TypeOut); Assert.True(empty.Enabled);
    }

    [Fact]
    public void External_collision_rolls_back_all_old_registrations_then_reassignment_and_off_release_them()
    {
        Sta(() =>
        {
            using var listener = new HotkeyListener(); using var blocker = new HotkeyListener(); using var probe = new HotkeyListener();
            Assert.True(listener.TryApply("Ctrl+Alt+Shift+F7", "Ctrl+Alt+Shift+F8", "Ctrl+Alt+Shift+F9", true), listener.LastError);
            Assert.True(blocker.TryApply("", "", "Ctrl+Alt+Shift+F10", true), blocker.LastError);
            Assert.False(listener.TryApply("Ctrl+Alt+Shift+F6", "", "Ctrl+Alt+Shift+F10", true));
            Assert.Contains("Previous assignments kept", listener.LastError);
            Assert.True(listener.PickerRegistered); Assert.True(listener.ScreenshotRegistered); Assert.True(listener.TypeOutRegistered);
            Assert.False(probe.TryApply("Ctrl+Alt+Shift+F7", "", "", false));
            Assert.True(probe.TryApply("Ctrl+Alt+Shift+F6", "", "", false), probe.LastError); // Partial attempted registration released.
            Assert.True(listener.TryApply("", "", "", true)); Assert.False(listener.TypeOutRegistered); Assert.False(listener.EscapeRegistered);
            Assert.True(listener.TryApply("", "", "Ctrl+Alt+Shift+F9", false)); Assert.False(listener.TypeOutRegistered);
        });
    }

    [Fact]
    public void Failed_startup_and_reserved_or_duplicate_apply_never_acquire_a_hidden_trigger()
    {
        Sta(() =>
        {
            using var blocker = new HotkeyListener(); using var listener = new HotkeyListener();
            Assert.True(blocker.TryApply("", "", "Ctrl+Alt+Shift+F10", true));
            Assert.False(listener.TryApply("", "", "Ctrl+Alt+Shift+F10", true)); Assert.False(listener.TypeOutRegistered);
            Assert.False(listener.TryApply("", "", "Win+V", true)); Assert.False(listener.TypeOutRegistered);
            Assert.False(listener.TryApply("Ctrl+Alt+Shift+F9", "", "Ctrl+Alt+Shift+F9", true));
            Assert.False(listener.PickerRegistered); Assert.False(listener.TypeOutRegistered);
        });
    }

    [Fact]
    public void Settings_form_defaults_toggle_dependent_pacing_and_apply_all_assignments_without_mutating_cancelled_state()
    {
        Sta(() =>
        {
            var original = new AppSettings { TypeOutEnabled = false }; AppSettings? applied = null;
            using var form = new SettingsForm(original, next => { applied = next.Copy(); return null; }, Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), Array.Empty<ObsidianVault>(), false);
            Assert.Contains("0.3.0", form.Text); Assert.NotNull(form.Icon);
            var enabled = Control<CheckBox>(form, "TypeOutEnabled");
            var delay = Control<NumericUpDown>(form, "TypeOutDelayMs");
            var typeOut = Control<TextBox>(form, "TypeOutGoHotkey");
            Assert.False(enabled.Checked);
            Assert.False(form.TypeOutDependentsEnabled);
            Assert.False(delay.Enabled);
            Assert.False(typeOut.Enabled);
            Assert.All(form.TypeOutDependentControls, c => Assert.False(c.Enabled));
            Assert.Equal(0, delay.Minimum); Assert.Equal(250, delay.Maximum);
            enabled.Checked = true;
            Assert.True(form.TypeOutDependentsEnabled);
            Assert.True(delay.Enabled);
            Assert.True(typeOut.Enabled);
            delay.Value = 80;
            typeOut.Text = "Ctrl+Alt+Shift+F9";
            Assert.True(form.ApplyChanges()); Assert.True(applied!.TypeOutEnabled); Assert.Equal(80, applied.TypeOutDelayMs);
            Assert.Equal("Ctrl+Shift+Alt+F9", applied.TypeOutGoHotkey);
            enabled.Checked = false;
            Assert.False(delay.Enabled);
            Assert.False(typeOut.Enabled);
            Assert.False(original.TypeOutEnabled); Assert.Equal(12, original.TypeOutDelayMs);
        });
    }

    [Fact]
    public void Type_out_toggle_gates_dependents_without_clearing_hotkey_or_delay_values()
    {
        Sta(() =>
        {
            var original = new AppSettings
            {
                TypeOutEnabled = true,
                TypeOutGoHotkey = "Ctrl+Alt+Shift+F9",
                TypeOutDelayMs = 80
            };
            using var form = new SettingsForm(original, _ => null, Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), Array.Empty<ObsidianVault>(), false);
            var enabled = Control<CheckBox>(form, "TypeOutEnabled");
            var delay = Control<NumericUpDown>(form, "TypeOutDelayMs");
            var typeOut = Control<TextBox>(form, "TypeOutGoHotkey");
            var typeOutLabel = Control<Label>(form, "TypeOutGoHotkeyLabel");
            var delayLabel = Control<Label>(form, "TypeOutDelayLabel");

            Assert.True(enabled.Checked);
            Assert.True(typeOut.Enabled);
            Assert.True(delay.Enabled);
            Assert.Equal("Ctrl+Alt+Shift+F9", typeOut.Text);
            Assert.Equal(80, delay.Value);
            Assert.True(typeOut.Left > enabled.Left); // nested under toggle
            Assert.True(typeOutLabel.Left > enabled.Left);
            Assert.True(delayLabel.Left > enabled.Left);

            enabled.Checked = false;
            Assert.False(form.TypeOutDependentsEnabled);
            Assert.All(form.TypeOutDependentControls, c => Assert.False(c.Enabled));
            Assert.Equal("Ctrl+Alt+Shift+F9", typeOut.Text);
            Assert.Equal(80, delay.Value);
            Assert.Equal(SettingsForm.DisabledFore.ToArgb(), typeOut.ForeColor.ToArgb());
            Assert.Equal(SettingsForm.DisabledBack.ToArgb(), typeOut.BackColor.ToArgb());
            Assert.Equal(SettingsForm.DisabledFore.ToArgb(), delay.ForeColor.ToArgb());
            Assert.Equal(SettingsForm.DisabledBack.ToArgb(), delay.BackColor.ToArgb());

            enabled.Checked = true;
            Assert.True(form.TypeOutDependentsEnabled);
            Assert.All(form.TypeOutDependentControls, c => Assert.True(c.Enabled));
            Assert.Equal("Ctrl+Alt+Shift+F9", typeOut.Text);
            Assert.Equal(80, delay.Value);
            Assert.Equal("Ctrl+Alt+Shift+F9", original.TypeOutGoHotkey);
            Assert.Equal(80, original.TypeOutDelayMs);
        });
    }

    [Fact]
    public void Type_out_disabled_palette_meets_wcag_contrast()
    {
        static double Contrast(int a, int b)
        {
            static double L(int rgb)
            {
                double C(int v) { var c = v / 255.0; return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
                return 0.2126 * C((rgb >> 16) & 0xFF) + 0.7152 * C((rgb >> 8) & 0xFF) + 0.0722 * C(rgb & 0xFF);
            }
            var (x, y) = (L(a), L(b));
            return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
        }

        var fore = SettingsForm.DisabledFore.ToArgb() & 0xFFFFFF;
        var back = SettingsForm.DisabledBack.ToArgb() & 0xFFFFFF;
        Assert.Equal(0x9AA0AC, fore);
        Assert.Equal(0x262A35, back);
        Assert.True(Contrast(fore, back) >= 4.5, $"disabled contrast was {Contrast(fore, back):0.00}");
    }

    [Fact]
    public void Settings_errors_are_inline_and_failed_apply_does_not_close_or_mutate_original()
    {
        Sta(() =>
        {
            var original = new AppSettings(); var attempts = 0;
            using var form = new SettingsForm(original, next => { attempts++; return "External collision. Previous assignments kept."; }, Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), Array.Empty<ObsidianVault>(), false);
            Control<TextBox>(form, "TypeOutGoHotkey").Text = "Win+V";
            Assert.False(form.ApplyChanges()); Assert.Equal(0, attempts); Assert.False(form.IsDisposed);
            Assert.Contains("reserved", Control<Label>(form, "SettingsError").Text);
            Control<TextBox>(form, "TypeOutGoHotkey").Text = "Ctrl+Alt+Shift+F9";
            Assert.False(form.ApplyChanges()); Assert.Equal(1, attempts); Assert.False(form.IsDisposed);
            Assert.Contains("External collision", Control<Label>(form, "SettingsError").Text);
            Assert.Equal("Ctrl+Win+Shift+T", original.TypeOutGoHotkey);
        });
    }

    [Fact]
    public void Settings_host_is_single_instance_and_every_open_cancels_without_reprimes()
    {
        Sta(() =>
        {
            var created = 0; var cancelled = 0;
            using var host = new SettingsWindowHost(() => { created++; return new SettingsForm(new AppSettings(), _ => null, Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), Array.Empty<ObsidianVault>(), false); }, () => cancelled++, _ => { });
            host.Open(); var first = host.Current; host.Open(); Assert.Same(first, host.Current); Assert.Equal(1, created); Assert.Equal(2, cancelled);
            first!.Dispose(); Assert.Null(host.Current);
            host.Open(); Assert.NotSame(first, host.Current); Assert.Equal(2, created); Assert.Equal(3, cancelled);
        });
    }

    [Fact]
    public void Persistence_is_atomic_legacy_off_enabled_restart_preserved_and_never_serializes_source_state()
    {
        var prior = Environment.GetEnvironmentVariable("SIDECLIP_DATA_DIR");
        var dir = Path.Combine(Path.GetTempPath(), "SideclipTests-" + Guid.NewGuid()); Environment.SetEnvironmentVariable("SIDECLIP_DATA_DIR", dir);
        try
        {
            Assert.False(SettingsManager.Load().TypeOutEnabled);
            Directory.CreateDirectory(dir); File.WriteAllText(SettingsManager.SettingsPath, "{\"Autostart\":false}"); Assert.False(SettingsManager.Load().TypeOutEnabled);
            var settings = new AppSettings { TypeOutEnabled = true, TypeOutGoHotkey = "Ctrl+Alt+Shift+F9", TypeOutDelayMs = 80 };
            SettingsManager.Save(settings); var original = File.ReadAllText(SettingsManager.SettingsPath);
            using (var locked = new FileStream(SettingsManager.SettingsPath, FileMode.Open, FileAccess.Read, FileShare.None))
            {
                var failure = Record.Exception(() => SettingsManager.Save(new AppSettings()));
                Assert.True(failure is IOException or UnauthorizedAccessException, "Locked settings replacement must fail closed.");
            }
            Assert.Equal(original, File.ReadAllText(SettingsManager.SettingsPath)); Assert.False(File.Exists(SettingsManager.SettingsPath + ".tmp"));
            var restored = SettingsManager.Load(); Assert.True(restored.TypeOutEnabled); Assert.Equal(80, restored.TypeOutDelayMs); Assert.Equal(settings.TypeOutGoHotkey, restored.TypeOutGoHotkey);
            Assert.DoesNotContain("candidate", original, StringComparison.OrdinalIgnoreCase); Assert.DoesNotContain("payload", original, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("running", original, StringComparison.OrdinalIgnoreCase);
            File.WriteAllText(SettingsManager.SettingsPath, "{\"TypeOutGoHotkey\":\"Win+V\"}");
            Assert.Equal("Win+V", SettingsManager.Load().TypeOutGoHotkey); // Report invalid startup, never silently substitute another chord.
        }
        finally { Environment.SetEnvironmentVariable("SIDECLIP_DATA_DIR", prior); if (Directory.Exists(dir)) Directory.Delete(dir, true); }
    }
}
