namespace WorkChatMini;

public sealed class StoryProposalWorkflow
{
    private readonly StoryAiStageRunner _runner = new();
    private readonly StoryResearchGate _researchGate = new();
    private readonly StoryResearchCoordinator _research = new();
    private readonly StoryContinuityChecker _continuity = new();

    public async Task<StoryActionResult> BuildCharacterAsync(
        StoryProject project,
        string instruction,
        string memoryContext,
        string providerId,
        string model,
        Action<string> onStage,
        CancellationToken cancellationToken)
    {
        onStage("Designing character…");
        string systemPrompt = StoryPromptBuilder.BuildBase(project, memoryContext) + """

You are the CHARACTER DESIGN workflow. Create one character proposal that fits the existing cast and story.
Check for duplicate identity, overlapping role, relationship contradictions, knowledge leaks, impossible location/state, and conflicts with confirmed canon.
Do not silently add the character to canon. This is only a proposal until the creator explicitly accepts it.
Return ONE JSON object only, no markdown fence:
{
  "summary":"A readable compact profile for the creator, with role, personality, goals, fears, appearance, relationships/fit, secrets, what they know, what they do NOT know, and possible arc.",
  "character":{
    "name":"", "aliases":[], "appearance":"", "personality":"", "goals":[], "fears":[],
    "secrets":[], "knowledge":[], "currentLocation":"", "condition":"", "abilities":[],
    "inventory":[], "progression":"", "notes":""
  },
  "relationships":[{"from":"", "to":"", "state":"", "change":""}]
}
Use empty fields rather than inventing details that the creator did not request and the story does not support.
""";
        string raw = await _runner.CompleteAsync(
            providerId, model, systemPrompt,
            new[] { new StoryMessage { Role = "user", Content = "CHARACTER REQUEST:\n" + instruction } },
            cancellationToken, temperature: 0.65, workload: StoryAiWorkload.Utility, stageName: "character-proposal");

        StoryCharacterProposalEnvelope? envelope = StoryJson.TryDeserialize<StoryCharacterProposalEnvelope>(raw);
        if (envelope is null || string.IsNullOrWhiteSpace(envelope.Character?.Name))
            throw new InvalidOperationException("The model did not return a usable character proposal.");

        StoryCharacterPatch character = envelope.Character;
        NormalizeCharacter(character);
        envelope.Relationships ??= new List<StoryRelationshipPatch>();
        List<StoryRelationshipPatch> relationships = envelope.Relationships
            .Where(x => !string.IsNullOrWhiteSpace(x.From) && !string.IsNullOrWhiteSpace(x.To))
            .Take(12)
            .ToList();
        var update = new StoryMemoryUpdate
        {
            Characters = new List<StoryCharacterPatch> { character },
            Relationships = relationships
        };
        string summary = string.IsNullOrWhiteSpace(envelope.Summary)
            ? BuildCharacterFallback(character)
            : envelope.Summary.Trim();

        return new StoryActionResult
        {
            Response = summary + "\n\nThis is a proposal. Use ‘Add to Story’ below only if you want it stored in persistent Story Memory.",
            Action = StoryActionType.BuildCharacter,
            ShouldAutoBookkeep = false,
            ProposalKind = "Character",
            ProposalJson = StoryProposalSerializer.SerializeUpdate(update)
        };
    }

