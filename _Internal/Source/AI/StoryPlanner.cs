namespace WorkChatMini;

public sealed class StoryPlanner
{
    private readonly StoryAiStageRunner _runner = new();

    public Task<string> PlanAsync(
        StoryProject project,
        string userInstruction,
        string memoryContext,
        string researchContext,
        string recentConversation,
        string providerId,
        string model,
        CancellationToken cancellationToken)
    {
        string systemPrompt = StoryPromptBuilder.BuildBase(project, memoryContext, researchContext) + """

You are now the private PLANNER stage. Do not write the final answer.
Produce a compact working plan for the requested output. Cover only what matters:
- objective and requested deliverable
- POV / viewpoint if relevant
- participating characters and motivation
- scene/chapter beats in order
- conflict and emotional movement
- reveals/foreshadowing and what must stay hidden
- ending/hook if relevant
- exact memory/research facts the writer must preserve
Do not invent permanent canon merely to fill a blank. Mark uncertain choices as flexible.
""";

        string input = $"""
CURRENT CREATOR INSTRUCTION:
{userInstruction}

RECENT CONVERSATION CONTEXT:
{Trim(recentConversation, 10000)}
""";
        return _runner.CompleteAsync(providerId, model, systemPrompt,
            new[] { new StoryMessage { Role = "user", Content = input } }, cancellationToken,
            temperature: 0.25, workload: StoryAiWorkload.Utility, stageName: "planner");
    }

    private static string Trim(string value, int max) => value.Length <= max ? value : value[..max] + "…";
}
