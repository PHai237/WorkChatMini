using System.Text;

namespace WorkChatMini;

public sealed class StoryWritingPipeline
{
    private const int MaxContextMessages = 20;
    private const int MaxConversationCharacters = 32000;

    private readonly StoryAiOrchestrator _direct = new();
    private readonly StoryResearchGate _researchGate = new();
    private readonly StoryResearchCoordinator _research = new();
    private readonly StoryDraftPreparationStage _preparation = new();
    private readonly StoryContinuityChecker _continuity = new();
    private readonly StoryWriterPipelineStage _writer = new();

    public async Task<StoryActionResult> RunAsync(
        StoryProject project,
        IReadOnlyList<StoryMessage> messages,
        string userInstruction,
        string providerId,
        string model,
        string memoryContext,
        StoryActionType action,
        Action<string> onDelta,
        Action<string> onStage,
        CancellationToken cancellationToken,
        bool skipRoutineResearchGate = false)
    {
        bool useWritingPipeline = StoryActions.UsesWritingPipeline(action);
        if (!useWritingPipeline)
        {
            string response = await _direct.StreamReplyAsync(
                project, messages, providerId, model, memoryContext, onDelta, cancellationToken);
            return new StoryActionResult { Response = response, Action = action, UsedWritingPipeline = false, ShouldAutoBookkeep = StoryActions.MayAutoBookkeep(action) };
        }

        string recentConversation = BuildConversationContext(messages, userInstruction);

        StoryResearchDecision decision;
        if (action == StoryActionType.ContinueStory && skipRoutineResearchGate)
        {
            // Ordinary "continue" supplies no real-world question. Skip this
            // utility API round trip; preserve preparation/draft/editor and
            // full research routing for any substantive creator direction.
            decision = new StoryResearchDecision
            {
                NeedsResearch = false,
                Reason = "Routine continuation has no external fact request."
            };
            AppLogger.Info("STORY-RESEARCH", "Routine Continue Story bypassed the research gate; no external factual request.");
        }
        else
        {
            onStage("Research gate…");
            decision = await _researchGate.EvaluateAsync(
                project, userInstruction, memoryContext, providerId, model, cancellationToken);
        }

        StoryResearchBundle research = new();
        if (decision.NeedsResearch)
        {
            onStage("Researching…");
            try
            {
                research = await _research.ResearchAsync(project, decision, providerId, model, cancellationToken);
                AppLogger.Info("STORY-RESEARCH",
                    $"Research completed | project={project.Id} | fresh={research.FreshSearches} | cached={research.CachedSearches} | sources={research.Notes.Sum(n => n.Sources.Count)}");
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                // Research improves accuracy but must never make writing unavailable.
                AppLogger.Warn("STORY-RESEARCH", $"Research skipped after provider/search error | project={project.Id} | {ex.Message}");
                research = new StoryResearchBundle();
            }
        }

        // Plan + continuity pre-check used to be two separate network round trips.
        // They are private reasoning stages over the same inputs, so prepare both in one compact pass.
        onStage("Preparing draft…");
        StoryDraftPreparation preparation = await _preparation.PrepareAsync(
            project, userInstruction, recentConversation, memoryContext, research.Context,
            providerId, model, cancellationToken,
            continueStory: action == StoryActionType.ContinueStory);

        onStage("Drafting…");
        string draft = await _writer.DraftAsync(
            project, userInstruction, recentConversation, memoryContext, research.Context,
            preparation.Plan, preparation.Constraints, providerId, model, cancellationToken,
            continueStory: action == StoryActionType.ContinueStory);

        onStage("Editing…");
        string edited = await _writer.EditAndStreamAsync(
            project, userInstruction, memoryContext, research.Context, preparation.Plan, preparation.Constraints, draft,
            providerId, model, onDelta, cancellationToken,
            continueStory: action == StoryActionType.ContinueStory);

        bool repaired = false;
        bool continuityVerified = false;
        string final = edited;
        try
        {
            onStage("Final continuity check…");
            StoryContinuityReport finalCheck = await _continuity.FinalCheckAsync(
                project, userInstruction, edited, memoryContext, providerId, model, cancellationToken);
            continuityVerified = finalCheck.Verified;

            if (finalCheck.RequiresRepair && finalCheck.Issues.Count > 0)
            {
                onStage("Repairing continuity…");
                string repairedDraft = await _writer.RepairAsync(
                    project, userInstruction, memoryContext, edited, finalCheck.Issues,
                    providerId, model, cancellationToken);
                if (!string.IsNullOrWhiteSpace(repairedDraft))
                {
                    final = repairedDraft;
                    repaired = true;
                    onStage("Verifying continuity…");
                    StoryContinuityReport verification = await _continuity.FinalCheckAsync(
                        project, userInstruction, final, memoryContext, providerId, model, cancellationToken);
                    continuityVerified = verification.Verified && !verification.RequiresRepair;
                    if (verification.RequiresRepair && verification.Issues.Count > 0)
                    {
                        AppLogger.Warn("STORY-CONTINUITY",
                            $"Continuity repair still has flagged issues | project={project.Id} | issues={string.Join(" | ", verification.Issues.Take(4))}");
                    }
                }
                else
                {
                    continuityVerified = false;
                }
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The polished candidate is still useful. Keep it visible/persisted, but do not
            // let unverifiable output silently become durable Story Memory via bookkeeping.
            continuityVerified = false;
            final = edited;
            repaired = false;
            AppLogger.Warn("STORY-CONTINUITY",
                $"Final continuity stage failed; keeping edited output without bookkeeping | project={project.Id} | {ex.Message}");
        }

        return new StoryActionResult
        {
            Response = final,
            Action = action,
            UsedWritingPipeline = true,
            ResearchSourceCount = research.Notes.Sum(note => note.Sources.Count),
            ResearchNoteCount = research.Notes.Count,
            RepairedAfterFinalCheck = repaired,
            ContinuityVerified = continuityVerified
        };
    }

    private static string BuildConversationContext(IReadOnlyList<StoryMessage> messages, string currentInstruction)
    {
        var selected = new List<StoryMessage>();
        var skippedDraftIndexes = new HashSet<int>();
        for (int i = 0; i < messages.Count; i++)
        {
            StoryMessage candidate = messages[i];
            if (!string.Equals(candidate.Role, "assistant", StringComparison.OrdinalIgnoreCase) ||
                (!candidate.IsNarrativeDraft && !candidate.IsInterruptedResponse) ||
                candidate.ManuscriptCommitted)
            {
                continue;
            }
            skippedDraftIndexes.Add(i);
            if (i > 0 && string.Equals(messages[i - 1].Role, "user", StringComparison.OrdinalIgnoreCase) &&
                (candidate.IsNarrativeDraft ||
                    (StoryActions.TryParse(candidate.Action, out StoryActionType action) &&
                     StoryActions.IsNarrativeAction(action))))
                skippedDraftIndexes.Add(i - 1);
        }

        int characters = 0;
        int start = messages.Count - 1;
        if (start >= 0 &&
            string.Equals(messages[start].Role, "user", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(messages[start].Content, currentInstruction, StringComparison.Ordinal))
        {
            start--;
        }

        for (int i = start; i >= 0 && selected.Count < MaxContextMessages; i--)
        {
            if (skippedDraftIndexes.Contains(i))
                continue;
            StoryMessage message = messages[i];
            if (message.IsInterruptedResponse)
                continue;
            if (characters + message.Content.Length > MaxConversationCharacters && selected.Count > 0)
                break;
            selected.Add(message);
            characters += message.Content.Length;
        }
        selected.Reverse();

        var sb = new StringBuilder();
        foreach (StoryMessage message in selected)
        {
            string role = string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? "Assistant" : "Creator";
            sb.AppendLine($"{role}: {message.Content}");
        }
        return sb.ToString().Trim();
    }
}
