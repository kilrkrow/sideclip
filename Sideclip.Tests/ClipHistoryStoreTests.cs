using System.IO;
using System.Text;
using System.Windows.Threading;
using Sideclip.History;

namespace Sideclip.Tests;

/// <summary>
/// Tests that secrets are never written to history.json regardless of TTL state.
/// Each test runs on a dedicated STA thread (required for Dispatcher) with an
/// isolated temp directory so the developer's real history is never touched.
/// </summary>
public class ClipHistoryStoreTests
{
    // Helpers

    private static string SetupTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "SideclipTests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Environment.SetEnvironmentVariable("SIDECLIP_DATA_DIR", dir);
        return dir;
    }

    private static void CleanupTempDir(string dir)
    {
        Environment.SetEnvironmentVariable("SIDECLIP_DATA_DIR", null);
        try { Directory.Delete(dir, recursive: true); } catch { }
    }

    private static ClipHistoryStore MakeStore(bool ttlEnabled = true)
    {
        var dispatcher = Dispatcher.CurrentDispatcher;
        return new ClipHistoryStore(dispatcher, _ => { }, TimeSpan.FromSeconds(30), ttlEnabled);
    }

    private static string HistoryJson(string dir)
    {
        var path = Path.Combine(dir, "history.json");
        return File.Exists(path) ? File.ReadAllText(path, Encoding.UTF8) : string.Empty;
    }

    private static void RunSta(Action test)
    {
        Exception? ex = null;
        var t = new Thread(() =>
        {
            try { test(); }
            catch (Exception e) { ex = e; }
        });
        t.SetApartmentState(ApartmentState.STA);
        t.Start();
        t.Join();
        if (ex is not null)
            throw new Xunit.Sdk.XunitException("STA test failed: " + ex);
    }

    // Test 1: Secret with TTL on is not persisted.
    [Fact]
    public void Secret_TtlOn_NotWrittenToDisk() => RunSta(() =>
    {
        var dir = SetupTempDir();
        try
        {
            using var store = MakeStore(ttlEnabled: true);
            store.Add("s3cret-Pa55!", isSecret: true, "test.exe");
            var json = HistoryJson(dir);
            Assert.DoesNotContain("s3cret-Pa55!", json);
        }
        finally { CleanupTempDir(dir); }
    });

    // Test 2: Disabling TTL leaves IsSecret true and keeps secrets off disk.
    [Fact]
    public void Secret_TtlDisabled_StaysSecretAndNotWrittenToDisk() => RunSta(() =>
    {
        var dir = SetupTempDir();
        try
        {
            using var store = MakeStore(ttlEnabled: true);
            var entry = store.Add("s3cret-Pa55!", isSecret: true, "test.exe");

            store.SetTtl(TimeSpan.FromSeconds(30), enabled: false);

            Assert.True(entry.IsSecret);
            Assert.Null(entry.TryGetNonSecretText()); // persist guard still returns null for secrets
            Assert.NotNull(entry.TryGetText());       // but text is readable for picker display
            var json = HistoryJson(dir);
            Assert.DoesNotContain("s3cret-Pa55!", json);
        }
        finally { CleanupTempDir(dir); }
    });

    // Test 3: New secret added while TTL is off is still not persisted.
    [Fact]
    public void Secret_AddedWhileTtlOff_NotWrittenToDisk() => RunSta(() =>
    {
        var dir = SetupTempDir();
        try
        {
            using var store = MakeStore(ttlEnabled: true);
            store.SetTtl(TimeSpan.FromSeconds(30), enabled: false);
            store.Add("another-Pa55!", isSecret: true, "test.exe");

            var json = HistoryJson(dir);
            Assert.DoesNotContain("another-Pa55!", json);
        }
        finally { CleanupTempDir(dir); }
    });

    // Test 4: Re-enabling TTL still keeps secrets off disk.
    [Fact]
    public void Secret_TtlReEnabled_StillNotWrittenToDisk() => RunSta(() =>
    {
        var dir = SetupTempDir();
        try
        {
            using var store = MakeStore(ttlEnabled: true);
            store.Add("s3cret-Pa55!", isSecret: true, "test.exe");
            store.SetTtl(TimeSpan.FromSeconds(30), enabled: false);
            store.SetTtl(TimeSpan.FromSeconds(30), enabled: true);

            var json = HistoryJson(dir);
            Assert.DoesNotContain("s3cret-Pa55!", json);
        }
        finally { CleanupTempDir(dir); }
    });

    // Test 5: Non-secret is persisted after TTL is disabled.
    [Fact]
    public void NonSecret_TtlDisabled_IsWrittenToDisk() => RunSta(() =>
    {
        var dir = SetupTempDir();
        try
        {
            using var store = MakeStore(ttlEnabled: true);
            store.Add("https://example.com/x", isSecret: false, "chrome.exe");
            store.SetTtl(TimeSpan.FromSeconds(30), enabled: false);

            var json = HistoryJson(dir);
            Assert.Contains("https://example.com/x", json);
        }
        finally { CleanupTempDir(dir); }
    });
}
