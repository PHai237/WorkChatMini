using System.Windows;

namespace WorkChatMini;

public partial class StoryPanel
{
    private StoryMessage? GetRetryablePrompt()
    {
        if (_activeProject is null || _messages.Count == 0 || _checkpointRecoveryFailed ||
            _loadingProject || _generationCts is not null || _storageAuditBusy ||
            _creativeDecisionBusy || _creatingStory) return null;
        // Only retry a pending user message or an explicitly incomplete reply.
        // A completed/committed chapter is never regenerated implicitly.
        return StoryRetryEligibility.FindLastUnansweredPrompt(_messages);
    }

    private void UpdateRetryLastUi()
    {
        if (RetryLastButton is null) return;
        bool retryable = GetRetryablePrompt() is not null;
        RetryLastButton.Visibility = retryable ? Visibility.Visible : Visibility.Collapsed;
        RetryLastButton.IsEnabled = retryable;
    }

    private async void RetryLastButton_Click(object sender, RoutedEventArgs e)
    {
        if (GetRetryablePrompt() is null) return;
        AppLogger.Info("STORY", $"Retrying saved unanswered prompt | project={_activeProject?.Id}");
        await SendAsync(retryLast: true);
    }
}
