using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Sideclip.Hotkeys;
using Sideclip.Settings;

namespace Sideclip.Settings;

internal sealed class HotkeySettingsForm : Form
{
    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vKey);
    private const int VkLwin = 0x5B;
    private const int VkRwin = 0x5C;
    private readonly TextBox _picker;
    private readonly TextBox _shot;
    private readonly TextBox _go;
    private readonly Label _error;

    public string PickerHotkey => _picker.Text.Trim();
    public string ScreenshotHotkey => _shot.Text.Trim();
    public string TypeOutGoHotkey => _go.Text.Trim();

    public HotkeySettingsForm(AppSettings settings)
    {
        Text = "Sideclip hotkeys";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = MinimizeBox = false;
        ClientSize = new Size(440, 255);
        Font = new Font("Segoe UI", 9f);
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.Gainsboro;
        _picker = AddBox("Picker", 14, settings.PickerHotkey);
        _shot = AddBox("Screenshot path", 52, settings.ScreenshotHotkey);
        _go = AddBox("Type-out Go", 90, settings.TypeOutGoHotkey);
        Controls.Add(new Label { Text = "Click a box and press the combo. Back clears. Esc cancels. Win+V stays with Windows.", Location = new Point(12, 132), Size = new Size(416, 32), ForeColor = Color.Silver });
        _error = new Label { Location = new Point(12, 169), Size = new Size(416, 32), ForeColor = Color.LightSalmon };
        Controls.Add(_error);
        var ok = new Button { Text = "OK", Location = new Point(256, 213), Width = 80 };
        ok.Click += (_, _) =>
        {
            var p = HotkeyParser.NormalizedOrError(_picker.Text);
            var s = HotkeyParser.NormalizedOrError(_shot.Text);
            var g = HotkeyParser.NormalizedOrError(_go.Text);
            if (p == "Reserved Windows hotkeys cannot be assigned." || s == "Reserved Windows hotkeys cannot be assigned." || g == "Reserved Windows hotkeys cannot be assigned.")
            { _error.Text = "Reserved hotkey. See the picker instruction."; return; }
            foreach (var item in new[] { _picker.Text, _shot.Text, _go.Text })
                if (!string.IsNullOrWhiteSpace(item) && !HotkeyParser.TryParse(item, out _, out _)) { _error.Text = "Each hotkey needs a non-modifier key, or be blank."; return; }
            var values = new[] { HotkeyParser.Normalize(_picker.Text), HotkeyParser.Normalize(_shot.Text), HotkeyParser.Normalize(_go.Text) };
            if (values.Any(string.IsNullOrEmpty) && _settingsNeedsGo(settings) && string.IsNullOrWhiteSpace(_go.Text))
            { _error.Text = "Type-out is enabled, so its Go hotkey is required."; return; }
            if (values.Where(v => !string.IsNullOrEmpty(v)).Distinct(StringComparer.OrdinalIgnoreCase).Count() < values.Count(v => !string.IsNullOrEmpty(v)))
            { _error.Text = "Picker, screenshot, and Go cannot use the same hotkey."; return; }
            DialogResult = DialogResult.OK;
            Close();
        };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(344, 213), Width = 80 };

        CancelButton = cancel;
        FormClosing += (_, e) => { if (DialogResult != DialogResult.OK && DialogResult != DialogResult.Cancel) DialogResult = DialogResult.Cancel; };
        Controls.Add(ok);
        Controls.Add(cancel);
    }

    private static bool _settingsNeedsGo(AppSettings s) => s.TypeOutEnabled;

    private TextBox AddBox(string label, int y, string value)
    {
        Controls.Add(new Label { Text = label, Location = new Point(12, y + 4), AutoSize = true });
        var box = new TextBox { Location = new Point(169, y), Width = 259, ReadOnly = true, Text = value, BackColor = Color.FromArgb(45, 45, 45), ForeColor = Color.Gainsboro };
        box.KeyDown += (_, e) =>
        {
            e.SuppressKeyPress = true;
            if (e.KeyCode == Keys.Back) { box.Text = string.Empty; return; }
            var down = (GetAsyncKeyState(VkLwin) & 0x8000) != 0 || (GetAsyncKeyState(VkRwin) & 0x8000) != 0;
            var parsed = HotkeyParser.FromKeyEvent(e, down);
            if (!string.IsNullOrEmpty(parsed)) box.Text = parsed;
        };
        Controls.Add(box);
        return box;
    }
}
