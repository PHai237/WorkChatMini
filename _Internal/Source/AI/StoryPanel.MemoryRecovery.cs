using System.Windows;

namespace WorkChatMini;

public partial class StoryPanel
{
    private bool _memoryRecoveryConfirmed;

    private void UpdateMemoryRecoveryButton()
    {
        if (InspectorRecoverMemoryButton is null) return;
        bool fromEmpty = StoryMemoryRecovery.CanRecover(_memory);
        int repairCount = StoryMemoryRepair.PendingChapters(_memory).Count;
        bool eligible = _activeProject is not null && (fromEmpty || repairCount > 0);
        InspectorRecoverMemoryButton.Visibility = eligible ? Visibility.Visible : Visibility.Collapsed;
        InspectorRecoverMemoryButton.Content = _memoryRecoveryConfirmed ? "Confirm Memory repair"
            : fromEmpty ? $"Recover missing Memory ({_memory.Chapters.Count} chapters)…"
            : $"Repair names ({repairCount} now / {StoryMemoryRepair.PendingCount(_memory)} pending)…";
        InspectorRecoverMemoryButton.IsEnabled = _generationCts is null && !_storageAuditBusy &&
            !_loadingProject && !_checkpointRecoveryFailed && !_creativeDecisionBusy;
    }

    private async void InspectorRecoverMemoryButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeProject is null || _settings is null ||
            _generationCts is not null || _loadingProject || _storageAuditBusy ||
            _checkpointRecoveryFailed || _creativeDecisionBusy ||
            (!StoryMemoryRecovery.CanRecover(_memory) && !StoryMemoryRepair.CanRepair(_memory))) return;
        if (!_memoryRecoveryConfirmed)
        {
            _memoryRecoveryConfirmed = true;
            UpdateMemoryRecoveryButton();
            SetStatus(StoryMemoryRecovery.CanRecover(_memory)
                ? "Recovery reads committed chapters and extracts missing Memory; press Confirm."
                : "This repairs names only, up to 20 chapters per batch. It does NOT verify events, relationships or canon. Press Confirm to start.");
            return;
        }
        _memoryRecoveryConfirmed = false;
        if (StoryMemoryRepair.CanRepair(_memory))
        {
            await RepairPartialMemoryAsync();
            return;
        }
        IStoryAiProvider provider = StoryProviderFactory.Get(_settings.StoryProvider);
        if (!StoryCredentialStore.HasKey(provider.Id))
        {
            SetStatus("Memory recovery requires a connected AI provider. No files were changed.");
            OpenInspector("Settings");
            return;
        }
        StoryProject project = _activeProject;
        string projectId = project.Id;
        _generationCts = new CancellationTokenSource();
        CancellationToken token = _generationCts.Token;
        UpdateMemoryRecoveryButton();
        SetGeneratingState(true);
        bool saved = false;
        try
        {
            // Recovery starts from the completely empty structured-memory graph.
            // Work on a detached copy and persist once, only after all chapters pass.
            StoryMemoryDocument baseline = _memory;
            StoryMemoryDocument candidate = await StoryMemoryStore.CloneForCommitAsync(baseline, token);
            int evidenceCount = 0;
            var examined = new List<(StoryChapterMemory Chapter, string Fingerprint)>();
            StoryChapterMemory[] chapters = candidate.Chapters.OrderBy(c => c.Number).ToArray();
            for (int chapterIndex = 0; chapterIndex < chapters.Length; chapterIndex++)
            {
                StoryChapterMemory chapter = chapters[chapterIndex];
                token.ThrowIfCancellationRequested();
                SetStatus($"Recovering Story Memory · chapter {chapter.Number} ({chapterIndex + 1}/{chapters.Length})…");
                string prose = await StoryMemoryRecovery.ReadCommittedChapterAsync(_memoryStore, projectId, chapter, token);
                examined.Add((chapter, await Task.Run(() => StoryChapterEvidenceFingerprint.ForProse(prose), token)));
                StoryMemoryDocument context = StoryMemoryRecovery.ContextThroughChapter(candidate, chapter.Number);
                StoryMemoryUpdate? update = await _bookkeeper.ExtractAsync(project, context,
                    "Recover only facts explicitly present in this already committed chapter. Never invent missing narrative.",
                    prose, provider.Id, _settings.StoryModel, StoryActionType.ContinueStory, token);
                if (update is null)
                    throw new InvalidOperationException($"AI did not provide usable bookkeeping for chapter {chapter.Number}.");
                update.IsChapter = false;
                update.Revisions.Clear(); // Never silently retcon committed story history.
                if (StoryMemoryRecovery.HasExtractedFacts(update)) evidenceCount++;
                StoryDeterministicReport report = StoryDeterministicContinuity.ValidateUpdate(candidate, update);
                if (report.HasConflict)
                    throw new InvalidOperationException($"Chapter {chapter.Number} conflicts with recovered canon; no changes were saved.");
                StoryMemoryMerger.Apply(candidate, update, $"Chapter {chapter.Number}: {chapter.Title} (recovered)", chapter);
                // Large prose was excerpted before extraction. Full coverage
                // cannot be claimed when middle passages were omitted.
                chapter.BookkeepingStatus = prose.Length > StoryMemoryBookkeeper.MaxBookkeepingCharacters
                    ? "Incomplete" : "Reviewed";
            }
            if (evidenceCount == 0 || !StoryMemoryRecovery.HasStructuredMemory(candidate))
                throw new InvalidOperationException("No structured facts were extracted. Nothing was saved.");
            await StoryChapterEvidenceFingerprint.EnsureUnchangedAsync(
                _memoryStore, projectId, examined, token);
            token.ThrowIfCancellationRequested();
            if (_activeProject?.Id != projectId || !ReferenceEquals(_memory, baseline))
                throw new InvalidOperationException("Story changed while recovery was running. Nothing was saved.");
            // Checkpoint first: manual recovery is reversible, no manuscript files
            // are added, removed, or modified. Atomic memory write is the last IO.
            await _checkpoints.CaptureAsync(projectId, baseline, _messages, "Before structured Memory recovery", token);
            await _memoryStore.SaveAsync(projectId, candidate, token);
            saved = true;
            _memory = candidate;
            UpdateMemoryButton();
            UpdateProjectSummaryUi();
            AppLogger.Info("STORY-MEMORY", $"Structured Memory recovered | project={projectId} | chapters={chapters.Length} | characters={candidate.Characters.Count} | events={candidate.Events.Count} | locations={candidate.Locations.Count}");
            SetStatus($"Recovered Memory from {chapters.Length} committed chapters. No chapter text was changed.");
        }
        catch (OperationCanceledException)
        {
            SetStatus("Memory recovery cancelled. No incomplete extraction was committed.");
        }
        catch (Exception ex)
        {
            AppLogger.Error("STORY-MEMORY", "Structured Memory recovery failed", ex);
            SetStatus(saved ? "Memory was saved, but UI refresh failed. Reopen this Story to refresh."
                : "Recovery did not complete; original Memory preserved. See Logs.");
        }
        finally
        {
            _generationCts?.Dispose();
            _generationCts = null;
            SetGeneratingState(false);
            if (_activeProject?.Id == projectId && InspectorPanel.Visibility == Visibility.Visible)
                RenderMemoryInspector(_activeInspectorSection ?? "Overview");
        }
    }
}
