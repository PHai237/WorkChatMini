namespace WorkChatMini;

public sealed class StoryRelationshipChange
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string From { get; set; } = string.Empty;
    public string To { get; set; } = string.Empty;
    public string BeforeState { get; set; } = string.Empty;
    public string AfterState { get; set; } = string.Empty;
    public string Change { get; set; } = string.Empty;
    public string Importance { get; set; } = "Major";
    public int? ChapterNumber { get; set; }
    public string ChapterTitle { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class StoryCharacterArcMemory
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Character { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public string Importance { get; set; } = "Major";
    public string Stage { get; set; } = string.Empty;
    public string Direction { get; set; } = "Stable";
    public string InternalConflict { get; set; } = string.Empty;
    public string ExternalPressure { get; set; } = string.Empty;
    public string Progress { get; set; } = string.Empty;
    public string LastChange { get; set; } = string.Empty;
    public int? StartedChapter { get; set; }
    public int? LastChapter { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

// A durable, chapter-provenanced character turning point; unlike CharacterArcs,
// this record is not overwritten by the next bookkeeping update.
public sealed class StoryCharacterDevelopmentChange
{
    public string Character { get; set; } = string.Empty;
    public string ArcTitle { get; set; } = string.Empty;
    public string BeforeStage { get; set; } = string.Empty;
    public string AfterStage { get; set; } = string.Empty;
    public string BeforeDirection { get; set; } = string.Empty;
    public string AfterDirection { get; set; } = string.Empty;
    public string Evidence { get; set; } = string.Empty;
    public int ChapterNumber { get; set; }
    public string Source { get; set; } = string.Empty;
}

public sealed class StoryLocationMemory
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Parent { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public List<string> Connections { get; set; } = new();
    public List<string> PresentCharacters { get; set; } = new();
    public List<string> RelevantObjects { get; set; } = new();
    public int? LastChapter { get; set; }
    public string Source { get; set; } = string.Empty;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class StorySceneStateMemory
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public int? ChapterNumber { get; set; }
    public string ChapterTitle { get; set; } = string.Empty;
    public string SceneLabel { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
    public string PovCharacter { get; set; } = string.Empty;
    public List<string> PresentCharacters { get; set; } = new();
    public List<string> DepartedCharacters { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
}

public sealed class StoryCharacterArcPatch
{
    public string Character { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public string Importance { get; set; } = "Major";
    public string Stage { get; set; } = string.Empty;
    public string Direction { get; set; } = "Stable";
    public string InternalConflict { get; set; } = string.Empty;
    public string ExternalPressure { get; set; } = string.Empty;
    public string Progress { get; set; } = string.Empty;
    public string LastChange { get; set; } = string.Empty;
}

public sealed class StoryLocationPatch
{
    public string Name { get; set; } = string.Empty;
    public string Summary { get; set; } = string.Empty;
    public string Parent { get; set; } = string.Empty;
    public string Status { get; set; } = "Active";
    public List<string> Connections { get; set; } = new();
    public List<string> PresentCharacters { get; set; } = new();
    public List<string> RelevantObjects { get; set; } = new();
}

public sealed class StorySceneStatePatch
{
    public string SceneLabel { get; set; } = string.Empty;
    public string Location { get; set; } = string.Empty;
    public string Time { get; set; } = string.Empty;
    public string PovCharacter { get; set; } = string.Empty;
    public List<string> PresentCharacters { get; set; } = new();
    public List<string> DepartedCharacters { get; set; } = new();
    public string Summary { get; set; } = string.Empty;
}
