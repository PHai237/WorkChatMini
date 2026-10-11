using System.Buffers;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace WorkChatMini;

/// <summary>
/// A portable, read-only copy of one Story's complete on-disk state. Backups live
/// outside Projects, include checkpoints and branch objects, and are never
/// automatically imported or used as canon. No browser profile or API keys enter
/// the archive. A manifest with per-file SHA-256 detects truncated/changed exports.
/// </summary>
internal sealed partial class StoryBackupService
{
    private readonly StoryProjectStore _projects = new();
    private readonly StoryCheckpointStore _checkpoints = new();
    private readonly StoryBranchStore _branches = new();
    private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };
    private const string ManifestEntry = "backup-manifest.json";

    internal async Task<StoryBackupResult> CreateAsync(string projectId, string? outputDirectory = null,
        CancellationToken cancellationToken = default)
    {
        // Keep checkpoint/branch/restore mutations out of a multi-file export.
        // The hash-based recheck below still protects against independent editors
        // and ordinary autosaves, which do not use this operation gate.
        using var operation = await StoryOperationGate.EnterAsync(projectId, cancellationToken);
        // A pending journal means the live files could represent different phases of
        // a chapter commit or branch switch. Never advertise that as a safe backup.
        if (_checkpoints.HasPendingTransaction(projectId) ||
            _branches.Load(projectId)?.PendingSwitch is not null ||
            HasDeferredRescues(projectId))
            throw new InvalidOperationException("Finish Story recovery and manuscript rescue cleanup before creating a backup.");

        string root = _projects.GetProjectDirectory(projectId);
        if (!File.Exists(Path.Combine(root, "project.json")))
            throw new InvalidDataException("Cannot back up a Story without project.json.");

        string destinationDirectory = outputDirectory ??
            Path.Combine(AppSettings.AppDataDirectory, "AI", "Backups");
        // Backups may not be written into the Story itself: otherwise a later
        // backup would recursively archive previous backups or changing .partial files.
        string sourceFull = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string destFull = Path.GetFullPath(destinationDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (destFull.StartsWith(sourceFull, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Choose a backup folder outside the Story directory.");

        // Walk directories explicitly: recursive EnumerateFiles can enter a
        // junction before we get the chance to inspect its parent. Reject any
        // linked directory/file without following it outside the Story root.
        var files = CollectSafeStoryFiles(root, cancellationToken);
        if (!files.Any(f => f.EntryName == "story/project.json"))
            throw new InvalidDataException("Story project metadata disappeared during backup.");
        if (_checkpoints.HasPendingTransaction(projectId) ||
            _branches.Load(projectId)?.PendingSwitch is not null ||
            HasDeferredRescues(projectId))
            throw new InvalidOperationException("A Story transaction started during backup preflight.");

        files.Sort((a, b) => StringComparer.Ordinal.Compare(a.EntryName, b.EntryName));
        Directory.CreateDirectory(destinationDirectory);
        string idPart = new string(projectId.Where(Uri.IsHexDigit).Take(8).ToArray());
        if (idPart.Length == 0) idPart = "unknown";
        string name = $"Story-{idPart}-{DateTime.UtcNow:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}";
        string finalPath = Path.Combine(destinationDirectory, name + ".zip");
        string partialPath = finalPath + ".partial";
        try
        {
            var manifest = new StoryBackupManifest
            {
                SourceProjectId = projectId,
                CreatedUtc = DateTimeOffset.UtcNow
            };
            await using (var output = new FileStream(partialPath, FileMode.CreateNew, FileAccess.Write,
                FileShare.None, 65536, FileOptions.Asynchronous))
            {
                using (var archive = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
                {
                    foreach ((string fullPath, string entryName) in files)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        ZipArchiveEntry entry = archive.CreateEntry(entryName, CompressionLevel.Fastest);
                        await using var input = new FileStream(fullPath, FileMode.Open, FileAccess.Read,
                            FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
                        await using Stream target = entry.Open();
                        (long length, string digest) = await CopyAndHashAsync(input, target, cancellationToken);
                        manifest.Files.Add(new StoryBackupFile { Path = entryName, Bytes = length, Sha256 = digest });
                    }
                    ZipArchiveEntry metadata = archive.CreateEntry(ManifestEntry, CompressionLevel.Fastest);
                    await using Stream entryStream = metadata.Open();
                    await JsonSerializer.SerializeAsync(entryStream, manifest, Options, cancellationToken);
                }
                await output.FlushAsync(cancellationToken);
                output.Flush(flushToDisk: true);
            }
            // Verify the exact archived bytes, not just the original source files.
            StoryBackupVerification verification = await VerifyAsync(partialPath, cancellationToken);
            if (!verification.Valid || verification.Files != manifest.Files.Count)
                throw new InvalidDataException("Story backup verification failed before publication.");
            // A delayed autosave, external editor or filesystem watcher can
            // replace a source file after it was copied. Re-hash source bytes
            // to refuse a mixed snapshot rather than publishing it as complete.
            for (int i = 0; i < files.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await using var current = new FileStream(files[i].FullPath, FileMode.Open,
                    FileAccess.Read, FileShare.Read, 65536,
                    FileOptions.Asynchronous | FileOptions.SequentialScan);
                (long size, string checksum) = await ReadHashAsync(current, cancellationToken);
                if (size != manifest.Files[i].Bytes ||
                    !checksum.Equals(manifest.Files[i].Sha256, StringComparison.Ordinal))
                    throw new IOException("Story files changed during backup; retry when editing has stopped.");
            }
            if (_checkpoints.HasPendingTransaction(projectId) ||
                _branches.Load(projectId)?.PendingSwitch is not null ||
                HasDeferredRescues(projectId))
                throw new IOException("Story transaction started during backup; refusing a mixed archive.");
            File.Move(partialPath, finalPath);
            return new StoryBackupResult(finalPath, verification.Files, verification.Bytes);
        }
        finally
        {
            if (File.Exists(partialPath)) File.Delete(partialPath);
        }
    }

    /// <summary>Verify portable archive content; does not extract or touch Story files.</summary>
    internal static async Task<StoryBackupVerification> VerifyAsync(string path,
        CancellationToken cancellationToken = default)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read,
            FileShare.Read, 65536, FileOptions.Asynchronous | FileOptions.SequentialScan);
        using var archive = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: false);
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (ZipArchiveEntry entry in archive.Entries)
            if (!names.Add(entry.FullName))
                throw new InvalidDataException("Backup ZIP contains duplicate entry names.");
        ZipArchiveEntry metadata = archive.GetEntry(ManifestEntry)
            ?? throw new InvalidDataException("Backup ZIP lacks its manifest.");
        StoryBackupManifest? manifest;
        await using (Stream input = metadata.Open())
            manifest = await JsonSerializer.DeserializeAsync<StoryBackupManifest>(input, Options, cancellationToken);
        if (manifest is null || manifest.Format != 1 || string.IsNullOrWhiteSpace(manifest.SourceProjectId) ||
            manifest.Files is null || manifest.Files.Count != archive.Entries.Count - 1 ||
            manifest.Files.Count > 100000)
            throw new InvalidDataException("Backup manifest is incomplete.");
        long total = 0;
        var expected = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { ManifestEntry };
        foreach (StoryBackupFile item in manifest.Files)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!item.Path.StartsWith("story/", StringComparison.Ordinal) ||
                !IsSafeRelativePath(item.Path[6..]) || !expected.Add(item.Path) ||
                item.Bytes < 0 || item.Sha256.Length != 64 || !item.Sha256.All(Uri.IsHexDigit))
                throw new InvalidDataException("Backup file manifest has an invalid entry.");
            ZipArchiveEntry entry = archive.GetEntry(item.Path)
                ?? throw new InvalidDataException("Backup ZIP is missing a referenced file.");
            await using Stream content = entry.Open();
            (long length, string hash) = await ReadHashAsync(content, cancellationToken);
            if (length != item.Bytes || !hash.Equals(item.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("Backup ZIP failed content checksum validation.");
            if (length > 512L * 1024 * 1024 || total > 8L * 1024 * 1024 * 1024 - length)
                throw new InvalidDataException("Backup exceeds safe single-file or total uncompressed size.");
            total += length;
        }
        if (expected.Count != names.Count || !expected.SetEquals(names))
            throw new InvalidDataException("Backup ZIP contains unlisted files.");
        return new StoryBackupVerification(true, manifest.Files.Count, total);
    }

    private static async Task<(long Length, string Hash)> ReadHashAsync(Stream source, CancellationToken token) =>
        await CopyAndHashAsync(source, Stream.Null, token);

    private static async Task<(long Length, string Hash)> CopyAndHashAsync(Stream source, Stream destination,
        CancellationToken token)
    {
        byte[] buffer = ArrayPool<byte>.Shared.Rent(65536);
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        long total = 0;
        try
        {
            int read;
            while ((read = await source.ReadAsync(buffer.AsMemory(0, buffer.Length), token)) > 0)
            {
                hash.AppendData(buffer, 0, read);
                await destination.WriteAsync(buffer.AsMemory(0, read), token);
                total += read;
            }
            return (total, Convert.ToHexString(hash.GetHashAndReset()));
        }
        finally { ArrayPool<byte>.Shared.Return(buffer); }
    }

    private static bool IsSafeRelativePath(string path) =>
        !string.IsNullOrWhiteSpace(path) && !path.StartsWith('/') &&
        path.Split('/').All(segment => segment.Length > 0 && segment is not "." and not ".." &&
            !segment.Contains(':') && !segment.Contains('\\'));

    private bool HasDeferredRescues(string projectId)
    {
        string dir = Path.Combine(_projects.GetProjectDirectory(projectId), "branches", "pending-rescues");
        return Directory.Exists(dir) && Directory.EnumerateFileSystemEntries(dir, "*.json").Any();
    }

    private static List<(string FullPath, string EntryName)> CollectSafeStoryFiles(
        string root, CancellationToken token)
    {
        if ((File.GetAttributes(root) & FileAttributes.ReparsePoint) != 0)
            throw new InvalidDataException("Linked Story root cannot be exported.");
        var files = new List<(string FullPath, string EntryName)>();
        var pending = new Stack<string>();
        var portableNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        long totalBytes = 0;
        pending.Push(root);
        while (pending.Count > 0)
        {
            token.ThrowIfCancellationRequested();
            string directory = pending.Pop();
            foreach (string entry in Directory.EnumerateFileSystemEntries(directory))
            {
                token.ThrowIfCancellationRequested();
                FileAttributes attributes = File.GetAttributes(entry);
                if ((attributes & FileAttributes.ReparsePoint) != 0)
                    throw new InvalidDataException("Linked Story file/folder prevents a safe backup.");
                string relative = Path.GetRelativePath(root, entry).Replace('\\', '/');
                if (!IsSafeRelativePath(relative))
                    throw new InvalidDataException("An unsafe Story path prevents backup.");
                if ((attributes & FileAttributes.Directory) != 0)
                {
                    pending.Push(entry);
                    continue;
                }
                if (IsTransient(relative)) continue;
                if (!portableNames.Add(relative))
                    throw new InvalidDataException("Two Story files collide on case-insensitive restore.");
                long length = new FileInfo(entry).Length;
                if (length > 512L * 1024 * 1024 || totalBytes > 8L * 1024 * 1024 * 1024 - length)
                    throw new InvalidDataException("Story files exceed the safe portable-backup size limit.");
                totalBytes += length;
                files.Add((entry, "story/" + relative));
                if (files.Count > 100000)
                    throw new InvalidDataException("Story has too many files for a portable backup.");
            }
        }
        return files;
    }

    private static bool IsTransient(string path)
    {
        string name = path.Split('/').Last();
        return name.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".partial", StringComparison.OrdinalIgnoreCase) ||
            name.EndsWith(".restore", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(name, "chapter-index.json", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasLinkedParent(string root, string file)
    {
        string? parent = Path.GetDirectoryName(file);
        while (parent is not null && !Path.GetFullPath(parent).Equals(
            Path.GetFullPath(root), StringComparison.OrdinalIgnoreCase))
        {
            if ((File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0) return true;
            parent = Path.GetDirectoryName(parent);
        }
        return false;
    }
}

internal sealed class StoryBackupManifest
{
    public int Format { get; set; } = 1;
    public string SourceProjectId { get; set; } = string.Empty;
    public DateTimeOffset CreatedUtc { get; set; }
    public List<StoryBackupFile> Files { get; set; } = new();
}

internal sealed class StoryBackupFile
{
    public string Path { get; set; } = string.Empty;
    public long Bytes { get; set; }
    public string Sha256 { get; set; } = string.Empty;
}

internal sealed record StoryBackupVerification(bool Valid, int Files, long Bytes);
internal sealed record StoryBackupResult(string Path, int Files, long Bytes);
