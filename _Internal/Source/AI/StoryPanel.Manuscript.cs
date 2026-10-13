using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfButton = System.Windows.Controls.Button;

namespace WorkChatMini;

public partial class StoryPanel
{
    private void AttachManuscriptActions(StackPanel content, StoryMessage message)
    {
        if (message.IsSupersededResponse)
            return;
        if (!message.IsNarrativeDraft)
        {
            AttachChatChapterPromotion(content, message);
            return;
        }

        if (message.ManuscriptCommitted)
        {
            string chapterLabel = message.ManuscriptChapterNumber is int number
                ? $"Committed · Chapter {number}" + (string.IsNullOrWhiteSpace(message.ManuscriptChapterTitle) ? string.Empty : $" · {message.ManuscriptChapterTitle}")
                : "Committed to Story";
            content.Children.Add(new TextBlock
            {
                Text = chapterLabel,
                Foreground = (SolidColorBrush)FindResource("StoryMuted"),
                FontSize = 10.5,
                Margin = new Thickness(0, 10, 0, 0)
            });
            AttachRollbackAction(content, message);
            return;
        }

        if (message.DraftDiscarded)
        {
            content.Children.Add(new TextBlock { Text = "Alternate not selected · archived draft", Foreground = (SolidColorBrush)FindResource("StoryMuted"), FontSize = 10.5 });
            return;
        }

        if (_activeProject is null ||
            StoryManuscriptModes.Normalize(_activeProject.ManuscriptMode) != StoryManuscriptModes.ReviewBeforeCommit)
        {
            return;
        }

        var row = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Margin = new Thickness(0, 12, 0, 0)
        };
        var commitButton = new WpfButton
        {
            Content = "Commit to Story",
            Tag = message,
            Style = (Style)FindResource("StoryAccentButton"),
            MinWidth = 122,
            Height = 32,
            Padding = new Thickness(10, 0, 10, 0)
        };
        commitButton.Click += CommitManuscriptButton_Click;
        if (StoryDraftIsolation.HasCommittedSibling(_messages, message))
            commitButton.IsEnabled = false;
        row.Children.Add(commitButton);
        var alternateButton = new WpfButton
        {
            Content = "Try alternate",
            Tag = message,
            Style = (Style)FindResource("StoryAccentButton"),
            Height = 32,
            Margin = new Thickness(8, 0, 0, 0),
            Padding = new Thickness(10, 0, 10, 0)
        };
        alternateButton.Click += TryAlternateButton_Click;
        row.Children.Add(alternateButton);
        row.Children.Add(new TextBlock
        {
            Text = "Draft · memory unchanged until committed",
            Foreground = (SolidColorBrush)FindResource("StoryMuted"),
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(9, 0, 0, 0)
        });
        content.Children.Add(row);
    }

    private async void TryAlternateButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeProject is null || _generationCts is not null || _checkpointRecoveryFailed || _storageAuditBusy ||
            sender is not WpfButton button || button.Tag is not StoryMessage message ||
            !message.IsNarrativeDraft || message.ManuscriptCommitted || message.DraftDiscarded ||
            StoryDraftIsolation.HasCommittedSibling(_messages, message) ||
            !StoryActions.TryParse(message.Action, out StoryActionType action) ||
            !StoryActions.IsNarrativeAction(action))
            return;

        _pendingAlternateGroupId = string.IsNullOrWhiteSpace(message.DraftGroupId)
            ? message.DraftVersionId.Length > 0 ? message.DraftVersionId : Guid.NewGuid().ToString("N")
            : message.DraftGroupId;
        // Legacy drafts had no group id. Persist grouping before a new variant is generated.
        message.DraftGroupId = _pendingAlternateGroupId;
        InputBox.Text = message.SourceInstruction;
        SetSelectedStoryAction(action);
        SetStatus("Alternate direction ready · edit the prompt, then Run. Original draft remains unchanged.");
        await _store.SaveMessagesAsync(_activeProject.Id, _messages);
        FocusComposer();
    }

    private static void MarkManuscriptCommitted(StoryMessage message, StoryChapterMemory chapter)
    {
        message.ManuscriptCommitted = true;
        message.ManuscriptChapterNumber = chapter.Number;
        message.ManuscriptChapterTitle = chapter.Title;
        message.ManuscriptCommittedUtc = DateTimeOffset.UtcNow;
    }

    private async void CommitManuscriptButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeProject is null || _settings is null || _generationCts is not null || _checkpointRecoveryFailed || _storageAuditBusy ||
            sender is not WpfButton button || button.Tag is not StoryMessage message ||
            message.ManuscriptCommitted || message.DraftDiscarded || !message.IsNarrativeDraft ||
            StoryDraftIsolation.HasCommittedSibling(_messages, message))
        {
            return;
        }

        if (!StoryActions.TryParse(message.Action, out StoryActionType action) || !StoryActions.IsNarrativeAction(action))
            return;

        IStoryAiProvider provider = StoryProviderFactory.Get(_settings.StoryProvider);
        if (!StoryCredentialStore.HasKey(provider.Id) && _activeProject.AutoBookkeeping)
        {
            OpenInspector("Settings");
            SetStatus("Error · Connect an AI provider to bookkeep this chapter.");
            return;
        }

        button.IsEnabled = false;
        _generationCts = new CancellationTokenSource();
        SetGeneratingState(true);
        try
        {
            SetStatus("Committing manuscript…");
            string instruction = string.IsNullOrWhiteSpace(message.SourceInstruction)
                ? "Commit this generated narrative to the story."
                : message.SourceInstruction;
            StoryChapterMemory? chapter = await TryAutoBookkeepAsync(
                instruction,
                message.Content,
                _settings.StoryProvider,
                _settings.StoryModel,
                action,
                _generationCts.Token,
                forceNarrativeCommit: true,
                committingMessage: message);
            if (chapter is null)
            {
                SetStatus("Error · Could not commit this draft. Check Logs.");
                button.IsEnabled = true;
                return;
            }

            RenderConversation();
            if (InspectorPanel.Visibility == Visibility.Visible)
                RenderMemoryInspector(_activeInspectorSection ?? "Overview");
            SetStatus(_bookkeepingExtractionIncomplete
                ? "Chapter committed, but automatic Memory extraction was incomplete. See Logs."
                : !_activeProject.AutoBookkeeping
                    ? "Chapter committed. Auto-bookkeeping is off; no structured characters/events were extracted."
                    : null);
            AppLogger.Info("STORY-MANUSCRIPT", $"Draft committed | project={_activeProject.Id} | chapter={chapter.Number}");
        }
        catch (OperationCanceledException)
        {
            SetStatus(null);
            button.IsEnabled = true;
        }
        catch (Exception ex)
        {
            button.IsEnabled = true;
            SetStatus("Error · manuscript commit failed");
            AppLogger.Error("STORY-MANUSCRIPT", "Draft commit failed", ex);
        }
        finally
        {
            _generationCts?.Dispose();
            _generationCts = null;
            SetGeneratingState(false);
        }
    }
}
