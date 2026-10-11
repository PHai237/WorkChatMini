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

    private BrowserHistoryStore(string path, List<BrowserHistoryEntry> entries)
    {
        _path = path;
        _entries = entries;
        _saveTimer = new System.Threading.Timer(_ => FlushFromTimer(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public static BrowserHistoryStore Load()
    {
        string path = Path.Combine(AppRuntime.DataDirectory, "browser-history.json");
        try
        {
            if (!File.Exists(path))
                return new BrowserHistoryStore(path, new List<BrowserHistoryEntry>());

            string json = File.ReadAllText(path);
            List<BrowserHistoryEntry>? loaded = JsonSerializer.Deserialize<List<BrowserHistoryEntry>>(json);
            List<BrowserHistoryEntry> clean = (loaded ?? new List<BrowserHistoryEntry>())
                .Where(entry => IsWebUrl(entry.Url) && AdBlockNuisanceGuard.IsSafeToPersist(entry.Url))
                .OrderByDescending(entry => entry.LastVisitedUtc)
                .GroupBy(entry => entry.Url, StringComparer.OrdinalIgnoreCase)
                .Select(group => group.First())
                .Take(MaxEntries)
                .ToList();
            return new BrowserHistoryStore(path, clean);
        }
        catch
        {
            return new BrowserHistoryStore(path, new List<BrowserHistoryEntry>());
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
            if (!_dirty)
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
