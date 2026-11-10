using System.Drawing;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Sideclip.Hotkeys;
using Sideclip.Obsidian;
using Sideclip.Screenshot;

namespace Sideclip.Settings;

internal sealed class SettingsForm : Form
{
    internal static readonly Color DisabledBack = Color.FromArgb(0x26, 0x2A, 0x35);
    internal static readonly Color DisabledFore = Color.FromArgb(0x9A, 0xA0, 0xAC);
    private static readonly Color FieldBack = Color.FromArgb(38, 42, 53);
    private static readonly Color Warn = Color.FromArgb(230, 180, 90);
    private static readonly Color Text2 = Color.FromArgb(192, 201, 212);
    private const int NestIndent = 28;

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int key);

    private readonly TextBox _picker, _shot, _typeOut;
    private readonly Label _typeOutLabel, _delayLabel;
    private readonly CheckBox _enabled;
    private readonly NumericUpDown _delay;
    private readonly Label _error, _status;
    private readonly AppSettings _snapshot;
    private readonly Func<AppSettings, string?> _apply;
    private readonly string _screenshotsFolder;
    private readonly bool _ocrAvailable;
    private readonly bool _inactive;

    private readonly CheckBox _copy;
    private readonly TextBox _copyFolder;
    private readonly Button _copyBrowse;
    private readonly Label _copyNote;
    private readonly CheckBox _daily;
    private readonly ComboBox _vault;
    private readonly Button _vaultBrowse;
    private readonly Label _dailyNote;
    private readonly CheckBox _ocr;
    private readonly Label _headingLabel;
    private readonly TextBox _heading;
    private readonly Label _headingNote;
    private readonly Label _vaultFolderLabel;
    private readonly TextBox _vaultFolder;
    private readonly Label _vaultFolderNote;
    private readonly Label _linkStyleLabel;
    private readonly ComboBox _linkStyle;
    private string _lastValidHeading;

    public SettingsForm(
        AppSettings settings,
        Func<AppSettings, string?> apply,
        string screenshotsFolder,
        IReadOnlyList<ObsidianVault> vaults,
        bool ocrAvailable,
        bool openInactive = false)
    {
        _snapshot = settings.Copy();
        _apply = apply;
        _screenshotsFolder = screenshotsFolder;
        _ocrAvailable = ocrAvailable;
        _inactive = openInactive;

        Text = "Sideclip Settings - " + DisplayVersion;
        Icon = LoadProductIcon();
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterScreen;
        MaximizeBox = MinimizeBox = false;
        AutoScroll = true;
        ClientSize = new Size(600, 820);
        Font = new Font("Consolas", 10f);
        BackColor = Color.FromArgb(46, 50, 63);
        ForeColor = Color.FromArgb(229, 233, 246);

        _picker = AddBox("Picker shortcut", "PickerHotkey", 18, 16, settings.PickerHotkey, out _);
        _shot = AddBox("Screenshot path shortcut", "ScreenshotHotkey", 60, 16, settings.ScreenshotHotkey, out _);

        _enabled = new CheckBox
        {
            Name = "TypeOutEnabled",
            AccessibleName = "Enable clipboard-free text type-out",
            Text = "Enable clipboard-free text type-out",
            Location = new Point(16, 108),
            Size = new Size(560, 28),
            Checked = settings.TypeOutEnabled,
            TabIndex = 10
        };
        Controls.Add(_enabled);

        _typeOut = AddBox("Text-only type-out shortcut", "TypeOutGoHotkey", 144, 16 + NestIndent, settings.TypeOutGoHotkey, out _typeOutLabel);
        _typeOut.TabIndex = 11;
        _delayLabel = new Label
        {
            Name = "TypeOutDelayLabel",
            Text = "Delay between chunks (ms)",
            Location = new Point(16 + NestIndent, 186),
            Size = new Size(320, 24),
            TabStop = false
        };
        Controls.Add(_delayLabel);
        _delay = new NumericUpDown
        {
            Name = "TypeOutDelayMs",
            AccessibleName = "Delay between chunks in milliseconds",
            Minimum = 0,
            Maximum = 250,
            Value = Math.Clamp(settings.TypeOutDelayMs, 0, 250),
            Location = new Point(400, 182),
            Width = 160,
            BackColor = FieldBack,
            ForeColor = DisabledFore,
            TabIndex = 12
        };
        Controls.Add(_delay);
        _enabled.CheckedChanged += (_, _) => SyncTypeOutDependents();
        SyncTypeOutDependents();

        Controls.Add(new Label
        {
            Name = "TypeOutHelp",
            AccessibleName = "Type-out and hotkey help",
            Text = "Choose a shortcut box and press keys. Backspace clears.\r\nBlank means unassigned. Esc closes without applying.\r\nSelect text, focus destination, then use its shortcut.\r\nTab/Enter may navigate or submit. Insertion is not verified.",
            Location = new Point(16, 226),
            Size = new Size(548, 80),
            ForeColor = Text2
        });

        var group = new GroupBox
        {
            Name = "ScreenshotsGroup",
            Text = "Screenshots",
            Location = new Point(16, 314),
            Size = new Size(560, 390),
            ForeColor = ForeColor
        };
        Controls.Add(group);

        _copy = new CheckBox
        {
            Name = "ScreenshotCopyEnabled",
            AccessibleName = "Also copy new screenshots to a folder",
            Text = "Also copy new screenshots to a folder",
            Location = new Point(12, 24),
            Size = new Size(530, 24),
            Checked = settings.ScreenshotCopyEnabled
        };
        group.Controls.Add(_copy);

        _copyFolder = new TextBox
        {
            Name = "ScreenshotCopyFolder",
            AccessibleName = "Copy folder",
            Location = new Point(12 + NestIndent, 54),
            Width = 360,
            Text = settings.ScreenshotCopyFolder ?? "",
            BackColor = FieldBack,
            ForeColor = ForeColor
        };
        group.Controls.Add(_copyFolder);
        _copyBrowse = new Button
        {
            Name = "ScreenshotCopyBrowse",
            Text = "Browse...",
            AccessibleName = "Browse for the copy folder",
            Location = new Point(386, 52),
            Size = new Size(150, 28)
        };
        group.Controls.Add(_copyBrowse);

        _copyNote = new Label { Name = "ScreenshotCopyNote", Location = new Point(12 + NestIndent, 88), Size = new Size(520, 20), ForeColor = Text2, AutoEllipsis = true };
        group.Controls.Add(_copyNote);

        _daily = new CheckBox
        {
            Name = "DailyNoteEnabled",
            AccessibleName = "Add to Obsidian daily note",
            Text = "Add to Obsidian daily note",
            Location = new Point(12 + NestIndent, 116),
            Size = new Size(520, 24),
            Checked = settings.DailyNoteEnabled
        };
        group.Controls.Add(_daily);

        _vault = new ComboBox
        {
            Name = "DailyNoteVault",
            AccessibleName = "Obsidian vault",
            Location = new Point(12 + NestIndent * 2, 146),
            Width = 330,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = FieldBack,
            ForeColor = ForeColor,
            FlatStyle = FlatStyle.Flat
        };
        group.Controls.Add(_vault);
        _vaultBrowse = new Button
        {
            Name = "DailyNoteVaultBrowse",
            Text = "Browse...",
            AccessibleName = "Browse for the vault folder",
            Location = new Point(386, 144),
            Size = new Size(150, 28)
        };
        group.Controls.Add(_vaultBrowse);

        _vaultFolderLabel = new Label { Name = "DailyNoteVaultFolderLabel", Text = "Vault folder", Location = new Point(12 + NestIndent * 2, 180), Size = new Size(100, 24) };
        group.Controls.Add(_vaultFolderLabel);
        if (!DailyNoteText.TryNormalizeVaultFolder(settings.DailyNoteVaultFolder, out var vaultFolderInit, out _))
            vaultFolderInit = DailyNoteText.DefaultVaultFolder;
        _vaultFolder = new TextBox
        {
            Name = "DailyNoteVaultFolder",
            AccessibleName = "Vault-relative destination folder",
            Location = new Point(120 + NestIndent * 2, 178),
            Width = 330,
            Text = vaultFolderInit,
            PlaceholderText = DailyNoteText.DefaultVaultFolder,
            BackColor = FieldBack,
            ForeColor = ForeColor
        };
        group.Controls.Add(_vaultFolder);
        _vaultFolderNote = new Label { Name = "DailyNoteVaultFolderNote", Location = new Point(12 + NestIndent * 2, 208), Size = new Size(520, 20), ForeColor = Text2, AutoEllipsis = true };
        group.Controls.Add(_vaultFolderNote);

        _linkStyleLabel = new Label { Name = "DailyNoteLinkStyleLabel", Text = "Link style", Location = new Point(12 + NestIndent * 2, 236), Size = new Size(100, 24) };
        group.Controls.Add(_linkStyleLabel);
        _linkStyle = new ComboBox
        {
            Name = "DailyNoteLinkStyle",
            AccessibleName = "Daily note link style",
            Location = new Point(120 + NestIndent * 2, 234),
            Width = 330,
            DropDownStyle = ComboBoxStyle.DropDownList,
            BackColor = FieldBack,
            ForeColor = ForeColor,
            FlatStyle = FlatStyle.Flat
        };
        _linkStyle.Items.Add("Wikilink");
        _linkStyle.Items.Add("Markdown");
        var styleInit = DailyNoteText.ParseLinkStyle(settings.DailyNoteLinkStyle);
        if (styleInit == DailyNoteLinkStyle.Embed) styleInit = DailyNoteLinkStyle.Wikilink;
        _linkStyle.SelectedItem = DailyNoteText.LinkStyleName(styleInit);
        group.Controls.Add(_linkStyle);

        _headingLabel = new Label { Name = "DailyNoteHeadingLabel", Text = "Heading", Location = new Point(12 + NestIndent * 2, 268), Size = new Size(70, 24) };
        group.Controls.Add(_headingLabel);
        var saved = (settings.DailyNoteHeading ?? "").TrimEnd();
        _lastValidHeading = saved.Length == 0 || NoteSections.IsValidHeading(saved) ? saved : "";
        _heading = new TextBox
        {
            Name = "DailyNoteHeading",
            AccessibleName = "Daily note heading",
            Location = new Point(90 + NestIndent * 2, 264),
            Width = 360,
            Text = _lastValidHeading,
            PlaceholderText = "## Sideclip Clips",
            BackColor = FieldBack,
            ForeColor = ForeColor
        };
        group.Controls.Add(_heading);
        _headingNote = new Label { Name = "DailyNoteHeadingNote", Location = new Point(12 + NestIndent * 2, 296), Size = new Size(500, 20), ForeColor = Text2, AutoEllipsis = true };
        group.Controls.Add(_headingNote);
        _dailyNote = new Label { Name = "DailyNoteStatus", Location = new Point(12 + NestIndent * 2, 320), Size = new Size(500, 36), ForeColor = Text2 };
        group.Controls.Add(_dailyNote);
        _ocr = new CheckBox
        {
            Name = "DailyNoteOcr",
            AccessibleName = "Add OCR text under each screenshot",
            Text = ocrAvailable ? "Add OCR text under each screenshot (Windows OCR)" : "Add OCR text (no Windows OCR language installed)",
            Location = new Point(12 + NestIndent * 2, 358),
            Size = new Size(520, 24),
            Checked = settings.DailyNoteOcr
        };
        group.Controls.Add(_ocr);

        foreach (var v in vaults) AddVault(v.Path);
        if (!string.IsNullOrWhiteSpace(settings.DailyNoteVault)) SelectVault(AddVault(settings.DailyNoteVault));
        else if (_vault.Items.Count > 0) _vault.SelectedIndex = 0;

        _copy.CheckedChanged += (_, _) => SyncScreenshotDependents();
        _copyFolder.TextChanged += (_, _) => SyncScreenshotDependents();
        _daily.CheckedChanged += (_, _) => SyncScreenshotDependents();
        _vault.SelectedIndexChanged += (_, _) => SyncScreenshotDependents();
        _vaultFolder.TextChanged += (_, _) => SyncScreenshotDependents();
        _linkStyle.SelectedIndexChanged += (_, _) => SyncScreenshotDependents();
        _heading.TextChanged += (_, _) =>
        {
            var ht = _heading.Text.TrimEnd();
            if (ht.Length == 0 || NoteSections.IsValidHeading(ht)) _lastValidHeading = ht;
            SyncScreenshotDependents();
        };
        _copyBrowse.Click += (_, _) => BrowseCopyFolder();
        _vaultBrowse.Click += (_, _) => BrowseVault();
        SyncScreenshotDependents();

        _error = new Label { Name = "SettingsError", AccessibleName = "Settings validation error", Location = new Point(16, 720), Size = new Size(560, 40), ForeColor = Color.LightSalmon };
        _status = new Label { Name = "TypeOutStatus", AccessibleName = "Type-out status", Location = new Point(16, 762), Size = new Size(560, 28), ForeColor = Text2 };
        Controls.Add(_error); Controls.Add(_status);
        Controls.Add(new Label { Name = "VersionLabel", Text = "Sideclip " + DisplayVersion + " | kilrkrow", Location = new Point(16, 798), Size = new Size(300, 24) });
        var applyButton = new Button { Name = "ApplySettings", Text = "Apply", Location = new Point(400, 792), Size = new Size(86, 32) };
        applyButton.Click += (_, _) => ApplyChanges();
        var close = new Button { Name = "CancelSettings", Text = "Close", Location = new Point(498, 792), Size = new Size(86, 32) };
        close.Click += (_, _) => Close(); CancelButton = close;
        Controls.Add(applyButton); Controls.Add(close);
    }

    protected override bool ShowWithoutActivation => _inactive;
    protected override CreateParams CreateParams
    {
        get
        {
            var cp = base.CreateParams;
            if (_inactive) cp.ExStyle |= 0x08000000;
            return cp;
        }
    }

    internal bool TypeOutDependentsEnabled => _enabled.Checked;
    internal IEnumerable<Control> TypeOutDependentControls
    {
        get { yield return _typeOutLabel; yield return _typeOut; yield return _delayLabel; yield return _delay; }
    }

    private void SyncTypeOutDependents()
    {
        var on = _enabled.Checked;
        foreach (var c in TypeOutDependentControls)
        {
            c.Enabled = on;
            if (c is TextBox or NumericUpDown) { c.BackColor = on ? FieldBack : DisabledBack; c.ForeColor = on ? ForeColor : DisabledFore; }
            else if (c is Label) c.ForeColor = on ? ForeColor : DisabledFore;
        }
    }

    private void SyncScreenshotDependents()
    {
        var copyOn = _copy.Checked;
        var folderOk = ScreenshotRules.IsCopyFolderValid(_copyFolder.Text, _screenshotsFolder, out var reason);
        SetEnabledChrome(_copyFolder, copyOn);
        _copyBrowse.Enabled = copyOn;
        if (copyOn && !folderOk) { _copyNote.Text = reason; _copyNote.ForeColor = Warn; }
        else { _copyNote.Text = "Watches " + _screenshotsFolder; _copyNote.ForeColor = Text2; }

        var dailyAllowed = copyOn && folderOk;
        SetEnabledChrome(_daily, dailyAllowed);
        var dailyOn = dailyAllowed && _daily.Checked;
        SetEnabledChrome(_vault, dailyOn);
        _vaultBrowse.Enabled = dailyOn;
        SetEnabledChrome(_ocr, dailyOn && _ocrAvailable);
        SetEnabledChrome(_heading, dailyOn);
        _headingLabel.Enabled = dailyOn;
        _headingLabel.ForeColor = dailyOn ? ForeColor : DisabledFore;

        var vault = DailyNoteVaultPath;
        var linkMode = dailyOn && vault.Length > 0 && Directory.Exists(vault) && !ScreenshotRules.IsInside(_copyFolder.Text.Trim(), vault);
        SetEnabledChrome(_vaultFolder, linkMode);
        _vaultFolderLabel.Enabled = linkMode;
        _vaultFolderLabel.ForeColor = linkMode ? ForeColor : DisabledFore;
        SetEnabledChrome(_linkStyle, linkMode);
        _linkStyleLabel.Enabled = linkMode;
        _linkStyleLabel.ForeColor = linkMode ? ForeColor : DisabledFore;

        var vaultFolderOk = DailyNoteText.TryNormalizeVaultFolder(_vaultFolder.Text, out var vaultFolderNorm, out var vaultFolderReason);
        if (linkMode && !vaultFolderOk) { _vaultFolderNote.Text = vaultFolderReason; _vaultFolderNote.ForeColor = Warn; }
        else if (linkMode) { _vaultFolderNote.Text = "Copies into the vault here, then links. Default: " + DailyNoteText.DefaultVaultFolder; _vaultFolderNote.ForeColor = Text2; }
        else { _vaultFolderNote.Text = "Used when the copy folder is outside the vault (link mode)."; _vaultFolderNote.ForeColor = Text2; }

        var headingText = _heading.Text.TrimEnd();
        if (dailyOn && headingText.Length > 0 && !NoteSections.IsValidHeading(headingText))
        { _headingNote.Text = "Needs 1-6 #, a space, then text. Last valid value is kept."; _headingNote.ForeColor = Warn; }
        else
        {
            _headingNote.Text = headingText.Length == 0 ? "Optional. Blank adds clips at the end of the note." : "Clips go at the end of this section; added if missing.";
            _headingNote.ForeColor = Text2;
        }

        _dailyNote.ForeColor = Text2;
        if (!dailyAllowed) { _dailyNote.Text = "Needs the copy option above, with a valid folder."; return; }
        if (!_daily.Checked) { _dailyNote.Text = "Adds one timestamped line per screenshot to today's note."; return; }
        if (string.IsNullOrWhiteSpace(vault) || !Directory.Exists(vault))
        { _dailyNote.Text = string.IsNullOrWhiteSpace(vault) ? "Pick a vault." : "Vault folder not found."; _dailyNote.ForeColor = Warn; return; }
        var config = DailyNotesConfig.Load(vault);
        var rel = config.RelativeNotePath(DateTime.Now);
        if (ScreenshotRules.IsInside(_copyFolder.Text.Trim(), vault))
            _dailyNote.Text = "Today: " + rel + ". Embed mode: ![[...]] from the copy folder.";
        else if (!vaultFolderOk)
        { _dailyNote.Text = "Fix the vault folder to enable link mode."; _dailyNote.ForeColor = Warn; }
        else
        {
            var styleName = (_linkStyle.SelectedItem as string) ?? "Wikilink";
            _dailyNote.Text = "Today: " + rel + ". Link mode: also copies to " + vaultFolderNorm + " (" + styleName + ").";
        }
    }

    private void SetEnabledChrome(Control c, bool on)
    {
        c.Enabled = on;
        if (c is TextBox or ComboBox) { c.BackColor = on ? FieldBack : DisabledBack; c.ForeColor = on ? ForeColor : DisabledFore; }
        else if (c is CheckBox) c.ForeColor = on ? ForeColor : DisabledFore;
    }

    private string DailyNoteVaultPath => (_vault.SelectedItem as VaultItem)?.Path ?? "";

    internal static string DisplayVersion => typeof(SettingsForm).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "0.3.0";
    internal static Icon LoadProductIcon() => Icon.ExtractAssociatedIcon(Path.Combine(AppContext.BaseDirectory, "Sideclip.exe"))
        ?? throw new InvalidOperationException("Sideclip product icon is unavailable.");

    internal bool ApplyChanges()
    {
        var proposed = _snapshot.Copy();
        if (!HotkeyAssignments.TryCreate(_picker.Text, _shot.Text, _typeOut.Text, _enabled.Checked, out var assignments, out var error))
        { _error.Text = error; return false; }
        proposed.PickerHotkey = assignments.Picker;
        proposed.ScreenshotHotkey = assignments.Screenshot;
        proposed.TypeOutGoHotkey = assignments.TypeOut;
        proposed.TypeOutEnabled = assignments.Enabled;
        proposed.TypeOutDelayMs = (int)_delay.Value;
        proposed.ScreenshotCopyEnabled = _copy.Checked;
        proposed.ScreenshotCopyFolder = _copyFolder.Text.Trim();
        proposed.DailyNoteEnabled = _daily.Checked;
        proposed.DailyNoteVault = DailyNoteVaultPath;
        proposed.DailyNoteOcr = _ocr.Checked;
        proposed.DailyNoteHeading = _lastValidHeading;
        if (!DailyNoteText.TryNormalizeVaultFolder(_vaultFolder.Text, out var vf, out var vfReason))
        { _error.Text = vfReason; return false; }
        proposed.DailyNoteVaultFolder = vf;
        var styleSel = (_linkStyle.SelectedItem as string) ?? "Wikilink";
        proposed.DailyNoteLinkStyle = DailyNoteText.LinkStyleName(DailyNoteText.ParseLinkStyle(styleSel));
        error = _apply(proposed);
        _error.Text = error ?? "";
        if (error is not null) return false;
        _snapshot.CopyFrom(proposed);
        _status.Text = "Settings applied. Select a text source again before typing.";
        return true;
    }

    internal void SetStatus(string status) => _status.Text = status;

    private void BrowseCopyFolder()
    {
        using var dlg = new FolderBrowserDialog { Description = "Folder for screenshot copies", UseDescriptionForTitle = true, ShowNewFolderButton = true };
        if (Directory.Exists(_copyFolder.Text.Trim())) dlg.InitialDirectory = _copyFolder.Text.Trim();
        if (dlg.ShowDialog(this) == DialogResult.OK) _copyFolder.Text = dlg.SelectedPath;
    }

    private void BrowseVault()
    {
        using var dlg = new FolderBrowserDialog { Description = "Obsidian vault folder", UseDescriptionForTitle = true, ShowNewFolderButton = false };
        if (Directory.Exists(DailyNoteVaultPath)) dlg.InitialDirectory = DailyNoteVaultPath;
        if (dlg.ShowDialog(this) == DialogResult.OK) SelectVault(AddVault(dlg.SelectedPath));
    }

    private int AddVault(string path)
    {
        for (var i = 0; i < _vault.Items.Count; i++)
            if (_vault.Items[i] is VaultItem v && ScreenshotRules.SamePath(v.Path, path)) return i;
        return _vault.Items.Add(new VaultItem(path));
    }

    private void SelectVault(int index)
    {
        if (index >= 0 && index < _vault.Items.Count) _vault.SelectedIndex = index;
    }

    private TextBox AddBox(string label, string name, int y, int labelX, string? value, out Label labelControl)
    {
        labelControl = new Label { Name = name + "Label", Text = label, Location = new Point(labelX, y + 4), Size = new Size(330 - (labelX - 16), 24), TabStop = false };
        Controls.Add(labelControl);
        var box = new TextBox
        {
            Name = name,
            AccessibleName = label,
            Location = new Point(360, y),
            Width = 210,
            ReadOnly = true,
            Text = value ?? "",
            BackColor = FieldBack,
            ForeColor = ForeColor
        };
        box.KeyDown += (_, e) =>
        {
            if (e.KeyCode == Keys.Escape) { Close(); e.SuppressKeyPress = true; return; }
            e.SuppressKeyPress = true;
            if (e.KeyCode == Keys.Back) { box.Text = ""; return; }
            var winDown = (GetAsyncKeyState(0x5B) & 0x8000) != 0 || (GetAsyncKeyState(0x5C) & 0x8000) != 0;
            var parsed = HotkeyParser.FromKeyEvent(e, winDown);
            if (parsed.Length > 0) box.Text = parsed;
        };
        Controls.Add(box); return box;
    }

    private sealed record VaultItem(string Path)
    {
        public override string ToString() => Path;
    }
}

internal sealed class SettingsWindowHost : IDisposable
{
    private readonly Func<SettingsForm> _factory;
    private readonly Action _beforeOpen;
    private readonly Action<SettingsForm> _show;
    internal SettingsForm? Current { get; private set; }
    public SettingsWindowHost(Func<SettingsForm> factory, Action beforeOpen, Action<SettingsForm>? show = null)
    { _factory = factory; _beforeOpen = beforeOpen; _show = show ?? (form => form.Show()); }
    public void Open()
    {
        _beforeOpen();
        if (Current is { IsDisposed: false }) { Current.BringToFront(); Current.Activate(); return; }
        var form = _factory(); Current = form;
        form.Disposed += (_, _) => { if (ReferenceEquals(Current, form)) Current = null; };
        _show(form);
    }
    public void Dispose() { Current?.Dispose(); Current = null; }
}
