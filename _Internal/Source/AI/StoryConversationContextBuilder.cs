using System.Text;

namespace WorkChatMini;

/// <summary>
/// Selects the most recent eligible conversation messages for a writing action.
/// Walks backwards only until the bounded context is collected. In particular,
/// uncommitted drafts and their originating directions never become evidence for
/// the next chapter. This avoids allocating an O(history length) skip index set.
/// </summary>
internal static class StoryConversationContextBuilder
{
    internal const int MaxMessages = 20;
    internal const int MaxCharacters = 32000;

    internal static string Build(IReadOnlyList<StoryMessage> messages, string currentInstruction)
    {
        var selected = new List<StoryMessage>(Math.Min(MaxMessages, messages.Count));
        int characters = 0;
        int skipPreviousUser = -1;
        int start = messages.Count - 1;
        if (start >= 0 &&
            string.Equals(messages[start].Role, "user", StringComparison.OrdinalIgnoreCase) &&
            string.Equals(messages[start].Content, currentInstruction, StringComparison.Ordinal))
            start--;

        for (int i = start; i >= 0 && selected.Count < MaxMessages; i--)
        {
            if (i == skipPreviousUser)
            {
                skipPreviousUser = -1;
                continue;
            }

            StoryMessage message = messages[i];
            bool isAssistant = string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase);
            bool uncommittedNarrative = isAssistant && !message.ManuscriptCommitted &&
                (message.IsNarrativeDraft || message.IsInterruptedResponse);

            if (uncommittedNarrative)
            {
                bool hasNarrativeAction = StoryActions.TryParse(message.Action, out StoryActionType action) &&
                    StoryActions.IsNarrativeAction(action);
                if (i > 0 && (message.IsNarrativeDraft || hasNarrativeAction) &&
                    string.Equals(messages[i - 1].Role, "user", StringComparison.OrdinalIgnoreCase))
                    skipPreviousUser = i - 1;
                continue;
            }
            if (message.IsInterruptedResponse) continue;

            // Match the previous budget rule, including an oversized single
            // message. Later stages already bound their final prompt excerpts.
            if (characters + message.Content.Length > MaxCharacters && selected.Count > 0)
                break;
            selected.Add(message);
            characters += message.Content.Length;
        }

        var builder = new StringBuilder(Math.Min(characters + selected.Count * 16, 65536));
        for (int i = selected.Count - 1; i >= 0; i--)
        {
            StoryMessage message = selected[i];
            builder.Append(string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                ? "Assistant: " : "Creator: ");
            builder.AppendLine(message.Content);
        }
        return builder.ToString().Trim();
    }
}
