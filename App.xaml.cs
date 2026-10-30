using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Sideclip.Cli;
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
    private Mutex? _single;
    private IpcServer? _ipc;
    private TaskCompletionSource<PickResult>? _pickWaiter;
    private readonly List<string> _pickPngPaths = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        try
        {
            if (CommandLine.TryRunAndExit(e.Args))
            {
                Environment.Exit(Environment.ExitCode);
                return;
            }
        }
        catch (Exception ex)
        {
            try
            {
                ConsoleAttach.Ensure();
                Console.Error.WriteLine(ex.Message);
            }
            catch
            {
            }
            Shutdown();
            return;
        }

        _single = new Mutex(true, IpcServer.MutexName, out var created);
        if (!created)
        {
            ConsoleAttach.Ensure();
            var ping = IpcServer.TrySend("picker", 8_000);
            if (ping is null || !ping.Ok)
                System.Windows.MessageBox.Show("Sideclip is already running.", "Sideclip");
            Shutdown();
            return;
        }

        base.OnStartup(e);

        _settings = SettingsManager.Load();
        SettingsManager.ApplyAutostart(_settings.Autostart);

        _watcher = new ClipboardWatcher();
        _history = new ClipHistoryStore(Dispatcher, OnSecretExpired, TimeSpan.FromSeconds(_settings.TtlSeconds), _settings.TtlEnabled);

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

        _ipc = new IpcServer(Dispatcher, OpenPicker, PickFromCli);
        _ipc.Start();

#if DEBUG
        _debug = new MainWindow(_watcher, _history);
        ApplyDebugVisibility();
        _debug.NoteCapture(false, "picker " + _settings.PickerHotkey + " / path " + _settings.ScreenshotHotkey);
#endif
    }

    private void OnSettingsChanged(AppSettings settings)
    {
        SettingsManager.Save(settings);
        _history?.SetTtl(TimeSpan.FromSeconds(settings.TtlSeconds), settings.TtlEnabled);
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
        _picker.Cancelled += () => FinishPick(new PickResult { Kind = "cancel" });
        _picker.Closed += (_, _) => _picker = null;
        _picker.Show();
        _picker.Activate();
    }

    private Task<PickResult> PickFromCli()
    {
        var tcs = new TaskCompletionSource<PickResult>(TaskCreationOptions.RunContinuationsAsynchronously);
        Dispatcher.BeginInvoke(() =>
        {
            _pickWaiter = tcs;
            OpenPicker();
            if (_picker is null && !tcs.Task.IsCompleted)
                tcs.TrySetResult(new PickResult { Kind = "cancel" });
        });
        return tcs.Task;
    }

    private void FinishPick(PickResult result)
    {
        var waiter = _pickWaiter;
        _pickWaiter = null;
        waiter?.TrySetResult(result);
    }

    private void CommitPick(ClipEntry entry, bool paste)
    {
        if (_pickWaiter is not null)
        {
            FinishPick(ResultFromEntry(entry));
            return;
        }

        if (_watcher is null)
            return;

        if (entry.IsImage && entry.ImagePng is not null)
            _watcher.SetImageSuppressing(entry.ImagePng);
        else
        {
            var text = entry.TryGetText();
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

    private PickResult ResultFromEntry(ClipEntry entry)
    {
        if (entry.IsImage && entry.ImagePng is { Length: > 0 })
        {
            var path = Path.Combine(
                Path.GetTempPath(),
                "sideclip-pick-" + Guid.NewGuid().ToString("N") + ".png");
            File.WriteAllBytes(path, entry.ImagePng);
            _pickPngPaths.Add(path);
            return new PickResult { Kind = "image", Path = path };
        }

        return new PickResult { Kind = "text", Text = entry.TryGetText() ?? "" };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _picker?.Close();
        _ipc?.Dispose();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _history?.Dispose();
        _watcher?.Dispose();
        if (_debug is not null) { _debug.AllowClose = true; _debug.Close(); }
        try { _single?.ReleaseMutex(); } catch { }
        _single?.Dispose();

        foreach (var p in _pickPngPaths)
        {
            try { File.Delete(p); } catch { }
        }

        base.OnExit(e);
    }
}



