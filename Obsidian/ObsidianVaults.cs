using System.IO;
using System.Text.Json;

namespace Sideclip.Obsidian;

internal sealed record ObsidianVault(string Id, string Path, long Ts, bool Open)
{
    public string Name => System.IO.Path.GetFileName(Path.TrimEnd('\\', '/'));
}

/// <summary>
/// Reads Obsidian's vault registry: %APPDATA%\obsidian\obsidian.json,
/// { "vaults": { "&lt;id&gt;": { "path": "...", "ts": 1700000000000, "open": true } } }.
/// Read only; Sideclip never writes it.
/// </summary>
internal static class ObsidianVaults
{
    /// <summary>Test hook: read this obsidian.json instead of the real one.</summary>
    public const string RegistryEnv = "SIDECLIP_OBSIDIAN_JSON";

    public static string RegistryPath
    {
        get
        {
            try
            {
                var env = Environment.GetEnvironmentVariable(RegistryEnv);
                if (!string.IsNullOrWhiteSpace(env))
                    return System.IO.Path.GetFullPath(env.Trim());
            }
            catch
            {
            }

            return System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "obsidian", "obsidian.json");
        }
    }

    /// <summary>Known vaults, open vault first, then most recently used. Empty on any problem.</summary>
    public static IReadOnlyList<ObsidianVault> Load(string? registryPath = null)
    {
        try
        {
            var path = registryPath ?? RegistryPath;
            if (!File.Exists(path))
                return Array.Empty<ObsidianVault>();
            return Parse(File.ReadAllText(path));
        }
        catch
        {
            return Array.Empty<ObsidianVault>();
        }
    }

    public static IReadOnlyList<ObsidianVault> Parse(string json)
    {
        var list = new List<ObsidianVault>();
        try
        {
            using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            if (doc.RootElement.ValueKind != JsonValueKind.Object ||
                !doc.RootElement.TryGetProperty("vaults", out var vaults) ||
                vaults.ValueKind != JsonValueKind.Object)
                return list;

            foreach (var v in vaults.EnumerateObject())
            {
                if (v.Value.ValueKind != JsonValueKind.Object)
                    continue;
                if (!v.Value.TryGetProperty("path", out var p) || p.ValueKind != JsonValueKind.String)
                    continue;
                var path = p.GetString();
                if (string.IsNullOrWhiteSpace(path))
                    continue;

                long ts = 0;
                if (v.Value.TryGetProperty("ts", out var t) && t.ValueKind == JsonValueKind.Number)
                    t.TryGetInt64(out ts);
                var open = v.Value.TryGetProperty("open", out var o) && o.ValueKind == JsonValueKind.True;
                list.Add(new ObsidianVault(v.Name, path, ts, open));
            }
        }
        catch (JsonException)
        {
            return new List<ObsidianVault>();
        }

        list.Sort((a, b) =>
        {
            if (a.Open != b.Open)
                return a.Open ? -1 : 1;
            return b.Ts.CompareTo(a.Ts);
        });
        return list;
    }
}
