using System.Text;
using Sideclip.Obsidian;
using Xunit;

namespace Sideclip.Tests;

public class HeadingTests
{
    private const string H = "## Sideclip Clips";
    private static readonly DateTime At = new(2026, 10, 27, 14, 5, 32);

    /// <summary>Writes <paramref name="original"/>, inserts <paramref name="lines"/> under <paramref name="heading"/>, returns the new text.</summary>
    private static string Insert(TestDir dir, string original, string? heading, params string[] lines)
    {
        var note = Path.Combine(dir.Path, "2026-10-27.md");
        File.WriteAllText(note, original, new UTF8Encoding(false));
        var r = DailyNoteWriter.Append(dir.Path, DailyNotesConfig.Default, At, lines, heading: heading);
        Assert.Equal(NoteOutcome.Appended, r.Outcome);
        return File.ReadAllText(note);
    }

    /// <summary>Asserts that <paramref name="after"/> is <paramref name="before"/> with exactly <paramref name="inserted"/> spliced in at one point.</summary>
    private static void AssertSpliced(byte[] before, byte[] after, byte[] inserted)
    {
        Assert.Equal(before.Length + inserted.Length, after.Length);
        var at = 0;
        while (at < before.Length && before[at] == after[at])
            at++;
        // The inserted bytes may start with a byte equal to the original at that point; find the splice that works.
        for (var start = Math.Min(at, before.Length); start >= 0; start--)
        {
            if (after.AsSpan(0, start).SequenceEqual(before.AsSpan(0, start)) &&
                after.AsSpan(start, inserted.Length).SequenceEqual(inserted) &&
                after.AsSpan(start + inserted.Length).SequenceEqual(before.AsSpan(start)))
                return;
        }

        Assert.Fail("not a pure insertion");
    }

    [Theory]
    [InlineData("## Sideclip Clips", true)]
    [InlineData("# A", true)]
    [InlineData("###### Six", true)]
    [InlineData("## Sideclip Clips   ", true)]
    [InlineData("#NoSpace", false)]
    [InlineData("####### seven", false)]
    [InlineData("Sideclip Clips", false)]
    [InlineData("# ", false)]
    [InlineData("#", false)]
    [InlineData("##    ", false)]
    [InlineData(" ## Leading space", false)]
    [InlineData("## Two\nlines", false)]
    [InlineData("", false)]
    public void Validates_atx_headings(string value, bool valid)
    {
        Assert.Equal(valid, NoteSections.IsValidHeading(value));
    }

    [Theory]
    [InlineData("#NoSpace")]
    [InlineData("####### seven")]
    [InlineData("Sideclip Clips")]
    [InlineData("# ")]
    public void Invalid_heading_is_never_written_and_falls_back_to_end(string bad)
    {
        using var dir = new TestDir();
        Assert.Equal("# Day\n- clip\n", Insert(dir, "# Day\n", bad, "- clip"));
    }

    [Fact]
    public void Blank_heading_appends_at_end()
    {
        using var dir = new TestDir();
        Assert.Equal("# Day\n\n## Other\n- x\n- clip\n", Insert(dir, "# Day\n\n## Other\n- x\n", "", "- clip"));
    }

    [Fact]
    public void Existing_heading_mid_note_inserts_before_blank_lines_and_next_section()
    {
        using var dir = new TestDir();
        var text = Insert(dir, "# Day\n\n## Sideclip Clips\n- one\n\n\n## Tasks\n- [ ] t\n", H, "- clip");
        Assert.Equal("# Day\n\n## Sideclip Clips\n- one\n- clip\n\n\n## Tasks\n- [ ] t\n", text);
    }

    [Fact]
    public void Heading_with_empty_section_gets_clip_right_under_it()
    {
        using var dir = new TestDir();
        var text = Insert(dir, "## Sideclip Clips\n\n# Next\n", H, "- clip");
        Assert.Equal("## Sideclip Clips\n- clip\n\n# Next\n", text);
    }

    [Fact]
    public void Heading_at_end_of_file_without_newline()
    {
        using var dir = new TestDir();
        Assert.Equal("# Day\n## Sideclip Clips\n- clip\n", Insert(dir, "# Day\n## Sideclip Clips", H, "- clip"));
    }

