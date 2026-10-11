using System.IO;
using System.Security.Cryptography;

namespace WorkChatMini;

// A checkpoint captures only chapter files referenced by Story Memory. Never erase
// other files (or external edits made after a checkpoint) when retiring the old
// live chapters after rollback, branch navigation, or an interrupted commit.
internal static class StoryRestoreRescue
{
    internal static async Task<int> PreserveUntrackedAsync(string archivedChapters,
        string rescueDirectory, IReadOnlyList<StoryCheckpointFile> sourceSnapshot)
    {
        if (!Directory.Exists(archivedChapters)) return 0;
        var expected = sourceSnapshot.ToDictionary(item => item.FileName, item => item.Sha256,
            StringComparer.OrdinalIgnoreCase);
        int preserved = 0;
        var pending = new Stack<string>();
        pending.Push(archivedChapters);
        while (pending.Count != 0)
        {
            string directory = pending.Pop();
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    // Never walk (or delete after following) a junction into other folders.
                    if ((attributes & FileAttributes.ReparsePoint) != 0)
                        throw new IOException("Linked chapter directory needs manual recovery; original archive preserved.");
                    pending.Push(entry);
                    continue;
                }
                string relative = Path.GetRelativePath(archivedChapters, entry);
                bool originalMatch = false;
                if (!relative.Contains(Path.DirectorySeparatorChar) &&
                    (attributes & FileAttributes.ReparsePoint) == 0 &&
                    expected.TryGetValue(relative, out string? originalHash))
                {
                    await using var file = new FileStream(entry, FileMode.Open, FileAccess.Read,
                        FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
                    string actualHash = Convert.ToHexString(await SHA256.HashDataAsync(file));
                    originalMatch = actualHash.Equals(originalHash, StringComparison.OrdinalIgnoreCase);
                }
                if (originalMatch) continue;

                // Moving into a non-live rescue folder is lossless even if the process
                // terminates before cleanup. Never overwrite an earlier rescued file.
                string destination = Path.Combine(rescueDirectory, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
                File.Move(entry, destination);
                preserved++;
            }
        }
        return preserved;
    }
}
