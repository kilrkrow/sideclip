using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Windows.Threading;
using Sideclip.History;
using Sideclip.TypeOut;

namespace Sideclip.Tests;

// Regression tests for the r2 HOLD fixes: Tab/Enter target-key warning,
// honest completion wording, control-level focus stop, kept expiry reason,
// and the picker's dark disabled button style.
public sealed class TypeOutTargetKeyTests
{
    [Theory]
    [InlineData("a\tb", true)]
    [InlineData("a\r\nb", true)]
    [InlineData("a\nb", true)]
    [InlineData("a\rb", true)]
    [InlineData("plain text", false)]
    [InlineData("caf\u00E9 \uD83D\uDE0A", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Target_key_warning_triggers_only_for_tab_or_line_breaks(string? text, bool expected) =>
        Assert.Equal(expected, TypeOutText.HasTargetKeys(text));

    [Fact]
    public void Warnings_are_ascii_name_the_risk_and_fit_the_tray_label()
    {
        foreach (var message in new[] { TypeOutText.TargetKeyWarning, TypeOutText.ArmedTargetKeyWarning })
        {
            Assert.All(message, ch => Assert.InRange(ch, ' ', '~'));
            Assert.Contains("Tab", message);
            Assert.Contains("Enter", message);
        }
        Assert.Contains("browsers", TypeOutText.TargetKeyWarning);
        Assert.Contains("navigation or submit", TypeOutText.TargetKeyWarning);
        Assert.True(TypeOutText.ArmedTargetKeyWarning.Length <= 80, "tray status label is cut at 80 characters");
    }

    [Fact]
    public async Task Completion_says_keys_were_sent_and_never_claims_insertion()
    {
        var sink = new TypeOutTests.FakeSink();
        var result = await new TypeOutRunner(sink, new TypeOutTests.FakeFocus(42))
            .RunAsync("abc".ToCharArray(), () => true, 42, 0, CancellationToken.None);
        Assert.Equal(TypeOutStatus.Completed, result.Status);
        Assert.Equal("Type-out sent 3 of 3 keys. Insertion not verified.", result.Message);
        Assert.DoesNotContain("finished", result.Message, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("inserted", result.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Completion_counts_tab_and_enter_key_presses()
    {
        var sink = new TypeOutTests.FakeSink();
        var result = await new TypeOutRunner(sink, new TypeOutTests.FakeFocus(42))
            .RunAsync("a\tb\r\nc".ToCharArray(), () => true, 42, 0, CancellationToken.None);
        Assert.Equal(TypeOutStatus.Completed, result.Status);
        Assert.Equal("Type-out sent 5 of 5 keys (2 Tab/Enter). Insertion not verified.", result.Message);
        Assert.Equal("a\tb\rc", sink.Text);
    }

    [Fact]
    public async Task Chunk_ends_after_each_tab_or_enter_so_focus_is_rechecked()
    {
        var sink = new TypeOutTests.FakeSink();
        await new TypeOutRunner(sink, new TypeOutTests.FakeFocus(42))
            .RunAsync("ab\tcd\nef".ToCharArray(), () => true, 42, 0, CancellationToken.None);
        Assert.Equal(new[] { "ab\t", "cd\r", "ef" }, sink.Chunks);
    }

    [Fact]
    public async Task Control_focus_move_after_tab_stops_before_following_text()
    {
        var focus = new ControlFocus(42, focused: 100);
        var sink = new TypeOutTests.FakeSink();
        sink.AfterSend = () => { if (sink.Text.EndsWith('\t')) focus.Focused = 200; };
        var result = await new TypeOutRunner(sink, focus)
            .RunAsync("name\tsecret-free tail".ToCharArray(), () => true, 42, 0, CancellationToken.None);
        Assert.Equal(TypeOutStatus.FocusChanged, result.Status);
        Assert.Contains("focus moved to another control", result.Message);
        Assert.Contains("About 5 of 21 UTF-16 units were sent.", result.Message);
        Assert.Equal(new[] { "name\t" }, sink.Chunks);
    }

    [Fact]
    public async Task Unknown_control_focus_keeps_window_level_behavior()
    {
        var focus = new ControlFocus(42, focused: 0);
        var sink = new TypeOutTests.FakeSink();
        var result = await new TypeOutRunner(sink, focus)
            .RunAsync("a\tb".ToCharArray(), () => true, 42, 0, CancellationToken.None);
        Assert.Equal(TypeOutStatus.Completed, result.Status);
        Assert.Equal("a\tb", sink.Text);
    }

    [Fact]
    public void Stop_reason_replaces_generic_cancel_and_keeps_counts()
    {
        var expired = TypeOutSession.StopMessage(EntryInvalidationReason.Expired);
        Assert.Equal("Type-out stopped because the entry expired (secret TTL).", expired);
        var cancelled = new TypeOutResult(TypeOutStatus.Cancelled, "Type-out cancelled. About 48 of 64 UTF-16 units were sent.");
        var kept = TypeOutSession.WithStopReason(cancelled, expired);
        Assert.Equal(TypeOutStatus.SourceGone, kept.Status);
        Assert.Equal("Type-out stopped because the entry expired (secret TTL). About 48 of 64 UTF-16 units were sent.", kept.Message);
        Assert.Same(cancelled, TypeOutSession.WithStopReason(cancelled, null));
        var done = new TypeOutResult(TypeOutStatus.Completed, "Type-out sent 2 of 2 keys. Insertion not verified.");
        Assert.Same(done, TypeOutSession.WithStopReason(done, expired));
        Assert.Contains("history was cleared", TypeOutSession.StopMessage(EntryInvalidationReason.Cleared));
        Assert.Contains("was deleted", TypeOutSession.StopMessage(EntryInvalidationReason.Deleted));
    }

    [Theory]
    [InlineData("Ttl", "Type-out stopped because the entry expired (secret TTL). About 2 of 4 UTF-16 units were sent.")]
    [InlineData("Clear", "Type-out stopped because history was cleared. About 2 of 4 UTF-16 units were sent.")]
    [InlineData("Remove", "Type-out stopped because the entry was deleted. About 2 of 4 UTF-16 units were sent.")]
    public void Running_job_reports_the_invalidation_reason(string operation, string expected)
    {
        RunSta(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "SideclipTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            Environment.SetEnvironmentVariable("SIDECLIP_DATA_DIR", directory);
            try
            {
                using var store = new ClipHistoryStore(Dispatcher.CurrentDispatcher, _ => { }, TimeSpan.FromSeconds(30));
                var entry = store.Add("abcd", isSecret: true, "test.exe");
                var reports = new ConcurrentQueue<string>();
                var sink = new BlockingSink();
                using var session = new TypeOutSession(store.IsPickerSource, reports.Enqueue,
                    () => new TypeOutRunner(sink, new TypeOutTests.FakeFocus(42), (ms, token) => sink.Delay(token), 2),
                    new TypeOutTests.FakeFocus(42), () => { }, action => action(), () => true, () => { });
                session.SetEnabled(true);
                store.EntryInvalidatedWithReason += session.EntryChanged;
                Assert.True(session.Arm(entry));
                session.Go(10000);
                Assert.True(sink.Started.Wait(TimeSpan.FromSeconds(2)), "delay entered");
                switch (operation)
                {
                    case "Remove": store.RemoveEntry(entry); break;
                    case "Clear": store.ClearAll(); break;
                    case "Ttl":
                        typeof(ClipHistoryStore).GetMethod("ExpireSecret", BindingFlags.NonPublic | BindingFlags.Instance)!
                            .Invoke(store, new object[] { entry });
                        break;
                }
                Assert.True(SpinWait.SpinUntil(() => !session.HasEntry && reports.Contains(expected), TimeSpan.FromSeconds(3)),
                    "final status: " + string.Join(" | ", reports));
                Assert.DoesNotContain(reports, r => r.StartsWith("Type-out cancelled", StringComparison.Ordinal));
                Assert.Equal(new[] { "ab" }, sink.Text);
            }
            finally
            {
                Environment.SetEnvironmentVariable("SIDECLIP_DATA_DIR", null);
                try { Directory.Delete(directory, true); } catch { }
            }
        });
    }

    [Fact]
    public void Arming_an_entry_with_tab_reports_the_target_key_warning()
    {
        var reports = new List<string>();
        using var session = new TypeOutSession(e => e.HasPayload, reports.Add,
            () => new TypeOutRunner(new TypeOutTests.FakeSink(), new TypeOutTests.FakeFocus(42)),
            new TypeOutTests.FakeFocus(42), () => { }, action => action(), () => true, () => { });
        session.SetEnabled(true);
        Assert.True(session.Arm(new ClipEntry("col1\tcol2", false, "test")));
        Assert.Equal(TypeOutText.ArmedTargetKeyWarning, reports[^1]);
        session.Cancel();
        Assert.True(session.Arm(new ClipEntry("plain", false, "test")));
        Assert.Equal("Armed: target the destination, then press Go. Esc cancels.", reports[^1]);
    }

    [Fact]
    public void Turning_off_while_armed_replaces_the_armed_status_and_releases_hotkeys()
    {
        var reports = new List<string>();
        var released = 0;
        using var session = new TypeOutSession(e => e.HasPayload, reports.Add,
            () => new TypeOutRunner(new TypeOutTests.FakeSink(), new TypeOutTests.FakeFocus(42)),
            new TypeOutTests.FakeFocus(42), () => { }, action => action(), () => true, () => released++);
        session.SetEnabled(true);
        Assert.True(session.Arm(new ClipEntry("plain", false, "test")));
        released = 0;
        session.SetEnabled(false);
        Assert.Equal(TypeOutSession.TurnedOffMessage, reports[^1]);
        Assert.False(session.HasEntry);
        Assert.True(released > 0, "Go/Esc hotkeys released");
        var count = reports.Count;
        session.SetEnabled(false);
        Assert.Equal(count, reports.Count);
    }

    [Fact]
    public void Turning_off_while_typing_keeps_the_off_reason()
    {
        RunSta(() =>
        {
            var reports = new ConcurrentQueue<string>();
            var sink = new BlockingSink();
            using var session = new TypeOutSession(e => e.HasPayload, reports.Enqueue,
                () => new TypeOutRunner(sink, new TypeOutTests.FakeFocus(42), (ms, token) => sink.Delay(token), 2),
                new TypeOutTests.FakeFocus(42), () => { }, action => action(), () => true, () => { });
            session.SetEnabled(true);
            Assert.True(session.Arm(new ClipEntry("abcd", false, "test")));
            session.Go(10000);
            Assert.True(sink.Started.Wait(TimeSpan.FromSeconds(2)), "delay entered");
            session.SetEnabled(false);
            const string expected = "Type-out stopped because it was turned off. About 2 of 4 UTF-16 units were sent.";
            Assert.True(SpinWait.SpinUntil(() => !session.HasEntry && reports.Contains(expected), TimeSpan.FromSeconds(3)),
                "final status: " + string.Join(" | ", reports));
            Assert.Equal(new[] { "ab" }, sink.Text);
        });
    }

    [Fact]
    public void Picker_buttons_use_dark_template_with_readable_disabled_state()
    {
        var xaml = File.ReadAllText(Path.Combine(SourceRoot, "Picker", "PickerWindow.xaml"));
        Assert.Contains("x:Key=\"PickerButton\"", xaml);
        Assert.Matches("x:Name=\"TypeOutButton\"[^>]*Style=\"\\{StaticResource PickerButton\\}\"", xaml);
        Assert.Matches("x:Name=\"CopyButton\"[^>]*Style=\"\\{StaticResource PickerButton\\}\"", xaml);
        var trigger = xaml[xaml.IndexOf("<Trigger Property=\"IsEnabled\" Value=\"False\">", StringComparison.Ordinal)..];
        trigger = trigger[..trigger.IndexOf("</Trigger>", StringComparison.Ordinal)];
        Assert.Contains("Value=\"#262A35\"", trigger);
        Assert.Contains("Property=\"Foreground\" Value=\"#9AA0AC\"", trigger);
        // WCAG contrast of disabled text on the disabled background.
        Assert.True(Contrast(0x9AA0AC, 0x262A35) >= 4.5, "disabled Arm text contrast");
        var cs = File.ReadAllText(Path.Combine(SourceRoot, "Picker", "PickerWindow.xaml.cs"));
        Assert.Contains("TypeOutText.HasTargetKeys(entry.TryGetText())", cs);
        Assert.Contains("TypeOutText.TargetKeyWarning", cs);
    }

    private static double Contrast(int a, int b)
    {
        static double L(int rgb)
        {
            double C(int v) { var c = v / 255.0; return c <= 0.03928 ? c / 12.92 : Math.Pow((c + 0.055) / 1.055, 2.4); }
            return 0.2126 * C((rgb >> 16) & 0xFF) + 0.7152 * C((rgb >> 8) & 0xFF) + 0.0722 * C(rgb & 0xFF);
        }
        var (x, y) = (L(a), L(b));
        return (Math.Max(x, y) + 0.05) / (Math.Min(x, y) + 0.05);
    }

    private static string SourceRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory.Parent is not null && !Directory.Exists(Path.Combine(directory.FullName, "TypeOut"))) directory = directory.Parent;
            return directory.FullName;
        }
    }

    private static void RunSta(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { test(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }

    private sealed class ControlFocus(int window, int focused) : IFocusProbe
    {
        public int Focused { get; set; } = focused;
        public IntPtr ForegroundWindow => window;
        public IntPtr FocusedControl => Focused;
    }

    private sealed class BlockingSink : IInputSink
    {
        public ManualResetEventSlim Started { get; } = new(false);
        public List<string> Text { get; } = new();
        public int Send(string text) { Text.Add(text); return text.Length * 2; }
        public Task Delay(CancellationToken token) { Started.Set(); return Task.Delay(10000, token); }
    }
}
