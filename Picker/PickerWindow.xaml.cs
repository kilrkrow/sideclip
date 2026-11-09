using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Sideclip.History;
using Sideclip.Native;
using Sideclip.TypeOut;

namespace Sideclip.Picker;

internal enum PickerCommitAction { None, Copy }

internal static class PickerKeyMapping
{
    public static PickerCommitAction Resolve(Key key, ModifierKeys modifiers, ClipEntry? entry, bool typeOutEnabled) =>
        key == Key.Enter ? PickerCommitAction.Copy : PickerCommitAction.None;
}
public partial class PickerWindow : Window
{
    private readonly List<ClipEntry> _all = new();
    private readonly List<ClipEntry> _shown = new();
    private string _filter = string.Empty;
    private readonly Action<ClipEntry, bool> _commit;
    public event Action<ClipEntry>? CandidateSelected;
    public event Action? CandidateInvalidated;
    private ClipEntry? _deliberate;
    private bool _didCommit;
    private bool _typeOutEnabled;
    private string? _typeOutShortcutDisplay;

    public PickerWindow(IEnumerable<ClipEntry> entries, IntPtr restoreHwnd, Action<ClipEntry, bool> commit, bool typeOutEnabled = false, string? typeOutShortcut = null)
    {
        InitializeComponent();
        _commit = commit;
        _typeOutEnabled = typeOutEnabled;
        _typeOutShortcutDisplay = typeOutShortcut;
        ApplyHelpTexts();
        foreach (var e in entries)
        {
            if (e.HasPayload)
                _all.Add(e);
        }

        ApplyFilter();
        Loaded += (_, _) => { Activate(); List.Focus(); RefreshTypeOut(Selected()); };
    }

    /// <summary>
    /// Refresh bottom help after Settings changes without recreating the picker.
    /// When enabled: non-empty = configured chord; empty = unassigned; null = unavailable.
    /// </summary>
    public void UpdateTypeOutHelp(bool typeOutEnabled, string? typeOutShortcutDisplay)
    {
        _typeOutEnabled = typeOutEnabled;
        _typeOutShortcutDisplay = typeOutShortcutDisplay;
        ApplyHelpTexts();
        RefreshTypeOut(Selected());
    }

    private void ApplyHelpTexts()
    {
        if (NavHelp is not null)
            NavHelp.Text = PickerNoArmHelp.NavigationLine;
        if (CopyHelp is not null)
            CopyHelp.Text = PickerNoArmHelp.CopyLine;
        if (TypeOutHelp is not null)
            TypeOutHelp.Text = PickerNoArmHelp.FormatTypeOutLine(_typeOutEnabled, _typeOutShortcutDisplay);
    }

    private void ApplyFilter()
    {
        _shown.Clear();
        List.Items.Clear();

        var q = _filter.Trim();
        Header.Text = string.IsNullOrEmpty(_filter) ? "Search clipboard..." : _filter;
        Header.Foreground = string.IsNullOrEmpty(_filter)
            ? System.Windows.Media.ColorConverter.ConvertFromString("#ABAFBC") is System.Windows.Media.Color c
                ? new System.Windows.Media.SolidColorBrush(c)
                : System.Windows.Media.Brushes.Gray
            : new System.Windows.Media.SolidColorBrush(System.Windows.Media.Color.FromRgb(0xE5, 0xE9, 0xF6));

        foreach (var e in _all)
        {
            if (!Matches(e, q))
                continue;
            _shown.Add(e);
            List.Items.Add(BuildRow(e));
        }

        EmptyHint.Visibility = _shown.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        EmptyHint.Text = _shown.Count == 0
            ? (string.IsNullOrEmpty(q) ? "Clipboard is empty" : ("No matches for " + (char)34 + q + (char)34))
            : EmptyHint.Text;

        if (_deliberate is not null && !_shown.Contains(_deliberate))
        { _deliberate = null; CandidateInvalidated?.Invoke(); }
        if (_shown.Count > 0)
        {
            // Programmatic highlight only. Never primes a candidate.
            var keep = _deliberate is not null ? _shown.IndexOf(_deliberate) : -1;
            List.SelectedIndex = keep >= 0 ? keep : 0;
        }
        else
            ShowPreview(null);
    }

