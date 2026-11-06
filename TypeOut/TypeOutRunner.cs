using System.Runtime.InteropServices;
using Sideclip.Native;

namespace Sideclip.TypeOut;

public interface IInputSink
{
    // Returns how many UTF-16 input events SendInput accepted.
    int Send(string text);
}

public interface IFocusProbe
{
    IntPtr ForegroundWindow { get; }
}

internal sealed class Win32InputSink : IInputSink, IFocusProbe
{
    public IntPtr ForegroundWindow => NativeMethods.GetForegroundWindow();

    public int Send(string text)
    {
        var inputs = new NativeMethods.INPUT[text.Length * 2];
        for (var i = 0; i < text.Length; i++)
        {
            inputs[i * 2] = Key(0, text[i], NativeMethods.KeyeventfUnicode);
            inputs[i * 2 + 1] = Key(0, text[i], NativeMethods.KeyeventfUnicode | NativeMethods.KeyeventfKeyup);
        }
        return (int)NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    private static NativeMethods.INPUT Key(ushort vk, int scan, uint flags) => new()
    {
        type = NativeMethods.InputKeyboard,
        U = new NativeMethods.InputUnion
        {
            ki = new NativeMethods.KEYBDINPUT
            {
                wVk = vk,
                wScan = (ushort)scan,
                dwFlags = flags,
                time = 0,
                dwExtraInfo = IntPtr.Zero
            }
        }
    };
}

public static class TypeOutText
{
    public const int MaxLength = 1_000_000;

    public static string? Validate(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "Only non-empty text entries can be typed.";
        if (text.Length > MaxLength) return "This entry is too long to type safely (one million UTF-16 units maximum).";
        for (var i = 0; i < text.Length; i++)
        {
            var ch = text[i];
            if (ch == '\0' || ch < 0x20 && ch is not '\r' and not '\n' and not '\t')
                return "NUL and control characters other than CR, LF, and Tab are not supported.";
            if (char.IsHighSurrogate(ch))
            {
                if (i + 1 >= text.Length || !char.IsLowSurrogate(text[i + 1])) return "Malformed UTF-16 is not supported.";
                i++;
            }
            else if (char.IsLowSurrogate(ch)) return "Malformed UTF-16 is not supported.";
        }
        return null;
    }

    public static string? ValidateEntryBuffer(char[] text) => Validate(new string(text));

    public static string Normalize(string text) =>
        text.Replace("\r\n", "\r", StringComparison.Ordinal).Replace('\n', '\r');
}

public enum TypeOutStatus { Completed, Cancelled, SourceGone, FocusChanged, PartialSend }
public sealed record TypeOutResult(TypeOutStatus Status, string Message);

public sealed class TypeOutRunner
{
    private readonly IInputSink _sink;
    private readonly IFocusProbe _focus;
    private readonly Func<int, CancellationToken, Task> _delay;
    private readonly int _chunkSize;
    public const int MaxChunkUnits = 64;

    public TypeOutRunner(IInputSink sink, IFocusProbe focus, Func<int, CancellationToken, Task>? delay = null, int chunkSize = 16)
    {
        _sink = sink;
        _focus = focus;
        _delay = delay ?? ((ms, token) => Task.Delay(ms, token));
        _chunkSize = Math.Clamp(chunkSize, 2, MaxChunkUnits);
    }

    public async Task<TypeOutResult> RunAsync(char[] payload, Func<bool> sourceIsLive, IntPtr target, int delayMilliseconds, CancellationToken token)
    {
        if (target == IntPtr.Zero) return new(TypeOutStatus.FocusChanged, "Type-out stopped: there is no destination window. No text was sent.");
        string normalized;
        try
        {
            var validation = TypeOutText.ValidateEntryBuffer(payload);
            if (validation is not null) return new(TypeOutStatus.PartialSend, "Type-out did not start: " + validation + " No text was sent.");
            normalized = TypeOutText.Normalize(new string(payload));
        }
        finally { Array.Clear(payload, 0, payload.Length); }
        if (normalized.Length > TypeOutText.MaxLength) return new(TypeOutStatus.PartialSend, "Type-out did not start: entry too large. No text was sent.");
        var sentUnits = 0;
        var offset = 0;
        while (offset < normalized.Length)
        {
            if (token.IsCancellationRequested) return Result(TypeOutStatus.Cancelled, "Type-out cancelled.", sentUnits, normalized.Length);
            if (!sourceIsLive()) return Result(TypeOutStatus.SourceGone, "Type-out stopped because the entry was deleted, expired, or cleared.", sentUnits, normalized.Length);
            var foreground = _focus.ForegroundWindow;
            if (foreground == IntPtr.Zero || foreground != target) return Result(TypeOutStatus.FocusChanged, "Type-out stopped because focus changed.", sentUnits, normalized.Length);

            // A surrogate pair stays within a chunk. SendInput sends this
            // bounded batch atomically; each code unit has a down and up event.
            var count = Math.Min(_chunkSize, normalized.Length - offset);
            if (offset + count < normalized.Length && char.IsHighSurrogate(normalized[offset + count - 1])) count--;
            var chunk = normalized.Substring(offset, count);
            var sentEvents = _sink.Send(chunk);
            var acceptedUnits = Math.Clamp(sentEvents / 2, 0, count);

            if (acceptedUnits < count)
            {
                return new(TypeOutStatus.PartialSend, "Windows did not accept all input. About " + (sentUnits + acceptedUnits) + " of " + normalized.Length + " UTF-16 units may have been inserted; type-out stopped and no total success is claimed.");
            }
            offset += count;
            sentUnits += count;
            if (offset < normalized.Length && delayMilliseconds > 0)
            {
                try { await _delay(Math.Clamp(delayMilliseconds, 0, 250), token).ConfigureAwait(false); }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    return Result(TypeOutStatus.Cancelled, "Type-out cancelled.", sentUnits, normalized.Length);
                }
            }
        }
        return new(TypeOutStatus.Completed, "Type-out finished.");
    }

    private static TypeOutResult Result(TypeOutStatus status, string message, int sent, int total) =>
        new(status, message + " " + (sent == 0 ? "No text was sent." : "About " + sent + " of " + total + " UTF-16 units were sent."));
}
