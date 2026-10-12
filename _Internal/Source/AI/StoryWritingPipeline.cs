namespace WorkChatMini;

public sealed class StoryWritingPipeline
{
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

        string recentConversation = StoryConversationContextBuilder.Build(messages, userInstruction);

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
        bool discussionOnly = action == StoryActionType.Chat;

        StoryDraftPreparation preparation = await _preparation.PrepareAsync(
            project, userInstruction, recentConversation, memoryContext, research.Context,
            providerId, model, cancellationToken,
            continueStory: action == StoryActionType.ContinueStory,
            discussionOnly: discussionOnly);

        // The scene brief remains private, bounded and non-canonical. It uses
        // the existing preparation/draft/editor stages; no extra model call.
        string workingPlan = action == StoryActionType.ContinueStory
            ? StoryLiteraryCraft.WithSceneBrief(preparation)
            : preparation.Plan;

        onStage("Drafting…");
        string draft = await _writer.DraftAsync(
            project, userInstruction, recentConversation, memoryContext, research.Context,
            workingPlan, preparation.Constraints, providerId, model, cancellationToken,
            continueStory: action == StoryActionType.ContinueStory,
            discussionOnly: discussionOnly);

        string crossChapterWatch = action == StoryActionType.ContinueStory
            ? StoryCrossChapterStyleWatch.Build(messages, draft)
            : string.Empty;
        onStage("Editing…");
        string edited = await _writer.EditAndStreamAsync(
            project, userInstruction, memoryContext, research.Context, workingPlan, preparation.Constraints, draft,
            providerId, model, onDelta, cancellationToken,
            continueStory: action == StoryActionType.ContinueStory,
            discussionOnly: discussionOnly,
            crossChapterWatch: crossChapterWatch);

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

}
