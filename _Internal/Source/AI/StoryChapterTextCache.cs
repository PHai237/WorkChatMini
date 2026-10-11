using System.IO;

namespace WorkChatMini;

// Shared, bounded cache: old retrieval paths must not pin every visited chapter in RAM.
// Branch/rollback operations explicitly invalidate the active project's chapter directory.
internal static class StoryChapterTextCache
{
    private const int MaxEntries = 32;
    private const long MaxTextBytes = 8L * 1024 * 1024;
    private static readonly object Gate = new();
    private static readonly Dictionary<string, CachedText> Cache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly LinkedList<string> Recent = new();
    private static long _totalBytes;

    internal static int CachedEntries
    {
        get { lock (Gate) return Cache.Count; }
    }

    internal static string Read(string path)
    {
        var file = new FileInfo(path);
        long ticks = file.LastWriteTimeUtc.Ticks;
        long fileLength = file.Length;
        lock (Gate)
        {
            if (Cache.TryGetValue(path, out CachedText? found))
            {
                if (found.LastWriteTicks == ticks && found.FileLength == fileLength)
                {
                    Recent.Remove(found.Node);
                    Recent.AddFirst(found.Node);
                    return found.Text;
                }
                Remove(path, found);
            }
        }

        string text = File.ReadAllText(path);
        file.Refresh();
        // Never pin content read during a concurrent write/restore under a stale stamp.
        if (file.Exists && file.LastWriteTimeUtc.Ticks == ticks && file.Length == fileLength)
            Store(path, ticks, fileLength, text);
        return text;
    }

    internal static void StoreWritten(string path, string text)
    {
        var file = new FileInfo(path);
        if (file.Exists)
            Store(path, file.LastWriteTimeUtc.Ticks, file.Length, text);
    }

    internal static void InvalidateDirectory(string directory)
    {
        string prefix = Path.TrimEndingDirectorySeparator(Path.GetFullPath(directory)) + Path.DirectorySeparatorChar;
        lock (Gate)
        {
            foreach (string path in Cache.Keys.Where(x => x.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)).ToArray())
                Remove(path, Cache[path]);
        }
    }

    private static void Store(string path, long ticks, long fileLength, string text)
    {
        long size = 2L * text.Length;
        if (size > MaxTextBytes)
            return;

        lock (Gate)
        {
            if (Cache.TryGetValue(path, out CachedText? old))
                Remove(path, old);
            LinkedListNode<string> node = Recent.AddFirst(path);
            Cache[path] = new CachedText(ticks, fileLength, text, size, node);
            _totalBytes += size;
            while (Cache.Count > MaxEntries || _totalBytes > MaxTextBytes)
            {
                LinkedListNode<string>? oldest = Recent.Last;
                if (oldest is null)
                    break;
                Remove(oldest.Value, Cache[oldest.Value]);
            }
        }
    }

    private static void Remove(string path, CachedText old)
    {
        Cache.Remove(path);
        Recent.Remove(old.Node);
        _totalBytes -= old.Size;
    }

    private sealed record CachedText(
        long LastWriteTicks,
        long FileLength,
        string Text,
        long Size,
        LinkedListNode<string> Node);
}
