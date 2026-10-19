using System.IO;
using System.Windows.Threading;
using Sideclip.History;
using Sideclip.Settings;
using Xunit;

// SettingsManager.DirOverride is process-wide, so tests must not run in parallel.
[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace Sideclip.Tests;

/// <summary>
/// A throwaway data folder. Sideclip is pointed at it through
/// SettingsManager.DirOverride, so no test can touch the real profile.
/// Set SIDECLIP_TEST_TEMP to choose where the folder is created.
/// </summary>
internal sealed class TestProfile : IDisposable
{
    public string Root { get; }
    public string Images => Path.Combine(Root, "images");

    public TestProfile()
    {
        var parent = Environment.GetEnvironmentVariable("SIDECLIP_TEST_TEMP");
        if (string.IsNullOrWhiteSpace(parent))
            parent = Path.GetTempPath();
        Root = Path.Combine(parent, "sideclip-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Root);
        SettingsManager.DirOverride = Root;
    }

    public ClipHistoryStore Open(int maxText = AppSettings.Unlimited, int maxImages = AppSettings.Unlimited) =>
        new(Dispatcher.CurrentDispatcher, _ => { }, null, true, maxText, maxImages);

    public string[] ImageFiles() =>
        Directory.Exists(Images)
            ? Directory.GetFiles(Images).Select(f => Path.GetFileName(f)).OrderBy(n => n, StringComparer.Ordinal).ToArray()
            : Array.Empty<string>();

    /// <summary>Clips newest first: text, or "IMG:n" for images made by <see cref="Png"/>.</summary>
    public static string[] Order(ClipHistoryStore s) =>
        s.PickerEntries()
            .Select(e => e.IsImage ? "IMG:" + ImageId(e.ImagePng!) : e.TryGetNonSecretText()!)
            .ToArray();

    /// <summary>Throwaway bytes that look like a PNG header plus an id.</summary>
    public static byte[] Png(int id)
    {
        var b = new byte[64];
        new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }.CopyTo(b, 0);
        BitConverter.GetBytes(id).CopyTo(b, 8);
        return b;
    }

    public static int ImageId(byte[] png) => BitConverter.ToInt32(png, 8);

    public static ClipEntry? Find(ClipHistoryStore s, string text) =>
        s.PickerEntries().FirstOrDefault(e => e.TryGetNonSecretText() == text);

    public static ClipEntry? FindImage(ClipHistoryStore s, int id) =>
        s.PickerEntries().FirstOrDefault(e => e.IsImage && ImageId(e.ImagePng!) == id);

    public void Dispose()
    {
        SettingsManager.DirOverride = null;
        try { Directory.Delete(Root, recursive: true); } catch { }
    }
}