using System.Windows;
using System.Windows.Controls;
using WpfButton = System.Windows.Controls.Button;

namespace WorkChatMini;

public partial class StoryPanel
{
    private int _checkpointHistoryPage;
    private string? _checkpointHistoryProjectId;
    private void AttachRollbackAction(StackPanel content, StoryMessage message)
    {
        if (message.ManuscriptChapterNumber is not int number)
            return;
        StoryChapterMemory? chapter = _memory.Chapters.FirstOrDefault(c => c.Number == number);
        if (chapter is null || string.IsNullOrWhiteSpace(chapter.CheckpointId))
            return; // Chapters created before preview2 have no checkpoint to restore.

        var host = new StackPanel { Margin = new Thickness(0, 5, 0, 0) };
        var button = new WpfButton
        {
            Content = "Return before this chapter",
            Style = (Style)FindResource("StoryButton"),
            ToolTip = "Rewind chapters and Story Memory to the checkpoint before this commit. Preserves a safety version.",
            Padding = new Thickness(8, 4, 8, 4),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left
        };
        button.Click += (_, _) => ShowCheckpointConfirmation(host, chapter.CheckpointId, number);
        host.Children.Add(button);
        content.Children.Add(host);
    }

    private void InspectorVersionExpander_Expanded(object sender, RoutedEventArgs e) => RenderCheckpointHistory();

    private void RenderCheckpointHistory()
    {
        if (InspectorCheckpointList is null)
            return;
        InspectorCheckpointList.Children.Clear();
        if (_activeProject is null)
            return;
        if (_checkpointHistoryProjectId != _activeProject.Id)
        {
            _checkpointHistoryProjectId = _activeProject.Id;
            _checkpointHistoryPage = 0;
        }
        RenderBranchControls(InspectorCheckpointList);
        IReadOnlyList<StoryCheckpoint> versions = _checkpoints.ListRecent(_activeProject.Id, 9,
            excludeBranchNavigation: true, skip: _checkpointHistoryPage * 8);
        if (versions.Count == 0 && _checkpointHistoryPage > 0)
        {
            _checkpointHistoryPage = 0;
            versions = _checkpoints.ListRecent(_activeProject.Id, 9, excludeBranchNavigation: true);
        }
        if (versions.Count == 0)
        {
            InspectorCheckpointList.Children.Add(new TextBlock
            {
                Text = "A restore point will be created automatically before the next narrative commit.",
                Foreground = (System.Windows.Media.Brush)FindResource("StoryMuted"),
                TextWrapping = TextWrapping.Wrap
            });
        }
        foreach (StoryCheckpoint checkpoint in versions.Take(8))
        {
            var host = new StackPanel { Margin = new Thickness(0, 0, 0, 10) };
            host.Children.Add(new TextBlock
            {
                Text = $"{checkpoint.CreatedUtc.ToLocalTime():dd/MM HH:mm} · {checkpoint.ChapterCount} chapter(s) · {checkpoint.Reason}",
                FontSize = 10.5,
                TextWrapping = TextWrapping.Wrap,
                Foreground = (System.Windows.Media.Brush)FindResource("StorySub")
            });
            var restore = new WpfButton
            {
                Content = "Restore this version",
                Style = (Style)FindResource("StoryButton"),
                Padding = new Thickness(8, 4, 8, 4),
                Margin = new Thickness(0, 5, 0, 0),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left
            };
            restore.Click += (_, _) => ShowCheckpointConfirmation(host, checkpoint.Id, checkpoint.ChapterCount + 1);
            host.Children.Add(restore);
            InspectorCheckpointList.Children.Add(host);
        }
        if (_checkpointHistoryPage > 0 || versions.Count > 8)
        {
            var nav = new WrapPanel { Margin = new Thickness(0, 2, 0, 8) };
            if (_checkpointHistoryPage > 0)
            {
                var newer = CreateSmallBranchButton("Newer restore points");
                newer.Margin = new Thickness(0, 0, 7, 4);
                newer.Click += (_, _) => { _checkpointHistoryPage--; RenderCheckpointHistory(); };
                nav.Children.Add(newer);
            }
            if (versions.Count > 8)
            {
                var older = CreateSmallBranchButton("Older restore points");
                older.Click += (_, _) => { _checkpointHistoryPage++; RenderCheckpointHistory(); };
                nav.Children.Add(older);
            }
            InspectorCheckpointList.Children.Add(nav);
        }
        RenderStorageHealth(InspectorCheckpointList);
    }

    private void ShowCheckpointConfirmation(StackPanel host, string checkpointId, int fromChapter)
    {
        if (_activeProject is null || _generationCts is not null || _checkpointRecoveryFailed || _storageAuditBusy || _creativeDecisionBusy)
            return;
        host.Children.Clear();
        host.Children.Add(new TextBlock
        {
            Text = $"Restore story to before chapter {fromChapter}? Later active chapters and memory changes will be rewound. The current version will be saved as a recoverable checkpoint.",
            Foreground = (System.Windows.Media.Brush)FindResource("StorySub"),
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 4, 0, 8)
        });
        var row = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        var cancel = new WpfButton { Content = "Cancel", Style = (Style)FindResource("StoryButton") };
        cancel.Click += (_, _) =>
        {
            RenderConversation();
            if (InspectorPanel.Visibility == Visibility.Visible)
                RenderMemoryInspector(_activeInspectorSection ?? "Overview");
        };
        var confirm = new WpfButton
        {
            Content = "Restore version",
            Style = (Style)FindResource("StoryDangerButton"),
            Margin = new Thickness(8, 0, 0, 0)
        };
        confirm.Click += async (_, _) => await RestoreCheckpointUiAsync(checkpointId);
        row.Children.Add(cancel);
        row.Children.Add(confirm);
        host.Children.Add(row);
    }

    private async Task RestoreCheckpointUiAsync(string checkpointId)
    {
        if (_activeProject is null || _generationCts is not null || _checkpointRecoveryFailed || _storageAuditBusy || _creativeDecisionBusy)
            return;
        StoryProject project = _activeProject;
        _generationCts = new CancellationTokenSource();
        SetGeneratingState(true);
        ProjectBox.IsEnabled = false;
        try
        {
            SetStatus("Restoring Story Memory and chapters…");
            string safetyId = await _checkpoints.RestoreAsync(project.Id, checkpointId, _memory, _messages);
            await ActivateProjectAsync(project);
            if (InspectorPanel.Visibility == Visibility.Visible && InspectorMemoryView.Visibility == Visibility.Visible)
                RenderMemoryInspector(_activeInspectorSection ?? "Overview");
            SetStatus("Version restored · previous state preserved in Story Memory → Versions.");
            AppLogger.Info("STORY-CHECKPOINT", $"UI restore completed | project={project.Id} | safety={safetyId}");
        }
        catch (Exception ex)
        {
            AppLogger.Error("STORY-CHECKPOINT", "Restore failed; recovery will be retried on story load", ex);
            SetStatus("Error · Restore interrupted. Reopen the story to recover from the journal.");
        }
        finally
        {
            ProjectBox.IsEnabled = true;
            _generationCts?.Dispose();
            _generationCts = null;
            SetGeneratingState(false);
        }
    }
}
