namespace WorkChatMini;

internal static class StoryDirectorSelfTest
{
    internal static void Verify()
    {
        var oldStory = new StoryProject();
        if (StoryDirector.Normalize(oldStory.StoryDirectorMode) != StoryDirector.Off ||
            StoryDirector.Normalize("unexpected") != StoryDirector.Off ||
            StoryDirector.Normalize(StoryDirector.Review) != StoryDirector.Review)
            throw new InvalidOperationException("Story Director changed defaults for existing projects.");
        string plain = StoryDirector.ComposeInstruction("Continue.", string.Empty);
        if (plain != "Continue.")
            throw new InvalidOperationException("Story Director modified an unreviewed continuation.");
        string composed = StoryDirector.ComposeInstruction("Continue.", "Let the friends talk after the storm.");
        if (!composed.Contains("CREATOR-APPROVED STORY DIRECTOR", StringComparison.Ordinal) ||
            !composed.Contains("Let the friends talk", StringComparison.Ordinal) ||
            !composed.Contains("NOT events", StringComparison.Ordinal))
            throw new InvalidOperationException("Reviewed direction was not distinguished from canon.");
        string huge = StoryDirector.ComposeInstruction("Continue.", new string('A', 12000));
        if (huge.Length > 3100 || !huge.Contains(new string('A', 2000), StringComparison.Ordinal))
            throw new InvalidOperationException("Director direction context budget was not enforced.");
        if (!StoryDirector.IsRoutineCreatorDirection(StoryDirector.ComposeInstruction("Continue the story.", "Let the characters speak about their loss.")) ||
            StoryDirector.IsRoutineCreatorDirection(StoryDirector.ComposeInstruction("Medieval medicine must be accurate", "Show a doctor responding to the injuries.")))
            throw new InvalidOperationException("Story Director changed research routing for genuine creator requests.");
        var steeringProject = new StoryProject { NarrativeFocusPreference = StoryNarrativeSteering.GroundedPlot,
            NarrativeGuardrails = "No arbitrary new factions" };
        string steering = StoryNarrativeSteering.Guidance(steeringProject);
        if (!steering.Contains("No arbitrary new factions", StringComparison.Ordinal) ||
            !steering.Contains("NOT confirmed canon", StringComparison.Ordinal))
            throw new InvalidOperationException("Creative steering was not safely included in prompt context.");
        if (StoryDirector.IsValidOption("too short") || !StoryDirector.IsValidOption("A grounded consequence from the previous chapter."))
            throw new InvalidOperationException("Director option validation accepted an empty plan.");
        string json = System.Text.Json.JsonSerializer.Serialize(new StoryProject { StoryDirectorMode = StoryDirector.Review });
        StoryProject? restored = System.Text.Json.JsonSerializer.Deserialize<StoryProject>(json);
        if (restored?.StoryDirectorMode != StoryDirector.Review)
            throw new InvalidOperationException("Per-Story Director setting did not survive persistence.");
        // Director is only an optional presentation gate before Continue Story.
        if (!StoryActions.MayAutoBookkeep(StoryActionType.ContinueStory) ||
            StoryActions.UsesWritingPipeline(StoryActionType.Chat) ||
            StoryActions.MayAutoBookkeep(StoryActionType.Chat) ||
            StoryActions.MayAutoBookkeep(StoryActionType.PlanChapter))
            throw new InvalidOperationException("Director or Chat was promoted into chapter bookkeeping.");
    }
}
