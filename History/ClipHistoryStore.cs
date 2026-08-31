using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows.Threading;
using Sideclip.Settings;

namespace Sideclip.History;

/// <summary>
/// In-memory history. Secrets are never persisted. TTL wipes one secret row
/// and optionally clears the live clipboard if that row is still latest.
/// Picker sees only non-secrets. TTL Off leaves secrets in memory until
/// expiry is turned back on.
/// </summary>
public sealed class ClipHistoryStore : IDisposable
{
    public const int DefaultTtlSeconds = 12;
    public const int MaxEntries = 500;

    private readonly Dispatcher _dispatcher;
    private readonly Action<ClipEntry> _onSecretExpired;
    private readonly List<ClipEntry> _entries = new();
    private readonly List<DispatcherTimer> _timers = new();
    private TimeSpan _ttl;
    private bool _ttlEnabled;

    public ObservableCollection<ClipEntryView> Rows { get; } = new();

    public TimeSpan Ttl => _ttl;
    public bool TtlEnabled => _ttlEnabled;

    public ClipEntry? Latest { get; private set; }

    public ClipHistoryStore(
        Dispatcher dispatcher,
        Action<ClipEntry> onSecretExpired,
        TimeSpan? ttl = null,
        bool ttlEnabled = true)
    {
        _dispatcher = dispatcher;
        _ttl = ttl ?? TimeSpan.FromSeconds(DefaultTtlSeconds);
        _ttlEnabled = ttlEnabled;
        _onSecretExpired = onSecretExpired;
        LoadNonSecrets();
    }

    public void SetTtl(TimeSpan ttl, bool enabled)
    {
        if (ttl.TotalSeconds < 3)
            ttl = TimeSpan.FromSeconds(3);
        _ttl = ttl;

        var turningOn = enabled && !_ttlEnabled;
        _ttlEnabled = enabled;

        if (!enabled)
        {
            foreach (var timer in _timers)
                timer.Stop();
            _timers.Clear();
            foreach (var e in _entries)
            {
                if (e.IsSecret)
                    e.ClearSecretFlag();
            }
            PersistNonSecrets();
            return;
        }

        if (turningOn)
        {
            foreach (var e in _entries)
            {
                if (e.IsSecret)
                    StartSecretTimer(e);
            }
        }
    }

    public IEnumerable<ClipEntry> PickerEntries()
    {
        foreach (var e in _entries)
        {
            if (!e.IsSecret && e.HasPayload)
                yield return e;
        }
    }

    public ClipEntry Add(string text, bool isSecret, string ownerExe, byte[]? imagePng = null)
    {
        var entry = new ClipEntry(text, isSecret, ownerExe, imagePng);
        Latest = entry;
        _entries.Insert(0, entry);
        Trim();

        var preview = isSecret
            ? string.Empty
            : entry.IsImage
                ? ImageCaption(entry)
                : ClipEntry.MakeNonSecretPreview(text);
        Rows.Insert(0, new ClipEntryView(entry, () => _ttl, () => _ttlEnabled, preview));

        if (isSecret)
        {
            if (_ttlEnabled)
                StartSecretTimer(entry);
        }
        else
        {
            PersistNonSecrets();
        }

        return entry;
    }

    public void RemoveEntry(ClipEntry entry)
    {
        _entries.Remove(entry);
        for (var i = Rows.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(Rows[i].Entry, entry))
                Rows.RemoveAt(i);
        }

        if (ReferenceEquals(Latest, entry))
            Latest = _entries.Count > 0 ? _entries[0] : null;

