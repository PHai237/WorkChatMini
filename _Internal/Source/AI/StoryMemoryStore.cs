using System.IO;
using System.Text;
using System.Text.Json;

namespace WorkChatMini;

public sealed partial class StoryMemoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    internal static void InvalidateChapterCache(string projectId)
    {
        string directory = Path.Combine(new StoryProjectStore().GetProjectDirectory(projectId), "chapters");
        StoryChapterTextCache.InvalidateDirectory(directory);
        StoryChapterIndexStore.Invalidate(projectId);
    }

    private readonly StoryProjectStore _projects = new();
    private readonly StoryChapterIndexStore _chapterIndex = new();

    public async Task<StoryMemoryDocument> LoadAsync(string projectId, CancellationToken cancellationToken = default)
    {
        string path = GetMemoryPath(projectId);
        if (!File.Exists(path))
            return new StoryMemoryDocument();
        try
        {
            string json = await File.ReadAllTextAsync(path, cancellationToken);
            // A long-running story can accumulate thousands of structured facts.
            // Keep JSON parsing + schema normalization off the WPF dispatcher.
            return await Task.Run(() => ParseMemoryDocument(json), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Never convert a broken existing memory file into an empty document:
            // the next save would erase the user's canonical story state.
            AppLogger.Error("STORY-MEMORY", $"Memory is unreadable; original file preserved | project={projectId}", ex);
            throw new InvalidDataException(
                "Story Memory could not be read safely. Original file was preserved. See Logs.", ex);
        }
    }

    internal static StoryMemoryDocument ParseMemoryDocument(string json)
    {
        try
        {
            StoryMemoryDocument memory = JsonSerializer.Deserialize<StoryMemoryDocument>(json, JsonOptions)
                ?? throw new InvalidDataException("Story Memory contained a null document.");
            NormalizeLoadedDocument(memory);
            return memory;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Story Memory JSON is malformed.", ex);
        }
    }

    // All manuscript/bookkeeping changes are made to a detached graph first.
    // This is the same JSON representation we persist; no mutable collections
    // from the active UI memory survive into the candidate commit.
    internal static Task<StoryMemoryDocument> CloneForCommitAsync(
        StoryMemoryDocument source, CancellationToken token = default) =>
        Task.Run(() => ParseMemoryDocument(JsonSerializer.Serialize(source, JsonOptions)), token);

    public async Task SaveAsync(string projectId, StoryMemoryDocument memory, CancellationToken cancellationToken = default)
    {
        memory.SchemaVersion = Math.Max(memory.SchemaVersion, 6);
        memory.UpdatedUtc = DateTimeOffset.UtcNow;
        string path = GetMemoryPath(projectId);
        string directory = Path.GetDirectoryName(path) ?? _projects.GetProjectDirectory(projectId);
        Directory.CreateDirectory(directory);
        // Unique temp names avoid colliding with another in-flight save.
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            string json = JsonSerializer.Serialize(memory, JsonOptions);
            await File.WriteAllTextAsync(temp, json, cancellationToken);
            File.Move(temp, path, true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }

    public async Task<StoryChapterMemory?> AppendChapterAsync(
        string projectId,
        StoryMemoryDocument memory,
        string title,
        string summary,
        string fullText,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(fullText))
            return null;

        // Never overwrite an existing chapter, even if a stale in-memory chapter
        // list or an interrupted operation suggests the same next number/title.
        // The surrounding commit journal handles transaction rollback, but an
        // overwritten old prose file cannot be reconstructed from a partial write.
        int number = memory.Chapters.Count == 0 ? 1 : checked(memory.Chapters.Max(c => c.Number) + 1);
        string safeTitle = SanitizeFilePart(string.IsNullOrWhiteSpace(title) ? $"Chapter {number}" : title);
        string fileName = $"{number:0000}-{safeTitle}.md";
        string directory = GetChaptersDirectory(projectId);
        Directory.CreateDirectory(directory);
        EnsureChapterSlotAvailable(directory, memory, number, fileName);
        string path = Path.Combine(directory, fileName);
        string content = $"# {(string.IsNullOrWhiteSpace(title) ? $"Chapter {number}" : title.Trim())}\n\n{fullText.Trim()}\n";
        bool created = false;
        try
        {
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 65536, FileOptions.Asynchronous))
            {
                created = true;
                await using var writer = new StreamWriter(output, Encoding.UTF8);
                await writer.WriteAsync(content.AsMemory(), cancellationToken);
            }
        }
        catch
        {
            // Only delete the file this call successfully created; never delete
            // another writer's existing chapter when CreateNew refused a collision.
            if (created)
            {
                try { if (File.Exists(path)) File.Delete(path); }
                catch (IOException) { /* Transaction recovery will clean the remainder. */ }
                catch (UnauthorizedAccessException) { /* Preserve original failure. */ }
            }
            throw;
        }
        StoryChapterTextCache.StoreWritten(path, content);

        var chapter = new StoryChapterMemory
        {
            Number = number,
            Title = string.IsNullOrWhiteSpace(title) ? $"Chapter {number}" : title.Trim(),
            Summary = summary.Trim(),
            FileName = fileName
        };
        memory.Chapters.Add(chapter);
        return chapter;
    }

    internal static void EnsureChapterSlotAvailable(string directory,
        StoryMemoryDocument memory, int number, string fileName)
    {
        if (number <= 0 || memory.Chapters.Any(c => c.Number == number ||
            string.Equals(c.FileName, fileName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidDataException("Chapter number/filename already exists in Story Memory.");
        // A legacy/unindexed orphan chapter must not be silently replaced with
        // new content. Preserve it until the author can inspect it.
        if (Directory.Exists(directory))
        {
            string prefix = $"{number:0000}-";
            if (Directory.EnumerateFiles(directory, "*.md", SearchOption.TopDirectoryOnly)
                .Any(path => Path.GetFileName(path).StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(Path.GetFileName(path), fileName, StringComparison.OrdinalIgnoreCase)))
                throw new IOException("Chapter file already exists on disk; refusing to overwrite it.");
        }
    }

    public Task<IReadOnlyList<StoryChapterSearchHit>> SearchChapterTextAsync(
        string projectId,
        string query,
        int maxResults = 3,
        CancellationToken cancellationToken = default)
    {
        string cleanQuery = query?.Trim() ?? string.Empty;
        var plan = new StoryRetrievalPlan
        {
            OriginalQuery = cleanQuery,
            ExpandedQuery = cleanQuery,
            Terms = StoryMemoryRetriever.Tokenize(cleanQuery)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(40)
                .ToArray(),
            DirectTerms = StoryMemoryRetriever.Tokenize(cleanQuery)
                .Distinct(StringComparer.OrdinalIgnoreCase).Take(16).ToArray()
        };
        return SearchChapterTextAsync(projectId, plan, maxResults, cancellationToken);
    }

    public async Task<IReadOnlyList<StoryChapterSearchHit>> SearchChapterTextAsync(
        string projectId,
        StoryRetrievalPlan plan,
        int maxResults = 3,
        CancellationToken cancellationToken = default)
    {
        string directory = GetChaptersDirectory(projectId);
        if (!Directory.Exists(directory) ||
            (plan.Terms.Count == 0 && plan.ChapterBoosts.Count == 0 && !plan.PreferRecentChapters))
            return Array.Empty<StoryChapterSearchHit>();

        try
        {
            StoryChapterIndexDocument index = await _chapterIndex.RefreshAsync(projectId, directory, cancellationToken);
            int maxCandidates = Math.Max(8, Math.Max(1, maxResults) * 4);
            // Ranking hundreds of chapter signatures must not stall the UI thread.
            IReadOnlyList<StoryChapterIndexCandidate> candidates = await Task.Run(
                () => StoryChapterIndexStore.FindCandidates(index, plan, maxCandidates), cancellationToken);
            string[] terms = plan.Terms.Take(40).ToArray();
            IReadOnlyList<StoryChapterSearchHit> hits = await Task.Run(() =>
            {
                var results = new List<StoryChapterSearchHit>();
                foreach (StoryChapterIndexCandidate candidate in candidates)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string path = Path.Combine(directory, candidate.FileName);
                    if (!File.Exists(path))
                        continue;

                    string text;
                    try { text = ReadChapterTextCached(path); }
                    catch { continue; }

                    int textScore = ScoreText(text, terms);
                    // A probabilistic index match is only a shortlist hint. Without
                    // actual prose evidence (or an explicit structured provenance
                    // boost), never surface the chapter as a search hit.
                    if (!AcceptVerifiedEvidence(textScore,
                        plan.ChapterBoosts.GetValueOrDefault(candidate.Number), plan.PreferRecentChapters))
                        continue;
                    int score = candidate.Score + textScore;
                    if (score <= 0)
                        continue;

                    string excerpt = StoryChapterExcerptSelector.Select(text, plan.DirectTerms, plan.ResolvedEntityTerms, terms);
                    string name = Path.GetFileNameWithoutExtension(path);
                    string title = ReadTitle(text, name);
                    results.Add(new StoryChapterSearchHit
                    {
                        Number = candidate.Number > 0 ? candidate.Number : ParseChapterNumber(name),
                        Title = title,
                        Excerpt = excerpt,
                        Score = score
                    });
                }
                return (IReadOnlyList<StoryChapterSearchHit>)results
                    .OrderByDescending(h => h.Score)
                    .ThenByDescending(h => h.Number)
                    .Take(Math.Max(1, maxResults))
                    .ToList();
            }, cancellationToken);

            // A local paraphrase fallback is intentionally weaker than a direct
            // lexical/provenance result, and can never write a story fact.
            if (hits.Count == 0 && !plan.PreferRecentChapters)
                hits = await SearchLocalParaphrasesAsync(index, directory, plan, maxResults, cancellationToken);

            AppLogger.Info("STORY-RETRIEVAL",
                $"Chapter retrieval | project={projectId} | mode=indexed | indexed={index.Chapters.Count} | candidates={candidates.Count} | hits={hits.Count} | boostedChapters={plan.ChapterBoosts.Count}");
            return hits;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Index failures must never bypass the committed-manuscript gate.
            // The older raw directory scan could surface an orphaned draft after
            // an index IO failure, even though indexed retrieval excluded it.
            AppLogger.Warn("STORY-RETRIEVAL",
                $"Indexed retrieval failed; trying ledger-scoped scan | project={projectId} | {ex.Message}");
            try
            {
                StoryMemoryDocument authoritativeMemory = await LoadAsync(projectId, cancellationToken);
                HashSet<string> committed = StoryChapterIndexStore.CommittedChapterFileNames(authoritativeMemory);
                return await SearchChapterTextLegacyAsync(directory, plan, maxResults, committed, cancellationToken);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception fallbackError)
            {
                // Malformed or unreadable story-memory.json must never be
                // interpreted as permission to index any stray chapter files.
                AppLogger.Warn("STORY-RETRIEVAL",
                    $"Ledger-scoped retrieval unavailable; no chapter evidence returned | project={projectId} | {fallbackError.Message}");
                return Array.Empty<StoryChapterSearchHit>();
            }
        }
    }

    // Exposed internally so regression tests can exercise this last-resort path
    // independently of platform-specific index IO failures.
    internal static async Task<IReadOnlyList<StoryChapterSearchHit>> SearchChapterTextLegacyAsync(
        string directory,
        StoryRetrievalPlan plan,
        int maxResults,
        IReadOnlySet<string> committedFileNames,
        CancellationToken cancellationToken = default)
    {
        if (committedFileNames.Count == 0 || !Directory.Exists(directory))
            return Array.Empty<StoryChapterSearchHit>();
        string[] terms = plan.Terms.Take(40).ToArray();
        return await Task.Run(() =>
        {
            var hits = new List<StoryChapterSearchHit>();
            foreach (string path in Directory.EnumerateFiles(directory, "*.md", SearchOption.TopDirectoryOnly))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!committedFileNames.Contains(Path.GetFileName(path)))
                    continue;
                string text;
                try { text = ReadChapterTextCached(path); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch { continue; }

                string name = Path.GetFileNameWithoutExtension(path);
                int number = ParseChapterNumber(name);
                int score = ScoreText(text, terms) + plan.ChapterBoosts.GetValueOrDefault(number) * 6;
                if (score <= 0)
                    continue;

                hits.Add(new StoryChapterSearchHit
                {
                    Number = number,
                    Title = ReadTitle(text, name),
                    Excerpt = StoryChapterExcerptSelector.Select(text, plan.DirectTerms, plan.ResolvedEntityTerms, terms),
                    Score = score
                });
            }
            return (IReadOnlyList<StoryChapterSearchHit>)hits
                .OrderByDescending(h => h.Score)
                .ThenByDescending(h => h.Number)
                .Take(Math.Max(1, maxResults))
                .ToList();
        }, cancellationToken);
    }

    public string GetMemoryPath(string projectId) => Path.Combine(_projects.GetProjectDirectory(projectId), "memory", "story-memory.json");
    public string GetChaptersDirectory(string projectId) => Path.Combine(_projects.GetProjectDirectory(projectId), "chapters");


    private static void NormalizeLoadedDocument(StoryMemoryDocument memory)
    {
        memory.SchemaVersion = Math.Max(memory.SchemaVersion, 6);
        memory.Chapters ??= new List<StoryChapterMemory>();
        memory.Canon ??= new List<StoryCanonFact>();
        memory.Characters ??= new List<StoryCharacterMemory>();
        memory.Relationships ??= new List<StoryRelationshipMemory>();
        memory.RelationshipHistory ??= new List<StoryRelationshipChange>();
        memory.Timeline ??= new List<StoryTimelineEvent>();
        memory.Threads ??= new List<StoryPlotThread>();
        memory.Lore ??= new List<StoryLoreEntry>();
        memory.Events ??= new List<StorySignificantEvent>();
        memory.Arcs ??= new List<StoryArcMemory>();
        memory.CharacterArcs ??= new List<StoryCharacterArcMemory>();
        memory.CharacterDevelopmentHistory ??= new List<StoryCharacterDevelopmentChange>();
        memory.Locations ??= new List<StoryLocationMemory>();
        memory.SceneStates ??= new List<StorySceneStateMemory>();
        memory.Revisions ??= new List<StoryCanonRevision>();
        memory.NarrativePromises ??= new List<StoryNarrativePromise>();
        memory.CausalLinks ??= new List<StoryCausalLink>();

        foreach (StoryCharacterMemory character in memory.Characters)
        {
            character.Aliases ??= new List<string>();
            character.Goals ??= new List<string>();
            character.Fears ??= new List<string>();
            character.Secrets ??= new List<string>();
            character.Knowledge ??= new List<string>();
            character.Abilities ??= new List<string>();
            character.Inventory ??= new List<string>();
        }
        foreach (StoryRelationshipMemory relationship in memory.Relationships)
            relationship.History ??= new List<string>();
        foreach (StoryCharacterArcMemory characterArc in memory.CharacterArcs)
        {
            characterArc.Status = string.IsNullOrWhiteSpace(characterArc.Status) ? "Active" : characterArc.Status;
            characterArc.Importance = StoryNarrativeFocus.NormalizeImportance(characterArc.Importance);
            characterArc.Direction = StoryCharacterArcLedger.NormalizeDirection(characterArc.Direction);
        }
        foreach (StoryLocationMemory location in memory.Locations)
        {
            location.Connections ??= new List<string>();
            location.PresentCharacters ??= new List<string>();
            location.RelevantObjects ??= new List<string>();
            location.Status = string.IsNullOrWhiteSpace(location.Status) ? "Active" : location.Status;
        }
        foreach (StorySceneStateMemory scene in memory.SceneStates)
        {
            scene.PresentCharacters ??= new List<string>();
            scene.DepartedCharacters ??= new List<string>();
        }
        foreach (StorySignificantEvent storyEvent in memory.Events)
        {
            storyEvent.RelatedCharacters ??= new List<string>();
            storyEvent.Tags ??= new List<string>();
        }
        foreach (StoryArcMemory arc in memory.Arcs)
        {
            arc.Goals ??= new List<string>();
            arc.RelatedCharacters ??= new List<string>();
            arc.RelatedThreads ??= new List<string>();
            arc.Status = string.IsNullOrWhiteSpace(arc.Status) ? "Active" : arc.Status;
            arc.Priority = StoryNarrativeFocus.NormalizePriority(arc.Priority);
            arc.Momentum = StoryNarrativeFocus.NormalizeMomentum(arc.Momentum);
        }
        foreach (StoryPlotThread thread in memory.Threads)
            thread.Importance = StoryNarrativeFocus.NormalizeImportance(thread.Importance);
        foreach (StoryCanonRevision revision in memory.Revisions)
            revision.RelatedCharacters ??= new List<string>();
        foreach (StoryNarrativePromise promise in memory.NarrativePromises)
        {
            promise.RelatedCharacters ??= new List<string>();
            promise.RelatedArcs ??= new List<string>();
            promise.RelatedThreads ??= new List<string>();
            promise.Kind = StoryCausalMemoryLedger.NormalizeKind(promise.Kind);
            promise.Status = StoryCausalMemoryLedger.NormalizeStatus(promise.Status);
            promise.Importance = StoryNarrativeFocus.NormalizeImportance(promise.Importance);
            promise.Horizon = StoryCausalMemoryLedger.NormalizeHorizon(promise.Horizon);
        }
        foreach (StoryCausalLink link in memory.CausalLinks)
        {
            link.RelatedCharacters ??= new List<string>();
            link.Relation = StoryCausalMemoryLedger.NormalizeRelation(link.Relation);
            link.Importance = StoryNarrativeFocus.NormalizeImportance(link.Importance);
        }
    }

    private static string ReadChapterTextCached(string path) => StoryChapterTextCache.Read(path);

    // Bloom signatures may produce false positives. Only actual prose, structured
    // chapter provenance, or explicit Continue Story recency can admit a hit.
    internal static bool AcceptVerifiedEvidence(int proseScore, int provenanceBoost, bool preferRecent) =>
        proseScore > 0 || provenanceBoost > 0 || preferRecent;

    private static int ScoreText(string text, IReadOnlyList<string> terms)
    {
        int score = 0;
        foreach (string term in terms)
        {
            int index = 0;
            while ((index = text.IndexOf(term, index, StringComparison.OrdinalIgnoreCase)) >= 0)
            {
                int end = index + term.Length;
                // Do not mistake a substring of another token for evidence.
                // The chapter-term index uses whole tokens, including '-' and '_'.
                bool wholeToken = (index == 0 || !IsIndexTokenChar(text[index - 1])) &&
                                  (end == text.Length || !IsIndexTokenChar(text[end]));
                if (wholeToken)
                {
                    score++;
                    if (score >= 30)
                        return score;
                }
                index = end;
            }
        }
        return score;
    }

    private static bool IsIndexTokenChar(char c) => char.IsLetterOrDigit(c) || c is '_' or '-';

    private static string ReadTitle(string text, string fallback)
    {
        using var reader = new StringReader(text);
        string? line = reader.ReadLine();
        return line?.StartsWith("# ", StringComparison.Ordinal) == true ? line[2..].Trim() : fallback;
    }

    private static int ParseChapterNumber(string name)
    {
        int dash = name.IndexOf('-');
        string prefix = dash > 0 ? name[..dash] : name;
        return int.TryParse(prefix, out int number) ? number : 0;
    }

    private static string SanitizeFilePart(string value)
    {
        var invalid = Path.GetInvalidFileNameChars().ToHashSet();
        string cleaned = new(value.Where(c => !invalid.Contains(c) && !char.IsControl(c)).ToArray());
        cleaned = string.Join('-', cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries));
        if (cleaned.Length > 60)
            cleaned = cleaned[..60];
        return string.IsNullOrWhiteSpace(cleaned) ? "chapter" : cleaned;
    }
}
