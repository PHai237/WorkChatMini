namespace WorkChatMini;

internal static class StorySceneCompassSelfTest
{
    internal static void Verify()
    {
        var noCanon = new StoryMemoryDocument();
        noCanon.Threads.Add(new StoryPlotThread { Title = "Uncommitted mystery", OpenedChapter = 1 });
        StorySceneCompassBrief empty = StorySceneCompass.Build(noCanon);
        if (empty.HasCommittedChapter || empty.SuggestedCue.Length > 0 ||
            empty.Display.Contains("Uncommitted mystery", StringComparison.Ordinal))
            throw new InvalidOperationException("Scene Compass leaked uncommitted branch material.");

        noCanon.Chapters.Add(new StoryChapterMemory { Number = 1, Title = "Arrival", Summary = "They arrived safely." });
        noCanon.Chapters.Add(new StoryChapterMemory { Number = 2, Title = "Return", Summary = "Trust changed quietly." });
        noCanon.Chapters.Add(new StoryChapterMemory { Number = 3, Title = "Ambiguous draft", Summary = "Draft only." });
        noCanon.Chapters.Add(new StoryChapterMemory { Number = 3, Title = "Duplicate", Summary = "Unknown state." });
        noCanon.Threads.Add(new StoryPlotThread { Title = "Existing trust", Detail = "What happens to the friendship?", OpenedChapter = 2, Status = "Open" });
        noCanon.Threads.Add(new StoryPlotThread { Title = "Future secret", Detail = "Not committed", OpenedChapter = 4, Status = "Open" });
        noCanon.Threads.Add(new StoryPlotThread { Title = "Resolved dispute", OpenedChapter = 1, Status = "Closed" });
        noCanon.NarrativePromises.Add(new StoryNarrativePromise { Title = "Delayed apology", IntroducedChapter = 2, Status = "Open" });
        noCanon.NarrativePromises.Add(new StoryNarrativePromise { Title = "Uncommitted setup", IntroducedChapter = 4, Status = "Open" });
        noCanon.Characters.Add(new StoryCharacterMemory { Name = "Luna", Goals = new() { "Restore trust" } });
        StorySceneCompassBrief first = StorySceneCompass.Build(noCanon);
        StorySceneCompassBrief second = StorySceneCompass.Build(noCanon);
        if (!first.HasCommittedChapter || first != second ||
            !first.Display.Contains("Trust changed quietly", StringComparison.Ordinal) ||
            !first.Display.Contains("Existing trust", StringComparison.Ordinal) ||
            !first.Display.Contains("Delayed apology", StringComparison.Ordinal) ||
            !first.Display.Contains("Restore trust", StringComparison.Ordinal) ||
            !first.SuggestedCue.Contains("Existing trust", StringComparison.Ordinal) ||
            first.Display.Contains("Draft only", StringComparison.Ordinal) ||
            first.Display.Contains("Unknown state", StringComparison.Ordinal) ||
            first.Display.Contains("Future secret", StringComparison.Ordinal) ||
            first.Display.Contains("Uncommitted setup", StringComparison.Ordinal) ||
            first.Display.Contains("Resolved dispute", StringComparison.Ordinal))
            throw new InvalidOperationException("Scene Compass did not respect committed-only branch boundaries.");
    }
}
