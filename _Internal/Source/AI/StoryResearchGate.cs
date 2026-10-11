namespace WorkChatMini;

public sealed class StoryResearchGate
{
    private static readonly TimeSpan DecisionTimeout = TimeSpan.FromSeconds(20);
    private readonly StoryAiStageRunner _runner = new();

    public async Task<StoryResearchDecision> EvaluateAsync(
        StoryProject project,
        string userInstruction,
        string memoryContext,
        string providerId,
        string model,
        CancellationToken cancellationToken)
    {
        string systemPrompt = $"""
You are the Research Gate for WorkChat Story Companion.
Project: {project.Title}
Decide whether drafting this fiction request materially benefits from current or precise REAL-WORLD web research.

Search when accuracy matters for real locations, historical periods, customs, architecture, clothing, food, professions, law, technology, travel/geography, medicine, science, or specialist terminology.
Do NOT search when project canon/lore already defines the answer, the concept is intentionally fictional, the creator is asking for custom rules, or research would add little value.
Do not include unpublished character names, secret plot details, or project-specific invented terms in web queries unless absolutely necessary. Rewrite queries as generic real-world topics where possible.
Return one JSON object only using the schema described after this prompt.
Use at most 3 focused queries. If unsure, prefer no research.

Relevant story memory (higher priority than research):
{Trim(memoryContext, 9000)}
""" + "\nJSON schema: {\"needsResearch\":true|false,\"reason\":\"short reason\",\"queries\":[\"query 1\",\"query 2\"]}";

        string raw;
        using (var gateCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
        {
            gateCts.CancelAfter(DecisionTimeout);
            try
            {
                raw = await _runner.CompleteAsync(
                    providerId,
                    model,
                    systemPrompt,
                    new[] { new StoryMessage { Role = "user", Content = userInstruction } },
                    gateCts.Token, temperature: 0.1, workload: StoryAiWorkload.Utility, stageName: "research-gate");
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                AppLogger.Warn("STORY-RESEARCH",
                    $"Research gate timed out after {DecisionTimeout.TotalSeconds:0}s; continuing without research | project={project.Id} | provider={providerId} | model={model}");
                return new StoryResearchDecision
                {
                    NeedsResearch = false,
                    Reason = "Research gate timed out; continuing without web research."
                };
            }
        }

        StoryResearchDecision? decision = StoryJson.TryDeserialize<StoryResearchDecision>(raw);
        if (decision is null || !decision.NeedsResearch)
            return new StoryResearchDecision { NeedsResearch = false, Reason = decision?.Reason ?? "No research needed." };

        decision.Queries ??= new List<string>();
        decision.Queries = decision.Queries
            .Where(q => !string.IsNullOrWhiteSpace(q))
            .Select(q => q.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(3)
            .ToList();
        decision.NeedsResearch = decision.Queries.Count > 0;
        return decision;
    }

    private static string Trim(string value, int max) => value.Length <= max ? value : value[..max] + "…";
}
