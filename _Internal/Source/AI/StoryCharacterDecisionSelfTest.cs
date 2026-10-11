namespace WorkChatMini;

// Part of the existing Story memory/retrieval case, not a new top-level test.
internal static class StoryCharacterDecisionSelfTest
{
    internal static void Verify()
    {
        var memory = new StoryMemoryDocument();
        memory.Characters.Add(new StoryCharacterMemory
        {
            Name = "Magica", Aliases = new List<string> { "Silver Crow", "Watcher" }
        });
        memory.Characters.Add(new StoryCharacterMemory
        {
            Name = "Luna", Aliases = new List<string> { "Watcher" }
        });
        var ch3 = new StoryChapterMemory { Number = 3, Title = "First trust" };
        var ch9 = new StoryChapterMemory { Number = 9, Title = "The betrayal" };
        memory.Chapters.Add(ch3);
        memory.Chapters.Add(ch9);

        StoryRelationshipLedger.Merge(memory, new[]
        {
            new StoryRelationshipPatch { From = "Magica", To = "Luna", State = "Trusting", Change = "They agreed to guard the map together." }
        }, "Ch.3", ch3);
        StoryRelationshipLedger.Merge(memory, new[]
        {
            new StoryRelationshipPatch { From = "Silver Crow", To = "Luna", State = "Guarded", Change = "Luna betrayed Magica at the gate." }
        }, "Ch.9", ch9);
        if (memory.Relationships.Count != 1 || memory.RelationshipHistory.Count != 2 ||
            memory.Relationships[0].From != "Magica" || memory.Relationships[0].State != "Guarded" ||
            memory.RelationshipHistory[1].BeforeState != "Trusting")
            throw new InvalidOperationException("Unique alias created a duplicate relationship or lost previous state.");
        StoryRelationshipLedger.Merge(memory, new[]
        {
            new StoryRelationshipPatch { From = "Silver Crow", To = "Luna", State = "Guarded", Change = "Luna betrayed Magica at the gate." }
        }, "Ch.9", ch9);
        if (memory.RelationshipHistory.Count != 2)
            throw new InvalidOperationException("Repeated bookkeeping duplicated relationship provenance.");

        // Shared aliases and two labels for the same person are not two actors.
        StoryRelationshipLedger.Merge(memory, new[]
        {
            new StoryRelationshipPatch { From = "Watcher", To = "Luna", State = "Enemies", Change = "Ambiguous actor" },
            new StoryRelationshipPatch { From = "Silver Crow", To = "Magica", State = "Friends", Change = "Same actor" }
        }, "Ch.9", ch9);
        if (memory.Relationships.Count != 1 || memory.RelationshipHistory.Count != 2)
            throw new InvalidOperationException("Ambiguous or self-relationship mutated durable memory.");

        var ambiguous = new StoryMemoryUpdate { Relationships = new List<StoryRelationshipPatch>
        {
            new() { From = "Watcher", To = "Luna", State = "Enemies" }
        } };
        if (!StoryDeterministicContinuity.ValidateUpdate(memory, ambiguous).Issues.Any(x =>
                x.Code == "ambiguous-or-self-relationship-identity" &&
                x.Severity == StoryDeterministicSeverity.Risk))
            throw new InvalidOperationException("Shared alias did not produce an identity risk.");
        var unexplained = new StoryMemoryUpdate { Relationships = new List<StoryRelationshipPatch>
        {
            new() { From = "Silver Crow", To = "Luna", State = "Trusting" }
        } };
        if (!StoryDeterministicContinuity.ValidateUpdate(memory, unexplained).Issues.Any(x =>
                x.Code == "relationship-shift-missing-evidence" &&
                x.Severity == StoryDeterministicSeverity.Risk))
            throw new InvalidOperationException("Unexplained relationship reversal was not flagged as risk.");
        unexplained.Relationships[0].Change = "Luna returned the map and apologized.";
        if (StoryDeterministicContinuity.ValidateUpdate(memory, unexplained).Issues.Any(x =>
                x.Code == "relationship-shift-missing-evidence"))
            throw new InvalidOperationException("An explicitly explained reversal was falsely flagged.");

        // A legacy record that used a unique alias must be upgraded in place,
        // rather than producing a second canonical relationship row.
        var legacy = new StoryMemoryDocument();
        legacy.Characters.AddRange(memory.Characters);
        legacy.Chapters.Add(ch3);
        legacy.Chapters.Add(ch9);
        legacy.Relationships.Add(new StoryRelationshipMemory
        {
            From = "Silver Crow", To = "Luna", State = "Trusting"
        });
        StoryRelationshipLedger.Merge(legacy, new[]
        {
            new StoryRelationshipPatch { From = "Magica", To = "Luna", State = "Guarded", Change = "Luna broke her promise." }
        }, "Ch.9", ch9);
        if (legacy.Relationships.Count != 1 || legacy.Relationships[0].From != "Magica" ||
            legacy.RelationshipHistory.Count != 1 || legacy.RelationshipHistory[0].BeforeState != "Trusting")
            throw new InvalidOperationException("Legacy alias relationship was not resolved without duplication.");

        legacy.Relationships.Add(new StoryRelationshipMemory
        {
            From = "Silver Crow", To = "Luna", State = "Hostile"
        });
        if (!StoryDeterministicContinuity.AnalyzeMemory(legacy).Issues.Any(x =>
                x.Code == "relationship-current-state-conflict" &&
                x.Severity == StoryDeterministicSeverity.Conflict))
            throw new InvalidOperationException("Contradictory legacy alias/canonical relationships were not detected.");

        var invalidSameActor = new StoryMemoryUpdate { Relationships = new List<StoryRelationshipPatch>
        {
            new() { From = "Silver Crow", To = "Magica", State = "Friends" }
        } };
        StoryDeterministicContinuity.SanitizeUnsafeUpdate(memory, invalidSameActor);
        if (invalidSameActor.Relationships.Count != 0)
            throw new InvalidOperationException("Sanitization retained a self-relationship disguised by an alias.");

        StoryCharacterArcLedger.Merge(memory, new[]
        {
            new StoryCharacterArcPatch { Character = "Magica", Title = "Learning to trust", Stage = "Guarded", Direction = "Regressing", LastChange = "Magica withdrew after Luna's betrayal." }
        }, "Ch.9", ch9);
        string focus = StoryNarrativeFocus.Build(memory);
        if (!focus.Contains("CHARACTER DECISIONS", StringComparison.Ordinal) ||
            !focus.Contains("turning point Ch.9", StringComparison.Ordinal) ||
            !focus.Contains("relationship Ch.9", StringComparison.Ordinal) ||
            !focus.Contains("Luna betrayed Magica", StringComparison.Ordinal))
            throw new InvalidOperationException("Continue Story lost the joined character/relationship evidence.");

        var draftOnly = new StoryMemoryDocument();
        draftOnly.Characters.AddRange(memory.Characters);
        draftOnly.CharacterDevelopmentHistory.Add(new StoryCharacterDevelopmentChange
        {
            Character = "Magica", ArcTitle = "Uncommitted", ChapterNumber = 10,
            Evidence = "Uncommitted alternate draft made Magica forgive Luna."
        });
        draftOnly.RelationshipHistory.Add(new StoryRelationshipChange
        {
            From = "Magica", To = "Luna", ChapterNumber = 10,
            Change = "Uncommitted reconciliation"
        });
        if (StoryCharacterDecisionFocus.Build(draftOnly).Contains("Uncommitted", StringComparison.Ordinal))
            throw new InvalidOperationException("An uncommitted branch draft leaked into decision context.");
    }
}
