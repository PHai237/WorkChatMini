namespace WorkChatMini;

// Offline, no file writes, provider calls or real project data.
internal static class StoryMemoryCoverageSelfTest
{
    internal static void Verify()
    {
        StoryMemoryDocument legacy = StoryMemoryStore.ParseMemoryDocument(
            "{\"chapters\":[{\"number\":1,\"fileName\":\"0001-Open.md\",\"bookkeepingStatus\":\"namesonly\"}," +
            "{\"number\":2,\"fileName\":\"0002-Second.md\",\"bookkeepingStatus\":\"REVIEWED\"}," +
            "{\"number\":3,\"fileName\":\"0003-Third.md\",\"bookkeepingStatus\":\"incomplete\"}]} ");
        if (legacy.Chapters[0].BookkeepingStatus != "NamesOnly" ||
            legacy.Chapters[1].BookkeepingStatus != "Reviewed" ||
            legacy.Chapters[2].BookkeepingStatus != "Incomplete")
            throw new InvalidOperationException("Memory coverage status normalization lost historical receipts.");
        legacy.Characters.Add(new StoryCharacterMemory { Name = "Mai" });
        if (StoryMemoryRepair.NamesOnlyCount(legacy) != 1 ||
            StoryMemoryRepair.UnreviewedCount(legacy) != 2 ||
            StoryMemoryRepair.PendingCount(legacy) != 1 ||
            StoryMemoryRepair.PendingChapters(legacy).Single().Number != 3 ||
            !StoryMemoryRepair.BuildCoverageWarning(legacy).Contains("Names-only", StringComparison.Ordinal))
            throw new InvalidOperationException("Names-only repair was incorrectly classified as fully reviewed.");
        legacy.Chapters[2].BookkeepingStatus = "NamesOnly";
        if (StoryMemoryRepair.PendingChapters(legacy).Count != 0 ||
            StoryMemoryRepair.UnreviewedCount(legacy) != 2)
            throw new InvalidOperationException("Names-only scans were repeated or treated as full bookkeeping.");

        var longStory = new StoryMemoryDocument();
        longStory.Characters.Add(new StoryCharacterMemory { Name = "Existing" });
        for (int i = 1; i <= 45; i++)
            longStory.Chapters.Add(new StoryChapterMemory
            {
                Number = i, FileName = $"{i:0000}-Chapter.md", BookkeepingStatus = "Skipped"
            });
        for (int batch = 0; batch < 3; batch++)
        {
            StoryChapterMemory[] next = StoryMemoryRepair.PendingChapters(longStory).ToArray();
            int expected = batch < 2 ? 20 : 5;
            if (next.Length != expected || next[0].Number != batch * 20 + 1)
                throw new InvalidOperationException("Large Memory repair did not advance in bounded chronological batches.");
            foreach (StoryChapterMemory chapter in next)
                chapter.BookkeepingStatus = "NamesOnly";
        }
        if (StoryMemoryRepair.CanRepair(longStory) || StoryMemoryRepair.UnreviewedCount(longStory) != 45 ||
            StoryMemoryRepair.NamesOnlyCount(longStory) != 45)
            throw new InvalidOperationException("Completed name scan was mistaken for complete structured canon.");

        var emptyBigStory = new StoryMemoryDocument();
        for (int n = 1; n <= 21; n++)
            emptyBigStory.Chapters.Add(new StoryChapterMemory
            {
                Number = n, FileName = $"{n:0000}-Chapter.md"
            });
        if (StoryMemoryRecovery.CanRecover(emptyBigStory) ||
            StoryMemoryRepair.PendingChapters(emptyBigStory).Count != 20)
            throw new InvalidOperationException("A 21-chapter empty Memory project had no safe name-only fallback.");
        emptyBigStory.Chapters.RemoveAt(20);
        if (!StoryMemoryRecovery.CanRecover(emptyBigStory) || StoryMemoryRepair.CanRepair(emptyBigStory))
            throw new InvalidOperationException("Full recovery for a small empty project was displaced by name-only repair.");
        for (int n = 21; n <= StoryMemoryRepair.MaxIndexedChapters + 1; n++)
            emptyBigStory.Chapters.Add(new StoryChapterMemory { Number = n, FileName = $"{n:0000}-Chapter.md" });
        if (StoryMemoryRepair.CanRepair(emptyBigStory))
            throw new InvalidOperationException("Unbounded chapter index was accepted for provider-backed name repair.");

        string a = StoryChapterEvidenceFingerprint.ForProse("Mai met An beside the river. ");
        string b = StoryChapterEvidenceFingerprint.ForProse("Mai met An beside the river.");
        string changed = StoryChapterEvidenceFingerprint.ForProse("Mai met An beyond the river.");
        if (a != b || a == changed || a.Length != 64)
            throw new InvalidOperationException("A chapter changed during provider extraction was not detectable.");
    }
}
