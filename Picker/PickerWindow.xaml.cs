using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using Sideclip.History;
using Sideclip.Native;

namespace Sideclip.Picker;

public partial class PickerWindow : Window
{
    private readonly List<ClipEntry> _all = new();
    private readonly List<ClipEntry> _shown = new();
    private string _filter = string.Empty;
    private IntPtr _restore;
    private readonly Action<ClipEntry, bool> _commit;

    public PickerWindow(IEnumerable<ClipEntry> entries, IntPtr restoreHwnd, Action<ClipEntry, bool> commit)
    {
        InitializeComponent();
        _restore = restoreHwnd;
        _commit = commit;
        foreach (var e in entries)
        {
            if (!e.IsSecret && e.HasPayload)
                _all.Add(e);
        }

        ApplyFilter();
        Loaded += (_, _) => Activate();
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

        if (_shown.Count > 0)
            List.SelectedIndex = 0;
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

        var text = e.TryGetNonSecretText() ?? string.Empty;
        return text.Contains(q, StringComparison.OrdinalIgnoreCase);
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
                : ClipEntry.MakeListLine(e.TryGetNonSecretText() ?? string.Empty),
            TextTrimming = TextTrimming.CharacterEllipsis,
            FontSize = 14,
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(label, 1);
        grid.Children.Add(label);
        grid.Tag = e;
        return grid;
    }

    private void OnSelectionChanged(object sender, SelectionChangedEventArgs e) =>
        ShowPreview(Selected());

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
            var t = e.TryGetNonSecretText() ?? string.Empty;
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

    private void OnPreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            if (_filter.Length > 0)
            {
                _filter = string.Empty;
                ApplyFilter();
            }
            else
            {
                Close();
            }

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
            if (_shown.Count > 0) List.SelectedIndex = 0;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.End)
        {
            if (_shown.Count > 0) List.SelectedIndex = _shown.Count - 1;
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Enter)
        {
            var paste = (Keyboard.Modifiers & ModifierKeys.Shift) == 0;
            Commit(paste);
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
    }

    private void OnRowActivate(object sender, MouseButtonEventArgs e) => Commit(paste: true);

    private void OnScrimClick(object sender, MouseButtonEventArgs e)
    {
        if (!Card.IsMouseOver)
            Close();
    }

    private void OnCardClick(object sender, MouseButtonEventArgs e) => e.Handled = true;

    private void Commit(bool paste)
    {
        var sel = Selected();
        if (sel is null)
        {
            Close();
            return;
        }

        Close();
        _commit(sel, paste);
    }

    protected override void OnClosed(EventArgs e)
    {
        if (_restore != IntPtr.Zero)
            InputSender.FocusWindow(_restore);
        base.OnClosed(e);
    }
}



