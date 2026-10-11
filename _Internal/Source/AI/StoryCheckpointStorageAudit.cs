using System.IO;
using System.Text.Json;

namespace WorkChatMini;

// Read-only metadata inventory. It never deletes files, rewrites manifests or claims that
// an unreferenced object is safe to remove: old, incomplete and inactive states matter.
internal sealed class StoryCheckpointStorageAudit
{
    private readonly StoryProjectStore _projects = new();

    public StoryStorageReport Analyze(string projectId, CancellationToken token = default)
    {
        string project = _projects.GetProjectDirectory(projectId);
        string root = Path.Combine(project, "branches");
        var report = new StoryStorageReport();
        if (!Directory.Exists(root)) return report;

        string deferred = Path.Combine(root, "pending-rescues");
        report.HasPendingOperation = File.Exists(Path.Combine(root, "pending-commit.json")) ||
            File.Exists(Path.Combine(root, "pending-restore.json")) ||
            (Directory.Exists(deferred) && Directory.EnumerateFiles(deferred, "*.json").Any());
        var manifests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var references = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        string checkpoints = Path.Combine(root, "checkpoints");
        if (Directory.Exists(checkpoints))
        {
            foreach (string directory in Directory.EnumerateDirectories(checkpoints))
            {
                token.ThrowIfCancellationRequested();
                string id = Path.GetFileName(directory);
                report.CheckpointBytes += DirectorySize(directory, token);
                string path = Path.Combine(directory, "checkpoint.json");
                try
                {
                    StoryCheckpoint? manifest = File.Exists(path)
                        ? JsonSerializer.Deserialize<StoryCheckpoint>(File.ReadAllText(path)) : null;
                    if (manifest is null || !IsId(id) || !string.Equals(manifest.Id, id,
                        StringComparison.OrdinalIgnoreCase) || manifest.Chapters is null ||
                        manifest.ChapterCount != manifest.Chapters.Count ||
                        manifest.Chapters.Any(c => !IsChapterName(c.FileName) || !IsHash(c.Sha256)) ||
                        manifest.Chapters.Select(c => c.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count()
                            != manifest.Chapters.Count)
                        throw new InvalidDataException("Incomplete checkpoint manifest");
                    // Even a damaged snapshot may still own immutable objects; preserve its references.
                    foreach (StoryCheckpointFile chapter in manifest.Chapters)
                        references.Add(chapter.Sha256);
                    if (manifest.SnapshotFormat is not (1 or 2))
                        throw new InvalidDataException("Unsupported snapshot format");
                    string memoryFile = manifest.SnapshotFormat == 2 ? "memory.json.gz" : "memory.json";
                    string dialogueFile = manifest.SnapshotFormat == 2 ? "conversation.json.gz" : "conversation.json";
                    if (!File.Exists(Path.Combine(directory, memoryFile)) ||
                        !File.Exists(Path.Combine(directory, dialogueFile)))
                        throw new InvalidDataException("Checkpoint payload missing");
                    manifests.Add(id);
                    report.CompleteCheckpoints++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
                {
                    report.IncompleteCheckpoints++;
                }
            }
        }

        string indexPath = Path.Combine(root, "branch-index.json");
        if (File.Exists(indexPath))
        {
            try
            {
                StoryBranchIndex? index = new StoryBranchStore().Load(projectId);
                if (index is null) throw new InvalidDataException("Missing branch index");
                report.ActiveBranches = index.Branches.Count;
                report.ArchivedBranches = index.ArchivedBranches.Count;
                report.HasPendingOperation |= index.PendingSwitch is not null;
                report.UnresolvedBranchHeads = index.Branches.Concat(index.ArchivedBranches)
                    .Count(b => !manifests.Contains(b.HeadCheckpointId));
                // A pending switch may refer to a source/destination not yet materialized.
                if (index.PendingSwitch is { } pending &&
                    (!manifests.Contains(pending.SourceCheckpointId) ||
                     !manifests.Contains(pending.TargetCheckpointId)))
                    report.UnresolvedBranchHeads++;
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException)
            {
                report.InvalidBranchIndex = true;
            }
        }

        string objects = Path.Combine(root, "objects");
        if (Directory.Exists(objects))
        {
            foreach (string path in Directory.EnumerateFiles(objects))
            {
                token.ThrowIfCancellationRequested();
                if (!string.Equals(Path.GetExtension(path), ".md", StringComparison.OrdinalIgnoreCase) ||
                    !IsHash(Path.GetFileNameWithoutExtension(path)))
                {
                    report.UnrecognizedObjectFiles++;
                    continue;
                }
                long length = new FileInfo(path).Length;
                report.ObjectCount++;
                report.ObjectBytes += length;
                if (references.Contains(Path.GetFileNameWithoutExtension(path)))
                {
                    report.ReferencedObjects++;
                }
                else
                {
                    report.PossiblyUnreferencedObjects++;
                    report.PossiblyUnreferencedBytes += length;
                }
            }
        }
        report.MissingReferencedObjects = references.Count - report.ReferencedObjects;
        // A suspicious/incomplete checkpoint could contain references we cannot read.
        // Therefore an 'unused object' estimate must not be shown as reliable.
        report.ReferenceScanComplete = report.IncompleteCheckpoints == 0 &&
            report.UnresolvedBranchHeads == 0 && report.MissingReferencedObjects == 0 &&
            !report.InvalidBranchIndex && !report.HasPendingOperation;

        foreach (string directoryName in new[] { "staging", "history" })
        {
            string directory = Path.Combine(root, directoryName);
            if (!Directory.Exists(directory)) continue;
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                token.ThrowIfCancellationRequested();
                report.TemporaryEntries++;
                report.TemporaryBytes += Directory.Exists(entry)
                    ? DirectorySize(entry, token) : new FileInfo(entry).Length;
            }
        }
        foreach (string directory in new[] { root, objects, checkpoints })
        {
            if (!Directory.Exists(directory)) continue;
            foreach (string path in Directory.EnumerateFiles(directory, "*.tmp"))
            {
                token.ThrowIfCancellationRequested();
                report.TemporaryEntries++;
                report.TemporaryBytes += new FileInfo(path).Length;
            }
        }
        foreach (string path in new[] {
            Path.Combine(project, "conversation.json.restore"),
            Path.Combine(project, "memory", "story-memory.json.restore") })
        {
            if (!File.Exists(path)) continue;
            report.TemporaryEntries++;
            report.TemporaryBytes += new FileInfo(path).Length;
        }
        // Rescued chapter files are intentionally retained and must not be shown
        // as disposable temporary leftovers or eligible for cleanup.
        string rescued = Path.Combine(root, "recovered");
        if (Directory.Exists(rescued))
        {
            report.RescueGroups = Directory.EnumerateDirectories(rescued).Count();
            report.RescueBytes = DirectorySize(rescued, token);
        }
        string live = Path.Combine(project, "chapters");
        if (Directory.Exists(live)) report.LiveChapterBytes = DirectorySize(live, token);
        return report;
    }

    private static long DirectorySize(string directory, CancellationToken token)
    {
        long bytes = 0;
        var pending = new Stack<string>();
        pending.Push(directory);
        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            string current = pending.Pop();
            foreach (string path in Directory.EnumerateFiles(current))
            {
                token.ThrowIfCancellationRequested();
                bytes += new FileInfo(path).Length;
            }
            foreach (string child in Directory.EnumerateDirectories(current))
            {
                // Never traverse directory junctions/reparse points while auditing.
                if ((File.GetAttributes(child) & FileAttributes.ReparsePoint) == 0)
                    pending.Push(child);
            }
        }
        return bytes;
    }

