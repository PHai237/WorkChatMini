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
        else if (!string.Equals(messages[index].Role, "user", StringComparison.OrdinalIgnoreCase))
            return null;
        return index >= 0 && string.Equals(messages[index].Role, "user", StringComparison.OrdinalIgnoreCase)
            ? messages[index] : null;
    }
}
