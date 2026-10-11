using System.Text;

namespace WorkChatMini;

// Read-only creator aid sourced from CURRENT branch's committed chapter memory.
// Never sends an API request, mutates the memory document or commits a chapter.
internal sealed record StorySceneCompassBrief(string Display, string SuggestedCue, bool HasCommittedChapter);

internal static class StorySceneCompass
{
    internal static StorySceneCompassBrief Build(StoryMemoryDocument memory)
    {
        // Duplicate numbers are ambiguous after interrupted checkpoint recovery;
        // exclude them rather than proposing material from an uncertain chapter.
        var chapters = memory.Chapters.Where(c => c.Number > 0)
            .GroupBy(c => c.Number).Where(g => g.Count() == 1)
            .Select(g => g.Single()).OrderByDescending(c => c.Number).ToArray();
        if (chapters.Length == 0)
            return new StorySceneCompassBrief(
                "No committed chapters on this branch yet.\n\nDiscuss your premise in Chat, then use Draft Scene or Continue Story to begin. Scene Compass only reads approved, committed story material.",
                string.Empty, false);

        int latestChapter = chapters[0].Number;
        var display = new StringBuilder();
        display.AppendLine($"CURRENT BRANCH · {chapters.Length} committed chapter(s)");
        display.AppendLine();
        display.AppendLine("LAST COMMITTED CHAPTERS");
        foreach (StoryChapterMemory chapter in chapters.Take(2))
            display.AppendLine($"• Ch. {chapter.Number} — {Trim(chapter.Title, 70)}\n  {Trim(chapter.Summary, 245)}");

        var threads = memory.Threads.Where(t => t.Status.Equals("Open", StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrWhiteSpace(t.Title) && t.OpenedChapter <= latestChapter)
            .OrderByDescending(t => t.Importance.Equals("Critical", StringComparison.OrdinalIgnoreCase))
            .ThenByDescending(t => t.LastTouchedChapter ?? t.OpenedChapter)
            .Take(3).ToArray();
        if (threads.Length > 0)
        {
            display.AppendLine("\nUNRESOLVED THREADS (possible, not mandatory next beats)");
            foreach (StoryPlotThread thread in threads)
                display.AppendLine($"• {Trim(thread.Title, 95)} — {Trim(thread.Detail, 135)}");
        }

        var promises = memory.NarrativePromises.Where(p => StoryCausalMemoryLedger.IsActive(p) &&
                !string.IsNullOrWhiteSpace(p.Title) && p.IntroducedChapter is > 0 && p.IntroducedChapter <= latestChapter)
            .OrderByDescending(p => p.LastTouchedChapter ?? p.IntroducedChapter)
            .Take(2).ToArray();
        if (promises.Length > 0)
        {
            display.AppendLine("\nONGOING SETUPS / PROMISES");
            foreach (StoryNarrativePromise promise in promises)
                display.AppendLine($"• {Trim(promise.Title, 105)} — {Trim(promise.Detail, 125)}");
        }

        var goals = memory.Characters.Where(c => !string.IsNullOrWhiteSpace(c.Name) && c.Goals.Count > 0)
            .OrderBy(c => c.Name, StringComparer.OrdinalIgnoreCase).Take(3).ToArray();
        if (goals.Length > 0)
        {
            display.AppendLine("\nCHARACTER MOTIVES (current memory)");
            foreach (StoryCharacterMemory character in goals)
                display.AppendLine($"• {Trim(character.Name, 50)}: {Trim(character.Goals[0], 145)}");
        }

        display.AppendLine("\nThis is a reading aid, not a generated outline or new canon. Open threads and goals do not have to be resolved in the next chapter. Uncommitted drafts are excluded.");
        var cue = new StringBuilder();
        cue.Append($"Continue naturally after committed chapter {latestChapter}. ");
        cue.Append("Show a credible character choice and consequences from existing events; keep pacing appropriate and avoid an unrelated twist. ");
        if (threads.Length > 0)
            cue.Append($"If it fits the scene, develop the existing thread ‘{Trim(threads[0].Title, 130)}’ without forcing its resolution. ");
        if (goals.Length > 0)
            cue.Append($"Respect {Trim(goals[0].Name, 60)}'s established goals. ");
        cue.Append("Preserve all committed canon. This is a suggested writing direction, not an event that has already happened.");
        return new StorySceneCompassBrief(display.ToString().TrimEnd(), cue.ToString(), true);
    }

    private static string Trim(string? text, int max)
    {
        string cleaned = string.Join(" ", (text ?? string.Empty).Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return cleaned.Length <= max ? cleaned : cleaned[..(max - 1)] + "…";
    }
}
