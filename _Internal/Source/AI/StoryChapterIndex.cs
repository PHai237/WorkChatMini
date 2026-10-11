namespace WorkChatMini;

public sealed class StoryChapterIndexDocument
{
    public int SchemaVersion { get; set; } = 3;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public List<StoryChapterIndexEntry> Chapters { get; set; } = new();
}

public sealed class StoryChapterIndexEntry
{
    public int Number { get; set; }
    public string FileName { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public long LastWriteTicks { get; set; }
    public int TextLength { get; set; }
    public List<string> Terms { get; set; } = new();
    // Base64-encoded, fixed-size approximate filter over the full chapter text.
    public string TermSignature { get; set; } = string.Empty;
}
