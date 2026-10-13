using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfButton = System.Windows.Controls.Button;

namespace WorkChatMini;

public partial class StoryPanel
{
    // Chat must remain discussion-only by default. When the author has asked Chat
    // to write a real chapter, offer an explicit and reversible chapter promotion.
    private bool CanPromoteChatReply(StoryMessage message) =>
        _activeProject is not null && _generationCts is null && !_loadingProject &&
        !_storageAuditBusy && !_creativeDecisionBusy && !_checkpointRecoveryFailed &&
        ReferenceEquals(_messages.LastOrDefault(), message) &&
        string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(message.Action, StoryActionType.Chat.ToString(), StringComparison.OrdinalIgnoreCase) &&
        (message.Content?.Length ?? 0) >= 500 &&
        !message.IsInterruptedResponse && !message.IsSupersededResponse &&
        !message.ManuscriptCommitted && !message.ProposalApplied &&
        string.IsNullOrWhiteSpace(message.ProposalKind) &&
        string.IsNullOrWhiteSpace(message.CreativeDirection);

    private void AttachChatChapterPromotion(StackPanel content, StoryMessage message)
    {
        if (!CanPromoteChatReply(message)) return;
        var button = new WpfButton
        {
            Content = "Save reply as Chapter…",
            Tag = message,
            Padding = new Thickness(10, 5, 10, 5),
            Margin = new Thickness(0, 10, 0, 0),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Style = (Style)FindResource("StoryButton"),
            ToolTip = "Only for an actual chapter written in Chat. Will store the full chapter and extract confirmed characters, locations and events when Auto-bookkeeping is on."
        };
        bool confirm = false;
        button.Click += async (_, _) =>
        {
            if (!CanPromoteChatReply(message)) return;
            if (!confirm)
            {
                confirm = true;
                button.Content = "Confirm: commit this Chapter";
                button.ToolTip = "This will turn the chat reply into a committed chapter. If it is just brainstorming, do not confirm.";
                return;
            }
            await PromoteChatReplyAsync(message, button);
        };
        content.Children.Add(button);
    }

    private async Task PromoteChatReplyAsync(StoryMessage message, WpfButton button)
    {
        if (!CanPromoteChatReply(message) || _activeProject is null || _settings is null)
            return;
        IStoryAiProvider provider = StoryProviderFactory.Get(_settings.StoryProvider);
        if (_activeProject.AutoBookkeeping && !StoryCredentialStore.HasKey(provider.Id))
        {
            OpenInspector("Settings");
            SetStatus("Cannot extract chapter memory · connect a provider first.");
            return;
        }
        StoryProject project = _activeProject;
        // A user's previous chat prompt, if available, is used as the source
        // instruction. The chapter itself is the only material that can establish canon.
        int index = _messages.IndexOf(message);
        string instruction = index > 0 && _messages[index - 1].Role == "user"
            ? _messages[index - 1].Content : "Save this already-written chapter.";
        int charactersBefore = _memory.Characters.Count;
        string originalAction = message.Action;
        bool originalDraft = message.IsNarrativeDraft;
        string originalSource = message.SourceInstruction;
        string originalGroup = message.DraftGroupId;
        string originalVersion = message.DraftVersionId;
        _generationCts = new CancellationTokenSource();
        button.IsEnabled = false;
        SetGeneratingState(true);
        try
        {
            message.Action = StoryActionType.ContinueStory.ToString();
            message.IsNarrativeDraft = true;
            message.SourceInstruction = instruction;
            message.DraftGroupId = Guid.NewGuid().ToString("N");
            message.DraftVersionId = Guid.NewGuid().ToString("N");
            SetStatus("Saving chapter and extracting Story Memory…");
            StoryChapterMemory? chapter = await TryAutoBookkeepAsync(
                instruction, message.Content, provider.Id, _settings.StoryModel,
                StoryActionType.ContinueStory, _generationCts.Token,
                forceNarrativeCommit: true, committingMessage: message);
            if (chapter is null)
            {
                // The persistence layer restores the original on-disk state if its
                // chapter transaction failed; do not present this reply as committed.
                if (_messages.Contains(message))
                {
                    message.Action = originalAction;
                    message.IsNarrativeDraft = originalDraft;
                    message.SourceInstruction = originalSource;
                    message.DraftGroupId = originalGroup;
                    message.DraftVersionId = originalVersion;
                }
                SetStatus("Chapter was not committed. Your chat reply remains saved; see Logs.");
                return;
            }
            await _store.SaveMessagesAsync(project.Id, _messages);
            if (InspectorPanel.Visibility == Visibility.Visible)
                RenderMemoryInspector(_activeInspectorSection ?? "Overview");
            SetStatus(_bookkeepingExtractionIncomplete
                ? $"Chapter {chapter.Number} saved; structured Memory extraction was incomplete. See Logs."
                : !project.AutoBookkeeping
                    ? $"Chapter {chapter.Number} saved. Auto-bookkeeping is off; structured Memory unchanged."
                    : project.AutoBookkeeping && _memory.Characters.Count == charactersBefore
                    ? $"Chapter {chapter.Number} saved. No new characters extracted; inspect Memory/Logs."
                    : $"Chapter {chapter.Number} saved and Story Memory updated.");
            AppLogger.Info("STORY-MEMORY", $"Creator promoted Chat reply to committed chapter | project={project.Id} | chapter={chapter.Number}");
        }
        catch (Exception ex)
        {
            AppLogger.Error("STORY-MEMORY", "Chat reply chapter promotion failed", ex);
            SetStatus("Chapter save failed. Original Story preserved; see Logs.");
        }
        finally
        {
            _generationCts?.Dispose();
            _generationCts = null;
            SetGeneratingState(false);
            RenderConversation();
        }
    }
}