    public async Task<StoryActionResult> ExpandLoreAsync(
        StoryProject project,
        string instruction,
        string memoryContext,
        string providerId,
        string model,
        Action<string> onStage,
        CancellationToken cancellationToken)
    {
        StoryResearchBundle research = await GetResearchAsync(
            project, instruction, memoryContext, providerId, model, onStage, cancellationToken);

        onStage("Expanding lore…");
        string systemPrompt = StoryPromptBuilder.BuildBase(project, memoryContext, research.Context) + """

You are the LORE DESIGN workflow. Expand the requested worldbuilding without contradicting confirmed canon.
External research is reference material only and never overrides the creator's world rules.
Do not silently make the proposal canon. Return ONE JSON object only, no markdown fence:
{
  "summary":"Readable explanation of the proposed lore and how it fits existing canon, including any uncertainty or tradeoff.",
  "lore":[{"category":"World|Culture|Magic|Technology|Faction|Location|History|Other","name":"","detail":"","status":"Proposed"}]
}
All returned lore entries MUST use status "Proposed". Prefer a small coherent set over many filler entries.
""";
        string raw = await _runner.CompleteAsync(
            providerId, model, systemPrompt,
            new[] { new StoryMessage { Role = "user", Content = "LORE REQUEST:\n" + instruction } },
            cancellationToken, temperature: 0.65, workload: StoryAiWorkload.Utility, stageName: "lore-proposal");

        StoryLoreProposalEnvelope? envelope = StoryJson.TryDeserialize<StoryLoreProposalEnvelope>(raw);
        if (envelope is not null)
            envelope.Lore ??= new List<StoryLorePatch>();
        List<StoryLorePatch> lore = envelope?.Lore
            .Where(x => !string.IsNullOrWhiteSpace(x.Name) && !string.IsNullOrWhiteSpace(x.Detail))
            .Take(8)
            .ToList() ?? new List<StoryLorePatch>();
        if (lore.Count == 0)
            throw new InvalidOperationException("The model did not return a usable lore proposal.");
        foreach (StoryLorePatch entry in lore)
        {
            entry.Category = string.IsNullOrWhiteSpace(entry.Category) ? "World" : entry.Category.Trim();
            entry.Name = entry.Name.Trim();
            entry.Detail = entry.Detail.Trim();
            entry.Status = "Proposed";
        }

        string summary = string.IsNullOrWhiteSpace(envelope?.Summary)
            ? string.Join("\n\n", lore.Select(x => $"{x.Name}\n{x.Detail}"))
            : envelope!.Summary.Trim();
        var update = new StoryMemoryUpdate { Lore = lore };

        onStage("Checking lore continuity…");
        StoryContinuityReport report = await _continuity.FinalCheckAsync(
            project, instruction, summary, memoryContext, providerId, model, cancellationToken);
        if (report.Verified && report.Issues.Count > 0)
            summary += "\n\nContinuity warnings:\n- " + string.Join("\n- ", report.Issues.Take(6));

        return new StoryActionResult
        {
            Response = summary + "\n\nThis is proposed lore. Use ‘Save as Proposed’ below to place it in Story Memory for later approval.",
            Action = StoryActionType.ExpandLore,
            ShouldAutoBookkeep = false,
            ContinuityVerified = !report.Verified || !report.RequiresRepair,
            ProposalKind = "Lore",
            ProposalJson = StoryProposalSerializer.SerializeUpdate(update),
            ResearchNoteCount = research.Notes.Count,
            ResearchSourceCount = research.Notes.Sum(n => n.Sources.Count)
        };
    }

    private async Task<StoryResearchBundle> GetResearchAsync(
        StoryProject project,
        string instruction,
        string memoryContext,
        string providerId,
        string model,
        Action<string> onStage,
        CancellationToken cancellationToken)
    {
        onStage("Research gate…");
        StoryResearchDecision decision = await _researchGate.EvaluateAsync(
            project, instruction, memoryContext, providerId, model, cancellationToken);
        if (!decision.NeedsResearch)
            return new StoryResearchBundle();
        onStage("Researching…");
        try
        {
            return await _research.ResearchAsync(project, decision, providerId, model, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("STORY-RESEARCH", $"Lore research skipped | project={project.Id} | {ex.Message}");
            return new StoryResearchBundle();
        }
    }

    private static void NormalizeCharacter(StoryCharacterPatch c)
    {
        c.Name = c.Name.Trim();
        c.Aliases ??= new List<string>();
        c.Goals ??= new List<string>();
        c.RemovedGoals ??= new List<string>();
        c.Fears ??= new List<string>();
        c.Secrets ??= new List<string>();
        c.Knowledge ??= new List<string>();
        c.ForgottenKnowledge ??= new List<string>();
        c.Abilities ??= new List<string>();
        c.RemovedAbilities ??= new List<string>();
        c.Inventory ??= new List<string>();
        c.RemovedInventory ??= new List<string>();
    }

    private static string BuildCharacterFallback(StoryCharacterPatch c) =>
        $"{c.Name}\n{c.Personality}\nGoals: {string.Join(", ", c.Goals)}\nNotes: {c.Notes}".Trim();
}
