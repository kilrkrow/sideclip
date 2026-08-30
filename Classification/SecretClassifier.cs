using System.Text.RegularExpressions;

namespace Sideclip.Classification;

/// <summary>
/// Heuristic password detector. A normal URL, Windows path, email, or
/// English/sentence-like token must not be tagged secret.
/// </summary>
internal static class SecretClassifier
{
    private static readonly Regex SchemeUrl = new(
        @"^(https?://|ftp://|www\.)\S+$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    private static readonly Regex HostLikeUrl = new(
        @"^[a-z0-9][-a-z0-9]*(\.[a-z0-9][-a-z0-9]*)+(:\d+)?(/[^\s]*)?$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant | RegexOptions.Compiled);

    public static bool LooksLikePassword(string text)
    {
        if (string.IsNullOrEmpty(text))
            return false;

        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
                return false;
        }

        if (text.Length is < 8 or > 64)
            return false;

        if (IsUrl(text) || IsWindowsPath(text) || IsEmail(text) || IsUuid(text) || IsHexToken(text))
            return false;

        if (IsSentenceLike(text))
            return false;

        return HasMixedClasses(text, minClasses: 3) || HasHighEntropy(text);
    }

    private static bool IsUrl(string text)
    {
        if (SchemeUrl.IsMatch(text) || HostLikeUrl.IsMatch(text))
            return true;

        return Uri.TryCreate(text, UriKind.Absolute, out var uri)
               && (uri.Scheme == Uri.UriSchemeHttp
                   || uri.Scheme == Uri.UriSchemeHttps
                   || uri.Scheme == Uri.UriSchemeFtp);
    }

    private static bool IsWindowsPath(string text)
    {
        if (text.Length >= 3
            && char.IsLetter(text[0])
            && text[1] == ':'
            && (text[2] == '\\' || text[2] == '/'))
            return true;

        return text.StartsWith(@"\\", StringComparison.Ordinal);
    }

    private static bool IsEmail(string text)
    {
        var at = text.IndexOf('@');
        if (at <= 0 || at == text.Length - 1)
            return false;
        if (text.IndexOf('@', at + 1) >= 0)
            return false;

        var dot = text.LastIndexOf('.');
        return dot > at + 1 && dot < text.Length - 1;
    }

    private static bool IsUuid(string text) => Guid.TryParse(text, out _);

    private static bool IsHexToken(string text)
    {
        var hexDigits = 0;
        foreach (var c in text)
        {
            if (c is '-' or '{' or '}')
                continue;
            if (!Uri.IsHexDigit(c))
                return false;
            hexDigits++;
        }

        return hexDigits >= 8;
    }

    private static bool IsSentenceLike(string text)
    {
        if (text.EndsWith('.') || text.EndsWith('!') || text.EndsWith('?'))
        {
            var letters = 0;
            foreach (var c in text)
            {
                if (char.IsLetter(c))
                    letters++;
            }

            if (letters >= text.Length * 0.8)
                return true;
        }

        if (LooksLikeCamelCaseEnglish(text) && CountClasses(text) <= 2)
            return true;

        if (text.Contains('-') || text.Contains('_'))
        {
            if (CountClasses(text) <= 2)
            {
                var wordish = 0;
                foreach (var c in text)
                {
                    if (char.IsLetter(c) || c is '-' or '_')
                        wordish++;
                }

                if (wordish >= text.Length * 0.85)
                    return true;
            }
        }

        return false;
    }

    private static bool LooksLikeCamelCaseEnglish(string text)
    {
        var longLowerRuns = 0;
        var run = 0;
        foreach (var c in text)
        {
            if (char.IsLower(c))
            {
                run++;
            }
            else
            {
                if (run >= 3)
                    longLowerRuns++;
                run = 0;
            }
        }

        if (run >= 3)
            longLowerRuns++;

        return longLowerRuns >= 2;
    }

    private static bool HasMixedClasses(string text, int minClasses) => CountClasses(text) >= minClasses;

    private static int CountClasses(string text)
    {
        var upper = false;
        var lower = false;
        var digit = false;
        var symbol = false;
        foreach (var c in text)
        {
            if (char.IsUpper(c))
                upper = true;
            else if (char.IsLower(c))
                lower = true;
            else if (char.IsDigit(c))
                digit = true;
            else
                symbol = true;
        }

        var n = 0;
        if (upper) n++;
        if (lower) n++;
        if (digit) n++;
        if (symbol) n++;
        return n;
    }

    private static bool HasHighEntropy(string text)
    {
        var freq = new Dictionary<char, int>();
        foreach (var c in text)
            freq[c] = freq.TryGetValue(c, out var n) ? n + 1 : 1;

        double entropy = 0;
        var len = (double)text.Length;
        foreach (var n in freq.Values)
        {
            var p = n / len;
            entropy -= p * Math.Log2(p);
        }

        var uniqueRatio = freq.Count / len;
        return entropy >= 3.5
               && uniqueRatio >= 0.55
               && CountClasses(text) >= 2
               && text.Length >= 12;
    }

    /// <summary>
    /// Used when the clip is from 1Password (desktop or browser plugin).
    /// Tag almost anything that is not an obvious URL / path / email / UUID / paragraph.
    /// Passphrases with spaces still count; a copied Firefox URL does not.
    /// </summary>
    public static bool LooksLikePasswordManagerPayload(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var t = text.Trim();
        if (t.Length is < 4 or > 256)
            return false;

        if (IsUrl(t) || IsWindowsPath(t) || IsEmail(t) || IsUuid(t))
            return false;

        var compacted = t.Replace(" ", string.Empty);
        if (IsHexToken(compacted) || IsUuid(compacted))
            return false;

        if (IsLongProse(t))
            return false;

        return true;
    }

    private static bool IsLongProse(string text)
    {
        var spaces = 0;
        foreach (var c in text)
        {
            if (char.IsWhiteSpace(c))
                spaces++;
        }

        if (spaces >= 8)
            return true;

        var endsSentence = text.EndsWith('.') || text.EndsWith('!') || text.EndsWith('?');
        if (endsSentence && spaces >= 3)
            return true;

        if (text.Contains(". ", StringComparison.Ordinal) && spaces >= 3)
            return true;

        return false;
    }
}
