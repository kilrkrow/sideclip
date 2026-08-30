using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Sideclip.Clipboard;
using Sideclip.History;

namespace Sideclip;

public partial class MainWindow : Window
{
    private readonly DispatcherTimer _uiTick;
    private readonly ClipHistoryStore _history;

    public MainWindow(ClipboardWatcher watcher, ClipHistoryStore history)
    {
        InitializeComponent();
        _ = watcher;
        _history = history;
        HistoryList.ItemsSource = _history.Rows;

        _uiTick = new DispatcherTimer(DispatcherPriority.Background, Dispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(500)
        };
        _uiTick.Tick += (_, _) => _history.TickViews();
        _uiTick.Start();
    }

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        Left = SystemParameters.WorkArea.Right - Width - 16;
        Top = SystemParameters.WorkArea.Top + 16;
        StatusText.Text =
            $"Listening. TTL {_history.Ttl.TotalSeconds:0}s. Ctrl+Win+Shift+V picker. Ctrl+Win+Shift+P screenshot path. No Win+V.";
    }

    public void NoteCapture(bool isSecret, string owner)
    {
        StatusText.Text = isSecret
            ? $"Captured SECRET from {owner} (redacted). Expires in {_history.Ttl.TotalSeconds:0}s."
            : $"Captured non-secret from {owner}.";
    }

    public void NoteExpire(bool cleared)
    {
        StatusText.Text = cleared
            ? "Secret TTL: wiped that row and cleared the live clipboard."
            : "Secret TTL: wiped that row only. Live clipboard left alone.";
    }

    public bool AllowClose { get; set; }

    private void OnClosing(object sender, CancelEventArgs e)
    {
        if (AllowClose)
            return;
        e.Cancel = true;
        Hide();
    }

    protected override void OnClosed(EventArgs e)
    {
        _uiTick.Stop();
        base.OnClosed(e);
    }
}


