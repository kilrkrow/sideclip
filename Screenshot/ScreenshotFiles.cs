using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Sideclip.Screenshot;

internal static class ScreenshotFiles
{
    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = false,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Folder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyPictures), "Screenshots");

    public static IReadOnlyList<FileInfo> Newest(int n)
    {
        if (n < 1)
            n = 1;

        var dir = Folder;
        if (!Directory.Exists(dir))
            return Array.Empty<FileInfo>();

        var files = new List<FileInfo>();
        foreach (var file in new DirectoryInfo(dir).EnumerateFiles("*.*", SearchOption.TopDirectoryOnly))
        {
            if ((file.Attributes & FileAttributes.Hidden) != 0)
                continue;
            files.Add(file);
        }

        files.Sort(static (a, b) => b.LastWriteTimeUtc.CompareTo(a.LastWriteTimeUtc));
        if (files.Count > n)
            files.RemoveRange(n, files.Count - n);
        return files;
    }

    public static FileInfo? NewestOne()
    {
        var list = Newest(1);
        return list.Count == 0 ? null : list[0];
    }

    public static string ToJson(IReadOnlyList<FileInfo> files)
    {
        var rows = new ShotRow[files.Count];
        for (var i = 0; i < files.Count; i++)
        {
            var f = files[i];
            rows[i] = new ShotRow
            {
                Path = f.FullName,
                Name = f.Name,
                LastWrite = f.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"),
                LastWriteUtc = f.LastWriteTimeUtc.ToString("o")
            };
        }

        return JsonSerializer.Serialize(rows, JsonOpts);
    }

    private sealed class ShotRow
    {
        [JsonPropertyName("path")]
        public string Path { get; set; } = "";

        [JsonPropertyName("name")]
        public string Name { get; set; } = "";

        [JsonPropertyName("lastWrite")]
        public string LastWrite { get; set; } = "";

        [JsonPropertyName("lastWriteUtc")]
        public string LastWriteUtc { get; set; } = "";
    }
}
