using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace Sideclip.Settings;

/// <summary>
/// Where Sideclip keeps settings and history. Normally %LocalAppData%\Sideclip.
/// Set SIDECLIP_DATA_DIR to run an isolated profile (tests, a second copy): it gets its own
/// settings, history, single-instance mutex and IPC pipe, and never touches the autostart key.
/// </summary>
internal static class AppProfile
{
    public const string DataDirEnv = "SIDECLIP_DATA_DIR";

    public static bool IsIsolated => ReadOverride() is not null;

    public static string DataDir
    {
        get
        {
            var ov = ReadOverride();
            return ov ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Sideclip");
        }
    }

    /// <summary>Empty for the normal profile, ".xxxxxxxx" for an isolated one.</summary>
    public static string NameSuffix
    {
        get
        {
            var ov = ReadOverride();
            if (ov is null) return string.Empty;
            var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(ov.ToUpperInvariant()));
            return "." + Convert.ToHexString(bytes, 0, 4).ToLowerInvariant();
        }
    }

    private static string? ReadOverride()
    {
        try
        {
            var v = Environment.GetEnvironmentVariable(DataDirEnv);
            if (string.IsNullOrWhiteSpace(v))
                return null;
            return Path.GetFullPath(v.Trim());
        }
        catch
        {
            return null;
        }
    }
}