    private static bool Matches(ClipEntry e, string q)
    {
        if (string.IsNullOrEmpty(q))
            return true;
        if (e.IsImage)
        {
            var cap = $"image screenshot {e.CopiedAt.ToLocalTime():dddd HH:mm}";
            return cap.Contains(q, StringComparison.OrdinalIgnoreCase);
        }

        var text = e.TryGetText() ?? string.Empty;
        return text.Contains(q, StringComparison.OrdinalIgnoreCase);
    }

    private void ChooseDeliberately(ClipEntry? entry)
    {
        if (entry is null) return;
        _deliberate = entry;
        CandidateSelected?.Invoke(entry);
    }

    private void OnRowChoose(object sender, MouseButtonEventArgs e)
    {
        if (sender is ListBoxItem row && row.Content is FrameworkElement { Tag: ClipEntry entry })
        {
            // The preview event identifies the clicked row before WPF's normal
            // selection update. Keep copy and candidate identity synchronized.
            var index = _shown.IndexOf(entry);
            if (index < 0) return;
            List.SelectedIndex = index;
            ChooseDeliberately(entry);
        }
    }

    private void OnCopyClick(object sender, RoutedEventArgs e) => Commit(paste: false);
    private void RefreshTypeOut(ClipEntry? entry)
    {
        var enabled = entry is not null && _typeOutEnabled && TypeOutSession.CanType(entry, entry.HasPayload);
        TypeOutReason.Text = !_typeOutEnabled ? "Type-out is off." : entry is null
            ? (_typeOutEnabled ? "Select an eligible text entry." : "Type-out is off.")
            : enabled ? (TypeOutText.HasTargetKeys(entry.TryGetText()) ? "Eligible. " + TypeOutText.TargetKeyWarning : "Eligible and will be rechecked before every chunk.") : entry.IsImage ? "Images and file entries cannot be typed." : TypeOutText.Validate(entry.TryGetText()) ?? "This entry cannot be typed.";
        // A fixed AutomationProperties.Name masks all inline text from UIA.
        // Expose the actual current help and warnings, not just a generic role.
        System.Windows.Automation.AutomationProperties.SetName(FooterHelp,
            new System.Windows.Documents.TextRange(FooterHelp.ContentStart, FooterHelp.ContentEnd).Text);
    }

    private static FrameworkElement BuildRow(ClipEntry e)
    {
        var grid = new Grid();
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        if (e.IsImage)
        {
            var img = new System.Windows.Controls.Image
            {
                Width = 34,
                Height = 34,
                Stretch = System.Windows.Media.Stretch.Uniform,
                Margin = new Thickness(0, 0, 10, 0),
                Source = e.TryDecodeImage()
            };
            Grid.SetColumn(img, 0);
            grid.Children.Add(img);
        }

        var label = new TextBlock
        {
            Text = e.IsImage
                ? $"Screenshot from {e.CopiedAt.ToLocalTime():dddd HH:mm}"
                : ClipEntry.MakeListLine(e.TryGetText() ?? string.Empty),
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);
        grid.Tag = e;
        return grid;
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        ShowPreview(Selected());
        RefreshTypeOut(Selected());
    }

    private void ShowPreview(ClipEntry? e)
    {
        if (e is null)
        {
            PreviewText.Text = string.Empty;
            PreviewImage.Visibility = Visibility.Collapsed;
            PreviewText.Visibility = Visibility.Collapsed;
            return;
        }

        if (e.IsImage)
        {
            PreviewImage.Source = e.TryDecodeImage();
            PreviewImage.Visibility = Visibility.Visible;
            PreviewText.Visibility = Visibility.Collapsed;
        }
        else
        {
            var t = e.TryGetText() ?? string.Empty;
            if (t.Length > 8192)
                t = t[..8192];
            PreviewText.Text = t;
            PreviewText.Visibility = Visibility.Visible;
            PreviewImage.Visibility = Visibility.Collapsed;
        }
    }

