using System.Windows;
using System.Windows.Controls;
using WpfButton = System.Windows.Controls.Button;
using System.Windows.Media;

namespace WorkChatMini;

public partial class StoryPanel
{
    private StoryActionType _selectedAction = StoryActionType.Chat;

    private void SuggestionCard_Click(object sender, RoutedEventArgs e)
    {
        if (_activeProject is null || sender is not WpfButton button || button.Tag is not string tag ||
            !StoryActions.TryParse(tag, out StoryActionType action) || action == StoryActionType.Chat)
        {
            return;
        }

        SetSelectedStoryAction(_selectedAction == action ? StoryActionType.Chat : action);
        FocusComposer();
    }

    private void ClearStoryActionButton_Click(object sender, RoutedEventArgs e)
    {
        SetSelectedStoryAction(StoryActionType.Chat);
        FocusComposer();
    }

    private void SetSelectedStoryAction(StoryActionType action)
    {
        // While editing an existing turn, its action is part of the immutable
        // provenance. Changing a chip would silently run a different workflow.
        if (_editingUserPrompt is not null &&
            StoryActions.TryParse(_editingUserPrompt.Action, out StoryActionType savedAction) &&
            action != savedAction)
        {
            SetStatus("Finish or cancel the prompt edit before switching Story Actions.");
            return;
        }
        _selectedAction = action;
        bool explicitAction = action != StoryActionType.Chat;
        ActiveActionBar.Visibility = explicitAction ? Visibility.Visible : Visibility.Collapsed;
        ActiveActionText.Text = explicitAction
            ? action == StoryActionType.ContinueStory && _activeProject is not null
                ? $"Continue Story · {StoryAutonomyModes.Normalize(_activeProject.AutonomyMode)}"
                : StoryActions.Label(action)
            : string.Empty;
        SendButton.Content = _editingUserPrompt is not null ? "Regenerate  ➤" : explicitAction ? "Run  ➤" : "Send  ➤";
        if (_generationCts is null)
            SendButton.IsEnabled = CanSubmitCurrentStoryAction();
        UpdateComposerHint();
    }

    private bool CanSubmitCurrentStoryAction() =>
        _activeProject is not null &&
        (_selectedAction == StoryActionType.ContinueStory || !string.IsNullOrWhiteSpace(InputBox.Text));

    private void UpdateComposerHint()
    {
        if (_activeProject is null)
            InputHintText.Text = "Start a story to begin writing…";
        else
            InputHintText.Text = StoryActions.ComposerHint(_selectedAction);
        InputHintText.Visibility = string.IsNullOrEmpty(InputBox.Text) ? Visibility.Visible : Visibility.Collapsed;
    }

