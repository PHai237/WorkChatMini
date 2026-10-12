using System.Text;

namespace WorkChatMini;

public sealed class StoryWriterPipelineStage
{
    private readonly StoryAiStageRunner _runner = new();

    public Task<string> DraftAsync(
        StoryProject project,
        string userInstruction,
        string recentConversation,
        string memoryContext,
        string researchContext,
        string plan,
        string continuityConstraints,
        string providerId,
        string model,
        CancellationToken cancellationToken,
        bool continueStory = false,
        bool discussionOnly = false)
    {
        string systemPrompt = StoryPromptBuilder.BuildBase(project, memoryContext, researchContext) + """

You are the private DRAFT stage. Create a strong complete candidate for the creator's requested deliverable.
Follow the plan and continuity constraints, but the creator's explicit current instruction remains authoritative unless it conflicts with confirmed canon.
Do not mention planning, memory, research, or this pipeline. Return only the requested creative deliverable.
""" + (continueStory ? StoryNarrativeCraftGuide.ForDraft + StoryLiteraryCraft.ForDraft +
            StoryLiteraryCraft.GenreNuance(project) : string.Empty)
            + (discussionOnly ? StoryBrainstormingCraft.ForDraft : string.Empty);
        string input = BuildWriterInput(userInstruction, recentConversation, plan, continuityConstraints, null);
        return _runner.CompleteAsync(providerId, model, systemPrompt,
            new[] { new StoryMessage { Role = "user", Content = input } }, cancellationToken,
            temperature: continueStory ? 0.65 : 0.75, workload: StoryAiWorkload.Creative, stageName: "draft");
    }

    public Task<string> EditAndStreamAsync(
        StoryProject project,
        string userInstruction,
        string memoryContext,
        string researchContext,
        string plan,
        string continuityConstraints,
        string draft,
        string providerId,
        string model,
        Action<string> onDelta,
        CancellationToken cancellationToken,
        bool continueStory = false,
        bool discussionOnly = false,
        string crossChapterWatch = "")
    {
        string systemPrompt = StoryPromptBuilder.BuildBase(project, memoryContext, researchContext) + """

You are the EDITOR PASS and final-facing writer.
Polish the candidate into the finished answer without reimagining its plot. Preserve the draft's scene decisions, outcomes and quiet endings.
Preserve plot facts and intent. Improve awkward prose, repetition, pacing, dialogue naturalness, exposition, POV consistency, character voice, clarity, and tonal drift.
Honor all continuity constraints and character-specific knowledge.
Do not add commentary about the edit. Do not mention research, memory, plan, stages, or hidden checks. Output only the finished requested deliverable.
""" + (continueStory ? StoryNarrativeCraftGuide.ForEditor + StoryLiteraryCraft.ForEditor +
            StoryLiteraryCraft.GenreNuance(project) : string.Empty)
            + (discussionOnly ? StoryBrainstormingCraft.ForEditor : string.Empty);
        string input = BuildWriterInput(userInstruction, string.Empty, plan, continuityConstraints, draft);
        if (continueStory)
        {
            string hints = StoryProseRepetitionHints.Analyze(draft);
            if (!string.IsNullOrEmpty(hints))
                input = hints + "\n" + input;
            if (!string.IsNullOrWhiteSpace(crossChapterWatch))
                input = crossChapterWatch + "\n" + input;
        }
        return _runner.StreamAsync(providerId, model, systemPrompt,
            new[] { new StoryMessage { Role = "user", Content = input } }, onDelta, cancellationToken,
            temperature: 0.35, workload: StoryAiWorkload.Creative, stageName: "editor");
    }

    public Task<string> RepairAsync(
        StoryProject project,
        string userInstruction,
        string memoryContext,
        string finalDraft,
        IReadOnlyList<string> issues,
        string providerId,
        string model,
        CancellationToken cancellationToken)
    {
        string systemPrompt = StoryPromptBuilder.BuildBase(project, memoryContext) + """

You are the CONTINUITY REPAIR stage. Make the smallest necessary corrections to the candidate so every listed concrete continuity issue is fixed.
Preserve style, pacing, structure, plot intent, and all unaffected wording as much as practical.
Do not use continuity repair to invent a relationship reversal, new motivation or forced emotional resolution. If a prior relationship turn matters, correct only the concrete mismatch; preserve earned ambiguity.
Do not mention the repair process. Output only the corrected final deliverable.
""" + StoryLiteraryCraft.ForContinuityRepair;
        var input = new StringBuilder();
        input.AppendLine("CREATOR INSTRUCTION:");
        input.AppendLine(userInstruction);
        input.AppendLine();
        input.AppendLine("ISSUES TO FIX:");
        foreach (string issue in issues)
            input.AppendLine("- " + issue);
        input.AppendLine();
        input.AppendLine("CANDIDATE:");
        input.Append(Trim(finalDraft, 60000));

        return _runner.CompleteAsync(providerId, model, systemPrompt,
            new[] { new StoryMessage { Role = "user", Content = input.ToString() } }, cancellationToken,
            temperature: 0.2, workload: StoryAiWorkload.Creative, stageName: "continuity-repair");
    }

    private static string BuildWriterInput(
        string userInstruction,
        string recentConversation,
        string plan,
        string continuityConstraints,
        string? draft)
    {
        var sb = new StringBuilder();
        sb.AppendLine("CREATOR INSTRUCTION:");
        sb.AppendLine(userInstruction);
        sb.AppendLine();
        if (!string.IsNullOrWhiteSpace(recentConversation))
        {
            sb.AppendLine("RECENT CONVERSATION:");
            sb.AppendLine(Trim(recentConversation, 10000));
            sb.AppendLine();
        }
        sb.AppendLine("PLAN:");
        sb.AppendLine(Trim(plan, 14000));
        sb.AppendLine();
        sb.AppendLine("CONTINUITY CONSTRAINTS:");
        sb.AppendLine(Trim(continuityConstraints, 12000));
        if (draft is not null)
        {
            sb.AppendLine();
            sb.AppendLine("CANDIDATE DRAFT TO EDIT:");
            sb.Append(Trim(draft, 60000));
        }
        return sb.ToString();
    }

    private static string Trim(string value, int max) => value.Length <= max ? value : value[..max] + "…";
}
