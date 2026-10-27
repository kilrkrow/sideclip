using System.Text;
using Sideclip.Obsidian;
using Xunit;

namespace Sideclip.Tests;

public class DailyNoteTextTests
{
    private static readonly DateTime At = new(2026, 10, 27, 14, 5, 32);

    [Fact]
    public void Inside_vault_embeds_vault_relative_path()
    {
        var line = DailyNoteText.EntryLine(@"D:\Vault", @"D:\Vault\Attachments\Shots\Screenshot 2026-10-27 140532.png", At);
        Assert.Equal("- 14:05:32 ![[Attachments/Shots/Screenshot 2026-10-27 140532.png]]", line);
    }

    [Fact]
    public void Inside_vault_with_trailing_slash_and_case_difference()
    {
        var line = DailyNoteText.EntryLine(@"d:\vault\", @"D:\Vault\shot (1).png", At);
        Assert.Equal("- 14:05:32 ![[shot (1).png]]", line);
    }

    [Fact]
    public void Wikilink_breaking_names_use_markdown_embed()
    {
        var line = DailyNoteText.EntryLine(@"D:\Vault", @"D:\Vault\a [b] #c.png", At);
        Assert.Equal("- 14:05:32 ![](a%20%5Bb%5D%20%23c.png)", line);
    }

    [Fact]
    public void Outside_vault_links_with_file_url()
    {
        var line = DailyNoteText.EntryLine(@"D:\Vault", @"D:\Shot Copies\Screenshot (3).png", At);
        Assert.Equal("- 14:05:32 [Screenshot (3).png](file:///D:/Shot%20Copies/Screenshot%20%283%29.png)", line);
    }

    [Fact]
    public void Sibling_folder_with_same_prefix_is_outside()
    {
        var line = DailyNoteText.EntryLine(@"D:\Vault", @"D:\Vault2\a.png", At);
        Assert.StartsWith("- 14:05:32 [a.png](file:///D:/Vault2/a.png)", line);
    }

    [Fact]
    public void File_url_encodes_unicode_and_percent()
    {
        Assert.Equal("file:///C:/x/caf%C3%A9%2550.png", DailyNoteText.FileUrl("C:\\x\\caf\u00e9%50.png"));
    }

    [Fact]
    public void Ocr_callout_is_collapsed_and_quoted()
    {
        var lines = DailyNoteText.OcrCallout("Hello\r\n\r\nWorld  ");
        Assert.Equal(new[] { "> [!note]- OCR text", "> Hello", ">", "> World" }, lines);
    }

    [Fact]
    public void Template_variables_are_filled()
    {
        var t = "# {{title}}\n{{date}} {{time}} {{date:dddd}} {{time:HH:mm:ss}} {{date+1d:YYYY-MM-DD}} {{yesterday}} {{tomorrow}} {{DATE:YYYY}}";
        var s = DailyNoteText.ApplyTemplate(t, At, "YYYY-MM-DD", "2026-10-27");
        Assert.Equal("# 2026-10-27\n2026-10-27 14:05 Tuesday 14:05:32 2026-10-28 2026-10-26 2026-10-28 2026", s);
    }
}

public class DailyNoteWriterTests
{
    private static readonly DateTime At = new(2026, 10, 27, 14, 5, 32);
    private static readonly string[] OneLine = { "- 14:05:32 ![[a.png]]" };

    private static byte[] AppendTo(TestDir dir, byte[] original, string[] lines)
    {
        var note = Path.Combine(dir.Path, "2026-10-27.md");
        File.WriteAllBytes(note, original);
        var result = DailyNoteWriter.Append(dir.Path, DailyNotesConfig.Default, At, lines);
        Assert.Equal(NoteOutcome.Appended, result.Outcome);
        Assert.False(result.Created);
        var after = File.ReadAllBytes(note);
        // Byte-identical prefix: the original is never rewritten.
        Assert.True(after.AsSpan(0, original.Length).SequenceEqual(original), "existing bytes changed");
        return after[original.Length..];
    }

    [Fact]
    public void Lf_note_without_trailing_newline_gets_one_leading_newline()
    {
        using var dir = new TestDir();
        var added = AppendTo(dir, Encoding.UTF8.GetBytes("# Today\n\nsome text"), OneLine);
        Assert.Equal("\n- 14:05:32 ![[a.png]]\n", Encoding.UTF8.GetString(added));
    }

    [Fact]
    public void Lf_note_with_trailing_newline_gets_no_extra_blank_line()
    {
        using var dir = new TestDir();
        var added = AppendTo(dir, Encoding.UTF8.GetBytes("# Today\n- item\n"), OneLine);
        Assert.Equal("- 14:05:32 ![[a.png]]\n", Encoding.UTF8.GetString(added));
    }

    [Fact]
    public void Crlf_note_keeps_crlf()
    {
        using var dir = new TestDir();
        var added = AppendTo(dir, Encoding.UTF8.GetBytes("# Today\r\ntext"), new[] { "- one", "- two" });
        Assert.Equal("\r\n- one\r\n- two\r\n", Encoding.UTF8.GetString(added));
    }

    [Fact]
    public void Utf8_bom_note_keeps_single_bom_and_utf8()
    {
        using var dir = new TestDir();
        var original = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("caf\u00e9\n")).ToArray();
        var added = AppendTo(dir, original, new[] { "- \u00fc" });
        Assert.Equal(Encoding.UTF8.GetBytes("- \u00fc\n"), added);
    }

