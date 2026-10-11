using System.IO;
using System.Text.Json;

namespace WorkChatMini;

// A branch is a named pointer to a checkpoint. Only the active branch is materialized
// in chapters/, memory/, and conversation.json. No per-branch copy of the project exists.
public sealed class StoryBranchStore
{
    private readonly StoryProjectStore _projects = new();
    private readonly StoryCheckpointStore _checkpoints = new();
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

    public StoryBranchIndex? Load(string projectId)
    {
        string path = IndexPath(projectId);
        if (!File.Exists(path))
            return null; // Legacy stories opt into branching only when requested.
        StoryBranchIndex? index = JsonSerializer.Deserialize<StoryBranchIndex>(File.ReadAllText(path), Options);
        Validate(index);
        return index;
    }

    public async Task<StoryBranchIndex> CreateBranchAsync(string projectId, StoryMemoryDocument memory,
        IReadOnlyList<StoryMessage> messages, string name, CancellationToken token = default)
    {
        using var operation = await StoryOperationGate.EnterAsync(projectId, token);
        string cleanName = CleanName(name);
        // Do not initialize a branch baseline from half-restored live files.
        if (_checkpoints.HasPendingTransaction(projectId))
            throw new InvalidOperationException("Finish Story recovery before creating a branch.");
        StoryBranchIndex index = await EnsureIndexAsync(projectId, memory, messages, token);
        if (index.PendingSwitch is not null || _checkpoints.HasPendingTransaction(projectId))
            throw new InvalidOperationException("Finish story recovery before creating another branch.");
        if (index.Branches.Count >= 20)
            throw new InvalidOperationException("This story has reached its 20-branch limit.");
        if (index.Branches.Any(b => string.Equals(b.Name, cleanName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A branch with this name already exists.");

        // Capture the live state, not the (possibly old) head pointer: commits advance
        // the active story between switches.
        string head = await _checkpoints.CaptureAsync(projectId, memory, messages,
            $"Branch start: {cleanName}", token);
        StoryBranchInfo active = GetBranch(index, index.ActiveBranchId);
        active.HeadCheckpointId = head;
        active.UpdatedUtc = DateTimeOffset.UtcNow;
        index.Branches.Add(new StoryBranchInfo
        {
            Id = Guid.NewGuid().ToString("N"), Name = cleanName, HeadCheckpointId = head
        });
        await SaveAsync(projectId, index, token);
        return index;
    }

    public async Task<StoryBranchIndex> RenameBranchAsync(string projectId, string branchId,
        string name, CancellationToken token = default)
    {
        using var operation = await StoryOperationGate.EnterAsync(projectId, token);
        StoryBranchIndex index = Load(projectId)
            ?? throw new InvalidOperationException("This story has no branches yet.");
        if (index.PendingSwitch is not null || _checkpoints.HasPendingTransaction(projectId))
            throw new InvalidOperationException("Story recovery is required before renaming a branch.");
        string cleanName = CleanName(name);
        StoryBranchInfo branch = GetBranch(index, branchId);
        if (index.Branches.Any(b => b.Id != branchId &&
            string.Equals(b.Name, cleanName, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("A branch with this name already exists.");
        branch.Name = cleanName;
        branch.UpdatedUtc = DateTimeOffset.UtcNow;
        await SaveAsync(projectId, index, token);
        return index;
    }

    // Archiving only moves a named pointer. It never deletes a checkpoint, chapter object,
    // or manuscript file. The active branch cannot be archived.
    public async Task<StoryBranchIndex> ArchiveBranchAsync(string projectId, string branchId,
        CancellationToken token = default)
    {
        using var operation = await StoryOperationGate.EnterAsync(projectId, token);
        StoryBranchIndex index = Load(projectId)
            ?? throw new InvalidOperationException("This story has no branches yet.");
        if (index.PendingSwitch is not null || _checkpoints.HasPendingTransaction(projectId))
            throw new InvalidOperationException("Finish story recovery before archiving a branch.");
        if (index.ActiveBranchId == branchId)
            throw new InvalidOperationException("Switch to another branch before archiving the active one.");
        if (index.ArchivedBranches.Count >= 100)
            throw new InvalidOperationException("The story has reached its 100 archived-branch limit.");
        StoryBranchInfo branch = GetBranch(index, branchId);
        await _checkpoints.VerifyAsync(projectId, branch.HeadCheckpointId, token);
        index.Branches.Remove(branch);
        branch.ArchivedUtc = DateTimeOffset.UtcNow;
        index.ArchivedBranches.Add(branch);
        await SaveAsync(projectId, index, token);
        return index;
    }

    public async Task<StoryBranchIndex> RestoreArchivedBranchAsync(string projectId, string branchId,
        CancellationToken token = default)
    {
        using var operation = await StoryOperationGate.EnterAsync(projectId, token);
        StoryBranchIndex index = Load(projectId)
            ?? throw new InvalidOperationException("This story has no branches yet.");
        if (index.PendingSwitch is not null || _checkpoints.HasPendingTransaction(projectId))
            throw new InvalidOperationException("Finish story recovery before restoring an archived branch.");
        if (index.Branches.Count >= 20)
            throw new InvalidOperationException("The 20 active-branch limit has been reached.");
        StoryBranchInfo branch = index.ArchivedBranches.SingleOrDefault(b => b.Id == branchId)
            ?? throw new InvalidOperationException("Archived branch not found.");
        await _checkpoints.VerifyAsync(projectId, branch.HeadCheckpointId, token);
        branch.Name = AvailableName(index.Branches, branch.Name);
        index.ArchivedBranches.Remove(branch);
        branch.ArchivedUtc = null;
        branch.UpdatedUtc = DateTimeOffset.UtcNow;
        index.Branches.Add(branch);
        await SaveAsync(projectId, index, token);
        return index;
    }

    private static string AvailableName(IEnumerable<StoryBranchInfo> active, string original)
    {
        var names = new HashSet<string>(active.Select(b => b.Name), StringComparer.OrdinalIgnoreCase);
        if (!names.Contains(original))
            return original;
        for (int n = 1; n <= 100; n++)
        {
            string suffix = n == 1 ? " (restored)" : $" (restored {n})";
            string proposal = original[..Math.Min(original.Length, 60 - suffix.Length)] + suffix;
            if (!names.Contains(proposal))
                return proposal;
        }
        throw new InvalidOperationException("Unable to assign an available restored branch name.");
    }

    public async Task SwitchAsync(string projectId, string destinationId,
        StoryMemoryDocument activeMemory, IReadOnlyList<StoryMessage> activeMessages,
        CancellationToken token = default)
    {
        using var operation = await StoryOperationGate.EnterAsync(projectId, token);
        StoryBranchIndex index = Load(projectId)
            ?? throw new InvalidOperationException("This story has no branches yet.");
        if (index.PendingSwitch is not null)
            throw new InvalidOperationException("A branch switch is already pending recovery.");
        if (index.ActiveBranchId == destinationId)
            return;
        StoryBranchInfo target = GetBranch(index, destinationId);
        StoryBranchInfo source = GetBranch(index, index.ActiveBranchId);
        // Validate target BEFORE writing a pending switch. Corrupt/missing targets must
        // leave the current story untouched and fully usable.
        await _checkpoints.VerifyAsync(projectId, target.HeadCheckpointId, token);
        if (_checkpoints.HasPendingTransaction(projectId))
            throw new InvalidOperationException("Story commit/restore recovery must finish first.");

        string sourceHead = await _checkpoints.CaptureAsync(projectId, activeMemory, activeMessages,
            $"Leave branch: {source.Name}", token);
        source.HeadCheckpointId = sourceHead;
        source.UpdatedUtc = DateTimeOffset.UtcNow;
        index.PendingSwitch = new StoryPendingBranchSwitch
        {
            FromBranchId = source.Id,
            ToBranchId = target.Id,
            SourceCheckpointId = sourceHead,
            TargetCheckpointId = target.HeadCheckpointId
        };
        // The pending switch is durable BEFORE the materialized story is touched.
        await SaveAsync(projectId, index, token);
        // Once pending is durable, cancellation must not leave a half-restored story.
        await CompletePendingSwitchAsync(projectId, index);
    }

    public async Task RecoverIfNeededAsync(string projectId)
    {
        using var operation = await StoryOperationGate.EnterAsync(projectId);
        StoryBranchIndex? index = Load(projectId);
        if (index?.PendingSwitch is null)
            return;
        AppLogger.Warn("STORY-BRANCH", $"Resuming interrupted branch switch | project={projectId}");
        await CompletePendingSwitchAsync(projectId, index);
    }

    private async Task CompletePendingSwitchAsync(string projectId, StoryBranchIndex index)
    {
        StoryPendingBranchSwitch pending = index.PendingSwitch
            ?? throw new InvalidOperationException("No pending branch switch.");
        // The checkpoint store's own journal is retried first on project activation.
        // Reapplying the destination is safe if a crash happened between restore and
        // writing the active pointer: it never snapshots the partly restored live state.
        await _checkpoints.RestoreForBranchSwitchAsync(projectId,
            pending.TargetCheckpointId, pending.SourceCheckpointId);
        index.ActiveBranchId = pending.ToBranchId;
        index.PendingSwitch = null;
        await SaveAsync(projectId, index, CancellationToken.None);
        AppLogger.Info("STORY-BRANCH", $"Activated branch | project={projectId} | branch={index.ActiveBranchId}");
    }

    private async Task<StoryBranchIndex> EnsureIndexAsync(string projectId, StoryMemoryDocument memory,
        IReadOnlyList<StoryMessage> messages, CancellationToken token)
    {
        StoryBranchIndex? existing = Load(projectId);
        if (existing is not null)
            return existing;
        if (_checkpoints.HasPendingTransaction(projectId))
            throw new InvalidOperationException("Story recovery must finish before enabling branches.");
        string initial = await _checkpoints.CaptureAsync(projectId, memory, messages,
            "First branch baseline", token);
        string mainId = Guid.NewGuid().ToString("N");
        var index = new StoryBranchIndex
        {
            ActiveBranchId = mainId,
            Branches = new List<StoryBranchInfo>
            {
                new() { Id = mainId, Name = "Main", HeadCheckpointId = initial }
            }
        };
        await SaveAsync(projectId, index, token);
        return index;
    }

    private async Task SaveAsync(string projectId, StoryBranchIndex index, CancellationToken token)
    {
        Validate(index);
        string path = IndexPath(projectId);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temporary, JsonSerializer.Serialize(index, Options), token);
            File.Move(temporary, path, true);
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    private string IndexPath(string id) => Path.Combine(_projects.GetProjectDirectory(id), "branches", "branch-index.json");
    private static StoryBranchInfo GetBranch(StoryBranchIndex index, string id) =>
        index.Branches.SingleOrDefault(b => b.Id == id)
        ?? throw new InvalidDataException("Branch identifier is missing from the story index.");
    private static bool IsId(string? value) => value is { Length: 32 } && value.All(Uri.IsHexDigit);

    private static string CleanName(string name)
    {
        string clean = (name ?? string.Empty).Trim();
        if (clean.Length is < 1 or > 60 || clean.Any(char.IsControl))
            throw new ArgumentException("Branch name must contain 1–60 printable characters.", nameof(name));
        return clean;
    }

    internal static void ValidateImportedIndex(StoryBranchIndex? index) => Validate(index);

    private static void Validate(StoryBranchIndex? index)
    {
        if (index is null || index.Version != 1 || index.Branches is null || index.Branches.Count is < 1 or > 20 ||
            !IsId(index.ActiveBranchId) || index.Branches.Count(b => b.Id == index.ActiveBranchId) != 1 ||
            index.Branches.Any(b => !IsId(b.Id) || !IsId(b.HeadCheckpointId) ||
                string.IsNullOrWhiteSpace(b.Name) || b.Name.Length > 60) ||
            index.Branches.Select(b => b.Id).Distinct(StringComparer.Ordinal).Count() != index.Branches.Count ||
            index.Branches.Select(b => b.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() != index.Branches.Count ||
            index.ArchivedBranches is null || index.ArchivedBranches.Count > 100 ||
            index.ArchivedBranches.Any(b => !IsId(b.Id) || !IsId(b.HeadCheckpointId) ||
                string.IsNullOrWhiteSpace(b.Name) || b.Name.Length > 60 || b.ArchivedUtc is null) ||
            index.Branches.Concat(index.ArchivedBranches).Select(b => b.Id)
                .Distinct(StringComparer.Ordinal).Count() != index.Branches.Count + index.ArchivedBranches.Count)
            throw new InvalidDataException("Invalid branch index: no story data was changed.");
        if (index.PendingSwitch is { } pending &&
            (!IsId(pending.FromBranchId) || !IsId(pending.ToBranchId) ||
             !IsId(pending.SourceCheckpointId) || !IsId(pending.TargetCheckpointId) ||
             pending.FromBranchId != index.ActiveBranchId ||
             !index.Branches.Any(b => b.Id == pending.ToBranchId) ||
             GetBranch(index, pending.FromBranchId).HeadCheckpointId != pending.SourceCheckpointId ||
             GetBranch(index, pending.ToBranchId).HeadCheckpointId != pending.TargetCheckpointId))
            throw new InvalidDataException("Invalid pending branch switch: automatic recovery is blocked.");
    }
}

public sealed class StoryBranchIndex
{
    public int Version { get; set; } = 1;
    public string ActiveBranchId { get; set; } = string.Empty;
    public List<StoryBranchInfo> Branches { get; set; } = new();
    public List<StoryBranchInfo> ArchivedBranches { get; set; } = new();
    public StoryPendingBranchSwitch? PendingSwitch { get; set; }
}

public sealed class StoryBranchInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string HeadCheckpointId { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedUtc { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset? ArchivedUtc { get; set; }
}

public sealed class StoryPendingBranchSwitch
{
    public string FromBranchId { get; set; } = string.Empty;
    public string ToBranchId { get; set; } = string.Empty;
    public string SourceCheckpointId { get; set; } = string.Empty;
    public string TargetCheckpointId { get; set; } = string.Empty;
}