    [Fact]
    public void Heading_at_end_of_file_with_trailing_blank_lines()
    {
        using var dir = new TestDir();
        Assert.Equal("## Sideclip Clips\n- a\n- clip\n\n", Insert(dir, "## Sideclip Clips\n- a\n\n", H, "- clip"));
    }

    [Fact]
    public void Missing_heading_is_added_after_a_blank_line()
    {
        using var dir = new TestDir();
        Assert.Equal("# Day\ntext\n\n## Sideclip Clips\n- clip\n", Insert(dir, "# Day\ntext", H, "- clip"));
    }

    [Fact]
    public void Missing_heading_after_existing_blank_line_adds_no_second_blank()
    {
        using var dir = new TestDir();
        Assert.Equal("# Day\n\n## Sideclip Clips\n- clip\n", Insert(dir, "# Day\n\n", H, "- clip"));
    }

    [Fact]
    public void Missing_heading_in_empty_note_starts_with_heading()
    {
        using var dir = new TestDir();
        Assert.Equal("## Sideclip Clips\n- clip\n", Insert(dir, "", H, "- clip"));
    }

    [Fact]
    public void New_note_from_template_gets_heading_then_later_clips_land_under_it()
    {
        using var dir = new TestDir();
        Directory.CreateDirectory(Path.Combine(dir.Path, "T"));
        File.WriteAllText(Path.Combine(dir.Path, "T", "Day.md"), "# {{title}}\n\n## Tasks\n- [ ] x\n");
        var cfg = new DailyNotesConfig("", "YYYY-MM-DD", "T/Day");
        var r1 = DailyNoteWriter.Append(dir.Path, cfg, At, new[] { "- first" }, heading: H);
        Assert.True(r1.Created);
        DailyNoteWriter.Append(dir.Path, cfg, At, new[] { "- second" }, heading: H);
        Assert.Equal("# 2026-10-27\n\n## Tasks\n- [ ] x\n\n## Sideclip Clips\n- first\n- second\n", File.ReadAllText(r1.NotePath));
    }

    [Fact]
    public void Nested_lower_level_subheadings_stay_inside_the_section()
    {
        using var dir = new TestDir();
        var text = Insert(dir, "## Sideclip Clips\n- a\n### Morning\n- b\n#### Detail\n- c\n\n## Next\n", H, "- clip");
        Assert.Equal("## Sideclip Clips\n- a\n### Morning\n- b\n#### Detail\n- c\n- clip\n\n## Next\n", text);
    }

    [Fact]
    public void Higher_level_heading_ends_the_section()
    {
        using var dir = new TestDir();
        Assert.Equal("## Sideclip Clips\n- a\n- clip\n# Top\n- z\n", Insert(dir, "## Sideclip Clips\n- a\n# Top\n- z\n", H, "- clip"));
    }

    [Fact]
    public void Heading_inside_code_fence_or_frontmatter_is_ignored()
    {
        using var dir = new TestDir();
        var original = "---\ntitle: x\n## Sideclip Clips\n---\n```md\n## Sideclip Clips\n```\n~~~\n## Sideclip Clips\n~~~\n# Day\n";
        Assert.Equal(original + "\n## Sideclip Clips\n- clip\n", Insert(dir, original, H, "- clip"));
    }

    [Fact]
    public void Fenced_heading_inside_section_does_not_end_it()
    {
        using var dir = new TestDir();
        var text = Insert(dir, "## Sideclip Clips\n```\n# not a heading\n```\n\n## Next\n", H, "- clip");
        Assert.Equal("## Sideclip Clips\n```\n# not a heading\n```\n- clip\n\n## Next\n", text);
    }

    [Fact]
    public void First_match_wins_and_trailing_whitespace_is_ignored()
    {
        using var dir = new TestDir();
        var text = Insert(dir, "## Sideclip Clips   \n- a\n## Sideclip Clips\n- b\n", "## Sideclip Clips  ", "- clip");
        Assert.Equal("## Sideclip Clips   \n- a\n- clip\n## Sideclip Clips\n- b\n", text);
    }

