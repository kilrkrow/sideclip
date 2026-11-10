using Sideclip.Obsidian;
using Xunit;

namespace Sideclip.Tests;

public class MomentFormatTests
{
    private static readonly DateTime Mon = new(2026, 1, 5, 14, 5, 9, 123); // Monday

    [Theory]
    [InlineData("YYYY-MM-DD", "2026-01-05")]
    [InlineData("YY M D", "26 1 5")]
    [InlineData("YYYY/MMMM/YYYY-MMM-DD", "2026/January/2026-Jan-05")]
    [InlineData("dddd, MMMM Do YYYY", "Monday, January 5th 2026")]
    [InlineData("ddd DD.MM", "Mon 05.01")]
    [InlineData("dd d E e", "Mo 1 1 1")]
    [InlineData("[Daily] YYYY-MM-DD", "Daily 2026-01-05")]
    [InlineData("[YYYY] YYYY", "YYYY 2026")]
    [InlineData("YYYY [W]WW", "2026 W02")]
    [InlineData("HH:mm:ss", "14:05:09")]
    [InlineData("H:m:s.SSS", "14:5:9.123")]
    [InlineData("h:mm A a", "2:05 PM pm")]
    [InlineData("k kk", "14 14")]
    [InlineData("Q Qo Mo DDD DDDD DDDo", "1 1st 1st 5 005 5th")]
    [InlineData("[Journal]/YYYY/MM/DD", "Journal/2026/01/05")]
    [InlineData("YYYY_MM_DD - ddd", "2026_01_05 - Mon")]
    public void Formats_tokens(string format, string expected)
    {
        Assert.Equal(expected, MomentFormat.Format(format, Mon));
    }

    [Theory]
    [InlineData(1, "1st")]
    [InlineData(2, "2nd")]
    [InlineData(3, "3rd")]
    [InlineData(4, "4th")]
    [InlineData(11, "11th")]
    [InlineData(12, "12th")]
    [InlineData(13, "13th")]
    [InlineData(21, "21st")]
    [InlineData(22, "22nd")]
    [InlineData(23, "23rd")]
    [InlineData(31, "31st")]
    public void Ordinal_day(int day, string expected)
    {
        Assert.Equal(expected, MomentFormat.Format("Do", new DateTime(2026, 1, day)));
    }

    [Fact]
    public void Week_tokens_around_new_year()
    {
        // 2026-12-31 is a Thursday. ISO: week 53 of 2026. Moment "en" locale: week 1 of 2027.
        var d = new DateTime(2026, 12, 31);
        Assert.Equal("2026-W53", MomentFormat.Format("GGGG-[W]WW", d));
        Assert.Equal("2027-01", MomentFormat.Format("gggg-ww", d));
        Assert.Equal("1", MomentFormat.Format("w", new DateTime(2026, 1, 1)));
        Assert.Equal("2", MomentFormat.Format("w", new DateTime(2026, 1, 4))); // Sunday starts week 2
    }

    [Fact]
    public void Single_letter_tokens_are_not_standard_dotnet_formats()
    {
        // "M" or "D" alone must not turn into .NET's standard "month/day" or "long date" patterns.
        Assert.Equal("1", MomentFormat.Format("M", Mon));
        Assert.Equal("5", MomentFormat.Format("D", Mon));
        Assert.Equal("14", MomentFormat.Format("H", Mon));
    }

    [Fact]
    public void Unclosed_bracket_is_literal()
    {
        Assert.Equal("2026 [x", MomentFormat.Format("YYYY [x", Mon));
    }

    [Fact]
    public void Unknown_characters_pass_through()
    {
        Assert.Equal("2026 - _ , ; ()", MomentFormat.Format("YYYY - _ , ; ()", Mon));
    }
}
