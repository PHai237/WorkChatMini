using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace WorkChatMini;

// Not persisted, and never used to generate new canon. Detects files modified
// while a provider is extracting a long backlog in the background.
internal static class StoryChapterEvidenceFingerprint
{
    internal static string ForProse(string prose)
    {
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(prose.Trim())));
    }

    internal static async Task EnsureUnchangedAsync(
        StoryMemoryStore store, string projectId,
        IReadOnlyList<(StoryChapterMemory Chapter, string Fingerprint)> examined,
        CancellationToken token)
    {
        foreach ((StoryChapterMemory chapter, string fingerprint) in examined)
        {
            token.ThrowIfCancellationRequested();
            string currentProse = await StoryMemoryRecovery.ReadCommittedChapterAsync(
                store, projectId, chapter, token).ConfigureAwait(false);
            if (!string.Equals(ForProse(currentProse), fingerprint, StringComparison.Ordinal))
                throw new InvalidDataException($"Chapter {chapter.Number} changed during Memory extraction; no Memory was saved.");
        }
    }
}
