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

    // Keyboard-focus HWND inside the foreground window's thread, or zero when
    // unknown. Browsers and other single-HWND apps keep one HWND for all page
    // fields, so this detects window/control-level moves only, not DOM focus.
    IntPtr FocusedControl => IntPtr.Zero;
}

internal sealed class Win32InputSink : IInputSink, IFocusProbe
{
    public IntPtr ForegroundWindow => NativeMethods.GetForegroundWindow();

    public IntPtr FocusedControl => FocusedControlOf(NativeMethods.GetForegroundWindow());

    /// <summary>Keyboard-focus HWND of the thread that owns <paramref name="window"/>.</summary>
    internal static IntPtr FocusedControlOf(IntPtr window)
    {
        if (window == IntPtr.Zero) return IntPtr.Zero;
        var thread = NativeMethods.GetWindowThreadProcessId(window, out _);
        var info = new NativeMethods.GUITHREADINFO { cbSize = Marshal.SizeOf<NativeMethods.GUITHREADINFO>() };
        return thread != 0 && NativeMethods.GetGUIThreadInfo(thread, ref info) ? info.hwndFocus : IntPtr.Zero;
    }

    public int Send(string text)
    {
        // RichEdit and browser textarea editors discard Unicode CR/Tab packets,
        // so CR and Tab go as ordinary VK key presses. Every segment is its own
        // SendInput call, in text order: mixing VK and Unicode packets in one
        // call can leave only a key-up with no visible key-down.
        var accepted = 0;
        foreach (var segment in Segments(text))
        {
            var inputs = new NativeMethods.INPUT[segment.Text.Length * 2];
            for (var i = 0; i < segment.Text.Length; i++)
            {
                var c = segment.Text[i];
                if (segment.IsKey)
                {
                    var vk = c == '\r' ? (ushort)0x0D : (ushort)0x09;
                    inputs[i * 2] = Key(vk, 0, 0);
                    inputs[i * 2 + 1] = Key(vk, 0, NativeMethods.KeyeventfKeyup);
                }
                else
                {
                    inputs[i * 2] = Key(0, c, NativeMethods.KeyeventfUnicode);
                    inputs[i * 2 + 1] = Key(0, c, NativeMethods.KeyeventfUnicode | NativeMethods.KeyeventfKeyup);
                }
            }
            var sent = (int)NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
            accepted += sent;
            if (sent != inputs.Length) return accepted;
        }
        return accepted;
    }

    /// <summary>
    /// Splits a chunk into ordered runs: CR/Tab become key-press segments (one
    /// per character), everything else stays in Unicode-packet runs.
    /// </summary>
    internal static IReadOnlyList<(bool IsKey, string Text)> Segments(string text)
    {
        var result = new List<(bool, string)>();
        var run = 0;
        for (var i = 0; i < text.Length; i++)
        {
            if (text[i] is not ('\r' or '\t')) continue;
            if (i > run) result.Add((false, text[run..i]));
            result.Add((true, text[i].ToString()));
            run = i + 1;
        }
        if (run < text.Length) result.Add((false, text[run..]));
        return result;
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

    /// <summary>
    /// Shown before arming/sending an entry that contains Tab or a line break.
    /// Those are sent as real key presses, which the target may treat as
    /// navigation or submit rather than text.
    /// </summary>
    public const string TargetKeyWarning =
        "Warning: contains Tab or Enter. They are sent as real key presses, so the target may treat them as navigation or submit (for example, Tab moves focus in browsers).";

    public const string ArmedTargetKeyWarning =
        "Armed. Tab/Enter are real keys; may move focus or submit. Go types, Esc cancels.";

    public static bool HasTargetKeys(string? text) =>
        !string.IsNullOrEmpty(text) && text.AsSpan().IndexOfAny('\t', '\r', '\n') >= 0;

    public static bool IsTargetKey(char ch) => ch is '\r' or '\t';

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
        var keyPresses = 0;
        var focusAtStart = _focus.FocusedControl;
        while (offset < normalized.Length)
        {
            if (token.IsCancellationRequested) return Result(TypeOutStatus.Cancelled, "Type-out cancelled.", sentUnits, normalized.Length);
            if (!sourceIsLive()) return token.IsCancellationRequested
                ? Result(TypeOutStatus.Cancelled, "Type-out cancelled.", sentUnits, normalized.Length)
                : Result(TypeOutStatus.SourceGone, "Type-out stopped because the entry was deleted, expired, or cleared.", sentUnits, normalized.Length);
            if (token.IsCancellationRequested) return Result(TypeOutStatus.Cancelled, "Type-out cancelled.", sentUnits, normalized.Length);
            var foreground = _focus.ForegroundWindow;
            if (foreground == IntPtr.Zero || foreground != target) return Result(TypeOutStatus.FocusChanged, "Type-out stopped because focus changed.", sentUnits, normalized.Length);
            if (focusAtStart != IntPtr.Zero && _focus.FocusedControl != focusAtStart)
                return Result(TypeOutStatus.FocusChanged, "Type-out stopped because focus moved to another control.", sentUnits, normalized.Length);
            if (token.IsCancellationRequested) return Result(TypeOutStatus.Cancelled, "Type-out cancelled.", sentUnits, normalized.Length);

            // A surrogate pair stays within a chunk. SendInput sends this
            // bounded batch atomically; each code unit has a down and up event.
            var count = Math.Min(_chunkSize, normalized.Length - offset);
            if (offset + count < normalized.Length && char.IsHighSurrogate(normalized[offset + count - 1])
                && char.IsLowSurrogate(normalized[offset + count]))
                count--;
            if (count <= 0)
                return new(TypeOutStatus.PartialSend, "The typing chunk size cannot hold a surrogate pair. No text was sent.");
            // End a chunk right after a Tab/Enter key press so the focus checks
            // run before any following text is sent.
            for (var k = 0; k < count; k++)
            {
                if (!TypeOutText.IsTargetKey(normalized[offset + k])) continue;
                count = k + 1;
                break;
            }
            var chunk = normalized.Substring(offset, count);
            var sentEvents = _sink.Send(chunk);
            var acceptedUnits = Math.Clamp(sentEvents / 2, 0, count);

            if (acceptedUnits < count)
            {
                return new(TypeOutStatus.PartialSend, "Windows did not accept all input. About " + (sentUnits + acceptedUnits) + " of " + normalized.Length + " UTF-16 units may have been inserted; type-out stopped and no total success is claimed.");
            }
            for (var k = 0; k < count; k++)
                if (TypeOutText.IsTargetKey(chunk[k])) keyPresses++;
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
        return new(TypeOutStatus.Completed, CompletedMessage(sentUnits, keyPresses));
    }

    /// <summary>
    /// Completion only means Windows accepted the input; the target's result is
    /// not observed, so never claim the text was inserted.
    /// </summary>
    public static string CompletedMessage(int sent, int keyPresses) =>
        "Type-out sent " + sent + " of " + sent + " keys" + (keyPresses > 0 ? " (" + keyPresses + " Tab/Enter)" : string.Empty) + ". Insertion not verified.";

    private static TypeOutResult Result(TypeOutStatus status, string message, int sent, int total) =>
        new(status, message + " " + (sent == 0 ? "No text was sent." : "About " + sent + " of " + total + " UTF-16 units were sent."));
}
