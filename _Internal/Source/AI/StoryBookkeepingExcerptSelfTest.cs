namespace WorkChatMini;

internal static class StoryBookkeepingExcerptSelfTest
{
    internal static void Verify()
    {
        const string shortChapter = "Short scene with an ending.";
        if (StoryMemoryBookkeeper.BuildBookkeepingExcerpt(shortChapter) != shortChapter)
            throw new InvalidOperationException("Short chapter was changed by bookkeeping excerpting.");

        string barelyLong = "START" + new string('x', StoryMemoryBookkeeper.MaxBookkeepingCharacters - 7) + "END";
        string barelyExcerpt = StoryMemoryBookkeeper.BuildBookkeepingExcerpt(barelyLong);
        if (barelyExcerpt.Length > StoryMemoryBookkeeper.MaxBookkeepingCharacters ||
            !barelyExcerpt.StartsWith("START", StringComparison.Ordinal) ||
            !barelyExcerpt.EndsWith("END", StringComparison.Ordinal))
            throw new InvalidOperationException("Near-budget bookkeeping excerpt lost opening or ending.");

        string longChapter = "OPENING_EVIDENCE " + new string('a', 21000) +
            " FIRST_OMITTED " + new string('a', 21000) +
            " MIDDLE_EVIDENCE " + new string('b', 21000) +
            " SECOND_OMITTED " + new string('b', 21000) + " ENDING_EVIDENCE";
        string excerpt = StoryMemoryBookkeeper.BuildBookkeepingExcerpt(longChapter);
        if (excerpt.Length > StoryMemoryBookkeeper.MaxBookkeepingCharacters ||
            !excerpt.StartsWith("OPENING_EVIDENCE", StringComparison.Ordinal) ||
            !excerpt.EndsWith("ENDING_EVIDENCE", StringComparison.Ordinal) ||
            !excerpt.Contains("MIDDLE_EVIDENCE", StringComparison.Ordinal) ||
            excerpt.Contains("FIRST_OMITTED", StringComparison.Ordinal) ||
            excerpt.Contains("SECOND_OMITTED", StringComparison.Ordinal) ||
            !excerpt.Contains("[OMITTED PASSAGE 1", StringComparison.Ordinal) ||
            !excerpt.Contains("[OMITTED PASSAGE 2", StringComparison.Ordinal))
            throw new InvalidOperationException("Bookkeeping excerpt lost a key evidence window or implied full coverage.");
    }
}
