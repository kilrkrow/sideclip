using System.Globalization;
using System.Text;

namespace Sideclip.Obsidian;

/// <summary>
/// Formats a date with a Moment.js format string (what Obsidian uses for daily-note names and
/// {{date:...}} template variables). Each Moment token is converted to the matching .NET custom
/// date specifier; tokens with no .NET equivalent (ordinals, quarters, weeks) are computed.
/// Text inside [brackets] is literal. Any other character is copied as is, like Moment does.
/// English names (Obsidian's default locale).
/// </summary>
internal static class MomentFormat
{
    private static readonly CultureInfo En = CultureInfo.InvariantCulture;

    // Longest first so "MMMM" wins over "MMM", "MM" and "M".
    private static readonly string[] Tokens =
    {
        "YYYY", "GGGG", "gggg", "MMMM", "DDDD", "dddd", "SSS",
        "MMM", "DDD", "ddd", "DDDo",
        "YY", "GG", "gg", "Mo", "MM", "Do", "DD", "do", "dd", "Wo", "WW", "wo", "ww", "Qo",
        "HH", "hh", "kk", "mm", "ss", "SS", "ZZ",
        "Y", "Q", "M", "D", "d", "e", "E", "W", "w", "H", "h", "k", "m", "s", "S", "A", "a", "X", "x", "Z"
    };

    /// <summary>Moment token -> .NET custom format specifier, for the tokens .NET can do directly.</summary>
    public static readonly IReadOnlyDictionary<string, string> DotNetEquivalent = new Dictionary<string, string>(StringComparer.Ordinal)
    {
        ["YYYY"] = "yyyy",
        ["YY"] = "yy",
        ["MMMM"] = "MMMM",
        ["MMM"] = "MMM",
        ["MM"] = "MM",
        ["M"] = "%M",
        ["DD"] = "dd",
        ["D"] = "%d",
        ["dddd"] = "dddd",
        ["ddd"] = "ddd",
        ["HH"] = "HH",
        ["H"] = "%H",
        ["hh"] = "hh",
        ["h"] = "%h",
        ["mm"] = "mm",
        ["m"] = "%m",
        ["ss"] = "ss",
        ["s"] = "%s",
        ["SSS"] = "fff",
        ["SS"] = "ff",
        ["S"] = "%f",
        ["A"] = "tt",
    };

    static MomentFormat()
    {
        // Sort by length so longer tokens are matched first regardless of the table order above.
        Array.Sort(Tokens, (a, b) => b.Length.CompareTo(a.Length));
    }

    public static string Format(string format, DateTime date)
    {
        var sb = new StringBuilder();
        var i = 0;
        while (i < format.Length)
        {
            var c = format[i];
            if (c == '[')
            {
                var end = format.IndexOf(']', i + 1);
                if (end < 0)
                {
                    sb.Append(format, i, format.Length - i);
                    break;
                }

                sb.Append(format, i + 1, end - i - 1);
                i = end + 1;
                continue;
            }

            var token = Match(format, i);
            if (token is null)
            {
                sb.Append(c);
                i++;
                continue;
            }

            sb.Append(Render(token, date));
            i += token.Length;
        }

        return sb.ToString();
    }

    private static string? Match(string format, int at)
    {
        foreach (var t in Tokens)
        {
            if (string.CompareOrdinal(format, at, t, 0, t.Length) == 0)
                return t;
        }

        return null;
    }

    private static string Render(string token, DateTime d)
    {
        if (DotNetEquivalent.TryGetValue(token, out var spec))
            return d.ToString(spec, En);

        switch (token)
        {
            case "Y": return d.Year.ToString(En);
            case "Q": return Quarter(d).ToString(En);
            case "Qo": return Ordinal(Quarter(d));
            case "Mo": return Ordinal(d.Month);
            case "Do": return Ordinal(d.Day);
            case "DDDD": return d.DayOfYear.ToString("000", En);
            case "DDD": return d.DayOfYear.ToString(En);
            case "DDDo": return Ordinal(d.DayOfYear);
            case "dd": return d.ToString("ddd", En)[..2];
            case "d":
            case "e": return ((int)d.DayOfWeek).ToString(En);
            case "do": return Ordinal((int)d.DayOfWeek);
            case "E": return (d.DayOfWeek == DayOfWeek.Sunday ? 7 : (int)d.DayOfWeek).ToString(En);
            case "W": return ISOWeek.GetWeekOfYear(d).ToString(En);
            case "WW": return ISOWeek.GetWeekOfYear(d).ToString("00", En);
            case "Wo": return Ordinal(ISOWeek.GetWeekOfYear(d));
            case "GGGG": return ISOWeek.GetYear(d).ToString("0000", En);
            case "GG": return (ISOWeek.GetYear(d) % 100).ToString("00", En);
            case "w": return LocaleWeek(d).ToString(En);
            case "ww": return LocaleWeek(d).ToString("00", En);
            case "wo": return Ordinal(LocaleWeek(d));
            case "gggg": return LocaleWeekYear(d).ToString("0000", En);
            case "gg": return (LocaleWeekYear(d) % 100).ToString("00", En);
            case "k": return (d.Hour == 0 ? 24 : d.Hour).ToString(En);
            case "kk": return (d.Hour == 0 ? 24 : d.Hour).ToString("00", En);
            case "a": return d.ToString("tt", En).ToLowerInvariant();
            case "X": return new DateTimeOffset(d).ToUnixTimeSeconds().ToString(En);
            case "x": return new DateTimeOffset(d).ToUnixTimeMilliseconds().ToString(En);
            case "Z": return Offset(d, colon: true);
            case "ZZ": return Offset(d, colon: false);
            default: return token;
        }
    }

    private static int Quarter(DateTime d) => (d.Month - 1) / 3 + 1;

    // Moment "en" locale weeks: Sunday start, week 1 contains Jan 1 (the week belongs to its Saturday's year).
    private static DateTime WeekSaturday(DateTime d) => d.Date.AddDays(6 - (int)d.DayOfWeek);
    private static int LocaleWeek(DateTime d) => (WeekSaturday(d).DayOfYear - 1) / 7 + 1;
    private static int LocaleWeekYear(DateTime d) => WeekSaturday(d).Year;

    private static string Offset(DateTime d, bool colon)
    {
        var off = d.Kind == DateTimeKind.Utc ? TimeSpan.Zero : TimeZoneInfo.Local.GetUtcOffset(d);
        var sign = off < TimeSpan.Zero ? "-" : "+";
        off = off.Duration();
        return sign + off.Hours.ToString("00", En) + (colon ? ":" : "") + off.Minutes.ToString("00", En);
    }

    public static string Ordinal(int n)
    {
        var rem100 = n % 100;
        var suffix = rem100 is >= 11 and <= 13
            ? "th"
            : (n % 10) switch { 1 => "st", 2 => "nd", 3 => "rd", _ => "th" };
        return n.ToString(En) + suffix;
    }
}
