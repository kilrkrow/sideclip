using System.Runtime.InteropServices;
using System.Windows.Interop;
using Sideclip.Native;
using Sideclip.Settings;

namespace Sideclip.Hotkeys;

internal sealed class HotkeyListener : IDisposable
{
    private HwndSource? _source;
    private bool _disposed, _activeEscape;
    private HotkeyAssignments? _current;
    public event Action? PickerRequested, ScreenshotPathRequested, TypeOutGoRequested, TypeOutCancelRequested;
    public event Action? RegistrationChanged;
    public string? LastError { get; private set; }
    public bool PickerRegistered { get; private set; }
    public bool ScreenshotRegistered { get; private set; }
    public bool TypeOutRegistered { get; private set; }
    public bool EscapeRegistered => _activeEscape;

    public void Start()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(HotkeyListener));
        if (_source is not null) return;
        _source = new HwndSource(new HwndSourceParameters("SideclipHotkeys") { Width = 1, Height = 1, PositionX = -32000, PositionY = -32000, WindowStyle = unchecked((int)0x80000000) });
        _source.AddHook(WndProc);
    }

    public void Apply(string? picker, string? screenshot, string? typeOutGo = null, bool typeOutEnabled = false) =>
        TryApply(picker, screenshot, typeOutGo, typeOutEnabled);

    public bool TryApply(string? picker, string? screenshot, string? typeOutGo, bool typeOutEnabled)
    {
        if (_source is null) Start();
        if (!HotkeyAssignments.TryCreate(picker, screenshot, typeOutGo, typeOutEnabled, out var next, out var validation))
        { LastError = validation; RegistrationChanged?.Invoke(); return false; }
        if (_current == next) { LastError = null; RegistrationChanged?.Invoke(); return true; }
        var previous = _current;
        ReleaseAssignments();
        if (RegisterAssignments(next, out var failure))
        { _current = next; LastError = null; RegistrationChanged?.Invoke(); return true; }
        ReleaseAssignments();
        var restored = previous is null || RegisterAssignments(previous.Value, out _);
        if (!restored) { ReleaseAssignments(); _current = null; }
        LastError = failure + (previous is null ? " No assignments acquired." : restored ? " Previous assignments kept." : " Previous assignments could not be restored; shortcuts are unavailable.");
        RegistrationChanged?.Invoke();
        return false;
    }

    private bool RegisterAssignments(HotkeyAssignments values, out string? error)
    {
        error = null;
        bool Register(string chord, int id)
        {
            if (chord.Length == 0) return true;
            HotkeyParser.TryParse(chord, out var mods, out var vk);
            var registered = id == NativeMethods.HotkeyTypeOutGo
                ? NativeMethods.RegisterHotKey(_source!.Handle, NativeMethods.HotkeyTypeOutGo, mods | NativeMethods.ModNorepeat, vk)
                : NativeMethods.RegisterHotKey(_source!.Handle, id, mods | NativeMethods.ModNorepeat, vk);
            if (id == NativeMethods.HotkeyPicker) PickerRegistered = registered;
            else if (id == NativeMethods.HotkeyScreenshot) ScreenshotRegistered = registered;
            else TypeOutRegistered = registered;
            return registered;
        }
        foreach (var (chord, id, name) in new[] { (values.Picker, NativeMethods.HotkeyPicker, "Picker"), (values.Screenshot, NativeMethods.HotkeyScreenshot, "Screenshot path"), (values.Enabled ? values.TypeOut : "", NativeMethods.HotkeyTypeOutGo, "Type-out shortcut") })
            if (!Register(chord, id)) { error = name + " is unavailable: already taken or invalid (win32 " + Marshal.GetLastWin32Error() + ")."; return false; }
        return true;
    }

    // Compatibility API: enabled trigger lifetime is independent of pending/run Esc.
    public void SetTypeOutActive(bool active, string? go)
    {
        var current = _current ?? new HotkeyAssignments("", "", "", false);
        TryApply(current.Picker, current.Screenshot, go, active);
    }

    public bool SetEscapeActive(bool active)
    {
        if (_source is null) return false;
        if (_activeEscape == active) return true;
        if (!active) { Unregister(NativeMethods.HotkeyEscape); _activeEscape = false; return true; }
        _activeEscape = NativeMethods.RegisterHotKey(_source.Handle, NativeMethods.HotkeyEscape, NativeMethods.ModNorepeat, NativeMethods.VkEscape);
        if (!_activeEscape) LastError = "Esc cancellation unavailable (win32 " + Marshal.GetLastWin32Error() + ").";
        return _activeEscape;
    }

    private void ReleaseAssignments()
    {
        Unregister(NativeMethods.HotkeyPicker); Unregister(NativeMethods.HotkeyScreenshot); Unregister(NativeMethods.HotkeyTypeOutGo);
        SetEscapeActive(false);
        PickerRegistered = ScreenshotRegistered = TypeOutRegistered = false;
    }
    private void Unregister(int id) { if (_source is not null) NativeMethods.UnregisterHotKey(_source.Handle, id); }
    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg != NativeMethods.WmHotkey) return IntPtr.Zero;
        handled = true;
        var id = wParam.ToInt32();
        if (id == NativeMethods.HotkeyPicker && PickerRegistered) PickerRequested?.Invoke();
        else if (id == NativeMethods.HotkeyScreenshot && ScreenshotRegistered) ScreenshotPathRequested?.Invoke();
        else if (id == NativeMethods.HotkeyTypeOutGo && TypeOutRegistered) TypeOutGoRequested?.Invoke();
        else if (id == NativeMethods.HotkeyEscape && EscapeRegistered) TypeOutCancelRequested?.Invoke();
        return IntPtr.Zero;
    }
    public void Dispose()
    {
        if (_disposed) return;
        ReleaseAssignments(); _current = null; _disposed = true;
        if (_source is not null) { _source.RemoveHook(WndProc); _source.Dispose(); _source = null; }
    }
}
