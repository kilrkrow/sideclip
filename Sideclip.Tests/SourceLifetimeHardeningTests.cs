using System.Collections.Concurrent;
using System.IO;
using System.Reflection;
using System.Windows.Threading;
using Sideclip.History;
using Sideclip.TypeOut;

namespace Sideclip.Tests;

public sealed class SourceLifetimeHardeningTests
{
    private static void WithStore(Action<ClipHistoryStore> body, TimeSpan? ttl = null, bool enabled = true)
    {
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var prior = Environment.GetEnvironmentVariable("SIDECLIP_DATA_DIR");
            var dir = Path.Combine(Path.GetTempPath(), "SideclipTests-" + Guid.NewGuid());
            Environment.SetEnvironmentVariable("SIDECLIP_DATA_DIR", dir);
            try { using var store = new ClipHistoryStore(Dispatcher.CurrentDispatcher, _ => { }, ttl, enabled); body(store); }
            catch (Exception ex) { failure = ex; }
            finally { Environment.SetEnvironmentVariable("SIDECLIP_DATA_DIR", prior); if (Directory.Exists(dir)) Directory.Delete(dir, true); }
        });
        thread.SetApartmentState(ApartmentState.STA); thread.Start(); thread.Join();
        if (failure is not null) System.Runtime.ExceptionServices.ExceptionDispatchInfo.Capture(failure).Throw();
    }

    [Fact]
    public void Same_or_longer_TTL_apply_cannot_extend_deadline_or_resurrect_elapsed_secret()
    {
        WithStore(store =>
        {
            var entry = store.Add("synthetic", true, "test");
            var deadlines = (Dictionary<ClipEntry, DateTime>)typeof(ClipHistoryStore).GetField("_expires", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;
            var original = deadlines[entry];
            store.SetTtl(TimeSpan.FromSeconds(30), true); Assert.Equal(original, deadlines[entry]);
            store.SetTtl(TimeSpan.FromSeconds(60), true); Assert.Equal(original, deadlines[entry]);
            deadlines[entry] = DateTime.UtcNow.AddMilliseconds(-1);
            store.SetTtl(TimeSpan.FromSeconds(60), false);
            Assert.False(entry.HasPayload); Assert.False(store.IsTypeOutSource(entry));
            store.SetTtl(TimeSpan.FromSeconds(60), true); Assert.False(store.IsTypeOutSource(entry));
        }, TimeSpan.FromSeconds(30));
    }

    [Fact]
    public void TTL_off_live_secret_can_type_and_selection_does_not_install_deadline()
    {
        WithStore(store =>
        {
            var entry = store.Add("synthetic", true, "test");
            var sink = new TypeOutTests.FakeSink();
            using var session = new TypeOutSession(store.IsTypeOutSource, _ => { }, () => new TypeOutRunner(sink, new TypeOutTests.FakeFocus(42)),
                new TypeOutTests.FakeFocus(42), () => { }, action => action());
            session.SetEnabled(true); Assert.True(session.SelectCandidate(entry)); session.Go(0);
            Assert.True(SpinWait.SpinUntil(() => !session.HasEntry, TimeSpan.FromSeconds(3)));
            Assert.Equal("synthetic", sink.Text); Assert.Equal("synthetic", entry.TryGetText());
            var deadlines = (Dictionary<ClipEntry, DateTime>)typeof(ClipHistoryStore).GetField("_expires", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(store)!;
            Assert.Empty(deadlines);
        }, enabled: false);
    }

    [Fact]
    public void Trimming_even_an_old_secret_invalidates_identity_and_bounds_history()
    {
        WithStore(store =>
        {
            var first = store.Add("old synthetic", true, "test");
            var invalidated = 0; store.EntryInvalidated += entry => { if (ReferenceEquals(entry, first)) invalidated++; };
            for (var i = 0; i < ClipHistoryStore.MaxEntries; i++) store.Add("synthetic " + i, true, "test");
            Assert.Equal(ClipHistoryStore.MaxEntries, store.PickerEntries().Count());
            Assert.Equal(1, invalidated); Assert.False(first.HasPayload); Assert.False(store.IsTypeOutSource(first));
        }, enabled: false);
    }

    [Fact]
    public void Background_membership_and_payload_reads_survive_UI_add_remove_clear()
    {
        WithStore(store =>
        {
            var entry = store.Add("synthetic", true, "test");
            using var done = new CancellationTokenSource();
            var reader = Task.Run(() => { while (!done.IsCancellationRequested) { _ = store.IsTypeOutSource(entry); _ = entry.PayloadEquals("synthetic"); _ = store.PickerEntries().Count(); } });
            for (var i = 0; i < 100; i++) { var added = store.Add("synthetic " + i, true, "test"); store.RemoveEntry(added); }
            store.ClearAll(); done.Cancel(); Assert.True(reader.Wait(TimeSpan.FromSeconds(3)));
            Assert.False(store.IsTypeOutSource(entry)); Assert.False(entry.PayloadEquals("synthetic"));
            Assert.Null(entry.CopyTypeOutPayload()); Assert.Empty(store.PickerEntries());
        });
    }

    [Theory]
    [InlineData("Cancel")]
    [InlineData("Off")]
    [InlineData("Invalidate")]
    [InlineData("Dispose")]
    public void Reentrant_stop_during_modifier_release_never_sends_or_leaves_a_ghost(string stop)
    {
        var entry = new ClipEntry("synthetic", true, "test");
        var sink = new TypeOutTests.FakeSink(); TypeOutSession? session = null;
        var esc = false;
        session = new TypeOutSession(e => e.HasPayload, _ => { }, () => new TypeOutRunner(sink, new TypeOutTests.FakeFocus(42)),
            new TypeOutTests.FakeFocus(42), () =>
            {
                Assert.False(session!.SelectCandidate(new ClipEntry("replacement", false, "test")));
                if (stop == "Off") session.SetEnabled(false);
                else if (stop == "Invalidate") { entry.WipePayload(); session.EntryChanged(entry); }
                else if (stop == "Dispose") session.Dispose();
                else session.Cancel("Settings opened; selection cancelled.");
            }, action => action(), () => esc = true, () => esc = false);
        using (session)
        {
            session.SetEnabled(true); session.SelectCandidate(entry); session.Go(0);
            Assert.Empty(sink.Chunks); Assert.False(session.HasEntry); Assert.False(session.IsActive); Assert.False(esc);
        }
    }

    [Theory]
    [InlineData("RunnerFactory")]
    [InlineData("ModifierRelease")]
    public void Startup_exception_clears_candidate_Esc_and_job_state_without_payload_logging(string operation)
    {
        var sink = new TypeOutTests.FakeSink(); var reports = new ConcurrentQueue<string>(); var esc = false;
        using var session = new TypeOutSession(e => e.HasPayload, reports.Enqueue,
            () => operation == "RunnerFactory" ? throw new InvalidOperationException("DO NOT LOG SYNTHETIC PAYLOAD") : new TypeOutRunner(sink, new TypeOutTests.FakeFocus(42)),
            new TypeOutTests.FakeFocus(42), () => { if (operation == "ModifierRelease") throw new InvalidOperationException("DO NOT LOG SYNTHETIC PAYLOAD"); },
            action => action(), () => esc = true, () => esc = false);
        session.SetEnabled(true); session.SelectCandidate(new ClipEntry("synthetic", true, "test")); session.Go(0);
        Assert.Empty(sink.Chunks); Assert.False(session.HasEntry); Assert.False(session.IsActive); Assert.False(esc);
        Assert.Contains(reports, message => message.Contains("failed to start safely"));
        Assert.DoesNotContain(reports, message => message.Contains("DO NOT LOG"));
    }
    [Fact]
    public void UI_dispatch_failure_cancels_and_reclaims_started_job_without_false_zero_send_claim()
    {
        var sink = new TypeOutTests.FakeSink();
        var reports = new ConcurrentQueue<string>();
        var esc = false;
        using var session = new TypeOutSession(e => e.HasPayload, reports.Enqueue,
            () => new TypeOutRunner(sink, new TypeOutTests.FakeFocus(42)),
            new TypeOutTests.FakeFocus(42), () => { },
            _ => throw new InvalidOperationException("DO NOT LOG SYNTHETIC PAYLOAD"),
            () => esc = true, () => esc = false);
        session.SetEnabled(true);
        Assert.True(session.SelectCandidate(new ClipEntry(new string('x', 100000), true, "test")));
        session.Go(250);
        Assert.True(SpinWait.SpinUntil(() => !session.IsActive && !session.HasEntry, TimeSpan.FromSeconds(3)));
        Assert.False(esc);
        var chunks = sink.Chunks.Count;
        session.Go(0);
        Assert.Equal(chunks, sink.Chunks.Count);
        Assert.DoesNotContain(reports, message => message.Contains("No input job was started") || message.Contains("DO NOT LOG"));
    }
}
