namespace WorkChatMini;

// No provider calls, filesystem writes or live Story access.
internal static class StoryMemoryRecoverySelfTest
{
    internal static void Verify()
    {
        var memory = new StoryMemoryDocument();
        if (StoryMemoryRecovery.CanRecover(memory))
            throw new InvalidOperationException("A Story without chapters offered recovery.");
        memory.Chapters.Add(new StoryChapterMemory { Number = 1, FileName = "0001-Opening.md" });
        memory.Chapters.Add(new StoryChapterMemory { Number = 2, FileName = "0002-Ending.md" });
        if (!StoryMemoryRecovery.CanRecover(memory))
            throw new InvalidOperationException("Empty structured memory was not eligible for recovery.");
        StoryMemoryDocument chapterOneContext = StoryMemoryRecovery.ContextThroughChapter(memory, 1);
        if (chapterOneContext.Chapters.Count != 1 || chapterOneContext.Chapters[0].Number != 1 ||
            memory.Chapters.Count != 2)
            throw new InvalidOperationException("Bookkeeper could see future chapter summaries.");
        memory.Canon.Add(new StoryCanonFact { Fact = "Author-approved fact" });
        if (StoryMemoryRecovery.CanRecover(memory))
            throw new InvalidOperationException("Recovery could overwrite existing manual canon.");
        memory.Canon.Clear();
        memory.Chapters[1].FileName = "../other.md";
        if (StoryMemoryRecovery.CanRecover(memory))
            throw new InvalidOperationException("Unsafe chapter path was accepted.");
        memory.Chapters[1].FileName = "0001-Opening.md";
        if (StoryMemoryRecovery.CanRecover(memory))
            throw new InvalidOperationException("Duplicate chapter filename was accepted.");
        memory.Chapters[1].FileName = "0002-Ending.md";
        memory.Chapters[1].Number = 1;
        if (StoryMemoryRecovery.CanRecover(memory))
            throw new InvalidOperationException("Duplicate chapter number was accepted.");
        memory.Chapters[1].Number = 2;
        for (int n = 3; n <= StoryMemoryRecovery.MaxChaptersPerRecovery + 1; n++)
            memory.Chapters.Add(new StoryChapterMemory { Number = n, FileName = $"{n:0000}-Test.md" });
        if (StoryMemoryRecovery.CanRecover(memory))
            throw new InvalidOperationException("Unbounded recovery requests were permitted.");
        memory.Chapters.RemoveRange(2, memory.Chapters.Count - 2);
        memory.Characters.Add(new StoryCharacterMemory { Name = "Existing character" });
        if (StoryMemoryRecovery.CanRecover(memory))
            throw new InvalidOperationException("Recovery could overwrite existing characters.");
        if (StoryMemoryRecovery.HasExtractedFacts(new StoryMemoryUpdate()))
            throw new InvalidOperationException("An empty AI update was accepted as extracted facts.");
        if (!StoryMemoryRecovery.HasExtractedFacts(new StoryMemoryUpdate
            { Characters = new List<StoryCharacterPatch> { new() { Name = "New character" } } }))
            throw new InvalidOperationException("Character extraction was not recognized.");
    }
}
