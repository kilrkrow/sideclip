using Sideclip.History;
using Sideclip.Native;

namespace Sideclip.TypeOut;

internal sealed class TypeOutSession : IDisposable
{
    private readonly Func<ClipEntry, bool> _isLive;
    private readonly Func<TypeOutRunner> _runnerFactory;
    private readonly IFocusProbe _focus;
    private readonly Action<string> _report;
    private readonly Action? _releaseModifiers;
    private readonly Action<Action> _postToUi;
    private readonly Func<bool> _activateHotkeys;
    private readonly Action _deactivateHotkeys;
    private ClipEntry? _entry;
    private ClipEntry? _candidate;
    private CancellationTokenSource? _cancel;
    private Task<TypeOutResult>? _running;
    private bool _enabled;
    private bool _disposed;
    private bool _starting;
    private string? _stopReason;


    public bool HasEntry => _candidate is not null || _entry is not null;
    internal ClipEntry? Candidate => _candidate;
    private bool _pickerForeground;
    public bool IsActive => _starting || _running is not null;

    public TypeOutSession(
        Func<ClipEntry, bool> isLive,
        Action<string> report,
        Func<TypeOutRunner>? runnerFactory = null,
        IFocusProbe? focus = null,
        Action? releaseModifiers = null,
        Action<Action>? postToUi = null,
        Func<bool>? activateHotkeys = null,
        Action? deactivateHotkeys = null)
    {
        _isLive = isLive;
        _report = report;
        var win32 = new Win32InputSink();
        _focus = focus ?? win32;
        _runnerFactory = runnerFactory ?? (() => new TypeOutRunner(win32, win32));
        _releaseModifiers = releaseModifiers ?? InputSender.ReleaseModifiers;
        _postToUi = postToUi ?? (action => System.Windows.Application.Current.Dispatcher.Invoke(action));
        _activateHotkeys = activateHotkeys ?? (() => true);
        _deactivateHotkeys = deactivateHotkeys ?? (() => { });
    }

    public static bool CanType(ClipEntry entry, bool isLive) =>
        isLive && entry.HasPayload && !entry.IsImage && TypeOutText.Validate(entry.TryGetText()) is null;

    // Selection retains identity only. The payload is read under its gate at trigger time.
    public bool SelectCandidate(ClipEntry entry)
    {
        if (_disposed || IsActive) return false;
        Cancel();
        if (!_enabled || !CanType(entry, _isLive(entry))) { Reject(); return false; }
        _candidate = entry;
        _stopReason = null;
        _report(TypeOutText.HasTargetKeys(entry.TryGetText())
            ? TypeOutText.SelectedTargetKeyWarning
            : "Selected: focus destination, then use the type-out shortcut. Esc cancels.");
        RefreshCancellation(_pickerForeground);
        return true;
    }

    // Keep picker-local Esc available for filter-first cancel. Failure while pending
    // is tolerated; starting a job requires successful Esc registration.
    public void RefreshCancellation(bool pickerForeground)
    {
        _pickerForeground = pickerForeground;
        if (IsActive) return;
        _deactivateHotkeys();
        if (_candidate is not null && !pickerForeground && !_activateHotkeys())
            _report("Pending Esc unavailable; picker Esc still cancels. Typing requires Esc.");
    }

    internal const string TurnedOffMessage = "Type-out turned off. No source selected.";
    internal const string StoppedOffMessage = "Type-out stopped because it was turned off.";
    internal const string IdleMessage = "Type-out idle. Select a text entry deliberately.";
    internal const string HotkeysChangedMessage = "Type-out cancelled because settings changed. Select again.";
    public void SetEnabled(bool enabled)
    {
        var wasEnabled = _enabled;
        _enabled = enabled;
        if (enabled) { if (!wasEnabled) _report(IdleMessage); return; }
        if (!HasEntry) { Cancel(); return; }
        if (IsActive) { _stopReason = StoppedOffMessage; Cancel(StoppedOffMessage); }
        else Cancel(TurnedOffMessage);
    }

