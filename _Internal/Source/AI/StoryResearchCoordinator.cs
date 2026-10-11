namespace WorkChatMini;

public sealed class StoryResearchCoordinator
{
    private static readonly TimeSpan CacheMaxAge = TimeSpan.FromDays(14);
    private static readonly TimeSpan QueryTimeout = TimeSpan.FromSeconds(30);
    private readonly StoryResearchStore _store = new();

    public async Task<StoryResearchBundle> ResearchAsync(
        StoryProject project,
        StoryResearchDecision decision,
        string providerId,
        string model,
        CancellationToken cancellationToken)
    {
        if (!decision.NeedsResearch || decision.Queries.Count == 0)
            return new StoryResearchBundle();

        IStoryAiProvider provider = StoryProviderFactory.Get(providerId);
        if (provider is not IStoryWebResearchProvider researchProvider)
        {
            AppLogger.Warn("STORY-RESEARCH", $"Provider has no web research capability | provider={provider.Id}");
            return new StoryResearchBundle();
        }
        string? apiKey = StoryCredentialStore.Load(provider.Id);
        if (string.IsNullOrWhiteSpace(apiKey))
            return new StoryResearchBundle();

        StoryResearchDocument document = await _store.LoadAsync(project.Id, cancellationToken);
        var notes = new List<StoryResearchNote>();
        int fresh = 0;
        int cached = 0;

        foreach (string query in decision.Queries.Take(3))
        {
            cancellationToken.ThrowIfCancellationRequested();
            StoryResearchNote? note = StoryResearchStore.FindFresh(document, query, CacheMaxAge);
            if (note is not null)
            {
                notes.Add(note);
                cached++;
                continue;
            }

            StoryWebResearchResult result;
            using (var queryCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            {
                queryCts.CancelAfter(QueryTimeout);
                try
                {
                    result = await researchProvider.ResearchWebAsync(model, apiKey, query, queryCts.Token);
                }
                catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
                {
                    AppLogger.Warn("STORY-RESEARCH",
                        $"Web research query timed out after {QueryTimeout.TotalSeconds:0}s; skipping query | project={project.Id} | query={query}");
                    continue;
                }
            }
            if (string.IsNullOrWhiteSpace(result.Summary))
                continue;

            note = new StoryResearchNote
            {
                Query = query,
                KeyFact = result.Summary.Trim(),
                Relevance = string.IsNullOrWhiteSpace(decision.Reason) ? "Real-world accuracy for the current draft." : decision.Reason.Trim(),
                Confidence = result.Sources.Count >= 2 ? "High" : result.Sources.Count == 1 ? "Medium" : "Low",
                AccessedUtc = DateTimeOffset.UtcNow,
                Sources = result.Sources
                    .Where(s => StoryResearchStore.IsSafeSourceUrl(s.Url))
                    .GroupBy(s => s.Url, StringComparer.OrdinalIgnoreCase)
                    .Select(group => group.First())
                    .Take(8)
                    .ToList()
            };
            StoryResearchStore.Upsert(document, note);
            notes.Add(note);
            fresh++;
        }

        if (fresh > 0)
            await _store.SaveAsync(project.Id, document, cancellationToken);

        return new StoryResearchBundle
        {
            Context = StoryResearchStore.BuildContext(notes),
            FreshSearches = fresh,
            CachedSearches = cached,
            Notes = notes
        };
    }
}
