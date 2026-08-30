using System.IO;
using Sideclip.Native;

namespace Sideclip.Screenshot;

internal static class ScreenshotPathTyper
{
    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");

    public static bool TryTypeNewest(out string? reason, out string? path)
    {
        reason = null;
        path = null;
        var dir = Folder;
        if (!Directory.Exists(dir))
        {
            reason = "Screenshots folder is missing.";
            return false;
        }

        FileInfo? newest = null;
        foreach (var file in new DirectoryInfo(dir).EnumerateFiles("*.*", SearchOption.TopDirectoryOnly))
        {
            if ((file.Attributes & FileAttributes.Hidden) != 0)
                continue;
            if (newest is null || file.LastWriteTimeUtc > newest.LastWriteTimeUtc)
                newest = file;
        }

        if (newest is null)
        {
            reason = "No screenshots yet.";
            return false;
        }

        path = newest.FullName;
        var typed = path;
        if (typed.Contains(' ', StringComparison.Ordinal) && !typed.StartsWith('"'))
            typed = "\"" + typed + "\"";

        if (!InputSender.TypeText(typed))
        {
            reason = "SendInput failed.";
            return false;
        }

        return true;
    }
}
