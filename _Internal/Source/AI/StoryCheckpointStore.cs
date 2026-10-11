using System.IO;
using System.Security.Cryptography;
using System.Text.Json;

namespace WorkChatMini;

// Content-addressed chapter objects avoid copying the entire manuscript on every checkpoint.
// A checkpoint contains a complete structured-memory and conversation state, but only hashes
// for immutable chapter prose. Checkpoints are local to one story and never become AI context.
public sealed class StoryCheckpointStore
{
    private readonly StoryProjectStore _projects = new();
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public async Task<string> CaptureAsync(string projectId, StoryMemoryDocument memory,
        IReadOnlyList<StoryMessage> messages, string reason, CancellationToken token = default)
    {
        // Refuse inconsistent Memory before writing any new checkpoint artifacts.
        StoryCheckpointChapterGuard.ValidateLiveChapters(memory);
        string root = Root(projectId);
        string objects = Path.Combine(root, "objects");
        string chapters = Path.Combine(_projects.GetProjectDirectory(projectId), "chapters");
        if (Directory.Exists(chapters) &&
            (File.GetAttributes(chapters) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Linked chapter directory cannot be checkpointed.");
        var entries = new List<StoryCheckpointFile>();
        Directory.CreateDirectory(objects);
        foreach (StoryChapterMemory chapter in memory.Chapters)
        {
            token.ThrowIfCancellationRequested();
            ValidateFileName(chapter.FileName);
            string path = Path.Combine(chapters, chapter.FileName);
            if (!File.Exists(path))
                throw new IOException($"Cannot checkpoint: chapter file missing: {chapter.FileName}");
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Linked chapter manuscript cannot be checkpointed.");
            // Stream chapters instead of materializing all prose in RAM. Hashing the live
            // file also catches edits made outside the application before a new checkpoint.
            string hash = await Sha256FileAsync(path, token);
            string objectPath = Path.Combine(objects, hash + ".md");
            if (!File.Exists(objectPath))
            {
                string temp = objectPath + "." + Guid.NewGuid().ToString("N") + ".tmp";
                try
                {
                    File.Copy(path, temp);
                    if (!string.Equals(await Sha256FileAsync(temp, token), hash, StringComparison.Ordinal))
                        throw new IOException("Chapter changed while checkpointing; retry after saving.");
                    File.Move(temp, objectPath, true);
                }
                finally { if (File.Exists(temp)) File.Delete(temp); }
            }
            else if (!string.Equals(await Sha256FileAsync(objectPath, token), hash, StringComparison.Ordinal))
            {
                throw new InvalidDataException("Chapter snapshot object hash mismatch; refusing unsafe commit.");
            }
            entries.Add(new StoryCheckpointFile { FileName = chapter.FileName, Sha256 = hash });
        }
        if (entries.Select(e => e.FileName).Distinct(StringComparer.OrdinalIgnoreCase).Count() != entries.Count)
            throw new InvalidDataException("Duplicate chapter filenames cannot be checkpointed.");

        string id = Guid.NewGuid().ToString("N");
        string directory = Path.Combine(root, "checkpoints", id);
        Directory.CreateDirectory(directory);
        var checkpoint = new StoryCheckpoint
        {
            Id = id,
            Reason = reason,
            ChapterCount = memory.Chapters.Count,
            Chapters = entries
        };
        // Manifest is written last: a partial checkpoint is never advertised as complete.
        await StoryCheckpointSnapshots.CaptureAsync(directory, checkpoint,
            JsonSerializer.Serialize(memory, JsonOptions), JsonSerializer.Serialize(messages, JsonOptions), token);
        await WriteAtomicAsync(Path.Combine(directory, "checkpoint.json"), JsonSerializer.Serialize(checkpoint, JsonOptions), token);
        AppLogger.Info("STORY-CHECKPOINT", $"Saved | project={projectId} | checkpoint={id} | chapters={entries.Count}");
        return id;
    }

    public IReadOnlyList<StoryCheckpoint> ListRecent(string projectId, int max = 8,
        bool excludeBranchNavigation = false, int skip = 0)
    {
        string directory = Path.Combine(Root(projectId), "checkpoints");
        if (!Directory.Exists(directory))
            return Array.Empty<StoryCheckpoint>();
        var result = new List<StoryCheckpoint>();
        foreach (string path in Directory.EnumerateFiles(directory, "checkpoint.json", SearchOption.AllDirectories))
        {
            try
            {
                StoryCheckpoint? checkpoint = JsonSerializer.Deserialize<StoryCheckpoint>(File.ReadAllText(path), JsonOptions);
                if (checkpoint is not null && IsId(checkpoint.Id) &&
                    string.Equals(Path.GetFileName(Path.GetDirectoryName(path)), checkpoint.Id, StringComparison.Ordinal))
                    result.Add(checkpoint);
            }
            catch (Exception ex)
            {
                AppLogger.Warn("STORY-CHECKPOINT", $"Incomplete checkpoint skipped | {ex.Message}");
            }
        }
        IEnumerable<StoryCheckpoint> visible = excludeBranchNavigation
            ? result.Where(c => !c.Reason.StartsWith("Branch start:", StringComparison.Ordinal) &&
                !c.Reason.StartsWith("Leave branch:", StringComparison.Ordinal) &&
                c.Reason != "First branch baseline")
            : result;
        return visible.OrderByDescending(c => c.CreatedUtc).ThenByDescending(c => c.Id)
            .Skip(Math.Max(0, skip)).Take(Math.Clamp(max, 1, 20)).ToList();
    }

    public async Task<string> RestoreAsync(string projectId, string checkpointId,
        StoryMemoryDocument currentMemory, IReadOnlyList<StoryMessage> currentMessages, CancellationToken token = default)
    {
        using var operation = await StoryOperationGate.EnterAsync(projectId, token);
        if (HasPendingTransaction(projectId) ||
            new StoryBranchStore().Load(projectId)?.PendingSwitch is not null)
            throw new InvalidOperationException("Story recovery must finish before another rollback.");
        // Validate target before changing anything. Preserve the current state as an explicit
        // alternate checkpoint, including prose written since the chosen checkpoint.
        await ValidateCheckpointAsync(projectId, checkpointId, token);
        string rescueId = await CaptureAsync(projectId, currentMemory, currentMessages,
            "Automatic safety snapshot before rollback", token);
        var pending = new StoryPendingRestore
        {
            TargetCheckpointId = checkpointId,
            SafetyCheckpointId = rescueId,
            TransactionId = Guid.NewGuid().ToString("N")
        };
        await WriteAtomicAsync(Journal(projectId), JsonSerializer.Serialize(pending, JsonOptions), token);
        // No cancellation once a journal exists: complete or recover at the next launch.
        await ApplyPendingRestoreAsync(projectId);
        return rescueId;
    }

    // Called inside StoryBranchStore's project-wide operation lease (do not reenter
    // the same gate here). Branch switching already preserved the source branch as a checkpoint. Do not
    // create a second, possibly inconsistent, rescue snapshot of live files.
    public async Task RestoreForBranchSwitchAsync(string projectId, string checkpointId,
        string sourceCheckpointId, CancellationToken token = default)
    {
        if (HasPendingTransaction(projectId))
            throw new InvalidOperationException("Another story recovery transaction is pending.");
        await ValidateCheckpointAsync(projectId, checkpointId, token);
        await ValidateCheckpointAsync(projectId, sourceCheckpointId, token);
        var pending = new StoryPendingRestore
        {
            TargetCheckpointId = checkpointId,
            SafetyCheckpointId = sourceCheckpointId,
            TransactionId = Guid.NewGuid().ToString("N")
        };
        await WriteAtomicAsync(Journal(projectId), JsonSerializer.Serialize(pending, JsonOptions), token);
        await ApplyPendingRestoreAsync(projectId);
    }

    public bool HasPendingTransaction(string projectId) =>
        File.Exists(Journal(projectId)) || File.Exists(CommitJournal(projectId)) ||
        HasPendingRescue(projectId);

    // Recovery markers are NOT commit journals, but allowing another rollback
    // or branch switch before they are retired can overlap archive cleanup.
    private bool HasPendingRescue(string projectId)
    {
        string path = Path.Combine(Root(projectId), "pending-rescues");
        return Directory.Exists(path) &&
            Directory.EnumerateFileSystemEntries(path, "*", SearchOption.TopDirectoryOnly).Any();
    }

    public async Task VerifyAsync(string projectId, string checkpointId, CancellationToken token = default) =>
        _ = await ValidateCheckpointAsync(projectId, checkpointId, token);

    // Read-only, validated snapshot for the branch comparison UI. Loading a snapshot
    // must never activate it or write any canon, chapter or conversation data.
    public async Task<StoryMemoryDocument> ReadMemorySnapshotAsync(string projectId,
        string checkpointId, CancellationToken token = default)
    {
        StoryCheckpoint manifest = await ValidateCheckpointAsync(projectId, checkpointId, token);
        string dir = Path.Combine(Root(projectId), "checkpoints", checkpointId);
        (string memoryJson, _) = await StoryCheckpointSnapshots.ReadAsync(dir, manifest, token);
        return JsonSerializer.Deserialize<StoryMemoryDocument>(memoryJson, JsonOptions)
            ?? throw new InvalidDataException("Branch memory snapshot is incomplete.");
    }

    // A chapter commit spans three files (prose, memory, conversation). The durable
    // checkpoint already exists before the journal is created. Until its committed
    // marker is written, recovery always restores the pre-commit snapshot.
    public async Task BeginCommitAsync(string projectId, string checkpointId, CancellationToken token = default)
    {
        using var operation = await StoryOperationGate.EnterAsync(projectId, token);
        if (HasPendingTransaction(projectId))
            throw new InvalidOperationException("Finish Story recovery and manuscript rescue before another commit.");
        await ValidateCheckpointAsync(projectId, checkpointId, token);
        var pending = new StoryPendingCommit
        {
            CheckpointId = checkpointId,
            TransactionId = Guid.NewGuid().ToString("N")
        };
        await WriteAtomicAsync(CommitJournal(projectId), JsonSerializer.Serialize(pending, JsonOptions), token);
    }

    public async Task CompleteCommitAsync(string projectId)
    {
        using var operation = await StoryOperationGate.EnterAsync(projectId);
        StoryPendingCommit pending = await ReadPendingCommitAsync(projectId);
        if (pending.Committed || pending.Aborting)
            throw new InvalidOperationException("Chapter commit has already been finalized or is being aborted.");
        await VerifyNewChapterBeforeFinalizeAsync(projectId, pending.CheckpointId);
        pending.Committed = true;
        // Mark success only after chapter prose, memory AND committed conversation are durable.
        await WriteAtomicAsync(CommitJournal(projectId), JsonSerializer.Serialize(pending, JsonOptions), CancellationToken.None);
        try { File.Delete(CommitJournal(projectId)); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // Recovery will only remove the committed journal, never undo a successful chapter.
            AppLogger.Warn("STORY-CHECKPOINT", $"Completed journal cleanup deferred | {ex.Message}");
        }
    }

    // The commit marker must never mean "success" if the new prose or the
    // structured chapter reference is missing on disk. This is a bounded
    // verification of the newly created chapter, not a costly rehash of every
    // historical chapter in a long Story.
    private async Task VerifyNewChapterBeforeFinalizeAsync(string projectId, string checkpointId)
    {
        string checkpointDir = Path.Combine(Root(projectId), "checkpoints", checkpointId);
        StoryCheckpoint checkpoint = await LoadManifestAsync(checkpointDir, checkpointId, CancellationToken.None);
        (string originalJson, _) = await StoryCheckpointSnapshots.ReadAsync(
            checkpointDir, checkpoint, CancellationToken.None);
        StoryMemoryDocument original = StoryMemoryStore.ParseMemoryDocument(originalJson);
        StoryMemoryDocument current = await new StoryMemoryStore().LoadAsync(projectId);
        if (current.Chapters.Count != original.Chapters.Count + 1)
            throw new InvalidDataException("Chapter commit has no single durable new chapter in Story Memory.");
        // The conversation must contain the committed draft for this exact
        // chapter; parseable-but-stale conversation.json is not enough.
        IReadOnlyList<StoryMessage> messages = await _projects.LoadMessagesAsync(projectId);
        StoryChapterMemory added = StoryChapterCommitGuard.Validate(original, current, messages);
        string path = Path.Combine(_projects.GetProjectDirectory(projectId), "chapters", added.FileName);
        if (!File.Exists(path) || new FileInfo(path).Length == 0)
            throw new InvalidDataException("Committed chapter file is missing or empty.");
    }

    public async Task AbortCommitAsync(string projectId)
    {
        using var operation = await StoryOperationGate.EnterAsync(projectId);
        await AbortCommitCoreAsync(projectId);
    }

    private async Task AbortCommitCoreAsync(string projectId)
    {
        StoryPendingCommit pending = await ReadPendingCommitAsync(projectId);
        if (pending.Committed)
            throw new InvalidOperationException("A completed chapter transaction must not be rolled back as interrupted.");
        if (pending.Aborting && !File.Exists(Journal(projectId)))
        {
            // The restore journal is removed only AFTER all live files were published.
            // Do not roll them back a second time after a crash during cleanup.
            File.Delete(CommitJournal(projectId));
            return;
        }
        // Reuse the idempotent restore journal, but do not save a partial commit as a branch.
        // It may contain inconsistent chapter/memory/conversation state.
        var restore = new StoryPendingRestore
        {
            // Keep the original commit's identity for crash recovery and rescued
            // manuscripts. An abort is the second phase of the SAME transaction,
            // not a new transaction with an unrelated recovery directory.
            TransactionId = pending.TransactionId,
            TargetCheckpointId = pending.CheckpointId,
            SafetyCheckpointId = pending.CheckpointId
        };
        await ValidateCheckpointAsync(projectId, pending.CheckpointId);
        if (File.Exists(Journal(projectId)))
        {
            StoryPendingRestore? existing = JsonSerializer.Deserialize<StoryPendingRestore>(
                await File.ReadAllTextAsync(Journal(projectId)), JsonOptions);
            if (existing is null || !IsId(existing.TransactionId) ||
                existing.TargetCheckpointId != pending.CheckpointId ||
                existing.SafetyCheckpointId != pending.CheckpointId)
                throw new InvalidDataException("Unrelated restore journal cannot be reused to abort a chapter commit.");
        }
        else
        {
            await WriteAtomicAsync(Journal(projectId), JsonSerializer.Serialize(restore, JsonOptions), CancellationToken.None);
        }
        // Intent to abort is only published AFTER the restore journal exists.
        // If we crash later, replay the restore journal before retiring this marker.
        if (!pending.Aborting)
        {
            pending.Aborting = true;
            await WriteAtomicAsync(CommitJournal(projectId), JsonSerializer.Serialize(pending, JsonOptions), CancellationToken.None);
        }
        await ApplyPendingRestoreAsync(projectId);
        File.Delete(CommitJournal(projectId));
    }

    public async Task RecoverIfNeededAsync(string projectId)
    {
        using var operation = await StoryOperationGate.EnterAsync(projectId);
        if (File.Exists(Journal(projectId)) && File.Exists(CommitJournal(projectId)))
        {
            // A crash may occur after creating pending-restore.json but before
            // writing Aborting=true into pending-commit.json. Handle the pair as
            // ONE abort first: applying the restore alone here would remove its
            // journal and then cause a SECOND abort in the block below.
            StoryPendingCommit overlapping = await ReadPendingCommitAsync(projectId);
            if (overlapping.Committed)
                throw new InvalidDataException("A completed commit cannot overlap an unfinished restore.");
            AppLogger.Warn("STORY-CHECKPOINT", $"Interrupted commit abort detected | project={projectId}");
            await AbortCommitCoreAsync(projectId);
        }
        else if (File.Exists(Journal(projectId)))
        {
            AppLogger.Warn("STORY-CHECKPOINT", $"Interrupted restore detected | project={projectId}");
            await ApplyPendingRestoreAsync(projectId);
        }
        if (File.Exists(CommitJournal(projectId)))
        {
            StoryPendingCommit pending = await ReadPendingCommitAsync(projectId);
            if (pending.Committed)
            {
                // A recorded success must still match the persisted chapter, memory
                // and conversation. Invalid state is never silently accepted as canon.
                await VerifyNewChapterBeforeFinalizeAsync(projectId, pending.CheckpointId);
                File.Delete(CommitJournal(projectId));
                AppLogger.Info("STORY-CHECKPOINT", $"Completed commit journal removed | project={projectId}");
            }
            else
            {
                AppLogger.Warn("STORY-CHECKPOINT", $"Interrupted chapter commit detected | project={projectId}");
                await AbortCommitCoreAsync(projectId);
            }
        }
        // A crash may occur AFTER the live Story has been restored and the
        // restore journal retired but BEFORE orphan manuscripts were rescued.
        // Retain independent, durable cleanup records and replay them on open.
        await FinishPendingRescuesAsync(projectId);
    }

    private async Task<StoryPendingCommit> ReadPendingCommitAsync(string projectId)
    {
        StoryPendingCommit? pending = JsonSerializer.Deserialize<StoryPendingCommit>(
            await File.ReadAllTextAsync(CommitJournal(projectId)), JsonOptions);
        if (pending is null || !IsId(pending.TransactionId) || !IsId(pending.CheckpointId) ||
            (pending.Committed && pending.Aborting))
            throw new InvalidDataException("Invalid pending story commit journal.");
        return pending;
    }

    private async Task ApplyPendingRestoreAsync(string projectId)
    {
        string journal = Journal(projectId);
        StoryPendingRestore? pending = JsonSerializer.Deserialize<StoryPendingRestore>(
            await File.ReadAllTextAsync(journal), JsonOptions);
        if (pending is null || !IsId(pending.TransactionId))
            throw new InvalidDataException("Invalid pending story restore journal.");
        StoryCheckpoint target = await ValidateCheckpointAsync(projectId, pending.TargetCheckpointId);
        // Do not retire the materialized source state unless its safety snapshot is
        // still fully recoverable. This also protects interrupted switch recovery.
        if (pending.SafetyCheckpointId != pending.TargetCheckpointId)
            await ValidateCheckpointAsync(projectId, pending.SafetyCheckpointId);
        string root = Root(projectId);
        string projectDir = _projects.GetProjectDirectory(projectId);
        string chapterDir = Path.Combine(projectDir, "chapters");
        string stage = Path.Combine(root, "staging", pending.TransactionId);
        string stageChapters = Path.Combine(stage, "chapters");
        string archive = Path.Combine(root, "history", pending.TransactionId + "-previous-chapters");
        Directory.CreateDirectory(stageChapters);

        // Build and validate a complete replacement set before taking the live chapters offline.
        foreach (StoryCheckpointFile entry in target.Chapters)
        {
            string src = Path.Combine(root, "objects", entry.Sha256 + ".md");
            string dest = Path.Combine(stageChapters, entry.FileName);
            File.Copy(src, dest, true);
            if (!string.Equals(await Sha256FileAsync(dest, CancellationToken.None),
                entry.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new IOException("Staged chapter differs from verified checkpoint object.");
        }
        string snapshotDir = Path.Combine(root, "checkpoints", target.Id);
        (string targetMemory, string targetConversation) =
            await StoryCheckpointSnapshots.ReadAsync(snapshotDir, target, CancellationToken.None);
        string memoryPath = Path.Combine(projectDir, "memory", "story-memory.json");
        string conversationPath = Path.Combine(projectDir, "conversation.json");
        Directory.CreateDirectory(Path.GetDirectoryName(memoryPath)!);
        await WriteAtomicAsync(memoryPath + ".restore", targetMemory, CancellationToken.None);
        await WriteAtomicAsync(conversationPath + ".restore", targetConversation, CancellationToken.None);

        // Register the archive cleanup before any live directory is moved.
        // This marker is NOT the restore journal: post-restore cleanup is safe
        // to retry without rolling the user's newly activated Story back.
        string rescueMarker = PendingRescueMarker(projectId, pending.TransactionId);
        if (!File.Exists(rescueMarker))
            await WriteAtomicAsync(rescueMarker, JsonSerializer.Serialize(new StoryPendingRescue
            {
                TransactionId = pending.TransactionId,
                SafetyCheckpointId = pending.SafetyCheckpointId
            }, JsonOptions), CancellationToken.None);

        if (Directory.Exists(chapterDir))
        {
            if (!Directory.Exists(archive))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(archive)!);
                Directory.Move(chapterDir, archive);
            }
            else
            {
                // Recovery is replaying a prior attempt. Do not discard files
                // externally added/edited in the partially restored live directory.
                string replayRescue = Path.Combine(root, "recovered", pending.TransactionId + "-replay");
                await StoryRestoreRescue.PreserveUntrackedAsync(chapterDir, replayRescue, target.Chapters);
                Directory.Delete(chapterDir, true);
            }
        }
        Directory.Move(stageChapters, chapterDir);
        File.Move(memoryPath + ".restore", memoryPath, true);
        File.Move(conversationPath + ".restore", conversationPath, true);
        StoryMemoryStore.InvalidateChapterCache(projectId);
        File.Delete(journal);
        // Rescue is idempotent and separately journaled; cleanup failures leave
        // the archived original files untouched for the next activation.
        await FinishPendingRescuesAsync(projectId);
        AppLogger.Info("STORY-CHECKPOINT", $"Restored | project={projectId} | checkpoint={target.Id} | safety={pending.SafetyCheckpointId}");
    }

    private string PendingRescueMarker(string projectId, string transactionId) =>
        Path.Combine(Root(projectId), "pending-rescues", transactionId + ".json");

    private async Task FinishPendingRescuesAsync(string projectId)
    {
        string root = Root(projectId);
        string markers = Path.Combine(root, "pending-rescues");
        if (!Directory.Exists(markers)) return;
        foreach (string path in Directory.EnumerateFiles(markers, "*.json", SearchOption.TopDirectoryOnly))
        {
            try
            {
                StoryPendingRescue? item = JsonSerializer.Deserialize<StoryPendingRescue>(
                    await File.ReadAllTextAsync(path), JsonOptions);
                if (item is null || !IsId(item.TransactionId) || !IsId(item.SafetyCheckpointId) ||
                    !string.Equals(Path.GetFileName(path), item.TransactionId + ".json", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Invalid pending manuscript rescue journal.");
                string archive = Path.Combine(root, "history", item.TransactionId + "-previous-chapters");
                if (Directory.Exists(archive))
                {
                    if ((File.GetAttributes(archive) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidDataException("Archived chapters are linked to another location.");
                    StoryCheckpoint safety = await ValidateCheckpointAsync(projectId, item.SafetyCheckpointId);
                    string rescue = Path.Combine(root, "recovered", item.TransactionId);
                    int moved = await StoryRestoreRescue.PreserveUntrackedAsync(archive, rescue, safety.Chapters);
                    if (moved != 0)
                        AppLogger.Warn("STORY-CHECKPOINT", $"Preserved {moved} unsnapshotted file(s) | project={projectId} | path={rescue}");
                    Directory.Delete(archive, recursive: true);
                }
                string stage = Path.Combine(root, "staging", item.TransactionId);
                if (Directory.Exists(stage) && (File.GetAttributes(stage) & FileAttributes.ReparsePoint) == 0)
                    Directory.Delete(stage, recursive: true);
                File.Delete(path);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or JsonException)
            {
                // Restoration itself was committed. Never destructively retry it
                // just because cleanup is blocked; keep the original archive.
                AppLogger.Warn("STORY-CHECKPOINT", $"Manuscript rescue cleanup deferred | project={projectId} | {ex.Message}");
            }
        }
    }

    private async Task<StoryCheckpoint> ValidateCheckpointAsync(string projectId, string id, CancellationToken token = default)
    {
        if (!IsId(id))
            throw new InvalidDataException("Invalid story checkpoint identifier.");
        string dir = Path.Combine(Root(projectId), "checkpoints", id);
        if ((File.GetAttributes(dir) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Linked checkpoint directory cannot be restored.");
        StoryCheckpoint checkpoint = await LoadManifestAsync(dir, id, token);
        (string memoryJson, string conversationJson) =
            await StoryCheckpointSnapshots.ReadAsync(dir, checkpoint, token);
        StoryMemoryDocument? memory = JsonSerializer.Deserialize<StoryMemoryDocument>(memoryJson, JsonOptions);
        _ = StoryProjectStore.ParseConversationMessages(conversationJson);
        if (memory is null)
            throw new InvalidDataException("Checkpoint memory snapshot is missing.");
        StoryCheckpointChapterGuard.ValidateManifest(memory, checkpoint);
        foreach (StoryCheckpointFile entry in checkpoint.Chapters)
        {
            token.ThrowIfCancellationRequested();
            string blob = Path.Combine(Root(projectId), "objects", entry.Sha256 + ".md");
            if (!File.Exists(blob) || (File.GetAttributes(blob) & FileAttributes.ReparsePoint) != 0 ||
                !string.Equals(await Sha256FileAsync(blob, token), entry.Sha256, StringComparison.Ordinal))
                throw new InvalidDataException($"Checkpoint chapter object is missing or corrupt: {entry.FileName}");
        }
        return checkpoint;
    }

    private static async Task<StoryCheckpoint> LoadManifestAsync(
        string directory, string id, CancellationToken token)
    {
        StoryCheckpoint? manifest = JsonSerializer.Deserialize<StoryCheckpoint>(
            await File.ReadAllTextAsync(Path.Combine(directory, "checkpoint.json"), token), JsonOptions);
        if (manifest is null || manifest.Id != id)
            throw new InvalidDataException("Checkpoint metadata is incomplete.");
        return manifest;
    }

    private static async Task<string> Sha256FileAsync(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        return Convert.ToHexString(await SHA256.HashDataAsync(stream, token));
    }

    internal static void ValidateFileName(string name)
    {
        if (string.IsNullOrWhiteSpace(name) || name.Length > 240 ||
            Path.GetFileName(name) != name ||
            name.IndexOfAny(new[] { '/', '\\', '<', '>', ':', '"', '|', '?', '*' }) >= 0 ||
            name.Any(char.IsControl) || name.EndsWith(' ') || name.EndsWith('.') ||
            !name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("Invalid chapter filename in checkpoint.");
        string stem = Path.GetFileNameWithoutExtension(name);
        string first = stem.Split('.')[0].TrimEnd(' ');
        if (first.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
            first.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
            first.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
            first.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
            (first.Length == 4 && (first.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                first.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                first[3] is >= '1' and <= '9'))
            throw new InvalidDataException("Reserved Windows chapter filename in checkpoint.");
    }

    private static bool IsId(string? id) => id is { Length: 32 } && id.All(Uri.IsHexDigit);
    private string Root(string projectId) => Path.Combine(_projects.GetProjectDirectory(projectId), "branches");
    private string Journal(string projectId) => Path.Combine(Root(projectId), "pending-restore.json");
    private string CommitJournal(string projectId) => Path.Combine(Root(projectId), "pending-commit.json");

    private static async Task WriteAtomicAsync(string path, string json, CancellationToken token)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(tmp, json, token);
            File.Move(tmp, path, true);
        }
        finally
        {
            // A cancelled/failed journal write must not strand temp files.
            // Never touch the published transaction journal on cleanup.
            if (File.Exists(tmp)) File.Delete(tmp);
        }
    }
}

public sealed class StoryCheckpoint
{
    public string Id { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public int ChapterCount { get; set; }
    public List<StoryCheckpointFile> Chapters { get; set; } = new();
    // Missing in old checkpoints: legacy plaintext snapshots remain readable.
    public int SnapshotFormat { get; set; } = 1;
    public string MemorySha256 { get; set; } = string.Empty;
    public string ConversationSha256 { get; set; } = string.Empty;
}

public sealed class StoryCheckpointFile
{
    public string FileName { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
}

public sealed class StoryPendingRestore
{
    public string TargetCheckpointId { get; set; } = string.Empty;
    public string SafetyCheckpointId { get; set; } = string.Empty;
    public string TransactionId { get; set; } = string.Empty;
}

public sealed class StoryPendingCommit
{
    public string TransactionId { get; set; } = string.Empty;
    public string CheckpointId { get; set; } = string.Empty;
    public bool Committed { get; set; }
    // Optional in old journals; false by default for backward compatibility.
    public bool Aborting { get; set; }
}

// Durable post-restore cleanup marker: safe to replay without undoing canon.
public sealed class StoryPendingRescue
{
    public string TransactionId { get; set; } = string.Empty;
    public string SafetyCheckpointId { get; set; } = string.Empty;
}
