using System.IO;

namespace WorkChatMini;

/// <summary>Read-only diagnostics for the active manuscript, independent of
/// the checkpoint object audit. Reports missing/stray chapters without deleting,
/// repairing or manufacturing structured canon.</summary>
internal static class StoryLiveConsistencyAudit
{
    internal static StoryLiveConsistencyReport Analyze(string projectDirectory,
        StoryMemoryDocument memory)
    {
        string chaptersDirectory = Path.Combine(projectDirectory, "chapters");
        var referenced = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var numbers = new HashSet<int>();
        var issues = new List<string>();
        int missing = 0, empty = 0, duplicate = 0, invalid = 0;
        foreach (StoryChapterMemory chapter in memory.Chapters)
        {
            if (chapter.Number < 1 || !numbers.Add(chapter.Number))
            {
                duplicate++;
                AddIssue(issues, $"Duplicate/invalid chapter number {chapter.Number}.");
            }
            if (!IsSafeChapterName(chapter.FileName))
            {
                invalid++;
                AddIssue(issues, $"Unsafe chapter filename at chapter {chapter.Number}.");
                continue;
            }
            if (!referenced.Add(chapter.FileName))
            {
                duplicate++;
                AddIssue(issues, $"Repeated filename for chapter {chapter.Number}.");
            }
            string path = Path.Combine(chaptersDirectory, chapter.FileName);
            if (!File.Exists(path))
            {
                missing++;
                AddIssue(issues, $"Chapter {chapter.Number}: missing file {chapter.FileName}.");
            }
            else if (new FileInfo(path).Length == 0)
            {
                empty++;
                AddIssue(issues, $"Chapter {chapter.Number}: empty manuscript file.");
            }
        }
        int unlisted = 0;
        if (Directory.Exists(chaptersDirectory))
            foreach (string path in Directory.EnumerateFiles(chaptersDirectory, "*.md", SearchOption.TopDirectoryOnly))
            {
                if (referenced.Contains(Path.GetFileName(path))) continue;
                unlisted++;
                AddIssue(issues, $"Unlisted chapter file: {Path.GetFileName(path)} (preserved).");
            }
        return new StoryLiveConsistencyReport(memory.Chapters.Count, missing, empty,
            duplicate, invalid, unlisted, issues);
    }

    internal static async Task<StoryLiveConsistencyReport> AnalyzeAsync(string projectId,
        CancellationToken token = default)
    {
        var projects = new StoryProjectStore();
        var memoryStore = new StoryMemoryStore();
        StoryMemoryDocument memory = await memoryStore.LoadAsync(projectId, token);
        return await Task.Run(() => Analyze(projects.GetProjectDirectory(projectId), memory), token);
    }

    private static bool IsSafeChapterName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name == Path.GetFileName(name) &&
        name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) &&
        !name.Contains('/') && !name.Contains('\\') && !name.Contains(':') &&
        !name.Any(char.IsControl) && name is not "." and not "..";

    private static void AddIssue(List<string> issues, string message)
    {
        if (issues.Count < 15) issues.Add(message);
    }
}

internal sealed record StoryLiveConsistencyReport(int ReferencedChapters, int MissingFiles,
    int EmptyFiles, int DuplicateReferences, int InvalidNames, int UnlistedFiles,
    IReadOnlyList<string> Issues)
{
    internal int IssueCount => MissingFiles + EmptyFiles + DuplicateReferences + InvalidNames + UnlistedFiles;
}
