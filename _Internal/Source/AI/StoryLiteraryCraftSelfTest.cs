namespace WorkChatMini;

// Reuses the existing Story memory/continuity self-test case.
internal static class StoryLiteraryCraftSelfTest
{
    internal static void Verify()
    {
        var prep = new StoryDraftPreparation
        {
            Plan = "The old choice has a consequence.",
            Constraints = "No invented knowledge.",
            CraftBrief = "The silence should change their trust rather than force a revelation."
        };
        string plan = StoryLiteraryCraft.WithSceneBrief(prep);
        if (!plan.Contains("PRIVATE SCENE CRAFT INTENT", StringComparison.Ordinal) ||
            !plan.Contains(prep.CraftBrief, StringComparison.Ordinal) ||
            StoryLiteraryCraft.WithSceneBrief(new StoryDraftPreparation { Plan = "plain" }) != "plain" ||
            StoryLiteraryCraft.NormalizeBrief(new string('a', 4000)).Length > 1401)
            throw new InvalidOperationException("Literary scene brief is not optional and bounded.");
        StoryDraftPreparation? legacy = StoryJson.TryDeserialize<StoryDraftPreparation>(
            "{\"plan\":\"A remembered choice\",\"constraints\":\"Preserve location\"}");
        if (legacy is null || legacy.CraftBrief.Length != 0 ||
            StoryLiteraryCraft.WithSceneBrief(legacy) != legacy.Plan)
            throw new InvalidOperationException("Older draft preparation JSON lost backward compatibility.");
        if (!StoryLiteraryCraft.GenreNuance(new StoryProject { CoreGenres = new() { "Slice of Life" } })
                .Contains("small changed habit", StringComparison.Ordinal) ||
            StoryLiteraryCraft.GenreNuance(new StoryProject()).Length != 0)
            throw new InvalidOperationException("Genre-sensitive literary guidance is not optional.");

        if (!StoryCraftFeedback.All.Contains(StoryCraftFeedback.StiffDialogue) ||
            !StoryCraftFeedback.Guidance(StoryCraftFeedback.OverExplaining).Contains("subtext", StringComparison.Ordinal) ||
            StoryCraftFeedback.Normalize("unrecognized feedback") != StoryCraftFeedback.None)
            throw new InvalidOperationException("New craft feedback guidance is not normalized safely.");

        if (!StoryLiteraryCraft.ForContinuityRepair.Contains("Fix only the specific grounded contradictions", StringComparison.Ordinal) ||
            !StoryLiteraryCraft.ForContinuityRepair.Contains("Preserve the narrator's original language", StringComparison.Ordinal) ||
            StoryLiteraryCraft.ForContinuityRepair.Contains("mandatory cliffhanger", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Continuity repair may replace literary prose instead of minimally correcting it.");

        const string repeatedStart = "The lantern shone on the narrow bridge where an old promise would be tested tonight";
        const string repeatedEnd = "and neither of them could pretend the familiar silence meant the same thing anymore";
        string prose = repeatedStart + ". They spent the afternoon talking carefully. " + repeatedEnd;
        StoryMessage Committed(int number, string content) => new()
        {
            Role = "assistant", IsNarrativeDraft = true, ManuscriptCommitted = true,
            ManuscriptChapterNumber = number, Content = content
        };
        var messages = new List<StoryMessage> { Committed(1, prose) };
        string hint = StoryCrossChapterStyleWatch.Build(messages, prose);
        if (!hint.Contains("first eleven words", StringComparison.Ordinal) ||
            !hint.Contains("last eleven words", StringComparison.Ordinal))
            throw new InvalidOperationException("Cross-chapter echo was not detected.");
        messages[0].ManuscriptCommitted = false;
        if (StoryCrossChapterStyleWatch.Build(messages, prose).Length != 0)
            throw new InvalidOperationException("Uncommitted draft leaked into the chapter style watch.");
        messages[0].ManuscriptCommitted = true;
        messages[0].IsInterruptedResponse = true;
        if (StoryCrossChapterStyleWatch.Build(messages, prose).Length != 0)
            throw new InvalidOperationException("Interrupted output leaked into the chapter style watch.");
        messages[0].IsInterruptedResponse = false;
        messages[0].DraftDiscarded = true;
        if (StoryCrossChapterStyleWatch.Build(messages, prose).Length != 0)
            throw new InvalidOperationException("Discarded draft leaked into the chapter style watch.");
        messages[0].DraftDiscarded = false;
        messages.Add(Committed(1, prose));
        if (StoryCrossChapterStyleWatch.Build(messages, prose).Length != 0)
            throw new InvalidOperationException("Ambiguous duplicated chapters leaked into the style watch.");
        messages.Clear();
        messages.Add(Committed(1, prose));
        if (StoryCrossChapterStyleWatch.Build(messages,
                "A new morning began with a different scene. The exchange ended very quietly.").Length != 0)
            throw new InvalidOperationException("Different prose triggered a copy-pattern false positive.");
        if (StoryCrossChapterStyleWatch.Build(messages, "A short line").Length != 0)
            throw new InvalidOperationException("Short prose triggered a copy-pattern false positive.");
    }
}
