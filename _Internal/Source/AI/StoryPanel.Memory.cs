using System.Diagnostics;
using System.Windows;

namespace WorkChatMini;

public partial class StoryPanel
{
    private readonly StoryMemoryStore _memoryStore = new();
    private readonly StoryMemoryBookkeeper _bookkeeper = new();
    private readonly StoryCheckpointStore _checkpoints = new();
    private StoryMemoryDocument _memory = new();
    private bool _bookkeepingExtractionIncomplete;

    private async Task LoadStoryMemoryAsync(StoryProject project)
    {
        _memoryRecoveryConfirmed = false;
        _memory = await _memoryStore.LoadAsync(project.Id);
        LogDeterministicIntegrity("load", StoryDeterministicContinuity.AnalyzeMemory(_memory));
        UpdateMemoryButton();
        UpdateProjectSummaryUi();
    }

    private async Task<string> BuildStoryMemoryContextAsync(
        string prompt,
        StoryActionType action,
        CancellationToken cancellationToken)
    {
        if (_activeProject is null)
            return string.Empty;

        bool autoContinue = action == StoryActionType.ContinueStory;
        var elapsed = Stopwatch.StartNew();
        StoryRetrievalPlan plan = StoryRetrievalPlanner.Build(_memory, prompt, autoContinue);
        long planMs = elapsed.ElapsedMilliseconds;
        AppLogger.Info("STORY-RETRIEVAL",
            $"Retrieval plan | project={_activeProject.Id} | action={action} | terms={plan.Terms.Count} | chapterBoosts={plan.ChapterBoosts.Count} | focusReasons={plan.FocusReasons.Count}");
        // A brand-new co-created story has no committed chapters to search.
        // Do not initialize/scan its chapter index on every brainstorming turn.
        IReadOnlyList<StoryChapterSearchHit> hits = _memory.Chapters.Count == 0
            ? Array.Empty<StoryChapterSearchHit>()
            : await _memoryStore.SearchChapterTextAsync(
                _activeProject.Id, plan, maxResults: autoContinue ? 5 : 4, cancellationToken);
        long searchMs = elapsed.ElapsedMilliseconds - planMs;
        StoryDeterministicReport stateIntegrity = StoryDeterministicContinuity.AnalyzeMemory(_memory);
        string deterministicContext = stateIntegrity.BuildPromptContext();
        string coverageWarning = StoryMemoryRepair.BuildCoverageWarning(_memory);
        if (coverageWarning.Length > 0)
            deterministicContext = coverageWarning + (deterministicContext.Length == 0
                ? string.Empty : "\n" + deterministicContext);
        string narrativeContext = autoContinue ? StoryNarrativeFocus.Build(_memory) : string.Empty;
        string retrievalFocus = plan.BuildFocusContext();
        string creativeDirections = StoryCoCreation.BuildContext(_messages);
        int memoryAllowance = StoryContextBudget.MemoryAllowance(
            deterministicContext, narrativeContext, retrievalFocus, plan.KnowledgeGraphContext, creativeDirections);
        string memoryContext = StoryMemoryRetriever.BuildContext(
            _memory, plan.ExpandedQuery, hits, memoryAllowance, plan);
        string context = StoryContextBudget.Compose(
            memoryContext, deterministicContext, narrativeContext,
            retrievalFocus, plan.KnowledgeGraphContext, creativeDirections);
        AppLogger.Info("STORY-RETRIEVAL", $"Writer context prepared | project={_activeProject.Id} | characters={context.Length} | cap={StoryContextBudget.MaxTotalCharacters} | chapterHits={hits.Count} | planMs={planMs} | chapterSearchMs={searchMs} | contextMs={elapsed.ElapsedMilliseconds - planMs - searchMs} | totalMs={elapsed.ElapsedMilliseconds}");
        return context;
    }

