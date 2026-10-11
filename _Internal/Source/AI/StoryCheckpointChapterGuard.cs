using System.IO;

namespace WorkChatMini;

// A checkpoint must describe EXACTLY the chapter set present in Memory. A
// count-only check is insufficient: [A,A] can pass a membership check against
// [A,B] and make restore silently omit B. This is read-only and allocation bounded.
internal static class StoryCheckpointChapterGuard
{
    internal static void ValidateLiveChapters(StoryMemoryDocument memory)
    {
        if (memory.Chapters is null)
            throw new InvalidDataException("Story chapter list is missing.");
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var numbers = new HashSet<int>();
        foreach (StoryChapterMemory chapter in memory.Chapters)
        {
            if (chapter is null || chapter.Number <= 0 || !numbers.Add(chapter.Number))
                throw new InvalidDataException("Story has invalid or duplicate chapter numbers.");
            StoryCheckpointStore.ValidateFileName(chapter.FileName);
            if (!names.Add(chapter.FileName))
                throw new InvalidDataException("Story has duplicate chapter filenames.");
        }
    }

    internal static void ValidateManifest(StoryMemoryDocument memory, StoryCheckpoint manifest)
    {
        ValidateLiveChapters(memory);
        if (manifest.Chapters is null || manifest.ChapterCount != memory.Chapters.Count ||
            manifest.Chapters.Count != memory.Chapters.Count)
            throw new InvalidDataException("Checkpoint chapter count differs from Story Memory.");
        var expected = new HashSet<string>(memory.Chapters.Select(c => c.FileName),
            StringComparer.OrdinalIgnoreCase);
        foreach (StoryCheckpointFile chapter in manifest.Chapters)
        {
            if (chapter is null)
                throw new InvalidDataException("Checkpoint contains an empty chapter reference.");
            StoryCheckpointStore.ValidateFileName(chapter.FileName);
            if (chapter.Sha256 is not { Length: 64 } || !chapter.Sha256.All(Uri.IsHexDigit) ||
                !expected.Remove(chapter.FileName))
                throw new InvalidDataException("Checkpoint chapter reference is missing, duplicated, or corrupt.");
        }
        if (expected.Count != 0)
            throw new InvalidDataException("Checkpoint omitted referenced chapter files.");
    }
}
