namespace WorkChatMini;

public enum StoryAiWorkload
{
    Utility,
    Creative
}

public static class StoryModelRouter
{
    private const string GeminiUtilityModel = "gemini-3.5-flash-lite";

    public static string Resolve(string providerId, string configuredModel, StoryAiWorkload workload)
    {
        string model = string.IsNullOrWhiteSpace(configuredModel)
            ? StoryProviderFactory.DefaultModel(providerId)
            : configuredModel.Trim();

        if (workload != StoryAiWorkload.Utility ||
            !string.Equals(providerId, "Gemini", StringComparison.OrdinalIgnoreCase))
        {
            return model;
        }

        return model.Contains("flash-lite", StringComparison.OrdinalIgnoreCase)
            ? model
            : GeminiUtilityModel;
    }
}
