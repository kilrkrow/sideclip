using System.Text;
using System.Text.RegularExpressions;

namespace Sideclip.Obsidian;

/// <summary>One line of a note: byte range (without terminator), terminator size, decoded text.</summary>
internal readonly record struct NoteLine(int Start, int Length, int TerminatorLength, string Text);

/// <summary>
/// Finds where a clip goes under a Markdown heading. Works on byte offsets so the rest of the
/// note can be kept byte for byte, whatever its encoding.
/// </summary>
internal static class NoteSections
{
    // ATX heading: 1-6 '#', one space, then some text. Trailing whitespace is ignored.
    private static readonly Regex ValidHeading = new(@"^(#{1,6}) [ \t]*\S.*$", RegexOptions.CultureInvariant);
    private static readonly Regex AnyHeading = new(@"^ {0,3}(#{1,6})(?:[ \t]|$)", RegexOptions.CultureInvariant);
    private static readonly Regex Fence = new(@"^ {0,3}(`{3,}|~{3,})", RegexOptions.CultureInvariant);

    /// <summary>True for "## Sideclip Clips"; false for "#NoSpace", "####### seven", "text", "# ".</summary>
    public static bool IsValidHeading(string? value)
    {
        if (value is null)
            return false;
        var v = value.TrimEnd();
        return !v.Contains('\n') && !v.Contains('\r') && ValidHeading.IsMatch(v);
    }

    public static int Level(string heading)
    {
        var m = AnyHeading.Match(heading);
        return m.Success ? m.Groups[1].Value.Length : 0;
    }

    /// <summary>Splits on "\n" (keeping a preceding "\r" as part of the terminator) in the note's own encoding.</summary>
    public static List<NoteLine> SplitLines(byte[] bytes, int start, Encoding encoding)
    {
        var unit = encoding is UnicodeEncoding ? 2 : 1;
        var bigEndian = unit == 2 && encoding.CodePage == 1201; // UTF-16BE
        var lines = new List<NoteLine>();
        var lineStart = start;
        var i = start;
        while (i + unit <= bytes.Length)
        {
            if (IsUnit(bytes, i, unit, bigEndian, '\n'))
            {
                var end = i;
                var term = unit;
                if (end - unit >= lineStart && IsUnit(bytes, end - unit, unit, bigEndian, '\r'))
                {
                    end -= unit;
                    term += unit;
                }

                lines.Add(new NoteLine(lineStart, end - lineStart, term, encoding.GetString(bytes, lineStart, end - lineStart)));
                i += unit;
                lineStart = i;
                continue;
            }

            i += unit;
        }

        if (lineStart < bytes.Length)
            lines.Add(new NoteLine(lineStart, bytes.Length - lineStart, 0, encoding.GetString(bytes, lineStart, bytes.Length - lineStart)));
        return lines;
    }

    private static bool IsUnit(byte[] b, int i, int unit, bool bigEndian, char c)
    {
        if (unit == 1)
            return b[i] == c;
        return bigEndian ? b[i] == 0 && b[i + 1] == c : b[i] == c && b[i + 1] == 0;
    }

    /// <summary>
    /// Index of the line to insert after, or -1 when the heading is not in the note.
    /// The heading must match a whole line (trailing whitespace ignored on both sides), outside
    /// YAML frontmatter and fenced code. Its section runs to the next heading of the same or a
    /// higher level (or the end); lower-level subheadings stay inside. The clip goes after the
    /// section's last non-blank line, so blank lines before the next heading stay after it.
    /// </summary>
    public static int FindInsertLine(IReadOnlyList<NoteLine> lines, string heading)
    {
        var target = heading.TrimEnd();
        var level = Level(target);
        if (level == 0)
            return -1;

        var skip = IgnoredLines(lines);
        var found = -1;
        for (var i = 0; i < lines.Count; i++)
        {
            if (!skip[i] && lines[i].Text.TrimEnd() == target)
            {
                found = i;
                break;
            }
        }

        if (found < 0)
            return -1;

        var last = found;
        for (var i = found + 1; i < lines.Count; i++)
        {
            if (!skip[i])
            {
                var l = Level(lines[i].Text);
                if (l > 0 && l <= level)
                    break;
            }

            if (!string.IsNullOrWhiteSpace(lines[i].Text))
                last = i;
        }

        return last;
    }

    /// <summary>Lines inside YAML frontmatter or fenced code blocks (fence lines included).</summary>
    public static bool[] IgnoredLines(IReadOnlyList<NoteLine> lines)
    {
        var skip = new bool[lines.Count];
        var i = 0;
        if (lines.Count > 0 && lines[0].Text.TrimEnd() == "---")
        {
            for (var j = 1; j < lines.Count; j++)
            {
                var t = lines[j].Text.TrimEnd();
                if (t == "---" || t == "...")
                {
                    for (var k = 0; k <= j; k++)
                        skip[k] = true;
                    i = j + 1;
                    break;
                }
            }
        }

        string? fence = null;
        for (; i < lines.Count; i++)
        {
            var m = Fence.Match(lines[i].Text);
            if (fence is null)
            {
                if (m.Success)
                {
                    fence = m.Groups[1].Value;
                    skip[i] = true;
                }

                continue;
            }

            skip[i] = true;
            // Closing fence: same character, at least as long, nothing else on the line.
            if (m.Success && m.Groups[1].Value[0] == fence[0] && m.Groups[1].Value.Length >= fence.Length &&
                lines[i].Text.Trim().Trim(fence[0]).Length == 0)
                fence = null;
        }

        return skip;
    }
}
