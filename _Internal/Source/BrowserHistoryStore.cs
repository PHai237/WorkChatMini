using System.IO;
using System.Text.Json;

namespace WorkChatMini;

internal sealed class BrowserHistoryEntry
{
    public string Url { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public DateTimeOffset LastVisitedUtc { get; set; } = DateTimeOffset.UtcNow;
}

internal sealed class BrowserHistoryStore : IDisposable
{
    private const int MaxEntries = 400;
    private readonly object _gate = new();
    private readonly string _path;
    private readonly System.Threading.Timer _saveTimer;
    private readonly List<BrowserHistoryEntry> _entries;
    private bool _dirty;
    private bool _disposed;
    private readonly bool _preserveDamagedFile;

    private BrowserHistoryStore(string path, List<BrowserHistoryEntry> entries, bool preserveDamagedFile = false)
    {
        _path = path;
        _entries = entries;
        _preserveDamagedFile = preserveDamagedFile;
        _saveTimer = new System.Threading.Timer(_ => FlushFromTimer(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public static BrowserHistoryStore Load() => LoadFromPath(
        Path.Combine(AppRuntime.DataDirectory, "browser-history.json"));

    private static BrowserHistoryStore LoadFromPath(string path)
    {
        if (!File.Exists(path)) return new BrowserHistoryStore(path, new List<BrowserHistoryEntry>());
        try
        {
            string json = File.ReadAllText(path);
            List<BrowserHistoryEntry> loaded = JsonSerializer.Deserialize<List<BrowserHistoryEntry>>(json)
                ?? throw new InvalidDataException("Empty history document.");
            List<BrowserHistoryEntry> clean = loaded
                .Where(entry => entry is not null && IsWebUrl(entry.Url) &&
                                AdBlockNuisanceGuard.IsSafeToPersist(entry.Url))
                .OrderByDescending(entry => entry.LastVisitedUtc)
                .GroupBy(entry => entry.Url, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Take(MaxEntries)
                .ToList();
            return new BrowserHistoryStore(path, clean);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            // Quarantine a bad JSON file BEFORE accepting new history writes.
            // Never overwrite or delete the only remaining copy on a failed move.
            string backup = path + ".corrupt-" + DateTimeOffset.UtcNow.ToString("yyyyMMddHHmmssfff") +
                "-" + Guid.NewGuid().ToString("N") + ".bak";
            try
            {
                File.Move(path, backup);
                AppLogger.Warn("BROWSER", $"Damaged history preserved as {Path.GetFileName(backup)}: {ex.Message}");
                return new BrowserHistoryStore(path, new List<BrowserHistoryEntry>());
            }
            catch (Exception backupError)
            {
                AppLogger.Warn("BROWSER", $"History is read-only until damaged file is recovered: {backupError.Message}");
                return new BrowserHistoryStore(path, new List<BrowserHistoryEntry>(), preserveDamagedFile: true);
            }
        }
        catch (Exception ex)
        {
            // An IO/read permission failure is not proof that the file is corrupt.
            // Do not rename it or allow a subsequent write over it.
            AppLogger.Warn("BROWSER", $"History could not be loaded; writes disabled: {ex.Message}");
            return new BrowserHistoryStore(path, new List<BrowserHistoryEntry>(), preserveDamagedFile: true);
        }
    }

    public void Record(string? url, string? title)
    {
        if (!IsWebUrl(url) || !AdBlockNuisanceGuard.IsSafeToPersist(url))
            return;

        string normalizedUrl = url!.Trim();
        string normalizedTitle = string.IsNullOrWhiteSpace(title) ? HostFromUrl(normalizedUrl) : title.Trim();
        lock (_gate)
        {
            BrowserHistoryEntry? existing = _entries.FirstOrDefault(entry =>
                string.Equals(entry.Url, normalizedUrl, StringComparison.OrdinalIgnoreCase));
            if (existing is null)
            {
                existing = new BrowserHistoryEntry { Url = normalizedUrl };
                _entries.Add(existing);
            }
            existing.Title = normalizedTitle;
            existing.LastVisitedUtc = DateTimeOffset.UtcNow;

            _entries.Sort((left, right) => right.LastVisitedUtc.CompareTo(left.LastVisitedUtc));
            if (_entries.Count > MaxEntries)
                _entries.RemoveRange(MaxEntries, _entries.Count - MaxEntries);
            MarkDirty_NoLock();
        }
    }

    // A modern web app may change document.title repeatedly (unread counts, playback,
    // editing status). Refresh its autocomplete label without changing the last-visit
    // time or scheduling another disk write when the label is unchanged. Never create
    // a visit for a title-change event on a still-loading or unvisited page.
    public bool UpdateTitle(string? url, string? title)
    {
        if (!IsWebUrl(url) || !AdBlockNuisanceGuard.IsSafeToPersist(url) ||
            string.IsNullOrWhiteSpace(title))
            return false;

        string cleanTitle = title.Trim();
        lock (_gate)
        {
            BrowserHistoryEntry? existing = _entries.FirstOrDefault(entry =>
                string.Equals(entry.Url, url!.Trim(), StringComparison.OrdinalIgnoreCase));
            if (existing is null || string.Equals(existing.Title, cleanTitle, StringComparison.Ordinal))
                return false;

            existing.Title = cleanTitle;
            MarkDirty_NoLock();
            return true;
        }
    }

    // Runs on the CI Self-Test runner against its own temporary history, never the
    // user's real history or WebView2 profile.
    internal static void VerifyTitleUpdates()
    {
        string path = Path.Combine(Path.GetTempPath(), "WorkChatHistoryTest-" + Guid.NewGuid().ToString("N") + ".json");
        try
        {
            using (var history = new BrowserHistoryStore(path, new List<BrowserHistoryEntry>()))
            {
                const string url = "https://example.com/reading";
                if (history.UpdateTitle(url, "Not visited"))
                    throw new InvalidOperationException("Title changes must not create history visits.");
                history.Record(url, "Original title");
                BrowserHistoryEntry original = history.Search("reading").Single();
                if (!history.UpdateTitle(url, "Updated title") ||
                    history.UpdateTitle(url, "Updated title") ||
                    history.UpdateTitle(url, "   "))
                    throw new InvalidOperationException("Duplicate or blank title updates were persisted.");
                BrowserHistoryEntry updated = history.Search("reading").Single();
                if (updated.LastVisitedUtc != original.LastVisitedUtc ||
                    updated.Title != "Updated title" ||
                    history.Search("Updated title").Count != 1)
                    throw new InvalidOperationException("Title refresh modified visit order or broke autocomplete.");
                history.Flush();
            }
            using var reloaded = LoadFromPathForTest(path);
            if (reloaded.Search("Updated title").Single().Title != "Updated title")
                throw new InvalidOperationException("Updated history title was not persisted.");
        }
        finally
        {
            try { File.Delete(path); } catch { }
            try { File.Delete(path + ".tmp"); } catch { }
        }
    }

    private static BrowserHistoryStore LoadFromPathForTest(string path) => LoadFromPath(path);

    internal static void VerifyCorruptFileProtection()
    {
        string directory = Path.Combine(Path.GetTempPath(), "WorkChatHistoryGuard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "browser-history.json");
        const string damaged = "{not-json-data";
        try
        {
            File.WriteAllText(path, damaged);
            using (BrowserHistoryStore history = LoadFromPath(path))
            {
                history.Record("https://example.com/after-recovery", "Recovered history");
                history.Flush();
            }
            string[] backups = Directory.GetFiles(directory, "browser-history.json.corrupt-*.bak");
            if (backups.Length != 1 || File.ReadAllText(backups[0]) != damaged ||
                !File.Exists(path))
                throw new InvalidOperationException("Corrupt browser history was not backed up before writing.");
            using var reloaded = LoadFromPath(path);
            if (reloaded.Search("after-recovery").Count != 1)
                throw new InvalidOperationException("History did not recover after damaged-file quarantine.");
        }
        finally { try { Directory.Delete(directory, recursive: true); } catch { } }
    }

    public IReadOnlyList<BrowserHistoryEntry> Search(string? query, int maxResults = 6)
    {
        string text = query?.Trim() ?? string.Empty;
        if (text.Length == 0)
            return Array.Empty<BrowserHistoryEntry>();
        int limit = Math.Clamp(maxResults, 1, 12);

        lock (_gate)
        {
            return _entries
                .Select(entry => new { Entry = entry, Rank = Rank(entry, text) })
                .Where(item => item.Rank < int.MaxValue)
                .OrderBy(item => item.Rank)
                .ThenByDescending(item => item.Entry.LastVisitedUtc)
                .Take(limit)
                .Select(item => new BrowserHistoryEntry
                {
                    Url = item.Entry.Url,
                    Title = item.Entry.Title,
                    LastVisitedUtc = item.Entry.LastVisitedUtc
                })
                .ToArray();
        }
    }

    public void Clear()
    {
        lock (_gate)
        {
            _entries.Clear();
            MarkDirty_NoLock(immediate: true);
        }
    }

    private static int Rank(BrowserHistoryEntry entry, string query)
    {
        string host = HostFromUrl(entry.Url);
        if (host.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 0;
        if (entry.Title.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 1;
        if (entry.Url.StartsWith(query, StringComparison.OrdinalIgnoreCase)) return 2;
        if (host.Contains(query, StringComparison.OrdinalIgnoreCase)) return 3;
        if (entry.Title.Contains(query, StringComparison.OrdinalIgnoreCase)) return 4;
        if (entry.Url.Contains(query, StringComparison.OrdinalIgnoreCase)) return 5;
        return int.MaxValue;
    }

    private void MarkDirty_NoLock(bool immediate = false)
    {
        _dirty = true;
        if (_disposed)
            return;
        _saveTimer.Change(immediate ? 20 : 1200, Timeout.Infinite);
    }

    private void FlushFromTimer()
    {
        try { Flush(); }
        catch { }
    }

    public void Flush()
    {
        BrowserHistoryEntry[] snapshot;
        lock (_gate)
        {
            if (!_dirty || _preserveDamagedFile)
                return;
            snapshot = _entries.Select(entry => new BrowserHistoryEntry
            {
                Url = entry.Url,
                Title = entry.Title,
                LastVisitedUtc = entry.LastVisitedUtc
            }).ToArray();
            _dirty = false;
        }

        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path) ?? AppRuntime.DataDirectory);
            string tempPath = _path + ".tmp";
            string json = JsonSerializer.Serialize(snapshot, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, _path, overwrite: true);
        }
        catch
        {
            lock (_gate)
                _dirty = true;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
                return;
            _disposed = true;
        }
        try { _saveTimer.Dispose(); } catch { }
        try { Flush(); } catch { }
    }

    private static bool IsWebUrl(string? value) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static string HostFromUrl(string value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri))
            return value;
        return uri.Host.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
