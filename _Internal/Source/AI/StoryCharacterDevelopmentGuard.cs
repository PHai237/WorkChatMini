namespace WorkChatMini;

// Only a conservative diagnostic. Arbitrary arc stages are author-defined;
// transitions, relapses, and unresolved contradictions are valid storytelling.
internal static class StoryCharacterDevelopmentGuard
{
    internal static void Check(StoryMemoryDocument memory, StoryMemoryUpdate update, StoryDeterministicReport report)
    {
        foreach (StoryCharacterArcPatch patch in update.CharacterArcs)
        {
            if (string.IsNullOrWhiteSpace(patch.Character) || string.IsNullOrWhiteSpace(patch.Title))
                continue;
            StoryCharacterMemory? owner = StoryCharacterIdentity.ResolveUnique(memory, patch.Character, out bool ambiguous);
            if (ambiguous)
            {
                report.Issues.Add(new StoryDeterministicIssue
                {
                    Code = "ambiguous-character-arc-owner",
                    Severity = StoryDeterministicSeverity.Risk,
                    Message = $"Character arc '{patch.Title}' names an ambiguous alias '{patch.Character}'; use a canonical name before merging."
                });
                continue;
            }
            string canonical = owner?.Name ?? patch.Character.Trim();
            StoryCharacterArcMemory? current = memory.CharacterArcs.FirstOrDefault(x =>
                Same(x.Character, canonical) && Same(x.Title, patch.Title));
            if (current is null || (!string.IsNullOrWhiteSpace(patch.LastChange) &&
                                    !Same(patch.LastChange, current.LastChange)))
                continue;
            bool moved = (!string.IsNullOrWhiteSpace(patch.Stage) &&
                           !string.IsNullOrWhiteSpace(current.Stage) && !Same(patch.Stage, current.Stage)) ||
                         (!string.IsNullOrWhiteSpace(patch.Direction) &&
                          !Same(StoryCharacterArcLedger.NormalizeDirection(patch.Direction),
                                StoryCharacterArcLedger.NormalizeDirection(current.Direction)));
            bool reopened = Same(current.Status, "Resolved") && Same(patch.Status, "Active");
            if (moved || reopened)
                report.Issues.Add(new StoryDeterministicIssue
                {
                    Code = "character-development-missing-evidence",
                    Severity = StoryDeterministicSeverity.Risk,
                    Message = $"{canonical}'s arc '{current.Title}' changes stage/direction/status without a cited on-page turning point. Check final prose; genuine relapse or reversal is allowed."
                });
        }
    }

    private static bool Same(string? left, string? right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}
