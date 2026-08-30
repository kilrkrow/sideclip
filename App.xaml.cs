using System.Windows;
using Sideclip.Clipboard;
using Sideclip.History;
using Sideclip.Hotkeys;
using Sideclip.Native;
using Sideclip.Picker;
using Sideclip.Screenshot;
using Sideclip.Settings;
using Sideclip.Tray;

namespace Sideclip;

public partial class App : System.Windows.Application
{
    private AppSettings _settings = new();
    private ClipboardWatcher? _watcher;
    private ClipHistoryStore? _history;
    private HotkeyListener? _hotkeys;
    private TrayService? _tray;
    private MainWindow? _debug;
    private PickerWindow? _picker;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _settings = SettingsManager.Load();
        SettingsManager.ApplyAutostart(_settings.Autostart);

        _watcher = new ClipboardWatcher();
        _history = new ClipHistoryStore(Dispatcher, OnSecretExpired, TimeSpan.FromSeconds(_settings.TtlSeconds));

        _watcher.ClipCaptured += OnClipCaptured;
        _watcher.ImageCaptured += OnImageCaptured;
        _watcher.Start();

        _hotkeys = new HotkeyListener();
        _hotkeys.PickerRequested += () => Dispatcher.Invoke(OpenPicker);
        _hotkeys.ScreenshotPathRequested += () => Dispatcher.Invoke(TypeScreenshotPath);
        _hotkeys.Start();
        _hotkeys.Apply(_settings.PickerHotkey, _settings.ScreenshotHotkey);

        _tray = new TrayService(_settings, OnSettingsChanged);
        _tray.OpenPicker += OpenPicker;
        _tray.ToggleDebug += ApplyDebugVisibility;
        _tray.EditHotkeys += OpenHotkeySettings;
        _tray.ClearHistory += ClearHistory;
        _tray.ExitApp += Shutdown;
        _tray.RefreshHotkeyLabel();
        if (!string.IsNullOrWhiteSpace(_hotkeys.LastError))
            _tray.Hint("Sideclip", _hotkeys.LastError.Trim());

#if DEBUG
        _debug = new MainWindow(_watcher, _history);
        ApplyDebugVisibility();
        _debug.NoteCapture(false, "picker " + _settings.PickerHotkey + " / path " + _settings.ScreenshotHotkey);
#endif
    }

    private void OnSettingsChanged(AppSettings settings)
    {
        SettingsManager.Save(settings);
        _history?.SetTtl(TimeSpan.FromSeconds(settings.TtlSeconds));
        _hotkeys?.Apply(settings.PickerHotkey, settings.ScreenshotHotkey);
        _tray?.RefreshHotkeyLabel();
        ApplyDebugVisibility();
        if (!string.IsNullOrWhiteSpace(_hotkeys?.LastError))
            _tray?.Hint("Sideclip", _hotkeys.LastError.Trim());
    }

    private void OpenHotkeySettings()
    {
        var form = new HotkeySettingsForm(_settings);
        if (form.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;

        _settings.PickerHotkey = form.PickerHotkey;
        _settings.ScreenshotHotkey = form.ScreenshotHotkey;
        OnSettingsChanged(_settings);
        _debug?.NoteCapture(false, "picker " + _settings.PickerHotkey + " / path " + _settings.ScreenshotHotkey);
    }

    private void ApplyDebugVisibility()
    {
#if DEBUG
        if (_debug is null)
            return;
        if (_settings.ShowDebugWindow)
            _debug.Show();
        else
            _debug.Hide();
#endif
    }

    private void OnClipCaptured(string text, bool isSecret, string owner)
    {
        _history?.Add(text, isSecret, owner);
        _debug?.NoteCapture(isSecret, owner);
    }

    private void OnImageCaptured(byte[] png, string owner)
    {
        _history?.Add(string.Empty, isSecret: false, owner, png);
        _debug?.NoteCapture(false, owner);
    }

    private void OnSecretExpired(ClipEntry entry)
    {
        var cleared = false;
        if (_history is not null && _watcher is not null && ReferenceEquals(_history.Latest, entry))
            cleared = _watcher.ClearLiveIfMatches(entry);
        _debug?.NoteExpire(cleared);
    }

    private void ClearHistory()
    {
        _history?.ClearAll();
        _debug?.NoteCapture(false, "history cleared");
        if (_picker is not null)
        {
            _picker.Close();
            _picker = null;
        }
    }

    private void OpenPicker()
    {
        if (_history is null || _watcher is null)
            return;

        if (_picker is not null)
        {
            _picker.Activate();
            return;
        }

        var restore = NativeMethods.GetForegroundWindow();
        _picker = new PickerWindow(_history.PickerEntries(), restore, CommitPick);
        _picker.Deleted += entry => _history.RemoveEntry(entry);
        _picker.Cleared += ClearHistory;
        _picker.Closed += (_, _) => _picker = null;
        _picker.Show();
        _picker.Activate();
    }

    private void CommitPick(ClipEntry entry, bool paste)
    {
        if (_watcher is null)
            return;

        if (entry.IsImage && entry.ImagePng is not null)
            _watcher.SetImageSuppressing(entry.ImagePng);
        else
        {
            var text = entry.TryGetNonSecretText();
            if (string.IsNullOrEmpty(text))
                return;
            _watcher.SetTextSuppressing(text);
        }

        if (!paste)
            return;

        Dispatcher.BeginInvoke(new Action(() =>
        {
            Thread.Sleep(40);
            InputSender.CtrlV();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void TypeScreenshotPath()
    {
        if (!ScreenshotPathTyper.TryTypeNewest(out var reason, out var path))
        {
            _tray?.Hint("Sideclip", reason ?? "No screenshot.");
            _debug?.NoteCapture(false, reason ?? "screenshot typer failed");
            return;
        }

        _debug?.NoteCapture(false, "typed " + path);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _picker?.Close();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _history?.Dispose();
        _watcher?.Dispose();
        if (_debug is not null) { _debug.AllowClose = true; _debug.Close(); }
        base.OnExit(e);
    }
}



