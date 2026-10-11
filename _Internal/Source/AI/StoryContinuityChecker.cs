namespace WorkChatMini;

public sealed class StoryContinuityChecker
{
    private readonly StoryAiStageRunner _runner = new();

    public Task<string> PreCheckAsync(
        StoryProject project,
        string userInstruction,
        string plan,
        string memoryContext,
        string researchContext,
        string providerId,
        string model,
        CancellationToken cancellationToken)
    {
        string systemPrompt = StoryPromptBuilder.BuildBase(project, memoryContext, researchContext) + """

You are the private CONTINUITY PRE-CHECK stage. Do not draft prose.
Inspect the creator instruction and plan against memory. Produce concise constraints for the writer.
Explicitly check: character knowledge, location/travel and scene presence, injuries/condition, power/progression, inventory, relationship history/current state, character-arc consistency, timeline, world rules, and open threads.
If there is no known conflict, say so briefly. If information is absent, say it is unknown rather than inventing it.
""";
        string input = $"""
CREATOR INSTRUCTION:
{userInstruction}

PLANNER OUTPUT:
{Trim(plan, 12000)}
""";
        return _runner.CompleteAsync(providerId, model, systemPrompt,
            new[] { new StoryMessage { Role = "user", Content = input } }, cancellationToken,
            temperature: 0.15, workload: StoryAiWorkload.Utility, stageName: "continuity-precheck");
    }


    public Task<string> ReviewAsync(
        StoryProject project,
        string request,
        string memoryContext,
        string providerId,
        string model,
        Action<string> onDelta,
        CancellationToken cancellationToken)
    {
        string systemPrompt = StoryPromptBuilder.BuildBase(project, memoryContext) + """

You are the CONTINUITY REVIEW tool. The creator is asking whether an idea, event, reveal, passage, or proposed change fits the existing story.
Do not draft new narrative and do not change persistent memory.
Check only story correctness and explicitly inspect:
- character knowledge and secret leaks
- character location / travel feasibility
- injuries, condition, death/destruction state
- inventory and possession
- abilities, power, progression
- relationship history and current state
- character-arc consistency (earned growth/regression; no unexplained personality reset)
- scene location / physical presence and travel feasibility
- timeline / chronology
- confirmed canon and world rules
- open threads when materially relevant
Distinguish a real contradiction from information that is merely unknown or not yet established.
Answer the creator directly. Start with one of: "No continuity conflict found", "Continuity risk", or "Continuity conflict".
Then list specific reasons and, when useful, the smallest safe fix. Never invent missing canon to make the idea work.
""";
        return _runner.StreamAsync(
            providerId, model, systemPrompt,
            new[] { new StoryMessage { Role = "user", Content = "CHECK THIS AGAINST STORY MEMORY:\n" + request } },
            onDelta, cancellationToken, temperature: 0.2,
            workload: StoryAiWorkload.Utility, stageName: "continuity-review");
    }

    public async Task<StoryContinuityReport> FinalCheckAsync(
        StoryProject project,
        string userInstruction,
        string finalDraft,
        string memoryContext,
        string providerId,
        string model,
        CancellationToken cancellationToken)
    {
        string systemPrompt = StoryPromptBuilder.BuildBase(project, memoryContext) + """

You are the private FINAL CONTINUITY CHECK stage. Check the candidate output against persistent story memory and the creator instruction.
Focus only on factual/story-state continuity: character knowledge leaks, impossible location/travel or duplicate presence, injuries, inventory, power/progression, relationship history/current state, character-arc consistency, timeline, world rules, dead/destroyed entities, and explicit creator constraints.
Do not demand stylistic rewrites. Do not create new canon.
Return ONE JSON object only using the schema appended after this prompt.
Set requiresRepair only for a concrete contradiction or knowledge leak that changes story correctness. Unknown/unspecified information is not a contradiction.
A character knowledge leak requires evidence that the character could not know the secret at that point. Merely missing a structured Knowledge entry is NOT proof of a leak. Check dialogue, thoughts and actions in the requested POV rather than treating omniscient narration as character awareness.
""" + "\nJSON schema: {\"pass\":true|false,\"requiresRepair\":true|false,\"issues\":[\"specific issue\"]}";
        string input = $"""
CREATOR INSTRUCTION:
{userInstruction}

CANDIDATE OUTPUT:
{Trim(finalDraft, 60000)}
""";
        string raw = await _runner.CompleteAsync(providerId, model, systemPrompt,
            new[] { new StoryMessage { Role = "user", Content = input } }, cancellationToken,
            temperature: 0.1, workload: StoryAiWorkload.Utility, stageName: "continuity-final");
        StoryContinuityReport? report = StoryJson.TryDeserialize<StoryContinuityReport>(raw);
        if (report is null)
            return new StoryContinuityReport { Pass = true, RequiresRepair = false, Verified = false };

        report.Verified = true;
        report.Issues ??= new List<string>();
        report.Issues = report.Issues.Where(x => !string.IsNullOrWhiteSpace(x)).Take(10).ToList();
        if (report.Issues.Count == 0)
        {
            report.Pass = true;
            report.RequiresRepair = false;
        }
        else if (!report.Pass)
        {
            report.RequiresRepair = true;
        }
        if (report.RequiresRepair)
            report.Pass = false;
        return report;
    }

    private static string Trim(string value, int max) => value.Length <= max ? value : value[..max] + "…";
}
