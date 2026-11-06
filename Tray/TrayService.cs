using System.Drawing;
using System.Reflection;
using System.Windows.Forms;
using Sideclip.Settings;

namespace Sideclip.Tray;

internal sealed class TrayService : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly AppSettings _settings;
    private readonly Action<AppSettings> _onSettingsChanged;
    private readonly ToolStripMenuItem _openPickerItem;
    private readonly ToolStripMenuItem _ttlMenu;
    private readonly ToolStripMenuItem _typeOutMenu = new("Type selected text");
    private readonly ToolStripMenuItem _typeOutParent;
    private readonly ToolStripMenuItem _typeOutOff;
    private readonly ToolStripMenuItem _typeOutOn;
    private readonly ToolStripMenuItem _typeOutSpeed;
    public event Action? TypeOutGoRequested;
    public event Action? TypeOutCancelRequested;

    public event Action? OpenPicker;
#pragma warning disable CS0067 // Invoked only by the debug-only menu item.
    public event Action? ToggleDebug;
#pragma warning restore CS0067
    public event Action? ExitApp;
    public event Action? EditHotkeys;
    public event Action? ClearHistory;

    public TrayService(AppSettings settings, Action<AppSettings> onSettingsChanged)
    {
        _settings = settings;
        _typeOutParent = new ToolStripMenuItem("Type selected text") { CheckOnClick = false, Checked = settings.TypeOutEnabled };
        _typeOutOff = new ToolStripMenuItem("Off");
        _typeOutOff.Click += (_, _) => SetTypeOutEnabled(false);
        _typeOutOn = new ToolStripMenuItem("On");
        _typeOutOn.Click += (_, _) => SetTypeOutEnabled(true);
        _typeOutParent.DropDownItems.AddRange(new ToolStripItem[] { _typeOutOff, _typeOutOn });
        _typeOutSpeed = new ToolStripMenuItem("Delay between chunks") { ToolTipText = "Human typing delay: bounds 0 to 250 milliseconds." };
        foreach (var ms in new[] { 0, 6, 12, 30, 80, 250 })
        {
            var item = new ToolStripMenuItem(ms + " ms") { Tag = ms };
            item.Click += (_, _) =>
            {
                SetTypeOutDelay((int)(item.Tag ?? 12));
            };
            _typeOutSpeed.DropDownItems.Add(item);
        }
        _typeOutParent.DropDownItems.Add(_typeOutSpeed);
        _typeOutParent.DropDownItems.Add(new ToolStripMenuItem("Go: " + settings.TypeOutGoHotkey + " (assign in Hotkeys...)") { Tag = "GoHint" });
        _onSettingsChanged = onSettingsChanged;
        _typeOutMenu.DropDownItems.Add("Go (assigned hotkey)", null, (_, _) => TypeOutGoRequested?.Invoke());
        _typeOutMenu.DropDownItems.Add("Cancel (Esc)", null, (_, _) => TypeOutCancelRequested?.Invoke());

        var menu = new ContextMenuStrip();
        _openPickerItem = new ToolStripMenuItem(OpenLabel()) { };
        _openPickerItem.Click += (_, _) => OpenPicker?.Invoke();
        menu.Items.Add(_openPickerItem);
        menu.Items.Add("Hotkeys...", null, (_, _) => EditHotkeys?.Invoke());
        menu.Items.Add("Clear history", null, (_, _) => ClearHistory?.Invoke());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_typeOutMenu);
        RefreshTypeOutSettings();

#if DEBUG
        var debug = new ToolStripMenuItem("Debug window") { Checked = settings.ShowDebugWindow, CheckOnClick = true };
        debug.CheckedChanged += (_, _) =>
        {
            _settings.ShowDebugWindow = debug.Checked;
            _onSettingsChanged(_settings);
            ToggleDebug?.Invoke();
        };
        menu.Items.Add(debug);
