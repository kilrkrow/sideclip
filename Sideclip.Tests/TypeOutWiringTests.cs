using System.IO;
using System.Reflection;
using System.Windows.Input;
using System.Windows.Threading;
using Sideclip.History;
using Sideclip.Picker;
using Sideclip.Settings;
using Sideclip.Tray;
using Sideclip.TypeOut;

namespace Sideclip.Tests;

public sealed class TypeOutWiringTests
{
    [Fact]
    public void Tray_typeout_parent_is_once_after_TTL_with_reachable_children()
    {
        var settings = new AppSettings { TypeOutEnabled = false };
        using var ttl = new System.Windows.Forms.ToolStripMenuItem("Secret TTL");
        var model = new TrayMenuModel(settings, _ => { });
        using var menu = model.Build(new System.Windows.Forms.ContextMenuStrip(), ttl);
        Assert.Single(menu.Items.Cast<System.Windows.Forms.ToolStripItem>(), item => ReferenceEquals(item, model.Parent));
        Assert.Single(menu.Items.Cast<System.Windows.Forms.ToolStripItem>(), item => ReferenceEquals(item, model.Actions));
        Assert.Equal(menu.Items.IndexOf(ttl) + 2, menu.Items.IndexOf(model.Parent));
        Assert.Equal(4, model.Parent.DropDownItems.Count);
        Assert.Same(model.Parent, model.Speed.OwnerItem);
        Assert.Contains("Go: " + settings.TypeOutGoHotkey, model.Hint.Text);
        Assert.False(model.Speed.Enabled);
        Assert.False(model.Hint.Enabled);
        Assert.False(model.Go.Enabled);
        Assert.False(model.Cancel.Enabled);
        Assert.All(model.Speed.DropDownItems.Cast<System.Windows.Forms.ToolStripItem>(), item => Assert.False(item.Enabled));
        Assert.True(model.Off.Enabled);
        Assert.True(model.On.Enabled);
        model.On.PerformClick();
        Assert.True(settings.TypeOutEnabled);
        Assert.True(model.Speed.Enabled);
        Assert.True(model.Hint.Enabled);
        Assert.True(model.Go.Enabled);
        Assert.True(model.Cancel.Enabled);
        Assert.All(model.Speed.DropDownItems.Cast<System.Windows.Forms.ToolStripItem>(), item => Assert.True(item.Enabled));
        model.Off.PerformClick();
        Assert.False(settings.TypeOutEnabled);
        Assert.False(model.Hint.Enabled);
    }

