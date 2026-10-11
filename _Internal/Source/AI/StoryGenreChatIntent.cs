namespace WorkChatMini;

/// <summary>Only a creator's explicit genre declaration may adjust the project identity.
/// An ordinary plot discussion or assistant guess must never modify genres.</summary>
internal static class StoryGenreChatIntent
{
    private static readonly (string Genre, string[] Keywords)[] Dictionary =
    {
        ("Medieval Fantasy", new[] { "fantasy", "giả tưởng", "gia tuong" }),
        ("MMORPG / Game System", new[] { "mmorpg", "game system", "hệ thống game", "he thong game" }),
        ("Isekai / Xuyên không", new[] { "isekai", "xuyên không", "xuyen khong" }),
        ("Rebirth / Trọng sinh", new[] { "rebirth", "trọng sinh", "trong sinh" }),
        ("Xianxia / Tu tiên", new[] { "xianxia", "tu tiên", "tu tien" }),
        ("Romcom", new[] { "romcom", "hài lãng mạn", "hai lang man" }),
        ("Romance", new[] { "romance", "lãng mạn", "lang man" }),
        ("School Life", new[] { "school life", "học đường", "hoc duong" }),
        ("Slice of Life", new[] { "slice of life", "đời thường", "doi thuong" }),
        ("Mystery", new[] { "mystery", "bí ẩn", "bi an", "trinh thám", "trinh tham" }),
        ("Horror", new[] { "horror", "kinh dị", "kinh di" }),
        ("Sci-Fi", new[] { "sci-fi", "khoa học viễn tưởng", "khoa hoc vien tuong" }),
        ("Dark Fantasy", new[] { "dark fantasy" })
    };

    internal static IReadOnlyList<string> FindDeclaredGenres(string input)
    {
        string lower = input.Trim().ToLowerInvariant();
        bool explicitGenre = lower.StartsWith("thể loại:") || lower.StartsWith("the loai:") ||
            lower.StartsWith("genres:") || lower.StartsWith("genre:") ||
            lower.StartsWith("tôi muốn viết truyện ") || lower.StartsWith("mình muốn viết truyện ");
        if (!explicitGenre || input.Length > 500) return Array.Empty<string>();
        int boundary = lower.IndexOfAny(new[] {'.', '\n', ';'});
        string head = boundary < 0 ? lower : lower[..boundary];
        var results = new List<string>();
        foreach ((string genre, string[] keywords) in Dictionary)
        {
            if (keywords.Any(keyword => HasPhrase(head, keyword))) results.Add(genre);
        }
        // Avoid silently overwriting the user's existing genre selection.
        return results.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
    }

    private static bool HasPhrase(string source, string phrase)
    {
        int position = source.IndexOf(phrase, StringComparison.OrdinalIgnoreCase);
        while (position >= 0)
        {
            int end = position + phrase.Length;
            if ((position == 0 || !char.IsLetterOrDigit(source[position - 1])) &&
                (end == source.Length || !char.IsLetterOrDigit(source[end]))) return true;
            position = source.IndexOf(phrase, position + 1, StringComparison.OrdinalIgnoreCase);
        }
        return false;
    }
}
