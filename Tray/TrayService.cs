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

        var ttl = new ToolStripMenuItem($"Secret TTL: {settings.TtlSeconds}s");
        foreach (var sec in new[] { 8, 12, 20, 30 })
        {
            var s = sec;
            var item = new ToolStripMenuItem($"{s} seconds") { Checked = settings.TtlSeconds == s };
            item.Click += (_, _) =>
            {
                _settings.TtlSeconds = s;
                ttl.Text = $"Secret TTL: {s}s";
                foreach (ToolStripMenuItem x in ttl.DropDownItems)
                    x.Checked = x.Text.StartsWith(s.ToString(), StringComparison.Ordinal);
                _onSettingsChanged(_settings);
            };
            ttl.DropDownItems.Add(item);
        }
        menu.Items.Add(ttl);

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




