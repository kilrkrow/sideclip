using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Sideclip.History;
using Sideclip.Hotkeys;
using Sideclip.Settings;

namespace Sideclip.Settings;

internal sealed class SettingsForm : Form
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int VkLwin = 0x5B;
    private const int VkRwin = 0x5C;

    // Layout rails: 12px window margin, 26px tall controls (>= 24px hit
    // targets), 8px between stacked controls, 12px between side-by-side ones.
    private const int ControlHeight = 26;

    private readonly TextBox _picker;
    private readonly TextBox _shot;
    private readonly CapBox _textCap;
    private readonly CheckBox _textUnlimited;
    private readonly CapBox _imageCap;
    private readonly CheckBox _imageUnlimited;

    private readonly int _savedTextCap;
    private readonly int _savedImageCap;
    private readonly Func<int> _storedText;
    private readonly Func<int> _storedImages;

    /// <summary>Asks the user to confirm a removal. Replaceable for tests.</summary>
    internal Func<IWin32Window, string, bool> ConfirmRemoval { get; set; } = ShowConfirm;

    public string PickerHotkey => _picker.Text.Trim();
    public string ScreenshotHotkey => _shot.Text.Trim();

    /// <summary>Chosen cap; 0 = Unlimited.</summary>
    public int MaxTextClips => _textUnlimited.Checked ? AppSettings.Unlimited : _textCap.ValueOr(_savedTextCap);
    public int MaxImageClips => _imageUnlimited.Checked ? AppSettings.Unlimited : _imageCap.ValueOr(_savedImageCap);

    /// <summary>
    /// Exact counts the user confirmed removing (0 unless a lowering was
    /// confirmed). Only valid when the dialog returned OK.
    /// </summary>
    public int ConfirmedTextRemovals { get; private set; }
    public int ConfirmedImageRemovals { get; private set; }

    /// <param name="storedText">Stored non-pinned text clips right now (read again when OK is pressed).</param>
    /// <param name="storedImages">Stored non-pinned image clips right now (read again when OK is pressed).</param>
    public SettingsForm(AppSettings settings, Func<int> storedText, Func<int> storedImages)
    {
        _savedTextCap = settings.MaxTextClips;
        _savedImageCap = settings.MaxImageClips;
        _storedText = storedText;
        _storedImages = storedImages;

        Text = "Sideclip settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(420, 328);
        Font = new Font("Segoe UI", 9f);
        BackColor = Color.FromArgb(30, 30, 30);
        ForeColor = Color.Gainsboro;

        Controls.Add(new Label { Text = "Picker", Location = new Point(12, 18), AutoSize = true, ForeColor = Color.Gainsboro });
        _picker = CaptureBox(new Point(150, 14), settings.PickerHotkey, "Picker hotkey");
        Controls.Add(_picker);

        Controls.Add(new Label { Text = "Screenshot path", Location = new Point(12, 56), AutoSize = true, ForeColor = Color.Gainsboro });
        _shot = CaptureBox(new Point(150, 52), settings.ScreenshotHotkey, "Screenshot path hotkey");
        Controls.Add(_shot);

        Controls.Add(new Label
        {
            Text = "Click a box and press the combo. Back clears. Win+V stays with Windows.",
            Location = new Point(12, 88),
            Size = new Size(396, 32),
            ForeColor = Color.Silver
        });

        Controls.Add(new Label { Text = "History limits", Location = new Point(12, 132), AutoSize = true, ForeColor = Color.Gainsboro });

        Controls.Add(new Label { Text = "Text clips to keep", Location = new Point(12, 162), AutoSize = true, ForeColor = Color.Gainsboro });
        _textCap = NewCapBox(new Point(150, 158), AppSettings.MaxTextCap, settings.MaxTextClips, AppSettings.DefaultMaxTextClips, "Text clips limit");
        _textUnlimited = UnlimitedBox(new Point(252, 158), settings.MaxTextClips, _textCap, "Unlimited text clips");
        Controls.Add(_textCap);
        Controls.Add(_textUnlimited);

        Controls.Add(new Label { Text = "Image clips to keep", Location = new Point(12, 196), AutoSize = true, ForeColor = Color.Gainsboro });
        _imageCap = NewCapBox(new Point(150, 192), AppSettings.MaxImageCap, settings.MaxImageClips, AppSettings.DefaultMaxImageClips, "Image clips limit");
        _imageUnlimited = UnlimitedBox(new Point(252, 192), settings.MaxImageClips, _imageCap, "Unlimited image clips");
        Controls.Add(_imageCap);
        Controls.Add(_imageUnlimited);

        Controls.Add(new Label
        {
            Text = StoredSummary(storedText(), storedImages())
                   + " At the limit, the oldest unpinned clip of that type is removed. Pinned clips are never removed.",
            Location = new Point(12, 232),
            Size = new Size(396, 48),
            ForeColor = Color.Silver
        });

        var ok = new Button { Text = "OK", Location = new Point(236, 290), Size = new Size(80, ControlHeight) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(324, 290), Size = new Size(80, ControlHeight) };
        ok.Click += OnOkClick;
        AcceptButton = ok;
        CancelButton = cancel;
        Controls.Add(ok);
        Controls.Add(cancel);

        _picker.TabIndex = 0;
        _shot.TabIndex = 1;
        _textCap.TabIndex = 2;
        _textUnlimited.TabIndex = 3;
        _imageCap.TabIndex = 4;
        _imageUnlimited.TabIndex = 5;
        ok.TabIndex = 6;
        cancel.TabIndex = 7;
    }

    internal static string StoredSummary(int text, int images) =>
        "Stored now: " + text + (text == 1 ? " text clip, " : " text clips, ")
        + images + (images == 1 ? " image clip." : " image clips.");

    private static CapBox NewCapBox(Point loc, int max, int current, int fallback, string name)
    {
        var unlimited = AppSettings.NormalizeCap(current, max) == AppSettings.Unlimited;
        return new CapBox(max)
        {
            Location = loc,
            AutoSize = false,
            Size = new Size(90, ControlHeight),
            Text = (unlimited ? fallback : current).ToString(System.Globalization.CultureInfo.InvariantCulture),
            Enabled = !unlimited,
            Name = name,
            AccessibleName = name,
            BackColor = Color.FromArgb(45, 45, 45),
            ForeColor = Color.Gainsboro
        };
    }
    private static CheckBox UnlimitedBox(Point loc, int current, CapBox partner, string name)
    {
        var box = new CheckBox
        {
            Text = "Unlimited",
            Location = loc,
            AutoSize = false,
            Size = new Size(100, ControlHeight),
            TextAlign = ContentAlignment.MiddleLeft,
            Checked = current <= 0,
            Name = name,
            AccessibleName = name,
            ForeColor = Color.Gainsboro
        };
        box.CheckedChanged += (_, _) => partner.Enabled = !box.Checked;
        return box;
    }

    private void OnOkClick(object? sender, EventArgs e)
    {
        // A limit box that is empty or 0 is not accepted (0 is not a limit; use
        // Unlimited). Put the previous value back and stay in the dialog.
        if (!AcceptCap(_textCap, _textUnlimited, _savedTextCap, AppSettings.MaxTextCap, AppSettings.DefaultMaxTextClips)
            || !AcceptCap(_imageCap, _imageUnlimited, _savedImageCap, AppSettings.MaxImageCap, AppSettings.DefaultMaxImageClips))
            return;

        var newText = MaxTextClips;
        var newImage = MaxImageClips;

        // Counted now, not when the window opened, so the number in the
        // confirmation is exact even if clips arrived in between.
        var removeText = ClipCaps.PlanLowering(_savedTextCap, newText, _storedText());
        var removeImage = ClipCaps.PlanLowering(_savedImageCap, newImage, _storedImages());

        if (removeText > 0 || removeImage > 0)
        {
            // No limit was changed: the profile is simply over its limits (for
            // example after an upgrade, or after limits were set while clips
            // were stored). Same prompt, same exact counts.
            var overLimitOnly = newText == _savedTextCap && newImage == _savedImageCap;
            var message = BuildRemovalMessage(removeText, newText, removeImage, newImage, overLimitOnly);
            if (!ConfirmRemoval(this, message))
            {
                ConfirmedTextRemovals = 0;
                ConfirmedImageRemovals = 0;
                if (!overLimitOnly)
                {
                    // A limit was lowered and not confirmed: nothing is applied,
                    // the limits go back to what they were, and the dialog stays open.
                    RestoreCaps();
                    return;
                }

                // Limits unchanged and removal declined: keep the clips, keep
                // the limits, and let the rest of the dialog (hotkeys) be saved.
                // The same prompt comes back the next time OK is pressed.
                DialogResult = DialogResult.OK;
                return;
            }
        }

        ConfirmedTextRemovals = removeText;
        ConfirmedImageRemovals = removeImage;
        DialogResult = DialogResult.OK;
    }

    private static bool AcceptCap(CapBox box, CheckBox unlimited, int saved, int max, int fallback)
    {
        if (unlimited.Checked)
            return true;

        var v = box.ParsedValue();
        if (v is null or < AppSettings.MinCap)
        {
            Restore(box, unlimited, saved, max, fallback);
            box.Focus();
            box.SelectAll();
            return false;
        }

        box.SetValue(Math.Min(v.Value, max)); // too big is clamped to the maximum
        return true;
    }

    private void RestoreCaps()
    {
        Restore(_textCap, _textUnlimited, _savedTextCap, AppSettings.MaxTextCap, AppSettings.DefaultMaxTextClips);
        Restore(_imageCap, _imageUnlimited, _savedImageCap, AppSettings.MaxImageCap, AppSettings.DefaultMaxImageClips);
    }

    private static void Restore(CapBox box, CheckBox unlimited, int saved, int max, int fallback)
    {
        var isUnlimited = AppSettings.NormalizeCap(saved, max) == AppSettings.Unlimited;
        unlimited.Checked = isUnlimited;
        box.SetValue(isUnlimited ? fallback : saved);
        box.Enabled = !isUnlimited;
    }

    internal static string BuildRemovalMessage(int textCount, int textCap, int imageCount, int imageCap, bool overLimitOnly = false)
    {
        var lines = new List<string>();
        if (textCount > 0)
            lines.Add("  " + textCount + (textCount == 1 ? " text clip" : " text clips") + " (limit " + textCap + ")");
        if (imageCount > 0)
            lines.Add("  " + imageCount + (imageCount == 1 ? " image clip" : " image clips") + " (limit " + imageCap + "), including the image files");

        var intro = overLimitOnly
            ? "Your clip history is over its limits. This will permanently remove the oldest unpinned clips:"
            : "This will permanently remove the oldest unpinned clips:";
        return intro
               + Environment.NewLine + Environment.NewLine
               + string.Join(Environment.NewLine, lines)
               + Environment.NewLine + Environment.NewLine
               + "Pinned clips are never removed. This cannot be undone."
               + Environment.NewLine + Environment.NewLine
               + "Remove them?";
    }

    private static bool ShowConfirm(IWin32Window owner, string message) =>
        MessageBox.Show(
            owner,
            message,
            "Remove old clips?",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2) == DialogResult.Yes;

    private TextBox CaptureBox(Point loc, string value, string name)
    {
        var tb = new TextBox
        {
            Location = loc,
            AutoSize = false,
            Size = new Size(250, ControlHeight),
            ReadOnly = true,
            Text = value,
            Name = name,
            AccessibleName = name,
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

    /// <summary>
    /// Digits-only text box for a clip limit. A plain 26px box (a spin control
    /// has 10px arrows, below the 24px hit-target minimum); Up/Down keys change
    /// the value by one.
    /// </summary>
    private sealed class CapBox : TextBox
    {
        private readonly int _max;

        public CapBox(int max)
        {
            _max = max;
            MaxLength = max.ToString(System.Globalization.CultureInfo.InvariantCulture).Length;
        }

        public int? ParsedValue() =>
            int.TryParse(Text.Trim(), System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var v)
                ? v
                : null;

        public int ValueOr(int fallback)
        {
            var v = ParsedValue();
            return v is null or < AppSettings.MinCap ? fallback : Math.Min(v.Value, _max);
        }

        public void SetValue(int v)
        {
            Text = Math.Min(Math.Max(v, AppSettings.MinCap), _max).ToString(System.Globalization.CultureInfo.InvariantCulture);
            SelectionStart = Text.Length;
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (!char.IsControl(e.KeyChar) && (e.KeyChar < '0' || e.KeyChar > '9'))
                e.Handled = true;
            base.OnKeyPress(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode is Keys.Up or Keys.Down)
            {
                var step = e.KeyCode == Keys.Up ? 1 : -1;
                SetValue((ParsedValue() ?? AppSettings.MinCap) + step);
                e.SuppressKeyPress = true;
                e.Handled = true;
                return;
            }

            base.OnKeyDown(e);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            // Pasted text: keep digits only.
            var digits = new string(Text.Where(c => c >= '0' && c <= '9').ToArray());
            if (digits != Text)
            {
                Text = digits;
                SelectionStart = Text.Length;
                return;
            }

            base.OnTextChanged(e);
        }
    }
}