using System.Security.Cryptography;
using Sideclip.Screenshot;
using Sideclip.Settings;
using Xunit;

namespace Sideclip.Tests;

public class ScreenshotCopierTests
{
    private static string Hash(string path) => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(path)));

    private static byte[] FakePng(int seed)
    {
        var bytes = new byte[4096 + seed * 37];
        new Random(seed).NextBytes(bytes);
        return bytes;
    }

    private static void WaitFor(Func<bool> condition, int ms = 15000)
    {
        var until = DateTime.UtcNow.AddMilliseconds(ms);
        while (!condition() && DateTime.UtcNow < until)
            Thread.Sleep(50);
    }

    [Fact]
    public void Ten_rapid_screenshots_make_exactly_ten_copies()
    {
        using var dir = new TestDir();
        var src = dir.Sub("Screenshots");
        var dst = dir.Sub("Copies");
        File.WriteAllBytes(Path.Combine(src, "old.png"), FakePng(99)); // existed before: never copied
        Thread.Sleep(100);

        var issues = new List<string>();
        using var copier = new ScreenshotCopier(src, dst, new IssueNotifier(issues.Add));
        var copied = 0;
        copier.Copied += (_, _) => Interlocked.Increment(ref copied);
        copier.Start();
        Thread.Sleep(300);

        for (var i = 1; i <= 10; i++)
            File.WriteAllBytes(Path.Combine(src, $"Screenshot ({i}).png"), FakePng(i));
        // Touch some files again: Changed events must not produce duplicates.
        for (var i = 1; i <= 10; i += 3)
            File.SetLastWriteTimeUtc(Path.Combine(src, $"Screenshot ({i}).png"), DateTime.UtcNow);

        WaitFor(() => Volatile.Read(ref copied) >= 10);
        Thread.Sleep(1500);

        var copies = Directory.GetFiles(dst);
        Assert.Equal(10, copies.Length);
        Assert.Equal(10, Volatile.Read(ref copied));
        for (var i = 1; i <= 10; i++)
        {
            var name = $"Screenshot ({i}).png";
            Assert.Equal(Convert.ToHexString(SHA256.HashData(FakePng(i))), Hash(Path.Combine(src, name)));
            Assert.Equal(Hash(Path.Combine(src, name)), Hash(Path.Combine(dst, name)));
        }

        Assert.Equal(11, Directory.GetFiles(src).Length);
        Assert.Empty(issues);
    }

    [Fact]
    public void Name_collision_gets_suffix_and_never_overwrites()
    {
        using var dir = new TestDir();
        var src = dir.Sub("Screenshots");
        var dst = dir.Sub("Copies");
        File.WriteAllText(Path.Combine(dst, "shot.png"), "existing");
        File.WriteAllText(Path.Combine(dst, "shot (1).png"), "existing 1");

        using var copier = new ScreenshotCopier(src, dst, new IssueNotifier(_ => { }));
        string? copy = null;
        copier.Copied += (_, c) => copy = c;
        copier.Start();
        Thread.Sleep(300);
        File.WriteAllBytes(Path.Combine(src, "shot.png"), FakePng(1));
        WaitFor(() => copy is not null);

        Assert.Equal(Path.Combine(dst, "shot (2).png"), copy);
        Assert.Equal("existing", File.ReadAllText(Path.Combine(dst, "shot.png")));
        Assert.Equal("existing 1", File.ReadAllText(Path.Combine(dst, "shot (1).png")));
    }

    [Fact]
    public void Waits_until_the_writer_has_finished()
    {
        using var dir = new TestDir();
        var src = dir.Sub("Screenshots");
        var dst = dir.Sub("Copies");
        using var copier = new ScreenshotCopier(src, dst, new IssueNotifier(_ => { }));
        string? copy = null;
        copier.Copied += (_, c) => copy = c;
        copier.Start();
        Thread.Sleep(300);

        var full = FakePng(7);
        using (var w = new FileStream(Path.Combine(src, "slow.png"), FileMode.CreateNew, FileAccess.Write, FileShare.None))
        {
            w.Write(full, 0, 100);
            w.Flush();
            Thread.Sleep(1200);
            w.Write(full, 100, full.Length - 100);
        }

        WaitFor(() => copy is not null);
        Assert.NotNull(copy);
        Assert.Equal(full, File.ReadAllBytes(copy!));
    }

    [Fact]
    public void Non_images_are_ignored_and_rename_to_png_is_copied()
    {
        using var dir = new TestDir();
        var src = dir.Sub("Screenshots");
        var dst = dir.Sub("Copies");
        using var copier = new ScreenshotCopier(src, dst, new IssueNotifier(_ => { }));
        var copied = 0;
        copier.Copied += (_, _) => Interlocked.Increment(ref copied);
        copier.Start();
        Thread.Sleep(300);

        File.WriteAllText(Path.Combine(src, "notes.txt"), "x");
        File.WriteAllBytes(Path.Combine(src, "capture.tmp"), FakePng(3));
        File.Move(Path.Combine(src, "capture.tmp"), Path.Combine(src, "capture.png"));
        WaitFor(() => Volatile.Read(ref copied) >= 1);
        Thread.Sleep(800);

        Assert.Equal(new[] { "capture.png" }, Directory.GetFiles(dst).Select(Path.GetFileName));
    }

    [Fact]
    public void Missing_destination_reports_once()
    {
        using var dir = new TestDir();
        var src = dir.Sub("Screenshots");
        var dst = Path.Combine(dir.Path, "gone");
        var issues = new List<string>();
        using var copier = new ScreenshotCopier(src, dst, new IssueNotifier(m => { lock (issues) issues.Add(m); }));
        copier.Start();
        Thread.Sleep(300);
        for (var i = 0; i < 5; i++)
            File.WriteAllBytes(Path.Combine(src, $"s{i}.png"), FakePng(i));
        WaitFor(() => issues.Count > 0);
        Thread.Sleep(1000);

        Assert.Single(issues);
        Assert.False(Directory.Exists(dst));
    }

    [Fact]
    public void Starts_later_when_screenshots_folder_does_not_exist_yet()
    {
        using var dir = new TestDir();
        var src = Path.Combine(dir.Path, "Screenshots");
        var dst = dir.Sub("Copies");
        using var copier = new ScreenshotCopier(src, dst, new IssueNotifier(_ => { }));
        string? copy = null;
        copier.Copied += (_, c) => copy = c;
        copier.Start();
        Directory.CreateDirectory(src);
        Thread.Sleep(6000); // attach timer runs every 5 s
        File.WriteAllBytes(Path.Combine(src, "first.png"), FakePng(1));
        WaitFor(() => copy is not null);
        Assert.Equal(Path.Combine(dst, "first.png"), copy);
    }
}