    [Theory]
    [InlineData("Remove", false)]
    [InlineData("Remove", true)]
    [InlineData("Clear", false)]
    [InlineData("Clear", true)]
    [InlineData("Ttl", false)]
    [InlineData("Ttl", true)]
    public void Invalidation_cancels_armed_and_running_sessions(string operation, bool running)
    {
        RunSta(() =>
        {
            var directory = Path.Combine(Path.GetTempPath(), "SideclipTests-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(directory);
            Environment.SetEnvironmentVariable("SIDECLIP_DATA_DIR", directory);
            try
            {
                using var store = new ClipHistoryStore(Dispatcher.CurrentDispatcher, _ => { }, TimeSpan.FromSeconds(3));
                var entry = store.Add("abc", isSecret: true, "test.exe");
                var invalidated = new List<ClipEntry>();
                store.EntryInvalidated += invalidated.Add;
                var sink = new DelaySink();
                var deactivated = false;
                using var session = new TypeOutSession(store.IsPickerSource, _ => { },
                    () => new TypeOutRunner(sink, new TestFocus(42), (ms, token) => sink.Delay(token), 2),
                    new TestFocus(42), () => { }, action => action(), () => true, () => deactivated = true);
                session.SetEnabled(true);
                store.EntryInvalidated += session.EntryChanged;
                Assert.True(session.Arm(entry));
                if (running)
                {
                    session.Go(10000);
                    Assert.True(sink.Started.Wait(TimeSpan.FromSeconds(2)), "delay entered");
                }
                switch (operation)
                {
                    case "Remove": store.RemoveEntry(entry); break;
                    case "Clear": store.ClearAll(); break;
                    case "Ttl":
                        typeof(ClipHistoryStore).GetMethod("ExpireSecret", BindingFlags.NonPublic | BindingFlags.Instance)!
                            .Invoke(store, new object[] { entry });
                        break;
                    default: throw new ArgumentOutOfRangeException(nameof(operation));
                }
                Assert.Equal(new[] { entry }, invalidated);
                Assert.False(store.IsPickerSource(entry));
                Assert.True(deactivated);
                Assert.True(SpinWait.SpinUntil(() => !session.HasEntry, TimeSpan.FromSeconds(2)), "session completed and reset");
                if (running) Assert.Equal(new[] { "ab" }, sink.Text);
                else Assert.Empty(sink.Text);
                session.Go(0);
            }
            finally
            {
                Environment.SetEnvironmentVariable("SIDECLIP_DATA_DIR", null);
                try { Directory.Delete(directory, true); } catch { }
            }
        });
    }

    [Fact]
    public void Keyboard_mapping_preserves_commit_and_requires_eligible_arm_target()
    {
        var text = new ClipEntry("text", false, "test");
        var secret = new ClipEntry("secret", true, "test");
        var image = new ClipEntry(string.Empty, false, "test", new byte[] { 1 });
        var malformed = new ClipEntry("bad\uD800", false, "test");
        Assert.Equal(PickerCommitAction.Paste, PickerKeyMapping.Resolve(Key.Enter, ModifierKeys.None, text, true));
        Assert.Equal(PickerCommitAction.Copy, PickerKeyMapping.Resolve(Key.Enter, ModifierKeys.Shift, text, true));
        Assert.Equal(PickerCommitAction.ArmTypeOut, PickerKeyMapping.Resolve(Key.T, ModifierKeys.Control, text, true));
        Assert.Equal(PickerCommitAction.ArmTypeOut, PickerKeyMapping.Resolve(Key.T, ModifierKeys.Control, secret, true));
        Assert.Equal(PickerCommitAction.None, PickerKeyMapping.Resolve(Key.T, ModifierKeys.Control, text, false));
        Assert.Equal(PickerCommitAction.None, PickerKeyMapping.Resolve(Key.T, ModifierKeys.Control, image, true));
        Assert.Equal(PickerCommitAction.None, PickerKeyMapping.Resolve(Key.T, ModifierKeys.Control, null, true));
        Assert.Equal(PickerCommitAction.None, PickerKeyMapping.Resolve(Key.T, ModifierKeys.Control, malformed, true));
        Assert.Equal(PickerCommitAction.None, PickerKeyMapping.Resolve(Key.T, ModifierKeys.Control | ModifierKeys.Shift, text, true));
        Assert.Equal(PickerCommitAction.None, PickerKeyMapping.Resolve(Key.T, ModifierKeys.None, text, true));
    }

    [Fact]
    public async Task Cancellation_before_next_chunk_sends_no_more()
    {
        var sink = new DelaySink();
        var live = true;
        using var cts = new CancellationTokenSource();
        var running = new TypeOutRunner(sink, new TestFocus(42), (ms, token) => sink.Delay(token), 2)
            .RunAsync("abcd".ToCharArray(), () => live && !cts.IsCancellationRequested, 42, 10, cts.Token);
        Assert.True(sink.Started.Wait(TimeSpan.FromSeconds(2)));
        cts.Cancel();
        var result = await running;
        Assert.Equal(TypeOutStatus.Cancelled, result.Status);
        Assert.Equal(new[] { "ab" }, sink.Text);
    }

    [Fact]
    public async Task Multiunit_surrogate_is_not_split_at_chunk_boundary()
    {
        var sink = new TypeOutTests.FakeSink();
        var text = "abc\uD83D\uDE0Aef";
        var result = await new TypeOutRunner(sink, new TestFocus(42), chunkSize: 4)
            .RunAsync(text.ToCharArray(), () => true, 42, 0, default);
        Assert.Equal(TypeOutStatus.Completed, result.Status);
        Assert.Equal(text, sink.Text);
        Assert.Equal(text, string.Concat(sink.Chunks));
        Assert.All(sink.Chunks, chunk => Assert.False(char.IsHighSurrogate(chunk[^1]) || char.IsLowSurrogate(chunk[0])));
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

    private sealed class TestFocus(int handle) : IFocusProbe
    {
        public IntPtr ForegroundWindow => handle;
    }

    private sealed class DelaySink : IInputSink
    {
        public ManualResetEventSlim Started { get; } = new(false);
        public List<string> Text { get; } = new();
        public int Send(string text)
        {
            Text.Add(text);
            return text.Length * 2;
        }
        public Task Delay(CancellationToken token)
        {
            Started.Set();
            return Task.Delay(10000, token);
        }
    }
}
