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
    private readonly TrayMenuModel _configuration;
    private readonly ContextMenuStrip _menu;

    public event Action? OpenPicker;
#pragma warning disable CS0067 // Invoked only by the debug-only menu item.
    public event Action? ToggleDebug;
#pragma warning restore CS0067
    public event Action? ExitApp;
    public event Action? OpenSettings;
    public event Action? ClearHistory;

    public TrayService(AppSettings settings, Action<AppSettings> onSettingsChanged)
    {
        _settings = settings;
        _onSettingsChanged = onSettingsChanged;
        _configuration = new TrayMenuModel(() => OpenSettings?.Invoke());

        _menu = new ContextMenuStrip();
        _openPickerItem = new ToolStripMenuItem(OpenLabel());
        _openPickerItem.Click += (_, _) => OpenPicker?.Invoke();
        _menu.Items.Add(_openPickerItem);
        _menu.Items.Add("Clear history", null, (_, _) => ClearHistory?.Invoke());
#if DEBUG
        var debug = new ToolStripMenuItem("Debug window") { Checked = settings.ShowDebugWindow, CheckOnClick = true };
        debug.CheckedChanged += (_, _) =>
        {
            _settings.ShowDebugWindow = debug.Checked;
            _onSettingsChanged(_settings);
            ToggleDebug?.Invoke();
        };
        _menu.Items.Add(debug);
#endif
        var auto = new ToolStripMenuItem("Start with Windows") { Checked = settings.Autostart, CheckOnClick = true };
        auto.CheckedChanged += (_, _) => { _settings.Autostart = auto.Checked; _onSettingsChanged(_settings); };
        _menu.Items.Add(auto);
        _ttlMenu = new ToolStripMenuItem(TtlLabel());
        var off = new ToolStripMenuItem("Off") { Tag = 0 };
        off.Click += (_, _) => ApplyTtl(enabled: false, _settings.TtlSeconds);
        _ttlMenu.DropDownItems.Add(off);
        foreach (var seconds in new[] { 8, 12, 20, 30 })
        {
            var item = new ToolStripMenuItem(seconds + " seconds") { Tag = seconds };
            item.Click += (_, _) => ApplyTtl(enabled: true, seconds);
            _ttlMenu.DropDownItems.Add(item);
        }
        RefreshTtlChecks();
        _configuration.Build(_menu, _ttlMenu);
        _menu.Items.Add(new ToolStripSeparator());
        _menu.Items.Add("About Sideclip...", null, (_, _) => ShowAbout());
        _menu.Items.Add("Exit", null, (_, _) => ExitApp?.Invoke());

        _icon = new NotifyIcon
        {
            Text = TrayTip(),
            Icon = SettingsForm.LoadProductIcon(),
            Visible = true,
            ContextMenuStrip = _menu
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
        foreach (ToolStripMenuItem item in _ttlMenu.DropDownItems)
            if (item.Tag is int seconds)
                item.Checked = _settings.TtlEnabled ? seconds == _settings.TtlSeconds : seconds == 0;
    }

    public void RefreshTypeOutStatus(string message) => Hint("Sideclip", message);

    private string TtlLabel() => _settings.TtlEnabled ? "Secret TTL: " + _settings.TtlSeconds + "s" : "Secret TTL: Off";
    private string OpenLabel() => string.IsNullOrWhiteSpace(_settings.PickerHotkey) ? "Open clipboard" : "Open clipboard (" + _settings.PickerHotkey + ")";

    private static string DisplayVersion() => AppVersion.Display();

    private static string TrayTip()
    {
        var tip = "Sideclip " + DisplayVersion();
        return tip.Length <= 63 ? tip : tip[..63];
    }

    private void ShowAbout()
    {
        MessageBox.Show(
            "Sideclip " + DisplayVersion() + Environment.NewLine +
            "Tray clipboard beside Win+V." + Environment.NewLine +
            "https://github.com/kilrkrow/sideclip",
            "About Sideclip", MessageBoxButtons.OK, MessageBoxIcon.Information);
    }

    public void Hint(string title, string text)
    {
        try
        {
            _icon.BalloonTipTitle = title;
            _icon.BalloonTipText = text;
            _icon.ShowBalloonTip(2500);
        }
        catch { }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _menu.Dispose();
        _icon.Dispose();
    }
}

