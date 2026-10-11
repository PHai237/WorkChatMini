namespace WorkChatMini;

public interface IStoryWebResearchProvider
{
    Task<StoryWebResearchResult> ResearchWebAsync(
        string model,
        string apiKey,
        string query,
        CancellationToken cancellationToken);
}
