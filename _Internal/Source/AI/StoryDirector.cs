namespace WorkChatMini;

// Optional creator review before Continue Story. Proposals are transient editorial
// choices, never events or confirmed Story Memory. Old projects default to Off.
internal static class StoryDirector
{
    internal const int MaxDirectionCharacters = 2400;
    internal const string Off = "Off";
    internal const string Review = "Review";
    internal static readonly IReadOnlyList<string> Modes = new[] { Off, Review };

    internal static string Normalize(string? mode) => mode == Review ? Review : Off;

    internal static string ComposeInstruction(string instruction, string approvedDirection)
    {
        string text = string.IsNullOrWhiteSpace(instruction) ? "Continue the story." : instruction.Trim();
        string direction = approvedDirection.Trim();
        if (direction.Length == 0) return text;
        if (direction.Length > MaxDirectionCharacters)
            direction = direction[..MaxDirectionCharacters];
        return $"""
{text}

CREATOR-APPROVED STORY DIRECTOR DIRECTION (planning guidance, NOT events that already happened):
{direction}

Follow this chosen direction for the upcoming chapter while respecting confirmed canon. Invent only fitting connective details, dialogue and plausible consequences. Do not treat alternative plans as events or force a random twist. If the direction conflicts with confirmed facts, preserve confirmed facts and adapt the direction without retconning.
""";
    }

    internal static bool IsRoutineCreatorDirection(string input)
    {
        int marker = input.IndexOf("CREATOR-APPROVED STORY DIRECTOR DIRECTION", StringComparison.Ordinal);
        string original = marker >= 0 ? input[..marker].Trim() : input;
        return StoryNarrativeCraftGuide.IsRoutineContinuation(original);
    }

    internal static bool IsValidOption(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Trim().Length >= 20;
}

// A creator's ongoing craft preference is neither a story event nor a canonical fact.
internal static class StoryNarrativeSteering
{
    internal const string Balanced = "Balanced";
    internal const string CharacterDepth = "Character depth";
    internal const string GroundedPlot = "Grounded plot";
    internal const string SlowerPace = "Slower pacing";
    internal static readonly IReadOnlyList<string> All = new[]
        { Balanced, CharacterDepth, GroundedPlot, SlowerPace };

    internal static string Normalize(string? value) =>
        value is CharacterDepth or GroundedPlot or SlowerPace ? value : Balanced;

    internal static string Guidance(StoryProject project)
    {
        string focus = Normalize(project.NarrativeFocusPreference) switch
        {
            CharacterDepth => "Prioritize character agency, evolving relationships and emotional consequences over plot twists. Avoid empty introspection: show change through choices.",
            GroundedPlot => "Favor causal follow-through and existing threads over introducing unrelated mysteries, powers or factions. Surprise must grow from established setup.",
            SlowerPace => "Give important decisions, aftermath, intimacy and recovery room to breathe. A quiet chapter can move relationships forward; do not create crisis by default.",
            _ => "Balance forward motion, relationship development, consequences and quiet scenes according to this story's genre and creator direction."
        };
        string guardrails = (project.NarrativeGuardrails ?? string.Empty).Trim();
        if (guardrails.Length > 480) guardrails = guardrails[..480];
        return "CREATOR'S NARRATIVE STEERING (editorial preference, NOT confirmed canon):\n" + focus +
            (guardrails.Length > 0 ? "\nCurrent avoid/prefer notes: " + guardrails : string.Empty);
    }
}

internal sealed class StoryDirectorProposal
{
    public string OptionA { get; set; } = string.Empty;
    public string OptionB { get; set; } = string.Empty;
    public string OptionC { get; set; } = string.Empty;
}

internal sealed class StoryDirectorPlanner
{
    private readonly StoryAiStageRunner _runner = new();

    internal async Task<StoryDirectorProposal> PreviewAsync(
        StoryProject project, string creatorInstruction, string memoryContext,
        string provider, string model, CancellationToken cancellationToken)
    {
        string system = StoryPromptBuilder.BuildBase(project, memoryContext) + """

You are STORY DIRECTOR, an optional private planning collaborator, NOT the prose writer.
Propose exactly three distinct plausible NEXT-CHAPTER DIRECTIONS based on confirmed chapters, active character motives, pending consequences and creator-approved working directions.
A: grounded continuation of the most relevant existing relationships or conflicts.
B: a meaningful but different emotional/character angle, without arbitrary escalation.
C: a fitting creative alternative; surprising only when it grows from existing setup.
Each option should be 2–4 sentences: what happens, why these characters choose it, the emotional change and what remains unresolved. A quiet chapter is valid. Do not repeatedly introduce strangers, secret factions, sudden powers, deaths or cliffhangers just to make it interesting.
The creator will select or edit ONE option. Do not invent established facts, portray proposals as canon, or write chapter prose.
Return JSON ONLY: {"optionA":"...","optionB":"...","optionC":"..."}
""";
        string request = "CREATOR'S CURRENT DIRECTION:\n" +
            (string.IsNullOrWhiteSpace(creatorInstruction) ? "Continue naturally from the last committed chapter." : creatorInstruction.Trim());
        string raw = await _runner.CompleteAsync(provider, model, system,
            new[] { new StoryMessage { Role = "user", Content = request } },
            cancellationToken, temperature: 0.4, workload: StoryAiWorkload.Utility,
            stageName: "story-director-preview");
        StoryDirectorProposal? proposal = StoryJson.TryDeserialize<StoryDirectorProposal>(raw);
        if (proposal is null || !StoryDirector.IsValidOption(proposal.OptionA) ||
            !StoryDirector.IsValidOption(proposal.OptionB) || !StoryDirector.IsValidOption(proposal.OptionC))
            throw new InvalidOperationException("Story Director could not produce three valid directions. No chapter or canon was changed; try again or choose Write freely.");
        proposal.OptionA = proposal.OptionA.Trim()[..Math.Min(proposal.OptionA.Trim().Length, StoryDirector.MaxDirectionCharacters)];
        proposal.OptionB = proposal.OptionB.Trim()[..Math.Min(proposal.OptionB.Trim().Length, StoryDirector.MaxDirectionCharacters)];
        proposal.OptionC = proposal.OptionC.Trim()[..Math.Min(proposal.OptionC.Trim().Length, StoryDirector.MaxDirectionCharacters)];
        return proposal;
    }
}
