using System.Drawing;
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

    public event Action? OpenPicker;
    public event Action? ToggleDebug;
    public event Action? ExitApp;
    public event Action? EditHotkeys;
    public event Action? ClearHistory;

    public TrayService(AppSettings settings, Action<AppSettings> onSettingsChanged)
    {
        _settings = settings;
        _onSettingsChanged = onSettingsChanged;

        var menu = new ContextMenuStrip();
        _openPickerItem = new ToolStripMenuItem(OpenLabel()) { };
        _openPickerItem.Click += (_, _) => OpenPicker?.Invoke();
        menu.Items.Add(_openPickerItem);
        menu.Items.Add("Hotkeys...", null, (_, _) => EditHotkeys?.Invoke());
        menu.Items.Add("Clear history", null, (_, _) => ClearHistory?.Invoke());
        menu.Items.Add(new ToolStripSeparator());

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
        menu.Items.Add("Exit", null, (_, _) => ExitApp?.Invoke());

        _icon = new NotifyIcon
        {
            Text = "Sideclip",
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

    private string TtlLabel() =>
        _settings.TtlEnabled
            ? "Secret TTL: " + _settings.TtlSeconds + "s"
            : "Secret TTL: Off";

    private string OpenLabel() =>
        string.IsNullOrWhiteSpace(_settings.PickerHotkey)
            ? "Open clipboard"
            : "Open clipboard (" + _settings.PickerHotkey + ")";

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
