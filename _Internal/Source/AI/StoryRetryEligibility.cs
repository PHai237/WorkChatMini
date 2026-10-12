namespace WorkChatMini;

internal static class StoryRetryEligibility
{
    internal static StoryMessage? FindLastUnansweredPrompt(IReadOnlyList<StoryMessage> messages)
    {
        if (messages.Count == 0) return null;
        int index = messages.Count - 1;
        if (messages[index].IsInterruptedResponse &&
            string.Equals(messages[index].Role, "assistant", StringComparison.OrdinalIgnoreCase))
            index--;
        // Edited prompts can have one or more archived AI responses immediately
        // after the user turn; they must not hide the real unanswered prompt.
        while (index >= 0 && messages[index].IsSupersededResponse &&
               string.Equals(messages[index].Role, "assistant", StringComparison.OrdinalIgnoreCase))
            index--;
        return index >= 0 && string.Equals(messages[index].Role, "user", StringComparison.OrdinalIgnoreCase)
            ? messages[index] : null;
    }
}
