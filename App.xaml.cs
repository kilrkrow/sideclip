using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Sideclip.Cli;
using Sideclip.Clipboard;
using Sideclip.History;
using Sideclip.Hotkeys;
using Sideclip.Native;
using Sideclip.Obsidian;
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

        _issues = new IssueNotifier(msg => Dispatcher.BeginInvoke(() => _tray?.Hint("Sideclip", msg)));
        ApplyScreenshotFeatures();

        // Test hook for UI checks: open Settings right away instead of via the tray menu.
        if (Environment.GetEnvironmentVariable("SIDECLIP_OPEN_SETTINGS") == "1")
            Dispatcher.BeginInvoke(() => OpenHotkeySettings(openInactive: true), System.Windows.Threading.DispatcherPriority.ApplicationIdle);

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

            // Setting 2 only runs while Setting 1 is on and valid; its saved values stay either way.
            var vault = (_settings.DailyNoteVault ?? "").Trim();
            if (_settings.DailyNoteEnabled && vault.Length > 0)
            {
                // OCR is optional and runs on the note queue with a timeout; the copy never waits for it.
                DailyNoteService.Recognizer? ocr = _settings.DailyNoteOcr && WindowsOcr.IsAvailable ? WindowsOcr.RecognizeAsync : null;
                _dailyNotes = new DailyNoteService(vault, _issues, ocr, TimeSpan.FromSeconds(15));
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

    private void OpenHotkeySettings() => OpenHotkeySettings(openInactive: false);

    private void OpenHotkeySettings(bool openInactive)
    {
        using var form = new SettingsForm(_settings, ScreenshotFolder.Resolve(), ObsidianVaults.Load(), WindowsOcr.IsAvailable, openInactive);
        if (form.ShowDialog() != System.Windows.Forms.DialogResult.OK)
            return;

        _settings.PickerHotkey = form.PickerHotkey;
        _settings.ScreenshotHotkey = form.ScreenshotHotkey;
        _settings.ScreenshotCopyEnabled = form.ScreenshotCopyEnabled;
        _settings.ScreenshotCopyFolder = form.ScreenshotCopyFolder;
        _settings.DailyNoteEnabled = form.DailyNoteEnabled;
        _settings.DailyNoteVault = form.DailyNoteVault;
        _settings.DailyNoteOcr = form.DailyNoteOcr;
        OnSettingsChanged(_settings);
        ApplyScreenshotFeatures();
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
        if (!_settings.TtlEnabled)
            isSecret = false;
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

    private static PickResult ResultFromEntry(ClipEntry entry)
    {
        if (entry.IsImage && entry.ImagePng is { Length: > 0 })
        {
            var path = Path.Combine(Path.GetTempPath(), "sideclip-pick.png");
            File.WriteAllBytes(path, entry.ImagePng);
            return new PickResult { Kind = "image", Path = path };
        }

        return new PickResult { Kind = "text", Text = entry.TryGetNonSecretText() ?? "" };
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _picker?.Close();
        _copier?.Dispose();
        _dailyNotes?.Dispose();
        _ipc?.Dispose();
        _hotkeys?.Dispose();
        _tray?.Dispose();
        _history?.Dispose();
        _watcher?.Dispose();
        if (_debug is not null) { _debug.AllowClose = true; _debug.Close(); }
        try { _single?.ReleaseMutex(); } catch { }
        _single?.Dispose();
        base.OnExit(e);
    }
}



