using System.IO;
using System.Text.Json;
using Sideclip.History;
using Sideclip.Settings;
using Xunit;

namespace Sideclip.Tests;

public class ClipHistoryStoreTests
{
    private static ClipEntry AddText(ClipHistoryStore s, string t) => s.Add(t, false, "test");
    private static ClipEntry AddImage(ClipHistoryStore s, int id) => s.Add(string.Empty, false, "test", TestProfile.Png(id));

    [Fact]
    public void Text_cap_drops_the_oldest_text_and_leaves_images_alone()
    {
        using var p = new TestProfile();
        using var s = p.Open(maxText: 3);
        AddImage(s, 1);
        foreach (var t in new[] { "t1", "t2", "t3", "t4", "t5" })
            AddText(s, t);

        Assert.Equal(new[] { "t5", "t4", "t3", "IMG:1" }, TestProfile.Order(s));
        Assert.Single(p.ImageFiles());
        Assert.Equal(4, s.Rows.Count);
    }

    [Fact]
    public void Image_cap_drops_the_oldest_image_and_deletes_its_file()
    {
        using var p = new TestProfile();
        using var s = p.Open(maxImages: 2);
        AddText(s, "keep me");
        AddImage(s, 1);
        AddImage(s, 2);
        Assert.Equal(2, p.ImageFiles().Length);

        AddImage(s, 3);

        Assert.Equal(new[] { "IMG:3", "IMG:2", "keep me" }, TestProfile.Order(s));
        var files = p.ImageFiles();
        Assert.Equal(2, files.Length);
        var onDisk = files.Select(f => TestProfile.ImageId(File.ReadAllBytes(Path.Combine(p.Images, f)))).OrderBy(x => x).ToArray();
        Assert.Equal(new[] { 2, 3 }, onDisk);
    }

    [Fact]
    public void Caps_survive_a_restart_with_the_right_survivors_in_order()
    {
        using var p = new TestProfile();
        using (var s = p.Open(maxText: 2, maxImages: 2))
        {
            foreach (var i in Enumerable.Range(1, 4)) { AddText(s, "t" + i); AddImage(s, i); }
        }

        using var again = p.Open(maxText: 2, maxImages: 2);
        Assert.Equal(new[] { "IMG:4", "t4", "IMG:3", "t3" }, TestProfile.Order(again));
        Assert.Equal(2, p.ImageFiles().Length);
    }

    [Fact]
    public void Rapid_copies_at_cap_keep_exactly_the_cap_and_no_orphan_files()
    {
        using var p = new TestProfile();
        using var s = p.Open(maxText: 10, maxImages: 5);
        for (var i = 1; i <= 200; i++)
        {
            AddImage(s, i);
            AddText(s, "t" + i);
        }

        Assert.Equal(10, s.CountCapped(ClipKind.Text));
        Assert.Equal(5, s.CountCapped(ClipKind.Image));
        Assert.Equal(15, s.Rows.Count);
        Assert.Equal("t200", TestProfile.Order(s)[0]);
        Assert.Equal(5, p.ImageFiles().Length);
        var ids = p.ImageFiles().Select(f => TestProfile.ImageId(File.ReadAllBytes(Path.Combine(p.Images, f)))).OrderBy(x => x);
        Assert.Equal(new[] { 196, 197, 198, 199, 200 }, ids);
    }

    [Fact]
    public void Pinned_text_is_never_removed_and_is_not_counted()
    {
        using var p = new TestProfile();
        using var s = p.Open(maxText: 2);
        AddText(s, "t1");
        AddText(s, "t2");
        AddText(s, "t3"); // t1 dropped (cap 2)
        TestProfile.Find(s, "t2")!.IsPinned = true; // oldest remaining is now pinned

        AddText(s, "t4"); // counted: t3,t4 = 2 -> nothing removed
        Assert.Equal(new[] { "t4", "t3", "t2" }, TestProfile.Order(s));

        AddText(s, "t5"); // counted: t3,t4,t5 = 3 -> oldest UNPINNED (t3) goes, never t2
        Assert.Equal(new[] { "t5", "t4", "t2" }, TestProfile.Order(s));
    }

