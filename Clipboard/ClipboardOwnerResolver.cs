using System.Diagnostics;
using System.IO;
using System.Text;
using Sideclip.Native;

namespace Sideclip.Clipboard;

/// <summary>
/// Resolves the clipboard-owner process via GetClipboardOwner + PID + image name.
/// Firefox/Chrome extension copies often leave clipboard owner blank, so we also
/// take GetForegroundWindow. Failure is non-fatal.
/// </summary>
internal static class ClipboardOwnerResolver
{
    public static bool TryGetOwnerExeName(out string? exeName)
        => TryGetExeName(NativeMethods.GetClipboardOwner(), out exeName);

    public static bool TryGetForegroundExeName(out string? exeName)
        => TryGetExeName(NativeMethods.GetForegroundWindow(), out exeName);

    private static bool TryGetExeName(IntPtr hwnd, out string? exeName)
    {
        exeName = null;
        try
        {
            if (hwnd == IntPtr.Zero)
                return false;

            _ = NativeMethods.GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == 0)
                return false;

            var handle = NativeMethods.OpenProcess(
                NativeMethods.ProcessQueryLimitedInformation,
                false,
                pid);
            if (handle == IntPtr.Zero)
                return false;

            try
            {
                var capacity = 1024;
                var buffer = new StringBuilder(capacity);
                var size = capacity;
                if (!NativeMethods.QueryFullProcessImageName(handle, 0, buffer, ref size))
                    return false;

                var fullPath = buffer.ToString();
                if (string.IsNullOrWhiteSpace(fullPath))
                    return false;

                exeName = Path.GetFileName(fullPath);
                return !string.IsNullOrEmpty(exeName);
            }
            finally
            {
                NativeMethods.CloseHandle(handle);
            }
        }
        catch
        {
            exeName = null;
            return false;
        }
    }

    public static bool IsOnePassword(string? exeName)
    {
        if (string.IsNullOrEmpty(exeName))
            return false;

        var n = exeName.ToLowerInvariant();
        return n.Contains("1password", StringComparison.Ordinal)
               || n.Contains("1 password", StringComparison.Ordinal);
    }

    public static bool IsBrowser(string? exeName)
    {
        if (string.IsNullOrEmpty(exeName))
            return false;

        var n = Path.GetFileNameWithoutExtension(exeName).ToLowerInvariant();
        return n is "firefox" or "chrome" or "msedge" or "brave"
            or "opera" or "vivaldi" or "chromium" or "librewolf"
            or "waterfox" or "iexplore" or "browser";
    }

    public static bool IsOnePasswordRunning()
    {
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    var name = p.ProcessName;
                    if (name.Contains("1Password", StringComparison.OrdinalIgnoreCase)
                        || name.Contains("1password", StringComparison.OrdinalIgnoreCase))
                        return true;
                }
                catch
                {
                }
                finally
                {
                    p.Dispose();
                }
            }
        }
        catch
        {
            return false;
        }

        return false;
    }

    /// <summary>
    /// Only when the clipboard owner or foreground app is a 1Password process
    /// (1Password.exe, 1Password-BrowserSupport). A browser copy is not 1Password
    /// just because 1Password is running — those go through the heuristic.
    /// </summary>
    public static bool IsOnePasswordSource(string? ownerExe, string? foregroundExe)
        => IsOnePassword(ownerExe) || IsOnePassword(foregroundExe);
}