    [Fact]
    public void Ocr_callout_lines_stay_together_and_repeated_clips_keep_order()
    {
        using var dir = new TestDir();
        var note = Path.Combine(dir.Path, "2026-10-27.md");
        File.WriteAllText(note, "## Sideclip Clips\n\n## Tasks\n");
        for (var i = 1; i <= 3; i++)
            DailyNoteWriter.Append(dir.Path, DailyNotesConfig.Default, At, new[] { "- clip " + i, "> [!note]- OCR text", "> text " + i }, heading: H);
        Assert.Equal(
            "## Sideclip Clips\n- clip 1\n> [!note]- OCR text\n> text 1\n- clip 2\n> [!note]- OCR text\n> text 2\n- clip 3\n> [!note]- OCR text\n> text 3\n\n## Tasks\n",
            File.ReadAllText(note));
    }

    [Fact]
    public void Crlf_and_bom_are_byte_identical_around_the_insert()
    {
        using var dir = new TestDir();
        var note = Path.Combine(dir.Path, "2026-10-27.md");
        var before = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("# Caf\u00e9\r\n## Sideclip Clips\r\n- a\r\n\r\n## Tasks\r\nend")).ToArray();
        File.WriteAllBytes(note, before);
        DailyNoteWriter.Append(dir.Path, DailyNotesConfig.Default, At, new[] { "- clip" }, heading: H);
        AssertSpliced(before, File.ReadAllBytes(note), Encoding.UTF8.GetBytes("- clip\r\n"));
    }

    [Fact]
    public void Utf16_note_is_byte_identical_around_the_insert()
    {
        using var dir = new TestDir();
        var note = Path.Combine(dir.Path, "2026-10-27.md");
        var before = new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes("## Sideclip Clips\r\n- a\r\n# Next\r\n")).ToArray();
        File.WriteAllBytes(note, before);
        DailyNoteWriter.Append(dir.Path, DailyNotesConfig.Default, At, new[] { "- clip" }, heading: H);
        AssertSpliced(before, File.ReadAllBytes(note), Encoding.Unicode.GetBytes("- clip\r\n"));
    }

    [Fact]
    public void Utf16_big_endian_note_is_byte_identical_around_the_insert()
    {
        using var dir = new TestDir();
        var note = Path.Combine(dir.Path, "2026-10-27.md");
        var be = new UnicodeEncoding(true, false);
        var before = new byte[] { 0xFE, 0xFF }.Concat(be.GetBytes("## Sideclip Clips\n- a\n# Next\n")).ToArray();
        File.WriteAllBytes(note, before);
        DailyNoteWriter.Append(dir.Path, DailyNotesConfig.Default, At, new[] { "- clip" }, heading: H);
        AssertSpliced(before, File.ReadAllBytes(note), be.GetBytes("- clip\n"));
    }

    [Fact]
    public void Non_utf8_bytes_elsewhere_are_kept()
    {
        using var dir = new TestDir();
        var note = Path.Combine(dir.Path, "2026-10-27.md");
        var before = Encoding.ASCII.GetBytes("## Sideclip Clips\n- a\n## Next\n").Concat(new byte[] { 0x41, 0xE9, 0x42, 0x0A }).ToArray();
        File.WriteAllBytes(note, before);
        DailyNoteWriter.Append(dir.Path, DailyNotesConfig.Default, At, new[] { "- clip" }, heading: H);
        AssertSpliced(before, File.ReadAllBytes(note), Encoding.UTF8.GetBytes("- clip\n"));
    }

    [Fact]
    public void Heading_is_never_edited_moved_or_duplicated()
    {
        using var dir = new TestDir();
        var note = Path.Combine(dir.Path, "2026-10-27.md");
        File.WriteAllText(note, "# Day\n");
        for (var i = 0; i < 4; i++)
            DailyNoteWriter.Append(dir.Path, DailyNotesConfig.Default, At, new[] { "- " + i }, heading: H);
        var text = File.ReadAllText(note);
        Assert.Equal(1, text.Split('\n').Count(l => l == H));
        Assert.Equal("# Day\n\n## Sideclip Clips\n- 0\n- 1\n- 2\n- 3\n", text);
    }
}
