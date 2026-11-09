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
/// Picker includes live secrets; wiped/expired secrets are never eligible.
/// TTL Off leaves live secrets in memory until expiry is turned on.
/// </summary>
public sealed class ClipHistoryStore : IDisposable
{
    public const int DefaultTtlSeconds = 12;
    public const int MaxEntries = 500;

    private readonly Dispatcher _dispatcher;
    private readonly Action<ClipEntry> _onSecretExpired;
    private readonly List<ClipEntry> _entries = new();
    private readonly Dictionary<ClipEntry, DispatcherTimer> _timers = new();
    private readonly Dictionary<ClipEntry, DateTime> _expires = new();
    private TimeSpan _ttl;
    private bool _ttlEnabled;

    public ObservableCollection<ClipEntryView> Rows { get; } = new();

    public event Action<ClipEntry>? EntryInvalidated;

    // Same events as EntryInvalidated, with the reason, so status text can say
    // "expired" instead of a generic cancel.
    public event Action<ClipEntry, EntryInvalidationReason>? EntryInvalidatedWithReason;
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
        if (ttl.TotalSeconds < 3) ttl = TimeSpan.FromSeconds(3);
        var now = DateTime.UtcNow;
        ClipEntry[] elapsed;
        lock (_entries)
            elapsed = _expires.Where(pair => pair.Value <= now).Select(pair => pair.Key).ToArray();
        // Even turning TTL off cannot resurrect a source whose old deadline elapsed.
        foreach (var entry in elapsed) ExpireSecret(entry);
        foreach (var timer in _timers.Values) timer.Stop();
        _timers.Clear();
        ClipEntry[] secrets;
        lock (_entries)
        {
            var turningOn = enabled && !_ttlEnabled;
            _ttl = ttl;
            _ttlEnabled = enabled;
            if (!enabled) _expires.Clear();
            secrets = _entries.Where(entry => entry.IsSecret).ToArray();
            if (enabled)
                foreach (var entry in secrets)
                {
                    var next = now + ttl;
                    if (turningOn || !_expires.TryGetValue(entry, out var old)) _expires[entry] = next;
                    else _expires[entry] = old < next ? old : next;
                }
        }
        if (enabled) foreach (var entry in secrets) StartSecretTimer(entry);
        PersistNonSecrets();
    }
    public bool IsPickerSource(ClipEntry entry)
    {
        lock (_entries) return _entries.Contains(entry, ReferenceEqualityComparer.Instance);
    }

    // Independent of dispatcher timer delivery: a delayed UI tick cannot keep
    // an already elapsed secret eligible for a trigger or another input chunk.
    public bool IsTypeOutSource(ClipEntry entry)
    {
        lock (_entries)
            return _entries.Contains(entry, ReferenceEqualityComparer.Instance) && entry.HasPayload
                && (!entry.IsSecret || !_ttlEnabled || _expires.TryGetValue(entry, out var deadline) && DateTime.UtcNow < deadline);
    }

    public IEnumerable<ClipEntry> PickerEntries()
    {
        lock (_entries) return _entries.Where(entry => entry.HasPayload).ToArray();
    }
    public ClipEntry Add(string text, bool isSecret, string ownerExe, byte[]? imagePng = null)
    {
        var entry = new ClipEntry(text, isSecret, ownerExe, imagePng);
        Latest = entry;
        lock (_entries)
        {
            _entries.Insert(0, entry);
            if (isSecret && _ttlEnabled) _expires[entry] = entry.CopiedAt + _ttl;
        }
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

    public void RemoveEntry(ClipEntry entry) => RemoveEntry(entry, EntryInvalidationReason.Deleted);

    private void RemoveEntry(ClipEntry entry, EntryInvalidationReason reason)
    {
        lock (_entries)
        {
            if (!_entries.Remove(entry)) return;
            _expires.Remove(entry);
        }
        if (_timers.Remove(entry, out var timer)) timer.Stop();
        for (var i = Rows.Count - 1; i >= 0; i--)
        {
            if (ReferenceEquals(Rows[i].Entry, entry))
                Rows.RemoveAt(i);
        }

        if (ReferenceEquals(Latest, entry))
            Latest = _entries.Count > 0 ? _entries[0] : null;

        entry.WipePayload();
        Invalidated(entry, reason);
        if (!entry.IsSecret)
            PersistNonSecrets();
    }

    public void ClearNonSecrets()
    {
        ClipEntry[] entries;
        lock (_entries) entries = _entries.Where(entry => !entry.IsSecret).ToArray();
        foreach (var entry in entries) RemoveEntry(entry, EntryInvalidationReason.Cleared);
    }

    public void ClearAll()
    {
        ClipEntry[] invalidated;
        foreach (var timer in _timers.Values) timer.Stop();
        _timers.Clear();
        lock (_entries)
        {
            invalidated = _entries.ToArray();
            _entries.Clear();
            _expires.Clear();
        }
        foreach (var entry in invalidated) entry.WipePayload();
        Rows.Clear();
        Latest = null;
        foreach (var entry in invalidated) Invalidated(entry, EntryInvalidationReason.Cleared);
        PersistNonSecrets();
    }

    private void StartSecretTimer(ClipEntry entry)
    {
        DateTime deadline;
        lock (_entries)
            if (!_ttlEnabled || !_expires.TryGetValue(entry, out deadline)) return;
        if (_timers.Remove(entry, out var old)) old.Stop();
        var remaining = deadline - DateTime.UtcNow;
        if (remaining <= TimeSpan.Zero) { ExpireSecret(entry); return; }
        var timer = new DispatcherTimer(DispatcherPriority.Normal, _dispatcher) { Interval = remaining };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            _timers.Remove(entry);
            // Recheck deadline if Windows delivers a timer tick early.
            DateTime current;
            lock (_entries)
                if (!_expires.TryGetValue(entry, out current)) return;
            if (DateTime.UtcNow < current) StartSecretTimer(entry);
            else ExpireSecret(entry);
        };
        _timers[entry] = timer;
        timer.Start();
    }
    private void ExpireSecret(ClipEntry entry)
    {
        _onSecretExpired(entry);
        RemoveEntry(entry, EntryInvalidationReason.Expired);
    }

    private void Invalidated(ClipEntry entry, EntryInvalidationReason reason)
    {
        EntryInvalidated?.Invoke(entry);
        EntryInvalidatedWithReason?.Invoke(entry, reason);
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
                lock (_entries) _entries.Add(entry);
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
        foreach (var timer in _timers.Values)
            timer.Stop();
        _timers.Clear();

        foreach (var entry in _entries)
            entry.WipePayload();
        lock (_entries) { _entries.Clear(); _expires.Clear(); }
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

public enum EntryInvalidationReason { Deleted, Cleared, Expired }
