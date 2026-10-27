using System.IO;
using System.Text;
using Sideclip.Screenshot;

namespace Sideclip.Obsidian;

internal enum NoteOutcome
{
    Appended,
    VaultMissing,
    NoteLocked,
    Failed
}

internal sealed class NoteResult
{
    public NoteOutcome Outcome { get; init; }
    public string NotePath { get; init; } = "";
    public bool Created { get; init; }
    public bool TemplateMissing { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Creates today's daily note when needed (from the configured template) and appends lines to it.
/// Append only: existing bytes are never rewritten. New text uses the note's own encoding (BOM is
/// respected) and line endings, and a newline is added first only if the note does not end with one.
/// </summary>
internal static class DailyNoteWriter
{
    private const int ErrorSharingViolation = unchecked((int)0x80070020);
    private const int ErrorLockViolation = unchecked((int)0x80070021);

    public static NoteResult Append(string vault, DailyNotesConfig config, DateTime now, IReadOnlyList<string> lines, int attempts = 10, int retryMs = 200)
    {
        string notePath;
        try
        {
            if (string.IsNullOrWhiteSpace(vault) || !Directory.Exists(vault))
                return new NoteResult { Outcome = NoteOutcome.VaultMissing };
            notePath = config.NotePath(vault, now);
            if (!ScreenshotRules.IsInside(notePath, vault))
                return new NoteResult { Outcome = NoteOutcome.Failed, NotePath = notePath, Error = "Daily note path leaves the vault." };
        }
        catch (Exception ex)
        {
            return new NoteResult { Outcome = NoteOutcome.Failed, Error = ex.Message };
        }

        var created = false;
        var templateMissing = false;
        string? lastError = null;
        for (var attempt = 0; attempt < Math.Max(1, attempts); attempt++)
        {
            try
            {
                if (!File.Exists(notePath))
                {
                    created = TryCreate(vault, config, now, notePath, out templateMissing) || created;
                }

                AppendLines(notePath, lines);
                return new NoteResult { Outcome = NoteOutcome.Appended, NotePath = notePath, Created = created, TemplateMissing = templateMissing };
            }
            catch (IOException ex) when (ex.HResult is ErrorSharingViolation or ErrorLockViolation)
            {
                lastError = ex.Message;
                Thread.Sleep(retryMs);
            }
            catch (UnauthorizedAccessException ex)
            {
                // Read-only note or a folder we may not write to: retrying will not help.
                return new NoteResult { Outcome = NoteOutcome.NoteLocked, NotePath = notePath, Error = ex.Message };
            }
            catch (Exception ex)
            {
                return new NoteResult { Outcome = NoteOutcome.Failed, NotePath = notePath, Error = ex.Message };
            }
        }

        return new NoteResult { Outcome = NoteOutcome.NoteLocked, NotePath = notePath, Error = lastError };
    }

    /// <returns>True if this call created the note.</returns>
    private static bool TryCreate(string vault, DailyNotesConfig config, DateTime now, string notePath, out bool templateMissing)
    {
        templateMissing = false;
        var text = string.Empty;
        var templatePath = config.TemplatePath(vault);
        if (templatePath is not null)
        {
            if (File.Exists(templatePath))
            {
                var title = Path.GetFileNameWithoutExtension(notePath);
                text = DailyNoteText.ApplyTemplate(File.ReadAllText(templatePath), now, config.Format, title);
            }
            else
            {
                templateMissing = true;
            }
        }

        Directory.CreateDirectory(Path.GetDirectoryName(notePath)!);
        try
        {
            using var fs = new FileStream(notePath, FileMode.CreateNew, FileAccess.Write, FileShare.Read);
            var bytes = new UTF8Encoding(false).GetBytes(text);
            fs.Write(bytes, 0, bytes.Length);
            fs.Flush(true);
            return true;
        }
        catch (IOException) when (File.Exists(notePath))
        {
            // Someone (Obsidian) created it a moment ago: just append to theirs.
            return false;
        }
    }

    /// <summary>Appends whole lines to an existing file, matching its encoding and line endings.</summary>
    public static void AppendLines(string path, IReadOnlyList<string> lines)
    {
        using var fs = new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.Read);
        var existing = new byte[fs.Length];
        var read = 0;
        while (read < existing.Length)
        {
            var n = fs.Read(existing, read, existing.Length - read);
            if (n <= 0)
                break;
            read += n;
        }

        var (encoding, bomLength) = Detect(existing);
        var text = encoding.GetString(existing, bomLength, read - bomLength);
        var nl = NewlineOf(text);

        var sb = new StringBuilder();
        if (text.Length > 0 && !(text.EndsWith('\n') || text.EndsWith('\r')))
            sb.Append(nl);
        foreach (var line in lines)
            sb.Append(line).Append(nl);

        // GetBytes never writes a BOM, so a BOM only ever appears at the start, where it already was.
        var bytes = encoding.GetBytes(sb.ToString());
        fs.Seek(0, SeekOrigin.End);
        fs.Write(bytes, 0, bytes.Length);
        fs.Flush(true);
    }

    public static (Encoding Encoding, int BomLength) Detect(byte[] b)
    {
        if (b.Length >= 3 && b[0] == 0xEF && b[1] == 0xBB && b[2] == 0xBF)
            return (new UTF8Encoding(false), 3);
        if (b.Length >= 2 && b[0] == 0xFF && b[1] == 0xFE)
            return (new UnicodeEncoding(false, false), 2);
        if (b.Length >= 2 && b[0] == 0xFE && b[1] == 0xFF)
            return (new UnicodeEncoding(true, false), 2);
        return (new UTF8Encoding(false), 0);
    }

    /// <summary>The note's own line ending (first one found); "\n" for an empty or single-line note.</summary>
    public static string NewlineOf(string text)
    {
        var i = text.IndexOf('\n');
        if (i > 0 && text[i - 1] == '\r')
            return "\r\n";
        if (i >= 0)
            return "\n";
        return text.Contains('\r') ? "\r" : "\n";
    }
}
