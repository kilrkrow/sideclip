using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Sideclip.Hotkeys;
using Sideclip.Obsidian;
using Sideclip.Screenshot;

namespace Sideclip.Settings;

/// <summary>Hotkeys plus the Screenshots group (copy to folder, Obsidian daily note).</summary>
internal sealed class SettingsForm : Form
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int VkLwin = 0x5B;
    private const int VkRwin = 0x5C;

    // Layout: 12px from window and group edges, 8px between neighbours, 24px+ hit targets.
    private const int Edge = 12;
    private const int Gap = 8;
    private const int RowH = 26;
    private const int ButtonH = 28;
    private const int CheckH = 24;
    private const int Indent = 20;

    private static readonly Color Back = Color.FromArgb(30, 30, 30);
    private static readonly Color Field = Color.FromArgb(45, 45, 45);
    private static readonly Color Text1 = Color.Gainsboro;
    private static readonly Color Text2 = Color.Silver;
    private static readonly Color Warn = Color.FromArgb(230, 180, 90);

    private readonly string _screenshotsFolder;
    private readonly TextBox _picker;
    private readonly TextBox _shot;
    private readonly CheckBox _copy;
    private readonly TextBox _copyFolder;
    private readonly Button _copyBrowse;
    private readonly Label _copyNote;
    private readonly CheckBox _daily;
    private readonly ComboBox _vault;
    private readonly Button _vaultBrowse;
    private readonly Label _dailyNote;
    private readonly CheckBox _ocr;
    private readonly bool _ocrAvailable;

    public string PickerHotkey => _picker.Text.Trim();
    public string ScreenshotHotkey => _shot.Text.Trim();
    public bool ScreenshotCopyEnabled => _copy.Checked;
    public string ScreenshotCopyFolder => _copyFolder.Text.Trim();
    public bool DailyNoteEnabled => _daily.Checked;
    public string DailyNoteVault => (_vault.SelectedItem as VaultItem)?.Path ?? string.Empty;
    public bool DailyNoteOcr => _ocr.Checked;

    private readonly bool _inactive;

    /// <param name="openInactive">Test hook only: show without taking keyboard focus from the user.</param>
    public SettingsForm(AppSettings settings, string screenshotsFolder, IReadOnlyList<ObsidianVault> vaults, bool ocrAvailable, bool openInactive = false)
    {
        _screenshotsFolder = screenshotsFolder;
        _inactive = openInactive;
        _ocrAvailable = ocrAvailable;

        Text = "Sideclip settings";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Font;
        Font = new Font("Segoe UI", 9f);
        BackColor = Back;
        ForeColor = Text1;

        const int width = 480;
        const int fieldX = 150;
        var inner = width - 2 * Edge;

        // Hotkeys (unchanged behaviour).
        Controls.Add(new Label { Text = "Picker", Location = new Point(Edge, Edge + 5), AutoSize = true, ForeColor = Text1 });
        _picker = CaptureBox(new Point(fieldX, Edge), width - Edge - fieldX, settings.PickerHotkey);
        _picker.AccessibleName = "Picker hotkey";
        Controls.Add(_picker);

        var y = Edge + RowH + Gap + 4;
        Controls.Add(new Label { Text = "Screenshot path", Location = new Point(Edge, y + 5), AutoSize = true, ForeColor = Text1 });
        _shot = CaptureBox(new Point(fieldX, y), width - Edge - fieldX, settings.ScreenshotHotkey);
        _shot.AccessibleName = "Screenshot path hotkey";
        Controls.Add(_shot);

        y += RowH + Gap;
        Controls.Add(new Label
        {
            Text = "Click a box and press the combo. Back clears. Win+V stays with Windows.",
            Location = new Point(Edge, y),
            Size = new Size(inner, 20),
            ForeColor = Text2
        });

        // Screenshots group.
        y += 20 + Gap;
        var group = new GroupBox
        {
            Text = "Screenshots",
            Location = new Point(Edge, y),
            Width = inner,
            ForeColor = Text1,
            BackColor = Back
        };
        Controls.Add(group);

        var gx = Edge;
        var gw = inner - 2 * Edge;
        var browseW = 86;
        var gy = 24;

        _copy = new CheckBox
        {
            Text = "Also copy new screenshots to a folder",
            Location = new Point(gx, gy),
            Size = new Size(gw, CheckH),
            Checked = settings.ScreenshotCopyEnabled,
            ForeColor = Text1
        };
        group.Controls.Add(_copy);

        gy += CheckH + 6;
        var x1 = gx + Indent;
        _copyFolder = new TextBox
        {
            Location = new Point(x1, gy + 1),
            Width = gx + gw - browseW - Gap - x1,
            Text = settings.ScreenshotCopyFolder ?? string.Empty,
            BackColor = Field,
            ForeColor = Text1,
            AccessibleName = "Copy folder"
        };
        _copyFolder.AutoSize = false;
        _copyFolder.Height = RowH;
        group.Controls.Add(_copyFolder);
        _copyBrowse = new Button
        {
            Text = "Browse...",
            Location = new Point(gx + gw - browseW, gy),
            Size = new Size(browseW, ButtonH),
            AccessibleDescription = "Browse for the copy folder",
            ForeColor = Color.Black,
            BackColor = SystemColors.Control,
            UseVisualStyleBackColor = true
        };
        group.Controls.Add(_copyBrowse);

        gy += ButtonH + 6;
        _copyNote = new Label { Location = new Point(x1, gy), Size = new Size(gx + gw - x1, 18), ForeColor = Text2, AutoEllipsis = true };
        group.Controls.Add(_copyNote);

        gy += 18 + Gap;
        _daily = new CheckBox
        {
            Text = "Add to Obsidian daily note",
            Location = new Point(x1, gy),
            Size = new Size(gx + gw - x1, CheckH),
            Checked = settings.DailyNoteEnabled,
            ForeColor = Text1
        };
        group.Controls.Add(_daily);

        gy += CheckH + 6;
        var x2 = x1 + Indent;
        _vault = new ComboBox
        {
            Location = new Point(x2, gy + 1),
            Width = gx + gw - browseW - Gap - x2,
            DropDownStyle = ComboBoxStyle.DropDownList,
            DrawMode = DrawMode.OwnerDrawFixed,
            ItemHeight = 20,
            BackColor = Field,
            ForeColor = Text1,
            FlatStyle = FlatStyle.Flat,
            AccessibleName = "Obsidian vault"
        };
        _vault.DrawItem += DrawVaultItem;
        group.Controls.Add(_vault);
        _vaultBrowse = new Button
        {
            Text = "Browse...",
            Location = new Point(gx + gw - browseW, gy),
            Size = new Size(browseW, ButtonH),
            AccessibleDescription = "Browse for the vault folder",
            ForeColor = Color.Black,
            BackColor = SystemColors.Control,
            UseVisualStyleBackColor = true
        };
        group.Controls.Add(_vaultBrowse);

        gy += ButtonH + 6;
        _dailyNote = new Label { Location = new Point(x2, gy), Size = new Size(gx + gw - x2, 48), ForeColor = Text2 };
        group.Controls.Add(_dailyNote);

        gy += 48 + 4;
        _ocr = new CheckBox
        {
            Text = ocrAvailable
                ? "Add OCR text under each screenshot (Windows OCR)"
                : "Add OCR text (no Windows OCR language installed)",
            Location = new Point(x2, gy),
            Size = new Size(gx + gw - x2, CheckH),
            Checked = settings.DailyNoteOcr,
            ForeColor = Text1
        };
        group.Controls.Add(_ocr);

        gy += CheckH + Edge - 4;
        group.Height = gy;

        // Vault list: obsidian.json first, then the saved one if it is not registered.
        foreach (var v in vaults)
            AddVault(v.Path);
        if (!string.IsNullOrWhiteSpace(settings.DailyNoteVault))
            SelectVault(AddVault(settings.DailyNoteVault));
        else if (_vault.Items.Count > 0)
            _vault.SelectedIndex = 0;

        // Buttons, then the version in the bottom-right corner.
        y += group.Height + Edge;
        var ok = new Button { Text = "OK", DialogResult = DialogResult.OK, Location = new Point(width - Edge - 84 - Gap - 84, y), Size = new Size(84, ButtonH), ForeColor = Color.Black, BackColor = SystemColors.Control, UseVisualStyleBackColor = true };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(width - Edge - 84, y), Size = new Size(84, ButtonH), ForeColor = Color.Black, BackColor = SystemColors.Control, UseVisualStyleBackColor = true };
        AcceptButton = ok;
        CancelButton = cancel;
        Controls.Add(ok);
        Controls.Add(cancel);
        ActiveControl = ok;

        y += ButtonH + 6;
        var version = new Label
        {
            Name = "VersionLabel",
            Text = "Sideclip " + AppVersion.Display(),
            Location = new Point(Edge, y),
            Size = new Size(inner, 18),
            TextAlign = ContentAlignment.MiddleRight,
            ForeColor = Color.Gray
        };
        Controls.Add(version);
        ClientSize = new Size(width, y + 18 + Edge - 2);

        _copy.CheckedChanged += (_, _) => UpdateStates();
        _copyFolder.TextChanged += (_, _) => UpdateStates();
        _daily.CheckedChanged += (_, _) => UpdateStates();
        _vault.SelectedIndexChanged += (_, _) => UpdateStates();
        _ocr.CheckedChanged += (_, _) => UpdateStates();
        _copyBrowse.Click += (_, _) => BrowseCopyFolder();
        _vaultBrowse.Click += (_, _) => BrowseVault();
        UpdateStates();
    }

    protected override bool ShowWithoutActivation => _inactive;

    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            if (_inactive)
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
            return cp;
        }
    }

    private void UpdateStates()
    {
        var copyOn = _copy.Checked;
        var folderOk = ScreenshotRules.IsCopyFolderValid(_copyFolder.Text, _screenshotsFolder, out var reason);
        _copyFolder.Enabled = copyOn;
        _copyBrowse.Enabled = copyOn;
        if (copyOn && !folderOk)
        {
            _copyNote.Text = reason;
            _copyNote.ForeColor = Warn;
        }
        else
        {
            _copyNote.Text = "Watches " + _screenshotsFolder;
            _copyNote.ForeColor = Text2;
        }

        // Setting 2 depends on Setting 1 being on with a usable folder. Its own values are kept.
        var dailyAllowed = copyOn && folderOk;
        _daily.Enabled = dailyAllowed;
        var dailyOn = dailyAllowed && _daily.Checked;
        _vault.Enabled = dailyOn;
        _vaultBrowse.Enabled = dailyOn;
        // OCR sits under the daily note and only means something while it is on.
        _ocr.Enabled = dailyOn && _ocrAvailable;
        _dailyNote.ForeColor = Text2;

        if (!dailyAllowed)
        {
            _dailyNote.Text = "Needs the copy option above, with a valid folder.";
            return;
        }

        if (!_daily.Checked)
        {
            _dailyNote.Text = "Adds one timestamped line per screenshot to today's note.";
            return;
        }

        var vault = DailyNoteVault;
        if (string.IsNullOrWhiteSpace(vault) || !Directory.Exists(vault))
        {
            _dailyNote.Text = string.IsNullOrWhiteSpace(vault) ? "Pick a vault." : "Vault folder not found.";
            _dailyNote.ForeColor = Warn;
            return;
        }

        var config = DailyNotesConfig.Load(vault);
        var rel = config.RelativeNotePath(DateTime.Now);
        _dailyNote.Text = ScreenshotRules.IsInside(_copyFolder.Text.Trim(), vault)
            ? "Today: " + rel + ". Screenshots are embedded in the note."
            : "Today: " + rel + ". The copy folder is outside the vault, so the note gets file links instead of embeds.";
    }

    private void BrowseCopyFolder()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Folder for screenshot copies",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true
        };
        if (Directory.Exists(_copyFolder.Text.Trim()))
            dlg.InitialDirectory = _copyFolder.Text.Trim();
        if (dlg.ShowDialog(this) == DialogResult.OK)
            _copyFolder.Text = dlg.SelectedPath;
    }

    private void BrowseVault()
    {
        using var dlg = new FolderBrowserDialog
        {
            Description = "Obsidian vault folder",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = false
        };
        if (Directory.Exists(DailyNoteVault))
            dlg.InitialDirectory = DailyNoteVault;
        if (dlg.ShowDialog(this) == DialogResult.OK)
            SelectVault(AddVault(dlg.SelectedPath));
    }

    private int AddVault(string path)
    {
        for (var i = 0; i < _vault.Items.Count; i++)
        {
            if (_vault.Items[i] is VaultItem v && ScreenshotRules.SamePath(v.Path, path))
                return i;
        }

        return _vault.Items.Add(new VaultItem(path));
    }

    private void SelectVault(int index)
    {
        if (index >= 0 && index < _vault.Items.Count)
            _vault.SelectedIndex = index;
    }

    private void DrawVaultItem(object? sender, DrawItemEventArgs e)
    {
        var selected = (e.State & DrawItemState.Selected) != 0 && (e.State & DrawItemState.ComboBoxEdit) == 0;
        var enabled = _vault.Enabled;
        using var back = new SolidBrush(selected ? Color.FromArgb(0, 90, 158) : enabled ? Field : Back);
        e.Graphics.FillRectangle(back, e.Bounds);
        if (e.Index >= 0 && e.Index < _vault.Items.Count)
        {
            TextRenderer.DrawText(
                e.Graphics,
                _vault.Items[e.Index]?.ToString() ?? string.Empty,
                Font,
                e.Bounds,
                enabled ? Text1 : Color.Gray,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }
    }

    private TextBox CaptureBox(Point loc, int width, string value)
    {
        var tb = new TextBox
        {
            Location = loc,
            Width = width,
            ReadOnly = true,
            Text = value,
            BackColor = Field,
            ForeColor = Text1
        };
        tb.AutoSize = false;
        tb.Height = RowH;
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

    private sealed record VaultItem(string Path)
    {
        public override string ToString() => Path;
    }
}
