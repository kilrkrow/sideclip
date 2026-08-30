using System.Windows.Media.Imaging;

namespace Sideclip.History;

/// <summary>
/// One in-memory clipboard row. Secret payloads live only in a char[] so they
/// can be overwritten on TTL; nothing secret is written to disk.
/// </summary>
public sealed class ClipEntry
{
    public DateTime CopiedAt { get; }
    public bool IsSecret { get; }
    public string OwnerExe { get; }
    public byte[]? ImagePng { get; }
    public bool IsImage => ImagePng is { Length: > 0 };

    private char[]? _payload;

    public ClipEntry(string text, bool isSecret, string ownerExe, byte[]? imagePng = null)
    {
        CopiedAt = DateTime.UtcNow;
        IsSecret = isSecret;
        OwnerExe = ownerExe;
        ImagePng = imagePng;
        _payload = string.IsNullOrEmpty(text) ? null : text.ToCharArray();
    }

    public bool HasPayload => _payload is { Length: > 0 } || IsImage;

    public string? TryGetNonSecretText()
    {
        if (IsSecret || _payload is null)
            return null;
        return new string(_payload);
    }

    public bool PayloadEquals(string? live)
    {
        if (_payload is null || live is null)
            return false;
        if (live.Length != _payload.Length)
            return false;
        for (var i = 0; i < _payload.Length; i++)
        {
            if (live[i] != _payload[i])
                return false;
        }

        return true;
    }

    public void WipePayload()
    {
        if (_payload is null)
            return;
        Array.Clear(_payload, 0, _payload.Length);
        _payload = null;
    }

    public BitmapImage? TryDecodeImage()
    {
        if (ImagePng is null || ImagePng.Length == 0)
            return null;
        try
        {
            var bmp = new BitmapImage();
            using var ms = new System.IO.MemoryStream(ImagePng);
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            return bmp;
        }
        catch
        {
            return null;
        }
    }

    public static string MakeNonSecretPreview(string text)
    {
        const int max = 48;
        var oneLine = text.Replace('\r', ' ').Replace('\n', ' ');
        return oneLine.Length <= max ? oneLine : string.Concat(oneLine.AsSpan(0, max), "...");
    }

    public static string MakeListLine(string text)
    {
        const int max = 120;
        var oneLine = text.Replace('\r', ' ').Replace('\n', ' ');
        while (oneLine.Contains("  ", StringComparison.Ordinal))
            oneLine = oneLine.Replace("  ", " ", StringComparison.Ordinal);
        oneLine = oneLine.Trim();
        return oneLine.Length <= max ? oneLine : string.Concat(oneLine.AsSpan(0, max), "...");
    }
}

