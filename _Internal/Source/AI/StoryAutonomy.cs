namespace WorkChatMini;

public static class StoryAutonomyModes
{
    public const string Guided = "Guided";
    public const string Balanced = "Balanced";
    public const string Autonomous = "Autonomous";

    public static readonly IReadOnlyList<string> All = new[] { Guided, Balanced, Autonomous };

    public static string Normalize(string? value) => value switch
    {
        Guided => Guided,
        Autonomous => Autonomous,
        _ => Balanced
    };

    public static string Guidance(string? value) => Normalize(value) switch
    {
        Guided => """
GUIDED AUTONOMY:
- Follow the creator's current direction closely.
- Prefer advancing already-established characters, locations, goals, and open threads.
- You may invent small connective details needed to make the scene natural, but avoid major new characters, lore, powers, factions, twists, or irreversible plot turns unless the creator asked for them or existing setup strongly requires them.
- Leave room for the creator to decide major future direction.
""",
        Autonomous => """
AUTONOMOUS AUTONOMY:
- Choose the next meaningful scene or chapter direction yourself from confirmed memory, recent events, character goals, unresolved tensions, open threads, and pacing.
- Take initiative: you may introduce fitting complications, clues, minor characters, locations, scene goals, and new open threads when they grow naturally from the story.
- Meaningful forward motion is preferred over filler, recap, random escalation, or resolving every mystery at once.
- Major reveals, deaths, permanent power changes, retcons, and world-changing twists still require strong setup and must never contradict confirmed canon.
""",
        _ => """
BALANCED AUTONOMY:
- Treat the creator's message as direction rather than a full outline; fill in the missing scene-level decisions yourself.
- Choose a natural next beat, advance one or two relevant open threads, and let character goals and relationships drive the scene.
- You may invent dialogue, small complications, clues, minor details, and other reversible connective material when useful.
- Avoid arbitrary major twists, new core lore, sudden relationship jumps, permanent power changes, or resolving too many threads without setup.
"""
    };

    public static string Description(string? value) => Normalize(value) switch
    {
        Guided => "AI stays close to your direction and invents only small connective details.",
        Autonomous => "AI chooses the next meaningful direction and can invent fitting developments while respecting canon.",
        _ => "AI fills in scene-level ideas and advances existing threads without taking over major canon casually."
    };
}
