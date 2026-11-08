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
using Sideclip.TypeOut;

namespace Sideclip;

public partial class App : System.Windows.Application
{
    private AppSettings _settings = new();
    private ClipboardWatcher? _watcher;
    private ClipHistoryStore? _history;
    private HotkeyListener? _hotkeys;
    private TrayService? _tray;
#pragma warning disable CS0649,CS0169 // This optional debug window is only assigned in debug builds.
    private MainWindow? _debug;
#pragma warning restore CS0649
    private PickerWindow? _picker;
    private Mutex? _single;
    private IpcServer? _ipc;
    private TypeOutSession? _typeOut;
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
            try { ConsoleAttach.Ensure(); Console.Error.WriteLine(ex.Message); } catch { }
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

        _typeOut = new TypeOutSession(
            entry => _history!.IsPickerSource(entry) && entry.HasPayload && entry.TryGetText() is not null,
            ReportTypeOut,
            releaseModifiers: () =>
            {
                InputSender.ReleaseModifiers();
                Task.Delay(80).GetAwaiter().GetResult();
            },
            activateHotkeys: () =>
            {
                _hotkeys?.SetTypeOutActive(true, _settings.TypeOutGoHotkey);
                return _hotkeys?.TypeOutRegistered == true && _hotkeys.EscapeRegistered;
            },
            deactivateHotkeys: () => _hotkeys?.SetTypeOutActive(false, _settings.TypeOutGoHotkey),
            postToUi: action => Dispatcher.BeginInvoke(action));
        _typeOut.SetEnabled(_settings.TypeOutEnabled);
        _history.EntryInvalidatedWithReason += (entry, reason) => _typeOut.EntryChanged(entry, reason);

        _watcher.ClipCaptured += (text, secret, owner) =>
        {
            _history.Add(text, secret, owner);
#if DEBUG
            _debug?.NoteCapture(secret, owner);
#endif
        };
        _watcher.ImageCaptured += (png, owner) =>
        {
            _history.Add(string.Empty, isSecret: false, owner, png);
#if DEBUG
            _debug?.NoteCapture(false, owner);
#endif
        };
        _watcher.Start();

        _hotkeys = new HotkeyListener();
        _hotkeys.PickerRequested += () => Dispatcher.BeginInvoke(OpenPicker);
        _hotkeys.ScreenshotPathRequested += () => Dispatcher.BeginInvoke(TypeScreenshotPath);
        _hotkeys.TypeOutGoRequested += () => Dispatcher.BeginInvoke(GoTypeOut);
        _hotkeys.TypeOutCancelRequested += () => Dispatcher.BeginInvoke(CancelTypeOut);
        _hotkeys.Start();
        _hotkeys.Apply(_settings.PickerHotkey, _settings.ScreenshotHotkey, _settings.TypeOutGoHotkey);

        _tray = new TrayService(_settings, OnSettingsChanged);
        _tray.OpenPicker += OpenPicker;
        _tray.EditHotkeys += OpenHotkeySettings;
        _tray.ClearHistory += ClearHistory;
        _tray.TypeOutGoRequested += GoTypeOut;
        _tray.TypeOutCancelRequested += CancelTypeOut;
        _tray.ExitApp += Shutdown;
        _tray.RefreshHotkeyLabel();
        _tray.RefreshTypeOutLabels();
        if (!string.IsNullOrWhiteSpace(_hotkeys.LastError))
            _tray.Hint("Sideclip", _hotkeys.LastError.Trim());

