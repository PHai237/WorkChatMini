namespace WorkChatMini;

public sealed class StoryResearchDecision
{
    public bool NeedsResearch { get; set; }
    public string Reason { get; set; } = string.Empty;
    public List<string> Queries { get; set; } = new();
}

public sealed class StoryResearchSource
{
    public string Title { get; set; } = string.Empty;
    public string Url { get; set; } = string.Empty;
}

public sealed class StoryWebResearchResult
{
    public string Summary { get; set; } = string.Empty;
    public List<StoryResearchSource> Sources { get; set; } = new();
}

public sealed class StoryResearchNote
{
    public string Query { get; set; } = string.Empty;
    public string KeyFact { get; set; } = string.Empty;
    public string Relevance { get; set; } = string.Empty;
    public string Confidence { get; set; } = "Medium";
    public DateTimeOffset AccessedUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<StoryResearchSource> Sources { get; set; } = new();
}

public sealed class StoryResearchDocument
{
    public int SchemaVersion { get; set; } = 1;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<StoryResearchNote> Notes { get; set; } = new();
}

public sealed class StoryResearchBundle
{
    public string Context { get; init; } = string.Empty;
    public int FreshSearches { get; init; }
    public int CachedSearches { get; init; }
    public IReadOnlyList<StoryResearchNote> Notes { get; init; } = Array.Empty<StoryResearchNote>();
}

public sealed class StoryContinuityReport
{
    public bool Pass { get; set; } = true;
    public bool RequiresRepair { get; set; }
    public bool Verified { get; set; }
    public List<string> Issues { get; set; } = new();
}
public sealed class StoryDraftPreparation
{
    public string Plan { get; set; } = string.Empty;
    public string Constraints { get; set; } = string.Empty;
}

