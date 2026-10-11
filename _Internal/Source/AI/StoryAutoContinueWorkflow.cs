namespace WorkChatMini;

public sealed class StoryAutoContinueWorkflow
{
    private readonly StoryWritingPipeline _writing = new();

    public Task<StoryActionResult> ContinueAsync(
        StoryProject project,
        IReadOnlyList<StoryMessage> messages,
        string creatorDirection,
        string providerId,
        string model,
        string memoryContext,
        Action<string> onDelta,
        Action<string> onStage,
        CancellationToken cancellationToken)
    {
        string direction = string.IsNullOrWhiteSpace(creatorDirection)
            ? "Continue the story naturally from the current established state."
            : creatorDirection.Trim();

        string instruction = $"""
Continue the story as finished narrative prose from the current established state.

CREATOR DIRECTION:
{direction}

{StoryAutonomyModes.Guidance(project.AutonomyMode)}

{StoryNarrativeSteering.Guidance(project)}

Private workflow rules:
- Decide the next scene/beat during planning; do not ask the creator to outline it for you.
- If the project has no established narrative yet, create a strong opening scene from the title, genres, creator direction, and any existing memory instead of asking for more setup.
- Use confirmed Story Memory and recent chapters as the continuity anchor.
- Preserve character-specific knowledge, relationship history/current state, active character arcs, injuries/condition, inventory, current scene/location, timeline, and world rules.
- Character development is cumulative: use committed turning points and recent relationship shifts, not just initial Personality/Goals. A character may regress or act inconsistently under stress, but show a believable trigger and consequences rather than silently resetting their arc.
- World truth and retrieved secrets are author context, not character knowledge. In POV dialogue/thoughts/actions, rely on that character's established knowledge or reveal a believable on-page discovery first. Missing structured knowledge is uncertainty, not proof of ignorance.
- Prefer consequences and unfinished threads that naturally follow from what already happened.
- If NARRATIVE FOCUS is present in memory context, use it to balance long-running plot arcs, character arcs, and neglected threads. It is planning guidance, not canon: do not force a stale arc to resolve merely because it has not appeared recently.
- If SETUP / PAYOFF ATTENTION is present, preserve those reader-facing promises. Revisit or deepen them only when natural; age alone is never permission to pay them off. Do not repeat the same clue just to prove you remember it.
- Respect CAUSAL MEMORY when choosing consequences: established causes/motivations should continue to matter until superseded by later confirmed events. Never invent a causal explanation merely to connect unrelated facts.
- Respect LATEST SPATIAL STATE: do not teleport characters, duplicate a character across locations, or use an object/location state contradicted by memory unless travel/change is shown in the new prose.
- Prefer advancing one primary arc at a time unless a convergence/climax is naturally earned.
- Do not mention autonomy mode, planning, bookkeeping, memory, research, or hidden workflow stages in the prose.
- Only events that actually appear in the final prose should be treated as having happened. Ideas considered during planning are not story truth.
""";

        return _writing.RunAsync(
            project,
            messages,
            instruction,
            providerId,
            model,
            memoryContext,
            StoryActionType.ContinueStory,
            onDelta,
            onStage,
            cancellationToken,
            skipRoutineResearchGate: StoryDirector.IsRoutineCreatorDirection(creatorDirection));
    }
}
