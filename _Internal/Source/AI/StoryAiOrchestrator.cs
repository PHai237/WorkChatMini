using System.Text;

namespace WorkChatMini;

public sealed class StoryAiOrchestrator
{
    private const int MaxContextMessages = 28;
    private const int MaxContextCharacters = 60000;

    public async Task<string> StreamReplyAsync(
        StoryProject project,
        IReadOnlyList<StoryMessage> messages,
        string providerId,
        string model,
        string memoryContext,
        Action<string> onDelta,
        CancellationToken cancellationToken)
    {
        IStoryAiProvider provider = StoryProviderFactory.Get(providerId);
        string? apiKey = StoryCredentialStore.Load(provider.Id);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException($"No API key is saved for {provider.DisplayName}.");

        IReadOnlyList<StoryMessage> context = BuildContext(messages);
        var request = new StoryAiRequest
        {
            SystemPrompt = BuildSystemPrompt(project, memoryContext),
            Model = model,
            Messages = context,
            ThinkingLevel = "low",
            NonStreamingTimeoutSeconds = 60
        };

        var builder = new StringBuilder();
        await provider.StreamAsync(request, apiKey, delta =>
        {
            builder.Append(delta);
            onDelta(delta);
        }, cancellationToken);
        return builder.ToString();
    }

    private static IReadOnlyList<StoryMessage> BuildContext(IReadOnlyList<StoryMessage> messages)
    {
        var selected = new List<StoryMessage>();
        int characters = 0;
        for (int i = messages.Count - 1; i >= 0 && selected.Count < MaxContextMessages; i--)
        {
            StoryMessage message = messages[i];
            if (characters + message.Content.Length > MaxContextCharacters && selected.Count > 0)
                break;
            selected.Add(message);
            characters += message.Content.Length;
        }
        selected.Reverse();
        return selected;
    }

    private static string BuildSystemPrompt(StoryProject project, string memoryContext) =>
        StoryPromptBuilder.BuildBase(project, memoryContext);
}