#endif

        var auto = new ToolStripMenuItem("Start with Windows") { Checked = settings.Autostart, CheckOnClick = true };
        auto.CheckedChanged += (_, _) =>
        {
            _settings.Autostart = auto.Checked;
            _onSettingsChanged(_settings);
        };
        menu.Items.Add(auto);

        _ttlMenu = new ToolStripMenuItem(TtlLabel());
        var off = new ToolStripMenuItem("Off") { Tag = 0 };
        off.Click += (_, _) => ApplyTtl(enabled: false, seconds: _settings.TtlSeconds);
        _ttlMenu.DropDownItems.Add(off);
        foreach (var sec in new[] { 8, 12, 20, 30 })
        {
            var s = sec;
            var item = new ToolStripMenuItem(s + " seconds") { Tag = s };
            item.Click += (_, _) => ApplyTtl(enabled: true, seconds: s);
            _ttlMenu.DropDownItems.Add(item);
        }
        RefreshTtlChecks();
        menu.Items.Add(_ttlMenu);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(_typeOutMenu);
        RefreshTypeOutSettings();
        menu.Items.Add("About Sideclip...", null, (_, _) => ShowAbout());
        menu.Items.Add("Exit", null, (_, _) => ExitApp?.Invoke());

        _icon = new NotifyIcon
        {
            Text = TrayTip(),
            Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath ?? string.Empty)
                   ?? SystemIcons.Application,
            Visible = true,
            ContextMenuStrip = menu
        };
        _icon.DoubleClick += (_, _) => OpenPicker?.Invoke();
    }

    public void RefreshHotkeyLabel()
    {
        _openPickerItem.Text = OpenLabel();
        _icon.Text = TrayTip();
    }

    private void ApplyTtl(bool enabled, int seconds)
    {
        _settings.TtlEnabled = enabled;
        _settings.TtlSeconds = seconds;
        _ttlMenu.Text = TtlLabel();
        RefreshTtlChecks();
        _onSettingsChanged(_settings);
    }

    private void RefreshTtlChecks()
    {
        foreach (ToolStripMenuItem x in _ttlMenu.DropDownItems)
        {
            if (x.Tag is not int tag)
                continue;
            x.Checked = _settings.TtlEnabled ? tag == _settings.TtlSeconds : tag == 0;
        }
    }

    private void SetTypeOutDelay(int milliseconds)
    {
        _settings.TypeOutDelayMs = milliseconds;
        RefreshTypeOutSettings();
        _onSettingsChanged(_settings);
        RefreshTypeOutSettings();
    }

    private void SetTypeOutEnabled(bool enabled)
    {
        _settings.TypeOutEnabled = enabled;
        _onSettingsChanged(_settings);
        RefreshTypeOutSettings();
    }

    private void RefreshTypeOutSettings()
    {

        _typeOutParent.Checked = _settings.TypeOutEnabled;
        _typeOutParent.ToolTipText = _settings.TypeOutEnabled ? "Enabled. Arm entries in the picker." : "Disabled. Enable from the On submenu.";
        _typeOutOn.Checked = _settings.TypeOutEnabled;
        _typeOutOff.Checked = !_settings.TypeOutEnabled;
        _typeOutSpeed.Enabled = _settings.TypeOutEnabled;

        foreach (ToolStripMenuItem item in _typeOutSpeed.DropDownItems)
            item.Checked = item.Tag is int ms && ms == _settings.TypeOutDelayMs;
        foreach (ToolStripItem item in _typeOutParent.DropDownItems)
            if (item.Tag is "GoHint") item.Text = "Go: " + _settings.TypeOutGoHotkey + " (assign in Hotkeys...)";
    }

    public void RefreshTypeOutStatus(string message)
    {
        _typeOutMenu.Text = message.Length <= 80 ? message : message[..77] + "...";
        _typeOutMenu.ToolTipText = message;
    }
    public void RefreshTypeOutLabels()
    {
        var go = _settings.TypeOutGoHotkey;

        foreach (ToolStripItem item in _typeOutParent.DropDownItems)
            if (item.Tag is "GoHint")
            {
                item.Text = "Go: " + go + " (assign in Hotkeys...)";
                item.Enabled = _settings.TypeOutEnabled;
            }
        RefreshTypeOutSettings();
    }

    private string TtlLabel() =>
        _settings.TtlEnabled
            ? "Secret TTL: " + _settings.TtlSeconds + "s"
            : "Secret TTL: Off";

    private string OpenLabel() =>
        string.IsNullOrWhiteSpace(_settings.PickerHotkey)
            ? "Open clipboard"
            : "Open clipboard (" + _settings.PickerHotkey + ")";


    private static string DisplayVersion()
    {
        var info = Assembly.GetExecutingAssembly()
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            var plus = info.IndexOf('+');
            return plus > 0 ? info[..plus] : info;
        }

        var v = Assembly.GetExecutingAssembly().GetName().Version;
        return v is null ? "0.1.0" : $"{v.Major}.{v.Minor}.{v.Build}";
    }

    private static string TrayTip()
    {
        // NotifyIcon.Text max 63 chars on Windows.
        var tip = "Sideclip " + DisplayVersion();
        return tip.Length <= 63 ? tip : tip[..63];
    }

    private void ShowAbout()
    {
        MessageBox.Show(
            "Sideclip " + DisplayVersion() + Environment.NewLine +
            "Tray clipboard beside Win+V." + Environment.NewLine +
            "https://github.com/kilrkrow/sideclip",
            "About Sideclip",
            MessageBoxButtons.OK,
            MessageBoxIcon.Information);
    }

    public void Hint(string title, string text)
    {
        try
        {
            _icon.BalloonTipTitle = title;
            _icon.BalloonTipText = text;
            _icon.ShowBalloonTip(2500);
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }
}
