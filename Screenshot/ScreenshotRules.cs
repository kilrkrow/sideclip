using System.IO;

namespace Sideclip.Screenshot;

/// <summary>Pure path rules shared by the settings UI, the copier and the daily-note writer.</summary>
internal static class ScreenshotRules
{
    public static readonly string[] ImageExtensions = { ".png", ".jpg", ".jpeg", ".gif", ".bmp", ".webp" };

    public static bool IsImageFile(string path)
    {
        var ext = Path.GetExtension(path);
        foreach (var e in ImageExtensions)
        {
            if (string.Equals(e, ext, StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }

    public static string Normalize(string path)
    {
        var full = Path.GetFullPath(path.Trim());
        var root = Path.GetPathRoot(full) ?? string.Empty;
        if (full.Length > root.Length)
            full = full.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return full;
    }

    public static bool SamePath(string a, string b)
    {
        try
        {
            return string.Equals(Normalize(a), Normalize(b), StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>True when <paramref name="path"/> is <paramref name="folder"/> or somewhere beneath it.</summary>
    public static bool IsInside(string path, string folder)
    {
        try
        {
            var p = Normalize(path);
            var f = Normalize(folder);
            if (string.Equals(p, f, StringComparison.OrdinalIgnoreCase))
                return true;
            var prefix = f.EndsWith(Path.DirectorySeparatorChar) ? f : f + Path.DirectorySeparatorChar;
            return p.StartsWith(prefix, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    /// <summary>Copy target must exist, be absolute, and not be the watched folder itself.</summary>
    public static bool IsCopyFolderValid(string? folder, string screenshotsFolder, out string reason)
    {
        reason = string.Empty;
        if (string.IsNullOrWhiteSpace(folder))
        {
            reason = "Choose a folder.";
            return false;
        }

        try
        {
            if (!Path.IsPathFullyQualified(folder.Trim()))
            {
                reason = "Use a full folder path.";
                return false;
            }

            if (!Directory.Exists(folder.Trim()))
            {
                reason = "Folder not found.";
                return false;
            }

            if (SamePath(folder, screenshotsFolder))
            {
                reason = "Pick a folder other than the Screenshots folder.";
                return false;
            }
        }
        catch
        {
            reason = "Folder path is not valid.";
            return false;
        }

        return true;
    }

    /// <summary>
    /// "name.png", then "name (1).png", "name (2).png", ... in <paramref name="folder"/>.
    /// Only a candidate; the caller must create it with FileMode.CreateNew and retry on collision.
    /// </summary>
    public static string CandidateName(string folder, string fileName, int index)
    {
        if (index <= 0)
            return Path.Combine(folder, fileName);
        var stem = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        return Path.Combine(folder, stem + " (" + index + ")" + ext);
    }
}
