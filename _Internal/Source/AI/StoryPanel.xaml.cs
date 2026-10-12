using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using WpfFontFamily = System.Windows.Media.FontFamily;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;

namespace WorkChatMini;

public partial class StoryPanel : System.Windows.Controls.UserControl
{
    private static readonly WpfFontFamily ChatFont = new("Segoe UI");
    private static readonly WpfFontFamily ReadingFont = new("Georgia");

    private readonly StoryProjectStore _store = new();
    private readonly StoryActionOrchestrator _actionOrchestrator = new();
    private readonly List<StoryProject> _projects = new();
    private readonly List<StoryMessage> _messages = new();
    private readonly object _streamSync = new();
    private readonly StringBuilder _pendingStream = new();
    // Full streaming text is retained for Stop/Copy, while its WPF preview is bounded.
    private readonly StringBuilder _streamAccumulated = new();
    private readonly DispatcherTimer _streamFlushTimer;
    private AppSettings? _settings;
    private StoryProject? _activeProject;
    private CancellationTokenSource? _generationCts;
    private TextBlock? _streamingTextBlock;
    private bool _initialized;
    private bool _loadingProject;
    private readonly StoryProjectSwitchQueue _projectSwitchQueue = new();
    private int _streamDispatchQueued;
    private int _streamGenerationId;
    private string _pendingAlternateGroupId = string.Empty;
    private bool _checkpointRecoveryFailed;

    public StoryPanel()
    {
        InitializeComponent();
        RoleBox.ItemsSource = StoryGenres.Roles;
        InitializeInspectorUi();
        _streamFlushTimer = new DispatcherTimer(DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(45)
        };
        _streamFlushTimer.Tick += (_, _) => FlushStreamingText();
    }

    public async Task InitializeAsync(AppSettings settings)
    {
        if (_initialized)
            return;
        _settings = settings;
        IReadOnlyList<StoryProject> loaded = await _store.LoadProjectsAsync();
        _projects.AddRange(loaded);
        ProjectBox.ItemsSource = _projects;
        UpdateProjectSelectorUi();
        RefreshProviderStatus();

        StoryProject? selected = _projects.FirstOrDefault(p => p.Id == settings.StoryActiveProjectId) ?? _projects.FirstOrDefault();
        if (selected is not null)
            ProjectBox.SelectedItem = selected;
        else
            UpdateEmptyState();
        _initialized = true;
    }

    public void FocusComposer()
    {
        InputBox.Focus();
        Keyboard.Focus(InputBox);
    }

    public bool HasPendingWorkForUpdate => _generationCts is not null || _loadingProject ||
        _storageAuditBusy || _creativeDecisionBusy || _checkpointRecoveryFailed;

    public void CancelGeneration() => _generationCts?.Cancel();

