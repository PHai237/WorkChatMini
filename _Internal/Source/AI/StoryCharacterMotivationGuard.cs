namespace WorkChatMini;

// Non-destructive bookkeeping diagnostics. An absent/mismatched structured
// patch warrants review; it does not prove the final prose is contradictory.
internal static class StoryCharacterMotivationGuard
{
    internal static void Check(StoryMemoryDocument memory, StoryMemoryUpdate update,
        StoryDeterministicReport report)
    {
        foreach (StoryEventPatch storyEvent in update.Events)
        {
            if (!string.Equals(storyEvent.Status, "Confirmed", StringComparison.OrdinalIgnoreCase) ||
                !IsChoice(storyEvent.Type))
                continue;

            if (string.IsNullOrWhiteSpace(storyEvent.Evidence))
                Risk("decision-missing-evidence",
                    $"The {storyEvent.Type} event for '{storyEvent.Subject}' has no on-page evidence.");

            if (!string.Equals(storyEvent.Type, "GoalAbandoned", StringComparison.OrdinalIgnoreCase) ||
                string.IsNullOrWhiteSpace(storyEvent.Target))
                continue;
            StoryCharacterMemory? actor = StoryCharacterIdentity.ResolveUnique(
                memory, storyEvent.Subject, out bool ambiguous);
            if (ambiguous)
            {
                Risk("ambiguous-choice-identity",
                    $"Goal abandonment uses ambiguous actor '{storyEvent.Subject}'. Use the unique canonical name.");
                continue;
            }
            if (actor is null || !actor.Goals.Any(x => Same(x, storyEvent.Target)))
                continue; // No verified existing current goal to reconcile.

            bool removed = update.Characters.Any(patch =>
                ReferenceEquals(StoryCharacterIdentity.ResolveUnique(memory, patch.Name, out _), actor) &&
                patch.RemovedGoals.Any(goal => Same(goal, storyEvent.Target)));
            if (!removed)
                Risk("abandoned-goal-still-current",
                    $"'{actor.Name}' abandons goal '{storyEvent.Target}' in Event Ledger, but no matching removedGoals patch updates current state.");
        }

        // One bookkeeping response must not simultaneously close the same vow
        // as both kept and broken, or remove and re-add the identical goal.
        // These are reviewable Risk diagnostics, never destructive rewrites.
        StoryEventPatch[] outcomes = update.Events.Where(e =>
            string.Equals(e.Status, "Confirmed", StringComparison.OrdinalIgnoreCase) &&
            (Same(e.Type, "PromiseKept") || Same(e.Type, "PromiseBroken")) &&
            !string.IsNullOrWhiteSpace(e.Target)).ToArray();
        for (int i = 0; i < outcomes.Length; i++)
        {
            StoryCharacterMemory? first = StoryCharacterIdentity.ResolveUnique(
                memory, outcomes[i].Subject, out bool firstAmbiguous);
            if (firstAmbiguous || first is null) continue;
            for (int j = i + 1; j < outcomes.Length; j++)
            {
                StoryCharacterMemory? second = StoryCharacterIdentity.ResolveUnique(
                    memory, outcomes[j].Subject, out bool secondAmbiguous);
                if (secondAmbiguous || !ReferenceEquals(first, second) ||
                    !Same(outcomes[i].Target, outcomes[j].Target) ||
                    Same(outcomes[i].Type, outcomes[j].Type)) continue;
                Risk("contradictory-vow-outcomes",
                    $"'{first.Name}' has both PromiseKept and PromiseBroken for '{outcomes[i].Target}' in one chapter update. Check the prose before accepting both outcomes.");
            }
        }
        foreach (StoryCharacterPatch patch in update.Characters)
        {
            if (patch.Goals.Any(added => patch.RemovedGoals.Any(removed => Same(added, removed))))
                Risk("goal-readded-in-same-update",
                    $"'{patch.Name}' adds and removes the same goal in one update. Check the intended final state.");
        }

        void Risk(string code, string message) => report.Issues.Add(new StoryDeterministicIssue
        {
            Code = code,
            Severity = StoryDeterministicSeverity.Risk,
            Message = message
        });
    }

    private static bool IsChoice(string? type) => type?.Trim().ToLowerInvariant() switch
    {
        "decisionmade" or "sacrificemade" or "goalabandoned" or "promisemade" or
        "promisekept" or "promisebroken" => true,
        _ => false
    };

    private static bool Same(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left) && !string.IsNullOrWhiteSpace(right) &&
        string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
}