    [Fact]
    public void Pinned_image_keeps_its_file_and_pin_survives_restart()
    {
        using var p = new TestProfile();
        using (var s = p.Open(maxImages: 1))
        {
            AddImage(s, 1);
            TestProfile.FindImage(s, 1)!.IsPinned = true;
            AddImage(s, 2);
            AddImage(s, 3); // 2 removed, pinned 1 stays
            Assert.Equal(new[] { "IMG:3", "IMG:1" }, TestProfile.Order(s));
            Assert.Equal(2, p.ImageFiles().Length);
        }

        using var again = p.Open(maxImages: 1);
        Assert.Equal(new[] { "IMG:3", "IMG:1" }, TestProfile.Order(again));
        Assert.True(TestProfile.FindImage(again, 1)!.IsPinned);
        Assert.False(TestProfile.FindImage(again, 3)!.IsPinned);
    }

    [Fact]
    public void Unlimited_never_removes()
    {
        using var p = new TestProfile();
        using var s = p.Open();
        for (var i = 0; i < 300; i++) AddText(s, "t" + i);
        for (var i = 0; i < 60; i++) AddImage(s, i);
        Assert.Equal(300, s.CountCapped(ClipKind.Text));
        Assert.Equal(60, s.CountCapped(ClipKind.Image));
        Assert.Equal(60, p.ImageFiles().Length);
    }

    [Fact]
    public void Secrets_do_not_count_toward_or_trigger_the_text_cap()
    {
        using var p = new TestProfile();
        using var s = p.Open(maxText: 1);
        AddText(s, "t1");
        s.Add("hunter2-throwaway", true, "test");
        AddText(s, "t2");
        Assert.Equal(new[] { "t2" }, TestProfile.Order(s));
        Assert.Equal(2, s.Rows.Count); // t2 + the in-memory secret
    }

    [Fact]
    public void Opening_with_a_lower_cap_or_setting_caps_deletes_nothing()
    {
        using var p = new TestProfile();
        using (var s = p.Open())
        {
            for (var i = 0; i < 20; i++) AddText(s, "t" + i);
            for (var i = 0; i < 8; i++) AddImage(s, i);
        }

        using var again = p.Open(maxText: 5, maxImages: 2);
        Assert.Equal(20, again.CountCapped(ClipKind.Text));
        Assert.Equal(8, again.CountCapped(ClipKind.Image));
        again.SetCaps(1, 1);
        Assert.Equal(20, again.CountCapped(ClipKind.Text));
        Assert.Equal(8, p.ImageFiles().Length);
    }

    [Fact]
    public void A_copy_in_an_over_cap_store_removes_at_most_one_clip()
    {
        using var p = new TestProfile();
        using (var s = p.Open())
        {
            for (var i = 0; i < 20; i++) AddText(s, "t" + i);
            for (var i = 0; i < 8; i++) AddImage(s, i);
        }

        using var again = p.Open(maxText: 5, maxImages: 2);
        AddText(again, "new-text");
        AddImage(again, 100);
        Assert.Equal(20, again.CountCapped(ClipKind.Text)); // +1 -1
        Assert.Equal(8, again.CountCapped(ClipKind.Image));
        Assert.Equal(new[] { "IMG:100", "new-text" }, TestProfile.Order(again).Take(2));
    }

    [Fact]
    public void Confirmed_lowering_removes_exactly_the_confirmed_oldest_clips_and_their_files()
    {
        using var p = new TestProfile();
        using var s = p.Open();
        for (var i = 1; i <= 10; i++) AddText(s, "t" + i);
        for (var i = 1; i <= 6; i++) AddImage(s, i);

        var removed = s.ApplyConfirmedLowering(4, 2, confirmedTextRemovals: 6, confirmedImageRemovals: 4);

        Assert.Equal(new CapRemoval(6, 4), removed);
        Assert.Equal(new[] { "IMG:6", "IMG:5", "t10", "t9", "t8", "t7" }, TestProfile.Order(s));
        Assert.Equal(2, p.ImageFiles().Length);
        Assert.Equal(4, s.MaxTextClips);
        Assert.Equal(2, s.MaxImageClips);
    }