    private async void ProjectBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (ProjectBox.SelectedItem is StoryProject project)
            await ActivateProjectAsync(project);
    }

    private async Task ActivateProjectAsync(StoryProject project)
    {
        // SelectionChanged may fire repeatedly while a long JSON/index load awaits.
        // Do not ignore the last selection or run two restores against shared UI state.
        if (!_projectSwitchQueue.TryBegin(project.Id)) return;
        _loadingProject = true;
        try
        {
            while (true)
            {
                // Lock edits while loading/recovering, but keep the selector usable
                // so a newer selection can replace the pending one.
                InputBox.IsEnabled = false;
                StoryNavPanel.IsEnabled = false;
                NewProjectButton.IsEnabled = false;
                try
                {
                    await LoadProjectCoreAsync(project);
                }
                catch (Exception ex)
                {
                    _checkpointRecoveryFailed = true;
                    AppLogger.Error("STORY", $"Failed to activate project | project={project.Id}", ex);
                    SetStatus("Error · Story could not be loaded safely. See Logs before editing.");
                }
                string? nextId = _projectSwitchQueue.Complete();
                if (nextId is null) break;
                StoryProject? next = _projects.FirstOrDefault(item => item.Id == nextId);
                if (next is null)
                {
                    _projectSwitchQueue.Abort();
                    break;
                }
                project = next;
            }
        }
        finally
        {
            _projectSwitchQueue.Abort();
            _loadingProject = false;
            // Only the final successfully loaded project may become editable.
            SetGeneratingState(false);
            UpdateStorySurfaceState();
        }
    }

    private async Task LoadProjectCoreAsync(StoryProject project)
    {
        _pendingAlternateGroupId = string.Empty;
        _editingUserPrompt = null;
        ClearDirectorPreview();
        ResetConversationSearch();
        _activeProject = project;
        // Never keep the previous Story's chat visible while restoring a
        // different Story from disk (even when recovery later fails).
        _messages.Clear();
        _memory = new StoryMemoryDocument();
        ResetStoryResearch();
        MessagesPanel.Children.Clear();
        UpdateStorySurfaceState();
        try
        {
            // Restore journal must finish before loading either conversation or memory.
            await _checkpoints.RecoverIfNeededAsync(project.Id);
            await _branches.RecoverIfNeededAsync(project.Id);
            _checkpointRecoveryFailed = false;
        }
        catch (Exception ex)
        {
            _checkpointRecoveryFailed = true;
            _messages.Clear();
            _memory = new StoryMemoryDocument();
            ResetStoryResearch();
            RenderConversation(scrollToEnd: false);
            RenderCoCreationInspector();
            AppLogger.Error("STORY-CHECKPOINT", "Could not recover an interrupted restore", ex);
            SetStatus("Error · Story restore needs recovery. See Logs before making changes.");
            return;
        }
        _messages.Clear();
        try
        {
            _messages.AddRange(await _store.LoadMessagesAsync(project.Id));
        }
        catch (System.IO.InvalidDataException ex)
        {
            _checkpointRecoveryFailed = true;
            _memory = new StoryMemoryDocument();
            ResetStoryResearch();
            AppLogger.Error("STORY", "Conversation needs recovery before editing the story", ex);
            RenderConversation(scrollToEnd: false);
            RenderCoCreationInspector(); // Remove controls bound to the previous project's messages.
            SetStatus("Error · Conversation unreadable. Original file preserved; see Logs before editing.");
            SetGeneratingState(false);
            return;
        }
        _visibleConversationMessages = StoryConversationPaging.PageSize;
        _conversationPageEnd = 0;
        _visibleCreativeIdeas = 40;
        ResetIdeaOrganizerUi();
        try
        {
            await LoadStoryMemoryAsync(project);
        }
        catch (System.IO.InvalidDataException ex)
        {
            _checkpointRecoveryFailed = true;
            _memory = new StoryMemoryDocument();
            ResetStoryResearch();
            RenderConversation(scrollToEnd: false);
            RenderCoCreationInspector();
            AppLogger.Error("STORY-MEMORY", "Unreadable Story Memory blocks editing until recovery", ex);
            SetStatus("Error · Story Memory unreadable. Original file preserved; see Logs before editing.");
            return;
        }
        await LoadStoryResearchAsync(project);
        RoleBox.SelectedItem = StoryGenres.Roles.Contains(project.Role) ? project.Role : "Companion";
        RoleBox.Visibility = Visibility.Visible;
        RenderConversation();
        UpdateGenreSummary();
        UpdateProjectSummaryUi();
        RebuildProjectList();
        UpdateStorySurfaceState();
        if (_settings is not null)
        {
            _settings.StoryActiveProjectId = project.Id;
            _settings.Save();
        }
        SetSelectedStoryAction(StoryActionType.Chat);
        SetStatus(null);
        SetGeneratingState(false);
    }

    private void NewProjectButton_Click(object sender, RoutedEventArgs e) => BeginNewStoryEditor();

    private async void RoleBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingProject || _activeProject is null || RoleBox.SelectedItem is not string role)
            return;
        _activeProject.Role = role;
        await _store.SaveProjectAsync(_activeProject);
    }

    private void RefreshProviderStatus()
    {
        if (_settings is null)
            return;
        IStoryAiProvider provider = StoryProviderFactory.Get(_settings.StoryProvider);
        bool hasKey = StoryCredentialStore.HasKey(provider.Id);
        string model = string.IsNullOrWhiteSpace(_settings.StoryModel) ? StoryProviderFactory.DefaultModel(provider.Id) : _settings.StoryModel;
        string utilityModel = StoryModelRouter.Resolve(provider.Id, model, StoryAiWorkload.Utility);
        string routingLine = !string.Equals(model, utilityModel, StringComparison.OrdinalIgnoreCase)
            ? $"\nUtility stages: {utilityModel}"
            : string.Empty;
        ProviderButton.Content = hasKey ? $"{provider.Id} · Ready" : $"{provider.Id} · No key";
        ProviderButton.ToolTip = hasKey
            ? $"{provider.DisplayName} · {model}{routingLine}\nAPI key saved securely. Open Story Settings to change it."
            : $"{provider.DisplayName} · {model}{routingLine}\nNo API key is saved yet. Open Story Settings to connect it.";
        if (InspectorSettingsView is not null && InspectorSettingsView.Visibility == Visibility.Visible)
            UpdateInspectorConnectionStatus();
    }

    private async void SendButton_Click(object sender, RoutedEventArgs e) => await SendAsync();

    private async void InputBox_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Enter && !Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            e.Handled = true;
            await SendAsync();
        }
    }

    private async Task SendAsync(bool retryLast = false)
    {
        if (_loadingProject || _generationCts is not null || _checkpointRecoveryFailed || _storageAuditBusy || _creativeDecisionBusy)
            return;
        if (_activeProject is null)
        {
            BeginNewStoryEditor();
            return;
        }
        if (_settings is null)
            return;
        StoryMessage? retryPrompt = retryLast ? GetRetryablePrompt() : null;
        if (retryLast && retryPrompt is null) return;
        StoryActionType requestedAction = retryPrompt is not null &&
            StoryActions.TryParse(retryPrompt.Action, out StoryActionType savedAction)
                ? savedAction : _editingUserPrompt is not null &&
                  StoryActions.TryParse(_editingUserPrompt.Action, out StoryActionType editAction)
                    ? editAction : _selectedAction;
        string text = retryPrompt is null ? InputBox.Text.Trim() : retryPrompt.Content;
        if (_editingUserPrompt is not null && (GetEditablePrompt() != _editingUserPrompt || text.Length == 0))
        {
            SetStatus("Edit unavailable · finish the current draft or enter nonempty text.");
            return;
        }
        if (text.Length == 0 && requestedAction != StoryActionType.ContinueStory)
            return;
        if (text.Length == 0)
            text = "Continue the story.";

        // The opt-in Director gate runs BEFORE persisting any user turn or
        // creating narrative output. Only approval starts the normal pipeline.
        if (!retryLast && NeedsDirectorReview(text, requestedAction))
        {
            await PreviewDirectorAsync(text);
            return;
        }
        StoryMessage? previousInterrupted = retryLast && _messages.Count > 0 &&
            _messages[^1].IsInterruptedResponse ? _messages[^1] : null;
        string effectiveInstruction = previousInterrupted is not null &&
            !string.IsNullOrWhiteSpace(previousInterrupted.SourceInstruction)
            ? previousInterrupted.SourceInstruction
            : requestedAction == StoryActionType.ContinueStory
                ? StoryDirector.ComposeInstruction(text, _directorApprovedDirection ?? string.Empty)
                : text;

        IStoryAiProvider provider = StoryProviderFactory.Get(_settings.StoryProvider);
        if (!StoryCredentialStore.HasKey(provider.Id))
        {
            OpenInspector("Settings");
            SetStatus("Error · Connect an AI provider to continue.");
            return;
        }

        // Acquire the generation lease BEFORE the first disk await. A second
        // Enter/Send or a Story switch must never interleave the initial save.
        StoryProject project = _activeProject;
        _generationCts = new CancellationTokenSource();
        int streamGenerationId = Interlocked.Increment(ref _streamGenerationId);
        _streamFlushTimer.Stop();
        lock (_streamSync) _pendingStream.Clear();
        _streamAccumulated.Clear();
        // A new user turn always returns to the latest conversation; search
        // results and visual windows belong to the previous local snapshot.
        bool wasReadingSearchResult = _conversationFocusIndex is not null;
        ResetConversationSearch();
        if (wasReadingSearchResult || _conversationPageEnd != 0 ||
            _visibleConversationMessages != StoryConversationPaging.PageSize)
        {
            _conversationPageEnd = 0;
            _visibleConversationMessages = StoryConversationPaging.PageSize;
            RenderConversation();
        }
        SetGeneratingState(true);
        string alternateGroupId = StoryActions.IsNarrativeAction(requestedAction) ? _pendingAlternateGroupId : string.Empty;
        _pendingAlternateGroupId = string.Empty;
        if (!retryLast && _editingUserPrompt is not null)
        {
            StoryMessage original = _editingUserPrompt;
            StoryMessage? priorReply = GetCurrentReplyForPrompt(original);
            string previousText = original.Content;
            bool revisionSaved = false;
            try
            {
                // Checkpoint contains the original user prompt and full old AI reply.
                // Editing NEVER mutates an already committed chapter, approved proposal
                // or a previously accepted creative direction.
                await _checkpoints.CaptureAsync(project.Id, _memory, _messages,
                    "Before editing latest prompt", _generationCts.Token);
                original.Content = text;
                if (priorReply is not null) priorReply.IsSupersededResponse = true;
                await _store.SaveMessagesAsync(project.Id, _messages, _generationCts.Token);
                revisionSaved = true;
                _editingUserPrompt = null;
                InputBox.Clear();
                SetSelectedStoryAction(StoryActionType.Chat);
                RenderConversation();
                AppLogger.Info("STORY", $"Last prompt edited; previous answer archived | project={project.Id}");
            }
            catch (Exception ex)
            {
                if (!revisionSaved)
                {
                    original.Content = previousText;
                    if (priorReply is not null) priorReply.IsSupersededResponse = false;
                }
                AppLogger.Error("STORY", "Could not safely finish edited prompt", ex);
                SetStatus(revisionSaved
                    ? "Edited prompt saved, but regeneration did not start. Try Retry AI; see Logs."
                    : "Error · Edit was not applied. Original turn preserved; see Logs.");
                _generationCts.Dispose();
                _generationCts = null;
                SetGeneratingState(false);
                return;
            }
        }
        else if (!retryLast)
        {
        List<string> previousGenres = new(project.CoreGenres);
        InputBox.Clear();
        SetSelectedStoryAction(StoryActionType.Chat);
        var userMessage = new StoryMessage { Role = "user", Content = text, Action = requestedAction.ToString() };
        StoryCoCreation.CaptureCreatorConfirmation(userMessage, _messages);
        _messages.Add(userMessage);
        if (requestedAction == StoryActionType.Chat)
        {
            foreach (string genre in StoryGenreChatIntent.FindDeclaredGenres(text))
            {
                if (!_activeProject.CoreGenres.Contains(genre, StringComparer.OrdinalIgnoreCase))
                    _activeProject.CoreGenres.Add(genre);
            }
            UpdateGenreSummary();
        }
        UpdateStorySurfaceState();
        AddMessageBubble(userMessage);
        bool promptSaved = false;
        try
        {
            await _store.SaveMessagesAsync(project.Id, _messages, _generationCts.Token);
            promptSaved = true;
            await _store.SaveProjectAsync(project, _generationCts.Token);
        }
        catch (Exception ex)
        {
            // If the prompt was not persisted, restore the exact unsent input
            // and genre state. Once persisted, NEVER silently remove the turn.
            if (!promptSaved)
            {
                _messages.Remove(userMessage);
                project.CoreGenres.Clear();
                project.CoreGenres.AddRange(previousGenres);
                _pendingAlternateGroupId = alternateGroupId;
                InputBox.Text = text;
                SetSelectedStoryAction(requestedAction);
                RenderConversation(scrollToEnd: false);
                UpdateGenreSummary();
            }
            AppLogger.Error("STORY", "Could not persist initial Story turn safely", ex);
            SetStatus(promptSaved
                ? "Error · Your message was saved, but the request was not sent. Check Logs."
                : "Error · Could not save your message. Input restored; check Logs.");
            _generationCts.Dispose();
            _generationCts = null;
            SetGeneratingState(false);
            return;
        }
        }

        // This turn is durable. Clear the transient Director proposal before
        // another request or a different Story can accidentally reuse it.
        ClearDirectorPreview();
        var assistantMessage = new StoryMessage
        {
            Role = "assistant",
            Content = string.Empty,
            Action = requestedAction.ToString(),
            SourceInstruction = effectiveInstruction,
            DraftGroupId = alternateGroupId,
            DraftVersionId = StoryActions.IsNarrativeAction(requestedAction) ? Guid.NewGuid().ToString("N") : string.Empty
        };
        _streamingTextBlock = AddMessageBubble(assistantMessage, streaming: true);
        string providerId = _settings.StoryProvider;
        string model = _settings.StoryModel;
        SetStatus($"Generating · {model}…");
        AppLogger.Info("STORY", $"Generation started | project={_activeProject.Id} | provider={providerId} | model={model} | role={_activeProject.Role}");

        try
        {
            string memoryContext = await BuildStoryMemoryContextAsync(effectiveInstruction, requestedAction, _generationCts.Token);
            StoryActionResult actionResult = await _actionOrchestrator.RunAsync(
                _activeProject,
                StoryDraftIsolation.ForGeneration(_messages),
                effectiveInstruction,
                requestedAction,
                providerId,
                model,
                memoryContext,
                delta => QueueStreamingDelta(streamGenerationId, delta),
                SetStatus,
                _generationCts.Token);
            string response = actionResult.Response;
            FlushStreamingText();
            assistantMessage.Content = response;
            if (_streamingTextBlock?.Parent is StackPanel finishedContent)
                AttachLongMessageReader(finishedContent, _streamingTextBlock, assistantMessage);
            if (requestedAction == StoryActionType.Chat && _streamingTextBlock?.Parent is StackPanel coCreationContent)
                AttachCoCreationActions(coCreationContent, assistantMessage);
            assistantMessage.ProposalKind = actionResult.ProposalKind;
            assistantMessage.ProposalJson = actionResult.ProposalJson;
            assistantMessage.IsNarrativeDraft = StoryActions.IsNarrativeAction(requestedAction) && actionResult.ContinuityVerified;
            if (assistantMessage.IsNarrativeDraft && string.IsNullOrWhiteSpace(assistantMessage.DraftGroupId))
                assistantMessage.DraftGroupId = Guid.NewGuid().ToString("N");
            _messages.Add(assistantMessage);
            if (actionResult.HasProposal && _streamingTextBlock?.Parent is StackPanel streamingContent)
                AttachProposalActions(streamingContent, assistantMessage);
            await _store.SaveMessagesAsync(_activeProject.Id, _messages);
            _activeProject.UpdatedUtc = DateTimeOffset.UtcNow;
            await _store.SaveProjectAsync(_activeProject);

            bool reviewBeforeCommit = assistantMessage.IsNarrativeDraft &&
                StoryManuscriptModes.Normalize(_activeProject.ManuscriptMode) == StoryManuscriptModes.ReviewBeforeCommit;
            if (StoryActions.MayAutoBookkeep(requestedAction) && actionResult.ShouldAutoBookkeep &&
                actionResult.ContinuityVerified && !reviewBeforeCommit)
            {
                await TryAutoBookkeepAsync(
                    text, response, providerId, model, requestedAction, _generationCts.Token,
                    committingMessage: assistantMessage);
            }
            else if (reviewBeforeCommit)
            {
                AppLogger.Info("STORY-MANUSCRIPT",
                    $"Narrative kept as review draft | project={_activeProject.Id} | action={requestedAction}");
            }
            else if (StoryActions.MayAutoBookkeep(requestedAction) && actionResult.ShouldAutoBookkeep)
            {
                AppLogger.Warn("STORY-MEMORY",
                    $"Auto-bookkeeping skipped because final continuity could not be verified | project={_activeProject.Id}");
            }

            if (_streamingTextBlock?.Parent is StackPanel finalStreamingContent && assistantMessage.IsNarrativeDraft)
                AttachManuscriptActions(finalStreamingContent, assistantMessage);
            await _store.SaveMessagesAsync(_activeProject.Id, _messages);
            if (actionResult.ResearchNoteCount > 0)
                await LoadStoryResearchAsync(_activeProject, _generationCts.Token);
            UpdateProjectSummaryUi();
            SetStatus(null);
            AppLogger.Info("STORY",
                $"Generation completed | project={_activeProject.Id} | chars={response.Length} | action={requestedAction} | pipeline={actionResult.UsedWritingPipeline} | researchNotes={actionResult.ResearchNoteCount} | researchSources={actionResult.ResearchSourceCount} | repaired={actionResult.RepairedAfterFinalCheck} | continuityVerified={actionResult.ContinuityVerified}");
        }
        catch (OperationCanceledException)
        {
            FlushStreamingText();
            bool? saved = await PreserveInterruptedReplyAsync(project, assistantMessage, stopped: true);
            SetStatus(saved == true ? "Stopped · partial reply saved as incomplete (not canon)"
                : saved is null ? "Error · partial reply could not be saved; see Logs" : null);
            AppLogger.Info("STORY", $"Generation cancelled | project={project.Id} | partialSaved={saved == true}");
        }
        catch (Exception ex)
        {
            FlushStreamingText();
            AppLogger.Error("STORY", "Generation failed", ex);
            bool? saved = await PreserveInterruptedReplyAsync(project, assistantMessage, stopped: false);
            string detail = StoryProviderFailure.Describe(ex);
            if (saved == false && _streamingTextBlock is not null && !_messages.Contains(assistantMessage))
                _streamingTextBlock.Text = detail;
            SetStatus(saved == true ? $"{detail} · Partial reply preserved (not canon)"
                : saved is null ? "Error · partial reply could not be saved; see Logs"
                : detail);
        }
        finally
        {
            // Late provider callbacks from a cancelled request must never
            // enter the next Story's streaming bubble.
            Interlocked.Increment(ref _streamGenerationId);
            _streamFlushTimer.Stop();
            lock (_streamSync) _pendingStream.Clear();
            _streamAccumulated.Clear();
            _generationCts.Dispose();
            _generationCts = null;
            _streamingTextBlock = null;
            SetGeneratingState(false);
            RefreshProviderStatus();
            // The last eligible prompt remains editable after generation.
            RenderConversation(scrollToEnd: true);
        }
    }

    private void StopButton_Click(object sender, RoutedEventArgs e)
    {
        if (_generationCts is null)
            return;
        StopButton.IsEnabled = false;
        SetStatus("Stopping…");
        _generationCts.Cancel();
    }

    private void QueueStreamingDelta(int generationId, string delta)
    {
        if (generationId != Volatile.Read(ref _streamGenerationId) || string.IsNullOrEmpty(delta))
            return;
        lock (_streamSync)
        {
            if (generationId != Volatile.Read(ref _streamGenerationId)) return;
            _pendingStream.Append(delta);
        }

        // Providers can yield dozens of tiny chunks per second. Queue at most one dispatcher
        // handoff to start the 45 ms flush timer instead of posting one WPF work item per token.
        if (Interlocked.Exchange(ref _streamDispatchQueued, 1) != 0)
            return;

        _ = Dispatcher.BeginInvoke(new Action(() =>
        {
            Interlocked.Exchange(ref _streamDispatchQueued, 0);
            if (!_streamFlushTimer.IsEnabled)
                _streamFlushTimer.Start();
        }), DispatcherPriority.Background);
    }

    private void FlushStreamingText()
    {
        string delta;
        lock (_streamSync)
        {
            if (_pendingStream.Length == 0)
            {
                if (_generationCts is null)
                    _streamFlushTimer.Stop();
                return;
            }
            delta = _pendingStream.ToString();
            _pendingStream.Clear();
        }
        if (_streamingTextBlock is not null)
        {
            bool follow = IsChatNearBottom();
            _streamAccumulated.Append(delta);
            _streamingTextBlock.Text = StoryLongMessageWindow.LiveTail(_streamAccumulated);
            if (follow)
                ChatScroll.ScrollToEnd();
        }
    }

    private void SetGeneratingState(bool generating)
    {
        bool hasProject = _activeProject is not null;
        bool canEdit = !generating && _generationCts is null && hasProject && !_loadingProject &&
            !_checkpointRecoveryFailed && !_creatingStory && !_storageAuditBusy;
        SendButton.IsEnabled = canEdit && CanSubmitCurrentStoryAction();
        InputBox.IsEnabled = canEdit;
        // Keep switching enabled during disk loads so the last clicked Story wins.
        ProjectBox.IsEnabled = !generating && _generationCts is null && !_creativeDecisionBusy && !_storageAuditBusy;
        NewProjectButton.IsEnabled = !generating && !_loadingProject && !_storageAuditBusy;
        EmptyStateNewStoryButton.IsEnabled = !generating && !_loadingProject && !_storageAuditBusy;
        StoryNavPanel.IsEnabled = hasProject && !_creatingStory && !_loadingProject && !_checkpointRecoveryFailed;
        GenresButton.IsEnabled = canEdit;
        ProviderButton.IsEnabled = !generating && !_loadingProject && !_storageAuditBusy;
        InspectorPanel.IsEnabled = hasProject && !_creatingStory && !_loadingProject;
        InspectorMemoryView.IsEnabled = canEdit;
        InspectorSettingsView.IsEnabled = canEdit;
        RoleBox.IsEnabled = canEdit;
        MemoryButton.IsEnabled = canEdit;
        StopButton.Visibility = generating ? Visibility.Visible : Visibility.Collapsed;
        StopButton.IsEnabled = generating;
        ConversationSearchToolbar.IsEnabled = !generating && !_loadingProject &&
            !_checkpointRecoveryFailed && !_storageAuditBusy;
        UpdateRetryLastUi();
        UpdatePromptEditUi();
        UpdateConversationSearchUi();
    }

    private void RenderConversation(bool scrollToEnd = true)
    {
        int renderRevision = ++_conversationRenderRevision;
        string? renderStoryId = _activeProject?.Id;
        MessagesPanel.Children.Clear();
        _renderingConversation = true;
        try
        {
            int start;
            int end;
            if (_conversationFocusIndex is int focus && _generationCts is null && _messages.Count > 0)
            {
                (start, end) = StoryConversationSearch.Around(_messages.Count, focus);
                if (start > 0)
                    AddConversationWindowNavigation(Math.Max(0, start - 1),
                        $"↑  Earlier context · {start} messages before");
            }
            else
            {
                (start, end) = StoryConversationPaging.Window(
                    _messages.Count, _visibleConversationMessages, _conversationPageEnd);
                if (start > 0)
                    AddEarlierMessagesButton(start);
            }
            if (start < end)
                AddSessionSeparator(_messages[start].CreatedUtc);
            Grid? focusedRow = null;
            for (int i = start; i < end; i++)
            {
                int matchPosition = _conversationFocusIndex == i
                    ? StoryConversationSearch.FindMatchPosition(_messages[i].Content, ConversationSearchBox.Text)
                    : -1;
                AddMessageBubble(_messages[i], preferredTextPosition: matchPosition);
                if (_conversationFocusIndex == i)
                {
                    focusedRow = MessagesPanel.Children.OfType<Grid>()
                        .LastOrDefault(row => Equals(row.Tag, "StoryMessageRow"));
                    // Accent only the selected search result, never the stored text.
                    Border? bubble = focusedRow?.Children.OfType<Border>()
                        .FirstOrDefault(b => b.Tag is string tag && tag.StartsWith(
                            "StoryMessageBubble:", StringComparison.Ordinal));
                    if (bubble is not null)
                    {
                        bubble.BorderBrush = (SolidColorBrush)FindResource("StoryGold");
                        bubble.BorderThickness = new Thickness(2);
                    }
                }
            }
            if (_conversationFocusIndex is not null && end < _messages.Count)
                AddConversationWindowNavigation(end, $"↓  Later context · {_messages.Count - end} messages after");
            else if (_conversationFocusIndex is null && end < _messages.Count)
                AddLaterMessagesButton(_messages.Count - end);
            if (focusedRow is not null)
            {
                Grid row = focusedRow;
                Dispatcher.BeginInvoke(new Action(() =>
                {
                    if (_conversationRenderRevision == renderRevision &&
                        _activeProject?.Id == renderStoryId)
                        row.BringIntoView();
                }), DispatcherPriority.Loaded);
            }
        }
        finally
        {
            _renderingConversation = false;
        }
        UpdateStorySurfaceState();
        UpdateProjectSummaryUi();
        if (scrollToEnd && _conversationFocusIndex is null && _conversationPageEnd == 0)
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_conversationRenderRevision == renderRevision &&
                    _activeProject?.Id == renderStoryId)
                    ChatScroll.ScrollToEnd();
            }), DispatcherPriority.Background);
    }

    private TextBlock AddMessageBubble(StoryMessage message, bool streaming = false,
        int preferredTextPosition = -1)
    {
        bool user = string.Equals(message.Role, "user", StringComparison.OrdinalIgnoreCase);
        var text = new TextBlock
        {
            // Avoid assigning a giant manuscript to the WPF visual tree before
            // AttachLongMessageReader swaps in its bounded visible excerpt.
            Text = !streaming && (message.Content?.Length ?? 0) > StoryLongMessageWindow.ChunkCharacters
                ? string.Empty : message.Content,
            TextWrapping = TextWrapping.Wrap,
            Foreground = (SolidColorBrush)FindResource("StoryText"),
            FontFamily = user ? ChatFont : ReadingFont,
            FontSize = user ? 13.5 : 14,
            LineHeight = user ? 22 : 24,
            MaxWidth = user ? 500 : 620
        };

        var content = new StackPanel();
        if (!user)
        {
            StoryActionType badgeAction = StoryActions.TryParse(message.Action, out StoryActionType parsedAction)
                ? parsedAction
                : StoryActionType.Chat;
            string badgeLabel = message.IsSupersededResponse ? "STORY COMPANION · EARLIER VERSION (NOT USED)"
                : message.IsInterruptedResponse ? "STORY COMPANION · INCOMPLETE"
                : badgeAction == StoryActionType.Chat ? "STORY COMPANION"
                : $"STORY COMPANION · {StoryActions.Label(badgeAction).ToUpperInvariant()}";
            var badgeText = new TextBlock
            {
                Text = badgeLabel,
                Foreground = (SolidColorBrush)FindResource("StoryGold"),
                FontSize = 9.5,
                FontWeight = FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            var badge = new Border
            {
                Background = (SolidColorBrush)FindResource("StoryGoldDim"),
                BorderBrush = (SolidColorBrush)FindResource("StoryGoldBorder"),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 3, 8, 3),
                Margin = new Thickness(0, 0, 0, 11),
                HorizontalAlignment = WpfHorizontalAlignment.Left,
                Child = badgeText
            };
            content.Children.Add(badge);
        }

        content.Children.Add(text);
        if (!streaming)
            AttachLongMessageReader(content, text, message, preferredTextPosition);
        AttachCoCreationActions(content, message);
        if (!user)
        {
            AttachProposalActions(content, message);
            AttachManuscriptActions(content, message);
        }
        content.Children.Add(CreateMessageMetaRow(message, text, message.CreatedUtc, user));

        var bubble = new Border
        {
            Background = (SolidColorBrush)FindResource(user ? "StoryUserMessage" : "StoryAiMessage"),
            BorderBrush = (SolidColorBrush)FindResource("StoryBorder"),
            BorderThickness = new Thickness(1),
            CornerRadius = user ? new CornerRadius(16, 4, 16, 16) : new CornerRadius(4, 16, 16, 16),
            Padding = new Thickness(17, 13, 17, 13),
            MaxWidth = user ? 540 : 660,
            Child = content,
            HorizontalAlignment = user ? WpfHorizontalAlignment.Right : WpfHorizontalAlignment.Left,
            Tag = user ? "StoryMessageBubble:user" : "StoryMessageBubble:assistant"
        };

        var row = new Grid
        {
            MaxWidth = 900,
            HorizontalAlignment = WpfHorizontalAlignment.Stretch,
            Margin = new Thickness(0, 0, 0, 20),
            Tag = "StoryMessageRow"
        };

        if (user)
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            bubble.Margin = new Thickness(120, 0, 0, 0);
            Grid.SetColumn(bubble, 1);
            row.Children.Add(bubble);
        }
        else
        {
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var avatar = new Border
            {
                Width = 30,
                Height = 30,
                CornerRadius = new CornerRadius(8),
                // The closed-book pixel icon already includes its own tile.
                // Avoid placing a second brass square underneath it.
                Background = System.Windows.Media.Brushes.Transparent,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 2, 12, 0),
                VerticalAlignment = VerticalAlignment.Top,
                Child = new System.Windows.Controls.Image
                {
                    Source = (ImageSource)FindResource("StoryIconImage"),
                    Width = 28,
                    Height = 28,
                    Stretch = Stretch.Uniform,
                    SnapsToDevicePixels = true
                }
            };
            RenderOptions.SetBitmapScalingMode((System.Windows.Controls.Image)avatar.Child, BitmapScalingMode.NearestNeighbor);
            bubble.Margin = new Thickness(0, 0, 100, 0);
            Grid.SetColumn(avatar, 0);
            Grid.SetColumn(bubble, 1);
            row.Children.Add(avatar);
            row.Children.Add(bubble);
        }

        bool followTail = IsChatNearBottom();
        MessagesPanel.Children.Add(row);
        UpdateMessageRowLayout(row);
        // Live chat can continue for hours without reopening a story. Compact the
        // rendered window occasionally so visual elements do not grow forever.
        // Never rebuild mid-stream: that would detach the active streaming TextBlock.
        if (!_renderingConversation && !streaming && _generationCts is null &&
            _messages.Count > _visibleConversationMessages + 12 &&
            MessagesPanel.Children.OfType<Grid>().Count(item => Equals(item.Tag, "StoryMessageRow")) >
                _visibleConversationMessages + 12)
        {
            if (followTail)
            {
                RenderConversation(scrollToEnd: true);
                return text;
            }
        }
        if (!_renderingConversation && followTail)
            ChatScroll.ScrollToEnd();
        return text;
    }

    private void UpdateGenreSummary()
    {
        GenreChipsPanel.Children.Clear();
        GenreChipsPanel.Visibility = Visibility.Collapsed;
        if (_activeProject is null)
        {
            GenresButton.ToolTip = "Optional genres: choose here or declare them in Chat";
            UpdateProjectSummaryUi();
            return;
        }

        string core = _activeProject.CoreGenres.Count == 0 ? "None" : string.Join(" · ", _activeProject.CoreGenres);
        string temporary = _activeProject.TemporaryTags.Count == 0
            ? string.Empty
            : $"\nCurrent: {string.Join(" · ", _activeProject.TemporaryTags)}";
        GenresButton.ToolTip = $"Core: {core}{temporary}";
        UpdateProjectSummaryUi();
    }

    private void ChatScroll_ScrollChanged(object sender, ScrollChangedEventArgs e) => UpdateLatestJumpUi();

    private void JumpToLatestButton_Click(object sender, RoutedEventArgs e)
    {
        if (_generationCts is not null)
        {
            // Rebuilding bubbles during streaming would detach its TextBlock.
            ChatScroll.ScrollToEnd();
            return;
        }
        // Loading hundreds of earlier messages is a reading choice, not a
        // permanent cost of opening the Story or returning to the present.
        _visibleConversationMessages = StoryConversationPaging.PageSize;
        _conversationPageEnd = 0;
        _conversationFocusIndex = null;
        RenderConversation();
    }

    private void UpdateLatestJumpUi()
    {
        if (JumpToLatestButton is null || ChatScroll is null) return;
        JumpToLatestButton.Visibility = _messages.Count > 0 && ChatScroll.Visibility == Visibility.Visible &&
            !_loadingProject && (_conversationFocusIndex is not null ||
                (_conversationPageEnd > 0 && _conversationPageEnd < _messages.Count) || !IsChatNearBottom())
                ? Visibility.Visible : Visibility.Collapsed;
    }

    private bool IsChatNearBottom()
    {
        if (ChatScroll.ExtentHeight <= 0)
            return true;
        double distance = ChatScroll.ExtentHeight - ChatScroll.VerticalOffset - ChatScroll.ViewportHeight;
        return distance < 120;
    }


    private void UpdateEmptyState()
    {
        _pendingAlternateGroupId = string.Empty;
        _activeProject = null;
        _memory = new StoryMemoryDocument();
        ResetStoryResearch();
        _messages.Clear();
        MessagesPanel.Children.Clear();
        ResetIdeaOrganizerUi();
        GenreChipsPanel.Children.Clear();
        GenreChipsPanel.Visibility = Visibility.Collapsed;
        RoleBox.Visibility = Visibility.Visible;
        MemoryButton.Visibility = Visibility.Collapsed;
        GenresButton.Visibility = Visibility.Collapsed;
        ProviderButton.Visibility = Visibility.Collapsed;
        RoleBox.SelectedIndex = 0;
        SetSelectedStoryAction(StoryActionType.Chat);
        UpdateMemoryButton();
        UpdateProjectSelectorUi();
        UpdateProjectSummaryUi();
        UpdateStorySurfaceState();
        SetStatus(null);
        SetGeneratingState(false);
    }

    private void UpdateProjectSelectorUi()
    {
        bool hasActive = _activeProject is not null;
        NewProjectButton.Visibility = Visibility.Visible;
        StoryNavPanel.Visibility = hasActive ? Visibility.Visible : Visibility.Collapsed;
        EmptyStateNewStoryButton.Visibility = _projects.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        RebuildProjectList();
    }

    private void SetStatus(string? text)
    {
        if (StatusText is null)
            return;
        StatusText.Text = text ?? string.Empty;
        StatusText.Visibility = string.IsNullOrWhiteSpace(text) ? Visibility.Collapsed : Visibility.Visible;
    }
}

