using System.Windows.Forms;
using Sideclip.Settings;

namespace Sideclip.Tray;

// This model does not create a NotifyIcon, show UI, or register input.
internal sealed class TrayMenuModel
{
    internal ToolStripMenuItem Parent { get; }
    internal ToolStripMenuItem Actions { get; }
    internal ToolStripMenuItem Speed { get; }
    internal ToolStripMenuItem Hint { get; }
    internal ToolStripMenuItem On { get; }
    internal ToolStripMenuItem Off { get; }

    private readonly AppSettings _settings;
    private readonly Action<AppSettings> _changed;

    internal TrayMenuModel(AppSettings settings, Action<AppSettings> changed)
    {
        _settings = settings;
        _changed = changed;
        Parent = new ToolStripMenuItem("Type selected text");
        On = new ToolStripMenuItem("On");
        Off = new ToolStripMenuItem("Off");
        On.Click += (_, _) => SetEnabled(true);
        Off.Click += (_, _) => SetEnabled(false);
        Speed = new ToolStripMenuItem("Delay between chunks");
        foreach (var ms in new[] { 0, 6, 12, 30, 80, 250 })
        {
            var item = new ToolStripMenuItem(ms + " ms") { Tag = ms };
            item.Click += (_, _) => SetDelay((int)item.Tag!);
            Speed.DropDownItems.Add(item);
        }
        Hint = new ToolStripMenuItem("Go: " + settings.TypeOutGoHotkey + " (assign in Hotkeys...)") { Tag = "GoHint" };
        Parent.DropDownItems.AddRange(new ToolStripItem[] { Off, On, Speed, Hint });
        Actions = new ToolStripMenuItem("Type-out actions");
        Actions.DropDownItems.Add("Go (assigned hotkey)");
        Actions.DropDownItems.Add("Cancel (Esc)");
        Refresh();
    }

    internal ContextMenuStrip Build(ContextMenuStrip menu, ToolStripMenuItem ttlMenu)
    {
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(ttlMenu);
        menu.Items.Add(new ToolStripSeparator());
        // Add the nested parent once, immediately after TTL.
        menu.Items.Add(Parent);
        menu.Items.Add(Actions);
        return menu;
    }

    internal ToolStripMenuItem Go => (ToolStripMenuItem)Actions.DropDownItems[0];
    internal ToolStripMenuItem Cancel => (ToolStripMenuItem)Actions.DropDownItems[1];

    internal void AttachGoCancel(Action go, Action cancel)
    {
        Go.Click += (_, _) => go();
        Cancel.Click += (_, _) => cancel();
    }

    internal void RefreshHotkey()
    {
        Hint.Text = "Go: " + _settings.TypeOutGoHotkey + " (assign in Hotkeys...)";
        Refresh();
    }

    internal void SetStatus(string message)
    {
        Actions.Text = message.Length <= 80 ? message : message[..77] + "...";
        Actions.ToolTipText = message;
    }

    private void SetEnabled(bool enabled)
    {
        _settings.TypeOutEnabled = enabled;
        _changed(_settings);
        Refresh();
    }

    private void SetDelay(int milliseconds)
    {
        _settings.TypeOutDelayMs = milliseconds;
        _changed(_settings);
        Refresh();
    }

    internal void Refresh()
    {
        Parent.Checked = _settings.TypeOutEnabled;
        Parent.ToolTipText = _settings.TypeOutEnabled ? "Enabled. Arm entries in the picker." : "Disabled. Enable from the On submenu.";
        On.Checked = _settings.TypeOutEnabled;
        Off.Checked = !_settings.TypeOutEnabled;
        Speed.Enabled = _settings.TypeOutEnabled;
        Hint.Enabled = _settings.TypeOutEnabled;
        foreach (ToolStripItem item in Speed.DropDownItems)
        {
            item.Enabled = _settings.TypeOutEnabled;
            if (item is ToolStripMenuItem speed)
                speed.Checked = speed.Tag is int ms && ms == _settings.TypeOutDelayMs;
        }
        foreach (ToolStripItem item in Actions.DropDownItems)
            item.Enabled = _settings.TypeOutEnabled;
    }
}