    [Fact]
    public void Confirmed_lowering_never_removes_more_than_was_confirmed()
    {
        using var p = new TestProfile();
        using var s = p.Open();
        for (var i = 1; i <= 10; i++) AddText(s, "t" + i);

        // Cap says 8 clips would have to go, but the user only confirmed 3.
        var removed = s.ApplyConfirmedLowering(2, 0, confirmedTextRemovals: 3, confirmedImageRemovals: 0);
        Assert.Equal(3, removed.Text);
        Assert.Equal(7, s.CountCapped(ClipKind.Text));
    }

    [Fact]
    public void Confirmed_lowering_removes_only_the_actual_excess_if_less_than_confirmed()
    {
        using var p = new TestProfile();
        using var s = p.Open();
        for (var i = 1; i <= 10; i++) AddText(s, "t" + i);

        var removed = s.ApplyConfirmedLowering(8, 0, confirmedTextRemovals: 100, confirmedImageRemovals: 0);
        Assert.Equal(2, removed.Text);
        Assert.Equal(8, s.CountCapped(ClipKind.Text));
    }

    [Fact]
    public void Raising_a_cap_or_going_unlimited_removes_nothing()
    {
        using var p = new TestProfile();
        using var s = p.Open(maxText: 5, maxImages: 3);
        for (var i = 1; i <= 5; i++) AddText(s, "t" + i);
        for (var i = 1; i <= 3; i++) AddImage(s, i);

        var removed = s.ApplyConfirmedLowering(50, 30, 0, 0);
        Assert.Equal(0, removed.Total);
        removed = s.ApplyConfirmedLowering(ClipCaps.Unlimited, ClipCaps.Unlimited, 0, 0);
        Assert.Equal(0, removed.Total);
        Assert.Equal(5, s.CountCapped(ClipKind.Text));
        Assert.Equal(3, p.ImageFiles().Length);
    }

    [Fact]
    public void Confirmed_lowering_keeps_pinned_clips()
    {
        using var p = new TestProfile();
        using var s = p.Open();
        for (var i = 1; i <= 5; i++) AddText(s, "t" + i);
        TestProfile.Find(s, "t1")!.IsPinned = true;
        TestProfile.Find(s, "t2")!.IsPinned = true;

        // 3 counted (t3,t4,t5); cap 1 -> 2 removals: t3,t4. Pinned t1,t2 stay.
        var removed = s.ApplyConfirmedLowering(1, 0, 2, 0);
        Assert.Equal(2, removed.Text);
        Assert.Equal(new[] { "t5", "t2", "t1" }, TestProfile.Order(s));
    }

    [Fact]
    public void Removing_an_image_whose_file_is_already_gone_does_not_throw_or_touch_other_files()
    {
        using var p = new TestProfile();
        using var s = p.Open();
        var first = AddImage(s, 1);
        AddImage(s, 2);
        File.Delete(Path.Combine(p.Images, first.ImageFileName!));

        var ex = Record.Exception(() => s.RemoveEntry(first));

        Assert.Null(ex);
        Assert.Equal(new[] { "IMG:2" }, TestProfile.Order(s));
        Assert.Single(p.ImageFiles());
    }