    public void Go(int delayMilliseconds)
    {
        if (_disposed || !_enabled || IsActive) return;
        var entry = _candidate;
        _candidate = null;
        _deactivateHotkeys();
        if (entry is null) { _report("Type-out: no selected source. Select text in the picker first."); return; }
        _entry = entry;
        _starting = true;
        _cancel = new CancellationTokenSource();
        var token = _cancel.Token;
        char[]? payload = null;
        try
        {
            if (!CanType(entry, _isLive(entry))) { _report("Type-out stopped: the entry is no longer available."); return; }
            var target = _focus.ForegroundWindow;
            NativeMethods.GetWindowThreadProcessId(target, out var pid);
            if (target == IntPtr.Zero || pid == Environment.ProcessId)
            { _report("Type-out refused: focus an external destination window."); return; }
            if (!_activateHotkeys()) { _report("Type-out refused: Esc cancellation is unavailable."); return; }
            payload = entry.CopyTypeOutPayload();
            if (payload is null || !_isLive(entry)) { _report("Type-out stopped: the entry is no longer available."); return; }
            _releaseModifiers?.Invoke();
            if (_disposed || token.IsCancellationRequested || !_enabled) return;
            TryStart(entry, payload, _runnerFactory(), target, Math.Clamp(delayMilliseconds, 0, 250), token);
            payload = null; // Runner now owns and clears the bounded copy.
        }
        catch (Exception)
        {
            _report("Type-out failed to start safely. No input job was started.");
        }
        finally
        {
            _starting = false;
            if (payload is not null) Array.Clear(payload);
            if (_running is null) ResetEntry();
        }
    }
    private void TryStart(ClipEntry entry, char[] payload, TypeOutRunner runner, IntPtr target, int delay, CancellationToken token)
    {
        if (_disposed) { Array.Clear(payload, 0, payload.Length); return; }
        _running = Task.Run(async () =>
        {
            try { return await runner.RunAsync(payload, () => _isLive(entry), target, delay, token).ConfigureAwait(false); }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                return new TypeOutResult(TypeOutStatus.Cancelled, "Type-out cancelled. No later input will be sent.");
            }
            catch (Exception)
            {
                return new TypeOutResult(TypeOutStatus.PartialSend, "Type-out failed unexpectedly; some text may already have been entered.");
            }
            finally { Array.Clear(payload, 0, payload.Length); }
        }, CancellationToken.None);
        var running = _running;
        try { _postToUi(() => FinishAsync(running)); }
        catch (Exception)
        {
            // If the UI is shutting down, retain completion ownership and stop
            // the already-started job instead of orphaning its payload/state.
            _cancel?.Cancel();
            FinishAsync(running);
        }
    }

    private void FinishAsync(Task<TypeOutResult>? task)
    {
        if (task is null) return;
        _ = CompleteAsync(task);
    }

    private async Task CompleteAsync(Task<TypeOutResult> task)
    {
        var result = await task.ConfigureAwait(true);
        if (_disposed || !ReferenceEquals(_running, task)) return;
        var stopReason = _stopReason;
        _stopReason = null;
        ResetEntry();
        _report(WithStopReason(result, stopReason).Message);
    }

    public void EntryChanged(ClipEntry changed)
    {
        if (ReferenceEquals(changed, _entry) || ReferenceEquals(changed, _candidate))
            StopForSource("Type-out stopped because the entry was deleted, expired, or cleared.");
    }

    public void EntryChanged(ClipEntry changed, EntryInvalidationReason reason)
    {
        if (ReferenceEquals(changed, _entry) || ReferenceEquals(changed, _candidate))
            StopForSource(StopMessage(reason));
    }

    internal static string StopMessage(EntryInvalidationReason reason) => reason switch
    {
        EntryInvalidationReason.Expired => "Type-out stopped because the entry expired (secret TTL).",
        EntryInvalidationReason.Cleared => "Type-out stopped because history was cleared.",
        _ => "Type-out stopped because the entry was deleted."
    };

    private void StopForSource(string message)
    {
        if (IsActive) _stopReason = message;
        Cancel(message);
    }

    /// <summary>
    /// A source invalidation cancels the running job's token; keep its reason
    /// instead of reporting a generic cancel.
    /// </summary>
    internal static TypeOutResult WithStopReason(TypeOutResult result, string? stopReason)
    {
        const string cancelled = "Type-out cancelled.";
        if (stopReason is null || result.Status is not (TypeOutStatus.Cancelled or TypeOutStatus.SourceGone)) return result;
        var message = result.Message;
        foreach (var prefix in new[] { cancelled, "Type-out stopped because the entry was deleted, expired, or cleared." })
            if (message.StartsWith(prefix, StringComparison.Ordinal)) { message = stopReason + message[prefix.Length..]; break; }
        return new TypeOutResult(TypeOutStatus.SourceGone, message);
    }

    public void Cancel(string? reason = null)
    {
        if (_disposed) return;
        _candidate = null;
        if (!IsActive) ResetEntry();
        _cancel?.Cancel();
        if (_running is null) _deactivateHotkeys();
        if (reason is not null) _report(reason);
    }

    private void ResetEntry()
    {
        _entry = null;
        _cancel?.Dispose();
        _cancel = null;
        _running = null;
        _deactivateHotkeys();
    }

    private void Reject() => _report("Type-out: selected entry is not eligible.");

    public void Dispose()
    {
        if (_disposed) return;
        Cancel(null);
        var running = _running;
        if (running is not null && !Task.Run(() => running).Wait(TimeSpan.FromMilliseconds(500)))
            _report("Type-out is waiting for Windows to stop accepting input.");
        _running = null;
        _deactivateHotkeys();
        _cancel?.Dispose();
        _cancel = null;
        _disposed = true;
        _candidate = null;
        _entry = null;
    }
}
