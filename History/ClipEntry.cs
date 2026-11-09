using System.Windows.Media.Imaging;

namespace Sideclip.History;

/// <summary>
/// One in-memory clipboard row. Secret payloads live only in a char[] so they
/// can be overwritten on TTL; nothing secret is written to disk.
/// </summary>
public sealed class ClipEntry
{
    public DateTime CopiedAt { get; }
    public bool IsSecret { get; private set; }

    public string OwnerExe { get; }
    public byte[]? ImagePng { get; }
    public bool IsImage => ImagePng is { Length: > 0 };

    private readonly object _payloadGate = new();
    private char[]? _payload;

    public ClipEntry(string text, bool isSecret, string ownerExe, byte[]? imagePng = null)
    {
        CopiedAt = DateTime.UtcNow;
        IsSecret = isSecret;
        OwnerExe = ownerExe;
        ImagePng = imagePng;
        _payload = string.IsNullOrEmpty(text) ? null : text.ToCharArray();
    }

    public bool HasPayload { get { lock (_payloadGate) return _payload is { Length: > 0 } || IsImage; } }

    public string? TryGetText()
    {
        lock (_payloadGate)
        {
            if (_payload is null) return null;
            return new string(_payload);
        }
    }
    // Only the running job may own this bounded wipeable copy.
    internal char[]? CopyTypeOutPayload()
    {
        lock (_payloadGate)
        {
            if (IsImage || _payload is not { Length: > 0 } || _payload.Length > TypeOut.TypeOutText.MaxLength) return null;
            return (char[])_payload.Clone();
        }
    }
    // Kept for callers that explicitly want null on a secret entry.
    public string? TryGetNonSecretText() => IsSecret ? null : TryGetText();

    public bool PayloadEquals(string? live)
    {
        lock (_payloadGate)
        {
            if (_payload is null || live is null || live.Length != _payload.Length) return false;
            return _payload.AsSpan().SequenceEqual(live.AsSpan());
        }
    }
    public void WipePayload()
    {
        lock (_payloadGate)
        {
            if (_payload is null) return;
            Array.Clear(_payload, 0, _payload.Length);
            _payload = null;
        }
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
