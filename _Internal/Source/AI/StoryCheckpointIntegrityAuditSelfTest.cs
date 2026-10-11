using System.IO;
using System.Text.Json;

namespace WorkChatMini;

// Uses only the disposable story from the existing Story memory self-test.
internal static class StoryCheckpointIntegrityAuditSelfTest
{
    public static async Task VerifyAsync(string projectId, StoryProjectStore projects)
    {
        var audit = new StoryCheckpointIntegrityAudit();
        StoryIntegrityReport clean = await audit.VerifyAsync(projectId);
        if (!clean.Verified || clean.CheckpointsChecked < 2 || clean.ObjectsChecked == 0)
            throw new InvalidOperationException("Deep integrity audit failed on a valid story fixture.");
        string root = Path.Combine(projects.GetProjectDirectory(projectId), "branches");
        string checkpointDir = Path.Combine(root, "checkpoints");
        string? compressedDirectory = null;
        StoryCheckpoint? manifest = null;
        foreach (string candidate in Directory.EnumerateDirectories(checkpointDir))
        {
            if (!File.Exists(Path.Combine(candidate, "memory.json.gz"))) continue;
            StoryCheckpoint? loaded = JsonSerializer.Deserialize<StoryCheckpoint>(
                await File.ReadAllTextAsync(Path.Combine(candidate, "checkpoint.json")));
            if (loaded?.Chapters.Count is not > 0) continue;
            compressedDirectory = candidate;
            manifest = loaded;
            break;
        }
        if (compressedDirectory is null || manifest is null)
            throw new InvalidOperationException("Deep audit test needs a compressed chapter fixture.");
        string memoryFile = Path.Combine(compressedDirectory, "memory.json.gz");
        byte[] memoryOriginal = await File.ReadAllBytesAsync(memoryFile);
        try
        {
            await File.WriteAllBytesAsync(memoryFile, new byte[] { 0, 1, 2, 3 });
            StoryIntegrityReport damaged = await audit.VerifyAsync(projectId);
            if (damaged.Verified || damaged.CheckpointsDamaged == 0)
                throw new InvalidOperationException("Deep audit accepted a corrupt compressed snapshot.");
        }
        finally { await File.WriteAllBytesAsync(memoryFile, memoryOriginal); }

        StoryCheckpointFile? chapter = manifest.Chapters.FirstOrDefault();
        if (chapter is null)
            throw new InvalidOperationException("Deep audit fixture has no chapter object.");
        string objectPath = Path.Combine(root, "objects", chapter.Sha256 + ".md");
        byte[] objectOriginal = await File.ReadAllBytesAsync(objectPath);
        try
        {
            await File.WriteAllTextAsync(objectPath, "Deliberately damaged chapter object in disposable fixture.");
            StoryIntegrityReport damaged = await audit.VerifyAsync(projectId);
            if (damaged.Verified || damaged.ObjectsDamaged == 0 || damaged.CheckpointsDamaged == 0)
                throw new InvalidOperationException("Deep audit accepted a corrupted chapter object.");
        }
        finally { await File.WriteAllBytesAsync(objectPath, objectOriginal); }
        if (!(await audit.VerifyAsync(projectId)).Verified)
            throw new InvalidOperationException("Deep audit did not recover after restoring test-only files.");
    }
}
