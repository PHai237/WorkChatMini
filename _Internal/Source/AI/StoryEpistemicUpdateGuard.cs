namespace WorkChatMini;

// A conservative, read-only bookkeeping review. Secret overlap is a risk, not
// proof of a leak: a scene may contain an unrecorded but legitimate disclosure.
// Never delete a knowledge patch or claim a missing event proves ignorance.
internal static class StoryEpistemicUpdateGuard
{
    internal static void Check(
        StoryMemoryDocument memory, StoryMemoryUpdate update, StoryDeterministicReport report)
    {
        foreach (StoryCharacterPatch patch in update.Characters
                     .Where(x => !string.IsNullOrWhiteSpace(x.Name)))
        {
            StoryCharacterMemory? recipient = StoryCharacterIdentity.ResolveUnique(
                memory, patch.Name, out bool ambiguous);
            if (ambiguous)
            {
                report.Issues.Add(new StoryDeterministicIssue
                {
                    Code = "ambiguous-character-update",
                    Severity = StoryDeterministicSeverity.Risk,
                    Message = $"Bookkeeping uses shared/duplicate character identity '{patch.Name}'. " +
                              "Its character-state patch will not be applied; use a unique canonical name."
                });
                continue;
            }
            if (recipient is null)
                continue; // New character: cannot establish a distinct owner of a secret.

            foreach (string fact in patch.Knowledge
                         .Where(x => !string.IsNullOrWhiteSpace(x))
                         .Select(x => x.Trim())
                         .Distinct(StringComparer.OrdinalIgnoreCase).Take(12))
            {
                if (recipient.Knowledge.Any(x => Same(x, fact)))
                    continue;

                // Exact equivalence, not substring or fuzzy similarity: a similar
                // phrase cannot prove that a character learned someone else's secret.
                bool otherPrivateSecret = memory.Characters.Any(other =>
                    !ReferenceEquals(other, recipient) &&
                    other.Secrets.Any(secret => Same(secret, fact)));
                if (!otherPrivateSecret)
                    continue;

                bool explicitLearning = update.Events.Any(e =>
                    Same(e.Type, "KnowledgeGained") &&
                    Same(e.Status, "Confirmed") &&
                    Same(e.Target, fact) &&
                    ReferenceEquals(StoryCharacterIdentity.ResolveUnique(memory, e.Subject, out _), recipient));
                if (explicitLearning)
                    continue;

                report.Issues.Add(new StoryDeterministicIssue
                {
                    Code = "unverified-secret-transfer",
                    Severity = StoryDeterministicSeverity.Risk,
                    Message = $"'{recipient.Name}' gains knowledge matching another character's private secret " +
                              $"('{Limit(fact)}') without a confirmed KnowledgeGained event in this update. " +
                              "Verify an on-page reveal or witness before trusting this change."
                });
            }
        }
    }

    private static bool Same(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) &&
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);

    private static string Limit(string value)
    {
        string clean = value.Trim();
        return clean.Length <= 120 ? clean : clean[..120] + "…";
    }
}
