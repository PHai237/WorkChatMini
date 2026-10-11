using System.IO;
using System.Text;
using System.Text.Json;

namespace WorkChatMini;

// Folded into the existing Story memory case; do not increase the 22-test contract.
internal static class StoryCheckpointStorageSelfTest
{
    public static async Task VerifyAsync(string projectId, StoryCheckpointStore checkpoints,
        StoryMemoryStore memories, StoryProjectStore projects)
    {
        StoryMemoryDocument memory = await memories.LoadAsync(projectId);
        IReadOnlyList<StoryMessage> messages = await projects.LoadMessagesAsync(projectId);
        var longConversation = messages.Concat(new[]
        {
            new StoryMessage { Role = "user", Content = string.Concat(
                Enumerable.Repeat("Long-story snapshot repetition for compression. ", 4000)) }
        }).ToList();
        string id = await checkpoints.CaptureAsync(projectId, memory, longConversation,
            "Self-test compressed snapshot and legacy compatibility");
        string directory = Path.Combine(projects.GetProjectDirectory(projectId), "branches", "checkpoints", id);
        var manifest = JsonSerializer.Deserialize<StoryCheckpoint>(
            await File.ReadAllTextAsync(Path.Combine(directory, "checkpoint.json")))
            ?? throw new InvalidDataException("Compressed test manifest missing.");
        string conversationPath = Path.Combine(directory, "conversation.json.gz");
        string memoryPath = Path.Combine(directory, "memory.json.gz");
        if (manifest.SnapshotFormat != 2 || !File.Exists(memoryPath) ||
            !File.Exists(conversationPath) || File.Exists(Path.Combine(directory, "memory.json")) ||
            new FileInfo(conversationPath).Length >= Encoding.UTF8.GetByteCount(
                JsonSerializer.Serialize(longConversation)))
            throw new InvalidOperationException("Story snapshots were not stored in compressed format.");
        await checkpoints.VerifyAsync(projectId, id);
        StoryMemoryDocument loaded = await checkpoints.ReadMemorySnapshotAsync(projectId, id);
        if (loaded.Chapters.Count != memory.Chapters.Count || loaded.Canon.Count != memory.Canon.Count)
            throw new InvalidOperationException("Compressed checkpoint lost structured memory.");

        // Corrupt only the disposable self-test checkpoint, never a live story file.
        byte[] good = await File.ReadAllBytesAsync(memoryPath);
        try
        {
            await File.WriteAllBytesAsync(memoryPath, new byte[] { 0, 1, 2, 3 });
            bool rejected = false;
            try { await checkpoints.VerifyAsync(projectId, id); }
            catch (InvalidDataException) { rejected = true; }
            catch (IOException) { rejected = true; }
            if (!rejected)
                throw new InvalidOperationException("Corrupt compressed checkpoint was accepted.");
        }
        finally { await File.WriteAllBytesAsync(memoryPath, good); }
        await checkpoints.VerifyAsync(projectId, id);

        // Simulate a pre-preview6 checkpoint: no snapshot-format property, plaintext
        // memory/conversation JSON, but the same validated chapter object manifest.
        string oldId = Guid.NewGuid().ToString("N");
        string oldDir = Path.Combine(projects.GetProjectDirectory(projectId), "branches", "checkpoints", oldId);
        Directory.CreateDirectory(oldDir);
        // Use an anonymous object to *omit* all newer properties exactly as old
        // checkpoint.json files do, not merely set SnapshotFormat to 1.
        await File.WriteAllTextAsync(Path.Combine(oldDir, "checkpoint.json"),
            JsonSerializer.Serialize(new
            {
                Id = oldId, Reason = "Legacy checkpoint compatibility fixture",
                CreatedUtc = DateTimeOffset.UtcNow,
                ChapterCount = manifest.ChapterCount, Chapters = manifest.Chapters
            }));
        await File.WriteAllTextAsync(Path.Combine(oldDir, "memory.json"),
            JsonSerializer.Serialize(memory));
        await File.WriteAllTextAsync(Path.Combine(oldDir, "conversation.json"),
            JsonSerializer.Serialize(messages));
        await checkpoints.VerifyAsync(projectId, oldId);
        if ((await checkpoints.ReadMemorySnapshotAsync(projectId, oldId)).Chapters.Count != memory.Chapters.Count)
            throw new InvalidOperationException("Legacy plaintext checkpoint could not be loaded.");

        // Portable backup captures the actual checkpoint object graph and must
        // reject post-export tampering. Runs only on the disposable self-test Story.
        await StoryBackupSelfTest.VerifyAsync(projectId);
    }
}
