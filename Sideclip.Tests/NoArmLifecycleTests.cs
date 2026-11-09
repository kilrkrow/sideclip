using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using Sideclip.History;
using Sideclip.Hotkeys;
using Sideclip.Picker;
using Sideclip.TypeOut;

namespace Sideclip.Tests;

public sealed class NoArmLifecycleTests
{
    private static void Sta(Action body)
    {
        Exception? error = null;
        var thread = new Thread(() => { try { body(); } catch (Exception ex) { error = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start(); thread.Join();
        if (error is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(error).Throw();
    }
    private static object? Call(object instance, string name, params object?[] args) =>
        instance.GetType().GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(instance, args);
    private static void Filter(PickerWindow picker, string query)
    {
        typeof(PickerWindow).GetField("_filter", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(picker, query);
        Call(picker, "ApplyFilter");
    }
    private static TypeOutSession Session(TypeOutTests.FakeSink sink, ConcurrentQueue<string> reports, Func<ClipEntry, bool>? live = null,
        Func<bool>? register = null, Action? release = null) => new(live ?? (entry => entry.HasPayload), reports.Enqueue,
            () => new TypeOutRunner(sink, new TypeOutTests.FakeFocus(42)), new TypeOutTests.FakeFocus(42), () => { }, action => action(),
            register ?? (() => true), release ?? (() => { }));
    private static void Complete(TypeOutSession session) =>
        Assert.True(SpinWait.SpinUntil(() => !session.HasEntry && !session.IsActive, TimeSpan.FromSeconds(3)));

    [Fact]
    public void Programmatic_highlight_and_filter_do_not_choose_but_navigation_does()
    {
        Sta(() =>
        {
            var first = new ClipEntry("first", false, "test");
            var second = new ClipEntry("second", false, "test");
            var picker = new PickerWindow(new[] { first, second }, 0, (_, _) => { }, true);
            var choices = new List<ClipEntry>(); picker.CandidateSelected += choices.Add;
            var list = (ListBox)picker.FindName("List");
            Assert.Equal(0, list.SelectedIndex); Assert.Empty(choices);
            list.SelectedIndex = 1; Call(picker, "ApplyFilter"); Assert.Empty(choices);
            Call(picker, "MoveSel", 1); Assert.Same(second, Assert.Single(choices));
            picker.Close();
        });
    }

    [Fact]
    public void Mouse_choice_of_initial_row_is_explicit_and_image_choice_clears_text()
    {
        Sta(() =>
        {
            var text = new ClipEntry("text", false, "test");
            var image = new ClipEntry("", false, "test", new byte[] { 1 });
            using var session = Session(new(), new()); session.SetEnabled(true);
            var picker = new PickerWindow(new[] { text, image }, 0, (_, _) => { }, true);
            picker.CandidateSelected += entry => session.SelectCandidate(entry);
            var list = (ListBox)picker.FindName("List");
            Call(picker, "OnRowChoose", new ListBoxItem { Content = list.Items[0] }, null);
            Assert.Same(text, session.Candidate);
            Call(picker, "OnRowChoose", new ListBoxItem { Content = list.Items[1] }, null);
            Assert.Null(session.Candidate); Assert.False(session.HasEntry);
            picker.Close();
        });
    }

    [Fact]
    public void Filtering_away_identity_invalidates_and_row_zero_does_not_replace_it()
    {
        Sta(() =>
        {
            var entry = new ClipEntry("first", false, "test");
            using var session = Session(new(), new()); session.SetEnabled(true);
            var picker = new PickerWindow(new[] { entry, new ClipEntry("second", false, "test") }, 0, (_, _) => { }, true);
            picker.CandidateSelected += row => session.SelectCandidate(row);
            picker.CandidateInvalidated += () => session.Cancel();
            Call(picker, "MoveSel", 0); Assert.Same(entry, session.Candidate);
            Filter(picker, "second"); Assert.False(session.HasEntry);
            Filter(picker, ""); Assert.False(session.HasEntry);
            picker.Close();
        });
    }

    [Fact]
    public void Picker_Esc_clears_filter_first_then_cancels_identity()
    {
        Sta(() =>
        {
            var entry = new ClipEntry("alpha", false, "test");
            using var session = Session(new(), new()); session.SetEnabled(true);
            var picker = new PickerWindow(new[] { entry }, 0, (_, _) => { }, true);
            picker.CandidateSelected += row => session.SelectCandidate(row);
            picker.CandidateInvalidated += () => session.Cancel();
            Call(picker, "MoveSel", 0); Filter(picker, "alp");
            picker.EscapePicker(); Assert.Same(entry, session.Candidate);
            picker.EscapePicker(); Assert.False(session.HasEntry);
        });
    }

    [Fact]
    public void Blur_preserves_identity_and_new_picker_open_wiring_clears_it()
    {
        Sta(() =>
        {
            var entry = new ClipEntry("alpha", false, "test");
            using var session = Session(new(), new()); session.SetEnabled(true);
            session.RefreshCancellation(true); session.SelectCandidate(entry);
            session.RefreshCancellation(false); Assert.Same(entry, session.Candidate);
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(root.FullName, "App.xaml.cs"))) root = root.Parent!;
            var app = File.ReadAllText(Path.Combine(root.FullName, "App.xaml.cs"));
            var open = app[app.IndexOf("private void OpenPicker()", StringComparison.Ordinal)..];
            Assert.True(open.IndexOf("_typeOut?.Cancel();", StringComparison.Ordinal) < open.IndexOf("new PickerWindow", StringComparison.Ordinal));
            Assert.DoesNotContain("InputSender.CtrlV()", app);
            var picker = File.ReadAllText(Path.Combine(root.FullName, "Picker", "PickerWindow.xaml.cs"));
            Assert.DoesNotContain("FocusWindow", picker);
        });
    }

    [Theory]
    [InlineData("Enter")]
    [InlineData("ShiftEnter")]
    [InlineData("DoubleClick")]
    [InlineData("Copy")]
    public void Explicit_activation_of_initial_row_is_copy_only(string action)
    {
        Sta(() =>
        {
            var entry = new ClipEntry("alpha", false, "test");
            var commits = new List<(ClipEntry Entry, bool Paste)>();
            var choices = new List<ClipEntry>();
            var picker = new PickerWindow(new[] { entry }, 0, (row, paste) => commits.Add((row, paste)), true);
            picker.CandidateSelected += choices.Add;
            if (action is "Enter" or "ShiftEnter")
            {
                Assert.Equal(PickerCommitAction.Copy, PickerKeyMapping.Resolve(Key.Enter,
                    action == "Enter" ? ModifierKeys.None : ModifierKeys.Shift, entry, true));
                Call(picker, "Commit", false);
            }
            else if (action == "DoubleClick")
                Call(picker, "OnRowActivate", null, new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left) { RoutedEvent = Control.MouseDoubleClickEvent });
            else Call(picker, "OnCopyClick", null, new System.Windows.RoutedEventArgs());
            Assert.Same(entry, Assert.Single(choices));
            Assert.Equal((entry, false), Assert.Single(commits));
        });
    }

    [Fact]
    public void New_capture_never_retargets_and_trigger_is_single_use_even_after_completion()
    {
        Sta(() =>
        {
            var oldDir = Environment.GetEnvironmentVariable("SIDECLIP_DATA_DIR");
            var dir = Path.Combine(Path.GetTempPath(), "SideclipTests-" + Guid.NewGuid());
            Environment.SetEnvironmentVariable("SIDECLIP_DATA_DIR", dir);
            try
            {
                using var store = new ClipHistoryStore(Dispatcher.CurrentDispatcher, _ => { });
                var selected = store.Add("selected", false, "test");
                var sink = new TypeOutTests.FakeSink();
                using var session = Session(sink, new(), store.IsTypeOutSource); session.SetEnabled(true);
                session.SelectCandidate(selected);
                store.Add("unrelated capture", false, "test");
                Assert.Same(selected, session.Candidate);
                session.Go(0); session.Go(0); Complete(session); session.Go(0);
                Assert.Equal("selected", sink.Text); Assert.Null(session.Candidate);
            }
            finally { Environment.SetEnvironmentVariable("SIDECLIP_DATA_DIR", oldDir); if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        });
    }

    [Fact]
    public void Pending_identity_has_no_payload_duplicate_and_wipe_refuses_trigger()
    {
        var entry = new ClipEntry("synthetic live secret", true, "test");
        var sink = new TypeOutTests.FakeSink();
        using var session = Session(sink, new()); session.SetEnabled(true); session.SelectCandidate(entry);
        Assert.DoesNotContain(typeof(TypeOutSession).GetFields(BindingFlags.Instance | BindingFlags.NonPublic), f => f.FieldType == typeof(char[]) || f.FieldType == typeof(string) && f.Name.Contains("payload", StringComparison.OrdinalIgnoreCase));
        entry.WipePayload(); session.Go(0);
        Assert.Empty(sink.Chunks); Assert.False(session.HasEntry);
    }

    [Fact]
    public void Elapsed_TTL_refuses_before_timer_tick_and_turning_off_cannot_resurrect()
    {
        Sta(() =>
        {
            var oldDir = Environment.GetEnvironmentVariable("SIDECLIP_DATA_DIR");
            var dir = Path.Combine(Path.GetTempPath(), "SideclipTests-" + Guid.NewGuid());
            Environment.SetEnvironmentVariable("SIDECLIP_DATA_DIR", dir);
            try
            {
                using var store = new ClipHistoryStore(Dispatcher.CurrentDispatcher, _ => { }, TimeSpan.FromMilliseconds(10));
                var entry = store.Add("synthetic", true, "test");
                var sink = new TypeOutTests.FakeSink();
                using var session = Session(sink, new(), store.IsTypeOutSource); session.SetEnabled(true); session.SelectCandidate(entry);
                Thread.Sleep(40); // Deliberately do not pump the timer's dispatcher.
                Assert.True(entry.HasPayload); Assert.False(store.IsTypeOutSource(entry));
                session.Go(0); Assert.Empty(sink.Chunks); Assert.False(session.HasEntry);
                store.SetTtl(TimeSpan.FromSeconds(3), false); Assert.False(store.IsTypeOutSource(entry)); Assert.False(entry.HasPayload);
            }
            finally { Environment.SetEnvironmentVariable("SIDECLIP_DATA_DIR", oldDir); if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        });
    }

    [Fact]
    public void No_source_is_status_only_and_off_on_restart_never_reprimes()
    {
        var sink = new TypeOutTests.FakeSink(); var reports = new ConcurrentQueue<string>();
        using var session = Session(sink, reports); session.SetEnabled(true); session.Go(0);
        Assert.Empty(sink.Chunks); Assert.Contains(reports, m => m.Contains("no selected source"));
        session.SelectCandidate(new ClipEntry("alpha", false, "test")); session.SetEnabled(false); session.SetEnabled(true);
        session.Go(0); Assert.Empty(sink.Chunks); Assert.Null(session.Candidate);
        using var restarted = Session(sink, reports); restarted.SetEnabled(true); restarted.Go(0); Assert.Empty(sink.Chunks);
    }

    [Fact]
    public void Esc_lifetime_is_external_pending_or_running_not_idle_or_picker_local()
    {
        var esc = false;
        using var session = Session(new(), new(), register: () => esc = true, release: () => esc = false);
        session.SetEnabled(true); Assert.False(esc);
        session.RefreshCancellation(true); session.SelectCandidate(new ClipEntry("alpha", false, "test")); Assert.False(esc);
        session.RefreshCancellation(false); Assert.True(esc);
        session.RefreshCancellation(true); Assert.False(esc);
        session.RefreshCancellation(false); Assert.True(esc);
        session.Cancel(); Assert.False(esc);
    }

    [Fact]
    public void Native_registration_trigger_while_enabled_and_Esc_independent_with_honest_collision()
    {
        Sta(() =>
        {
            using var first = new HotkeyListener(); first.Start();
            first.Apply(null, null, "Ctrl+Alt+Shift+F9", true);
            Assert.True(first.TypeOutRegistered, first.LastError); Assert.False(first.EscapeRegistered);
            using var second = new HotkeyListener(); second.Start(); second.Apply(null, null, "Ctrl+Alt+Shift+F9", true);
            Assert.False(second.TypeOutRegistered); Assert.Contains("unavailable", second.LastError);
            Assert.True(first.SetEscapeActive(true), first.LastError); Assert.True(first.EscapeRegistered);
            Assert.False(second.SetEscapeActive(true)); Assert.Contains("Esc cancellation unavailable", second.LastError);
            first.SetEscapeActive(false); Assert.True(first.TypeOutRegistered); Assert.False(first.EscapeRegistered);
            first.SetTypeOutActive(false, null); Assert.False(first.TypeOutRegistered);
            second.Apply(null, null, "Ctrl+Alt+Shift+F9", true); Assert.True(second.TypeOutRegistered, second.LastError);
            second.Apply(null, null, "", false); Assert.False(second.TypeOutRegistered); Assert.False(second.EscapeRegistered);
        });
    }
    [Fact]
    public void Any_own_process_window_is_refused_and_consumes_candidate()
    {
        Sta(() =>
        {
            using var window = new System.Windows.Interop.HwndSource(new System.Windows.Interop.HwndSourceParameters("SideclipTestOwnedWindow") { Width = 1, Height = 1 });
            var sink = new TypeOutTests.FakeSink(); var reports = new ConcurrentQueue<string>();
            using var session = new TypeOutSession(e => e.HasPayload, reports.Enqueue,
                () => new TypeOutRunner(sink, new TypeOutTests.FakeFocus(window.Handle)),
                new TypeOutTests.FakeFocus(window.Handle), () => { }, action => action());
            session.SetEnabled(true); session.SelectCandidate(new ClipEntry("alpha", false, "test")); session.Go(0);
            Assert.Empty(sink.Chunks); Assert.False(session.HasEntry);
            Assert.Contains(reports, m => m.Contains("external destination"));
        });
    }

    [Fact]
    public void Running_job_keeps_identity_and_Esc_until_cancellation_finishes()
    {
        var sink = new TypeOutTests.FakeSink(); var entered = new ManualResetEventSlim();
        var esc = false;
        using var session = new TypeOutSession(e => e.HasPayload, _ => { },
            () => new TypeOutRunner(sink, new TypeOutTests.FakeFocus(42), (ms, token) => { entered.Set(); return Task.Delay(10000, token); }, 2),
            new TypeOutTests.FakeFocus(42), () => { }, action => action(), () => esc = true, () => esc = false);
        session.SetEnabled(true); session.SelectCandidate(new ClipEntry("abcd", false, "test")); session.Go(1);
        Assert.True(entered.Wait(TimeSpan.FromSeconds(2))); Assert.True(esc);
        Assert.False(session.SelectCandidate(new ClipEntry("replacement", false, "test")));
        session.Go(1); session.Cancel(); Complete(session);
        Assert.False(esc); Assert.Equal("ab", sink.Text); Assert.Null(session.Candidate);
    }
    [Theory]
    [InlineData("Scrim")]
    [InlineData("WindowClose")]
    [InlineData("Esc")]
    public void Uncommitted_close_completes_CLI_waiter_once_and_only_Esc_clears_candidate(string action)
    {
        Sta(() =>
        {
            using var session = Session(new(), new()); session.SetEnabled(true);
            var entry = new ClipEntry("alpha", false, "test");
            var picker = new PickerWindow(new[] { entry }, 0, (_, _) => Assert.Fail("Must not commit"), true);
            picker.CandidateSelected += row => session.SelectCandidate(row);
            picker.CandidateInvalidated += () => session.Cancel();
            var waiter = new TaskCompletionSource<string>(); var cancellations = 0;
            picker.Cancelled += () => { cancellations++; waiter.TrySetResult("cancel"); };
            Call(picker, "MoveSel", 0);
            if (action == "Esc") picker.EscapePicker();
            else if (action == "Scrim") Call(picker, "OnScrimClick", null, new MouseButtonEventArgs(Mouse.PrimaryDevice, 0, MouseButton.Left));
            else picker.Close();
            Assert.True(waiter.Task.IsCompletedSuccessfully);
            Assert.Equal("cancel", waiter.Task.Result); Assert.Equal(1, cancellations);
            Assert.Equal(action != "Esc", session.HasEntry);
            if (action != "Esc") Assert.Same(entry, session.Candidate);
            var root = new DirectoryInfo(AppContext.BaseDirectory);
            while (!File.Exists(Path.Combine(root.FullName, "App.xaml.cs"))) root = root.Parent!;
            var app = File.ReadAllText(Path.Combine(root.FullName, "App.xaml.cs"));
            Assert.Contains("_picker.Cancelled += () => FinishPick(new PickResult { Kind = \"cancel\" });", app);
        });
    }

    [Fact]
    public void Committed_close_delivers_CLI_selection_without_premature_cancellation()
    {
        Sta(() =>
        {
            var entry = new ClipEntry("alpha", false, "test");
            var waiter = new TaskCompletionSource<ClipEntry>(); var cancellations = 0;
            var picker = new PickerWindow(new[] { entry }, 0, (selected, paste) =>
            {
                Assert.False(paste); Assert.False(waiter.Task.IsCompleted);
                waiter.TrySetResult(selected);
            }, true);
            picker.Cancelled += () => { cancellations++; waiter.TrySetException(new Exception("Premature cancel")); };
            Call(picker, "Commit", false);
            Assert.Equal(0, cancellations); Assert.True(waiter.Task.IsCompletedSuccessfully);
            Assert.Same(entry, waiter.Task.Result);
        });
    }

    [Fact]
    public void Trigger_registration_explicitly_requests_MOD_NOREPEAT()
    {
        var root = new DirectoryInfo(AppContext.BaseDirectory);
        while (!File.Exists(Path.Combine(root.FullName, "App.xaml.cs"))) root = root.Parent!;
        var source = File.ReadAllText(Path.Combine(root.FullName, "Native", "HotkeyListener.cs"));
        Assert.Contains("NativeMethods.HotkeyTypeOutGo, mods | NativeMethods.ModNorepeat, vk)", source);
    }
}
