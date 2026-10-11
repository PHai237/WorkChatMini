namespace WorkChatMini;

public static class StoryProviderFactory
{
    private static readonly IStoryAiProvider Gemini = new GeminiStoryProvider();
    private static readonly IStoryAiProvider OpenAi = new OpenAiStoryProvider();

    public static IReadOnlyList<IStoryAiProvider> Providers { get; } = new[] { Gemini, OpenAi };

    public static IStoryAiProvider Get(string? id) =>
        Providers.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase)) ?? Gemini;

    public static string DefaultModel(string providerId) =>
        string.Equals(providerId, "OpenAI", StringComparison.OrdinalIgnoreCase)
            ? "gpt-6-luna"
            : "gemini-3.8-flash";
}
