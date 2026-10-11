using System.IO;

namespace WorkChatMini;

internal static class StoryTransactionGuardSelfTest
{
    internal static void Verify()
    {
        var before = new StoryMemoryDocument();
        before.Chapters.Add(new StoryChapterMemory { Number = 1, FileName = "0001-Start.md" });
        before.Chapters.Add(new StoryChapterMemory { Number = 2, FileName = "0002-Next.md" });
        var manifest = new StoryCheckpoint
        {
            ChapterCount = 2,
            Chapters = new List<StoryCheckpointFile>
            {
                new() { FileName = "0001-Start.md", Sha256 = new string('A', 64) },
                new() { FileName = "0002-Next.md", Sha256 = new string('B', 64) }
            }
        };
        StoryCheckpointChapterGuard.ValidateManifest(before, manifest);
        // Previously an [A,A] manifest passed membership checks against [A,B].
        manifest.Chapters[1].FileName = "0001-Start.md";
        ExpectInvalid(() => StoryCheckpointChapterGuard.ValidateManifest(before, manifest),
            "Duplicate checkpoint entry was accepted.");
        manifest.Chapters[1].FileName = "0002-Next.md";
        var repeated = new StoryMemoryDocument();
        repeated.Chapters.Add(new StoryChapterMemory { Number = 1, FileName = "0001-Start.md" });
        repeated.Chapters.Add(new StoryChapterMemory { Number = 1, FileName = "0002-Next.md" });
        ExpectInvalid(() => StoryCheckpointChapterGuard.ValidateManifest(repeated, manifest),
            "Duplicate chapter number was accepted.");
        manifest.Chapters[1].FileName = "../escape.md";
        ExpectInvalid(() => StoryCheckpointChapterGuard.ValidateManifest(before, manifest),
            "Unsafe manifest chapter filename was accepted.");

        var after = new StoryMemoryDocument();
        after.Chapters.Add(new StoryChapterMemory { Number = 1, FileName = "0001-Start.md" });
        after.Chapters.Add(new StoryChapterMemory { Number = 2, FileName = "0002-Next.md" });
        after.Chapters.Add(new StoryChapterMemory { Number = 3, FileName = "0003-New.md" });
        var committed = new StoryMessage
        {
            Role = "assistant", IsNarrativeDraft = true, ManuscriptCommitted = true,
            ManuscriptChapterNumber = 3, Content = "Durable new prose"
        };
        if (StoryChapterCommitGuard.Validate(before, after, new[] { committed }).Number != 3)
            throw new InvalidOperationException("Valid chapter commit guard rejected a committed draft.");
        ExpectInvalid(() => StoryChapterCommitGuard.Validate(before, after, Array.Empty<StoryMessage>()),
            "Stale but parseable conversation passed commit verification.");
        committed.ManuscriptCommitted = false;
        ExpectInvalid(() => StoryChapterCommitGuard.Validate(before, after, new[] { committed }),
            "Uncommitted draft acknowledged a chapter.");
        committed.ManuscriptCommitted = true;
        after.Chapters[0].FileName = "0001-Replaced.md";
        ExpectInvalid(() => StoryChapterCommitGuard.Validate(before, after, new[] { committed }),
            "Existing chapter reference was replaced during commit.");
    }

    private static void ExpectInvalid(Action action, string message)
    {
        try { action(); }
        catch (InvalidDataException) { return; }
        throw new InvalidOperationException(message);
    }
}
