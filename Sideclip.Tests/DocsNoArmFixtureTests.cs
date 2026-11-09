using System.IO;
using System.Xml.Linq;
using System.Text.RegularExpressions;

namespace Sideclip.Tests;

/// <summary>
/// Piece (d) docs/fixture guards only. Does not exercise Picker/App runtime.
/// </summary>
public sealed class DocsNoArmFixtureTests
{
    private static string SourceRoot
    {
        get
        {
            var directory = new DirectoryInfo(AppContext.BaseDirectory);
            while (directory.Parent is not null && !File.Exists(Path.Combine(directory.FullName, "App.xaml.cs")))
                directory = directory.Parent;
            if (!File.Exists(Path.Combine(directory.FullName, "App.xaml.cs")))
                throw new DirectoryNotFoundException("Sideclip source root not found from " + AppContext.BaseDirectory);
            return directory.FullName;
        }
    }

    private static string Read(params string[] parts) =>
        File.ReadAllText(Path.Combine(new[] { SourceRoot }.Concat(parts).ToArray()));

    private static void AssertAscii(string path, string text)
    {
        Assert.DoesNotContain("\u2026", text);
        foreach (var ch in text)
        {
            if (ch is '\r' or '\n' or '\t') continue;
            Assert.True(ch <= 0x7F, path + " contains non-ASCII U+" + ((int)ch).ToString("X4"));
        }
    }

    // Personal identities belong in the external release scan, not hard-coded
    // into the public test source. These guards enforce generic public metadata.
    private static string? PublicDocumentViolation(string text)
    {
        if (Regex.IsMatch(text, @"(?i)[A-Z]:\\Users\\|/home/[^/\s]+/"))
            return "A public document contains a private profile path.";
        foreach (Match field in Regex.Matches(text,
            @"(?im)^\s*(?:[-*]\s*)?(?:author|maintainer)\s*:\s*(?<value>[^\r\n]+)$"))
            if (!string.Equals(field.Groups["value"].Value.Trim(), "kilrkrow", StringComparison.Ordinal))
                return "Public authorship must use the approved pen name.";
        return null;
    }

    private static void AssertPublicDocument(string text) => Assert.Null(PublicDocumentViolation(text));

    [Theory]
    [InlineData("Author: synthetic-unapproved-handle")]
    [InlineData(@"C:\Users\synthetic-profile\document.txt")]
    [InlineData("/home/synthetic-profile/document.txt")]
    public void Public_privacy_guards_reject_nonpersonal_negative_fixtures(string fixture)
    {
        Assert.NotNull(PublicDocumentViolation(fixture));
        AssertPublicDocument("Author: kilrkrow\nGeneric public instructions.");
    }

    [Fact]
    public void Public_project_and_package_authorship_allow_only_the_pen_name()
    {
        var project = XDocument.Parse(Read("Sideclip.csproj"));
        var package = XDocument.Parse(Read("pack", "chocolatey", "sideclip", "sideclip.nuspec"));
        Assert.Equal("kilrkrow", Assert.Single(project.Descendants(), element => element.Name.LocalName == "Authors").Value);
        Assert.Equal("Copyright (c) 2026 kilrkrow", Assert.Single(project.Descendants(), element => element.Name.LocalName == "Copyright").Value);
        Assert.Equal("kilrkrow", Assert.Single(package.Descendants(), element => element.Name.LocalName == "authors").Value);
        foreach (var owner in package.Descendants().Where(element => element.Name.LocalName == "owners"))
            Assert.Equal("kilrkrow", owner.Value);
    }

