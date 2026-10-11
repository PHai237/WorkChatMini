using System.Text.Json;

namespace WorkChatMini;

/// <summary>
/// A proposal must be validated and saved against a detached document. Merging
/// directly into the live Memory before disk success leaves invisible changes
/// that can be applied again after a failed save.
/// </summary>
internal static class StoryMemoryStaging
{
    internal static StoryMemoryDocument Clone(StoryMemoryDocument source)
    {
        string json = JsonSerializer.Serialize(source);
        return JsonSerializer.Deserialize<StoryMemoryDocument>(json)
            ?? throw new InvalidOperationException("Could not snapshot Story Memory safely.");
    }
}
