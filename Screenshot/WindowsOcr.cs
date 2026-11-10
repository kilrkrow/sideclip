using System.IO;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;
using Windows.Storage;
using Windows.Storage.Streams;

namespace Sideclip.Screenshot;

/// <summary>
/// Built-in Windows OCR (Windows.Media.Ocr), using the user's profile languages.
/// Optional: any failure returns null and the caller carries on without text.
/// </summary>
internal static class WindowsOcr
{
    private static readonly Lazy<bool> Available = new(() =>
    {
        try
        {
            return OcrEngine.TryCreateFromUserProfileLanguages() is not null;
        }
        catch
        {
            return false;
        }
    });

    /// <summary>False when Windows has no OCR language installed for the user's languages.</summary>
    public static bool IsAvailable => Available.Value;

    public static Task<string?> RecognizeAsync(string imagePath, CancellationToken ct) =>
        // Off the caller's thread: WinRT decoding and recognition can take a moment on big captures.
        Task.Run(() => RecognizeCoreAsync(imagePath, ct), ct);

    private static async Task<string?> RecognizeCoreAsync(string imagePath, CancellationToken ct)
    {
        var engine = OcrEngine.TryCreateFromUserProfileLanguages();
        if (engine is null)
            return null;

        var file = await StorageFile.GetFileFromPathAsync(Path.GetFullPath(imagePath)).AsTask(ct).ConfigureAwait(false);
        using IRandomAccessStream stream = await file.OpenAsync(FileAccessMode.Read).AsTask(ct).ConfigureAwait(false);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask(ct).ConfigureAwait(false);

        var max = OcrEngine.MaxImageDimension;
        var transform = new BitmapTransform();
        var w = decoder.PixelWidth;
        var h = decoder.PixelHeight;
        if (w > max || h > max)
        {
            var scale = Math.Min((double)max / w, (double)max / h);
            transform.ScaledWidth = (uint)Math.Max(1, Math.Floor(w * scale));
            transform.ScaledHeight = (uint)Math.Max(1, Math.Floor(h * scale));
        }

        using var bitmap = await decoder.GetSoftwareBitmapAsync(
            BitmapPixelFormat.Bgra8,
            BitmapAlphaMode.Premultiplied,
            transform,
            ExifOrientationMode.RespectExifOrientation,
            ColorManagementMode.DoNotColorManage).AsTask(ct).ConfigureAwait(false);

        ct.ThrowIfCancellationRequested();
        var result = await engine.RecognizeAsync(bitmap).AsTask(ct).ConfigureAwait(false);
        var lines = result.Lines.Select(l => l.Text).Where(t => !string.IsNullOrWhiteSpace(t)).ToList();
        return lines.Count == 0 ? null : string.Join("\n", lines);
    }
}
