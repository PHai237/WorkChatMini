namespace WorkChatMini;

// Offline acceptance gate for the highest-risk context boundaries. This runs
// inside the existing Quick/Stress Story Memory case; it never calls Gemini,
// creates a user project, or mutates on-disk Story data.
internal static class StoryReadinessSelfTest
{
    internal static void Verify()
    {
        StoryMemoryRecoverySelfTest.Verify();
        StoryMemoryRepairSelfTest.Verify();
        StoryMemoryCoverageSelfTest.Verify();
        StoryEvidenceAuditSelfTest.Verify();
        var memory = new StoryMemoryDocument();
        for (int n = 1; n <= 20; n++)
            memory.Chapters.Add(new StoryChapterMemory
            {
                Number = n,
                Title = "Committed chapter " + n,
                Summary = n == 17 ? "The blue crescent was hidden under the eastern bridge."
                    : "A unique and approved scene about the harbor at chapter " + n
            });
        // One duplicated chapter number is ambiguous, even if both stored
        // summaries look convincing to a lexical retriever.
        memory.Chapters.Add(new StoryChapterMemory
        {
            Number = 12, Title = "Conflicting manuscript",
            Summary = "The false pearl appeared during the vanished ritual."
        });
        StoryRetrievalPlan legit = StoryRetrievalPlanner.Build(memory, "blue crescent");
        if (!legit.ChapterBoosts.ContainsKey(17))
            throw new InvalidOperationException("A committed chapter was not retrieved.");
        StoryRetrievalPlan duplicate = StoryRetrievalPlanner.Build(memory, "false pearl");
        if (duplicate.ChapterBoosts.ContainsKey(12) ||
            duplicate.FocusReasons.Any(x => x.Contains("Chapter 12", StringComparison.Ordinal)))
            throw new InvalidOperationException("Duplicate chapter summaries were treated as evidence.");

        memory.Canon.Add(new StoryCanonFact
        {
            Fact = "A phantom lantern opens the forbidden passage.",
            Source = "Chapter 9", Status = "Proposed"
        });
        StoryRetrievalPlan proposed = StoryRetrievalPlanner.Build(memory, "phantom lantern");
        if (proposed.ChapterBoosts.ContainsKey(9) ||
            proposed.ExpandedQuery.Contains("forbidden passage", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Proposed canon promoted a chapter or expanded proven facts.");
        memory.Canon[0].Status = "Confirmed";
        StoryRetrievalPlan confirmed = StoryRetrievalPlanner.Build(memory, "phantom lantern");
        if (!confirmed.ChapterBoosts.ContainsKey(9))
            throw new InvalidOperationException("Confirmed canon lost a valid provenance boost.");

        // A stale chapter reference may occur in a thread/arc even after a
        // rollback. No expansion path may leave an uncommitted chapter boost.
        memory.Threads.Add(new StoryPlotThread
        {
            Title = "The missing observatory", Detail = "An old sea chart points north.",
            Status = "Open", OpenedChapter = 21, LastTouchedChapter = 22
        });
        StoryRetrievalPlan orphan = StoryRetrievalPlanner.Build(memory, "missing observatory");
        if (orphan.ChapterBoosts.ContainsKey(21) || orphan.ChapterBoosts.ContainsKey(22))
            throw new InvalidOperationException("Stale chapter provenance survived the final retrieval guard.");

        string budgeted = StoryContextBudget.Compose(new string('x', 35000),
            "CRITICAL CURRENT STATE", "CURRENT POV IN CLOCKTOWER", "", "");
        if (budgeted.Length > StoryContextBudget.MaxTotalCharacters ||
            !budgeted.Contains("CRITICAL CURRENT STATE", StringComparison.Ordinal) ||
            !budgeted.Contains("CURRENT POV IN CLOCKTOWER", StringComparison.Ordinal))
            throw new InvalidOperationException("Long history displaced current story state or exceeded budget.");

        var turns = new List<StoryMessage>
        {
            new() { Role = "user", Action = "Chat", Content = "Keep the ending quiet." },
            new() { Role = "assistant", Action = "Chat", Content = "A quiet ending can work." },
            new() { Role = "user", Action = "ContinueStory", Content = "Rejected direction" },
            new() { Role = "assistant", Action = "ContinueStory", Content = "Unapproved manuscript",
                IsNarrativeDraft = true, ManuscriptCommitted = false },
            new() { Role = "user", Action = "Chat", Content = "Please reconsider the pacing." }
        };
        string context = StoryConversationContextBuilder.Build(turns, "Fresh instruction");
        if (context.Contains("Rejected direction", StringComparison.Ordinal) ||
            context.Contains("Unapproved manuscript", StringComparison.Ordinal) ||
            !context.Contains("ending quiet", StringComparison.Ordinal) ||
            !context.Contains("reconsider the pacing", StringComparison.Ordinal))
            throw new InvalidOperationException("Rejected drafts leaked or approved chat history was lost.");
        if (StoryActions.MayAutoBookkeep(StoryActionType.Chat) ||
            StoryManuscriptModes.Normalize(StoryManuscriptModes.ReviewBeforeCommit) !=
                StoryManuscriptModes.ReviewBeforeCommit)
            throw new InvalidOperationException("Chat/Review workflow safety boundary changed.");
    }
}
