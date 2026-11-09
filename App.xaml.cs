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
using Sideclip.Obsidian;
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
    private SettingsWindowHost? _settingsWindows;
    private TaskCompletionSource<PickResult>? _pickWaiter;
    private readonly List<string> _pickPngPaths = new();
    private IssueNotifier? _issues;
    private ScreenshotCopier? _copier;
    private DailyNoteService? _dailyNotes;

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
            entry => _history!.IsTypeOutSource(entry) && entry.HasPayload && entry.TryGetText() is not null,
            ReportTypeOut,
            releaseModifiers: () =>
            {
                InputSender.ReleaseModifiers();
                Task.Delay(80).GetAwaiter().GetResult();
            },
            activateHotkeys: () =>
            {
                return _hotkeys?.TypeOutRegistered == true && _hotkeys.SetEscapeActive(true);
            },
            deactivateHotkeys: () => _hotkeys?.SetEscapeActive(false),
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
        _hotkeys.RegistrationChanged += NotifyPickerConfiguration;
        _hotkeys.Start();
        _hotkeys.Apply(_settings.PickerHotkey, _settings.ScreenshotHotkey, _settings.TypeOutGoHotkey, _settings.TypeOutEnabled);

        _tray = new TrayService(_settings, OnSettingsChanged);
        _tray.OpenPicker += OpenPicker;
        _tray.OpenSettings += OpenSettings;
        _tray.ClearHistory += ClearHistory;
        _tray.ExitApp += Shutdown;
        _tray.RefreshHotkeyLabel();
        if (!string.IsNullOrWhiteSpace(_hotkeys.LastError))
            _tray.Hint("Sideclip", _hotkeys.LastError.Trim());

        _ipc = new IpcServer(Dispatcher, OpenPicker, PickFromCli);
        _ipc.Start();

        _issues = new IssueNotifier(msg => Dispatcher.BeginInvoke(() => _tray?.Hint("Sideclip", msg)));
        ApplyScreenshotFeatures();

        // Test hook for UI checks: open Settings right away instead of via the tray menu.
        if (Environment.GetEnvironmentVariable("SIDECLIP_OPEN_SETTINGS") == "1")
            Dispatcher.BeginInvoke(() => OpenSettings(openInactive: true), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
#if DEBUG
        _debug = new MainWindow(_watcher, _history);
        ApplyDebugVisibility();
        _debug.NoteCapture(false, "Picker and type-out ready.");
#endif
    }

    private void OnSettingsChanged(AppSettings settings)
    {
        _typeOut?.Cancel(TypeOutSession.HotkeysChangedMessage);
        SettingsManager.Save(settings);
        _history?.SetTtl(TimeSpan.FromSeconds(settings.TtlSeconds), settings.TtlEnabled);
        _typeOut?.SetEnabled(settings.TypeOutEnabled);
        _hotkeys?.Apply(settings.PickerHotkey, settings.ScreenshotHotkey, settings.TypeOutGoHotkey, settings.TypeOutEnabled);
        _tray?.RefreshHotkeyLabel();
        NotifyPickerConfiguration();
        ApplyDebugVisibility();
        if (!_typeOut!.HasEntry && !string.IsNullOrWhiteSpace(_hotkeys?.LastError))
            _tray?.Hint("Sideclip", _hotkeys.LastError.Trim());
        ApplyScreenshotFeatures();
    }

    private void OpenSettings() => OpenSettings(openInactive: false);

    private void OpenSettings(bool openInactive)
    {
        _settingsWindows ??= new SettingsWindowHost(
            () => new SettingsForm(_settings, TrySaveSettings, ScreenshotFolder.Resolve(), ObsidianVaults.Load(), WindowsOcr.IsAvailable, openInactive),
            () => _typeOut?.Cancel(_typeOut.HasEntry ? TypeOutSession.HotkeysChangedMessage : null));
        if (openInactive && _settingsWindows.Current is { IsDisposed: false })
            _settingsWindows.Current.Close();
        _settingsWindows.Open();
        // A maximized topmost picker otherwise covers the native Settings form.
        // This is transient: demote Settings again when that picker closes.
        if (_settingsWindows.Current is { } settingsWindow)
            settingsWindow.TopMost = _picker?.IsVisible == true;
    }

    /// <summary>(Re)starts the screenshot copier and daily-note writer from the current settings.</summary>
    private void ApplyScreenshotFeatures()
    {
        try
        {
            _copier?.Dispose();
            _copier = null;
            _dailyNotes?.Dispose();
            _dailyNotes = null;
            if (_issues is null || !_settings.ScreenshotCopyEnabled)
                return;

            var source = ScreenshotFolder.Resolve();
            var dest = (_settings.ScreenshotCopyFolder ?? "").Trim();
            if (!ScreenshotRules.IsCopyFolderValid(dest, source, out var reason))
            {
                _issues.Report(ScreenshotCopier.KindDestination, "Screenshot copy is off until the folder is fixed: " + reason);
                return;
            }

            var vault = (_settings.DailyNoteVault ?? "").Trim();
            if (_settings.DailyNoteEnabled && vault.Length > 0)
            {
                DailyNoteService.Recognizer? ocr = _settings.DailyNoteOcr && WindowsOcr.IsAvailable ? WindowsOcr.RecognizeAsync : null;
                _dailyNotes = new DailyNoteService(vault, _issues, ocr, TimeSpan.FromSeconds(15), _settings.DailyNoteHeading);
            }

            var copier = new ScreenshotCopier(source, dest, _issues);
            var notes = _dailyNotes;
            if (notes is not null)
                copier.Copied += (_, copy) => notes.Enqueue(copy, DateTime.Now);
            _copier = copier;
            copier.Start();
        }
        catch (Exception ex)
        {
            _issues?.Report("screenshot-setup", "Screenshot copy could not start: " + ex.Message);
        }
    }


    private string? TrySaveSettings(AppSettings edited)
    {
        _typeOut?.Cancel(TypeOutSession.HotkeysChangedMessage);
        // Only the fields offered by this form change. Preserve current tray TTL/autostart.
        var next = _settings.Copy();
        next.PickerHotkey = edited.PickerHotkey;
        next.ScreenshotHotkey = edited.ScreenshotHotkey;
        next.TypeOutGoHotkey = edited.TypeOutGoHotkey;
        next.TypeOutEnabled = edited.TypeOutEnabled;
        next.TypeOutDelayMs = Math.Clamp(edited.TypeOutDelayMs, 0, 250);
        next.ScreenshotCopyEnabled = edited.ScreenshotCopyEnabled;
        next.ScreenshotCopyFolder = edited.ScreenshotCopyFolder ?? "";
        next.DailyNoteEnabled = edited.DailyNoteEnabled;
        next.DailyNoteVault = edited.DailyNoteVault ?? "";
        next.DailyNoteHeading = edited.DailyNoteHeading ?? "";
        next.DailyNoteOcr = edited.DailyNoteOcr;
        if (_hotkeys is null || !_hotkeys.TryApply(next.PickerHotkey, next.ScreenshotHotkey, next.TypeOutGoHotkey, next.TypeOutEnabled))
        { NotifyPickerConfiguration(); return _hotkeys?.LastError ?? "Shortcut registration is unavailable."; }
        try { SettingsManager.Save(next); }
        catch (Exception)
        {
            _hotkeys.TryApply(_settings.PickerHotkey, _settings.ScreenshotHotkey, _settings.TypeOutGoHotkey, _settings.TypeOutEnabled);
            NotifyPickerConfiguration();
            return "Settings could not be saved. Previous configuration kept. " + (_hotkeys.LastError ?? "");
        }
        _settings.CopyFrom(next);
        _typeOut?.SetEnabled(next.TypeOutEnabled);
        _tray?.RefreshHotkeyLabel();
        NotifyPickerConfiguration();
        ApplyScreenshotFeatures();
        return null;
    }

    private void NotifyPickerConfiguration()
    {
        // Agreed picker-owned API: empty = unassigned; null = unavailable.
        _picker?.UpdateTypeOutHelp(_settings.TypeOutEnabled, CurrentTypeOutShortcutDisplay);
    }

    private string? CurrentTypeOutShortcutDisplay =>
        TypeOutShortcutDisplay(_settings.TypeOutGoHotkey, _hotkeys?.TypeOutRegistered == true);

    internal static string? TypeOutShortcutDisplay(string? configured, bool registered) =>
        string.IsNullOrWhiteSpace(configured) ? "" : registered ? configured : null;

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

        _typeOut?.Cancel();
        var restore = IntPtr.Zero;
        _picker = new PickerWindow(_history.PickerEntries(), restore, CommitPick, _settings.TypeOutEnabled, CurrentTypeOutShortcutDisplay);
        _picker.Deleted += entry => _history.RemoveEntry(entry);
        _picker.CandidateSelected += entry => _typeOut?.SelectCandidate(entry);
        _picker.CandidateInvalidated += () => _typeOut?.Cancel();
        _picker.Activated += (_, _) => _typeOut?.RefreshCancellation(true);
        _picker.Deactivated += (_, _) => _typeOut?.RefreshCancellation(false);
        _picker.Cleared += ClearHistory;
        _picker.Cancelled += () => FinishPick(new PickResult { Kind = "cancel" });
        _picker.Closed += (_, _) =>
        {
            _picker = null;
            if (_settingsWindows?.Current is { } settingsWindow) settingsWindow.TopMost = false;
            _typeOut?.RefreshCancellation(false);
        };
        NotifyPickerConfiguration();
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
    }

    private void GoTypeOut()
    {
        if (_hotkeys?.TypeOutRegistered == true)
            _typeOut?.Go(Math.Clamp(_settings.TypeOutDelayMs, 0, 250));
    }
    private void CancelTypeOut() => _typeOut?.Cancel(TypeOutSession.IdleMessage);

    private void ReportTypeOut(string message)
    {
        _tray?.RefreshTypeOutStatus(message);
        _settingsWindows?.Current?.SetStatus(message);
    }

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
        _copier?.Dispose();
        _dailyNotes?.Dispose();
        _settingsWindows?.Dispose();
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
