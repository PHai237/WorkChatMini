namespace WorkChatMini;

public sealed class StoryMemoryDocument
{
    public int SchemaVersion { get; set; } = 6;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<StoryChapterMemory> Chapters { get; set; } = new();
    public List<StoryCanonFact> Canon { get; set; } = new();
    public List<StoryCharacterMemory> Characters { get; set; } = new();
    public List<StoryRelationshipMemory> Relationships { get; set; } = new();
    public List<StoryRelationshipChange> RelationshipHistory { get; set; } = new();
    public List<StoryTimelineEvent> Timeline { get; set; } = new();
    public List<StoryPlotThread> Threads { get; set; } = new();
    public List<StoryLoreEntry> Lore { get; set; } = new();
    public List<StorySignificantEvent> Events { get; set; } = new();
    public List<StoryArcMemory> Arcs { get; set; } = new();
    public List<StoryCharacterArcMemory> CharacterArcs { get; set; } = new();
    // Optional v6 extension. Only recorded from committed chapter deltas.
    public List<StoryCharacterDevelopmentChange> CharacterDevelopmentHistory { get; set; } = new();
    public List<StoryLocationMemory> Locations { get; set; } = new();
    public List<StorySceneStateMemory> SceneStates { get; set; } = new();
    public List<StoryCanonRevision> Revisions { get; set; } = new();
    public List<StoryNarrativePromise> NarrativePromises { get; set; } = new();
    public List<StoryCausalLink> CausalLinks { get; set; } = new();
    // Optional receipt for approved character/lore/canon proposals. Never an AI
    // fact: ensures a crash between two JSON saves cannot reapply one delta.
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull)]
    public List<string>? AppliedProposalKeys { get; set; }
}

public sealed class StoryArcMemory
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public string Priority { get; set; } = "Major";
    public string Momentum { get; set; } = "Steady";
    public string Stakes { get; set; } = string.Empty;
    public string Progress { get; set; } = string.Empty;
    public List<string> Goals { get; set; } = new();
    public List<string> RelatedCharacters { get; set; } = new();
    public List<string> RelatedThreads { get; set; } = new();
    public int? StartedChapter { get; set; }
    public int? LastChapter { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}


