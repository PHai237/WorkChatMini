using System.IO;
using System.Text.Json;

namespace WorkChatMini;

public sealed class StoryChapterIndexStore
{
    private const int MaxTermsPerChapter = 1800;
    private const int CurrentIndexSchema = 3;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = false,
        PropertyNameCaseInsensitive = true
    };
    private static readonly StoryChapterIndexMemoryCache IndexCache = new();
    internal static int CachedProjectCount => IndexCache.Count;

    // A branch restore can reuse a chapter filename with different prose.
    // Drop the cached index rather than relying solely on filesystem timestamp granularity.
    internal static void Invalidate(string projectId)
    {
        IndexCache.Remove(projectId);
        try
        {
            // The index is disposable, and a restored alternate may reuse a filename.
            string path = Path.Combine(new StoryProjectStore().GetProjectDirectory(projectId), "memory", "chapter-index.json");
            if (File.Exists(path)) File.Delete(path);
        }
        catch (IOException ex)
        {
            AppLogger.Warn("STORY-RETRIEVAL", $"Could not discard stale chapter index | {ex.Message}");
        }
        catch (UnauthorizedAccessException ex)
        {
            AppLogger.Warn("STORY-RETRIEVAL", $"Could not discard stale chapter index | {ex.Message}");
        }
    }

    // Preserve existing legacy chapter names, but refuse ambiguous or unsafe
    // references. A duplicate chapter number / filename is not solid evidence.
    internal static HashSet<string> CommittedChapterFileNames(StoryMemoryDocument memory)
    {
        var byNumber = (memory.Chapters ?? new List<StoryChapterMemory>())
            .Where(c => c is not null && c.Number > 0 && !string.IsNullOrWhiteSpace(c.FileName))
            .GroupBy(c => c.Number)
            .Where(group => group.Count() == 1)
            .Select(group => group.Single())
            .ToArray();
        return byNumber.GroupBy(c => c.FileName, StringComparer.OrdinalIgnoreCase)
            .Where(group => group.Count() == 1)
            .Select(group => group.Single().FileName)
            .Where(file => string.Equals(Path.GetFileName(file), file, StringComparison.Ordinal) &&
                           file.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    private readonly StoryProjectStore _projects = new();

    public async Task<StoryChapterIndexDocument> RefreshAsync(
        string projectId,
        string chaptersDirectory,
        CancellationToken cancellationToken = default)
    {
        // An orphaned chapter file is NOT committed prose. Only the manuscript
        // ledger in story-memory.json can authorize chapter evidence. If memory
        // cannot be read safely, do not silently fall back to scanning files.
        StoryMemoryDocument committedMemory = await new StoryMemoryStore().LoadAsync(projectId, cancellationToken);
        HashSet<string> committedFiles = CommittedChapterFileNames(committedMemory);
        StoryChapterIndexDocument index = await LoadAsync(projectId, cancellationToken);
        if (!Directory.Exists(chaptersDirectory))
        {
            if (index.Chapters.Count > 0)
            {
                index.Chapters.Clear();
                try { await SaveAsync(projectId, index, cancellationToken); }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex)
                {
                    IndexCache.Put(projectId, index);
                    AppLogger.Warn("STORY-RETRIEVAL", $"Empty chapter index save failed; cache remains usable | project={projectId} | {ex.Message}");
                }
            }
            return index;
        }

        bool changed = false;
        bool requiresUpgrade = index.SchemaVersion < CurrentIndexSchema;
        bool incompleteUpgrade = false;
        var files = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string path in Directory.EnumerateFiles(chaptersDirectory, "*.md", SearchOption.TopDirectoryOnly))
        {
            string fileName = Path.GetFileName(path) ?? string.Empty;
            if (!string.IsNullOrWhiteSpace(fileName) && committedFiles.Contains(fileName))
                files[fileName] = path;
        }

        int removed = index.Chapters.RemoveAll(x => string.IsNullOrWhiteSpace(x.FileName) || !files.ContainsKey(x.FileName));
        changed |= removed > 0;

        var existing = new Dictionary<string, StoryChapterIndexEntry>(StringComparer.OrdinalIgnoreCase);
        foreach (StoryChapterIndexEntry indexedChapter in index.Chapters)
        {
            if (!string.IsNullOrWhiteSpace(indexedChapter.FileName))
                existing[indexedChapter.FileName] = indexedChapter;
        }
        foreach ((string fileName, string path) in files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            long ticks;
            try { ticks = File.GetLastWriteTimeUtc(path).Ticks; }
            catch { continue; }

            if (existing.TryGetValue(fileName, out StoryChapterIndexEntry? entry) &&
                !requiresUpgrade && entry.LastWriteTicks == ticks &&
                StoryChapterTokenFilter.TryRead(entry.TermSignature) is not null)
                continue;

            string text;
            try { text = await File.ReadAllTextAsync(path, cancellationToken); }
            catch (OperationCanceledException) { throw; }
            catch
            {
                incompleteUpgrade = true;
                continue;
            }

            // Full-chapter tokenization is CPU-heavy. Do not execute it on the WPF dispatcher.
            StoryChapterIndexEntry rebuilt = await Task.Run(
                () => BuildEntry(fileName, text, ticks), cancellationToken);
            if (entry is null)
                index.Chapters.Add(rebuilt);
            else
            {
                int position = index.Chapters.IndexOf(entry);
                index.Chapters[position] = rebuilt;
            }
            changed = true;
        }

        if (requiresUpgrade && !incompleteUpgrade)
        {
            index.SchemaVersion = CurrentIndexSchema;
            changed = true;
        }
        if (changed)
        {
            index.UpdatedUtc = DateTimeOffset.UtcNow;
            try
            {
                await SaveAsync(projectId, index, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // The index is disposable cache data. Keep the in-memory index and never fail Story generation because cache persistence failed.
                IndexCache.Put(projectId, index);
                AppLogger.Warn("STORY-RETRIEVAL", $"Chapter index save failed; continuing with in-memory index | project={projectId} | {ex.Message}");
            }
            AppLogger.Info("STORY-RETRIEVAL", $"Chapter index refreshed | project={projectId} | chapters={index.Chapters.Count} | removed={removed}");
        }
        return index;
    }

    public static IReadOnlyList<StoryChapterIndexCandidate> FindCandidates(
        StoryChapterIndexDocument index, StoryRetrievalPlan plan, int maxCandidates) =>
        StoryChapterIndexRanking.Find(index, plan, maxCandidates);

    private async Task<StoryChapterIndexDocument> LoadAsync(string projectId, CancellationToken cancellationToken)
    {
        if (IndexCache.TryGet(projectId, out StoryChapterIndexDocument? cached) && cached is not null)
            return cached;

        string path = GetPath(projectId);
        if (!File.Exists(path))
        {
            var empty = new StoryChapterIndexDocument();
            IndexCache.Put(projectId, empty);
            return empty;
        }
        try
        {
            string json = await File.ReadAllTextAsync(path, cancellationToken);
            StoryChapterIndexDocument index = await Task.Run(() =>
            {
                StoryChapterIndexDocument loaded =
                    JsonSerializer.Deserialize<StoryChapterIndexDocument>(json, JsonOptions) ?? new StoryChapterIndexDocument();
                loaded.Chapters ??= new List<StoryChapterIndexEntry>();
                foreach (StoryChapterIndexEntry chapter in loaded.Chapters)
                    chapter.Terms ??= new List<string>();
                return loaded;
            }, cancellationToken);
            IndexCache.Put(projectId, index);
            return index;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("STORY-RETRIEVAL", $"Chapter index load failed; rebuilding lazily | project={projectId} | {ex.Message}");
            var empty = new StoryChapterIndexDocument();
            IndexCache.Put(projectId, empty);
            return empty;
        }
    }

    private async Task SaveAsync(string projectId, StoryChapterIndexDocument index, CancellationToken cancellationToken)
    {
        string path = GetPath(projectId);
        string directory = Path.GetDirectoryName(path) ?? _projects.GetProjectDirectory(projectId);
        Directory.CreateDirectory(directory);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            string json = await Task.Run(() => JsonSerializer.Serialize(index, JsonOptions), cancellationToken);
            await File.WriteAllTextAsync(temp, json, cancellationToken);
            File.Move(temp, path, true);
            IndexCache.Put(projectId, index);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    private static StoryChapterIndexEntry BuildEntry(string fileName, string text, long ticks)
    {
        string name = Path.GetFileNameWithoutExtension(fileName) ?? fileName;
        int number = ParseChapterNumber(name);
        string title = ReadTitle(text, name);
        // Sample bounded windows to keep index size small; the separate full-text
        // signature makes clues outside those windows searchable too.
        int window = Math.Min(12000, text.Length);
        int middleStart = Math.Max(0, (text.Length - window) / 2);
        int endStart = Math.Max(0, text.Length - window);
        List<string> terms = StoryMemoryRetriever.Tokenize(text[..window]).Take(MaxTermsPerChapter / 3)
            .Concat(StoryMemoryRetriever.Tokenize(text.Substring(middleStart, window)).Take(MaxTermsPerChapter / 3))
            .Concat(StoryMemoryRetriever.Tokenize(text.Substring(endStart, window)).Take(MaxTermsPerChapter / 3))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(MaxTermsPerChapter)
            .ToList();
        string signature = StoryChapterTokenFilter.Build(text).Serialize();
        return new StoryChapterIndexEntry
        {
            Number = number,
            FileName = fileName,
            Title = title,
            LastWriteTicks = ticks,
            TextLength = text.Length,
            Terms = terms,
            TermSignature = signature
        };
    }

    private string GetPath(string projectId) => Path.Combine(_projects.GetProjectDirectory(projectId), "memory", "chapter-index.json");

    private static int ParseChapterNumber(string name)
    {
        int dash = name.IndexOf('-');
        string prefix = dash > 0 ? name[..dash] : name;
        return int.TryParse(prefix, out int number) ? number : 0;
    }

    private static string ReadTitle(string text, string fallback)
    {
        using var reader = new StringReader(text);
        string? line = reader.ReadLine();
        return line?.StartsWith("# ", StringComparison.Ordinal) == true ? line[2..].Trim() : fallback;
    }
}

public sealed record StoryChapterIndexCandidate(int Number, string FileName, int Score);
