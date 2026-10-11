using System.Text.Json;

namespace WorkChatMini;

public enum StoryActionType
{
    Chat,
    ContinueStory,
    DraftScene,
    PlanChapter,
    BuildCharacter,
    CheckContinuity,
    ExpandLore,
    ReviseCanon
}

public static class StoryActions
{
    public static string Label(StoryActionType action) => action switch
    {
        StoryActionType.ContinueStory => "Continue Story",
        StoryActionType.DraftScene => "Draft Scene",
        StoryActionType.PlanChapter => "Plan Chapter",
        StoryActionType.BuildCharacter => "Build Character",
        StoryActionType.CheckContinuity => "Check Continuity",
        StoryActionType.ExpandLore => "Expand Lore",
        StoryActionType.ReviseCanon => "Revise Canon",
        _ => "Chat"
    };

    public static string ComposerHint(StoryActionType action) => action switch
    {
        StoryActionType.ContinueStory => "Optional direction — leave blank and press Run to let AI continue on its own…",
        StoryActionType.DraftScene => "Describe what should happen in this scene…",
        StoryActionType.PlanChapter => "Describe what you want the next chapter to accomplish…",
        StoryActionType.BuildCharacter => "Describe the character, role, vibe, or gap in the cast…",
        StoryActionType.CheckContinuity => "Describe the idea, event, reveal, or passage you want checked…",
        StoryActionType.ExpandLore => "Describe the world rule, place, faction, history, or system to expand…",
        StoryActionType.ReviseCanon => "Describe exactly which established fact you want to correct or retcon…",
        _ => "Discuss ideas with Gemini. Chat never auto-commits a chapter; choose Continue Story when ready…"
    };

    public static bool TryParse(string? value, out StoryActionType action) =>
        Enum.TryParse(value, ignoreCase: true, out action);

    // Story Actions are explicit workflows. Chat must stay chat even when the
    // creator mentions words such as "scene", "chapter", or "write" while discussing them.
    public static bool UsesWritingPipeline(StoryActionType action) =>
        action is StoryActionType.DraftScene or StoryActionType.ContinueStory;

    public static bool IsNarrativeAction(StoryActionType action) =>
        action is StoryActionType.DraftScene or StoryActionType.ContinueStory;

    public static bool MayAutoBookkeep(StoryActionType action) => IsNarrativeAction(action);
}

public sealed class StoryActionResult
{
    public required string Response { get; init; }
    public StoryActionType Action { get; init; }
    public bool UsedWritingPipeline { get; init; }
    public bool ShouldAutoBookkeep { get; init; } = true;
    public int ResearchSourceCount { get; init; }
    public int ResearchNoteCount { get; init; }
    public bool RepairedAfterFinalCheck { get; init; }
    public bool ContinuityVerified { get; init; } = true;
    public string ProposalKind { get; init; } = string.Empty;
    public string ProposalJson { get; init; } = string.Empty;

    public bool HasProposal => !string.IsNullOrWhiteSpace(ProposalKind) && !string.IsNullOrWhiteSpace(ProposalJson);
}

public sealed class StoryCharacterProposalEnvelope
{
    public string Summary { get; set; } = string.Empty;
    public StoryCharacterPatch Character { get; set; } = new();
    public List<StoryRelationshipPatch> Relationships { get; set; } = new();
}

public sealed class StoryLoreProposalEnvelope
{
    public string Summary { get; set; } = string.Empty;
    public List<StoryLorePatch> Lore { get; set; } = new();
}

public static class StoryProposalSerializer
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        WriteIndented = false
    };

    public static string SerializeUpdate(StoryMemoryUpdate update) => JsonSerializer.Serialize(update, Options);

    public static StoryMemoryUpdate? DeserializeUpdate(string json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;
        try
        {
            StoryMemoryUpdate? update = JsonSerializer.Deserialize<StoryMemoryUpdate>(json, Options);
            return update is null ? null : StoryMemoryUpdateNormalizer.Normalize(update);
        }
        catch
        {
            return null;
        }
    }
}
