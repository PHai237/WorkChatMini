using System.IO;

namespace WorkChatMini;

// Explicit, bounded recovery for legacy stories whose chapters were committed
// while provider bookkeeping was disabled or unavailable. Never rebuild over
// an author's existing structured facts; never append/replace chapter files.
internal static class StoryMemoryRecovery
{
    internal const int MaxChaptersPerRecovery = 20;
    private const long MaxChapterFileBytes = 2 * 1024 * 1024;

    internal static bool HasStructuredMemory(StoryMemoryDocument memory) =>
        memory.Canon.Count > 0 || memory.Characters.Count > 0 ||
        memory.Relationships.Count > 0 || memory.RelationshipHistory.Count > 0 ||
        memory.Timeline.Count > 0 || memory.Threads.Count > 0 ||
        memory.Lore.Count > 0 || memory.Events.Count > 0 ||
        memory.Arcs.Count > 0 || memory.CharacterArcs.Count > 0 ||
        memory.CharacterDevelopmentHistory.Count > 0 || memory.Locations.Count > 0 ||
        memory.SceneStates.Count > 0 || memory.Revisions.Count > 0 ||
        memory.NarrativePromises.Count > 0 || memory.CausalLinks.Count > 0 ||
        (memory.AppliedProposalKeys?.Count ?? 0) > 0;

    internal static bool CanRecover(StoryMemoryDocument memory) =>
        memory.Chapters.Count is > 0 and <= MaxChaptersPerRecovery &&
        !HasStructuredMemory(memory) &&
        HasValidChapterSet(memory);

    internal static bool HasValidChapterSet(StoryMemoryDocument memory) =>
        memory.Chapters.Count > 0 &&
        memory.Chapters.All(c => c.Number > 0 && IsSafeChapterFileName(c.FileName)) &&
        memory.Chapters.Select(c => c.Number).Distinct().Count() == memory.Chapters.Count &&
        memory.Chapters.Select(c => c.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count() == memory.Chapters.Count;

    internal static bool HasExtractedFacts(StoryMemoryUpdate update) =>
        update.Canon.Count > 0 || update.Characters.Count > 0 ||
        update.Relationships.Count > 0 || update.Timeline.Count > 0 ||
        update.OpenThreads.Count > 0 || update.ResolvedThreads.Count > 0 ||
        update.Lore.Count > 0 || update.Events.Count > 0 ||
        update.Arcs.Count > 0 || update.CharacterArcs.Count > 0 ||
        update.Locations.Count > 0 || update.SceneStates.Count > 0 ||
        update.NarrativePromises.Count > 0 || update.CausalLinks.Count > 0;

    internal static StoryMemoryDocument ContextThroughChapter(StoryMemoryDocument memory, int chapterNumber)
    {
        // The bookkeeper for chapter N must not see summaries of chapters N+1.
        // The caller supplies a detached memory graph, not the live UI graph.
        var context = new StoryMemoryDocument
        {
            Chapters = memory.Chapters.Where(c => c.Number <= chapterNumber)
                .OrderBy(c => c.Number).ToList(),
            Canon = memory.Canon,
            Characters = memory.Characters,
            Relationships = memory.Relationships,
            RelationshipHistory = memory.RelationshipHistory,
            Timeline = memory.Timeline,
            Threads = memory.Threads,
            Lore = memory.Lore,
            Events = memory.Events,
            Arcs = memory.Arcs,
            CharacterArcs = memory.CharacterArcs,
            CharacterDevelopmentHistory = memory.CharacterDevelopmentHistory,
            Locations = memory.Locations,
            SceneStates = memory.SceneStates,
            Revisions = memory.Revisions,
            NarrativePromises = memory.NarrativePromises,
            CausalLinks = memory.CausalLinks
        };
        return context;
    }

    internal static async Task<string> ReadCommittedChapterAsync(
        StoryMemoryStore store, string projectId, StoryChapterMemory chapter, CancellationToken token)
    {
        if (!IsSafeChapterFileName(chapter.FileName))
            throw new InvalidDataException("Chapter file name is unsafe; recovery stopped.");
        string directory = store.GetChaptersDirectory(projectId);
        if (!Directory.Exists(directory) ||
            (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Chapter folder missing or linked; recovery stopped.");
        string path = Path.Combine(directory, chapter.FileName);
        var info = new FileInfo(path);
        if (info.Exists && (info.Attributes & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Linked chapter file cannot be sent to the AI recovery provider.");
        if (!info.Exists || info.Length == 0 || info.Length > MaxChapterFileBytes)
            throw new InvalidDataException($"Chapter {chapter.Number} is missing, empty, or too large for safe recovery.");
        string text = await File.ReadAllTextAsync(path, token);
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidDataException($"Chapter {chapter.Number} has no readable prose.");
        // The manuscript store writes '# Title\n\nBody'. The heading is not
        // narrative evidence and should not generate duplicate world facts.
        int bodyStart = text.IndexOf("\n\n", StringComparison.Ordinal);
        if (text.StartsWith("# ", StringComparison.Ordinal) && bodyStart is > 0 and < 500)
            text = text[(bodyStart + 2)..];
        return text.Trim();
    }

    private static bool IsSafeChapterFileName(string? name) =>
        !string.IsNullOrWhiteSpace(name) &&
        name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(Path.GetFileName(name), name, StringComparison.Ordinal) &&
        name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 &&
        name.IndexOfAny(new[] { '/', '\\', ':' }) < 0;
}
