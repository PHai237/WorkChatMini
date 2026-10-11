namespace WorkChatMini;

public sealed class StoryPlanningWorkflow
{
    private readonly StoryResearchGate _researchGate = new();
    private readonly StoryResearchCoordinator _research = new();
    private readonly StoryDraftPreparationStage _preparation = new();
    private readonly StoryAiStageRunner _runner = new();

    public async Task<StoryActionResult> PlanChapterAsync(
        StoryProject project,
        string instruction,
        string memoryContext,
        string providerId,
        string model,
        Action<string> onDelta,
        Action<string> onStage,
        CancellationToken cancellationToken)
    {
        StoryResearchBundle research = await GetResearchAsync(
            project, instruction, memoryContext, providerId, model, onStage, cancellationToken);

        onStage("Preparing chapter plan…");
        StoryDraftPreparation preparation = await _preparation.PrepareAsync(
            project, instruction, string.Empty, memoryContext, research.Context,
            providerId, model, cancellationToken);

        onStage("Formatting chapter plan…");
        string systemPrompt = StoryPromptBuilder.BuildBase(project, memoryContext, research.Context) + """

You are the CHAPTER PLANNER. Return a useful plan for the creator, not narrative prose.
Use the private planning notes and continuity constraints below, but do not mention hidden stages.
Do not turn proposed ideas into confirmed canon. Clearly mark flexible or undecided choices.
Prefer this practical structure when relevant:
- Chapter objective
- POV / participating characters
- Opening situation
- Beats in order
- Character / emotional movement
- Conflict or complication
- Reveal / foreshadowing constraints
- Threads advanced or intentionally left open
- Closing hook
- Continuity notes the writer must preserve
Keep it concrete enough that Draft Scene/Chapter can use it next.
""";
        string input = $"""
CREATOR REQUEST:
{instruction}

PRIVATE PLAN:
{Trim(preparation.Plan, 16000)}

CONTINUITY CONSTRAINTS:
{Trim(preparation.Constraints, 12000)}
""";

        string response = await _runner.StreamAsync(
            providerId, model, systemPrompt,
            new[] { new StoryMessage { Role = "user", Content = input } },
            onDelta, cancellationToken, temperature: 0.55,
            workload: StoryAiWorkload.Creative, stageName: "chapter-plan-facing");

        return new StoryActionResult
        {
            Response = response,
            Action = StoryActionType.PlanChapter,
            ShouldAutoBookkeep = false,
            ContinuityVerified = true,
            ResearchNoteCount = research.Notes.Count,
            ResearchSourceCount = research.Notes.Sum(n => n.Sources.Count)
        };
    }

    private async Task<StoryResearchBundle> GetResearchAsync(
        StoryProject project,
        string instruction,
        string memoryContext,
        string providerId,
        string model,
        Action<string> onStage,
        CancellationToken cancellationToken)
    {
        onStage("Research gate…");
        StoryResearchDecision decision = await _researchGate.EvaluateAsync(
            project, instruction, memoryContext, providerId, model, cancellationToken);
        if (!decision.NeedsResearch)
            return new StoryResearchBundle();

        onStage("Researching…");
        try
        {
            return await _research.ResearchAsync(project, decision, providerId, model, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("STORY-RESEARCH", $"Chapter planning research skipped | project={project.Id} | {ex.Message}");
            return new StoryResearchBundle();
        }
    }

    private static string Trim(string value, int max) => value.Length <= max ? value : value[..max] + "…";
}
