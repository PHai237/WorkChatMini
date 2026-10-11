using System.Text.Json.Serialization;

namespace WorkChatMini;

public sealed class StoryProject
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "Untitled Story";
    public List<string> CoreGenres { get; set; } = new();
    public List<string> TemporaryTags { get; set; } = new();
    public string Role { get; set; } = "Companion";
    public bool AutoBookkeeping { get; set; } = true;
    public string AutonomyMode { get; set; } = StoryAutonomyModes.Balanced;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string? StoryDirectorMode { get; set; }
    public string ManuscriptMode { get; set; } = StoryManuscriptModes.AutoCommit;
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NarrativeFocusPreference { get; set; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? NarrativeGuardrails { get; set; }
    public StoryContentProfile ContentProfile { get; set; } = new();
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;

    public override string ToString() => Title;
}

public sealed class StoryMessage
{
    public string Role { get; set; } = "user";
    public string Content { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public string Action { get; set; } = string.Empty;
    public string ProposalKind { get; set; } = string.Empty;
    public string ProposalJson { get; set; } = string.Empty;
    public bool ProposalApplied { get; set; }
    public string SourceInstruction { get; set; } = string.Empty;
    public string DraftGroupId { get; set; } = string.Empty;
    public string DraftVersionId { get; set; } = string.Empty;
    public bool DraftDiscarded { get; set; }
    // A streamed reply that stopped or failed before the provider completed.
    // Optional so older stories keep the same JSON shape; never eligible for canon.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsInterruptedResponse { get; set; }
    public bool IsNarrativeDraft { get; set; }
    public bool ManuscriptCommitted { get; set; }
    public int? ManuscriptChapterNumber { get; set; }
    public string ManuscriptChapterTitle { get; set; } = string.Empty;
    // Co-created direction, explicitly accepted by the creator. Not confirmed story canon.
    public string CreativeDirection { get; set; } = string.Empty;
    public bool CreativeDirectionArchived { get; set; }
    // Optional creator-assigned organizing category; editorial metadata, never canon.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? CreativeDirectionCategory { get; set; }
    // Earlier creator-approved wording (editorial revisions, never confirmed canon).
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<StoryCreativeRevision>? CreativeDirectionRevisions { get; set; }
    // Explicitly declined assistant suggestions. Unselected suggestions are NOT declined.
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? DeclinedCreativeSuggestions { get; set; }
    public DateTimeOffset? ManuscriptCommittedUtc { get; set; }
}

public sealed class StoryAiRequest
{
    public required string SystemPrompt { get; init; }
    public required string Model { get; init; }
    public required IReadOnlyList<StoryMessage> Messages { get; init; }
    public double Temperature { get; init; } = 0.9;
    public string ThinkingLevel { get; init; } = "low";
    public bool PreferNonStreaming { get; init; }
    public int NonStreamingTimeoutSeconds { get; init; } = 60;
}

public static class StoryGenres
{
    public static readonly IReadOnlyList<string> All = new[]
    {
        "Medieval Fantasy", "MMORPG / Game System", "Isekai / Xuyên không",
        "Rebirth / Trọng sinh", "Xianxia / Tu tiên", "Wuxia", "Romcom",
        "Romance", "School Life", "Slice of Life", "Adventure", "Action",
        "Mystery", "Political Fantasy", "Drama", "Comedy", "Dark Fantasy",
        "Urban Fantasy", "Apocalypse", "Sci-Fi", "Supernatural", "Horror"
    };

    public static readonly IReadOnlyList<string> Roles = new[]
    {
        "Companion", "Writer", "Editor", "Worldmaster", "Character"
    };
}
