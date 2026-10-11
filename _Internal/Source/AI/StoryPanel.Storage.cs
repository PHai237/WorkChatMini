using System.Windows;
using System.Windows.Controls;

namespace WorkChatMini;

public partial class StoryPanel
{
    private readonly StoryCheckpointStorageAudit _storageAudit = new();
    private readonly StoryCheckpointIntegrityAudit _integrityAudit = new();
    private bool _storageAuditBusy;

    private void RenderStorageHealth(StackPanel container)
    {
        container.Children.Add(new System.Windows.Controls.Separator
        {
            Margin = new Thickness(2, 7, 2, 12),
            Background = (System.Windows.Media.Brush)FindResource("StoryDivider")
        });
        container.Children.Add(new TextBlock
        {
            Text = "STORAGE HEALTH",
            Foreground = (System.Windows.Media.Brush)FindResource("StoryGold"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 6)
        });
        container.Children.Add(new TextBlock
        {
            Text = "Inspect checkpoints and storage without changing or deleting story files.",
            Foreground = (System.Windows.Media.Brush)FindResource("StorySub"),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 10.5,
            Margin = new Thickness(0, 0, 0, 7)
        });
        var result = new StackPanel { Margin = new Thickness(0, 7, 0, 4) };
        var button = CreateSmallBranchButton("Analyze storage (read-only)");
        button.Click += async (_, _) =>
        {
            if (_activeProject is null || _generationCts is not null || _storageAuditBusy) return;
            string projectId = _activeProject.Id;
            _storageAuditBusy = true;
            button.IsEnabled = false;
            SetGeneratingState(false);
            result.Children.Clear();
            AddStorageLine(result, "Checking local checkpoint metadata…");
            try
            {
                StoryStorageReport report = await Task.Run(() => _storageAudit.Analyze(projectId));
                StoryLiveConsistencyReport live = await StoryLiveConsistencyAudit.AnalyzeAsync(projectId);
                if (_activeProject?.Id != projectId) return;
                result.Children.Clear();
                AddStorageLine(result, $"Checkpoints: {report.CompleteCheckpoints} complete, {report.IncompleteCheckpoints} incomplete · {FormatBytes(report.CheckpointBytes)}");
                AddStorageLine(result, $"Branches: {report.ActiveBranches} active, {report.ArchivedBranches} archived");
                AddStorageLine(result, $"Chapter objects: {report.ObjectCount} · {FormatBytes(report.ObjectBytes)} · {report.ReferencedObjects} referenced");
                AddStorageLine(result, $"Live manuscript: {FormatBytes(report.LiveChapterBytes)} · temporary leftovers: {report.TemporaryEntries} item(s), {FormatBytes(report.TemporaryBytes)}");
                if (report.RescueGroups > 0)
                    AddStorageLine(result, $"Protected recovery files: {report.RescueGroups} group(s), {FormatBytes(report.RescueBytes)}. Retained under branches/recovered; review manually, not auto-cleaned.");
                if (report.ReferenceScanComplete)
                    AddStorageLine(result, $"Objects not referenced by readable checkpoints: {report.PossiblyUnreferencedObjects} ({FormatBytes(report.PossiblyUnreferencedBytes)}). NOT a deletion recommendation.");
                else
                    AddStorageLine(result, "Reference inventory is incomplete; unused-space estimates are suppressed.");
                if (report.HasPendingOperation || report.InvalidBranchIndex ||
                    report.UnresolvedBranchHeads > 0 || report.MissingReferencedObjects > 0 ||
                    report.IncompleteCheckpoints > 0 || report.UnrecognizedObjectFiles > 0)
                    AddStorageLine(result, $"Review needed: pending={report.HasPendingOperation}, invalid index={report.InvalidBranchIndex}, missing branch heads={report.UnresolvedBranchHeads}, missing objects={report.MissingReferencedObjects}, incomplete checkpoints={report.IncompleteCheckpoints}, unknown object files={report.UnrecognizedObjectFiles}.");
                AddStorageLine(result, $"Live manuscript: {live.ReferencedChapters} chapters, {live.MissingFiles} missing, {live.EmptyFiles} empty, {live.DuplicateReferences} duplicates, {live.InvalidNames} unsafe names, {live.UnlistedFiles} unlisted files.");
                foreach (string issue in live.Issues)
                    AddStorageLine(result, issue);
                if (live.IssueCount > live.Issues.Count)
                    AddStorageLine(result, $"…and {live.IssueCount - live.Issues.Count} more live chapter issues.");
                AddStorageLine(result, "Read-only scan; active chapter file contents were not cryptographically verified. Nothing was removed.");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("STORY-STORAGE", $"Read-only inventory failed | {ex.Message}");
                result.Children.Clear();
                AddStorageLine(result, "Storage analysis could not finish. See Logs; no files were changed.");
            }
            finally { button.IsEnabled = true; _storageAuditBusy = false; SetGeneratingState(false); }
        };
        container.Children.Add(button);
        container.Children.Add(result);

        var integrityResult = new StackPanel { Margin = new Thickness(0, 7, 0, 4) };
        var verify = CreateSmallBranchButton("Verify checkpoint integrity (read-only)");
        verify.Margin = new Thickness(0, 8, 0, 0);
        verify.ToolTip = "Read checkpoint JSON, decompress snapshots, verify SHA-256 of referenced chapter objects. May take time for long stories.";
        verify.Click += async (_, _) =>
        {
            if (_activeProject is null || _generationCts is not null || _storageAuditBusy) return;
            string projectId = _activeProject.Id;
            _storageAuditBusy = true;
            verify.IsEnabled = false;
            SetGeneratingState(false);
            integrityResult.Children.Clear();
            AddStorageLine(integrityResult, "Verifying checkpoint contents and chapter hashes…");
            try
            {
                StoryIntegrityReport report = await Task.Run(async () =>
                    await _integrityAudit.VerifyAsync(projectId));
                if (_activeProject?.Id != projectId) return;
                integrityResult.Children.Clear();
                if (report.CheckpointsChecked == 0)
                    AddStorageLine(integrityResult, "No checkpoints available for deep verification.");
                AddStorageLine(integrityResult, $"Deep verification: {report.CheckpointsValid}/{report.CheckpointsChecked} checkpoints · {report.ObjectsChecked} unique chapter objects checked.");
                if (report.LegacyCheckpoints > 0)
                    AddStorageLine(integrityResult, $"Legacy plaintext snapshots: {report.LegacyCheckpoints}; their structure was checked, but they have no original checksum.");
                AddStorageLine(integrityResult, report.Verified
                    ? "Verified: no damage detected in the checked checkpoint content."
                    : $"Review needed: {report.CheckpointsDamaged} damaged checkpoints, {report.ObjectsDamaged} damaged/missing objects, {report.UnrecoverableBranchHeads} unverified branch heads.");
                foreach (string issue in report.Issues)
                    AddStorageLine(integrityResult, issue);
                if (report.HiddenIssues > 0)
                    AddStorageLine(integrityResult, $"…and {report.HiddenIssues} more issues. No files were changed.");
                AddStorageLine(integrityResult, "Read-only snapshot of storage at scan time; no automatic cleanup or repair.");
            }
            catch (Exception ex)
            {
                AppLogger.Warn("STORY-STORAGE", $"Deep checkpoint verification failed | {ex.Message}");
                integrityResult.Children.Clear();
                AddStorageLine(integrityResult, "Deep verification could not finish. See Logs; no files were changed.");
            }
            finally { verify.IsEnabled = true; _storageAuditBusy = false; SetGeneratingState(false); }
        };
        container.Children.Add(verify);
        container.Children.Add(integrityResult);

        // One explicit portable backup includes the working manuscript, Creative
        // Ideas, branch pointers and their content-addressed checkpoint objects.
        // This is deliberately not an automatic restore/import operation.
        var backupResult = new StackPanel { Margin = new Thickness(0, 9, 0, 4) };
        var backup = CreateSmallBranchButton("Create verified Story backup (ZIP)");
        backup.ToolTip = "Create a complete local ZIP under WorkChatMini/AI/Backups; does not alter the Story or store API keys.";
        backup.Click += async (_, _) =>
        {
            if (_activeProject is null || _generationCts is not null || _storageAuditBusy ||
                _creativeDecisionBusy || _loadingProject || _checkpointRecoveryFailed) return;
            string projectId = _activeProject.Id;
            _storageAuditBusy = true;
            backup.IsEnabled = false;
            ProjectBox.IsEnabled = false;
            SetGeneratingState(false);
            backupResult.Children.Clear();
            AddStorageLine(backupResult, "Creating and verifying a portable Story backup…");
            try
            {
                StoryBackupResult report = await Task.Run(() => new StoryBackupService().CreateAsync(projectId));
                if (_activeProject?.Id != projectId) return;
                backupResult.Children.Clear();
                AddStorageLine(backupResult, $"Verified backup: {report.Files} files, {FormatBytes(report.Bytes)} (uncompressed).");
                AddStorageLine(backupResult, report.Path);
                AddStorageLine(backupResult, "Keep this file in a safe place. No Story data was restored, replaced or deleted.");
                AppLogger.Info("STORY-BACKUP", $"Portable backup created | project={projectId} | files={report.Files}");
            }
            catch (Exception ex)
            {
                AppLogger.Error("STORY-BACKUP", "Portable Story backup failed; original files unchanged", ex);
                backupResult.Children.Clear();
                AddStorageLine(backupResult, "Backup failed; no completed ZIP was published. Original Story files were unchanged. See Logs.");
            }
            finally
            {
                _storageAuditBusy = false;
                backup.IsEnabled = true;
                SetGeneratingState(false);
            }
        };
        container.Children.Add(backup);
        container.Children.Add(backupResult);
        RenderBackupImport(container);
    }

    private void AddStorageLine(StackPanel panel, string value) => panel.Children.Add(new TextBlock
    {
        Text = value,
        Foreground = (System.Windows.Media.Brush)FindResource("StorySub"),
        TextWrapping = TextWrapping.Wrap,
        FontSize = 10.5,
        Margin = new Thickness(0, 0, 0, 5)
    });

    private static string FormatBytes(long bytes) => bytes >= 1024L * 1024L
        ? $"{bytes / (1024d * 1024d):0.0} MiB"
        : $"{bytes / 1024d:0.0} KiB";
}
