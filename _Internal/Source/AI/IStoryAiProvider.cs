namespace WorkChatMini;

public interface IStoryAiProvider
{
    string Id { get; }
    string DisplayName { get; }
    Task StreamAsync(
        StoryAiRequest request,
        string apiKey,
        Action<string> onDelta,
        CancellationToken cancellationToken);
}
