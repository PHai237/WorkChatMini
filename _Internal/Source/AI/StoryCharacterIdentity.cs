namespace WorkChatMini;

// A canonical name takes precedence over nicknames. Shared nicknames or duplicate
// canonical records are not identities: never pick the first matching character.
// This is deliberately branch-local, read-only lookup; no new canon is inferred.
internal static class StoryCharacterIdentity
{
    internal static StoryCharacterMemory? ResolveUnique(
        StoryMemoryDocument memory, string? subject, out bool ambiguous)
    {
        ambiguous = false;
        if (string.IsNullOrWhiteSpace(subject))
            return null;
        string label = subject.Trim();

        StoryCharacterMemory[] canonical = memory.Characters
            .Where(c => Same(c.Name, label)).Take(2).ToArray();
        if (canonical.Length > 0)
        {
            ambiguous = canonical.Length != 1;
            return ambiguous ? null : canonical[0];
        }

        StoryCharacterMemory[] aliases = memory.Characters
            .Where(c => c.Aliases.Any(alias => Same(alias, label)))
            .Take(2).ToArray();
        ambiguous = aliases.Length > 1;
        return aliases.Length == 1 ? aliases[0] : null;
    }

    private static bool Same(string? a, string? b) =>
        !string.IsNullOrWhiteSpace(a) && !string.IsNullOrWhiteSpace(b) &&
        string.Equals(a.Trim(), b.Trim(), StringComparison.OrdinalIgnoreCase);
}