    private static bool IsId(string? text) => text is { Length: 32 } && text.All(Uri.IsHexDigit);
    private static bool IsHash(string? text) => text is { Length: 64 } && text.All(Uri.IsHexDigit);
    private static bool IsChapterName(string? text) => !string.IsNullOrWhiteSpace(text) &&
        Path.GetFileName(text) == text && text.EndsWith(".md", StringComparison.OrdinalIgnoreCase);
}

internal sealed class StoryStorageReport
{
    public int CompleteCheckpoints { get; set; }
    public int IncompleteCheckpoints { get; set; }
    public int ActiveBranches { get; set; }
    public int ArchivedBranches { get; set; }
    public int UnresolvedBranchHeads { get; set; }
    public bool InvalidBranchIndex { get; set; }
    public bool HasPendingOperation { get; set; }
    public bool ReferenceScanComplete { get; set; }
    public int ObjectCount { get; set; }
    public int ReferencedObjects { get; set; }
    public int MissingReferencedObjects { get; set; }
    public int PossiblyUnreferencedObjects { get; set; }
    public int UnrecognizedObjectFiles { get; set; }
    public int TemporaryEntries { get; set; }
    public long CheckpointBytes { get; set; }
    public long ObjectBytes { get; set; }
    public long PossiblyUnreferencedBytes { get; set; }
    public long TemporaryBytes { get; set; }
    public long LiveChapterBytes { get; set; }
    public int RescueGroups { get; set; }
    public long RescueBytes { get; set; }
}