        _ipc = new IpcServer(Dispatcher, OpenPicker, PickFromCli);
        _ipc.Start();
#if DEBUG
        _debug = new MainWindow(_watcher, _history);
        ApplyDebugVisibility();
        _debug.NoteCapture(false, "Picker and type-out ready.");
#endif
    }

    private void OnSettingsChanged(AppSettings settings)
    {
        var oldGo = _settings.TypeOutGoHotkey;
        SettingsManager.Save(settings);
        _history?.SetTtl(TimeSpan.FromSeconds(settings.TtlSeconds), settings.TtlEnabled);
        if (settings.TypeOutEnabled && _typeOut?.HasEntry == true && settings.TypeOutGoHotkey != oldGo)
            _typeOut?.Cancel("Type-out cancelled because the Go hotkey changed.");
        _typeOut?.SetEnabled(settings.TypeOutEnabled);
        // Apply does not re-register the scoped Escape chord or its active Go.
        _hotkeys?.Apply(settings.PickerHotkey, settings.ScreenshotHotkey, settings.TypeOutGoHotkey, _typeOut?.HasEntry == true);
        _tray?.RefreshHotkeyLabel();
        _tray?.RefreshTypeOutLabels();
        ApplyDebugVisibility();
        if (!_typeOut!.HasEntry && !string.IsNullOrWhiteSpace(_hotkeys?.LastError))
            _tray?.Hint("Sideclip", _hotkeys.LastError.Trim());
    }

    private void OpenHotkeySettings()
    {
        var form = new HotkeySettingsForm(_settings);
        if (form.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
        _typeOut?.Cancel();
        _settings.PickerHotkey = form.PickerHotkey;
        _settings.ScreenshotHotkey = form.ScreenshotHotkey;
        _settings.TypeOutGoHotkey = form.TypeOutGoHotkey;
        OnSettingsChanged(_settings);
    }

    private void ApplyDebugVisibility()
    {
#if DEBUG
        if (_debug is null) return;
        if (_settings.ShowDebugWindow) _debug.Show(); else _debug.Hide();
#endif
    }

    private void OnSecretExpired(ClipEntry entry)
    {
        var cleared = false;
        if (_history is not null && _watcher is not null && ReferenceEquals(_history.Latest, entry))
            cleared = _watcher.ClearLiveIfMatches(entry);
#if DEBUG
        _debug?.NoteExpire(cleared);
#endif
    }

    private void ClearHistory()
    {
        _typeOut?.Cancel();
        _history?.ClearAll();
        if (_picker is not null)
        {
            _picker.Close();
            _picker = null;
        }
    }

    private void OpenPicker()
    {
        if (_history is null || _watcher is null) return;
        if (_picker is not null) { _picker.Activate(); return; }

        var restore = NativeMethods.GetForegroundWindow();
        _picker = new PickerWindow(_history.PickerEntries(), restore, CommitPick, _settings.TypeOutEnabled);
        _picker.Deleted += entry => _history.RemoveEntry(entry);
        _picker.TypeOutRequested += entry => _typeOut?.Arm(entry);
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
            if (_picker is null && !tcs.Task.IsCompleted) tcs.TrySetResult(new PickResult { Kind = "cancel" });
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
            var result = ResultFromEntry(entry);
            if (entry.IsSecret) _history?.RemoveEntry(entry);
            FinishPick(result);
            return;
        }
        if (_watcher is null) return;
        if (entry.IsImage && entry.ImagePng is not null)
            _watcher.SetImageSuppressing(entry.ImagePng);
        else
        {
            var text = entry.TryGetText();
            if (string.IsNullOrEmpty(text)) return;
            _watcher.SetTextSuppressing(text, entry.IsSecret);
        }
        if (!paste) return;
        Dispatcher.BeginInvoke(new Action(() =>
        {
            Thread.Sleep(40);
            InputSender.CtrlV();
        }), System.Windows.Threading.DispatcherPriority.Background);
    }

    private void GoTypeOut() => _typeOut?.Go(Math.Clamp(_settings.TypeOutDelayMs, 0, 250));
    private void CancelTypeOut() => _typeOut?.Cancel("Type-out idle. Arm an entry to start.");
    private void ReportTypeOut(string message) => _tray?.RefreshTypeOutStatus(message);

    private void TypeScreenshotPath()
    {
        if (!ScreenshotPathTyper.TryTypeNewest(out var reason, out var path))
        {
            _tray?.Hint("Sideclip", reason ?? "No screenshot.");
            return;
        }
#if DEBUG
        _debug?.NoteCapture(false, "Screenshot path typed.");
#endif
    }

    private PickResult ResultFromEntry(ClipEntry entry)
    {
        if (entry.IsImage && entry.ImagePng is { Length: > 0 })
        {
            var path = Path.Combine(Path.GetTempPath(), "sideclip-pick-" + Guid.NewGuid().ToString("N") + ".png");
            File.WriteAllBytes(path, entry.ImagePng);
            _pickPngPaths.Add(path);
            return new PickResult { Kind = "image", Path = path };
        }
        return new PickResult { Kind = "text", Text = entry.TryGetText() ?? "" };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _typeOut?.Dispose();
        _picker?.Close();
        _ipc?.Dispose();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _history?.Dispose();
        _watcher?.Dispose();
        try { _single?.ReleaseMutex(); } catch { }
        _single?.Dispose();
        foreach (var p in _pickPngPaths) { try { File.Delete(p); } catch { } }
        base.OnExit(e);
    }
}