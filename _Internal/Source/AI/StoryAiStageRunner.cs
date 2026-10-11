using System.Diagnostics;
using System.Text;

namespace WorkChatMini;

public sealed class StoryAiStageRunner
{
    private static readonly TimeSpan UtilityStageTimeout = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan CreativeStageTimeout = TimeSpan.FromSeconds(100);
    private static readonly TimeSpan StreamingStageTimeout = TimeSpan.FromMinutes(3);

    public async Task<string> CompleteAsync(
        string providerId,
        string model,
        string systemPrompt,
        IReadOnlyList<StoryMessage> messages,
        CancellationToken cancellationToken,
        double temperature = 0.2,
        StoryAiWorkload workload = StoryAiWorkload.Utility,
        string stageName = "utility")
    {
        IStoryAiProvider provider = StoryProviderFactory.Get(providerId);
        string? apiKey = StoryCredentialStore.Load(provider.Id);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException($"No API key is saved for {provider.DisplayName}.");

        string effectiveModel = StoryModelRouter.Resolve(provider.Id, model, workload);
        TimeSpan timeout = workload == StoryAiWorkload.Utility ? UtilityStageTimeout : CreativeStageTimeout;
        using var stageCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stageCts.CancelAfter(timeout);

        var builder = new StringBuilder();
        var stopwatch = Stopwatch.StartNew();
        int inputChars = systemPrompt.Length + messages.Sum(m => m.Content.Length);
        string requestMode = workload == StoryAiWorkload.Utility ? "non-stream" : "stream-buffered";
        AppLogger.Info("STORY-AI",
            $"Stage started | stage={stageName} | workload={workload} | configuredModel={model} | effectiveModel={effectiveModel} | inputChars={inputChars} | mode={requestMode}");
        try
        {
            await provider.StreamAsync(
                new StoryAiRequest
                {
                    SystemPrompt = systemPrompt,
                    Model = effectiveModel,
                    Messages = messages,
                    Temperature = temperature,
                    ThinkingLevel = workload == StoryAiWorkload.Utility ? "low" : "medium",
                    PreferNonStreaming = workload == StoryAiWorkload.Utility,
                    NonStreamingTimeoutSeconds = workload == StoryAiWorkload.Utility ? 25 : 90
                },
                apiKey,
                delta => builder.Append(delta),
                stageCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"AI stage '{stageName}' did not complete within {(int)timeout.TotalSeconds}s.");
        }

        string result = builder.ToString().Trim();
        AppLogger.Info("STORY-AI",
            $"Stage completed | stage={stageName} | effectiveModel={effectiveModel} | elapsedMs={stopwatch.ElapsedMilliseconds} | outputChars={result.Length}");
        return result;
    }

    public async Task<string> StreamAsync(
        string providerId,
        string model,
        string systemPrompt,
        IReadOnlyList<StoryMessage> messages,
        Action<string> onDelta,
        CancellationToken cancellationToken,
        double temperature = 0.75,
        StoryAiWorkload workload = StoryAiWorkload.Creative,
        string stageName = "stream")
    {
        IStoryAiProvider provider = StoryProviderFactory.Get(providerId);
        string? apiKey = StoryCredentialStore.Load(provider.Id);
        if (string.IsNullOrWhiteSpace(apiKey))
            throw new InvalidOperationException($"No API key is saved for {provider.DisplayName}.");

        string effectiveModel = StoryModelRouter.Resolve(provider.Id, model, workload);
        using var stageCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        stageCts.CancelAfter(StreamingStageTimeout);

        var builder = new StringBuilder();
        var stopwatch = Stopwatch.StartNew();
        int inputChars = systemPrompt.Length + messages.Sum(m => m.Content.Length);
        AppLogger.Info("STORY-AI",
            $"Stage started | stage={stageName} | workload={workload} | configuredModel={model} | effectiveModel={effectiveModel} | inputChars={inputChars} | mode=stream");
        try
        {
            await provider.StreamAsync(
                new StoryAiRequest
                {
                    SystemPrompt = systemPrompt,
                    Model = effectiveModel,
                    Messages = messages,
                    Temperature = temperature,
                    ThinkingLevel = workload == StoryAiWorkload.Utility ? "low" : "medium",
                    PreferNonStreaming = false,
                    NonStreamingTimeoutSeconds = workload == StoryAiWorkload.Utility ? 35 : 90
                },
                apiKey,
                delta =>
                {
                    builder.Append(delta);
                    onDelta(delta);
                },
                stageCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"AI stage '{stageName}' did not complete within {(int)StreamingStageTimeout.TotalSeconds}s.");
        }

        string result = builder.ToString();
        AppLogger.Info("STORY-AI",
            $"Stage completed | stage={stageName} | effectiveModel={effectiveModel} | elapsedMs={stopwatch.ElapsedMilliseconds} | outputChars={result.Length}");
        return result;
    }
}
