using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using Sideclip.Screenshot;

namespace Sideclip.Obsidian;

/// <summary>How a copied screenshot is referenced from the daily note.</summary>
internal enum DailyNoteLinkStyle
{
    /// <summary>Obsidian embed: ![[folder/name.png]]. Used when the copy folder is inside the vault.</summary>
    Embed = 0,
    /// <summary>Wikilink: [[folder/name.png]]. Default for link mode (copy folder outside the vault).</summary>
    Wikilink = 1,
    /// <summary>Markdown: [shot](folder/name.png) with the target percent-encoded.</summary>
    Markdown = 2
}

/// <summary>Builds the Markdown that Sideclip appends to a daily note. Pure string work.</summary>
internal static class DailyNoteText
{
    public const string DefaultVaultFolder = "Attachments/Shots";
    public const string MarkdownLabel = "shot";

    private static readonly char[] WikiBreakers = { '[', ']', '|', '#', '^' };

    /// <summary>
    /// Builds one timestamped line for a file that already lives inside the vault.
    /// Never emits a file:/// URI. Callers in link mode must copy into the vault first.
    /// </summary>
    public static string EntryLine(string vault, string file, DateTime at, DailyNoteLinkStyle style = DailyNoteLinkStyle.Embed)
    {
        var time = at.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        if (!ScreenshotRules.IsInside(file, vault))
            throw new ArgumentException("Daily-note entry file must be inside the vault (no file:/// links).", nameof(file));

        var rel = Path.GetRelativePath(ScreenshotRules.Normalize(vault), Path.GetFullPath(file)).Replace('\\', '/');
        return "- " + time + " " + LinkBody(rel, style);
    }

    /// <summary>Link / embed body for a vault-relative path using forward slashes.</summary>
    public static string LinkBody(string vaultRelativePath, DailyNoteLinkStyle style)
    {
        var rel = vaultRelativePath.Replace('\\', '/');
        return style switch
        {
            DailyNoteLinkStyle.Markdown => "[" + MarkdownLabel + "](" + EncodePath(rel) + ")",
            DailyNoteLinkStyle.Wikilink => WikilinkOrMarkdownFallback(rel, embed: false),
            _ => WikilinkOrMarkdownFallback(rel, embed: true)
        };
    }

    private static string WikilinkOrMarkdownFallback(string rel, bool embed)
    {
        if (rel.IndexOfAny(WikiBreakers) >= 0)
            return embed ? "![](" + EncodePath(rel) + ")" : "[" + MarkdownLabel + "](" + EncodePath(rel) + ")";
        return embed ? "![[" + rel + "]]" : "[[" + rel + "]]";
    }

    /// <summary>
    /// Validates and normalizes a vault-relative destination folder.
    /// Rejects rooted paths, drive letters, and '..' segments. Empty becomes <see cref="DefaultVaultFolder"/>.
    /// </summary>
    public static bool TryNormalizeVaultFolder(string? folder, out string normalized, out string reason)
    {
        normalized = DefaultVaultFolder;
        reason = string.Empty;
        var raw = (folder ?? string.Empty).Trim().Replace('\\', '/');
        if (raw.Length == 0)
        {
            normalized = DefaultVaultFolder;
            return true;
        }

        if (Path.IsPathRooted(raw) || raw.Contains(':') || raw.StartsWith('/') || raw.StartsWith('\\'))
        {
            reason = "Use a vault-relative folder (no drive letter or rooted path).";
            return false;
        }

        var parts = raw.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            normalized = DefaultVaultFolder;
            return true;
        }

        foreach (var p in parts)
        {
            if (p is "." or "..")
            {
                reason = "Folder must stay inside the vault (no '..').";
                return false;
            }

            if (p.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
            {
                reason = "Folder has invalid characters.";
                return false;
            }
        }

        normalized = string.Join('/', parts);
        return true;
    }

    public static DailyNoteLinkStyle ParseLinkStyle(string? value)
    {
        if (string.Equals(value, "Markdown", StringComparison.OrdinalIgnoreCase))
            return DailyNoteLinkStyle.Markdown;
        if (string.Equals(value, "Wikilink", StringComparison.OrdinalIgnoreCase))
            return DailyNoteLinkStyle.Wikilink;
        if (string.Equals(value, "Embed", StringComparison.OrdinalIgnoreCase))
            return DailyNoteLinkStyle.Embed;
        // Existing profiles that used outside-vault (link) mode get Wikilink by default.
        return DailyNoteLinkStyle.Wikilink;
    }

    public static string LinkStyleName(DailyNoteLinkStyle style) => style switch
    {
        DailyNoteLinkStyle.Markdown => "Markdown",
        DailyNoteLinkStyle.Embed => "Embed",
        _ => "Wikilink"
    };

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
