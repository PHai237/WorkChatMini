namespace WorkChatMini;

/// <summary>
/// Pure, local organization of creator-approved editorial notes. Categories are
/// assigned by the creator and never inferred from AI text or promoted to canon.
/// Search/filtering never mutates conversation data or impacts chapter retrieval.
/// </summary>
internal static class StoryIdeaOrganizer
{
    internal const string All = "All";
    internal const string Active = "Active";
    internal const string Archived = "Archived";
    internal const string Passed = "Passed";
    internal const string General = "General";

    internal static readonly IReadOnlyList<string> Categories =
        new[] { General, "Characters", "Plot", "World", "Relationships", "Writing style" };
    internal static readonly IReadOnlyList<string> CategoryFilters =
        new[] { All, General, "Characters", "Plot", "World", "Relationships", "Writing style" };
    internal static readonly IReadOnlyList<string> StatusFilters =
        new[] { All, Active, Archived, Passed };

    internal static string NormalizeCategory(string? value) =>
        Categories.FirstOrDefault(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase)) ?? General;

    internal static bool Matches(StoryMessage note, string? search, string? status, string? category)
    {
        if (string.IsNullOrWhiteSpace(note.CreativeDirection) ||
            note.Action != StoryActionType.Chat.ToString() || note.DraftDiscarded ||
            note.IsInterruptedResponse ||
            (note.IsNarrativeDraft && !note.ManuscriptCommitted)) return false;
        if (string.Equals(status, Passed, StringComparison.Ordinal)) return false;
        if (string.Equals(status, Active, StringComparison.Ordinal) && note.CreativeDirectionArchived) return false;
        if (string.Equals(status, Archived, StringComparison.Ordinal) && !note.CreativeDirectionArchived) return false;
        if (!string.IsNullOrEmpty(category) && category != All &&
            !string.Equals(NormalizeCategory(note.CreativeDirectionCategory), category,
                StringComparison.Ordinal)) return false;
        return string.IsNullOrWhiteSpace(search) || note.CreativeDirection.Contains(search.Trim(),
            StringComparison.OrdinalIgnoreCase);
    }

    internal static bool MatchesDecline(string declined, string? search, string? status, string? category) =>
        (status == All || status == Passed) && (category == All || string.IsNullOrEmpty(category)) &&
        (string.IsNullOrWhiteSpace(search) || declined.Contains(search.Trim(), StringComparison.OrdinalIgnoreCase));
}