    [Fact]
    public void Utf16_note_appends_utf16()
    {
        using var dir = new TestDir();
        var original = new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes("x\r\ny")).ToArray();
        var added = AppendTo(dir, original, new[] { "- z" });
        Assert.Equal(Encoding.Unicode.GetBytes("\r\n- z\r\n"), added);
    }

    [Fact]
    public void Non_utf8_bytes_are_left_alone()
    {
        using var dir = new TestDir();
        var original = new byte[] { 0x41, 0xE9, 0x42 }; // Latin-1 "AeB", invalid UTF-8
        var added = AppendTo(dir, original, OneLine);
        Assert.Equal("\n- 14:05:32 ![[a.png]]\n", Encoding.UTF8.GetString(added));
    }

    [Fact]
    public void Empty_existing_note_gets_no_leading_newline()
    {
        using var dir = new TestDir();
        var added = AppendTo(dir, Array.Empty<byte>(), OneLine);
        Assert.Equal("- 14:05:32 ![[a.png]]\n", Encoding.UTF8.GetString(added));
    }

    [Fact]
    public void Repeated_appends_only_ever_grow_the_file()
    {
        using var dir = new TestDir();
        var note = Path.Combine(dir.Path, "2026-10-27.md");
        File.WriteAllText(note, "start");
        var prev = File.ReadAllBytes(note);
        for (var i = 0; i < 10; i++)
        {
            DailyNoteWriter.Append(dir.Path, DailyNotesConfig.Default, At, new[] { "- " + i });
            var now = File.ReadAllBytes(note);
            Assert.True(now.AsSpan(0, prev.Length).SequenceEqual(prev));
            prev = now;
        }

        Assert.Equal("start\n- 0\n- 1\n- 2\n- 3\n- 4\n- 5\n- 6\n- 7\n- 8\n- 9\n", File.ReadAllText(note));
    }

    [Fact]
    public void Missing_note_is_created_from_template_without_md_extension()
    {
        using var dir = new TestDir();
        Directory.CreateDirectory(Path.Combine(dir.Path, ".obsidian"));
        Directory.CreateDirectory(Path.Combine(dir.Path, "Templates"));
        File.WriteAllText(Path.Combine(dir.Path, ".obsidian", "daily-notes.json"),
            "{\"folder\":\"Daily\",\"format\":\"YYYY/YYYY-MM-DD\",\"template\":\"Templates/Day\"}");
        File.WriteAllText(Path.Combine(dir.Path, "Templates", "Day.md"), "# {{title}}\n\n## Shots\n");

        var cfg = DailyNotesConfig.Load(dir.Path);
        var result = DailyNoteWriter.Append(dir.Path, cfg, At, OneLine);

        Assert.Equal(NoteOutcome.Appended, result.Outcome);
        Assert.True(result.Created);
        Assert.False(result.TemplateMissing);
        Assert.Equal(Path.Combine(dir.Path, "Daily", "2026", "2026-10-27.md"), result.NotePath);
        Assert.Equal("# 2026-10-27\n\n## Shots\n- 14:05:32 ![[a.png]]\n", File.ReadAllText(result.NotePath));
    }

    [Fact]
    public void Missing_template_creates_empty_note_and_flags_it()
    {
        using var dir = new TestDir();
        var cfg = new DailyNotesConfig("", "YYYY-MM-DD", "Templates/Nope");
        var result = DailyNoteWriter.Append(dir.Path, cfg, At, OneLine);
        Assert.Equal(NoteOutcome.Appended, result.Outcome);
        Assert.True(result.TemplateMissing);
        Assert.Equal("- 14:05:32 ![[a.png]]\n", File.ReadAllText(result.NotePath));
    }

    [Fact]
    public void Missing_vault_is_reported_not_thrown()
    {
        using var dir = new TestDir();
        var result = DailyNoteWriter.Append(Path.Combine(dir.Path, "moved"), DailyNotesConfig.Default, At, OneLine);
        Assert.Equal(NoteOutcome.VaultMissing, result.Outcome);
        Assert.False(Directory.Exists(Path.Combine(dir.Path, "moved")));
    }

    [Fact]
    public void Locked_note_is_reported_and_untouched()
    {
        using var dir = new TestDir();
        var note = Path.Combine(dir.Path, "2026-10-27.md");
        File.WriteAllText(note, "keep");
        using (new FileStream(note, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            var result = DailyNoteWriter.Append(dir.Path, DailyNotesConfig.Default, At, OneLine, attempts: 2, retryMs: 10);
            Assert.Equal(NoteOutcome.NoteLocked, result.Outcome);
        }

        Assert.Equal("keep", File.ReadAllText(note));
    }

    [Fact]
    public void Format_that_escapes_the_vault_is_refused()
    {
        using var dir = new TestDir();
        var vault = dir.Sub("v");
        var result = DailyNoteWriter.Append(vault, new DailyNotesConfig("", "[../../outside]", ""), At, OneLine);
        Assert.Equal(NoteOutcome.Failed, result.Outcome);
        Assert.Empty(Directory.GetFiles(dir.Path, "*.md", SearchOption.AllDirectories));
    }
}
