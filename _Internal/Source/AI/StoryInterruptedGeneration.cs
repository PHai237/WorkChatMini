namespace WorkChatMini;

/// <summary>
/// Classifies a provider reply that ended before success. The original streamed
/// text is retained verbatim, but incomplete text is never a manuscript draft,
/// accepted editorial idea, or a structured canon/proposal update.
/// </summary>
internal static class StoryInterruptedGeneration
{
    internal static bool TryPrepare(StoryMessage message, string? accumulated, bool stopped)
    {
        if (message.IsInterruptedResponse || message.IsNarrativeDraft || message.ManuscriptCommitted ||
            string.IsNullOrWhiteSpace(accumulated)) return false;
        message.Content = accumulated + (stopped
            ? "\n\n[Stopped before completion · not committed to Story]"
            : "\n\n[Generation interrupted · not committed to Story]");
        message.IsInterruptedResponse = true;
        message.IsNarrativeDraft = false;
        message.ManuscriptCommitted = false;
        message.ProposalKind = string.Empty;
        message.ProposalJson = string.Empty;
        message.ProposalApplied = false;
        message.CreativeDirection = string.Empty;
        message.DeclinedCreativeSuggestions = null;
        return true;
    }
}
