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
        if (!Directory.Exists(Folder))
        {
            reason = "Screenshots folder is missing.";
            return false;
        }

        var newest = ScreenshotFiles.NewestOne();
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