    private ClipEntry? Selected()
    {
        var i = List.SelectedIndex;
        return i >= 0 && i < _shown.Count ? _shown[i] : null;
    }

    internal void EscapePicker()
    {
        if (_filter.Length > 0) { _filter = string.Empty; ApplyFilter(); }
        else { CandidateInvalidated?.Invoke(); Close(); }
    }

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            EscapePicker();

            e.Handled = true;
            return;
        }

        if (e.Key == Key.Back)
        {
            if (_filter.Length > 0)
            {
                _filter = _filter[..^1];
                ApplyFilter();
            }

            e.Handled = true;
            return;
        }

        if (e.Key == Key.Down)
        {
            MoveSel(1);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Up)
        {
            MoveSel(-1);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.PageDown)
        {
            MoveSel(6);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.PageUp)
        {
            MoveSel(-6);
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Home)
        {
            if (_shown.Count > 0) { List.SelectedIndex = 0; ChooseDeliberately(Selected()); }
            e.Handled = true;
            return;
        }

        if (e.Key == Key.End)
        {
            if (_shown.Count > 0) { List.SelectedIndex = _shown.Count - 1; ChooseDeliberately(Selected()); }
            e.Handled = true;
            return;
        }

        switch (PickerKeyMapping.Resolve(e.Key, Keyboard.Modifiers, Selected(), _typeOutEnabled))
        {
            case PickerCommitAction.Copy:
                Commit(paste: false);
                e.Handled = true;
                return;
        }

        if (e.Key == Key.Delete && (Keyboard.Modifiers & ModifierKeys.Shift) != 0)
        {
            ClearAll();
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Delete)
        {
            var sel = Selected();
            if (sel is not null)
            {
                _all.Remove(sel);
                Deleted?.Invoke(sel);
                ApplyFilter();
            }

            e.Handled = true;
        }
    }

    public event Action<ClipEntry>? Deleted;
    public event Action? Cleared;
    public event Action? Cancelled;


    private void OnClearClick(object sender, RoutedEventArgs e) => ClearAll();

    private void ClearAll()
    {
        _all.Clear();
        _filter = string.Empty;
        Cleared?.Invoke();
        ApplyFilter();
    }

    private void OnPreviewTextInput(object sender, TextCompositionEventArgs e)
    {
        if (string.IsNullOrEmpty(e.Text) || e.Text == "\r" || e.Text == "\n")
            return;
        _filter += e.Text;
        ApplyFilter();
        e.Handled = true;
    }

    private void MoveSel(int delta)
    {
        if (_shown.Count == 0)
            return;
        var n = _shown.Count;
        var i = List.SelectedIndex;
        if (i < 0) i = 0;
        i = (i + delta) % n;
        if (i < 0) i += n;
        List.SelectedIndex = i;
        List.ScrollIntoView(List.SelectedItem);
        ChooseDeliberately(Selected());
    }

    private void OnRowActivate(object sender, MouseButtonEventArgs e)
    {
        Commit(paste: false);
        e.Handled = true;
    }

    private void OnPreviewActivate(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2)
            return;
        Commit(paste: false);
        e.Handled = true;
    }

    private void OnScrimClick(object sender, MouseButtonEventArgs e)
    {
        if (!Card.IsMouseOver)
            Close();
    }

    private void OnCardClick(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void Commit(bool paste)
    {
        if (_didCommit) return;
        var sel = Selected();
        if (sel is null)
        {
            Close();
            return;
        }

        ChooseDeliberately(sel);
        _didCommit = true;
        Close();
        _commit(sel, false);
    }

    protected override void OnClosed(EventArgs e)
    {
        // Complete a CLI pick on any uncommitted close (scrim/window close/Esc).
        // This notification does not invalidate a type-out candidate: only an
        // explicit cancel or source/settings invalidation owns that policy.
        if (!_didCommit)
            Cancelled?.Invoke();
        base.OnClosed(e);
    }
}
