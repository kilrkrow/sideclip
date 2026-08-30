using System.Runtime.InteropServices;

namespace Sideclip.Native;

internal static class InputSender
{
    public static bool TypeText(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        ReleaseModifiers();
        Thread.Sleep(40);

        var inputs = new NativeMethods.INPUT[text.Length * 2];
        var i = 0;
        foreach (var ch in text)
        {
            inputs[i++] = Key(0, ch, NativeMethods.KeyeventfUnicode);
            inputs[i++] = Key(0, ch, NativeMethods.KeyeventfUnicode | NativeMethods.KeyeventfKeyup);
        }

        var sent = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
        return sent == (uint)inputs.Length;
    }

    public static void ReleaseModifiers()
    {
        ushort[] keys =
        {
            NativeMethods.VkControl,
            NativeMethods.VkLcontrol,
            NativeMethods.VkRcontrol,
            NativeMethods.VkShift,
            NativeMethods.VkLshift,
            NativeMethods.VkRshift,
            NativeMethods.VkLwin,
            NativeMethods.VkRwin
        };

        var inputs = new NativeMethods.INPUT[keys.Length];
        for (var i = 0; i < keys.Length; i++)
            inputs[i] = Key(keys[i], 0, NativeMethods.KeyeventfKeyup);

        _ = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    public static void CtrlV()
    {
        ReleaseModifiers();
        Thread.Sleep(40);
        var inputs = new[]
        {
            Key(NativeMethods.VkControl, 0, 0),
            Key(NativeMethods.VkV, 0, 0),
            Key(NativeMethods.VkV, 0, NativeMethods.KeyeventfKeyup),
            Key(NativeMethods.VkControl, 0, NativeMethods.KeyeventfKeyup)
        };
        _ = NativeMethods.SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<NativeMethods.INPUT>());
    }

    public static bool FocusWindow(IntPtr hwnd)
    {
        if (hwnd == IntPtr.Zero)
            return false;

        var fg = NativeMethods.GetForegroundWindow();
        var fgTid = NativeMethods.GetWindowThreadProcessId(fg, out _);
        var toTid = NativeMethods.GetWindowThreadProcessId(hwnd, out _);
        var cur = NativeMethods.GetCurrentThreadId();

        if (fgTid != 0 && fgTid != cur)
            NativeMethods.AttachThreadInput(cur, fgTid, true);
        if (toTid != 0 && toTid != cur && toTid != fgTid)
            NativeMethods.AttachThreadInput(cur, toTid, true);

        var ok = NativeMethods.SetForegroundWindow(hwnd);

        if (toTid != 0 && toTid != cur && toTid != fgTid)
            NativeMethods.AttachThreadInput(cur, toTid, false);
        if (fgTid != 0 && fgTid != cur)
            NativeMethods.AttachThreadInput(cur, fgTid, false);

        return ok;
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
