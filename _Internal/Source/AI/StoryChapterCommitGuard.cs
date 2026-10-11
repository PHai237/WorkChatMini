using System.IO;

namespace WorkChatMini;

// Pure pre-finalization check. A valid conversation.json is not proof that the
// new chapter was acknowledged; verify its matching committed draft as well.
internal static class StoryChapterCommitGuard
{
    internal static StoryChapterMemory Validate(StoryMemoryDocument before,
        StoryMemoryDocument after, IReadOnlyList<StoryMessage> persistedMessages)
    {
        StoryCheckpointChapterGuard.ValidateLiveChapters(before);
        StoryCheckpointChapterGuard.ValidateLiveChapters(after);
        if (after.Chapters.Count != before.Chapters.Count + 1)
            throw new InvalidDataException("Chapter commit must add exactly one durable new chapter.");
        var original = before.Chapters.ToDictionary(c => c.Number);
        foreach (StoryChapterMemory old in before.Chapters)
        {
            if (!after.Chapters.Any(now => now.Number == old.Number &&
                string.Equals(now.FileName, old.FileName, StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Chapter commit replaced an existing chapter reference.");
        }
        StoryChapterMemory[] added = after.Chapters.Where(c => !original.ContainsKey(c.Number)).ToArray();
        if (added.Length != 1)
            throw new InvalidDataException("Chapter commit did not create one new chapter reference.");
        StoryChapterMemory chapter = added[0];
        if (persistedMessages is null || !persistedMessages.Any(message =>
                message is not null && message.Role == "assistant" &&
                message.IsNarrativeDraft && message.ManuscriptCommitted &&
                !message.DraftDiscarded && message.ManuscriptChapterNumber == chapter.Number))
            throw new InvalidDataException("Chapter commit is missing its persisted conversation acknowledgement.");
        return chapter;
    }
}
