namespace WorkChatMini;

// This is a deliberately small, local paraphrase *candidate* bridge, not
// embeddings, translation, factual reasoning or an authoritative story source.
// Alternate terms are only used if normal chapter retrieval finds no hit.
internal static class StoryLocalSemanticBridge
{
    private const int MaxAlternatives = 12;

    // Narrow narrative-equivalent phrases; never include names, identity claims,
    // relationship status, events, abilities, or world rules in this dictionary.
    private static readonly string[][] PhraseGroups =
    {
        new[] { "mất tích", "mat tich", "biến mất", "bien mat", "disappeared", "vanished", "went missing" },
        new[] { "bị bắt cóc", "bi bat coc", "bắt cóc", "bat coc", "kidnapped", "abducted" },
        new[] { "bị phản bội", "bi phan boi", "phản bội", "phan boi", "betrayed", "betrayal" },
        new[] { "bị thương", "thương tích", "injured", "wounded" },
        new[] { "qua đời", "thiệt mạng", "died", "passed away" },
        new[] { "chữa lành", "hồi phục", "healed", "recovered" },
        new[] { "đánh mất", "làm mất", "lost" },
        new[] { "tìm thấy", "phát hiện", "discovered", "found" },
        new[] { "lời tiên tri", "sấm truyền", "prophecy", "prophetic vision" },
        new[] { "mật thư", "mat thu", "thư bí mật", "thu bi mat", "secret letter", "coded letter" },
        new[] { "lối đi bí mật", "đường hầm bí mật", "secret passage", "hidden passage" },
        new[] { "lời thề", "thề nguyện", "oath", "solemn vow" }
    };

    internal static IReadOnlyList<string> Expand(string query)
    {
        if (string.IsNullOrWhiteSpace(query))
            return Array.Empty<string>();
        var results = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string[] group in PhraseGroups)
        {
            foreach (string phrase in group)
            {
                if (!ContainsWholePhrase(query, phrase))
                    continue;
                foreach (string alternate in group)
                {
                    if (!string.Equals(phrase, alternate, StringComparison.OrdinalIgnoreCase))
                        results.Add(alternate);
                    if (results.Count >= MaxAlternatives)
                        return results.Take(MaxAlternatives).ToArray();
                }
            }
        }
        return results.ToArray();
    }

    internal static bool ContainsWholePhrase(string text, string phrase)
    {
        if (string.IsNullOrWhiteSpace(text) || string.IsNullOrWhiteSpace(phrase))
            return false;
        int start = 0;
        while (start < text.Length)
        {
            int at = text.IndexOf(phrase, start, StringComparison.OrdinalIgnoreCase);
            if (at < 0)
                return false;
            int end = at + phrase.Length;
            if ((at == 0 || !IsWord(text[at - 1])) &&
                (end == text.Length || !IsWord(text[end])))
                return true;
            start = at + 1;
        }
        return false;
    }

    private static bool IsWord(char c) => char.IsLetterOrDigit(c) || c is '_' or '-';
}
