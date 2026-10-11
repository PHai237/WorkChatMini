namespace WorkChatMini;

// Extended assertions inside the existing 22-case self-test suite.
internal static class StoryCharacterPrioritySelfTest
{
    internal static void Verify()
    {
        var memory = new StoryMemoryDocument();
        memory.Characters.Add(new StoryCharacterMemory
        {
            Name = "Magica", Aliases = new List<string> { "Silver Crow", "Watcher" },
            Goals = new List<string> { "Protect the city", "Recover the letter" }
        });
        memory.Characters.Add(new StoryCharacterMemory
        {
            Name = "Luna", Aliases = new List<string> { "Watcher" },
            Goals = new List<string> { "Find her brother" }
        });
        memory.Chapters.Add(new StoryChapterMemory { Number = 2, Title = "The vow" });
        memory.Chapters.Add(new StoryChapterMemory { Number = 8, Title = "The hard choice" });
        memory.Events.AddRange(new[]
        {
            new StorySignificantEvent { Id = "promise-a", Type = "PromiseMade",
                Subject = "Silver Crow", Target = "Return the letter to Luna",
                Summary = "Magica vowed to return the letter.", Evidence = "Magica swore on the gate.",
                Status = "Confirmed", ChapterNumber = 2 },
            new StorySignificantEvent { Id = "choice-a", Type = "DecisionMade",
                Subject = "Magica", Target = "Protect the city",
                Summary = "Magica refused to leave the city.", Evidence = "She closed the city gate.",
                Status = "Confirmed", ChapterNumber = 8 },
            new StorySignificantEvent { Id = "draft", Type = "PromiseBroken",
                Subject = "Magica", Target = "Return the letter to Luna",
                Summary = "A rejected alternate ending.", Evidence = "Draft text only.",
                Status = "Proposed", ChapterNumber = 8 },
            new StorySignificantEvent { Id = "ambiguous", Type = "DecisionMade",
                Subject = "Watcher", Target = "Secret option",
                Summary = "Unattributable Watcher decision.", Evidence = "Two people share Watcher.",
                Status = "Confirmed", ChapterNumber = 8 },
            new StorySignificantEvent { Id = "future", Type = "PromiseBroken",
                Subject = "Luna", Target = "Find her brother",
                Summary = "Uncommitted alternate branch.", Evidence = "Alternate text.",
                Status = "Confirmed", ChapterNumber = 40 }
        });
        string focus = StoryCharacterPriorityFocus.Build(memory);
        if (!focus.Contains("CURRENT goal: Protect the city", StringComparison.Ordinal) ||
            !focus.Contains("CHOICE Ch.8 DecisionMade", StringComparison.Ordinal) ||
            !focus.Contains("VOW RECORD Ch.2 PromiseMade", StringComparison.Ordinal) ||
            focus.Contains("rejected alternate", StringComparison.Ordinal) ||
            focus.Contains("Unattributable Watcher", StringComparison.Ordinal) ||
            focus.Contains("Uncommitted alternate", StringComparison.Ordinal) ||
            focus.Length > 1100)
            throw new InvalidOperationException("Recorded character priorities lost provenance or leaked another branch/identity.");

        // A completed vow can be mentioned historically, but must not still
        // be described as an unresolved PromiseMade.
        memory.Events.Add(new StorySignificantEvent
        {
            Id = "promise-result", Type = "PromiseKept", Subject = "Magica",
            Target = "Return the letter to Luna", Summary = "Magica returned the letter.",
            Evidence = "Luna took the letter from Magica.", Status = "Confirmed", ChapterNumber = 8
        });
        string resolved = StoryCharacterPriorityFocus.Build(memory);
        if (!resolved.Contains("PromiseKept", StringComparison.Ordinal) ||
            resolved.Contains("no later recorded outcome", StringComparison.Ordinal))
            throw new InvalidOperationException("The latest committed vow outcome was not respected.");

        memory.Events.Add(new StorySignificantEvent
        {
            Id = "promise-b", Type = "PromiseMade", Subject = "Magica",
            Target = "Guard the archive", Summary = "Magica promised to protect the archive.",
            Evidence = "Magica made the promise in front of Luna.",
            Status = "Confirmed", ChapterNumber = 2
        });
        string competing = StoryCharacterPriorityFocus.Build(memory);
        if (!competing.Contains("Guard the archive", StringComparison.Ordinal) ||
            competing.Contains("VOW RECORD Ch.8 PromiseKept", StringComparison.Ordinal))
            throw new InvalidOperationException("A resolved vow hid another independently unresolved promise.");

        // IDs are global identifiers. A collision with a Proposed record must
        // prevent trusting the corresponding Confirmed event.
        memory.Events.Add(new StorySignificantEvent
        {
            Id = "choice-a", Type = "DecisionMade", Subject = "Luna",
            Summary = "Duplicated ID in a Proposed record.", Status = "Proposed", ChapterNumber = 8
        });
        if (StoryCharacterPriorityFocus.Build(memory).Contains("CHOICE Ch.8 DecisionMade", StringComparison.Ordinal))
            throw new InvalidOperationException("A duplicate event ID was accepted as unique provenance.");
        memory.Events.RemoveAt(memory.Events.Count - 1);

        // A missing chapter removes both its committed events and any state
        // that would be wrongly extrapolated from those events.
        memory.Chapters.RemoveAll(c => c.Number == 8);
        if (StoryCharacterPriorityFocus.Build(memory).Contains("CHOICE Ch.8", StringComparison.Ordinal) ||
            StoryCharacterPriorityFocus.Build(memory).Contains("PromiseKept", StringComparison.Ordinal))
            throw new InvalidOperationException("Removed chapter leaked into character priorities.");
        memory.Chapters.Add(new StoryChapterMemory { Number = 8, Title = "Restored" });

        var contradictions = new StoryMemoryUpdate();
        contradictions.Events.Add(new StoryEventPatch
        {
            Type = "PromiseKept", Subject = "Silver Crow", Target = "Return the letter to Luna",
            Evidence = "The letter was returned.", Status = "Confirmed"
        });
        contradictions.Events.Add(new StoryEventPatch
        {
            Type = "PromiseBroken", Subject = "Magica", Target = "Return the letter to Luna",
            Evidence = "The letter was destroyed.", Status = "Confirmed"
        });
        contradictions.Characters.Add(new StoryCharacterPatch
        {
            Name = "Magica", Goals = new List<string> { "Recover the letter" },
            RemovedGoals = new List<string> { "Recover the letter" }
        });
        StoryDeterministicReport report = StoryDeterministicContinuity.ValidateUpdate(memory, contradictions);
        if (!report.Issues.Any(i => i.Code == "contradictory-vow-outcomes" &&
                                    i.Severity == StoryDeterministicSeverity.Risk) ||
            !report.Issues.Any(i => i.Code == "goal-readded-in-same-update" &&
                                    i.Severity == StoryDeterministicSeverity.Risk) ||
            report.HasConflict)
            throw new InvalidOperationException("Conflicting choice deltas were not review-only Risks.");
        contradictions.Events.RemoveAt(1);
        contradictions.Characters[0].Goals.Clear();
        StoryDeterministicReport clean = StoryDeterministicContinuity.ValidateUpdate(memory, contradictions);
        if (clean.Issues.Any(i => i.Code is "contradictory-vow-outcomes" or "goal-readded-in-same-update"))
            throw new InvalidOperationException("False conflicting-choice warning after reconciliation.");

        // A long-running story must still surface main arc + thread + setup
        // while including actual character evidence under the writer budget.
        memory.Arcs.Add(new StoryArcMemory { Title = "The First Arc", Status = "Active",
            Priority = "Core", Progress = "Protect the city while the army regroups." });
        memory.Threads.Add(new StoryPlotThread { Title = "Hidden passage", Status = "Open",
            Detail = "Find who left the map in the cathedral." });
        memory.NarrativePromises.Add(new StoryNarrativePromise { Title = "The bell", Kind = "Foreshadowing",
            Status = "Open", IntroducedChapter = 2, Detail = "The bronze bell has yet to ring." });
        string narrative = StoryNarrativeFocus.Build(memory);
        if (narrative.Length > StoryContextBudget.MaxTotalCharacters / 10 ||
            !narrative.Contains("The First Arc", StringComparison.Ordinal) ||
            !narrative.Contains("Hidden passage", StringComparison.Ordinal) ||
            !narrative.Contains("The bell", StringComparison.Ordinal) ||
            !narrative.Contains("CHARACTER PRIORITIES", StringComparison.Ordinal))
            throw new InvalidOperationException("The Narrative Focus budget starved arc, thread, setup or character priorities.");

        for (int chapter = 10; chapter < 25; chapter++)
        {
            memory.Arcs.Add(new StoryArcMemory
            {
                Title = "Very long background arc " + chapter,
                Status = "Active", Priority = "Minor",
                Progress = new string('x', 280)
            });
            memory.Threads.Add(new StoryPlotThread
            {
                Title = "Unresolved clue " + chapter, Status = "Open",
                Detail = new string('y', 280)
            });
        }
        string crowded = StoryNarrativeFocus.Build(memory);
        if (crowded.Length > 2380 ||
            !crowded.Contains("The First Arc", StringComparison.Ordinal) ||
            !crowded.Contains("CHARACTER PRIORITIES", StringComparison.Ordinal))
            throw new InvalidOperationException("A crowded story starved core arc or character evidence.");

        // A separate branch with no committed evidence never inherits the
        // active branch's decisions, even if character names are identical.
        var alternative = new StoryMemoryDocument();
        alternative.Characters.Add(new StoryCharacterMemory { Name = "Magica" });
        alternative.Chapters.Add(new StoryChapterMemory { Number = 2 });
        if (!string.IsNullOrEmpty(StoryCharacterPriorityFocus.Build(alternative)))
            throw new InvalidOperationException("Branch-local evidence leaked into an unrelated memory document.");
    }
}
