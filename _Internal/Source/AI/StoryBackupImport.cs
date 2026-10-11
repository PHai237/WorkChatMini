using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace WorkChatMini;

internal sealed partial class StoryBackupService
{
    // Import is deliberately *copy-only*. Existing project IDs are never used as
    // destinations, and the original ZIP and current Story are never modified.
    // Publish happens via a same-volume directory rename after full verification.
    internal async Task<StoryProject> ImportAsNewStoryAsync(string backupPath,
        CancellationToken cancellationToken = default)
    {
        StoryBackupVerification verified = await VerifyAsync(backupPath, cancellationToken);
        if (!verified.Valid || verified.Files < 1)
            throw new InvalidDataException("Backup is incomplete.");

        string newId = Guid.NewGuid().ToString("N");
        string destination = _projects.GetProjectDirectory(newId);
        string stagingRoot = Path.Combine(AppSettings.AppDataDirectory, "AI", "ImportStaging");
        string staging = Path.Combine(stagingRoot, newId);
        if (Directory.Exists(destination) || Directory.Exists(staging))
            throw new IOException("A Story import destination already exists.");
        Directory.CreateDirectory(stagingRoot);
        try
        {
            Directory.CreateDirectory(staging);
            using var archive = ZipFile.OpenRead(backupPath);
            ZipArchiveEntry metadata = archive.GetEntry(ManifestEntry)
                ?? throw new InvalidDataException("Missing backup manifest.");
            StoryBackupManifest manifest;
            await using (Stream content = metadata.Open())
                manifest = await JsonSerializer.DeserializeAsync<StoryBackupManifest>(
                    content, Options, cancellationToken)
                    ?? throw new InvalidDataException("Unreadable backup manifest.");
            if (manifest.Format != 1 || !IsProjectId(manifest.SourceProjectId) || manifest.Files is null ||
                manifest.Files.Count != verified.Files)
                throw new InvalidDataException("Unsupported backup project or manifest.");

            long extractedBytes = 0;
            foreach (StoryBackupFile item in manifest.Files)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!item.Path.StartsWith("story/", StringComparison.Ordinal) ||
                    !IsSafeImportPath(item.Path[6..]) || IsTransient(item.Path))
                    throw new InvalidDataException("Backup contains a path that cannot safely be imported.");
                if (item.Path.StartsWith("story/branches/pending-rescues/", StringComparison.OrdinalIgnoreCase) ||
                    item.Path.Equals("story/branches/pending-restore.json", StringComparison.OrdinalIgnoreCase) ||
                    item.Path.Equals("story/branches/pending-commit.json", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Backup contains an unfinished Story transaction.");

                string relative = item.Path[6..].Replace('/', Path.DirectorySeparatorChar);
                string target = Path.Combine(staging, relative);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                ZipArchiveEntry entry = archive.GetEntry(item.Path)
                    ?? throw new InvalidDataException("Missing backup file.");
                await using Stream input = entry.Open();
                await using var output = new FileStream(target, FileMode.CreateNew, FileAccess.Write,
                    FileShare.None, 65536, FileOptions.Asynchronous);
                (long bytes, string digest) = await CopyAndHashAsync(input, output, cancellationToken);
                if (bytes != item.Bytes || !digest.Equals(item.Sha256, StringComparison.OrdinalIgnoreCase) ||
                    bytes > 512L * 1024 * 1024 || extractedBytes > 8L * 1024 * 1024 * 1024 - bytes)
                    throw new InvalidDataException("Imported backup content failed integrity or size verification.");
                extractedBytes += bytes;
            }
            if (extractedBytes != verified.Bytes)
                throw new InvalidDataException("Backup changed during import.");

            string projectPath = Path.Combine(staging, "project.json");
            StoryProject project = JsonSerializer.Deserialize<StoryProject>(
                await File.ReadAllTextAsync(projectPath, cancellationToken), Options)
                ?? throw new InvalidDataException("Story project metadata is missing.");
            if (project.Id != manifest.SourceProjectId)
                throw new InvalidDataException("Backup project ID does not match its manifest.");
            string conversation = Path.Combine(staging, "conversation.json");
            if (File.Exists(conversation))
                _ = StoryProjectStore.ParseConversationMessages(
                    await File.ReadAllTextAsync(conversation, cancellationToken));
            string memoryPath = Path.Combine(staging, "memory", "story-memory.json");
            if (File.Exists(memoryPath))
            {
                StoryMemoryDocument memory = StoryMemoryStore.ParseMemoryDocument(
                    await File.ReadAllTextAsync(memoryPath, cancellationToken));
                // Reject a syntactically valid ZIP whose live Memory has colliding
                // chapter IDs or filenames; do not activate ambiguous canon.
                StoryCheckpointChapterGuard.ValidateLiveChapters(memory);
                foreach (StoryChapterMemory chapter in memory.Chapters)
                {
                    if (!IsSafeImportPath(chapter.FileName) || chapter.FileName.Contains('/') ||
                        !File.Exists(Path.Combine(staging, "chapters", chapter.FileName)))
                        throw new InvalidDataException("Backup manuscript is missing a referenced chapter.");
                }
            }
            string branchIndex = Path.Combine(staging, "branches", "branch-index.json");
            if (File.Exists(branchIndex))
            {
                StoryBranchIndex? index = JsonSerializer.Deserialize<StoryBranchIndex>(
                    await File.ReadAllTextAsync(branchIndex, cancellationToken), Options);
                if (index is null)
                    throw new InvalidDataException("Backup branch index is empty.");
                // Validate the actual checkpoint payload/object relationships,
                // not just per-file ZIP SHA-256 checksums.
                await StoryPortableImportGuard.ValidateBranchHeadsAsync(staging, index, cancellationToken);
            }

            project.Id = newId;
            string title = string.IsNullOrWhiteSpace(project.Title) ? "Untitled Story" : project.Title.Trim();
            project.Title = title.Length > 90 ? title[..90] + " (restored)" : title + " (restored)";
            project.CreatedUtc = DateTimeOffset.UtcNow;
            project.UpdatedUtc = DateTimeOffset.UtcNow;
            await File.WriteAllTextAsync(projectPath, JsonSerializer.Serialize(project, Options), cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
            Directory.Move(staging, destination);
            AppLogger.Info("STORY-BACKUP", $"Imported verified backup as separate Story | project={newId} | files={verified.Files}");
            return project;
        }
        finally
        {
            // Staging belongs only to this operation; never delete the destination
            // or original source Story if import fails at any stage.
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    private static bool IsProjectId(string? id) =>
        id is { Length: 32 } && id.All(Uri.IsHexDigit);

    private static bool IsSafeImportPath(string relative)
    {
        if (!IsSafeRelativePath(relative)) return false;
        foreach (string segment in relative.Split('/'))
        {
            if (segment.EndsWith('.') || segment.EndsWith(' ') ||
                segment.Any(c => c < 32 || "<>:\"\\|?*".Contains(c))) return false;
            string root = segment.Split('.')[0];
            if (root.Equals("CON", StringComparison.OrdinalIgnoreCase) ||
                root.Equals("PRN", StringComparison.OrdinalIgnoreCase) ||
                root.Equals("AUX", StringComparison.OrdinalIgnoreCase) ||
                root.Equals("NUL", StringComparison.OrdinalIgnoreCase) ||
                (root.Length == 4 && (root.StartsWith("COM", StringComparison.OrdinalIgnoreCase) ||
                    root.StartsWith("LPT", StringComparison.OrdinalIgnoreCase)) &&
                    root[3] is >= '1' and <= '9')) return false;
        }
        return true;
    }
}