    [Fact]
    public void Shipped_readme_describes_no_Arm_copy_only_and_Settings()
    {
        var readme = Read("README.md");
        AssertAscii("README.md", readme);
        Assert.Contains("Settings...", readme, StringComparison.Ordinal);
        Assert.Contains("Enable clipboard-free text type-out", readme, StringComparison.Ordinal);
        Assert.Contains("deliberately select", readme, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("ordinary Windows clipboard only", readme, StringComparison.Ordinal);
        Assert.Contains("does not simulate Ctrl+V", readme, StringComparison.Ordinal);
        Assert.Contains("0.3.0", readme, StringComparison.Ordinal);
        Assert.Contains("kilrkrow", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("Arm for type-out", readme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Type selected text", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("Enter paste", readme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Hotkeys...", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("countdown", readme, StringComparison.OrdinalIgnoreCase);
        AssertPublicDocument(readme);
        Assert.DoesNotContain("D:\\_dev", readme, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("VENGEANCE", readme, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Typeout_test_plan_is_generic_no_Arm_with_ledger_and_soak()
    {
        var plan = Read("docs", "TYPEOUT-TEST-PLAN.md");
        AssertAscii("docs/TYPEOUT-TEST-PLAN.md", plan);
        Assert.Contains("no-Arm", plan, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Settings...", plan, StringComparison.Ordinal);
        Assert.Contains("0-250", plan, StringComparison.Ordinal);
        Assert.Contains("Honest evidence ledger", plan, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Soak plan", plan, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Physical Win-key capture", plan, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("held-key repeat", plan, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Insertion not verified", plan, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("AutoHotkey", plan, StringComparison.Ordinal);
        Assert.Contains("KeePass", plan, StringComparison.Ordinal);
        Assert.Contains("paste-blocked.html", plan, StringComparison.Ordinal);
        Assert.DoesNotContain("Arm for type-out", plan, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Type selected text", plan, StringComparison.Ordinal);
        Assert.DoesNotContain("tray status menu -> Go", plan, StringComparison.OrdinalIgnoreCase);
        AssertPublicDocument(plan);
        Assert.DoesNotContain("D:\\_dev", plan, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("VENGEANCE", plan, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("JARVIS", plan, StringComparison.Ordinal);
    }

    [Fact]
    public void Paste_blocked_fixture_allows_keyboard_blocks_paste_and_documents_Tab_Enter()
    {
        var html = Read("fixtures", "paste-blocked.html");
        AssertAscii("fixtures/paste-blocked.html", html);
        Assert.Contains("paste", html, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("preventDefault", html, StringComparison.Ordinal);
        Assert.Contains("insertFromPaste", html, StringComparison.Ordinal);
        Assert.Contains("insertFromDrop", html, StringComparison.Ordinal);
        Assert.Contains("Tab", html, StringComparison.Ordinal);
        Assert.Contains("Enter", html, StringComparison.Ordinal);
        Assert.Contains("DOM", html, StringComparison.Ordinal);
        Assert.Contains("textarea", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("fetch(", html, StringComparison.Ordinal);
        Assert.DoesNotContain("http://", html, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("https://", html, StringComparison.OrdinalIgnoreCase);
        AssertPublicDocument(html);
        Assert.True(Regex.IsMatch(html, "keydown"), "fixture should observe Tab/Enter for status honesty");
    }

    [Fact]
    public void Cli_docs_document_existing_secret_text_pick_not_cancel()
    {
        var cli = Read("docs", "CLI.md");
        AssertAscii("docs/CLI.md", cli);
        Assert.Contains("--pick", cli, StringComparison.Ordinal);
        Assert.Contains("clipboard only", cli, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Arm", cli, StringComparison.Ordinal);
        AssertPublicDocument(cli);

        // Existing App CommitPick/--pick boundary (baseline HEAD and current WIP):
        // secret selection returns kind=text then RemoveEntry; cancel is close-without-choice only.
        Assert.Contains("includes `text` for the chosen history entry", cli, StringComparison.Ordinal);
        Assert.Contains("deliberately selected secret entry", cli, StringComparison.Ordinal);
        Assert.Contains("`kind` `text`", cli, StringComparison.Ordinal);
        Assert.Contains("removes that secret entry from history", cli, StringComparison.Ordinal);
        Assert.Contains("closed the picker without committing a choice", cli, StringComparison.Ordinal);
        Assert.DoesNotContain("selected entry was secret", cli, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotMatch("(?i)password\\s*=|sk-[A-Za-z0-9]|BEGIN (RSA )?PRIVATE KEY", cli);
    }

    [Fact]
    public void Chocolatey_packaging_copy_describes_0_3_0_and_copy_only_picker()
    {
        var nuspec = Read("pack", "chocolatey", "sideclip", "sideclip.nuspec");
        var readme = Read("pack", "chocolatey", "README.md");
        Assert.Contains("<version>0.3.0</version>", nuspec, StringComparison.Ordinal);
        Assert.Contains("ordinary Windows clipboard only", nuspec, StringComparison.Ordinal);
        Assert.Contains("Settings...", nuspec, StringComparison.Ordinal);
        Assert.DoesNotContain("Enter paste", nuspec, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("Arm for type-out", nuspec, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("0.3.0", readme, StringComparison.Ordinal);
        Assert.Contains("sideclip.0.3.0.nupkg", readme, StringComparison.Ordinal);
        Assert.Contains("Sideclip-win-x64-v0.3.0.zip", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("v0.2.0", readme, StringComparison.Ordinal);
        Assert.DoesNotContain("sideclip.0.2.0.nupkg", readme, StringComparison.Ordinal);
        AssertPublicDocument(nuspec + readme);
    }
    [Fact]
    public void Local_publish_bundles_canonical_skill_without_checkout_or_user_sync()
    {
        var script = Read("scripts", "publish-release.ps1");
        Assert.Contains("$skillSrc = Join-Path $repoRoot '.agents\\skills\\sideclip'", script);
        Assert.Contains("Copy-Item -LiteralPath $skillSrc -Destination $skillDest -Recurse", script);
        Assert.DoesNotContain("sync-agent-skill.ps1", script);
        Assert.DoesNotContain("-InstallUser", script);
    }
}