public sealed class StoryCanonRevision
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Kind { get; set; } = "Retcon";
    public string OriginalFactId { get; set; } = string.Empty;
    public string OriginalFact { get; set; } = string.Empty;
    public string ReplacementFact { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public List<string> RelatedCharacters { get; set; } = new();
    public string Source { get; set; } = string.Empty;
    public int? ChapterNumber { get; set; }
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class StoryChapterMemory
{
    public string CheckpointId { get; set; } = string.Empty;
    public int Number { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class StoryCanonFact
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Fact { get; set; } = string.Empty;
    public string Status { get; set; } = "Confirmed";
    public string Source { get; set; } = string.Empty;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class StoryCharacterMemory
{
    public string Name { get; set; } = string.Empty;
    public List<string> Aliases { get; set; } = new();
    public string Appearance { get; set; } = string.Empty;
    public string Personality { get; set; } = string.Empty;
    public List<string> Goals { get; set; } = new();
    public List<string> Fears { get; set; } = new();
    public List<string> Secrets { get; set; } = new();
    public List<string> Knowledge { get; set; } = new();
    public string CurrentLocation { get; set; } = string.Empty;
    public string Condition { get; set; } = string.Empty;
    public List<string> Abilities { get; set; } = new();
    public List<string> Inventory { get; set; } = new();
    public string Progression { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class StoryRelationshipMemory
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public List<string> History { get; set; } = new();
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class StoryTimelineEvent
{
    public string When { get; set; } = string.Empty;
    public string Event { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
}

public sealed class StoryPlotThread
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Status { get; set; } = "Open";
    public string Importance { get; set; } = "Major";
    public int? OpenedChapter { get; set; }
    public int? LastTouchedChapter { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class StoryLoreEntry
{
    public string Category { get; set; } = "World";
    public string Name { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Status { get; set; } = "Confirmed";
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class StorySignificantEvent
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Type { get; set; } = "Custom";
    public string Subject { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Before { get; set; } = string.Empty;
    public string After { get; set; } = string.Empty;
    public string Importance { get; set; } = "Major";
    public string Status { get; set; } = "Confirmed";
    public string When { get; set; } = string.Empty;
    public string Evidence { get; set; } = string.Empty;
    public List<string> RelatedCharacters { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public int? ChapterNumber { get; set; }
    public string ChapterTitle { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class StoryMemoryUpdate
{
    public bool IsChapter { get; set; }
    public string ChapterTitle { get; set; } = string.Empty;
    public string ChapterSummary { get; set; } = string.Empty;
    public List<StoryCanonFactPatch> Canon { get; set; } = new();
    public List<StoryCharacterPatch> Characters { get; set; } = new();
    public List<StoryRelationshipPatch> Relationships { get; set; } = new();
    public List<StoryTimelinePatch> Timeline { get; set; } = new();
    public List<StoryThreadPatch> OpenThreads { get; set; } = new();
    public List<string> ResolvedThreads { get; set; } = new();
    public List<StoryLorePatch> Lore { get; set; } = new();
    public List<StoryEventPatch> Events { get; set; } = new();
    public List<StoryArcPatch> Arcs { get; set; } = new();
    public List<StoryCharacterArcPatch> CharacterArcs { get; set; } = new();
    public List<StoryLocationPatch> Locations { get; set; } = new();
    public List<StorySceneStatePatch> SceneStates { get; set; } = new();
    public List<StoryCanonRevisionPatch> Revisions { get; set; } = new();
    public List<StoryNarrativePromisePatch> NarrativePromises { get; set; } = new();
    public List<StoryCausalLinkPatch> CausalLinks { get; set; } = new();
}

public sealed class StoryArcPatch
{
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public string Priority { get; set; } = string.Empty;
    public string Momentum { get; set; } = string.Empty;
    public string Stakes { get; set; } = string.Empty;
    public string Progress { get; set; } = string.Empty;
    public List<string> Goals { get; set; } = new();
    public List<string> RelatedCharacters { get; set; } = new();
    public List<string> RelatedThreads { get; set; } = new();
}


public sealed class StoryCanonRevisionPatch
{
    public string Kind { get; set; } = "Retcon";
    public string OriginalFactId { get; set; } = string.Empty;
    public string OriginalFact { get; set; } = string.Empty;
    public string ReplacementFact { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public List<string> RelatedCharacters { get; set; } = new();
}

public sealed class StoryCanonFactPatch
{
    public string Fact { get; set; } = string.Empty;
    public string Status { get; set; } = "Confirmed";
}

public sealed class StoryCharacterPatch
{
    public string Name { get; set; } = string.Empty;
    public List<string> Aliases { get; set; } = new();
    public string Appearance { get; set; } = string.Empty;
    public string Personality { get; set; } = string.Empty;
    public List<string> Goals { get; set; } = new();
    public List<string> RemovedGoals { get; set; } = new();
    public List<string> Fears { get; set; } = new();
    public List<string> Secrets { get; set; } = new();
    public List<string> Knowledge { get; set; } = new();
    public List<string> ForgottenKnowledge { get; set; } = new();
    public string CurrentLocation { get; set; } = string.Empty;
    public string Condition { get; set; } = string.Empty;
    public List<string> Abilities { get; set; } = new();
    public List<string> RemovedAbilities { get; set; } = new();
    public List<string> Inventory { get; set; } = new();
    public List<string> RemovedInventory { get; set; } = new();
    public string Progression { get; set; } = string.Empty;
    public string Notes { get; set; } = string.Empty;
}

public sealed class StoryRelationshipPatch
{
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public string Change { get; set; } = string.Empty;
}

public sealed class StoryTimelinePatch
{
    public string When { get; set; } = string.Empty;
    public string Event { get; set; } = string.Empty;
}

public sealed class StoryThreadPatch
{
    public string Title { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Importance { get; set; } = string.Empty;
}

public sealed class StoryLorePatch
{
    public string Category { get; set; } = "World";
    public string Name { get; set; } = string.Empty;
    public string Detail { get; set; } = string.Empty;
    public string Status { get; set; } = "Confirmed";
}

public sealed class StoryEventPatch
{
    public string Type { get; set; } = "Custom";
    public string Subject { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Before { get; set; } = string.Empty;
    public string After { get; set; } = string.Empty;
    public string Importance { get; set; } = "Major";
    public string Status { get; set; } = "Confirmed";
    public string When { get; set; } = string.Empty;
    public string Evidence { get; set; } = string.Empty;
    public List<string> RelatedCharacters { get; set; } = new();
    public List<string> Tags { get; set; } = new();
}

public sealed class StoryChapterSearchHit
{
    public int Number { get; init; }
    public string Title { get; init; } = string.Empty;
    public string Excerpt { get; init; } = string.Empty;
    public int Score { get; init; }
}
