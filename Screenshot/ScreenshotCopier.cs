using System.Collections.Concurrent;
using System.IO;
using System.Threading.Channels;
using Sideclip.Settings;

namespace Sideclip.Screenshot;

/// <summary>
/// Watches the Screenshots folder and copies every new image into another folder.
/// Never moves, renames or deletes the original. All work runs on a background task,
/// so it can never block the clipboard watcher or the UI.
/// </summary>
internal sealed class ScreenshotCopier : IDisposable
{
    public const string KindDestination = "copy-destination";
    public const string KindSource = "copy-source";
    public const string KindCopy = "copy-failed";

    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(30);

    private readonly string _source;
    private readonly string _dest;
    private readonly IssueNotifier _issues;
    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>(new UnboundedChannelOptions { SingleReader = true });
    private readonly ConcurrentDictionary<string, byte> _seen = new(StringComparer.OrdinalIgnoreCase);
    private readonly CancellationTokenSource _cts = new();
    private readonly DateTime _startUtc = DateTime.UtcNow;
    private readonly object _gate = new();
    private FileSystemWatcher? _fsw;
    private System.Threading.Timer? _attachTimer;
    private Task? _worker;
    private bool _disposed;

    /// <summary>Raised on the worker thread after each successful copy (source, copy).</summary>
    public event Action<string, string>? Copied;

    public ScreenshotCopier(string sourceFolder, string destFolder, IssueNotifier issues)
    {
        _source = sourceFolder;
        _dest = destFolder;
        _issues = issues;
    }

    public string SourceFolder => _source;
    public string DestinationFolder => _dest;

    public void Start()
    {
        _worker = Task.Run(WorkAsync);
        if (!TryAttach())
            _attachTimer = new System.Threading.Timer(_ => TryAttach(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
    }

    private bool TryAttach()
    {
        lock (_gate)
        {
            if (_disposed)
                return true;
            if (_fsw is not null)
                return true;
            try
            {
                // The Screenshots folder appears with the first Win+PrtScn; wait for it quietly.
                if (!Directory.Exists(_source))
                    return false;

                var fsw = new FileSystemWatcher(_source)
                {
                    IncludeSubdirectories = false,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    InternalBufferSize = 64 * 1024
                };
                fsw.Created += (_, e) => Offer(e.FullPath);
                fsw.Changed += (_, e) => Offer(e.FullPath);
                fsw.Renamed += (_, e) => Offer(e.FullPath);
                fsw.Error += (_, _) => OnWatcherError();
                fsw.EnableRaisingEvents = true;
                _fsw = fsw;
                _attachTimer?.Dispose();
                _attachTimer = null;
                // Catch anything written between construction and the watcher going live.
                Rescan();
                return true;
            }
            catch (Exception ex)
            {
                _issues.Report(KindSource, "Screenshot copy: cannot watch the Screenshots folder. " + ex.Message);
                return false;
            }
        }
    }

    private void OnWatcherError()
    {
        // Buffer overflow or the folder went away: rescan for anything new, re-arm if needed.
        try
        {
            lock (_gate)
            {
                if (_disposed)
                    return;
                if (!Directory.Exists(_source))
                {
                    _fsw?.Dispose();
                    _fsw = null;
                    _attachTimer ??= new System.Threading.Timer(_ => TryAttach(), null, TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5));
                    return;
                }
            }

            Rescan();
        }
        catch
        {
        }
    }

    private void Rescan()
    {
        try
        {
            foreach (var f in new DirectoryInfo(_source).EnumerateFiles("*", SearchOption.TopDirectoryOnly))
            {
                // Only files created after the copier started count as new.
                if (f.CreationTimeUtc >= _startUtc)
                    Offer(f.FullName);
            }
        }
        catch
        {
        }
    }

    /// <summary>Created, Changed and Renamed all land here; each path is queued at most once.</summary>
    private void Offer(string path)
    {
        try
        {
            if (_disposed || !ScreenshotRules.IsImageFile(path))
                return;
            if ((File.GetAttributes(path) & (FileAttributes.Hidden | FileAttributes.Directory)) != 0)
                return;
            // Ignore files that were already there before we started (a Changed event on an old file).
            if (File.GetCreationTimeUtc(path) < _startUtc)
                return;
            if (!_seen.TryAdd(path, 0))
                return;
            _queue.Writer.TryWrite(path);
        }
        catch
        {
            // File vanished between the event and the check: nothing to copy.
        }
    }

    private async Task WorkAsync()
    {
        var ct = _cts.Token;
        try
        {
            while (await _queue.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (_queue.Reader.TryRead(out var path))
                    CopyOne(path, ct);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }

    private void CopyOne(string path, CancellationToken ct)
    {
        try
        {
            var outcome = FileCopier.CopyWhenReady(path, _dest, ReadyTimeout, ct, out var copy, out var error);
            switch (outcome)
            {
                case CopyOutcome.Copied:
                    _issues.Clear(KindDestination);
                    _issues.Clear(KindCopy);
                    try
                    {
                        Copied?.Invoke(path, copy!);
                    }
                    catch
                    {
                    }
                    break;
                case CopyOutcome.DestinationMissing:
                    _issues.Report(KindDestination, "Screenshot copy: folder not found. Check Settings.");
                    break;
                case CopyOutcome.SourceBusy:
                case CopyOutcome.Failed:
                    _issues.Report(KindCopy, "Screenshot copy failed: " + (error ?? Path.GetFileName(path)));
                    break;
                case CopyOutcome.SourceGone:
                    break;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            _issues.Report(KindCopy, "Screenshot copy failed: " + ex.Message);
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
            _attachTimer?.Dispose();
            _attachTimer = null;
            if (_fsw is not null)
            {
                _fsw.EnableRaisingEvents = false;
                _fsw.Dispose();
                _fsw = null;
            }
        }

        _queue.Writer.TryComplete();
        // Not disposed: the worker may still be observing the token.
        _cts.Cancel();
    }
}
