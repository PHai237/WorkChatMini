using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace WorkChatMini;

// The ZIP can be byte-for-byte valid yet contain a branch pointer to a missing
// or logically inconsistent checkpoint. Check the restore-critical branch heads
// BEFORE publishing a new Story directory. Imported data is never activated here.
internal static class StoryPortableImportGuard
{
    internal static async Task ValidateBranchHeadsAsync(string storyDirectory,
        StoryBranchIndex index, CancellationToken token = default)
    {
        StoryBranchStore.ValidateImportedIndex(index);
        if (index.PendingSwitch is not null)
            throw new InvalidDataException("Cannot import a backup with an unfinished branch switch.");
        string branches = Path.Combine(storyDirectory, "branches");
        var validatedHeads = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var verifiedObjects = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (StoryBranchInfo branch in index.Branches.Concat(index.ArchivedBranches))
        {
            token.ThrowIfCancellationRequested();
            string id = branch.HeadCheckpointId;
            if (!validatedHeads.Add(id)) continue;
            if (id is not { Length: 32 } || !id.All(Uri.IsHexDigit))
                throw new InvalidDataException("Branch references an unsafe checkpoint identifier.");
            string folder = Path.Combine(branches, "checkpoints", id);
            string manifestPath = Path.Combine(folder, "checkpoint.json");
            if (!File.Exists(manifestPath))
                throw new InvalidDataException("Backup branch references a missing checkpoint.");
            if ((File.GetAttributes(folder) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Backup branch checkpoint directory is linked.");
            StoryCheckpoint checkpoint = JsonSerializer.Deserialize<StoryCheckpoint>(
                await File.ReadAllTextAsync(manifestPath, token))
                ?? throw new InvalidDataException("Branch checkpoint manifest is unreadable.");
            if (!string.Equals(checkpoint.Id, id, StringComparison.Ordinal))
                throw new InvalidDataException("Branch checkpoint manifest has a different identifier.");
            (string memoryJson, string conversationJson) = await StoryCheckpointSnapshots.ReadAsync(
                folder, checkpoint, token);
            StoryMemoryDocument memory = StoryMemoryStore.ParseMemoryDocument(memoryJson);
            _ = StoryProjectStore.ParseConversationMessages(conversationJson);
            StoryCheckpointChapterGuard.ValidateManifest(memory, checkpoint);
            foreach (StoryCheckpointFile chapter in checkpoint.Chapters)
            {
                token.ThrowIfCancellationRequested();
                if (!verifiedObjects.Add(chapter.Sha256)) continue;
                string objectPath = Path.Combine(branches, "objects", chapter.Sha256 + ".md");
                if (!File.Exists(objectPath))
                    throw new InvalidDataException("Branch checkpoint references a missing chapter object.");
                if ((File.GetAttributes(objectPath) & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Backup branch chapter object is linked.");
                await using var stream = new FileStream(objectPath, FileMode.Open, FileAccess.Read,
                    FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
                string digest = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
                if (!digest.Equals(chapter.Sha256, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Branch checkpoint chapter object is corrupted.");
            }
        }
    }
}
