namespace WorkChatMini;

// Runs within the existing Story memory/retrieval case; no extra top-level
// self-test, no Gemini network call and no durable user data touched.
internal static class StoryNarrativeQualitySelfTest
{
    internal static void Verify()
    {
        StoryRelationshipContinuitySelfTest.Verify();
        StoryLongContinuityAuditSelfTest.Verify();
        StoryReadinessSelfTest.Verify();
        var memory = new StoryMemoryDocument();
        for (int n = 1; n <= 5; n++)
            memory.Chapters.Add(new StoryChapterMemory { Number = n,
                Summary = $"Chapter {n} progresses an established situation." });
        memory.Characters.Add(new StoryCharacterMemory
        {
            Name = "Magica", Aliases = new() { "Watcher" },
            Personality = "Patient, thoughtful, learning to trust others"
        });
        memory.Characters.Add(new StoryCharacterMemory
        {
            Name = "Luna", Aliases = new() { "Watcher" },
            Personality = "Blunt but protective"
        });
        memory.CharacterArcs.Add(new StoryCharacterArcMemory
        {
            Character = "Magica", Title = "Learning to trust", Status = "Active",
            Stage = "Guarded after betrayal", LastChapter = 4
        });
        memory.SceneStates.Add(new StorySceneStateMemory
        {
            ChapterNumber = 5, PovCharacter = "Magica",
            PresentCharacters = new() { "Watcher", "Luna" }
        });

        var cast = StoryCharacterVoiceFocus.Build(memory);
        if (cast.Count != 2 || !cast[0].Contains("Magica (POV)", StringComparison.Ordinal) ||
            !cast[0].Contains("Guarded after betrayal", StringComparison.Ordinal) ||
            !cast[1].Contains("Luna", StringComparison.Ordinal) ||
            cast.Any(s => s.Contains("Watcher", StringComparison.Ordinal)))
            throw new InvalidOperationException("Voice anchors guessed a shared nickname or lost a committed arc.");

        for (int n = 1; n <= 3; n++)
            memory.Events.Add(NewEvent($"real-{n}", n, "SecretRevealed", "Magica"));
        memory.Events.Add(NewEvent("proposed", 4, "SecretRevealed", "Luna", "Proposed"));
        memory.Events.Add(NewEvent("uncommitted", 6, "SecretRevealed", "Magica"));
        memory.Events.Add(NewEvent("ambiguous", 4, "SecretRevealed", "Watcher"));
        memory.Events.Add(NewEvent("duplicate", 4, "SecretRevealed", "Magica"));
        memory.Events.Add(NewEvent("duplicate", 5, "SecretRevealed", "Magica"));

        string[] watch = StoryPlotPatternFocus.Build(memory).ToArray();
        if (watch.Length != 1 || !watch[0].Contains("revelations", StringComparison.Ordinal) ||
            !watch[0].Contains("1, 2, 3", StringComparison.Ordinal) ||
            watch[0].Contains("4,", StringComparison.Ordinal) ||
            watch[0].Contains("6", StringComparison.Ordinal))
            throw new InvalidOperationException("Plot-pattern watch used uncommitted, ambiguous or unverified events.");

        string focus = StoryNarrativeFocus.Build(memory);
        if (!focus.Contains("LAST COMMITTED CAST", StringComparison.Ordinal) ||
            !focus.Contains("REPEATED PLOT SHAPE", StringComparison.Ordinal) ||
            focus.Length > 2380)
            throw new InvalidOperationException("The new craft hints were dropped or exceeded the Narrative Focus budget.");

        // No false structural-alarm for only two qualifying chapters.
        memory.Events[2].Status = "Proposed";
        if (StoryPlotPatternFocus.Build(memory).Count != 0)
            throw new InvalidOperationException("Two chapters were incorrectly treated as a repetitive pattern.");
        memory.Events[2].Status = "Confirmed";
        memory.Chapters.Add(new StoryChapterMemory { Number = 3, Summary = "Ambiguous duplicate" });
        if (StoryPlotPatternFocus.Build(memory).Count != 0)
            throw new InvalidOperationException("A duplicated chapter number was treated as evidence.");

        // A chapter number duplicated in history cannot establish a single
        // spatial truth, either.
        memory.SceneStates.Clear();
        memory.SceneStates.Add(new StorySceneStateMemory
        {
            ChapterNumber = 3, PovCharacter = "Magica", Location = "Contradictory room"
        });
        if (StoryNarrativeFocus.Build(memory).Contains("Contradictory room", StringComparison.Ordinal))
            throw new InvalidOperationException("Ambiguous chapter state leaked into scene handoff.");

        // A scene from an uncommitted later chapter cannot drive character voice.
        memory.SceneStates.Clear();
        memory.SceneStates.Add(new StorySceneStateMemory
        {
            ChapterNumber = 10, PovCharacter = "Magica"
        });
        if (StoryCharacterVoiceFocus.Build(memory).Count != 0)
            throw new InvalidOperationException("Uncommitted scene leaked into voice guidance.");
        memory.Chapters.Clear();
        if (StoryPlotPatternFocus.Build(memory).Count != 0 ||
            StoryCharacterVoiceFocus.Build(memory).Count != 0)
            throw new InvalidOperationException("Uncommitted data leaked into new Narrative Quality hints.");

        // Long repeated paragraph tails are an advisory, never a rewrite.
        string tail = "the same shadow followed them through the empty courtyard.";
        string prose = string.Join("\n\n", Enumerable.Range(1, 3).Select(n =>
            $"They made a different choice in this very detailed and purposeful passage of chapter {n} " +
            "and took time to discuss its meaning. Still, " + tail));
        if (!StoryProseRepetitionHints.Analyze(prose).Contains("same six words", StringComparison.Ordinal))
            throw new InvalidOperationException("Long repeated paragraph endings were missed by the editor advisory.");
        if (StoryProseRepetitionHints.Analyze("A quiet pause between two friends.").Length != 0)
            throw new InvalidOperationException("Short scene prose generated a false alarm.");
    }

    private static StorySignificantEvent NewEvent(string id, int chapter, string type,
        string subject, string status = "Confirmed") => new()
    {
        Id = id, ChapterNumber = chapter, Type = type, Subject = subject,
        Status = status, Evidence = "The event is on the page.",
        Summary = "A new clue was revealed to the cast."
    };
}
