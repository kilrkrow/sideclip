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
    private char[]? _armedPayload;
    private CancellationTokenSource? _cancel;
    private Task<TypeOutResult>? _running;
    private bool _enabled;
    private bool _disposed;


    public bool HasEntry => _entry is not null;
    public bool IsActive => _running is not null;

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

    public bool Arm(ClipEntry entry)
    {
        if (!_enabled || _running is not null || !CanType(entry, _isLive(entry))) { Reject(); return false; }
        if (!RegisterCancellation()) return false;
        _armedPayload = entry.TryGetText()!.ToCharArray();
        _entry = entry;
        _report("Armed: target the destination, then press Go. Esc cancels.");
        return true;
    }

    private bool RegisterCancellation()
    {
        try
        {
            if (_activateHotkeys()) return true;
            _report("Type-out could not be armed: the Go or Esc hotkey could not be registered.");
        }
        catch (Exception ex) { _report("Type-out could not be armed: " + ex.Message); }
        ClearArmedEntry();
        return false;
    }

    public void SetEnabled(bool enabled)
    {
        _enabled = enabled;
        if (!enabled) Cancel();
    }

    public void Go(int delayMilliseconds)
    {
        if (!_enabled || _running is not null || _armedPayload is null || _entry is null) return;
        var entry = _entry;
        if (!CanType(entry, _isLive(entry))) { Cancel("Type-out stopped: the entry is no longer available."); return; }
        var target = _focus.ForegroundWindow;
        if (target == IntPtr.Zero) { Cancel("Type-out stopped: there is no destination window."); return; }

        _releaseModifiers?.Invoke();
        if (_disposed || !ReferenceEquals(_entry, entry)) return;
        _cancel = new CancellationTokenSource();
        var token = _cancel.Token;
        var payload = _armedPayload;
        _armedPayload = null;
        var runner = _runnerFactory();
        TryStart(entry, payload, runner, target, delayMilliseconds, token);
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
        _postToUi(() => FinishAsync(_running));
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
        ResetEntry();
        _report(result.Message);
    }

    public void EntryChanged(ClipEntry changed)
    {
        if (ReferenceEquals(changed, _entry))
            Cancel("Type-out stopped because the entry was deleted, expired, or cleared.");
    }

    public void Cancel(string? reason = null)
    {
        if (_disposed) return;
        if (_running is null) ClearArmedEntry();
        _cancel?.Cancel();
        _deactivateHotkeys();
        if (reason is not null) _report(reason);
    }

    public bool Disarm()
    {
        if (_running is not null) return false;
        var wasArmed = _armedPayload is not null;
        if (wasArmed) { Cancel(null); _report("Type-out idle. Select another entry to arm it."); }
        return wasArmed;
    }

    private void ClearArmedEntry()
    {
        if (_armedPayload is not null) Array.Clear(_armedPayload, 0, _armedPayload.Length);
        _armedPayload = null;
        if (_running is null) ResetEntry();
    }

    private void ResetEntry()
    {
        _entry = null;
        _cancel?.Dispose();
        _cancel = null;
        _running = null;
        _deactivateHotkeys();
    }

    private void Reject() => _report("Type-out could not be armed: the entry is not eligible.");

    public void Dispose()
    {
        if (_disposed) return;
        Cancel(null);
        var running = _running;
        if (running is not null && !Task.Run(() => running).Wait(TimeSpan.FromMilliseconds(500)))
            _report("Type-out is waiting for Windows to stop accepting input.");
        _running = null;
        _disposed = true;
        if (_armedPayload is not null) Array.Clear(_armedPayload, 0, _armedPayload.Length);
        _armedPayload = null;
        _entry = null;
    }
}
