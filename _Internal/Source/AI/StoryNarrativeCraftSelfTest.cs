namespace WorkChatMini;

// Embedded into the existing Story memory test; no new top-level test case.
internal static class StoryNarrativeCraftSelfTest
{
    internal static void Verify()
    {
        StoryDirectorSelfTest.Verify();
        StorySceneCompassSelfTest.Verify();
        StoryLiteraryCraftSelfTest.Verify();
        if (!StoryNarrativeCraftGuide.IsRoutineContinuation("tiếp tục") ||
            !StoryNarrativeCraftGuide.IsRoutineContinuation("  Continue! ") ||
            !StoryNarrativeCraftGuide.IsRoutineContinuation(string.Empty) ||
            StoryNarrativeCraftGuide.IsRoutineContinuation("tiếp tục ở Tokyo thời Edo") ||
            StoryNarrativeCraftGuide.IsRoutineContinuation("continue with realistic medieval medicine"))
            throw new InvalidOperationException("Routine continuation incorrectly bypasses external research routing.");

        var memory = new StoryMemoryDocument();
        memory.Chapters.Add(new StoryChapterMemory { Number = 1, Summary = "A quieter conversation changes their trust." });
        memory.Chapters.Add(new StoryChapterMemory { Number = 2, Summary = "The village faces a sudden storm." });
        memory.Chapters.Add(new StoryChapterMemory { Number = 3, Summary = "They deal with the flood's aftermath." });
        memory.Chapters.Add(new StoryChapterMemory { Number = 4, Summary = "A scene from an abandoned draft." });
        memory.Chapters.Add(new StoryChapterMemory { Number = 4, Summary = "An ambiguous duplicate chapter number." });
        memory.Characters.Add(new StoryCharacterMemory { Name = "Magica", Aliases = new() { "Watcher" } });
        memory.Characters.Add(new StoryCharacterMemory { Name = "Luna", Aliases = new() { "Watcher" } });
        memory.Events.Add(new StorySignificantEvent
        {
            Id = "consequence-ambiguous", Type = "Betrayal", Status = "Confirmed",
            ChapterNumber = 3, Subject = "Watcher", Summary = "An ambiguous nickname event",
            Evidence = "Recorded but actor unknown"
        });
        memory.Events.Add(new StorySignificantEvent
        {
            Id = "consequence-valid", Type = "RelationshipChanged", Status = "Confirmed",
            ChapterNumber = 3, Summary = "Magica chose to speak truthfully to Luna.",
            Evidence = "Magica confessed the truth directly."
        });
        memory.Events.Add(new StorySignificantEvent
        {
            Id = "consequence-proposed", Type = "Betrayal", Status = "Proposed",
            ChapterNumber = 3, Summary = "A fabricated betrayal", Evidence = "Uncertain"
        });
        memory.Events.Add(new StorySignificantEvent
        {
            Id = "consequence-no-evidence", Type = "Injury", Status = "Confirmed",
            ChapterNumber = 2, Summary = "An unsupported injury"
        });
        memory.Events.Add(new StorySignificantEvent
        {
            Id = "consequence-not-committed", Type = "Death", Status = "Confirmed",
            ChapterNumber = 5, Summary = "An uncommitted death", Evidence = "Draft only"
        });
        memory.Events.Add(new StorySignificantEvent
        {
            Id = "consequence-duplicate", Type = "ItemLost", Status = "Confirmed",
            ChapterNumber = 3, Summary = "A duplicate-id item", Evidence = "Recorded"
        });
        memory.Events.Add(new StorySignificantEvent
        {
            Id = "consequence-duplicate", Type = "Injury", Status = "Confirmed",
            ChapterNumber = 2, Summary = "Another duplicate-id event", Evidence = "Recorded"
        });
        string focus = StoryNarrativeFocus.Build(memory);
        if (!focus.Contains("RECENT CONSEQUENCES", StringComparison.Ordinal) ||
            !focus.Contains("Magica chose to speak truthfully", StringComparison.Ordinal) ||
            focus.Contains("fabricated betrayal", StringComparison.OrdinalIgnoreCase) ||
            focus.Contains("unsupported injury", StringComparison.OrdinalIgnoreCase) ||
            focus.Contains("uncommitted death", StringComparison.OrdinalIgnoreCase) ||
            focus.Contains("duplicate-id", StringComparison.OrdinalIgnoreCase) ||
            focus.Contains("ambiguous nickname", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Narrative consequence evidence included an unverified or ambiguous event.");
        if (!focus.Contains("RECENT COMMITTED BEATS", StringComparison.Ordinal) ||
            !focus.Contains("Ch.3:", StringComparison.Ordinal) ||
            !focus.Contains("Ch.2:", StringComparison.Ordinal) ||
            focus.Contains("abandoned draft", StringComparison.OrdinalIgnoreCase) ||
            focus.Contains("Ch.1:", StringComparison.Ordinal))
            throw new InvalidOperationException("Narrative craft evidence included an ambiguous/unwanted chapter or lost recent beats.");
        if (focus.Length > 2380)
            throw new InvalidOperationException("Narrative craft evidence broke the writer context budget.");

        // There must be no historical context if no chapter was committed on
        // this branch, even if dangling events exist in the structured ledger.
        memory.Chapters.Clear();
        memory.Events.Add(new StorySignificantEvent { ChapterNumber = 5, Summary = "Uncommitted twist" });
        if (StoryNarrativeCraftGuide.RecentBeatEvidence(memory).Count != 0 ||
            StoryRecentConsequenceFocus.Build(memory).Count != 0)
            throw new InvalidOperationException("Narrative craft leaked uncommitted events into pacing history.");

        string repeating = string.Join("\n\n", Enumerable.Repeat(
            "The old wooden door opened without a sound and brought the same cold wind into the silent chamber.", 4));
        if (!StoryProseRepetitionHints.Analyze(repeating).Contains("repeats verbatim", StringComparison.Ordinal))
            throw new InvalidOperationException("Editor diagnostic missed deliberate test repetition.");
        string repeatedDialogue = string.Join("\n", Enumerable.Repeat(
            "“I will not open the gate tonight,” Luna replied.", 3));
        repeatedDialogue += new string(' ', 280);
        if (!StoryProseRepetitionHints.Analyze(repeatedDialogue).Contains("dialogue line", StringComparison.Ordinal))
            throw new InvalidOperationException("Editor diagnostic missed exact repeated dialogue.");
        string repeatedSentence = "A very long sentence describes the lantern's reflection and the meaning of a quiet choice for both characters in the room.";
        string withinOneParagraph = string.Join(" ", Enumerable.Repeat(repeatedSentence, 3));
        if (!StoryProseRepetitionHints.Analyze(withinOneParagraph).Contains("long sentence repeats", StringComparison.Ordinal))
            throw new InvalidOperationException("Prose diagnostics missed a repeated long sentence inside one paragraph.");
        if (StoryProseRepetitionHints.Analyze("A short scene is quiet.").Length != 0)
            throw new InvalidOperationException("Editor diagnostic flagged a short quiet scene.");
        if (StoryNarrativeCraftGuide.ForPreparation.Contains("cliffhanger every", StringComparison.OrdinalIgnoreCase) ||
            !StoryNarrativeCraftGuide.ForPreparation.Contains("surprise ending is optional", StringComparison.OrdinalIgnoreCase) ||
            !StoryNarrativeCraftGuide.ForEditor.Contains("do not insert a mandatory hook", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Narrative craft reverted to mandatory cliffhangers.");
    }
}
