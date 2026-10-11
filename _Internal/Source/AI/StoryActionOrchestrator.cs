namespace WorkChatMini;

public sealed class StoryActionOrchestrator
{
    private readonly StoryWritingPipeline _writing = new();
    private readonly StoryAutoContinueWorkflow _autoContinue = new();
    private readonly StoryPlanningWorkflow _planning = new();
    private readonly StoryProposalWorkflow _proposals = new();
    private readonly StoryContinuityChecker _continuity = new();
    private readonly StoryCanonRevisionWorkflow _canonRevision = new();

    public Task<StoryActionResult> RunAsync(
        StoryProject project,
        IReadOnlyList<StoryMessage> messages,
        string instruction,
        StoryActionType action,
        string providerId,
        string model,
        string memoryContext,
        Action<string> onDelta,
        Action<string> onStage,
        CancellationToken cancellationToken)
    {
        return action switch
        {
            StoryActionType.ContinueStory => _autoContinue.ContinueAsync(
                project, messages, instruction, providerId, model, memoryContext,
                onDelta, onStage, cancellationToken),
            StoryActionType.DraftScene or StoryActionType.Chat => _writing.RunAsync(
                project, messages, instruction, providerId, model, memoryContext, action,
                onDelta, onStage, cancellationToken),
            StoryActionType.PlanChapter => _planning.PlanChapterAsync(
                project, instruction, memoryContext, providerId, model,
                onDelta, onStage, cancellationToken),
            StoryActionType.BuildCharacter => _proposals.BuildCharacterAsync(
                project, instruction, memoryContext, providerId, model,
                onStage, cancellationToken),
            StoryActionType.CheckContinuity => CheckContinuityAsync(
                project, instruction, memoryContext, providerId, model,
                onDelta, onStage, cancellationToken),
            StoryActionType.ExpandLore => _proposals.ExpandLoreAsync(
                project, instruction, memoryContext, providerId, model,
                onStage, cancellationToken),
            StoryActionType.ReviseCanon => _canonRevision.ProposeAsync(
                project, instruction, memoryContext, providerId, model,
                onStage, cancellationToken),
            _ => throw new ArgumentOutOfRangeException(nameof(action), action, null)
        };
    }

    private async Task<StoryActionResult> CheckContinuityAsync(
        StoryProject project,
        string instruction,
        string memoryContext,
        string providerId,
        string model,
        Action<string> onDelta,
        Action<string> onStage,
        CancellationToken cancellationToken)
    {
        onStage("Checking continuity…");
        string response = await _continuity.ReviewAsync(
            project, instruction, memoryContext, providerId, model,
            onDelta, cancellationToken);
        return new StoryActionResult
        {
            Response = response,
            Action = StoryActionType.CheckContinuity,
            ShouldAutoBookkeep = false,
            ContinuityVerified = true
        };
    }
}
