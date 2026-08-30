using System.Runtime.InteropServices;
using System.Windows.Interop;
using Sideclip.Native;

namespace Sideclip.Hotkeys;

internal sealed class HotkeyListener : IDisposable
{
    private HwndSource? _source;
    private bool _disposed;
    private bool _pickerOk;
    private bool _shotOk;

    public event Action? PickerRequested;
    public event Action? ScreenshotPathRequested;

    public string? LastError { get; private set; }
    public bool PickerRegistered => _pickerOk;
    public bool ScreenshotRegistered => _shotOk;

    public void Start()
    {
        if (_source is not null)
            return;

        var parameters = new HwndSourceParameters("SideclipHotkeys")
        {
            Width = 1,
            Height = 1,
            PositionX = -32000,
            PositionY = -32000,
            WindowStyle = unchecked((int)0x80000000)
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WndProc);
    }

    public void Apply(string? pickerHotkey, string? screenshotHotkey)
    {
        if (_source is null)
            Start();
        if (_source is null)
            return;

        LastError = null;
        NativeMethods.UnregisterHotKey(_source.Handle, NativeMethods.HotkeyPicker);
        NativeMethods.UnregisterHotKey(_source.Handle, NativeMethods.HotkeyScreenshot);
        _pickerOk = false;
        _shotOk = false;

        if (HotkeyParser.TryParse(pickerHotkey, out var pMods, out var pVk))
        {
            _pickerOk = NativeMethods.RegisterHotKey(_source.Handle, NativeMethods.HotkeyPicker, pMods, pVk);
            if (!_pickerOk)
                LastError = (pickerHotkey ?? "picker") + " is already taken (win32 " + Marshal.GetLastWin32Error() + ").";
        }

        if (HotkeyParser.TryParse(screenshotHotkey, out var sMods, out var sVk))
        {
            _shotOk = NativeMethods.RegisterHotKey(_source.Handle, NativeMethods.HotkeyScreenshot, sMods, sVk);
            if (!_shotOk)
                LastError = (LastError ?? "") + " " + (screenshotHotkey ?? "screenshot") + " is already taken (win32 " + Marshal.GetLastWin32Error() + ").";
        }
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmHotkey)
        {
            handled = true;
            var id = wParam.ToInt32();
            if (id == NativeMethods.HotkeyPicker)
                PickerRequested?.Invoke();
            else if (id == NativeMethods.HotkeyScreenshot)
                ScreenshotPathRequested?.Invoke();
        }

        return IntPtr.Zero;
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        if (_source is not null)
        {
            NativeMethods.UnregisterHotKey(_source.Handle, NativeMethods.HotkeyPicker);
            NativeMethods.UnregisterHotKey(_source.Handle, NativeMethods.HotkeyScreenshot);
            _source.RemoveHook(WndProc);
            _source.Dispose();
            _source = null;
        }
    }
}
