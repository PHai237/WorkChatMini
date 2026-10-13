using System.Windows;

namespace WorkChatMini;

public partial class StoryPanel
{
    private async Task RepairPartialMemoryAsync()
    {
        if (_activeProject is null || _settings is null || _generationCts is not null ||
            _checkpointRecoveryFailed || _storageAuditBusy || _creativeDecisionBusy ||
            _loadingProject || !StoryMemoryRepair.CanRepair(_memory)) return;
        IStoryAiProvider provider = StoryProviderFactory.Get(_settings.StoryProvider);
        if (!StoryCredentialStore.HasKey(provider.Id))
        {
            SetStatus("Connect an AI provider before repairing Memory. No data was changed.");
            OpenInspector("Settings");
            return;
        }
        StoryProject project = _activeProject;
        string projectId = project.Id;
        StoryMemoryDocument baseline = _memory;
        CancellationTokenSource operation = new();
        _generationCts = operation;
        CancellationToken token = operation.Token;
        UpdateMemoryRecoveryButton();
        SetGeneratingState(true);
        bool saved = false;
        try
        {
            StoryMemoryDocument candidate = await StoryMemoryStore.CloneForCommitAsync(baseline, token);
            StoryChapterMemory[] missing = StoryMemoryRepair.PendingChapters(candidate).ToArray();
            int newCharacters = 0;
            int newLocations = 0;
            var examined = new List<(StoryChapterMemory Chapter, string Fingerprint)>();
            for (int i = 0; i < missing.Length; i++)
            {
                token.ThrowIfCancellationRequested();
                StoryChapterMemory chapter = missing[i];
                SetStatus($"Repairing missing Story Memory · chapter {chapter.Number} ({i + 1}/{missing.Length})…");
                string text = await StoryMemoryRecovery.ReadCommittedChapterAsync(_memoryStore, projectId, chapter, token);
                examined.Add((chapter, await Task.Run(() => StoryChapterEvidenceFingerprint.ForProse(text), token)));
                // Only earlier committed chapter metadata. Supplying today's
                // structured memory would inject future scene knowledge into a
                // historical chapter extraction and risk reverse causality.
                var historical = new StoryMemoryDocument
                {
                    Chapters = candidate.Chapters.Where(x => x.Number <= chapter.Number)
                        .OrderBy(x => x.Number).ToList()
                };
                StoryMemoryUpdate? extracted = await _bookkeeper.ExtractAsync(
                    project, historical,
                    "Identify explicitly mentioned character and place names in this committed chapter only. " +
                    "Do not guess identities, imply later events, or fill omitted prose. " +
                    "Existing canon and state must not be altered.",
                    text, provider.Id, _settings.StoryModel, StoryActionType.ContinueStory, token);
                if (extracted is null)
                    throw new InvalidOperationException($"Chapter {chapter.Number} returned invalid Memory JSON.");
                StoryMemoryUpdate safe = await Task.Run(
                    () => StoryMemoryRepair.NewNamesOnly(candidate, extracted, text), token);
                // No retcon, events, relationship, scene movement, inventory,
                // location status or character attributes are ever applied.
                StoryDeterministicReport check = StoryDeterministicContinuity.ValidateUpdate(candidate, safe);
                if (check.HasConflict)
                    throw new InvalidOperationException($"Unsafe addition in chapter {chapter.Number}; original Memory preserved.");
                StoryMemoryRepair.ApplySafeNames(candidate, safe, chapter);
                newCharacters += safe.Characters.Count;
                newLocations += safe.Locations.Count;
                // This operation checked only names, not the historical canon,
                // events or timelines. Never falsely promote it to Reviewed.
                chapter.BookkeepingStatus = "NamesOnly";
            }
            await StoryChapterEvidenceFingerprint.EnsureUnchangedAsync(
                _memoryStore, projectId, examined, token);
            token.ThrowIfCancellationRequested();
            if (_activeProject?.Id != projectId || !ReferenceEquals(_memory, baseline))
                throw new InvalidOperationException("Active Story changed during repair; nothing was saved.");
            // Even when no new names were found, the audit receipts are durable
            // so the same expensive AI requests will not repeat on every opening.
            await _checkpoints.CaptureAsync(projectId, baseline, _messages,
                "Before partial Story Memory repair", token);
            await _memoryStore.SaveAsync(projectId, candidate, token);
            saved = true;
            _memory = candidate;
            UpdateMemoryButton();
            UpdateProjectSummaryUi();
            AppLogger.Info("STORY-MEMORY", $"Partial Memory repair committed | project={projectId} | scanned={missing.Length} | newCharacters={newCharacters} | newLocations={newLocations} | existingCanonUntouched=true");
            int remaining = StoryMemoryRepair.PendingCount(candidate);
            SetStatus($"Name-only scan: {missing.Length} chapters, {newCharacters} characters, {newLocations} places added. " +
                $"{remaining} chapters still awaiting a name scan. Canon/events were not reviewed.");
        }
        catch (OperationCanceledException)
        {
            SetStatus("Memory repair cancelled. Existing data was not changed.");
        }
        catch (Exception ex)
        {
            AppLogger.Error("STORY-MEMORY", "Partial Memory repair failed", ex);
            SetStatus(saved ? "Memory saved but UI refresh failed. Reopen this Story."
                : "Memory repair failed. Existing Story Memory preserved; see Logs.");
        }
        finally
        {
            operation.Dispose();
            if (ReferenceEquals(_generationCts, operation)) _generationCts = null;
            SetGeneratingState(false);
            if (_activeProject?.Id == projectId && InspectorPanel.Visibility == Visibility.Visible)
                RenderMemoryInspector(_activeInspectorSection ?? "Overview");
        }
    }
}
