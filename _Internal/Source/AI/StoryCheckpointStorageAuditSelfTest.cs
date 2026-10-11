using System.IO;

namespace WorkChatMini;

// Included inside the existing Story memory test; all fixtures belong to its
// disposable project, never a user's actual story.
internal static class StoryCheckpointStorageAuditSelfTest
{
    public static async Task VerifyAsync(string projectId, StoryProjectStore projects)
    {
        string root = Path.Combine(projects.GetProjectDirectory(projectId), "branches");
        var audit = new StoryCheckpointStorageAudit();
        StoryStorageReport baseline = audit.Analyze(projectId);
        if (baseline.CompleteCheckpoints < 2 || baseline.MissingReferencedObjects != 0 ||
            !baseline.ReferenceScanComplete)
            throw new InvalidOperationException("Storage metadata audit rejected valid checkpoint fixtures.");

        string hash = Guid.NewGuid().ToString("N") + Guid.NewGuid().ToString("N");
        string objectPath = Path.Combine(root, "objects", hash + ".md");
        string incompletePath = Path.Combine(root, "checkpoints", Guid.NewGuid().ToString("N"));
        string temporaryPath = Path.Combine(root, "staging", "audit-fixture-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(objectPath)!);
            await File.WriteAllTextAsync(objectPath, "Disposable unreferenced audit fixture.");
            StoryStorageReport candidate = audit.Analyze(projectId);
            if (!candidate.ReferenceScanComplete ||
                candidate.PossiblyUnreferencedObjects <= baseline.PossiblyUnreferencedObjects ||
                !File.Exists(objectPath))
                throw new InvalidOperationException("Read-only storage audit failed to identify an unreferenced fixture.");

            Directory.CreateDirectory(incompletePath);
            Directory.CreateDirectory(temporaryPath);
            await File.WriteAllTextAsync(Path.Combine(temporaryPath, "partial.tmp"), "Interrupted restore fixture");
            StoryStorageReport guarded = audit.Analyze(projectId);
            if (guarded.ReferenceScanComplete ||
                guarded.IncompleteCheckpoints <= baseline.IncompleteCheckpoints ||
                guarded.TemporaryEntries <= baseline.TemporaryEntries ||
                !File.Exists(objectPath) || !Directory.Exists(incompletePath))
                throw new InvalidOperationException("Storage inventory missed incomplete state or modified a file.");
        }
        finally
        {
            if (File.Exists(objectPath)) File.Delete(objectPath);
            if (Directory.Exists(incompletePath)) Directory.Delete(incompletePath, recursive: true);
            if (Directory.Exists(temporaryPath)) Directory.Delete(temporaryPath, recursive: true);
        }
        if (!audit.Analyze(projectId).ReferenceScanComplete)
            throw new InvalidOperationException("Storage audit did not recover after removing disposable fixtures.");
    }
}
