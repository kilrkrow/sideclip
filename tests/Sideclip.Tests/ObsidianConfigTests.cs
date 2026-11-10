using Sideclip.Obsidian;
using Xunit;

namespace Sideclip.Tests;

public class ObsidianVaultsTests
{
    private const string Sample = """
    {
      "vaults": {
        "a179fe5a7cf0f399": { "path": "D:\\Vaults\\Work", "ts": 1670708098766 },
        "e8e26f57e95a42bb": { "path": "D:\\Vaults\\Home", "ts": 1666589798427, "open": true },
        "6ebff3494854a96b": { "path": "D:\\Vaults\\Newest", "ts": 1770000000000 },
        "bad1": { "ts": 1 },
        "bad2": "nope"
      },
      "openSchemes": { "app": true },
      "frame": "custom"
    }
    """;

    [Fact]
    public void Parses_vaults_open_first_then_most_recent()
    {
        var vaults = ObsidianVaults.Parse(Sample);
        Assert.Equal(new[] { @"D:\Vaults\Home", @"D:\Vaults\Newest", @"D:\Vaults\Work" }, vaults.Select(v => v.Path));
        Assert.True(vaults[0].Open);
        Assert.Equal("e8e26f57e95a42bb", vaults[0].Id);
        Assert.Equal(1666589798427, vaults[0].Ts);
        Assert.Equal("Home", vaults[0].Name);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json")]
    [InlineData("[]")]
    [InlineData("{}")]
    [InlineData("{\"vaults\": []}")]
    public void Bad_registry_gives_empty_list(string json)
    {
        Assert.Empty(ObsidianVaults.Parse(json));
    }

    [Fact]
    public void Missing_registry_file_gives_empty_list()
    {
        using var dir = new TestDir();
        Assert.Empty(ObsidianVaults.Load(Path.Combine(dir.Path, "obsidian.json")));
    }

    [Fact]
    public void Load_reads_file()
    {
        using var dir = new TestDir();
        var path = Path.Combine(dir.Path, "obsidian.json");
        File.WriteAllText(path, Sample);
        Assert.Equal(3, ObsidianVaults.Load(path).Count);
    }
}

public class DailyNotesConfigTests
{
    [Fact]
    public void Missing_config_uses_obsidian_defaults()
    {
        using var dir = new TestDir();
        var cfg = DailyNotesConfig.Load(dir.Path);
        Assert.Equal("", cfg.Folder);
        Assert.Equal("YYYY-MM-DD", cfg.Format);
        Assert.Equal("", cfg.Template);
        Assert.Equal("2026-10-27.md", cfg.RelativeNotePath(new DateTime(2026, 10, 27)));
        Assert.Null(cfg.TemplatePath(dir.Path));
    }

    [Fact]
    public void Reads_folder_format_template()
    {
        using var dir = new TestDir();
        Directory.CreateDirectory(Path.Combine(dir.Path, ".obsidian"));
        File.WriteAllText(Path.Combine(dir.Path, ".obsidian", "daily-notes.json"),
            "{\"folder\":\"Journal/Daily\",\"format\":\"YYYY/MMMM/YYYY-MMM-DD\",\"template\":\"Templates/Daily\",\"autorun\":false}");
        var cfg = DailyNotesConfig.Load(dir.Path);
        Assert.Equal("Journal/Daily", cfg.Folder);
        Assert.Equal("Journal/Daily/2026/October/2026-Oct-27.md", cfg.RelativeNotePath(new DateTime(2026, 10, 27)));
        Assert.Equal(Path.Combine(dir.Path, "Journal", "Daily", "2026", "October", "2026-Oct-27.md"),
            cfg.NotePath(dir.Path, new DateTime(2026, 10, 27)));
        Assert.Equal(Path.Combine(dir.Path, "Templates", "Daily.md"), cfg.TemplatePath(dir.Path));
    }

    [Theory]
    [InlineData("{\"folder\":\"/Daily/\"}", "Daily", "YYYY-MM-DD")]
    [InlineData("{\"folder\":\"\",\"format\":\"\"}", "", "YYYY-MM-DD")]
    [InlineData("{\"format\":\"DD-MM-YYYY\"}", "", "DD-MM-YYYY")]
    [InlineData("{\"folder\":42}", "", "YYYY-MM-DD")]
    [InlineData("garbage", "", "YYYY-MM-DD")]
    public void Partial_or_bad_config_falls_back(string json, string folder, string format)
    {
        var cfg = DailyNotesConfig.Parse(json);
        Assert.Equal(folder, cfg.Folder);
        Assert.Equal(format, cfg.Format);
    }

    [Fact]
    public void Template_path_keeps_existing_md_extension()
    {
        var cfg = DailyNotesConfig.Parse("{\"template\":\"T/Day.md\"}");
        Assert.EndsWith(Path.Combine("T", "Day.md"), cfg.TemplatePath(@"C:\v"));
    }
}