    private void AttachProposalActions(StackPanel content, StoryMessage message)
    {
        if (message.IsSupersededResponse || string.IsNullOrWhiteSpace(message.ProposalKind))
            return;

        if (message.ProposalApplied)
        {
            content.Children.Add(new TextBlock
            {
                Text = string.Equals(message.ProposalKind, "Lore", StringComparison.OrdinalIgnoreCase)
                    ? "Saved to Story Memory as Proposed lore."
                    : string.Equals(message.ProposalKind, "CanonRevision", StringComparison.OrdinalIgnoreCase)
                        ? "Canon revision applied."
                        : "Added to Story Memory.",
                Foreground = (SolidColorBrush)FindResource("StoryMuted"),
                FontSize = 10.5,
                Margin = new Thickness(0, 10, 0, 0)
            });
            return;
        }

        var row = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Margin = new Thickness(0, 12, 0, 0)
        };
        row.Children.Add(new WpfButton
        {
            Content = string.Equals(message.ProposalKind, "Lore", StringComparison.OrdinalIgnoreCase)
                ? "Save as Proposed"
                : string.Equals(message.ProposalKind, "CanonRevision", StringComparison.OrdinalIgnoreCase)
                    ? "Apply Revision"
                    : "Add to Story",
            Tag = message,
            Style = (Style)FindResource("StoryAccentButton"),
            MinWidth = 112,
            Height = 32,
            Padding = new Thickness(10, 0, 10, 0)
        });
        if (row.Children[0] is WpfButton applyButton)
            applyButton.Click += ApplyProposalButton_Click;
        content.Children.Add(row);
    }

    private async void ApplyProposalButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeProject is null || _generationCts is not null || _storageAuditBusy ||
            _creativeDecisionBusy || _loadingProject || _checkpointRecoveryFailed ||
            sender is not WpfButton button || button.Tag is not StoryMessage message || message.ProposalApplied)
        {
            return;
        }

        StoryMemoryUpdate? update = StoryProposalSerializer.DeserializeUpdate(message.ProposalJson);
        if (update is null)
        {
            AppLogger.Warn("STORY-ACTION", $"Proposal could not be applied | project={_activeProject.Id} | kind={message.ProposalKind}");
            return;
        }

        button.IsEnabled = false;
        _creativeDecisionBusy = true;
        ProjectBox.IsEnabled = false;
        string projectId = _activeProject.Id;
        UpdateStorySurfaceState();
        try
        {
            // A previous attempt may have saved Memory but crashed before its
            // conversation flag. Reconcile from the durable memory receipt.
            if (StoryProposalCommit.AlreadyApplied(_memory, message))
            {
                message.ProposalApplied = true;
                await _store.SaveMessagesAsync(projectId, _messages);
                RenderConversation();
                SetStatus("Proposal already applied · confirmation recovered from Story Memory.");
                return;
            }
            string source = string.Equals(message.ProposalKind, "Lore", StringComparison.OrdinalIgnoreCase)
                ? "Creator-saved lore proposal"
                : string.Equals(message.ProposalKind, "CanonRevision", StringComparison.OrdinalIgnoreCase)
                    ? "Creator-approved canon revision"
                    : "Creator-approved character proposal";
            // Stage proposal on a detached document. If merging or the first
            // memory write fails, the live in-memory canon remains unchanged.
            StoryMemoryDocument staged = StoryMemoryStaging.Clone(_memory);
            int revisionsBefore = staged.Revisions.Count;
            StoryMemoryMerger.Apply(staged, update, source);
            if (string.Equals(message.ProposalKind, "CanonRevision", StringComparison.OrdinalIgnoreCase) &&
                staged.Revisions.Count == revisionsBefore)
            {
                button.IsEnabled = true;
                SetStatus("Error · Revision could not match current canon. Run Revise Canon again against the latest memory.");
                AppLogger.Warn("STORY-ACTION", $"Canon revision proposal matched no current canon | project={_activeProject.Id}");
                return;
            }
            StoryProposalCommit.MarkApplied(staged, message);
            await _memoryStore.SaveAsync(projectId, staged);
            // From this point the memory update is durable. If saving the
            // conversation flag fails, keep the live state applied and warn;
            // never allow a second click to mutate the same memory again.
            _memory = staged;
            message.ProposalApplied = true;
            await _store.SaveMessagesAsync(projectId, _messages);
            UpdateMemoryButton();
            UpdateProjectSummaryUi();
            if (InspectorPanel.Visibility == Visibility.Visible)
                RenderMemoryInspector(_activeInspectorSection ?? "Overview");
            RenderConversation();
            AppLogger.Info("STORY-ACTION", $"Proposal applied | project={_activeProject.Id} | kind={message.ProposalKind}");
        }
        catch (Exception ex)
        {
            button.IsEnabled = !message.ProposalApplied;
            SetStatus(message.ProposalApplied
                ? "Error · Memory saved but confirmation flag could not be saved. Reopen after checking Logs."
                : "Error · Proposal was not applied. Original Memory preserved; see Logs.");
            AppLogger.Error("STORY-ACTION", "Proposal apply failed", ex);
        }
        finally
        {
            _creativeDecisionBusy = false;
            ProjectBox.IsEnabled = !_loadingProject && _generationCts is null;
            UpdateStorySurfaceState();
        }
    }
}
