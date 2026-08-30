using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Sideclip.Hotkeys;
using Sideclip.Settings;

namespace Sideclip.Settings;

internal sealed class HotkeySettingsForm : Form
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int VkLwin = 0x5B;
    private const int VkRwin = 0x5C;

    private readonly TextBox _picker;
    private readonly TextBox _shot;
    public string PickerHotkey => _picker.Text.Trim();
    public string ScreenshotHotkey => _shot.Text.Trim();

    public HotkeySettingsForm(AppSettings settings)
    {
        Text = "Sideclip hotkeys";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(420, 180);
        Font = new Font("Segoe UI", 9f);
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.Gainsboro;

        Controls.Add(new Label { Text = "Picker", Location = new Point(12, 18), AutoSize = true, ForeColor = Color.Gainsboro });
        _picker = CaptureBox(new Point(150, 14), settings.PickerHotkey);
        Controls.Add(_picker);

        Controls.Add(new Label { Text = "Screenshot path", Location = new Point(12, 56), AutoSize = true, ForeColor = Color.Gainsboro });
        _shot = CaptureBox(new Point(150, 52), settings.ScreenshotHotkey);
        Controls.Add(_shot);

        Controls.Add(new Label
        {
            Text = "Click a box and press the combo. Back clears. Win+V stays with Windows.",
            Location = new Point(12, 96),
            Size = new Size(396, 32),
            ForeColor = Color.Silver
        });

        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(236, 138), Width = 80 };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(324, 138), Width = 80 };
        AcceptButton = ok;
        CancelButton = cancel;
        Controls.Add(ok);
        Controls.Add(cancel);
    }

    private TextBox CaptureBox(Point loc, string value)
    {
        var tb = new TextBox
        {
            Location = loc,
            Width = 250,
            ReadOnly = true,
            Text = value,
            BackColor = Color.FromArgb(45, 45, 45),
            ForeColor = Color.Gainsboro
        };
        tb.KeyDown += OnCapture;
        return tb;
    }

    private void OnCapture(object? sender, KeyEventArgs e)
    {
        e.SuppressKeyPress = true;
        var tb = (TextBox)sender!;
        if (e.KeyCode == Keys.Back)
        {
            tb.Text = string.Empty;
            return;
        }

        var win = (GetAsyncKeyState(VkLwin) & 0x8000) != 0 || (GetAsyncKeyState(VkRwin) & 0x8000) != 0;
        var text = HotkeyParser.FromKeyEvent(e, win);
        if (!string.IsNullOrEmpty(text))
            tb.Text = text;
    }
}
