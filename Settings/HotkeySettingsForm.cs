using System.Drawing;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Sideclip.Hotkeys;

namespace Sideclip.Settings;

internal sealed class SettingsForm : Form
{
    // Same readable disabled palette as PickerWindow disabled trigger (#9AA0AC on #262A35, >=4.5:1).
    internal static readonly Color DisabledBack = Color.FromArgb(0x26, 0x2A, 0x35);
    internal static readonly Color DisabledFore = Color.FromArgb(0x9A, 0xA0, 0xAC);
    private static readonly Color FieldBack = Color.FromArgb(38, 42, 53);
    private const int NestIndent = 28;

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);
    private readonly TextBox _picker, _shot, _typeOut;
    private readonly Label _typeOutLabel, _delayLabel;
    private readonly CheckBox _enabled;
    private readonly NumericUpDown _delay;
    private readonly Label _error, _status;
    private readonly AppSettings _snapshot;
    private readonly Func<AppSettings, string?> _apply;

    public SettingsForm(AppSettings settings, Func<AppSettings, string?> apply)
    {
        _snapshot = settings.Copy(); _apply = apply;
        Text = "Sideclip Settings - " + DisplayVersion;
        Icon = LoadProductIcon();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(580, 500);
        Font = new Font("Consolas", 10f);
        BackColor = Color.FromArgb(46, 50, 63); ForeColor = Color.FromArgb(229, 233, 246);
        _picker = AddBox("Picker shortcut", "PickerHotkey", 18, 16, settings.PickerHotkey, out _);
        _shot = AddBox("Screenshot path shortcut", "ScreenshotHotkey", 60, 16, settings.ScreenshotHotkey, out _);

        _enabled = new CheckBox
        {
            Name = "TypeOutEnabled",
            AccessibleName = "Enable clipboard-free text type-out",
            Text = "Enable clipboard-free text type-out",
            Location = new Point(16, 108),
            Size = new Size(548, 28),
            Checked = settings.TypeOutEnabled,
            TabIndex = 10
        };
        Controls.Add(_enabled);

        // Dependent type-out controls nested under the enable toggle.
        _typeOut = AddBox("Text-only type-out shortcut", "TypeOutGoHotkey", 144, 16 + NestIndent, settings.TypeOutGoHotkey, out _typeOutLabel);
        _typeOut.TabIndex = 11;
        _delayLabel = new Label
        {
            Name = "TypeOutDelayLabel",
            Text = "Delay between chunks (ms)",
            Location = new Point(16 + NestIndent, 186),
            Size = new Size(320, 24),
            TabStop = false
        };
        Controls.Add(_delayLabel);
        _delay = new NumericUpDown
        {
            Name = "TypeOutDelayMs",
            AccessibleName = "Delay between chunks in milliseconds",
            Minimum = 0,
            Maximum = 250,
            Value = Math.Clamp(settings.TypeOutDelayMs, 0, 250),
            Location = new Point(392, 182),
            Width = 170,
            BackColor = FieldBack,
            ForeColor = DisabledFore,
            TabIndex = 12
        };
        Controls.Add(_delay);

        _enabled.CheckedChanged += (_, _) => SyncTypeOutDependents();
        SyncTypeOutDependents();

        Controls.Add(new Label
        {
            Text = "Choose a shortcut box and press keys. Backspace clears.\r\nBlank means unassigned. Esc closes without applying.\r\nSelect text, focus destination, then use its shortcut.\r\nTab/Enter may navigate or submit. Insertion is not verified.",
            Location = new Point(16, 226),
            Size = new Size(548, 80),
            ForeColor = Color.FromArgb(192, 201, 212)
        });
        _error = new Label { Name = "SettingsError", AccessibleName = "Settings validation error", Location = new Point(16, 310), Size = new Size(548, 52), ForeColor = Color.LightSalmon };
        _status = new Label { Name = "TypeOutStatus", AccessibleName = "Type-out status", Location = new Point(16, 364), Size = new Size(548, 42), ForeColor = Color.FromArgb(192, 201, 212) };
        Controls.Add(_error); Controls.Add(_status);
        Controls.Add(new Label { Text = "Sideclip " + DisplayVersion + " | kilrkrow", Location = new Point(16, 458), Size = new Size(300, 24) });
        var applyButton = new Button { Name = "ApplySettings", Text = "Apply", Location = new Point(380, 450), Size = new Size(86, 32) };
        applyButton.Click += (_, _) => ApplyChanges();
        var close = new Button { Name = "CancelSettings", Text = "Close", Location = new Point(478, 450), Size = new Size(86, 32) };
        close.Click += (_, _) => Close(); CancelButton = close;
        Controls.Add(applyButton); Controls.Add(close);
    }

    /// <summary>True when type-out dependents should accept input (mirrors the enable toggle).</summary>
    internal bool TypeOutDependentsEnabled => _enabled.Checked;

    internal IEnumerable<Control> TypeOutDependentControls
    {
        get
        {
            yield return _typeOutLabel;
            yield return _typeOut;
            yield return _delayLabel;
            yield return _delay;
        }
    }

    private void SyncTypeOutDependents()
    {
        var on = _enabled.Checked;
        foreach (var c in TypeOutDependentControls)
        {
            c.Enabled = on;
            if (c is TextBox or NumericUpDown)
            {
                c.BackColor = on ? FieldBack : DisabledBack;
                c.ForeColor = on ? ForeColor : DisabledFore;
            }
            else if (c is Label)
            {
                c.ForeColor = on ? ForeColor : DisabledFore;
            }
        }
        // Values are never cleared here; toggle only gates Enabled.
    }

    internal static string DisplayVersion => typeof(SettingsForm).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.3.0";
    internal static Icon LoadProductIcon() => Icon.ExtractAssociatedIcon(System.IO.Path.Combine(AppContext.BaseDirectory, "Sideclip.exe"))
        ?? throw new InvalidOperationException("Sideclip product icon is unavailable.");

    internal bool ApplyChanges()
    {
        var proposed = _snapshot.Copy();
        if (!HotkeyAssignments.TryCreate(_picker.Text, _shot.Text, _typeOut.Text, _enabled.Checked, out var assignments, out var error))
        { _error.Text = error; return false; }
        proposed.PickerHotkey = assignments.Picker; proposed.ScreenshotHotkey = assignments.Screenshot;
        proposed.TypeOutGoHotkey = assignments.TypeOut; proposed.TypeOutEnabled = assignments.Enabled;
        proposed.TypeOutDelayMs = (int)_delay.Value;
        error = _apply(proposed);
        _error.Text = error ?? "";
        if (error is not null) return false;
        _snapshot.CopyFrom(proposed);
        _status.Text = "Settings applied. Select a text source again before typing.";
        return true;
    }

    internal void SetStatus(string status) => _status.Text = status;

    private TextBox AddBox(string label, string name, int y, int labelX, string? value, out Label labelControl)
    {
        labelControl = new Label { Name = name + "Label", Text = label, Location = new Point(labelX, y + 4), Size = new Size(330 - (labelX - 16), 24), TabStop = false };
        Controls.Add(labelControl);
        var box = new TextBox
        {
            Name = name,
            AccessibleName = label,
            Location = new Point(352, y),
            Width = 210,
            ReadOnly = true,
            Text = value ?? "",
            BackColor = FieldBack,
            ForeColor = ForeColor
        };
        box.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) { Close(); e.SuppressKeyPress = true; return; }
            e.SuppressKeyPress = true;
            if (e.KeyCode == Keys.Back) { box.Text = ""; return; }
            var winDown = (GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0;
            var parsed = HotkeyParser.FromKeyEvent(e, winDown);
            if (parsed.Length > 0) box.Text = parsed;
        };
        Controls.Add(box); return box;
    }
}

internal sealed class SettingsWindowHost : IDisposable
{
    private readonly Func<SettingsForm> _factory;
    private readonly Action _beforeOpen;
    private readonly Action<SettingsForm> _show;
    internal SettingsForm? Current { get; private set; }
    public SettingsWindowHost(Func<SettingsForm> factory, Action beforeOpen, Action<SettingsForm>? show = null)
    { _factory = factory; _beforeOpen = beforeOpen; _show = show ?? (form => form.Show()); }
    public void Open()
    {
        _beforeOpen();
        if (Current is { IsDisposed: false }) { Current.BringToFront(); Current.Activate(); return; }
        var form = _factory(); Current = form;
        form.Disposed += (_, _) => { if (ReferenceEquals(Current, form)) Current = null; };
        _show(form);
    }
    public void Dispose() { Current?.Dispose(); Current = null; }
}