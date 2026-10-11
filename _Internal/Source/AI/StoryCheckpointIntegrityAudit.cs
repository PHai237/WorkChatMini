using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace WorkChatMini;

// Opt-in deep verification, independent of restore. Read-only, and each immutable
// chapter object is hashed at most once per scan even if hundreds of checkpoints use it.
internal sealed class StoryCheckpointIntegrityAudit
{
    private readonly StoryProjectStore _projects = new();

    public async Task<StoryIntegrityReport> VerifyAsync(string projectId, CancellationToken token = default)
    {
        string root = Path.Combine(_projects.GetProjectDirectory(projectId), "branches");
        var result = new StoryIntegrityReport();
        if (!Directory.Exists(root)) return result;
        var verifiedObjects = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        var validCheckpoints = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        result.PendingOperation = File.Exists(Path.Combine(root, "pending-commit.json")) ||
            File.Exists(Path.Combine(root, "pending-restore.json"));
        string checkpointDirectory = Path.Combine(root, "checkpoints");
        if (Directory.Exists(checkpointDirectory))
        {
            foreach (string directory in Directory.EnumerateDirectories(checkpointDirectory))
            {
                token.ThrowIfCancellationRequested();
                result.CheckpointsChecked++;
                string id = Path.GetFileName(directory);
                try
                {
                    if (!IsHex(id, 32) || (File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("Invalid checkpoint directory");
                    StoryCheckpoint? checkpoint = JsonSerializer.Deserialize<StoryCheckpoint>(
                        await File.ReadAllTextAsync(Path.Combine(directory, "checkpoint.json"), token));
                    if (checkpoint is null || !string.Equals(checkpoint.Id, id, StringComparison.Ordinal) ||
                        checkpoint.Chapters is null || checkpoint.ChapterCount != checkpoint.Chapters.Count)
                        throw new InvalidDataException("Invalid checkpoint manifest");
                    (string memoryJson, string conversationJson) =
                        await StoryCheckpointSnapshots.ReadAsync(directory, checkpoint, token);
                    StoryMemoryDocument? memory = JsonSerializer.Deserialize<StoryMemoryDocument>(memoryJson);
                    _ = StoryProjectStore.ParseConversationMessages(conversationJson);
                    if (memory is null || memory.Chapters is null ||
                        memory.Chapters.Count != checkpoint.Chapters.Count)
                        throw new InvalidDataException("Checkpoint payload does not match manifest");
                    StoryCheckpointChapterGuard.ValidateManifest(memory, checkpoint);
                    foreach (StoryCheckpointFile chapter in checkpoint.Chapters)
                    {
                        token.ThrowIfCancellationRequested();
                        if (!verifiedObjects.TryGetValue(chapter.Sha256, out bool valid))
                        {
                            string blob = Path.Combine(root, "objects", chapter.Sha256 + ".md");
                            valid = await VerifyObjectAsync(blob, chapter.Sha256, token);
                            verifiedObjects[chapter.Sha256] = valid;
                        }
                        if (!valid) throw new InvalidDataException("Missing or corrupted chapter object");
                    }
                    validCheckpoints.Add(id);
                    result.CheckpointsValid++;
                    if (checkpoint.SnapshotFormat == 1) result.LegacyCheckpoints++;
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                    InvalidDataException or JsonException or ArgumentException)
                {
                    result.CheckpointsDamaged++;
                    result.RecordIssue($"Checkpoint {id}: {Describe(ex)}");
                }
            }
        }
        result.ObjectsChecked = verifiedObjects.Count;
        result.ObjectsDamaged = verifiedObjects.Values.Count(valid => !valid);
        string indexPath = Path.Combine(root, "branch-index.json");
        if (File.Exists(indexPath))
        {
            try
            {
                StoryBranchIndex? index = new StoryBranchStore().Load(projectId);
                if (index is null) throw new InvalidDataException("Missing branch index");
                result.PendingOperation |= index.PendingSwitch is not null;
                foreach (StoryBranchInfo branch in index.Branches.Concat(index.ArchivedBranches))
                {
                    if (!validCheckpoints.Contains(branch.HeadCheckpointId))
                    {
                        result.UnrecoverableBranchHeads++;
                        result.RecordIssue($"Branch {branch.Name}: head checkpoint could not be verified");
                    }
                }
                if (index.PendingSwitch is { } pending &&
                    (!validCheckpoints.Contains(pending.SourceCheckpointId) ||
                     !validCheckpoints.Contains(pending.TargetCheckpointId)))
                    result.RecordIssue("Pending branch switch refers to an unverified checkpoint");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or
                InvalidDataException or JsonException or ArgumentException)
            {
                result.InvalidBranchIndex = true;
                result.RecordIssue($"Branch index: {Describe(ex)}");
            }
        }
        if (result.PendingOperation)
            result.RecordIssue("A story transaction is pending; retry after recovery");
        return result;
    }

    private static async Task<bool> VerifyObjectAsync(string path, string expected, CancellationToken token)
    {
        try
        {
            await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
                FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
            string actual = Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
            return string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private static bool IsHex(string? value, int count) =>
        value is not null && value.Length == count && value.All(Uri.IsHexDigit);

    private static bool ValidChapterName(string? name) =>
        !string.IsNullOrWhiteSpace(name) && Path.GetFileName(name) == name &&
        name.EndsWith(".md", StringComparison.OrdinalIgnoreCase) && name is not ("." or "..");

    private static string Describe(Exception exception) => exception switch
    {
        JsonException => "invalid JSON",
        UnauthorizedAccessException => "access denied",
        FileNotFoundException => "file missing",
        DirectoryNotFoundException => "directory missing",
        InvalidDataException => exception.Message,
        _ => "file cannot be read"
    };
}

internal sealed class StoryIntegrityReport
{
    private const int MaxVisibleIssues = 8;
    private readonly List<string> _issues = new();
    public int CheckpointsChecked { get; set; }
    public int CheckpointsValid { get; set; }
    public int CheckpointsDamaged { get; set; }
    public int LegacyCheckpoints { get; set; }
    public int ObjectsChecked { get; set; }
    public int ObjectsDamaged { get; set; }
    public int UnrecoverableBranchHeads { get; set; }
    public bool InvalidBranchIndex { get; set; }
    public bool PendingOperation { get; set; }
    public bool Verified => CheckpointsChecked > 0 && CheckpointsDamaged == 0 && ObjectsDamaged == 0 &&
        UnrecoverableBranchHeads == 0 && !InvalidBranchIndex && !PendingOperation;
    public IReadOnlyList<string> Issues => _issues;
    public int HiddenIssues { get; private set; }
    public void RecordIssue(string issue)
    {
        if (_issues.Count < MaxVisibleIssues) _issues.Add(issue);
        else HiddenIssues++;
    }
}