public class ScreenshotRulesTests
{
    [Fact]
    public void Copy_folder_rules()
    {
        using var dir = new TestDir();
        var shots = dir.Sub("Screenshots");
        Assert.False(ScreenshotRules.IsCopyFolderValid("", shots, out _));
        Assert.False(ScreenshotRules.IsCopyFolderValid("relative\\path", shots, out _));
        Assert.False(ScreenshotRules.IsCopyFolderValid(Path.Combine(dir.Path, "missing"), shots, out _));
        Assert.False(ScreenshotRules.IsCopyFolderValid(shots + "\\", shots, out var why));
        Assert.Contains("Screenshots", why);
        Assert.True(ScreenshotRules.IsCopyFolderValid(dir.Sub("Copies"), shots, out _));
    }

    [Fact]
    public void Inside_checks_segment_boundaries()
    {
        Assert.True(ScreenshotRules.IsInside(@"D:\Vault\a\b.png", @"D:\Vault"));
        Assert.True(ScreenshotRules.IsInside(@"D:\Vault", @"d:\vault\"));
        Assert.False(ScreenshotRules.IsInside(@"D:\Vault2\b.png", @"D:\Vault"));
        Assert.True(ScreenshotRules.IsInside(@"D:\x.png", @"D:\"));
    }
}

public class IssueNotifierTests
{
    [Fact]
    public void Debounces_per_kind_and_clear_rearms()
    {
        var now = new DateTime(2026, 1, 1);
        var shown = new List<string>();
        var n = new IssueNotifier(shown.Add, TimeSpan.FromMinutes(10), () => now);
        Assert.True(n.Report("a", "1"));
        Assert.False(n.Report("a", "2"));
        Assert.True(n.Report("b", "3"));
        now = now.AddMinutes(11);
        Assert.True(n.Report("a", "4"));
        n.Clear("a");
        Assert.True(n.Report("a", "5"));
        Assert.Equal(new[] { "1", "3", "4", "5" }, shown);
    }
}