    [Fact]
    public void A_locked_image_file_is_skipped_without_error_and_removed_on_a_later_save()
    {
        using var p = new TestProfile();
        using var s = p.Open(maxImages: 1);
        var first = AddImage(s, 1);
        var firstPath = Path.Combine(p.Images, first.ImageFileName!);

        using (new FileStream(firstPath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            var ex = Record.Exception(() => AddImage(s, 2)); // removes clip 1; its file is locked
            Assert.Null(ex);
            Assert.True(File.Exists(firstPath));
        }

        AddText(s, "later"); // any save retries the sweep
        Assert.False(File.Exists(firstPath));
        Assert.Single(p.ImageFiles());
    }

    [Fact]
    public void Foreign_files_and_files_outside_the_images_folder_are_never_deleted()
    {
        using var p = new TestProfile();
        using var s = p.Open(maxImages: 1);
        AddImage(s, 1);
        File.WriteAllText(Path.Combine(p.Images, "notes.txt"), "mine");
        File.WriteAllBytes(Path.Combine(p.Images, "holiday.png"), new byte[] { 1, 2, 3 });
        File.WriteAllBytes(Path.Combine(p.Images, "2001-not-ours.png"), new byte[] { 1, 2, 3 });
        var outside = Path.Combine(p.Root, "20260101000000000.png"); // app-style name, wrong folder
        File.WriteAllBytes(outside, new byte[] { 9 });
        var sibling = Path.Combine(p.Root, "images-backup");
        Directory.CreateDirectory(sibling);
        File.WriteAllBytes(Path.Combine(sibling, "20260101000000001.png"), new byte[] { 9 });

        AddImage(s, 2);
        AddImage(s, 3);
        s.ClearNonSecrets();

        Assert.True(File.Exists(Path.Combine(p.Images, "notes.txt")));
        Assert.True(File.Exists(Path.Combine(p.Images, "holiday.png")));
        Assert.True(File.Exists(Path.Combine(p.Images, "2001-not-ours.png")));
        Assert.True(File.Exists(outside));
        Assert.True(File.Exists(Path.Combine(sibling, "20260101000000001.png")));
    }

    [Fact]
    public void Two_records_sharing_one_file_keep_it_until_both_are_gone()
    {
        using var p = new TestProfile();
        Directory.CreateDirectory(p.Images);
        var name = "20260101000000000.png";
        File.WriteAllBytes(Path.Combine(p.Images, name), TestProfile.Png(7));
        var recs = new[]
        {
            new { Text = (string?)null, ImageFile = name, Owner = "x", Pinned = false, CopiedAt = DateTime.UtcNow },
            new { Text = (string?)null, ImageFile = name, Owner = "x", Pinned = false, CopiedAt = DateTime.UtcNow }
        };
        File.WriteAllText(Path.Combine(p.Root, "history.json"), JsonSerializer.Serialize(recs));

        using var s = p.Open();
        var all = s.PickerEntries().ToList();
        Assert.Equal(2, all.Count);

        s.RemoveEntry(all[0]);
        Assert.True(File.Exists(Path.Combine(p.Images, name)));
        s.RemoveEntry(all[1]);
        Assert.False(File.Exists(Path.Combine(p.Images, name)));
    }

    [Fact]
    public void A_history_record_cannot_point_outside_the_images_folder()
    {
        using var p = new TestProfile();
        var outside = Path.Combine(p.Root, "evil.png");
        File.WriteAllBytes(outside, TestProfile.Png(9));
        var recs = new[] { new { Text = "ok", ImageFile = @"..\evil.png", Owner = "x", Pinned = false, CopiedAt = DateTime.UtcNow } };
        File.WriteAllText(Path.Combine(p.Root, "history.json"), JsonSerializer.Serialize(recs));

        using var s = p.Open();
        Assert.Equal(new[] { "ok" }, TestProfile.Order(s)); // loaded as text, no image read
        Assert.False(s.PickerEntries().Single().IsImage);
        AddText(s, "more");
        Assert.True(File.Exists(outside));
    }

    [Fact]
    public void Unreadable_history_json_is_backed_up_and_no_image_is_deleted()
    {
        using var p = new TestProfile();
        Directory.CreateDirectory(p.Images);
        var img = Path.Combine(p.Images, "20260101000000000.png");
        File.WriteAllBytes(img, TestProfile.Png(1));
        File.WriteAllText(Path.Combine(p.Root, "history.json"), "{ this is not json");

        using var s = p.Open(maxText: 5, maxImages: 5);
        AddText(s, "fresh");

        Assert.True(File.Exists(img));
        Assert.Equal("{ this is not json", File.ReadAllText(Path.Combine(p.Root, "history.json.corrupt.bak")));
    }

    [Fact]
    public void Capture_time_and_image_file_names_are_kept_across_restarts()
    {
        using var p = new TestProfile();
        DateTime copiedAt;
        string fileName;
        using (var s = p.Open())
        {
            var e = AddImage(s, 1);
            copiedAt = e.CopiedAt;
            fileName = e.ImageFileName!;
        }

        using var again = p.Open();
        var loaded = again.PickerEntries().Single();
        Assert.Equal(copiedAt, loaded.CopiedAt);
        Assert.Equal(fileName, loaded.ImageFileName);
        Assert.Equal(new[] { fileName }, p.ImageFiles());
    }

    [Fact]
    public void History_is_written_atomically_and_leaves_no_temp_file()
    {
        using var p = new TestProfile();
        using var s = p.Open();
        AddText(s, "one");
        AddText(s, "two");
        Assert.True(File.Exists(Path.Combine(p.Root, "history.json")));
        Assert.False(File.Exists(Path.Combine(p.Root, "history.json.tmp")));
    }

    [Fact]
    public void Rapid_images_in_the_same_millisecond_get_distinct_files()
    {
        using var p = new TestProfile();
        using var s = p.Open();
        for (var i = 0; i < 40; i++) AddImage(s, i);
        Assert.Equal(40, p.ImageFiles().Length);
        Assert.Equal(40, p.ImageFiles().Distinct(StringComparer.OrdinalIgnoreCase).Count());
    }

    [Fact]
    public void Upgraded_profile_above_the_default_caps_is_not_trimmed_on_launch_or_next_copy()
    {
        using var p = new TestProfile();
        // A profile from before caps existed: lots of clips, no settings.json.
        using (var old = p.Open())
        {
            for (var i = 0; i < 150; i++) AddText(old, "t" + i);
            for (var i = 0; i < 40; i++) AddImage(old, i);
        }

        Assert.False(File.Exists(Path.Combine(p.Root, "settings.json")));
        var settings = SettingsManager.Load();
        Assert.Equal(AppSettings.Unlimited, settings.MaxTextClips);
        Assert.Equal(AppSettings.Unlimited, settings.MaxImageClips);

        using var s = p.Open(settings.MaxTextClips, settings.MaxImageClips);
        Assert.Equal(150, s.CountCapped(ClipKind.Text));
        Assert.Equal(40, s.CountCapped(ClipKind.Image));

        AddText(s, "next copy");
        AddImage(s, 999);
        Assert.Equal(151, s.CountCapped(ClipKind.Text));
        Assert.Equal(41, s.CountCapped(ClipKind.Image));
        Assert.Equal(41, p.ImageFiles().Length);
    }

    /// <summary>history.json with pinned and unpinned records (one image each kind).</summary>
    private static void SeedPinned(TestProfile p)
    {
        Directory.CreateDirectory(p.Images);
        File.WriteAllBytes(Path.Combine(p.Images, "20260101000000001.png"), TestProfile.Png(1)); // pinned image
        File.WriteAllBytes(Path.Combine(p.Images, "20260101000000002.png"), TestProfile.Png(2)); // unpinned image
        var now = DateTime.UtcNow;
        var recs = new object[]
        {
            new { Text = (string?)"p-text", ImageFile = (string?)null, Owner = "x", Pinned = true, CopiedAt = now },
            new { Text = (string?)"u-text-1", ImageFile = (string?)null, Owner = "x", Pinned = false, CopiedAt = now },
            new { Text = (string?)null, ImageFile = (string?)"20260101000000001.png", Owner = "x", Pinned = true, CopiedAt = now },
            new { Text = (string?)"u-text-2", ImageFile = (string?)null, Owner = "x", Pinned = false, CopiedAt = now },
            new { Text = (string?)null, ImageFile = (string?)"20260101000000002.png", Owner = "x", Pinned = false, CopiedAt = now }
        };
        File.WriteAllText(Path.Combine(p.Root, "history.json"), JsonSerializer.Serialize(recs));
    }

    [Fact]
    public void ClearNonSecrets_spares_pinned_and_deletes_only_the_removed_clips_image_files()
    {
        using var p = new TestProfile();
        SeedPinned(p);
        File.WriteAllText(Path.Combine(p.Images, "notes.txt"), "mine");
        using var s = p.Open();
        Assert.Equal(5, s.PickerEntries().Count());

        s.ClearNonSecrets();

        Assert.Equal(new[] { "p-text", "IMG:1" }, TestProfile.Order(s));
        Assert.Equal(new[] { "20260101000000001.png", "notes.txt" }, p.ImageFiles());
        using var reopened = p.Open();
        Assert.Equal(new[] { "p-text", "IMG:1" }, TestProfile.Order(reopened));
    }

    [Fact]
    public void ClearHistory_spares_pinned_wipes_secrets_and_persists()
    {
        using var p = new TestProfile();
        SeedPinned(p);
        using var s = p.Open();
        s.Add("hunter2", true, "test");
        Assert.Equal(6, s.Rows.Count); // 5 stored + 1 secret (secrets are not in the picker list)

        s.ClearHistory();

        Assert.Equal(new[] { "p-text", "IMG:1" }, TestProfile.Order(s));
        Assert.Equal(2, s.Rows.Count);
        Assert.Equal(new[] { "20260101000000001.png" }, p.ImageFiles());
        Assert.Equal("p-text", s.Latest?.TryGetNonSecretText());
        using var reopened = p.Open();
        Assert.Equal(new[] { "p-text", "IMG:1" }, TestProfile.Order(reopened));
    }

    [Fact]
    public void ClearHistory_with_nothing_pinned_clears_everything_and_all_image_files()
    {
        using var p = new TestProfile();
        using var s = p.Open();
        AddText(s, "a");
        AddImage(s, 1);
        AddImage(s, 2);
        Assert.Equal(2, p.ImageFiles().Length);

        s.ClearHistory();

        Assert.Empty(s.PickerEntries());
        Assert.Empty(s.Rows);
        Assert.Null(s.Latest);
        Assert.Empty(p.ImageFiles());
    }

    [Fact]
    public void ClearHistory_never_deletes_foreign_files()
    {
        using var p = new TestProfile();
        using var s = p.Open();
        AddImage(s, 1);
        File.WriteAllBytes(Path.Combine(p.Images, "holiday.png"), new byte[] { 1, 2, 3 });
        var outside = Path.Combine(p.Root, "20260101000000000.png");
        File.WriteAllBytes(outside, new byte[] { 9 });

        s.ClearHistory();

        Assert.Equal(new[] { "holiday.png" }, p.ImageFiles());
        Assert.True(File.Exists(outside));
    }

    [Fact]
    public void ClearAll_is_the_explicit_delete_everything_path_and_does_remove_pinned()
    {
        using var p = new TestProfile();
        SeedPinned(p);
        using var s = p.Open();

        s.ClearAll();

        Assert.Empty(s.PickerEntries());
        Assert.Empty(p.ImageFiles());
    }

    [Fact]
    public void Over_cap_profile_is_not_trimmed_by_opening_it_and_a_copy_removes_only_one_clip()
    {
        using var p = new TestProfile();
        var recs = new List<object>();
        var now = DateTime.UtcNow;
        for (var i = 0; i < 10; i++)
            recs.Add(new { Text = (string?)("old-" + i), ImageFile = (string?)null, Owner = "x", Pinned = false, CopiedAt = now });
        File.WriteAllText(Path.Combine(p.Root, "history.json"), JsonSerializer.Serialize(recs));

        using var s = p.Open(maxText: 3);
        Assert.Equal(10, s.CountCapped(ClipKind.Text)); // opened over cap: untouched

        AddText(s, "new");

        Assert.Equal(10, s.CountCapped(ClipKind.Text)); // +1 added, exactly 1 evicted
    }}
