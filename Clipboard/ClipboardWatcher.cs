using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Sideclip.Classification;
using Sideclip.History;
using Sideclip.Native;

namespace Sideclip.Clipboard;

/// <summary>
/// Hidden top-level HWND + AddClipboardFormatListener. HWND_MESSAGE does not
/// receive WM_CLIPBOARDUPDATE. Does not register Win+V and does not read
/// Windows clipboard history.
/// </summary>
public sealed class ClipboardWatcher : IDisposable
{
    private HwndSource? _source;
    private DateTime _suppressUntil;
    private bool _disposed;
    private DateTime _lastAcceptedUtc;
    private int _lastAcceptedLen;
    private int _lastAcceptedHash;
    private int _lastImageHash;
    private bool _lastWasImage;

    public event Action<string, bool, string>? ClipCaptured;
    public event Action<byte[], string>? ImageCaptured;

    public void Start()
    {
        // Same pattern as HotkeyListener: real off-screen WS_POPUP.
        // Parenting to HWND_MESSAGE (-3) makes AddClipboardFormatListener a no-op.
        var parameters = new HwndSourceParameters("SideclipClipboardListener")
        {
            Width = 1,
            Height = 1,
            PositionX = -32000,
            PositionY = -32000,
            WindowStyle = unchecked((int)0x80000000), // WS_POPUP
            ExtendedWindowStyle = 0x08000080 // WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW
        };

        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
        if (!NativeMethods.AddClipboardFormatListener(_source.Handle))
            throw new InvalidOperationException("AddClipboardFormatListener failed.");
    }

