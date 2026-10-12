namespace WorkChatMini;

/// <summary>
/// Narrative drafts are not canon. Exclude their prompts and responses from model context,
/// regardless of manuscript mode, until the user commits one. No database changes required.
/// </summary>
public static class StoryDraftIsolation
{
    public static IReadOnlyList<StoryMessage> ForGeneration(IReadOnlyList<StoryMessage> messages)
    {
        var excluded = new HashSet<int>();
        for (int i = 0; i < messages.Count; i++)
        {
            StoryMessage message = messages[i];
            if (!string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase))
                continue;
            if (message.IsSupersededResponse)
            {
                excluded.Add(i);
                continue;
            }
            if (message.IsInterruptedResponse)
            {
                // Incomplete Chat responses are not reliable creative context.
                // Their user prompt remains available. Narrative prompts are
                // paired with their draft and excluded by the rule below.
                excluded.Add(i);
            }
            if (message.ManuscriptCommitted || !StoryActions.TryParse(message.Action, out StoryActionType action) ||
                !StoryActions.IsNarrativeAction(action))
                continue;
            excluded.Add(i);
            if (i > 0 && string.Equals(messages[i - 1].Role, "user", StringComparison.OrdinalIgnoreCase) &&
                string.Equals(messages[i - 1].Action, message.Action, StringComparison.OrdinalIgnoreCase))
                excluded.Add(i - 1);
        }
        return messages.Where((_, index) => !excluded.Contains(index)).ToArray();
    }


    public static bool AreSiblings(StoryMessage a, StoryMessage b) =>
        !string.IsNullOrWhiteSpace(a.DraftGroupId) &&
        string.Equals(a.DraftGroupId, b.DraftGroupId, StringComparison.Ordinal) &&
        a.IsNarrativeDraft && b.IsNarrativeDraft;

    public static bool HasCommittedSibling(IReadOnlyList<StoryMessage> messages, StoryMessage draft) =>
        messages.Any(item => !ReferenceEquals(item, draft) && AreSiblings(item, draft) && item.ManuscriptCommitted);
}
