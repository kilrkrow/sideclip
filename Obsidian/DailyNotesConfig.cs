using System.IO;
using System.Text.Json;

namespace Sideclip.Obsidian;

/// <summary>
/// The core Daily notes plugin settings, from &lt;vault&gt;/.obsidian/daily-notes.json
/// (keys "folder", "format", "template"). Missing file or keys fall back to Obsidian's
/// defaults: format YYYY-MM-DD, vault root, no template.
/// </summary>
internal sealed record DailyNotesConfig(string Folder, string Format, string Template)
{
    public const string DefaultFormat = "YYYY-MM-DD";

    public static DailyNotesConfig Default { get; } = new(string.Empty, DefaultFormat, string.Empty);

    public static string ConfigPath(string vault) => Path.Combine(vault, ".obsidian", "daily-notes.json");

    public static DailyNotesConfig Load(string vault)
    {
        try
        {
            var path = ConfigPath(vault);
            if (!File.Exists(path))
                return Default;
            return Parse(File.ReadAllText(path));
        }
        catch
        {
            return Default;
        }
    }

    public static DailyNotesConfig Parse(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (doc.RootElement.ValueKind != JsonValueKind.Object)
                return Default;

            static string Str(JsonElement root, string name) =>
                root.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? (v.GetString() ?? "").Trim() : "";

            var folder = Str(doc.RootElement, "folder").Trim('/', '\\');
            var format = Str(doc.RootElement, "format");
            var template = Str(doc.RootElement, "template").Trim('/', '\\');
            return new DailyNotesConfig(folder, format.Length == 0 ? DefaultFormat : format, template);
        }
        catch (JsonException)
        {
            return Default;
        }
    }

    /// <summary>Vault-relative path of the note for <paramref name="date"/>, with forward slashes.</summary>
    public string RelativeNotePath(DateTime date)
    {
        var name = MomentFormat.Format(Format, date).Replace('\\', '/').Trim('/');
        var rel = Folder.Length == 0 ? name : Folder.Replace('\\', '/') + "/" + name;
        return rel + ".md";
    }

    public string NotePath(string vault, DateTime date) =>
        Path.GetFullPath(Path.Combine(vault, RelativeNotePath(date).Replace('/', Path.DirectorySeparatorChar)));

    /// <summary>Template note path; the setting is vault-relative and may omit ".md".</summary>
    public string? TemplatePath(string vault)
    {
        if (Template.Length == 0)
            return null;
        var rel = Template.Replace('/', Path.DirectorySeparatorChar);
        if (!rel.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            rel += ".md";
        return Path.GetFullPath(Path.Combine(vault, rel));
    }
}
