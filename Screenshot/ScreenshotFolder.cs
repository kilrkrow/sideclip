using System.IO;
using System.Runtime.InteropServices;

namespace Sideclip.Screenshot;

/// <summary>Finds the folder Windows saves Win+PrtScn / Snipping Tool captures into.</summary>
internal static class ScreenshotFolder
{
    /// <summary>Test hook: watch this folder instead of the real Screenshots folder.</summary>
    public const string OverrideEnv = "SIDECLIP_SCREENSHOTS_DIR";

    // FOLDERID_Screenshots
    private static readonly Guid ScreenshotsFolderId = new("b7bede81-df94-4682-a7d8-57a52620b86f");

    public static string Resolve()
    {
        try
        {
            var env = Environment.GetEnvironmentVariable(OverrideEnv);
            if (!string.IsNullOrWhiteSpace(env))
                return Path.GetFullPath(env.Trim());
        }
        catch
        {
        }

        try
        {
            // KF_FLAG_DONT_VERIFY (0x4000): return the path even if the folder does not exist yet.
            if (SHGetKnownFolderPath(ScreenshotsFolderId, 0x4000, IntPtr.Zero, out var ptr) == 0 && ptr != IntPtr.Zero)
            {
                try
                {
                    var path = Marshal.PtrToStringUni(ptr);
                    if (!string.IsNullOrWhiteSpace(path))
                        return path;
                }
                finally
                {
                    Marshal.FreeCoTaskMem(ptr);
                }
            }
        }
        catch
        {
        }

        return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetKnownFolderPath(
        [MarshalAs(UnmanagedType.LPStruct)] Guid rfid, uint dwFlags, IntPtr hToken, out IntPtr ppszPath);
}
