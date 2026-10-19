using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Windows.Threading;
using Sideclip.Settings;

namespace Sideclip.History;

/// <summary>
/// In-memory history. Secrets are never persisted. TTL wipes one secret row
/// and optionally clears the live clipboard if that row is still latest.
/// Picker sees only non-secrets. TTL Off leaves secrets in memory until
/// expiry is turned back on.
///
/// Size limits: separate caps for non-pinned text clips and non-pinned image
/// clips (0 = Unlimited). There is exactly one enforcement path,
/// <see cref="EnforceCap"/>. It runs from two places only:
///  1. Add: after inserting a new clip, remove at most ONE oldest clip of the
///     same kind (so a store that is somehow far over its cap is never
///     bulk-trimmed by a copy).
///  2. ApplyConfirmedLowering: after the user confirmed an exact count in
///     Settings, remove at most that many.
/// Everything else (startup, raising a cap, Unlimited) removes nothing.
/// Not thread-safe: call from the UI thread only (as the clipboard watcher
/// and picker already do).
/// </summary>
public sealed class ClipHistoryStore : IDisposable
{
    public const int DefaultTtlSeconds = 12;

    // App-generated image names only: yyyyMMddHHmmssfff, optional -N suffix.
    private static readonly Regex AppImageName = new(@"^\d{17}(-\d+)?\.png$", RegexOptions.CultureInvariant);

    private readonly Dispatcher _dispatcher;
    private readonly Action<ClipEntry> _onSecretExpired;
    private readonly List<ClipEntry> _entries = new();
    private readonly List<DispatcherTimer> _timers = new();
    private TimeSpan _ttl;
    private bool _ttlEnabled;
    private int _maxText;
    private int _maxImages;
    private bool _loadFailed;

    public ObservableCollection<ClipEntryView> Rows { get; } = new();

    public TimeSpan Ttl => _ttl;
    public bool TtlEnabled => _ttlEnabled;

    public ClipEntry? Latest { get; private set; }

    public ClipHistoryStore(
        Dispatcher dispatcher,
        Action<ClipEntry> onSecretExpired,
        TimeSpan? ttl = null,
        bool ttlEnabled = true,
        int maxTextClips = ClipCaps.Unlimited,
        int maxImageClips = ClipCaps.Unlimited)
    {
        _dispatcher = dispatcher;
        _maxText = maxTextClips;
        _maxImages = maxImageClips;
        _ttl = ttl ?? TimeSpan.FromSeconds(DefaultTtlSeconds);
        _ttlEnabled = ttlEnabled;
        _onSecretExpired = onSecretExpired;
        LoadNonSecrets();
    }

    public int MaxTextClips => _maxText;
    public int MaxImageClips => _maxImages;

    /// <summary>
    /// Sets the caps. Never removes anything by itself, whatever the values.
    /// </summary>
    public void SetCaps(int maxTextClips, int maxImageClips)
    {
        _maxText = maxTextClips;
        _maxImages = maxImageClips;
    }

    /// <summary>Stored clips of this kind that count toward its cap (non-pinned, non-secret).</summary>
    public int CountCapped(ClipKind kind) => ClipCaps.CountOf(_entries, kind);

    /// <summary>
    /// Sets both caps, then removes oldest-first, at most the counts the user
    /// confirmed. If the store holds fewer excess clips than confirmed (a copy
    /// raced the dialog) it removes only the actual excess; it never removes
    /// more than confirmed.
    /// </summary>
    public CapRemoval ApplyConfirmedLowering(
        int maxTextClips,
        int maxImageClips,
        int confirmedTextRemovals,
        int confirmedImageRemovals)
    {
        SetCaps(maxTextClips, maxImageClips);
        var text = EnforceCap(ClipKind.Text, confirmedTextRemovals);
        var images = EnforceCap(ClipKind.Image, confirmedImageRemovals);
        if (text + images > 0)
            PersistNonSecrets();
        return new CapRemoval(text, images);
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
        if (!isSecret)
            EnforceCap(ClipCaps.KindOf(entry), maxRemovals: 1);

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

        // Removed clips' image files are deleted by the PersistNonSecrets above.
        return entry;
    }

