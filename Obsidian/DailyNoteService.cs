using System.IO;
using System.Threading.Channels;
using Sideclip.Settings;

namespace Sideclip.Obsidian;

/// <summary>
/// Background queue that adds one line per copied screenshot to today's daily note.
/// Runs on its own task so a slow disk, a locked note or OCR never holds up copying.
/// </summary>
internal sealed class DailyNoteService : IDisposable
{
    public const string KindVault = "note-vault";
    public const string KindLocked = "note-locked";
    public const string KindTemplate = "note-template";
    public const string KindFailed = "note-failed";

    /// <summary>Optional text recognizer: (image path, cancel) -> text or null.</summary>
    public delegate Task<string?> Recognizer(string imagePath, CancellationToken ct);

    private readonly string _vault;
    private readonly IssueNotifier _issues;
    private readonly Recognizer? _ocr;
    private readonly TimeSpan _ocrTimeout;
    private readonly Channel<(string File, DateTime At)> _queue =
        Channel.CreateUnbounded<(string, DateTime)>(new UnboundedChannelOptions { SingleReader = true });
    private readonly CancellationTokenSource _cts = new();

    public DailyNoteService(string vault, IssueNotifier issues, Recognizer? ocr = null, TimeSpan? ocrTimeout = null)
    {
        _vault = vault;
        _issues = issues;
        _ocr = ocr;
        _ocrTimeout = ocrTimeout ?? TimeSpan.FromSeconds(15);
        _ = Task.Run(WorkAsync);
    }

    public void Enqueue(string copiedFile, DateTime at) => _queue.Writer.TryWrite((copiedFile, at));

    private async Task WorkAsync()
    {
        var ct = _cts.Token;
        try
        {
            while (await _queue.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                while (_queue.Reader.TryRead(out var item))
                    await HandleAsync(item.File, item.At, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch
        {
        }
    }

    private async Task HandleAsync(string file, DateTime at, CancellationToken ct)
    {
        try
        {
            var lines = new List<string> { DailyNoteText.EntryLine(_vault, file, at) };
            if (_ocr is not null)
            {
                var text = await RecognizeAsync(file, ct).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(text))
                    lines.AddRange(DailyNoteText.OcrCallout(text.Trim()));
            }

            // Re-read the plugin settings every time; the user may change them in Obsidian.
            var config = DailyNotesConfig.Load(_vault);
            var result = DailyNoteWriter.Append(_vault, config, at, lines);
            switch (result.Outcome)
            {
                case NoteOutcome.Appended:
                    _issues.Clear(KindVault);
                    _issues.Clear(KindLocked);
                    _issues.Clear(KindFailed);
                    if (result.TemplateMissing)
                        _issues.Report(KindTemplate, "Daily note template not found: " + config.Template + ". Created an empty note.");
                    break;
                case NoteOutcome.VaultMissing:
                    _issues.Report(KindVault, "Daily note: vault folder not found. Was it moved? Check Settings.");
                    break;
                case NoteOutcome.NoteLocked:
                    _issues.Report(KindLocked, "Daily note is locked or read-only: " + Path.GetFileName(result.NotePath));
                    break;
                default:
                    _issues.Report(KindFailed, "Daily note update failed: " + (result.Error ?? "unknown error"));
                    break;
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _issues.Report(KindFailed, "Daily note update failed: " + ex.Message);
        }
    }

    private async Task<string?> RecognizeAsync(string file, CancellationToken ct)
    {
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(_ocrTimeout);
            var work = _ocr!(file, timeout.Token);
            var done = await Task.WhenAny(work, Task.Delay(_ocrTimeout, ct)).ConfigureAwait(false);
            if (done != work)
                return null;
            return await work.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!ct.IsCancellationRequested)
        {
            return null;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            // OCR is a nice-to-have; the embed line is still written.
            return null;
        }
    }

    public void Dispose()
    {
        _queue.Writer.TryComplete();
        // Let queued lines finish writing on shutdown, but never wait on it here.
        _cts.CancelAfter(TimeSpan.FromSeconds(5));
    }
}
