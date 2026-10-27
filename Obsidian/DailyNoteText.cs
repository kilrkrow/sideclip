using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Sideclip.Screenshot;

namespace Sideclip.Obsidian;

/// <summary>Builds the Markdown that Sideclip appends to a daily note. Pure string work.</summary>
internal static class DailyNoteText
{
    private static readonly char[] WikiBreakers = { '[', ']', '|', '#', '^' };

    /// <summary>
    /// "- 14:05:32 ![[Screens/shot.png]]" when the file is inside the vault,
    /// "- 14:05:32 [shot.png](file:///D:/Shots/shot.png)" when it is not.
    /// </summary>
    public static string EntryLine(string vault, string file, DateTime at)
    {
        var time = at.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        if (ScreenshotRules.IsInside(file, vault))
        {
            var rel = Path.GetRelativePath(ScreenshotRules.Normalize(vault), Path.GetFullPath(file)).Replace('\\', '/');
            // Wikilinks cannot carry these characters; fall back to a Markdown embed.
            if (rel.IndexOfAny(WikiBreakers) >= 0)
                return "- " + time + " ![](" + EncodePath(rel) + ")";
            return "- " + time + " ![[" + rel + "]]";
        }

        var name = Path.GetFileName(file).Replace("[", "\\[").Replace("]", "\\]");
        return "- " + time + " [" + name + "](" + FileUrl(file) + ")";
    }

    /// <summary>file:/// URL with spaces and Markdown-breaking characters percent-encoded.</summary>
    public static string FileUrl(string path)
    {
        var full = Path.GetFullPath(path);
        var sb = new StringBuilder("file:///");
        var parts = full.Replace('\\', '/').Split('/');
        for (var i = 0; i < parts.Length; i++)
        {
            if (i > 0)
                sb.Append('/');
            // Keep the drive colon ("D:") readable; encode everything else that is not URL-safe.
            sb.Append(i == 0 && parts[0].EndsWith(':') ? parts[0] : EncodeSegment(parts[i]));
        }

        return sb.ToString();
    }

    private static string EncodePath(string rel) =>
        string.Join("/", rel.Split('/').Select(EncodeSegment));

    private static string EncodeSegment(string s)
    {
        var sb = new StringBuilder();
        foreach (var b in Encoding.UTF8.GetBytes(s))
        {
            var c = (char)b;
            if (b < 0x80 && (char.IsAsciiLetterOrDigit(c) || c is '-' or '_' or '.' or '~'))
                sb.Append(c);
            else
                sb.Append('%').Append(b.ToString("X2", CultureInfo.InvariantCulture));
        }

        return sb.ToString();
    }

    /// <summary>Collapsed callout with OCR text, one "&gt; " line per text line.</summary>
    public static IReadOnlyList<string> OcrCallout(string text)
    {
        var lines = new List<string> { "> [!note]- OCR text" };
        foreach (var raw in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = raw.TrimEnd();
            lines.Add(line.Length == 0 ? ">" : "> " + line);
        }

        return lines;
    }

    private static readonly Regex DateTimeVar = new(
        @"\{\{\s*(date|time)\s*(?:([+-]\d+)([yqmwdhs]))?\s*(?::(.*?))?\s*\}\}",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    /// <summary>
    /// Fills a daily-note template the way Obsidian's Daily notes plugin does:
    /// {{title}} and {{date}} become the note name, {{time}} is HH:mm, {{date:FORMAT}} /
    /// {{time:FORMAT}} use Moment tokens (optionally offset, e.g. {{date+1d:YYYY-MM-DD}}),
    /// {{yesterday}} / {{tomorrow}} use the daily-note format.
    /// </summary>
    public static string ApplyTemplate(string template, DateTime date, string dailyFormat, string title)
    {
        var s = Regex.Replace(template, @"\{\{\s*title\s*\}\}", _ => title, RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\{\{\s*date\s*\}\}", _ => title, RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\{\{\s*time\s*\}\}", _ => MomentFormat.Format("HH:mm", date), RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\{\{\s*yesterday\s*\}\}", _ => MomentFormat.Format(dailyFormat, date.AddDays(-1)), RegexOptions.IgnoreCase);
        s = Regex.Replace(s, @"\{\{\s*tomorrow\s*\}\}", _ => MomentFormat.Format(dailyFormat, date.AddDays(1)), RegexOptions.IgnoreCase);
        s = DateTimeVar.Replace(s, m =>
        {
            var d = date;
            if (m.Groups[2].Success)
            {
                var n = int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture);
                d = m.Groups[3].Value.ToLowerInvariant() switch
                {
                    "y" => d.AddYears(n),
                    "q" => d.AddMonths(3 * n),
                    "m" => d.AddMonths(n),
                    "w" => d.AddDays(7 * n),
                    "d" => d.AddDays(n),
                    "h" => d.AddHours(n),
                    _ => d.AddSeconds(n)
                };
            }

            var fmt = m.Groups[4].Success && m.Groups[4].Value.Trim().Length > 0
                ? m.Groups[4].Value.Trim()
                : string.Equals(m.Groups[1].Value, "time", StringComparison.OrdinalIgnoreCase) ? "HH:mm" : dailyFormat;
            return MomentFormat.Format(fmt, d);
        });
        return s;
    }
}
