namespace WorkChatMini;

// Read-only query expansion. A nickname is not a new identity or a canon fact.
// Shared nicknames are deliberately not resolved without an unambiguous name.
internal static class StoryAliasResolver
{
    internal sealed record Result(
        IReadOnlyList<string> Names,
        IReadOnlyList<string> Terms,
        IReadOnlyList<string> Reasons);

    internal static Result Resolve(StoryMemoryDocument memory, string query)
    {
        if (string.IsNullOrWhiteSpace(query) || memory.Characters.Count == 0)
            return new Result(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());

        StoryCharacterMemory[] characters = memory.Characters
            .Where(c => !string.IsNullOrWhiteSpace(c.Name))
            .ToArray();
        var chosen = new List<StoryCharacterMemory>();
        var reasons = new List<string>();
        foreach (StoryCharacterMemory character in characters)
        {
            if (ContainsWholePhrase(query, character.Name) &&
                characters.Count(c => string.Equals(c.Name.Trim(), character.Name.Trim(), StringComparison.OrdinalIgnoreCase)) == 1)
                chosen.Add(character);
        }

        // A matched full name takes precedence over an ambiguous nickname, but
        // never silently maps a nickname shared by different characters.
        foreach (StoryCharacterMemory character in characters)
        {
            if (chosen.Contains(character))
                continue;
            foreach (string alias in character.Aliases.Where(a => !string.IsNullOrWhiteSpace(a)))
            {
                if (!ContainsWholePhrase(query, alias))
                    continue;
                int owners = characters.Count(c =>
                    string.Equals(c.Name.Trim(), alias.Trim(), StringComparison.OrdinalIgnoreCase) ||
                    c.Aliases.Any(a => string.Equals(a.Trim(), alias.Trim(), StringComparison.OrdinalIgnoreCase)));
                if (owners != 1)
                {
                    reasons.Add($"Ambiguous character alias not expanded: {alias.Trim()}");
                    continue;
                }
                chosen.Add(character);
                reasons.Add($"Character alias resolved: {alias.Trim()} → {character.Name.Trim()}");
                break;
            }
        }

        string[] names = chosen.Select(c => c.Name.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(4).ToArray();
        // Only canonical names are added to lexical retrieval. This prevents
        // unrelated story fragments sharing a loose nickname from being promoted.
        string[] terms = names.SelectMany(StoryMemoryRetriever.Tokenize)
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(12).ToArray();
        return new Result(names, terms, reasons.Distinct(StringComparer.OrdinalIgnoreCase).Take(6).ToArray());
    }

    private static bool ContainsWholePhrase(string text, string phrase)
    {
        string needle = phrase.Trim();
        if (needle.Length < 3)
            return false;
        int from = 0;
        while (from < text.Length)
        {
            int at = text.IndexOf(needle, from, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
                return false;
            int end = at + needle.Length;
            bool left = at == 0 || !IsWord(text[at - 1]);
            bool right = end == text.Length || !IsWord(text[end]);
            if (left && right)
                return true;
            from = at + 1;
        }
        return false;
    }

    private static bool IsWord(char c) => char.IsLetterOrDigit(c) || c == '_';
}
