namespace WorkChatMini;

public sealed class StoryCanonRevisionWorkflow
{
    private readonly StoryAiStageRunner _runner = new();

    public async Task<StoryActionResult> ProposeAsync(
        StoryProject project,
        string instruction,
        string memoryContext,
        string providerId,
        string model,
        Action<string> onStage,
        CancellationToken cancellationToken)
    {
        onStage("Preparing canon revision…");
        string systemPrompt = StoryPromptBuilder.BuildBase(project, memoryContext) + """

You are the CANON REVISION workflow. The creator explicitly wants to correct or retcon established canon.
This workflow is the ONLY place where old confirmed canon may be deliberately retired.

Rules:
- Do not write narrative prose.
- Return ONE JSON object only, no markdown fence.
- Copy originalFact as exactly as possible from CURRENT confirmed/proposed canon in Story Memory. Never target a Retconned/Contradicted historical fact.
- Use kind "Retcon" when the creator intentionally changes established story reality retroactively.
- Use kind "Correction" when the stored fact itself was mistaken and the creator is correcting the record.
- replacementFact must be a standalone statement of the new current truth.
- Do not invent extra retcons merely to make the request cleaner.
- If the request can be satisfied without changing established canon, return an empty revisions array and explain that no canon revision is required.
- This is still only a proposal. The app will not apply it until the creator presses Apply Revision.

JSON schema:
{
  "summary":"Plain-language explanation of exactly what would change and any continuity consequences.",
  "revisions":[{
    "kind":"Retcon|Correction",
    "originalFactId":"",
    "originalFact":"exact existing canon fact",
    "replacementFact":"new current canon fact",
    "reason":"why the creator requested this change",
    "relatedCharacters":[]
  }]
}
""";

        string raw = await _runner.CompleteAsync(
            providerId,
            model,
            systemPrompt,
            new[] { new StoryMessage { Role = "user", Content = "CANON REVISION REQUEST:\n" + instruction } },
            cancellationToken,
            temperature: 0.15,
            workload: StoryAiWorkload.Utility,
            stageName: "canon-revision-proposal");

        StoryCanonRevisionProposalEnvelope? envelope = StoryJson.TryDeserialize<StoryCanonRevisionProposalEnvelope>(raw);
        if (envelope is null)
            throw new InvalidOperationException("The model did not return a usable canon revision proposal.");
        envelope.Revisions ??= new List<StoryCanonRevisionPatch>();
        List<StoryCanonRevisionPatch> revisions = envelope.Revisions
            .Where(x => !string.IsNullOrWhiteSpace(x.OriginalFact) && !string.IsNullOrWhiteSpace(x.ReplacementFact))
            .Take(8)
            .ToList();
        if (revisions.Count == 0)
        {
            string noChange = string.IsNullOrWhiteSpace(envelope.Summary)
                ? "No existing canon needs to be revised for that request."
                : envelope.Summary.Trim();
            return new StoryActionResult
            {
                Response = noChange,
                Action = StoryActionType.ReviseCanon,
                ShouldAutoBookkeep = false
            };
        }

        foreach (StoryCanonRevisionPatch revision in revisions)
        {
            revision.Kind = string.Equals(revision.Kind, "Correction", StringComparison.OrdinalIgnoreCase) ? "Correction" : "Retcon";
            revision.OriginalFactId = revision.OriginalFactId?.Trim() ?? string.Empty;
            revision.OriginalFact = revision.OriginalFact.Trim();
            revision.ReplacementFact = revision.ReplacementFact.Trim();
            revision.Reason = revision.Reason?.Trim() ?? string.Empty;
            revision.RelatedCharacters ??= new List<string>();
        }

        string summary = string.IsNullOrWhiteSpace(envelope.Summary)
            ? BuildFallback(revisions)
            : envelope.Summary.Trim();
        var update = new StoryMemoryUpdate { Revisions = revisions };
        return new StoryActionResult
        {
            Response = summary + "\n\nThis changes established canon. Review it carefully, then use ‘Apply Revision’ below to commit the change.",
            Action = StoryActionType.ReviseCanon,
            ShouldAutoBookkeep = false,
            ProposalKind = "CanonRevision",
            ProposalJson = StoryProposalSerializer.SerializeUpdate(update)
        };
    }

    private static string BuildFallback(IEnumerable<StoryCanonRevisionPatch> revisions) => string.Join("\n\n", revisions.Select(x =>
        $"{x.Kind}\nOLD · {x.OriginalFact}\nNEW · {x.ReplacementFact}" +
        (string.IsNullOrWhiteSpace(x.Reason) ? string.Empty : $"\nReason · {x.Reason}")));
}

public sealed class StoryCanonRevisionProposalEnvelope
{
    public string Summary { get; set; } = string.Empty;
    public List<StoryCanonRevisionPatch> Revisions { get; set; } = new();
}