        entry.WipePayload();
        if (!entry.IsSecret)
            PersistNonSecrets();
    }

    public void ClearNonSecrets()
    {
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            if (!_entries[i].IsSecret)
            {
                var e = _entries[i];
                _entries.RemoveAt(i);
                e.WipePayload();
            }
        }

        for (var i = Rows.Count - 1; i >= 0; i--)
        {
            if (!Rows[i].Entry.IsSecret)
                Rows.RemoveAt(i);
        }

        PersistNonSecrets();
    }

    public void ClearAll()
    {
        foreach (var timer in _timers)
            timer.Stop();
        _timers.Clear();

        foreach (var e in _entries)
            e.WipePayload();
        _entries.Clear();
        Rows.Clear();
        Latest = null;
        PersistNonSecrets();
    }

    private void StartSecretTimer(ClipEntry entry)
    {
        if (!_ttlEnabled)
            return;

        var timer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher)
        {
            Interval = _ttl
        };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _timers.Remove(timer);
            ExpireSecret(entry);
        };
        _timers.Add(timer);
        timer.Start();
    }

    private void ExpireSecret(ClipEntry entry)
    {
        _onSecretExpired(entry);
        RemoveEntry(entry);
    }

    public void TickViews()
    {
        foreach (var row in Rows)
            row.Tick();
    }

    private void Trim()
    {
        while (_entries.Count > MaxEntries)
        {
            var last = _entries[^1];
            if (last.IsSecret)
                break;
            RemoveEntry(last);
        }
    }

    private static string ImageCaption(ClipEntry entry)
    {
        var local = entry.CopiedAt.ToLocalTime();
        return $"Screenshot from {local:dddd HH:mm}";
    }

    private void PersistNonSecrets()
    {
        try
        {
            Directory.CreateDirectory(SettingsManager.Dir);
            Directory.CreateDirectory(SettingsManager.ImagesDir);

            var list = new List<PersistedClip>();
            var keepImages = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var e in _entries)
            {
                if (e.IsSecret)
                    continue;

                var rec = new PersistedClip
                {
                    Owner = e.OwnerExe,
                    CopiedAt = e.CopiedAt
                };

                if (e.IsImage && e.ImagePng is not null)
                {
                    var name = e.CopiedAt.ToString("yyyyMMddHHmmssfff") + ".png";
                    var path = Path.Combine(SettingsManager.ImagesDir, name);
                    if (!File.Exists(path))
                        File.WriteAllBytes(path, e.ImagePng);
                    rec.ImageFile = name;
                    keepImages.Add(name);
                }
                else
                {
                    rec.Text = e.TryGetNonSecretText();
                    if (string.IsNullOrEmpty(rec.Text))
                        continue;
                }

                list.Add(rec);
            }

            File.WriteAllText(
                SettingsManager.HistoryPath,
                JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));

            if (Directory.Exists(SettingsManager.ImagesDir))
            {
                foreach (var file in Directory.GetFiles(SettingsManager.ImagesDir, "*.png"))
                {
                    if (!keepImages.Contains(Path.GetFileName(file)))
                    {
                        try { File.Delete(file); } catch { }
                    }
                }
            }
        }
        catch
        {
        }
    }

    private void LoadNonSecrets()
    {
        try
        {
            if (!File.Exists(SettingsManager.HistoryPath))
                return;

            var json = File.ReadAllText(SettingsManager.HistoryPath);
            var list = JsonSerializer.Deserialize<List<PersistedClip>>(json);
            if (list is null)
                return;

            foreach (var rec in list)
            {
                byte[]? png = null;
                var text = rec.Text ?? string.Empty;
                if (!string.IsNullOrEmpty(rec.ImageFile))
                {
                    var path = Path.Combine(SettingsManager.ImagesDir, rec.ImageFile);
                    if (File.Exists(path))
                        png = File.ReadAllBytes(path);
                }

                if (string.IsNullOrEmpty(text) && png is null)
                    continue;

                var entry = new ClipEntry(text, isSecret: false, rec.Owner ?? "unknown", png);
                _entries.Add(entry);
                var preview = entry.IsImage ? ImageCaption(entry) : ClipEntry.MakeNonSecretPreview(text);
                Rows.Add(new ClipEntryView(entry, () => _ttl, () => _ttlEnabled, preview));
            }

            Latest = _entries.Count > 0 ? _entries[0] : null;
        }
        catch
        {
        }
    }

    public void Dispose()
    {
        foreach (var timer in _timers)
            timer.Stop();
        _timers.Clear();

        foreach (var entry in _entries)
            entry.WipePayload();
        _entries.Clear();
        Rows.Clear();
        Latest = null;
    }
}

public sealed class ClipEntryView : INotifyPropertyChanged
{
    private readonly Func<TimeSpan> _ttl;
    private readonly Func<bool> _ttlEnabled;
    private readonly string _nonSecretPreview;

    public ClipEntry Entry { get; }

    public ClipEntryView(ClipEntry entry, Func<TimeSpan> ttl, Func<bool> ttlEnabled, string nonSecretPreview)
    {
        Entry = entry;
        _ttl = ttl;
        _ttlEnabled = ttlEnabled;
        _nonSecretPreview = nonSecretPreview;
    }

    public string Age
    {
        get
        {
            var s = Math.Max(0, (int)(DateTime.UtcNow - Entry.CopiedAt).TotalSeconds);
            return s + "s";
        }
    }

    public string Secret => Entry.IsSecret ? "yes" : "no";

    public string Owner => Entry.OwnerExe;

    public string Preview
    {
        get
        {
            if (!Entry.IsSecret)
                return _nonSecretPreview;

            if (!_ttlEnabled())
                return "secret";

            var remain = Math.Max(0, (int)(_ttl() - (DateTime.UtcNow - Entry.CopiedAt)).TotalSeconds);
            return remain > 0 ? $"secret ({remain}s)" : "...";
        }
    }

    public event PropertyChangedEventHandler? PropertyChanged;

    public void Tick()
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Age)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Preview)));
    }
}
