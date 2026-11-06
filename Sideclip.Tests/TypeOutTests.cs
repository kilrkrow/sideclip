using System.IO;
using System.Runtime.CompilerServices;
using Sideclip.History;
using Sideclip.Settings;
using Sideclip.TypeOut;

namespace Sideclip.Tests;

public sealed class TypeOutTests
{
    [Fact]
    public void TestFidelityFixturesAreExplicitUnicode()
    {
        var bmp = "caf\u00E9";
        var emoji = "\uD83D\uDE0A";
        var source = "plain\r\nnext\tend " + emoji;
        Assert.Equal(0x00E9, bmp[^1]);
        Assert.Equal(2, emoji.Length);
        Assert.Equal(0xD83D, (int)emoji[0]);
        Assert.Equal(0xDE0A, (int)emoji[1]);
        Assert.Null(TypeOutText.Validate(bmp + " " + emoji));
        Assert.Equal("plain\rnext\tend " + emoji, TypeOutText.Normalize(source));
    }
    [Fact]
    public void Text_accepted_is_preserved_and_newlines_normalized()
    {
        var text = "one\r\ntwo\nthree\rfour\t\"separator\" caf\u00E9 \uD83D\uDE0A";
        Assert.Null(TypeOutText.Validate(text));
        Assert.Equal("one\rtwo\rthree\rfour\t\"separator\" caf\u00E9 \uD83D\uDE0A", TypeOutText.Normalize(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("\0")]
    [InlineData("\u0001")]
    [InlineData("\u0007")]
    public void Unsupported_text_is_refused_before_send(string text) => Assert.NotNull(TypeOutText.Validate(text));

    [Fact]
    public void Both_malformed_surrogate_directions_are_refused()
    {
        Assert.NotNull(TypeOutText.Validate("bad\uD800"));
        Assert.NotNull(TypeOutText.Validate("bad\uDC00"));
        Assert.Null(TypeOutText.Validate("valid\uD83D\uDE0A"));
    }

    [Fact]
    public void Too_large_text_is_bounded() => Assert.NotNull(TypeOutText.Validate(new string('x', TypeOutText.MaxLength + 1)));

    [Fact]
    public async Task Fake_sink_receives_exact_unicode_and_tabs()
    {
        var sink = new FakeSink();
        var focus = new FakeFocus(42);
        var result = await Run("plain\r\nnext\tend \uD83D\uDE0A", sink, focus, chunkSize: 4);
        Assert.Equal(TypeOutStatus.Completed, result.Status);
        Assert.Equal("plain\rnext\tend 😊", sink.Text);
        Assert.True(sink.Chunks.Count > 1);
        Assert.All(sink.Chunks, chunk => Assert.True(chunk.Length <= 16));
        Assert.DoesNotContain(sink.Chunks, chunk => char.IsHighSurrogate(chunk[^1]) || char.IsLowSurrogate(chunk[0]));
    }

    [Fact]
    public async Task Cancellation_stops_next_chunk()
    {
        var sink = new FakeSink();
        using var cts = new CancellationTokenSource();
        sink.AfterSend = () => cts.Cancel();
        var result = await Run("first-chunk-second-chunk", sink, new FakeFocus(42), 2, cts.Token);
        Assert.Equal(TypeOutStatus.Cancelled, result.Status);
        Assert.Equal("fi", sink.Text);
    }

    [Fact]
    public async Task Cancellation_during_delay_stops_cleanly()
    {
        var sink = new FakeSink();
        using var cts = new CancellationTokenSource();
        var result = await Run("abcdef", sink, new FakeFocus(42), 3, cts.Token, async (_, token) => { cts.Cancel(); await Task.Delay(Timeout.Infinite, token); });
        Assert.Equal(TypeOutStatus.Cancelled, result.Status);
        Assert.Equal("abc", sink.Text);
    }

    [Fact]
    public async Task Focus_change_sends_no_later_input()
    {
        var sink = new FakeSink();
        var focus = new FakeFocus(42);
        sink.AfterSend = () => focus.Handle = 99;
        var result = await Run("abcdef", sink, focus, 3);
        Assert.Equal(TypeOutStatus.FocusChanged, result.Status);
        Assert.Equal("abc", sink.Text);
    }

    [Fact]
    public async Task Zero_focus_sends_nothing()
    {
        var sink = new FakeSink();
        var result = await Run("abc", sink, new FakeFocus(0), 2);
        Assert.Equal(TypeOutStatus.FocusChanged, result.Status);
        Assert.Empty(sink.Chunks);
    }

    [Fact]
    public async Task Expired_secret_midstream_stops_before_next_chunk()
    {
        var sink = new FakeSink();
        var live = true;
        sink.AfterSend = () => live = false;
        var result = await Run("abcdef", sink, new FakeFocus(42), () => live, 2);
        Assert.Equal(TypeOutStatus.SourceGone, result.Status);
        Assert.Equal("ab", sink.Text);
    }

    [Fact]
    public async Task Partial_send_is_reported_as_partial()
    {
        var sink = new FakeSink { ToAccept = 9 };
        var result = await Run("ab😊cd", sink, new FakeFocus(42), 6);
        Assert.Equal(TypeOutStatus.PartialSend, result.Status);
        Assert.Equal("ab😊cd", sink.Text);
        Assert.Contains("may have been inserted", result.Message);
        Assert.DoesNotContain("finished", result.Message);
    }

    [Fact]
    public async Task Fully_failed_send_reports_zero_units()
    {
        var sink = new FakeSink { ToAccept = 0 };
        var result = await Run("abc", sink, new FakeFocus(42), 2);
        Assert.Equal(TypeOutStatus.PartialSend, result.Status);
        Assert.Contains("0 of 3", result.Message);
    }

    [Fact]
    public void Default_and_legacy_settings_are_off() => RunSta(DefaultAndLegacy);

    private static void DefaultAndLegacy()
    {
        var settings = new AppSettings();
        Assert.False(settings.TypeOutEnabled);
        Assert.Equal(12, settings.TypeOutDelayMs);
        Assert.Equal("Ctrl+Win+Shift+T", settings.TypeOutGoHotkey);
        var dir = TempSettings();
        try
        {
            File.WriteAllText(SettingsManager.SettingsPath, "{\"TtlSeconds\":12,\"Autostart\":false}");
            var migrated = SettingsManager.Load();
            Assert.False(migrated.TypeOutEnabled);
            Assert.False(migrated.Autostart);
            Assert.Equal(12, migrated.TypeOutDelayMs);
            Assert.Equal("Ctrl+Win+Shift+T", migrated.TypeOutGoHotkey);
        }
        finally { Environment.SetEnvironmentVariable("SIDECLIP_DATA_DIR", null); }
    }

    [Fact]
    public void Setting_round_trip_preserves_enabled_go_and_delay() => RunSta(SettingsRoundTrip);

    private static void SettingsRoundTrip()
    {
        var dir = TempSettings();
        try
        {
            var settings = new AppSettings { Autostart = false, TypeOutEnabled = true, TypeOutGoHotkey = "Ctrl+Alt+T", TypeOutDelayMs = 80 };
            SettingsManager.Save(settings);
            var actual = SettingsManager.Load();
            Assert.True(actual.TypeOutEnabled);
            Assert.Equal("Ctrl+Alt+T", actual.TypeOutGoHotkey);
            Assert.Equal(80, actual.TypeOutDelayMs);
        }
        finally { Environment.SetEnvironmentVariable("SIDECLIP_DATA_DIR", null); }
    }

    [Theory]
    [InlineData("win+v")]
    [InlineData("CONTROL+WINDOWS+V")]
    [InlineData("Ctrl+Win+P")]
    public void Reserved_chords_are_rejected(string chord) => Assert.True(Sideclip.Hotkeys.HotkeyParser.IsForbidden(chord));

    [Fact]
    public void Picker_screenshot_existing_bindings_remain_unchanged()
    {
        var settings = new AppSettings();
        Assert.Equal("Ctrl+Shift+Win+V", settings.PickerHotkey);
        Assert.Equal("Ctrl+Shift+Win+P", settings.ScreenshotHotkey);
        Assert.False(Sideclip.Hotkeys.HotkeyParser.IsForbidden(settings.TypeOutGoHotkey));
    }

    [Fact]
    public void Disabled_session_cannot_arm_or_type()
    {
        var sink = new FakeSink();
        var session = MakeSession(new ClipEntry("hello", false, "test"), sink, out _, out _);
        Assert.False(session.Arm(new ClipEntry("hello", false, "test")));
        session.Go(5);
        Assert.Empty(sink.Chunks);
        session.Dispose();
    }

    [Fact]
    public async Task Session_requires_explicit_go_and_only_runs_once()
    {
        var sink = new FakeSink();
        var entry = new ClipEntry("abc 😊", false, "test");
        var session = MakeSession(entry, sink, out var active, out _);
        session.SetEnabled(true);
        Assert.True(session.Arm(entry));
        Assert.True(active.Value);
        Assert.Empty(sink.Chunks);
        session.Go(7);
        session.Go(7);
        await Pump(() => !session.HasEntry);
        Assert.Equal("abc 😊", sink.Text);
        Assert.Equal(3, sink.Chunks.Count);
        Assert.Equal(7, sink.Delay);
        Assert.False(active.Value);
        session.Cancel();
        session.Dispose();
    }

    [Fact]
    public void Failed_hotkey_registration_never_arms()
    {
        var sink = new FakeSink();
        var entry = new ClipEntry("abc", false, "test");
        var messages = new List<string>();
        var session = new TypeOutSession(e => e.HasPayload, messages.Add,
            () => new TypeOutRunner(sink, new FakeFocus(42)), new FakeFocus(42), () => { }, action => action(),
            () => false, () => { });
        session.SetEnabled(true);
        Assert.False(session.Arm(entry));
        Assert.False(session.HasEntry);
        Assert.Contains(messages, message => message.Contains("could not be registered"));
    }

    [Fact]
    public async Task Runner_revalidates_malformed_payload()
    {
        var sink = new FakeSink();
        var payload = "bad\0after".ToCharArray();
        var result = await new TypeOutRunner(sink, new FakeFocus(42), chunkSize: 2).RunAsync(payload, () => true, 42, 7, default);
        Assert.Equal(TypeOutStatus.PartialSend, result.Status);
        Assert.Empty(sink.Chunks);
        Assert.All(payload, ch => Assert.Equal('\0', ch));
    }

    [Fact]
    public void Entry_image_empty_file_and_secret_wipe_cannot_arm()
    {
        var normal = new ClipEntry("abc", false, "test");
        Assert.False(TypeOutSession.CanType(new ClipEntry("", false, "test"), true));
        Assert.False(TypeOutSession.CanType(new ClipEntry("", false, "test", new byte[] { 1, 2, 3 }), true));
        var secret = new ClipEntry("abc", true, "test");
        Assert.True(TypeOutSession.CanType(secret, true));
        secret.WipePayload();
        Assert.False(TypeOutSession.CanType(secret, true));
    }

    [Fact]
    public void Stale_selection_and_second_cancel_are_safe()
    {
        var sink = new FakeSink();
        var entry = new ClipEntry("abc", true, "test");
        var session = MakeSession(entry, sink, out _, out var messages);
        session.SetEnabled(true);
        Assert.True(session.Arm(entry));
        entry.WipePayload();
        session.EntryChanged(entry);
        session.Cancel();
        session.Cancel();
        session.Go(0);
        Assert.False(session.HasEntry);
        Assert.Empty(sink.Chunks);
        Assert.Null(entry.TryGetText());
        Assert.DoesNotContain(messages, m => m.Contains("armed"));
    }

    [Fact]
    public async Task Live_secret_typeout_does_not_change_manually_managed_ttl_metadata()
    {
        var sink = new FakeSink();
        var entry = new ClipEntry("not-a-real-secret", true, "test");
        var session = MakeSession(entry, sink, out _, out _);
        session.SetEnabled(true);
        session.Arm(entry);
        await Task.Delay(40);
        session.Go(0);
        await Pump(() => !session.HasEntry);
        Assert.Equal("not-a-real-secret", entry.TryGetText());
        Assert.True(entry.IsSecret);
        var expectedAge = (DateTime.UtcNow - entry.CopiedAt).TotalMilliseconds;
        Assert.InRange(expectedAge, 0, 5_000);
        Assert.Equal("not-a-real-secret", entry.TryGetText());
    }

    private static TypeOutSession MakeSession(ClipEntry entry, FakeSink sink, out StrongBox<bool> active, out List<string> messages)
    {
        var activation = new System.Runtime.CompilerServices.StrongBox<bool>(false);
        var reports = new List<string>();
        var session = new TypeOutSession(e => e.HasPayload && e.TryGetText() is not null, reports.Add,
            () => new TypeOutRunner(sink, new FakeFocus(42), (ms, token) => { sink.Delay = ms; return Task.CompletedTask; }, 2),
            new FakeFocus(42), () => { }, action => action(), () => { activation.Value = true; return true; }, () => activation.Value = false);
        active = activation;
        messages = reports;
        return session;
    }

        [Fact]
    public void Typeout_route_does_not_write_clipboard_or_persist_payload()
    {
        var runner = File.ReadAllText(Path.Combine(TypeOutSourceDirectory, "TypeOutRunner.cs"));
        var session = File.ReadAllText(Path.Combine(TypeOutSourceDirectory, "TypeOutSession.cs"));
        foreach (var source in new[] { runner, session })
        {
            Assert.DoesNotContain("Clipboard", source, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("File.", source, StringComparison.Ordinal);
            Assert.DoesNotContain("SetTtl(", source, StringComparison.Ordinal);
            Assert.DoesNotContain("StartSecretTimer(", source, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void UIButtons_are_keyboard_and_mouse_operable_without_unicode_ellipsis()
    {
        foreach (var file in new[] { "Picker\\PickerWindow.xaml", "Tray\\TrayService.cs", "Settings\\HotkeySettingsForm.cs", "TypeOut\\TypeOutSession.cs", "TypeOut\\TypeOutRunner.cs" })
        {
            var source = File.ReadAllText(Path.Combine(TypeOutSourceDirectory, "..", file));
            Assert.DoesNotContain("\u2026", source);
        }
    }

    [Fact]
    public void SourceExpiringWipesPayloadUnderLock()
    {
        var entry = new ClipEntry("no_real_secret", true, "test.exe");
        Parallel.For(0, 200, _ => entry.TryGetText());
        entry.WipePayload();
        Assert.Null(entry.TryGetText());
    }

    private static string TypeOutSourceDirectory
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory.Parent is not null && !Directory.Exists(Path.Combine(directory.FullName, "TypeOut"))) directory = directory.Parent;
            return Path.Combine(directory.FullName, "TypeOut");
        }
    }
private static string TempSettings()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SideclipTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable("SIDECLIP_DATA_DIR", dir);
        return dir;
    }

    private static Task<TypeOutResult> Run(string text, FakeSink sink, FakeFocus focus, int chunkSize = 16, CancellationToken token = default, Func<int, CancellationToken, Task>? delay = null)
        => Run(text, sink, focus, () => true, chunkSize, token, delay);

    private static Task<TypeOutResult> Run(string text, FakeSink sink, FakeFocus focus, Func<bool> live, int chunkSize = 16, CancellationToken token = default, Func<int, CancellationToken, Task>? delay = null)
        => new TypeOutRunner(sink, focus, delay ?? ((_, _) => Task.CompletedTask), chunkSize).RunAsync(text.ToCharArray(), live, focus.Handle, 12, token);

    private static void RunSta(Action test)
    {
        Exception? failure = null;
        var thread = new Thread(() => { try { test(); } catch (Exception ex) { failure = ex; } });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();
        if (failure is not null) throw failure;
    }

    private static async Task Pump(Func<bool> until)
    {
        for (var i = 0; i < 100; i++)
        {
            if (until()) return;
            await Task.Delay(5);
        }
        Assert.True(until(), "Timed out waiting for deterministic fake result.");
    }


    [Fact]
    public async Task Concurrent_payload_reads_while_wiping_never_resurrect_payload()
    {
        for (var repeat = 0; repeat < 100; repeat++)
        {
            var entry = new ClipEntry("bounded synthetic payload", isSecret: true, "fixture");
            var errors = 0;
            var done = 0;
            var reader = Task.Run(() =>
            {
                try
                {
                    while (Volatile.Read(ref done) == 0)
                    {
                        var observed = entry.TryGetText();
                        if (observed is not null && observed != "bounded synthetic payload") Interlocked.Increment(ref errors);
                    }
                }
                catch { Interlocked.Increment(ref errors); }
            });
            entry.WipePayload();
            Volatile.Write(ref done, 1);
            var completed = await Task.WhenAny(reader, Task.Delay(2_000));
            Assert.True(ReferenceEquals(completed, reader), "Reader did not finish.");
            await reader;
            Assert.Equal(0, errors);
            Assert.True(entry.TryGetText() is null, "Payload reappeared after wipe.");
        }
    }

    [Fact]
    public void StoreLiveMembershipAndWipeAreConsistent()
    {
        var old = Environment.GetEnvironmentVariable("SIDECLIP_DATA_DIR");
        var dir = Path.Combine(Path.GetTempPath(), "SideclipTests-" + Guid.NewGuid().ToString("N"));
        try
        {
            Environment.SetEnvironmentVariable("SIDECLIP_DATA_DIR", dir);
            var sta = new Thread(() =>
            {
                using var store = new ClipHistoryStore(System.Windows.Threading.Dispatcher.CurrentDispatcher, _ => { });
                var entry = store.Add("fixture", true, "fixture");
                Assert.True(store.IsPickerSource(entry));
                store.RemoveEntry(entry);
                Assert.False(store.IsPickerSource(entry));
                Assert.Null(entry.TryGetText());
            });
            sta.SetApartmentState(ApartmentState.STA);
            sta.Start();
            sta.Join();
        }
        finally { Environment.SetEnvironmentVariable("SIDECLIP_DATA_DIR", old); }
    }
    public sealed class FakeFocus : IFocusProbe
    {
        internal FakeFocus(nint handle) => Handle = handle;
        public nint Handle { get; set; }
        public nint ForegroundWindow => Handle;
    }

    public sealed class FakeSink : IInputSink
    {
        public List<string> Chunks { get; } = new();
        public int ToAccept { get; set; } = -1;
        public Action? AfterSend { get; set; }
        public int Delay { get; set; }
        public string Text => string.Concat(Chunks);
        public int Send(string text)
        {
            Chunks.Add(text);
            AfterSend?.Invoke();
            return ToAccept < 0 ? text.Length * 2 : ToAccept;
        }
    }
}
