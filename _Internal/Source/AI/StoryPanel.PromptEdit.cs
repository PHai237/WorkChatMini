using System.Windows;

namespace WorkChatMini;

public partial class StoryPanel
{
    private StoryMessage? _editingUserPrompt;

    /// <summary>Only the most recent turn can be edited, and only if nothing
    /// from its reply has entered canon or been explicitly accepted by the author.
    /// Older turns must use an explicit checkpoint/branch restore instead.</summary>
    private StoryMessage? GetEditablePrompt()
    {
        if (_activeProject is null || _loadingProject || _creatingStory ||
            _checkpointRecoveryFailed || _storageAuditBusy || _creativeDecisionBusy ||
            _generationCts is not null)
            return null;

        int userIndex = _messages.FindLastIndex(m => string.Equals(m.Role, "user", StringComparison.OrdinalIgnoreCase));
        if (userIndex < 0) return null;
        StoryMessage prompt = _messages[userIndex];
        if (!StoryActions.TryParse(prompt.Action, out _) ||
            !string.IsNullOrWhiteSpace(prompt.CreativeDirection))
            return null;

        int activeReplies = 0;
        for (int i = userIndex + 1; i < _messages.Count; i++)
        {
            StoryMessage reply = _messages[i];
            if (!string.Equals(reply.Role, "assistant", StringComparison.OrdinalIgnoreCase) ||
                reply.ManuscriptCommitted || reply.ProposalApplied ||
                !string.IsNullOrWhiteSpace(reply.CreativeDirection) ||
                reply.ManuscriptChapterNumber.HasValue)
                return null;
            if (!reply.IsSupersededResponse) activeReplies++;
            if (activeReplies > 1) return null;
        }
        return prompt;
    }

    private StoryMessage? GetCurrentReplyForPrompt(StoryMessage prompt)
    {
        int index = _messages.IndexOf(prompt);
        if (index < 0) return null;
        return _messages.Skip(index + 1).LastOrDefault(m =>
            string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase) && !m.IsSupersededResponse);
    }

    private void UpdatePromptEditUi()
    {
        if (EditLastPromptButton is null || CancelPromptEditButton is null) return;
        bool editing = _editingUserPrompt is not null;
        bool available = GetEditablePrompt() is not null;
        // On narrow layouts the same Edit action is available below the last
        // user message. Keep the composer footer from covering Send/Retry.
        EditLastPromptButton.Visibility = !editing && available && ActualWidth >= 940
            ? Visibility.Visible : Visibility.Collapsed;
        EditLastPromptButton.IsEnabled = available;
        CancelPromptEditButton.Visibility = editing ? Visibility.Visible : Visibility.Collapsed;
        CancelPromptEditButton.IsEnabled = editing && _generationCts is null;
        if (editing) SendButton.Content = "Regenerate  ➤";
    }

    private void BeginPromptEdit(StoryMessage prompt)
    {
        if (!ReferenceEquals(GetEditablePrompt(), prompt)) return;
        if (!string.IsNullOrWhiteSpace(InputBox.Text))
        {
            SetStatus("Edit · Your composer has unsent text. Clear it first so nothing is lost.");
            return;
        }
        _editingUserPrompt = prompt;
        InputBox.Text = prompt.Content;
        if (StoryActions.TryParse(prompt.Action, out StoryActionType action))
            SetSelectedStoryAction(action);
        UpdatePromptEditUi();
        SetStatus("Editing last prompt · Regenerate creates a new reply; the old answer is archived, not canon.");
        FocusComposer();
        InputBox.CaretIndex = InputBox.Text.Length;
    }

    private void EditLastPromptButton_Click(object sender, RoutedEventArgs e)
    {
        StoryMessage? prompt = GetEditablePrompt();
        if (prompt is not null) BeginPromptEdit(prompt);
    }

    private void CancelPromptEditButton_Click(object sender, RoutedEventArgs e)
    {
        if (_editingUserPrompt is null || _generationCts is not null) return;
        _editingUserPrompt = null;
        InputBox.Clear();
        SetSelectedStoryAction(StoryActionType.Chat);
        SetStatus(null);
        UpdatePromptEditUi();
    }
}