    public void RemoveEntry(ClipEntry entry)
    {
        RemoveCore(entry);
        if (!entry.IsSecret)
            PersistNonSecrets();
    }

    private void RemoveCore(ClipEntry entry)
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
    }

    /// <summary>
    /// Removes every stored (non-secret) clip EXCEPT pinned ones. Image files
    /// are deleted only for the clips removed here (PersistNonSecrets sweeps
    /// files not referenced by a surviving clip).
    /// </summary>
    public void ClearNonSecrets()
    {
        RemoveUnpinnedNonSecretsNoPersist();
        PersistNonSecrets();
    }

    /// <summary>
    /// The user-facing "Clear history" (tray menu, picker Clear button and
    /// Shift+Delete): removes all secret clips (and their timers) and every
    /// non-secret clip that is not pinned. Pinned clips are never removed.
    /// </summary>
    public void ClearHistory()
    {
        // Secrets are memory-only and short-lived; Clear history always wipes
        // them (a pinned secret is not a supported state).
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            if (_entries[i].IsSecret)
            {
                var e = _entries[i];
                _entries.RemoveAt(i);
                e.WipePayload();
            }
        }

        for (var i = Rows.Count - 1; i >= 0; i--)
        {
            if (Rows[i].Entry.IsSecret)
                Rows.RemoveAt(i);
        }

        foreach (var timer in _timers)
            timer.Stop();
        _timers.Clear();

        RemoveUnpinnedNonSecretsNoPersist();
        Latest = _entries.Count > 0 ? _entries[0] : null;
        PersistNonSecrets();
    }

    private void RemoveUnpinnedNonSecretsNoPersist()
    {
        for (var i = _entries.Count - 1; i >= 0; i--)
        {
            var e = _entries[i];
            if (!e.IsSecret && !e.IsPinned)
            {
                _entries.RemoveAt(i);
                e.WipePayload();
            }
        }

        for (var i = Rows.Count - 1; i >= 0; i--)
        {
            var e = Rows[i].Entry;
            if (!e.IsSecret && !e.IsPinned)
                Rows.RemoveAt(i);
        }
    }

    /// <summary>
    /// EXPLICIT "delete everything": removes every clip including pinned ones.
    /// Nothing in the UI calls this; the user-facing Clear history is
    /// <see cref="ClearHistory"/>, which spares pinned clips.
    /// </summary>
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

    /// <summary>
    /// THE enforcement path. Removes up to <paramref name="maxRemovals"/> of the
    /// oldest non-pinned clips of <paramref name="kind"/> while that kind is
    /// over its cap. Updates the in-memory lists only; the caller persists
    /// (which also deletes the removed clips' image files).
    /// </summary>
    private int EnforceCap(ClipKind kind, int maxRemovals)
    {
        var cap = kind == ClipKind.Image ? _maxImages : _maxText;
        var victims = ClipCaps.SelectOldestToRemove(_entries, kind, cap, maxRemovals);
        foreach (var v in victims)
            RemoveCore(v);
        return victims.Count;
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

            // Names already claimed by entries that were loaded with one.
            foreach (var e in _entries)
            {
                if (!e.IsSecret && e.IsImage && IsAppImageName(e.ImageFileName))
                    keepImages.Add(e.ImageFileName!);
            }

            foreach (var e in _entries)
            {
                if (e.IsSecret)
                    continue;

                var rec = new PersistedClip
                {
                    Owner = e.OwnerExe,
                    CopiedAt = e.CopiedAt,
                    Pinned = e.IsPinned
                };

                if (e.IsImage && e.ImagePng is not null)
                {
                    var name = EnsureImageFileName(e, keepImages);
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

            // Write-then-replace so a crash mid-write cannot leave a truncated
            // history.json (which would look like "no history" on next start).
            var tmp = SettingsManager.HistoryPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(list, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(tmp, SettingsManager.HistoryPath, overwrite: true);

            // Only after history.json is safely written, and never when this
            // session failed to read the existing history (it may still
            // reference files we know nothing about).
            if (!_loadFailed)
                SweepOrphanImages(keepImages);
        }
        catch
        {
        }
    }

    private static bool IsAppImageName(string? name) =>
        !string.IsNullOrEmpty(name) && AppImageName.IsMatch(name);

    /// <summary>
    /// Each image clip owns one file. The name is assigned once and never
    /// collides with another clip's file or an existing file.
    /// </summary>
    private static string EnsureImageFileName(ClipEntry e, HashSet<string> claimed)
    {
        if (IsAppImageName(e.ImageFileName))
            return e.ImageFileName!;

        var stem = e.CopiedAt.ToString("yyyyMMddHHmmssfff", System.Globalization.CultureInfo.InvariantCulture);
        var candidate = stem + ".png";
        var n = 1;
        while (claimed.Contains(candidate) || File.Exists(Path.Combine(SettingsManager.ImagesDir, candidate)))
            candidate = stem + "-" + n++ + ".png";
        e.ImageFileName = candidate;
        return candidate;
    }

    /// <summary>
    /// Deletes image files no stored clip references. Guarded: only direct
    /// children of the app's images folder, only app-generated names, never
    /// links. A file that cannot be deleted (locked, missing) is skipped and
    /// retried on the next save. Never throws.
    /// </summary>
    private static void SweepOrphanImages(HashSet<string> keep)
    {
        try
        {
            var dir = Path.GetFullPath(SettingsManager.ImagesDir);
            if (!Directory.Exists(dir))
                return;

            var prefix = dir.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            foreach (var file in Directory.EnumerateFiles(dir, "*.png"))
            {
                try
                {
                    var name = Path.GetFileName(file);
                    if (keep.Contains(name) || !IsAppImageName(name))
                        continue;

                    var full = Path.GetFullPath(file);
                    if (!full.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
                        || !string.Equals(Path.GetDirectoryName(full), dir.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase))
                        continue;

                    if ((File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
                        continue;

                    File.Delete(full);
                }
                catch
                {
                    // locked or already gone; the next save retries
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
                string? imageName = null;
                var text = rec.Text ?? string.Empty;
                if (!string.IsNullOrEmpty(rec.ImageFile) && IsAppImageName(rec.ImageFile))
                {
                    var path = Path.Combine(SettingsManager.ImagesDir, rec.ImageFile);
                    if (File.Exists(path))
                    {
                        png = File.ReadAllBytes(path);
                        imageName = rec.ImageFile;
                    }
                }

                if (string.IsNullOrEmpty(text) && png is null)
                    continue;

                var copiedAt = rec.CopiedAt == default
                    ? DateTime.UtcNow
                    : rec.CopiedAt.Kind == DateTimeKind.Unspecified
                        ? DateTime.SpecifyKind(rec.CopiedAt, DateTimeKind.Utc)
                        : rec.CopiedAt.ToUniversalTime();
                var entry = new ClipEntry(text, isSecret: false, rec.Owner ?? "unknown", png, copiedAt)
                {
                    IsPinned = rec.Pinned,
                    ImageFileName = imageName
                };
                _entries.Add(entry);
                var preview = entry.IsImage ? ImageCaption(entry) : ClipEntry.MakeNonSecretPreview(text);
                Rows.Add(new ClipEntryView(entry, () => _ttl, () => _ttlEnabled, preview));
            }

            Latest = _entries.Count > 0 ? _entries[0] : null;
        }
        catch
        {
            // Unreadable history: keep a copy of the file, and do not delete
            // any image files this session.
            _loadFailed = true;
            try
            {
                var bak = SettingsManager.HistoryPath + ".corrupt.bak";
                if (!File.Exists(bak) && File.Exists(SettingsManager.HistoryPath))
                    File.Copy(SettingsManager.HistoryPath, bak);
            }
            catch
            {
            }
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
