using System.IO;

namespace Sideclip.Screenshot;

internal enum CopyOutcome
{
    Copied,
    SourceGone,
    SourceBusy,
    DestinationMissing,
    Failed
}

/// <summary>Copies one finished file without ever touching the original or overwriting a target.</summary>
internal static class FileCopier
{
    private const int ErrorSharingViolation = unchecked((int)0x80070020);
    private const int ErrorLockViolation = unchecked((int)0x80070021);
    private const int ErrorFileExists = unchecked((int)0x80070050);
    private const int ErrorAlreadyExists = unchecked((int)0x800700B7);

    /// <summary>
    /// Waits until the source can be opened without other writers (the capture tool has finished),
    /// then streams it to a new, uniquely named file in <paramref name="destFolder"/>.
    /// </summary>
    public static CopyOutcome CopyWhenReady(
        string source,
        string destFolder,
        TimeSpan timeout,
        CancellationToken ct,
        out string? destPath,
        out string? error)
    {
        destPath = null;
        error = null;
        var deadline = DateTime.UtcNow + timeout;
        var delay = 100;
        long lastLen = -1;

        while (true)
        {
            ct.ThrowIfCancellationRequested();
            if (!Directory.Exists(destFolder))
                return CopyOutcome.DestinationMissing;

            try
            {
                // FileShare.Read: fails with a sharing violation while the writer still has it open.
                // FileShare.Delete: never stop the user deleting or moving the original.
                using var src = new FileStream(source, FileMode.Open, FileAccess.Read, FileShare.Read | FileShare.Delete, 81920, FileOptions.SequentialScan);
                var len = src.Length;
                // Zero-length or still growing: give the writer another moment.
                if (len == 0 || len != lastLen)
                {
                    lastLen = len;
                    if (DateTime.UtcNow < deadline)
                    {
                        src.Dispose();
                        Sleep(ref delay, ct);
                        continue;
                    }
                }

                destPath = CreateUnique(destFolder, Path.GetFileName(source), out var dest);
                using (dest)
                {
                    src.CopyTo(dest);
                    dest.Flush(true);
                }

                try
                {
                    File.SetLastWriteTimeUtc(destPath, File.GetLastWriteTimeUtc(source));
                }
                catch
                {
                }

                return CopyOutcome.Copied;
            }
            catch (FileNotFoundException)
            {
                return CopyOutcome.SourceGone;
            }
            catch (DirectoryNotFoundException) when (!Directory.Exists(destFolder))
            {
                return CopyOutcome.DestinationMissing;
            }
            catch (DirectoryNotFoundException)
            {
                return CopyOutcome.SourceGone;
            }
            catch (IOException ex) when (ex.HResult is ErrorSharingViolation or ErrorLockViolation)
            {
                if (DateTime.UtcNow >= deadline)
                {
                    error = ex.Message;
                    return CopyOutcome.SourceBusy;
                }

                Sleep(ref delay, ct);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // A half-written copy is worse than none.
                TryDelete(destPath);
                destPath = null;
                error = ex.Message;
                return CopyOutcome.Failed;
            }
        }
    }

    private static string CreateUnique(string folder, string fileName, out FileStream stream)
    {
        for (var i = 0; i < 10_000; i++)
        {
            var candidate = ScreenshotRules.CandidateName(folder, fileName, i);
            if (File.Exists(candidate))
                continue;
            try
            {
                stream = new FileStream(candidate, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                return candidate;
            }
            catch (IOException ex) when (ex.HResult is ErrorFileExists or ErrorAlreadyExists)
            {
                // Lost a race with another writer; try the next suffix.
            }
        }

        throw new IOException("No free file name for " + fileName);
    }

    private static void Sleep(ref int delay, CancellationToken ct)
    {
        ct.WaitHandle.WaitOne(delay);
        ct.ThrowIfCancellationRequested();
        delay = Math.Min(delay * 2, 1000);
    }

    private static void TryDelete(string? path)
    {
        if (path is null)
            return;
        try
        {
            File.Delete(path);
        }
        catch
        {
        }
    }
}
