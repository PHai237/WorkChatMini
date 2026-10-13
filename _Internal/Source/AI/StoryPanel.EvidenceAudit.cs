using System.Windows;

namespace WorkChatMini;

public partial class StoryPanel
{
    private bool _evidenceAuditBusy;

    private async void InspectorEvidenceAuditButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeProject is null || _evidenceAuditBusy || _generationCts is not null ||
            _loadingProject || _storageAuditBusy || _checkpointRecoveryFailed) return;

        // Capture a bounded, immutable source list before asynchronous IO.
        StoryMemoryDocument baseline = _memory;
        StoryEvidenceAudit.Snapshot snapshot = StoryEvidenceAudit.Capture(baseline);
        string projectId = _activeProject.Id;
        _evidenceAuditBusy = true;
        InspectorEvidenceAuditButton.IsEnabled = false;
        try
        {
            SetStatus("Checking committed chapter evidence (read-only)…");
            StoryEvidenceAudit.Report report = await Task.Run(() =>
                StoryEvidenceAudit.InspectAsync(_memoryStore, projectId, snapshot, CancellationToken.None));
            if (_activeProject?.Id != projectId || !ReferenceEquals(_memory, baseline)) return;
            // Another panel may have opened while the audit read chapter files.
            if (InspectorPanel.Visibility == Visibility.Visible &&
                string.Equals(_activeInspectorSection, "Overview", StringComparison.OrdinalIgnoreCase))
            {
                InspectorMemoryHeading.Text = "Source audit · read-only";
                InspectorMemoryBodyText.Inlines.Clear();
                InspectorMemoryBodyText.Text = report.Display();
            }
            SetStatus(report.StopReason is null
                ? $"Evidence audit checked {report.CheckedClaims}/{report.TotalClaims} entries. No Story data changed."
                : "Evidence audit could not safely read the chapter ledger. No Story data changed.");
            AppLogger.Info("STORY-MEMORY", $"Source audit | project={projectId} | checked={report.CheckedClaims} | chaptersRead={report.ChaptersRead} | noWrites=true");
        }
        catch (Exception ex)
        {
            AppLogger.Error("STORY-MEMORY", "Read-only source audit failed", ex);
            if (_activeProject?.Id == projectId)
                SetStatus("Source audit could not finish. No Story data changed; see Logs.");
        }
        finally
        {
            _evidenceAuditBusy = false;
            InspectorEvidenceAuditButton.IsEnabled = _generationCts is null &&
                !_loadingProject && !_storageAuditBusy && !_checkpointRecoveryFailed;
        }
    }
}
