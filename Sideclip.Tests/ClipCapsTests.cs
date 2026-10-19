using Sideclip.History;
using Xunit;

namespace Sideclip.Tests;

public class ClipCapsTests
{
    private static ClipEntry Text(string t, bool pinned = false, bool secret = false) =>
        new(t, secret, "test") { IsPinned = pinned };

    private static ClipEntry Image(int id, bool pinned = false) =>
        new(string.Empty, false, "test", TestProfile.Png(id)) { IsPinned = pinned };

    // Lists are newest first, like the store.
    private static string[] Names(IEnumerable<ClipEntry> e) =>
        e.Select(x => x.IsImage ? "IMG" + TestProfile.ImageId(x.ImagePng!) : x.TryGetNonSecretText() ?? "secret").ToArray();

    [Fact]
    public void Removes_oldest_first_and_only_the_excess()
    {
        var list = new List<ClipEntry> { Text("d"), Text("c"), Text("b"), Text("a") }; // a oldest
        var victims = ClipCaps.SelectOldestToRemove(list, ClipKind.Text, cap: 2, maxRemovals: 99);
        Assert.Equal(new[] { "a", "b" }, Names(victims));
    }

    [Fact]
    public void Never_removes_more_than_maxRemovals()
    {
        var list = new List<ClipEntry> { Text("d"), Text("c"), Text("b"), Text("a") };
        var victims = ClipCaps.SelectOldestToRemove(list, ClipKind.Text, cap: 1, maxRemovals: 1);
        Assert.Equal(new[] { "a" }, Names(victims));
    }

    [Fact]
    public void Nothing_removed_at_or_under_cap()
    {
        var list = new List<ClipEntry> { Text("b"), Text("a") };
        Assert.Empty(ClipCaps.SelectOldestToRemove(list, ClipKind.Text, 2, 5));
        Assert.Empty(ClipCaps.SelectOldestToRemove(list, ClipKind.Text, 3, 5));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(int.MinValue)]
    public void Unlimited_removes_nothing(int cap)
    {
        var list = Enumerable.Range(0, 50).Select(i => Text("t" + i)).ToList();
        Assert.Empty(ClipCaps.SelectOldestToRemove(list, ClipKind.Text, cap, 1000));
    }

    [Fact]
    public void Kinds_are_independent()
    {
        var list = new List<ClipEntry> { Text("t2"), Image(2), Text("t1"), Image(1) };
        Assert.Equal(new[] { "IMG1" }, Names(ClipCaps.SelectOldestToRemove(list, ClipKind.Image, 1, 9)));
        Assert.Equal(new[] { "t1" }, Names(ClipCaps.SelectOldestToRemove(list, ClipKind.Text, 1, 9)));
        Assert.Empty(ClipCaps.SelectOldestToRemove(list, ClipKind.Text, 2, 9));
    }

    [Fact]
    public void Pinned_clips_are_not_counted_and_never_selected()
    {
        // 3 text clips, the oldest is pinned: only 2 count, cap 2 -> nothing to remove.
        var list = new List<ClipEntry> { Text("c"), Text("b"), Text("a", pinned: true) };
        Assert.Empty(ClipCaps.SelectOldestToRemove(list, ClipKind.Text, 2, 9));
        Assert.Equal(2, ClipCaps.CountOf(list, ClipKind.Text));

        // Cap 1: the oldest UNPINNED one goes, not the pinned one.
        var victims = ClipCaps.SelectOldestToRemove(list, ClipKind.Text, 1, 9);
        Assert.Equal(new[] { "b" }, Names(victims));
    }

    [Fact]
    public void Secrets_are_not_counted_or_selected()
    {
        var list = new List<ClipEntry> { Text("b"), Text("s", secret: true), Text("a") };
        Assert.Equal(2, ClipCaps.CountOf(list, ClipKind.Text));
        var victims = ClipCaps.SelectOldestToRemove(list, ClipKind.Text, 1, 9);
        Assert.Equal(new[] { "a" }, Names(victims));
    }

    [Fact]
    public void Oldest_is_sequence_position_not_CopiedAt()
    {
        // Same CopiedAt (a tie) and reversed CopiedAt (what builds before caps wrote
        // after every restart): the sequence decides, deterministically.
        var t = DateTime.UtcNow;
        var newest = new ClipEntry("new", false, "x", null, t.AddDays(-9));
        var middle = new ClipEntry("mid", false, "x", null, t);
        var oldest = new ClipEntry("old", false, "x", null, t);
        var list = new List<ClipEntry> { newest, middle, oldest };
        Assert.Equal(new[] { "old" }, Names(ClipCaps.SelectOldestToRemove(list, ClipKind.Text, 2, 9)));
        Assert.Equal(new[] { "old", "mid" }, Names(ClipCaps.SelectOldestToRemove(list, ClipKind.Text, 1, 9)));
    }

    [Theory]
    // old, new, stored, expected removals
    [InlineData(100, 50, 80, 30)]   // lowering below stored
    [InlineData(100, 50, 40, 0)]    // lowering but still above stored
    [InlineData(50, 100, 80, 0)]    // raising removes nothing
    [InlineData(50, 50, 80, 0)]     // same cap removes nothing (over-cap stays untouched)
    [InlineData(0, 10, 25, 15)]     // Unlimited -> N counts as lowering
    [InlineData(10, 0, 25, 0)]      // N -> Unlimited removes nothing
    [InlineData(0, 0, 25, 0)]
    [InlineData(100, 1, 1, 0)]
    public void PlanLowering_gives_the_exact_count(int oldCap, int newCap, int stored, int expected) =>
        Assert.Equal(expected, ClipCaps.PlanLowering(oldCap, newCap, stored));
}