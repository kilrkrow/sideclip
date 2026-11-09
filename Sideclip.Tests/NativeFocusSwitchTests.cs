using System.Runtime.InteropServices;
using Sideclip.TypeOut;

namespace Sideclip.Tests;

// Positive native check for the control-level focus stop: a hidden test
// window with two real Win32 EDIT controls on this thread. Moving keyboard
// focus from one EDIT to the other (same top-level window) must stop
// type-out. No input is injected; the sink is fake. This covers Win32
// child-control focus only, never DOM focus inside a browser.
public sealed class NativeFocusSwitchTests
{
    private const uint WsOverlapped = 0x00000000, WsChild = 0x40000000, WsVisible = 0x10000000;

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr CreateWindowExW(uint exStyle, string className, string? name, uint style,
        int x, int y, int w, int h, IntPtr parent, IntPtr menu, IntPtr instance, IntPtr param);
    [DllImport("user32.dll")] private static extern IntPtr SetFocus(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetFocus();
    [DllImport("user32.dll")] private static extern bool DestroyWindow(IntPtr hWnd);

    [Fact]
    public void Focus_switch_between_two_native_edit_controls_stops_type_out()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            IntPtr top = IntPtr.Zero;
            try
            {
                // Hidden, never activated: no foreground change during the test run.
                top = CreateWindowExW(0, "STATIC", "Sideclip focus test", WsOverlapped, -32000, -32000, 300, 120, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                Assert.NotEqual(IntPtr.Zero, top);
                var first = CreateWindowExW(0, "EDIT", "", WsChild | WsVisible, 0, 0, 120, 24, top, (IntPtr)1, IntPtr.Zero, IntPtr.Zero);
                var second = CreateWindowExW(0, "EDIT", "", WsChild | WsVisible, 0, 40, 120, 24, top, (IntPtr)2, IntPtr.Zero, IntPtr.Zero);
                Assert.NotEqual(IntPtr.Zero, first);
                Assert.NotEqual(IntPtr.Zero, second);

                SetFocus(first);
                Assert.Equal(first, GetFocus());
                Assert.Equal(first, Win32InputSink.FocusedControlOf(top));

                var probe = new WindowFocus(top);
                var sink = new TypeOutTests.FakeSink();
                // The target app reacts to Tab by moving focus to its next control.
                sink.AfterSend = () => { if (sink.Text.EndsWith('\t')) SetFocus(second); };
                var result = new TypeOutRunner(sink, probe)
                    .RunAsync("user\tsecond field text".ToCharArray(), () => true, top, 0, CancellationToken.None)
                    .GetAwaiter().GetResult();

                Assert.Equal(second, Win32InputSink.FocusedControlOf(top));
                Assert.Equal(TypeOutStatus.FocusChanged, result.Status);
                Assert.Contains("focus moved to another control", result.Message);
                Assert.Equal(new[] { "user\t" }, sink.Chunks);
            }
            catch (Exception ex) { failure = ex; }
            finally { if (top != IntPtr.Zero) DestroyWindow(top); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }

    [Fact]
    public void Focus_staying_in_one_native_edit_control_completes()
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            IntPtr top = IntPtr.Zero;
            try
            {
                top = CreateWindowExW(0, "STATIC", "Sideclip focus test", WsOverlapped, -32000, -32000, 300, 120, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero, IntPtr.Zero);
                var edit = CreateWindowExW(0, "EDIT", "", WsChild | WsVisible, 0, 0, 120, 24, top, (IntPtr)1, IntPtr.Zero, IntPtr.Zero);
                SetFocus(edit);
                var sink = new TypeOutTests.FakeSink();
                var result = new TypeOutRunner(sink, new WindowFocus(top))
                    .RunAsync("a\tb\rc".ToCharArray(), () => true, top, 0, CancellationToken.None)
                    .GetAwaiter().GetResult();
                Assert.Equal(TypeOutStatus.Completed, result.Status);
                Assert.Equal(edit, Win32InputSink.FocusedControlOf(top));
            }
            catch (Exception ex) { failure = ex; }
            finally { if (top != IntPtr.Zero) DestroyWindow(top); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }

    // Production focus read (GetGUIThreadInfo) scoped to the test window
    // instead of the real foreground window.
    private sealed class WindowFocus(IntPtr window) : IFocusProbe
    {
        public IntPtr ForegroundWindow => window;
        public IntPtr FocusedControl => Win32InputSink.FocusedControlOf(window);
    }
}
