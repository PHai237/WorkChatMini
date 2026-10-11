namespace WorkChatMini;

public sealed class StoryDraftPreparationStage
{
    private readonly StoryAiStageRunner _runner = new();

    public async Task<StoryDraftPreparation> PrepareAsync(
        StoryProject project,
        string userInstruction,
        string recentConversation,
        string memoryContext,
        string researchContext,
        string providerId,
        string model,
        CancellationToken cancellationToken,
        bool continueStory = false)
    {
        string systemPrompt = StoryPromptBuilder.BuildBase(project, memoryContext, researchContext) + """

You are the private DRAFT PREPARATION stage. Do not write narrative prose.
In one pass, prepare both the working plan and continuity constraints for the writer.

PLAN must cover only what matters: objective, POV, participating characters/motivation, beats in order, conflict/emotional movement, reveals/foreshadowing, what must stay hidden, ending state or aftermath (a hook only when earned), and important memory/research facts.

CONTINUITY CONSTRAINTS must explicitly check character knowledge, scene location/travel, injuries/condition, power/progression, inventory, relationship history/current state, active character arcs, timeline, world rules, and open threads. Unknown information is not a contradiction. Do not invent canon to fill blanks.

Return ONE JSON object only, no markdown fence:
{"plan":"compact working plan","constraints":"compact continuity constraints"}
""" + (continueStory ? StoryNarrativeCraftGuide.ForPreparation : string.Empty);

        string input = $"""
CREATOR INSTRUCTION:
{userInstruction}

RECENT CONVERSATION:
{Trim(recentConversation, 10000)}
""";

        string raw = await _runner.CompleteAsync(
            providerId,
            model,
            systemPrompt,
            new[] { new StoryMessage { Role = "user", Content = input } },
            cancellationToken,
            temperature: 0.25,
            workload: StoryAiWorkload.Utility,
            stageName: "draft-preparation");

        StoryDraftPreparation? result = StoryJson.TryDeserialize<StoryDraftPreparation>(raw);
        if (result is not null && !string.IsNullOrWhiteSpace(result.Plan))
        {
            result.Plan = result.Plan.Trim();
            result.Constraints = string.IsNullOrWhiteSpace(result.Constraints)
                ? "Preserve confirmed Story Memory and the creator's explicit constraints. Treat unknown facts as unknown rather than inventing them."
                : result.Constraints.Trim();
            return result;
        }

        AppLogger.Warn("STORY-PIPELINE", "Draft preparation JSON was not parseable; using the returned notes as the plan.");
        return new StoryDraftPreparation
        {
            Plan = string.IsNullOrWhiteSpace(raw) ? "Follow the creator's instruction directly." : raw.Trim(),
            Constraints = "Preserve confirmed Story Memory and the creator's explicit constraints. Treat unknown facts as unknown rather than inventing them."
        };
    }

    private static string Trim(string value, int max) => value.Length <= max ? value : value[..max] + "…";
}