    public IntPtr Handle => _source?.Handle ?? IntPtr.Zero;

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmClipboardUpdate)
        {
            handled = true;
            // Return from WM_CLIPBOARDUPDATE before OpenClipboard; the owner
            // (Sublime, etc.) often still holds it during the notify.
            var disp = _source?.Dispatcher;
            if (disp is not null)
                disp.BeginInvoke(DispatcherPriority.Background, new Action(OnClipboardUpdate));
            else
                OnClipboardUpdate();
        }

        return IntPtr.Zero;
    }

    private void OnClipboardUpdate()
    {
        if (DateTime.UtcNow < _suppressUntil)
            return;

        if (TryGetClipboardImagePng(out var png))
        {
            var imgHash = HashPng(png);
            if (_lastWasImage
                && imgHash == _lastImageHash
                && (DateTime.UtcNow - _lastAcceptedUtc).TotalMilliseconds < 800)
                return;

            var ownerKnown = ClipboardOwnerResolver.TryGetOwnerExeName(out var exe);
            var owner = ownerKnown && !string.IsNullOrEmpty(exe) ? exe! : "unknown";
            ImageCaptured?.Invoke(png, owner);
            _lastAcceptedLen = png.Length;
            _lastAcceptedHash = imgHash;
            _lastImageHash = imgHash;
            _lastWasImage = true;
            _lastAcceptedUtc = DateTime.UtcNow;
            return;
        }

        if (!TryGetClipboardText(out var text))
            return;

        var hash = text.GetHashCode();
        if (text.Length == _lastAcceptedLen
            && hash == _lastAcceptedHash
            && (DateTime.UtcNow - _lastAcceptedUtc).TotalMilliseconds < 400)
            return;

        if (_lastWasImage
            && (DateTime.UtcNow - _lastAcceptedUtc).TotalMilliseconds < 2000
            && LooksLikeScreenshotPath(text))
            return;

        var ownerKnown2 = ClipboardOwnerResolver.TryGetOwnerExeName(out var exe2);
        var fgKnown = ClipboardOwnerResolver.TryGetForegroundExeName(out var fg);
        var owner2 = ownerKnown2 && !string.IsNullOrEmpty(exe2)
            ? exe2!
            : (fgKnown && !string.IsNullOrEmpty(fg) ? fg + " (fg)" : "unknown");
        var fromOnePassword = ClipboardOwnerResolver.IsOnePasswordSource(
            ownerKnown2 ? exe2 : null,
            fgKnown ? fg : null);
        var secret = fromOnePassword || SecretClassifier.LooksLikePassword(text);

        _lastAcceptedLen = text.Length;
        _lastAcceptedHash = hash;
        _lastAcceptedUtc = DateTime.UtcNow;
        _lastWasImage = false;

        ClipCaptured?.Invoke(text, secret, owner2);
    }

    public void SetTextSuppressing(string text)
    {
        try
        {
            _suppressUntil = DateTime.UtcNow.AddMilliseconds(800);
            _lastAcceptedLen = text.Length;
            _lastAcceptedHash = text.GetHashCode();
            _lastAcceptedUtc = DateTime.UtcNow;
            System.Windows.Clipboard.SetText(text);
        }
        catch
        {
            _suppressUntil = DateTime.MinValue;
        }
    }

    public void SetImageSuppressing(byte[] png)
    {
        try
        {
            var bmp = new BitmapImage();
            using var ms = new MemoryStream(png);
            bmp.BeginInit();
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.StreamSource = ms;
            bmp.EndInit();
            bmp.Freeze();
            _suppressUntil = DateTime.UtcNow.AddMilliseconds(800);
            System.Windows.Clipboard.SetImage(bmp);
        }
        catch
        {
            _suppressUntil = DateTime.MinValue;
        }
    }

    public bool ClearLiveIfMatches(ClipEntry entry)
    {
        try
        {
            if (!TryGetClipboardText(out var live))
                return false;
            if (!entry.PayloadEquals(live))
                return false;

            _suppressUntil = DateTime.UtcNow.AddMilliseconds(800);
            System.Windows.Clipboard.Clear();
            _lastAcceptedLen = 0;
            _lastAcceptedHash = 0;
            return true;
        }
        catch
        {
            _suppressUntil = DateTime.MinValue;
            return false;
        }
    }

    private static bool TryGetClipboardText(out string text)
    {
        text = string.Empty;
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                if (!System.Windows.Clipboard.ContainsText())
                {
                    Thread.Sleep(25);
                    continue;
                }
                text = System.Windows.Clipboard.GetText();
                return !string.IsNullOrEmpty(text);
            }
            catch (COMException)
            {
                Thread.Sleep(25);
            }
        }

        return false;
    }

    private static bool TryGetClipboardImagePng(out byte[] png)
    {
        png = Array.Empty<byte>();
        for (var attempt = 0; attempt < 8; attempt++)
        {
            try
            {
                if (!System.Windows.Clipboard.ContainsImage())
                    return false;
                var src = System.Windows.Clipboard.GetImage();
                if (src is null)
                    return false;

                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(src));
                using var ms = new MemoryStream();
                encoder.Save(ms);
                png = ms.ToArray();
                return png.Length > 0;
            }
            catch (COMException)
            {
                Thread.Sleep(25);
            }
        }

        return false;
    }

    private static int HashPng(byte[] png)
    {
        var h = new HashCode();
        h.Add(png.Length);
        var n = Math.Min(64, png.Length);
        for (var i = 0; i < n; i++)
            h.Add(png[i]);
        for (var i = Math.Max(n, png.Length - 64); i < png.Length; i++)
            h.Add(png[i]);
        return h.ToHashCode();
    }

    private static bool LooksLikeScreenshotPath(string text)
    {
        var t = text.Trim().Trim('"');
        if (t.IndexOf("\\Screenshots\\", StringComparison.OrdinalIgnoreCase) < 0
            && t.IndexOf("/Screenshots/", StringComparison.OrdinalIgnoreCase) < 0)
            return false;
        return t.EndsWith(".png", StringComparison.OrdinalIgnoreCase)
               || t.EndsWith(".jpg", StringComparison.OrdinalIgnoreCase)
               || t.EndsWith(".jpeg", StringComparison.OrdinalIgnoreCase);
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;

        if (_source is not null)
        {
            NativeMethods.RemoveClipboardFormatListener(_source.Handle);
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }

        _lastAcceptedLen = 0;
        _lastAcceptedHash = 0;
    }
}
