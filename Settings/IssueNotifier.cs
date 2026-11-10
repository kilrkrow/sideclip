namespace Sideclip.Settings;

/// <summary>
/// Turns background failures into at most one tray notification per kind per window,
/// so a broken folder or a locked note never floods the user.
/// </summary>
internal sealed class IssueNotifier
{
    private readonly Action<string> _show;
    private readonly TimeSpan _window;
    private readonly Func<DateTime> _clock;
    private readonly Dictionary<string, DateTime> _last = new(StringComparer.Ordinal);
    private readonly object _gate = new();

    public IssueNotifier(Action<string> show, TimeSpan? window = null, Func<DateTime>? clock = null)
    {
        _show = show;
        _window = window ?? TimeSpan.FromMinutes(10);
        _clock = clock ?? (() => DateTime.UtcNow);
    }

    /// <returns>True when a notification was actually shown.</returns>
    public bool Report(string kind, string message)
    {
        lock (_gate)
        {
            var now = _clock();
            if (_last.TryGetValue(kind, out var at) && now - at < _window)
                return false;
            _last[kind] = now;
        }

        try
        {
            _show(message);
        }
        catch
        {
        }

        return true;
    }

    /// <summary>Call after a success so the next failure of this kind is reported again.</summary>
    public void Clear(string kind)
    {
        lock (_gate)
            _last.Remove(kind);
    }
}