    private async Task<StoryChapterMemory?> TryAutoBookkeepAsync(
        string userInstruction,
        string assistantResponse,
        string providerId,
        string model,
        StoryActionType action,
        CancellationToken cancellationToken,
        bool forceNarrativeCommit = false,
        StoryMessage? committingMessage = null)
    {
        if (_activeProject is null)
            return null;
        _bookkeepingExtractionIncomplete = false;

        bool narrativeAction = StoryActions.IsNarrativeAction(action);
        bool narrativeEligible = narrativeAction && assistantResponse.Length >= (action == StoryActionType.ContinueStory ? 250 : 500);
        bool commitNarrative = forceNarrativeCommit || narrativeEligible;
        bool extractMemory = _activeProject.AutoBookkeeping && ShouldBookkeep(action, userInstruction, assistantResponse);
        if (!commitNarrative && !extractMemory)
            return null;

        string pendingCheckpointId = string.Empty;
        bool commitStarted = false;
        try
        {
            StoryMemoryUpdate? update = null;
            if (extractMemory)
            {
                SetStatus("Bookkeeping…");
                try
                {
                    update = await _bookkeeper.ExtractAsync(
                        _activeProject,
                        _memory,
                        userInstruction,
                        assistantResponse,
                        providerId,
                        model,
                        action,
                        cancellationToken);
                    if (update is null)
                    {
                        _bookkeepingExtractionIncomplete = true;
                        AppLogger.Warn("STORY-MEMORY", $"Bookkeeping produced no usable update | project={_activeProject.Id}");
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _bookkeepingExtractionIncomplete = true;
                    // Manuscript persistence is independent of AI bookkeeping. A valid final narrative
                    // must still be committable even if the utility model/network is temporarily unavailable.
                    AppLogger.Warn("STORY-MEMORY", $"Bookkeeping extraction failed; manuscript commit will continue without structured deltas | project={_activeProject.Id} | {ex.Message}");
                }
            }

            if (extractMemory && assistantResponse.Length > StoryMemoryBookkeeper.MaxBookkeepingCharacters)
            {
                // The model only saw sampled prose, so no fully reviewed
                // chapter receipt is possible even if the JSON was valid.
                _bookkeepingExtractionIncomplete = true;
            }

            if (commitNarrative)
            {
                update ??= new StoryMemoryUpdate();
                update.IsChapter = true;
                if (string.IsNullOrWhiteSpace(update.ChapterTitle))
                    update.ChapterTitle = $"Chapter {(_memory.Chapters.Count == 0 ? 1 : _memory.Chapters.Max(x => x.Number) + 1)}";
                if (string.IsNullOrWhiteSpace(update.ChapterSummary))
                    update.ChapterSummary = BuildFallbackChapterSummary(assistantResponse);
            }

            if (update is null)
                return null;

            StoryDeterministicReport deltaIntegrity = StoryDeterministicContinuity.ValidateUpdate(_memory, update);
            LogDeterministicIntegrity("bookkeeping-delta", deltaIntegrity);
            if (deltaIntegrity.HasConflict)
            {
                // Pruning unsafe model facts means this chapter still needs audit.
                _bookkeepingExtractionIncomplete = true;
                update = StoryDeterministicContinuity.SanitizeUnsafeUpdate(_memory, update);
                StoryDeterministicReport sanitizedIntegrity = StoryDeterministicContinuity.ValidateUpdate(_memory, update);
                LogDeterministicIntegrity("bookkeeping-delta-after-sanitize", sanitizedIntegrity);
                if (sanitizedIntegrity.HasConflict)
                {
                    AppLogger.Warn("STORY-CONTINUITY",
                        $"Unsafe structured bookkeeping delta suppressed after deterministic sanitize; chapter prose remains eligible for commit | project={_activeProject.Id}");
                    update = StoryDeterministicContinuity.ToSafeChapterOnlyUpdate(update);
                    _bookkeepingExtractionIncomplete = true;
                }
                else
                {
                    AppLogger.Warn("STORY-CONTINUITY",
                        $"Unsafe bookkeeping entries pruned; remaining structured delta will be merged | project={_activeProject.Id}");
                }
            }

            // Do not mutate the active Story Memory while preparing a chapter or
            // bookkeeping-only update. On IO failure its in-memory state must still
            // match what a subsequent reload would read from disk.
            StoryMemoryDocument candidateMemory = await StoryMemoryStore.CloneForCommitAsync(
                _memory, cancellationToken);

            string source = action == StoryActionType.ContinueStory
                ? "Auto story continuation"
                : action == StoryActionType.DraftScene
                    ? "Committed generated narrative"
                    : "Generated narrative";
            StoryChapterMemory? savedChapter = null;
            if (update.IsChapter)
            {
                // The checkpoint must be durable before any committed chapter or canon changes.
                // A failed checkpoint aborts the mutation instead of allowing an unrewindable commit.
                pendingCheckpointId = await _checkpoints.CaptureAsync(
                    _activeProject.Id, _memory, _messages, "Before narrative commit", cancellationToken);
                await _checkpoints.BeginCommitAsync(_activeProject.Id, pendingCheckpointId, cancellationToken);
                commitStarted = true;
                savedChapter = await _memoryStore.AppendChapterAsync(
                    _activeProject.Id,
                    candidateMemory,
                    update.ChapterTitle,
                    update.ChapterSummary,
                    assistantResponse,
                    cancellationToken);
                if (savedChapter is not null)
                {
                    savedChapter.CheckpointId = pendingCheckpointId;
                    savedChapter.BookkeepingStatus = !extractMemory ? "Skipped"
                        : _bookkeepingExtractionIncomplete ? "Incomplete" : "Reviewed";
                    source = $"Chapter {savedChapter.Number}: {savedChapter.Title}";
                }
            }

            StoryMemoryMerger.Apply(candidateMemory, update, source, savedChapter);
            LogDeterministicIntegrity("post-merge", StoryDeterministicContinuity.AnalyzeMemory(candidateMemory));
            await _memoryStore.SaveAsync(_activeProject.Id, candidateMemory, cancellationToken);
            if (commitStarted)
            {
                if (savedChapter is null)
                    throw new InvalidOperationException("Narrative commit did not produce a chapter.");
                if (committingMessage is not null)
                {
                    MarkManuscriptCommitted(committingMessage, savedChapter);
                    foreach (StoryMessage sibling in _messages)
                    {
                        if (!ReferenceEquals(sibling, committingMessage) &&
                            StoryDraftIsolation.AreSiblings(committingMessage, sibling) && !sibling.ManuscriptCommitted)
                            sibling.DraftDiscarded = true;
                    }
                }
                await _store.SaveMessagesAsync(_activeProject.Id, _messages, cancellationToken);
                await _checkpoints.CompleteCommitAsync(_activeProject.Id);
                pendingCheckpointId = string.Empty;
                commitStarted = false;
            }
            _memory = candidateMemory;
            UpdateMemoryButton();
            UpdateProjectSummaryUi();
            AppLogger.Info("STORY-MEMORY",
                $"Bookkeeping/manuscript saved | project={_activeProject.Id} | chapter={(savedChapter?.Number.ToString() ?? "none")} | chapters={_memory.Chapters.Count} | canon={_memory.Canon.Count} | characters={_memory.Characters.Count} | events={_memory.Events.Count} | arcs={_memory.Arcs.Count} | characterArcs={_memory.CharacterArcs.Count} | relationshipChanges={_memory.RelationshipHistory.Count} | locations={_memory.Locations.Count} | scenes={_memory.SceneStates.Count} | revisions={_memory.Revisions.Count} | threads={_memory.Threads.Count(x => x.Status == "Open")}");
            return savedChapter;
        }
        catch (OperationCanceledException)
        {
            if (commitStarted)
                await RollBackFailedCommitAsync();
            AppLogger.Info("STORY-MEMORY", $"Bookkeeping/manuscript cancelled | project={_activeProject.Id}");
            throw;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("STORY-MEMORY", $"Bookkeeping/manuscript skipped after generation | project={_activeProject.Id} | {ex.Message}");
            if (commitStarted)
                await RollBackFailedCommitAsync();
            return null;
        }
    }

    private async Task RollBackFailedCommitAsync()
    {
        if (_activeProject is null)
            return;
        try
        {
            await _checkpoints.AbortCommitAsync(_activeProject.Id);
            _memory = await _memoryStore.LoadAsync(_activeProject.Id);
            _messages.Clear();
            _messages.AddRange(await _store.LoadMessagesAsync(_activeProject.Id));
            UpdateMemoryButton();
            RenderConversation();
        }
        catch (Exception ex)
        {
            _checkpointRecoveryFailed = true;
            AppLogger.Error("STORY-CHECKPOINT", "Failed commit restore requires recovery", ex);
            SetStatus("Error · Chapter transaction interrupted. Reopen story for recovery.");
        }
    }

    private static string BuildFallbackChapterSummary(string text)
    {
        string compact = string.Join(" ", text
            .Split(new[] { '\r', '\n', '\t' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(x => x.Trim())
            .Where(x => x.Length > 0));
        return compact.Length <= 500 ? compact : compact[..500] + "…";
    }

    private void MemoryButton_Click(object sender, RoutedEventArgs e) => OpenInspector("Overview");

    private void UpdateMemoryButton()
    {
        if (MemoryButton is null)
            return;
        if (_activeProject is null)
        {
            MemoryButton.Content = "Memory";
            MemoryButton.IsEnabled = false;
            return;
        }

        int proposed = _memory.Canon.Count(x => string.Equals(x.Status, "Proposed", StringComparison.OrdinalIgnoreCase))
                       + _memory.Lore.Count(x => string.Equals(x.Status, "Proposed", StringComparison.OrdinalIgnoreCase))
                       + _memory.Events.Count(x => string.Equals(x.Status, "Proposed", StringComparison.OrdinalIgnoreCase));
        MemoryButton.Content = "Memory";
        MemoryButton.IsEnabled = true;
        MemoryButton.ToolTip = $"Story Memory\n{_memory.Chapters.Count} chapters · {_memory.Characters.Count} characters · {_memory.Canon.Count(x => !IsRetiredCanon(x.Status))} current canon facts · {_memory.Events.Count} significant events · {_memory.Arcs.Count} story arcs · {_memory.CharacterArcs.Count} character arcs · {_memory.Locations.Count} locations · {_memory.Revisions.Count} revisions · {_memory.Threads.Count(x => x.Status == "Open")} open threads"
            + (proposed > 0 ? $"\n{proposed} proposed item(s) need review" : string.Empty)
            + $"\nAuto-bookkeeping: {(_activeProject.AutoBookkeeping ? "On" : "Off")}"
            + (StoryMemoryRepair.UnreviewedCount(_memory) is int missed && missed > 0
                ? $"\n{missed} chapters lack full extraction ({StoryMemoryRepair.NamesOnlyCount(_memory)} scanned for names only)" : string.Empty);
    }


    private static void LogDeterministicIntegrity(string phase, StoryDeterministicReport report)
    {
        if (report.Issues.Count == 0)
            return;
        string detail = string.Join(" | ", report.Issues.Take(8).Select(x => $"{x.Severity}:{x.Code}:{x.Message}"));
        if (report.HasConflict)
            AppLogger.Warn("STORY-CONTINUITY", $"Deterministic state conflict | phase={phase} | {detail}");
        else if (report.HasRisk)
            AppLogger.Warn("STORY-CONTINUITY", $"Deterministic state risk | phase={phase} | {detail}");
        else
            AppLogger.Info("STORY-CONTINUITY", $"Deterministic state note | phase={phase} | {detail}");
    }


    private static bool ShouldBookkeep(StoryActionType action, string prompt, string response)
    {
        // Brainstorming in Chat must never modify confirmed Memory merely because the
        // creator mentions a chapter/scene or Gemini's answer is long. Only explicit
        // narrative actions are eligible for automatic structured bookkeeping.
        return action switch
        {
            StoryActionType.ContinueStory => response.Length >= 250,
            StoryActionType.DraftScene => response.Length >= 500,
            _ => false
        };
    }

}
