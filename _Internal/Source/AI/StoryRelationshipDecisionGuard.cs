namespace WorkChatMini;

// Conservative diagnostic for bookkeeping. It never blocks a plausible plot
// reversal; the final prose may explain the change even when patch.Change does not.
internal static class StoryRelationshipDecisionGuard
{
    internal static void Check(StoryMemoryDocument memory, StoryMemoryUpdate update, StoryDeterministicReport report)
    {
        foreach (StoryRelationshipPatch patch in update.Relationships)
        {
            if (string.IsNullOrWhiteSpace(patch.From) || string.IsNullOrWhiteSpace(patch.To))
                continue;
            bool valid = StoryRelationshipLedger.TryCanonicalPair(
                memory, patch.From, patch.To, out string from, out string to);
            if (!valid)
            {
                report.Issues.Add(new StoryDeterministicIssue
                {
                    Code = "ambiguous-or-self-relationship-identity",
                    Severity = StoryDeterministicSeverity.Risk,
                    Message = $"Relationship '{patch.From}' → '{patch.To}' cannot be resolved to two distinct, unambiguous characters. Do not guess its owners."
                });
                continue;
            }
            if (string.IsNullOrWhiteSpace(patch.State) || !string.IsNullOrWhiteSpace(patch.Change))
                continue;
            StoryRelationshipMemory[] current = memory.Relationships.Where(x =>
                StoryRelationshipLedger.TryCanonicalPair(memory, x.From, x.To,
                    out string oldFrom, out string oldTo) &&
                Same(from, oldFrom) && Same(to, oldTo)).Take(2).ToArray();
            if (current.Length != 1 || string.IsNullOrWhiteSpace(current[0].State) ||
                Same(current[0].State, patch.State))
                continue;
            report.Issues.Add(new StoryDeterministicIssue
            {
                Code = "relationship-shift-missing-evidence",
                Severity = StoryDeterministicSeverity.Risk,
                Message = $"Relationship {from} → {to} changes from '{current[0].State}' to '{patch.State}' without an explicit change explanation. Verify final prose; an earned reversal is allowed."
            });
        }
    }

    private static bool Same(string? a, string? b) =>
        string.Equals(a?.Trim(), b?.Trim(), StringComparison.OrdinalIgnoreCase);
}
