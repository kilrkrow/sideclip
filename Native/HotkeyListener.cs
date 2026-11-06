using System.Runtime.InteropServices;
using System.Windows.Interop;
using Sideclip.Native;

namespace Sideclip.Hotkeys;

internal sealed class HotkeyListener : IDisposable
{
    private HwndSource? _source;
    private bool _disposed;
    private bool _activeEscape;

    public event Action? PickerRequested;
    public event Action? ScreenshotPathRequested;
    public event Action? TypeOutGoRequested;
    public event Action? TypeOutCancelRequested;

    public string? LastError { get; private set; }
    public bool PickerRegistered { get; private set; }
    public bool ScreenshotRegistered { get; private set; }
    public bool TypeOutRegistered { get; private set; }
    public bool EscapeRegistered => _activeEscape;

    public void Start()
    {
        if (_source is not null) return;
        _source = new HwndSource(new HwndSourceParameters("SideclipHotkeys")
        {
            Width = 1,
            Height = 1,
            PositionX = -32000,
            PositionY = -32000,
            WindowStyle = unchecked((int)0x80000000)
        });
        _source.AddHook(WndProc);
    }

    public void Apply(string? picker, string? screenshot, string? typeOutGo = null, bool typeOutActive = false)
    {
        if (_source is null) Start();
        if (_source is null) return;
        LastError = null;
        Unregister(NativeMethods.HotkeyPicker);
        Unregister(NativeMethods.HotkeyScreenshot);
        PickerRegistered = ScreenshotRegistered = false;
        if (HotkeyParser.IsForbidden(picker)) LastError = "The picker hotkey is reserved by Windows.";
        else if (HotkeyParser.TryParse(picker, out var pMods, out var pVk))
        {
            PickerRegistered = NativeMethods.RegisterHotKey(_source.Handle, NativeMethods.HotkeyPicker, pMods, pVk);
            if (!PickerRegistered) LastError = (picker ?? "picker") + " is already taken or invalid (win32 " + Marshal.GetLastWin32Error() + ").";
        }
        if (HotkeyParser.IsForbidden(screenshot)) LastError = (LastError + " The screenshot hotkey is reserved by Windows.").Trim();
        else if (HotkeyParser.TryParse(screenshot, out var sMods, out var sVk))
        {
            ScreenshotRegistered = NativeMethods.RegisterHotKey(_source.Handle, NativeMethods.HotkeyScreenshot, sMods, sVk);
            if (!ScreenshotRegistered) LastError = (LastError + " " + (screenshot ?? "screenshot") + " is already taken or invalid (win32 " + Marshal.GetLastWin32Error() + ").").Trim();
        }
        SetTypeOutActive(typeOutActive, typeOutGo);
    }

    // Go and Esc exist only while type-out is armed or running, never at rest.
    public void SetTypeOutActive(bool active, string? go)
    {
        if (_source is null) return;
        Unregister(NativeMethods.HotkeyTypeOutGo);
        Unregister(NativeMethods.HotkeyEscape);
        TypeOutRegistered = _activeEscape = false;
        if (!active) return;
        if (HotkeyParser.IsForbidden(go) || !HotkeyParser.TryParse(go, out var mods, out var vk))
        {
            LastError = "Type-out Go hotkey is invalid or reserved.";
            return;
        }
        TypeOutRegistered = NativeMethods.RegisterHotKey(_source.Handle, NativeMethods.HotkeyTypeOutGo, mods, vk);
        if (!TypeOutRegistered)
        {
            LastError = "Type-out Go is unavailable: " + (go ?? "hotkey") + " is already taken or invalid (win32 " + Marshal.GetLastWin32Error() + ").";
            return;
        }
        _activeEscape = NativeMethods.RegisterHotKey(_source.Handle, NativeMethods.HotkeyEscape, NativeMethods.ModNorepeat, NativeMethods.VkEscape);
        if (!_activeEscape)
        {
            Unregister(NativeMethods.HotkeyTypeOutGo);
            TypeOutRegistered = false;
            LastError = "Type-out did not arm: Esc could not be registered (win32 " + Marshal.GetLastWin32Error() + ").";
        }
    }

    private void Unregister(int id)
    {
        if (_source is not null) NativeMethods.UnregisterHotKey(_source.Handle, id);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmHotkey)
        {
            handled = true;
            var id = wParam.ToInt32();
            if (id == NativeMethods.HotkeyPicker) PickerRequested?.Invoke();
            else if (id == NativeMethods.HotkeyScreenshot) ScreenshotPathRequested?.Invoke();
            else if (id == NativeMethods.HotkeyTypeOutGo) TypeOutGoRequested?.Invoke();
            else if (id == NativeMethods.HotkeyEscape) TypeOutCancelRequested?.Invoke();
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_source is not null)
        {
            Unregister(NativeMethods.HotkeyPicker);
            Unregister(NativeMethods.HotkeyScreenshot);
            Unregister(NativeMethods.HotkeyTypeOutGo);
            Unregister(NativeMethods.HotkeyEscape);
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }
    }
}
