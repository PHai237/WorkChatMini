namespace WorkChatMini;

/// <summary>Editable author intent stored alongside conversation messages and included
/// in branch/checkpoint snapshots. Never modifies canonical Story Memory.</summary>
public sealed class StoryCreativeRevision
{
    public string PreviousText { get; set; } = string.Empty;
    public DateTimeOffset ChangedUtc { get; set; }
}

internal static class StoryCreativeDecision
{
    internal static bool Revise(StoryMessage target, string? wording, DateTimeOffset changedUtc,
        string? category = null)
    {
        string normalized = StoryCoCreation.NormalizeDirection(wording);
        if (normalized.Length == 0) return false;
        string prior = StoryCoCreation.NormalizeDirection(target.CreativeDirection);
        if (prior.Length > 0 && !string.Equals(prior, normalized, StringComparison.Ordinal))
        {
            target.CreativeDirectionRevisions ??= new();
            target.CreativeDirectionRevisions.Add(new StoryCreativeRevision
            {
                PreviousText = prior, ChangedUtc = changedUtc
            });
        }
        target.CreativeDirection = normalized;
        if (category is not null)
            target.CreativeDirectionCategory = StoryIdeaOrganizer.NormalizeCategory(category) == StoryIdeaOrganizer.General
                ? null : StoryIdeaOrganizer.NormalizeCategory(category);
        target.CreativeDirectionArchived = false;
        return true;
    }

    // Replacement must be deliberately selected in the UI. We cannot infer
    // semantic conflicts from Vietnamese prose with keyword matching.
    internal static bool Replace(StoryMessage incoming, StoryMessage old, string? wording, DateTimeOffset utc,
        string? category = null)
    {
        if (ReferenceEquals(incoming, old) || old.CreativeDirectionArchived ||
            string.IsNullOrWhiteSpace(old.CreativeDirection)) return false;
        if (!Revise(incoming, wording, utc, category)) return false;
        old.CreativeDirectionArchived = true;
        return true;
    }

    internal static bool SetDeclined(StoryMessage assistant, string? suggestion, bool declined)
    {
        if (assistant.Role != "assistant" || assistant.Action != StoryActionType.Chat.ToString() ||
            assistant.IsNarrativeDraft || assistant.DraftDiscarded ||
            assistant.IsInterruptedResponse) return false;
        string item = StoryCoCreation.NormalizeDirection(suggestion);
        // Only permit an exact option surfaced by the local extractor. Never
        // turn arbitrary text, quoted prompt injection or a draft into intent.
        if (item.Length == 0 || !StoryCreativeSuggestionExtractor.ExtractDetailed(assistant.Content)
                .Any(option => string.Equals(option.Text, item, StringComparison.Ordinal))) return false;
        bool existing = assistant.DeclinedCreativeSuggestions?.Contains(item,
            StringComparer.OrdinalIgnoreCase) == true;
        if (existing == declined) return false;
        if (declined)
        {
            List<string> items = assistant.DeclinedCreativeSuggestions ??= new();
            if (items.Count >= 3) return false;
            items.Add(item);
        }
        else
        {
            if (assistant.DeclinedCreativeSuggestions is not { } items) return false;
            items.RemoveAll(x => string.Equals(x, item, StringComparison.OrdinalIgnoreCase));
            if (items.Count == 0)
                assistant.DeclinedCreativeSuggestions = null;
        }
        return true;
    }

    internal static IReadOnlyList<string> ActiveDeclines(IReadOnlyList<StoryMessage> messages)
    {
        var accepted = new HashSet<string>(StoryCoCreation.ActiveDirections(messages)
            .Select(m => StoryCoCreation.NormalizeDirection(m.CreativeDirection)), StringComparer.OrdinalIgnoreCase);
        var declined = new List<string>();
        foreach (StoryMessage m in messages.Reverse())
        {
            if (m.Role != "assistant" || m.Action != StoryActionType.Chat.ToString() ||
                m.IsNarrativeDraft || m.DraftDiscarded || m.IsInterruptedResponse ||
                m.DeclinedCreativeSuggestions is null) continue;
            foreach (string item in m.DeclinedCreativeSuggestions.AsEnumerable().Reverse())
            {
                string normalized = StoryCoCreation.NormalizeDirection(item);
                if (normalized.Length == 0 || accepted.Contains(normalized) ||
                    declined.Contains(normalized, StringComparer.OrdinalIgnoreCase)) continue;
                declined.Add(normalized);
                if (declined.Count == 8) return declined;
            }
        }
        return declined;
    }
}
