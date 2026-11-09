using System.Windows.Forms;

namespace Sideclip.Tray;

// Pure menu composition: no NotifyIcon or input registration in tests.
internal sealed class TrayMenuModel
{
    internal ToolStripMenuItem SettingsItem { get; } = new("Settings...");
    internal TrayMenuModel(Action openSettings) => SettingsItem.Click += (_, _) => openSettings();
    internal ContextMenuStrip Build(ContextMenuStrip menu, ToolStripMenuItem ttlMenu)
    {
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(ttlMenu);
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add(SettingsItem);
        return menu;
    }
}
