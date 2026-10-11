using System.IO;
using System.IO.Compression;
using System.Text.Json;

namespace WorkChatMini;

// Self-test creates only disposable ZIPs and never imports/deletes a real Story.
// Called inside the existing Story memory case to keep the 22-case contract.
internal static class StoryBackupSelfTest
{
    internal static async Task VerifyAsync(string projectId)
    {
        string directory = Path.Combine(Path.GetTempPath(), "WorkChatMini-BackupSelfTest-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        StoryProject? imported = null;
        try
        {
            // The export must wait for an in-process branch/rollback lease;
            // otherwise it might package multiple stages of one restore.
            Task<StoryBackupResult> export;
            using (await StoryOperationGate.EnterAsync(projectId))
            {
                export = new StoryBackupService().CreateAsync(projectId, directory);
                if (export.IsCompleted)
                    throw new InvalidOperationException("Backup ran while the Story mutation gate was held.");
            }
            StoryBackupResult result = await export;
            StoryBackupVerification verified = await StoryBackupService.VerifyAsync(result.Path);
            if (!verified.Valid || result.Files != verified.Files || result.Bytes != verified.Bytes ||
                verified.Files < 3 || !File.Exists(result.Path))
                throw new InvalidOperationException("Portable Story backup was not verified after export.");
            using (var archive = ZipFile.OpenRead(result.Path))
            {
                if (archive.GetEntry("story/project.json") is null ||
                    archive.GetEntry("story/conversation.json") is null ||
                    !archive.Entries.Any(entry => entry.FullName.StartsWith("story/branches/", StringComparison.Ordinal)))
                    throw new InvalidOperationException("Portable backup omitted core Story or checkpoint files.");
                if (archive.Entries.Any(entry => entry.FullName.Contains("chapter-index.json", StringComparison.OrdinalIgnoreCase) ||
                    entry.FullName.Contains(".tmp", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Portable backup accidentally captured disposable caches or temp files.");
            }
            // Byte-valid archives may still have dangling branch heads. A
            // missing branch checkpoint must fail BEFORE any import is published.
            string missingHead = Guid.NewGuid().ToString("N");
            string activeBranch = Guid.NewGuid().ToString("N");
            var dangling = new StoryBranchIndex
            {
                ActiveBranchId = activeBranch,
                Branches = new List<StoryBranchInfo>
                {
                    new() { Id = activeBranch, Name = "Main", HeadCheckpointId = missingHead }
                }
            };
            bool missingHeadRejected = false;
            try { await StoryPortableImportGuard.ValidateBranchHeadsAsync(directory, dangling); }
            catch (InvalidDataException) { missingHeadRejected = true; }
            if (!missingHeadRejected)
                throw new InvalidOperationException("Backup import accepted a missing branch checkpoint.");

            // A verified portable ZIP must restore as an independent project.
            imported = await new StoryBackupService().ImportAsNewStoryAsync(result.Path);
            var projectStore = new StoryProjectStore();
            if (imported.Id == projectId || !File.Exists(Path.Combine(
                projectStore.GetProjectDirectory(imported.Id), "project.json")) ||
                (await projectStore.LoadMessagesAsync(imported.Id)).Count !=
                (await projectStore.LoadMessagesAsync(projectId)).Count ||
                (await new StoryMemoryStore().LoadAsync(imported.Id)).Chapters.Count !=
                (await new StoryMemoryStore().LoadAsync(projectId)).Chapters.Count)
                throw new InvalidOperationException("Backup import did not preserve conversation and chapter metadata.");
            await projectStore.DeleteProjectAsync(imported.Id);
            imported = null;

            // Corrupt only the disposable archive manifest; the verifier must
            // reject a mismatched checksum without extracting or touching sources.
            using (var archive = ZipFile.Open(result.Path, ZipArchiveMode.Update))
            {
                ZipArchiveEntry entry = archive.GetEntry("backup-manifest.json")
                    ?? throw new InvalidDataException("Backup manifest not found.");
                StoryBackupManifest manifest;
                using (Stream input = entry.Open())
                    manifest = JsonSerializer.Deserialize<StoryBackupManifest>(input)
                        ?? throw new InvalidDataException("Backup manifest was empty.");
                entry.Delete();
                manifest.Files[0].Sha256 = new string('0', 64);
                using Stream output = archive.CreateEntry("backup-manifest.json").Open();
                JsonSerializer.Serialize(output, manifest);
            }
            bool rejected = false;
            try { await StoryBackupService.VerifyAsync(result.Path); }
            catch (InvalidDataException) { rejected = true; }
            if (!rejected)
                throw new InvalidOperationException("Backup verifier accepted modified archive contents.");

            // Chapter number collisions must preserve orphan files and never
            // overwrite text from a previous interrupted commit.
            string chapters = Path.Combine(directory, "chapters");
            Directory.CreateDirectory(chapters);
            string existing = Path.Combine(chapters, "0001-Old chapter.md");
            await File.WriteAllTextAsync(existing, "Original prose must survive.");
            bool collisionRejected = false;
            try
            {
                StoryMemoryStore.EnsureChapterSlotAvailable(chapters,
                    new StoryMemoryDocument(), 1, "0001-New chapter.md");
            }
            catch (IOException) { collisionRejected = true; }
            if (!collisionRejected || await File.ReadAllTextAsync(existing) != "Original prose must survive.")
                throw new InvalidOperationException("Chapter overwrite protection failed on an orphan chapter.");
            StoryMemoryStore.EnsureChapterSlotAvailable(chapters, new StoryMemoryDocument(), 2, "0002-New.md");
            var expected = new StoryMemoryDocument();
            expected.Chapters.Add(new StoryChapterMemory { Number = 1, FileName = "0001-Old chapter.md" });
            expected.Chapters.Add(new StoryChapterMemory { Number = 2, FileName = "0002-Missing.md" });
            expected.Chapters.Add(new StoryChapterMemory { Number = 2, FileName = "../unsafe.md" });
            StoryLiveConsistencyReport live = StoryLiveConsistencyAudit.Analyze(directory, expected);
            if (live.ReferencedChapters != 3 || live.MissingFiles != 1 ||
                live.InvalidNames != 1 || live.DuplicateReferences != 1 ||
                live.EmptyFiles != 0 || live.UnlistedFiles != 0)
                throw new InvalidOperationException("Read-only live manuscript diagnostics missed missing or invalid chapters.");
        }
        finally
        {
            if (imported is not null) await new StoryProjectStore().DeleteProjectAsync(imported.Id);
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }
}
