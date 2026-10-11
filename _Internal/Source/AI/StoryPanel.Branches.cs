using System.Windows;
using System.Windows.Controls;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBox = System.Windows.Controls.TextBox;

namespace WorkChatMini;

public partial class StoryPanel
{
    private readonly StoryBranchStore _branches = new();

    private void RenderBranchControls(StackPanel container)
    {
        var title = new TextBlock
        {
            Text = "STORY BRANCHES",
            Foreground = (System.Windows.Media.Brush)FindResource("StoryGold"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 7)
        };
        container.Children.Add(title);

        StoryBranchIndex? index;
        try { index = _activeProject is null ? null : _branches.Load(_activeProject.Id); }
        catch (Exception ex)
        {
            AppLogger.Error("STORY-BRANCH", "Branch index needs manual recovery", ex);
            container.Children.Add(new TextBlock
            {
                Text = "Branch index is unreadable. Switching disabled; see Logs. No story files were changed.",
                Foreground = (System.Windows.Media.Brush)FindResource("StorySub"),
                TextWrapping = TextWrapping.Wrap
            });
            return;
        }

        if (index is null)
            container.Children.Add(new TextBlock
            {
                Text = "Create your first alternate direction. Your existing story becomes Main; no chapter will be duplicated.",
                Foreground = (System.Windows.Media.Brush)FindResource("StorySub"),
                TextWrapping = TextWrapping.Wrap,
                FontSize = 10.5,
                Margin = new Thickness(0, 0, 0, 8)
            });
        else
        {
            foreach (StoryBranchInfo branch in index.Branches.OrderBy(b => b.CreatedUtc))
            {
                bool active = branch.Id == index.ActiveBranchId;
                var line = new StackPanel { Margin = new Thickness(0, 0, 0, 8) };
                var nameLine = new TextBlock
                {
                    Text = (active ? "● " : "○ ") + branch.Name + (active ? "  · active" : ""),
                    Foreground = (System.Windows.Media.Brush)FindResource(active ? "StoryGold" : "StoryText"),
                    FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
                    TextWrapping = TextWrapping.Wrap
                };
                line.Children.Add(nameLine);
                var actions = new WrapPanel { Margin = new Thickness(0, 3, 0, 0) };
                if (!active)
                {
                    var switchButton = CreateSmallBranchButton("Switch");
                    switchButton.Margin = new Thickness(0, 0, 6, 6);
                    switchButton.Click += (_, _) => ShowBranchSwitchConfirmation(line, branch);
                    actions.Children.Add(switchButton);
                    var compareButton = CreateSmallBranchButton("Compare");
                    compareButton.Margin = new Thickness(0, 0, 6, 6);
                    compareButton.Click += async (_, _) => await ShowBranchComparisonAsync(line, branch);
                    actions.Children.Add(compareButton);
                }
                var renameButton = CreateSmallBranchButton("Rename");
                renameButton.Margin = new Thickness(0, 0, 6, 6);
                renameButton.Click += (_, _) => ShowBranchRenameForm(line, branch);
                actions.Children.Add(renameButton);
                if (!active)
                {
                    var archiveButton = CreateSmallBranchButton("Archive");
                    archiveButton.Margin = new Thickness(0, 0, 6, 6);
                    archiveButton.Click += (_, _) => ShowBranchArchiveConfirmation(line, branch);
                    actions.Children.Add(archiveButton);
                }
                line.Children.Add(actions);
                container.Children.Add(line);
            }
        }

        var createRow = new StackPanel { Margin = new Thickness(0, 8, 0, 12) };
        var input = new WpfTextBox
        {
            Style = (Style)FindResource("StoryTextBox"),
            MaxLength = 60,
            Text = "",
            ToolTip = "A new branch starts from the current chapters and memory.",
            MinHeight = 32
        };
        createRow.Children.Add(new TextBlock
        {
            Text = "Branch name",
            Foreground = (System.Windows.Media.Brush)FindResource("StorySub"),
            FontSize = 10.5,
            Margin = new Thickness(0, 0, 0, 4)
        });
        createRow.Children.Add(input);
        var create = CreateSmallBranchButton("Create alternate branch");
        create.Margin = new Thickness(0, 6, 0, 0);
        create.Click += async (_, _) => await RunBranchOperationAsync(async project =>
        {
            await _branches.CreateBranchAsync(project.Id, _memory, _messages, input.Text);
            return "Branch created from the current story. Switch when you want to write a different direction.";
        }, refreshStory: false);
        createRow.Children.Add(create);
        container.Children.Add(createRow);
        if (index is not null && index.ArchivedBranches.Count > 0)
            RenderArchivedBranchControls(container, index);
        container.Children.Add(new System.Windows.Controls.Separator
        {
            Margin = new Thickness(0, 0, 0, 12),
            Background = (System.Windows.Media.Brush)FindResource("StoryDivider")
        });
        container.Children.Add(new TextBlock
        {
            Text = "RESTORE POINTS",
            Foreground = (System.Windows.Media.Brush)FindResource("StoryGold"),
            FontWeight = FontWeights.SemiBold,
            Margin = new Thickness(0, 0, 0, 8)
        });
    }

    private WpfButton CreateSmallBranchButton(string label) => new()
    {
        Content = label,
        Style = (Style)FindResource("StoryButton"),
        Padding = new Thickness(8, 4, 8, 4),
        HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
        MinHeight = 27
    };

    private void ShowBranchSwitchConfirmation(StackPanel host, StoryBranchInfo branch)
    {
        if (_generationCts is not null || _checkpointRecoveryFailed)
            return;
        host.Children.Clear();
        host.Children.Add(new TextBlock
        {
            Text = $"Switch to '{branch.Name}'? Current chapters, conversation and memory will be saved to your active branch first.",
            TextWrapping = TextWrapping.Wrap,
            Foreground = (System.Windows.Media.Brush)FindResource("StorySub"),
            Margin = new Thickness(0, 0, 0, 8)
        });
        var buttons = new StackPanel { Orientation = System.Windows.Controls.Orientation.Horizontal };
        var cancel = CreateSmallBranchButton("Cancel");
        cancel.Click += (_, _) => RenderCheckpointHistory();
        buttons.Children.Add(cancel);
        var confirm = CreateSmallBranchButton("Switch branch");
        confirm.Margin = new Thickness(7, 0, 0, 0);
        confirm.Click += async (_, _) => await RunBranchOperationAsync(async project =>
        {
            await _branches.SwitchAsync(project.Id, branch.Id, _memory, _messages);
            return $"Switched to {branch.Name}. Story context now uses only this branch.";
        }, refreshStory: true);
        buttons.Children.Add(confirm);
        host.Children.Add(buttons);
    }

    private void ShowBranchRenameForm(StackPanel host, StoryBranchInfo branch)
    {
        if (_generationCts is not null || _checkpointRecoveryFailed)
            return;
        host.Children.Clear();
        var input = new WpfTextBox
        {
            Style = (Style)FindResource("StoryTextBox"),
            Text = branch.Name,
            MaxLength = 60
        };
        host.Children.Add(input);
        var buttons = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Margin = new Thickness(0, 6, 0, 0)
        };
        var cancel = CreateSmallBranchButton("Cancel");
        cancel.Click += (_, _) => RenderCheckpointHistory();
        buttons.Children.Add(cancel);
        var save = CreateSmallBranchButton("Save name");
        save.Margin = new Thickness(7, 0, 0, 0);
        save.Click += async (_, _) => await RunBranchOperationAsync(async project =>
        {
            await _branches.RenameBranchAsync(project.Id, branch.Id, input.Text);
            return "Branch renamed.";
        }, refreshStory: false);
        buttons.Children.Add(save);
        host.Children.Add(buttons);
        input.Focus();
        input.SelectAll();
    }

    private void RenderArchivedBranchControls(StackPanel container, StoryBranchIndex index)
    {
        var content = new StackPanel { Margin = new Thickness(0, 5, 0, 5) };
        foreach (StoryBranchInfo archived in index.ArchivedBranches.OrderByDescending(b => b.ArchivedUtc))
        {
            var line = new StackPanel { Margin = new Thickness(0, 0, 0, 12) };
            line.Children.Add(new TextBlock
            {
                Text = archived.Name + " · archived " +
                    (archived.ArchivedUtc is DateTimeOffset date
                        ? date.ToLocalTime().ToString("dd/MM/yyyy") : "unknown"),
                Foreground = (System.Windows.Media.Brush)FindResource("StorySub"),
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 4)
            });
            var restore = CreateSmallBranchButton("Restore to branch list");
            restore.Click += (_, _) => ShowArchivedBranchRestoreConfirmation(line, archived);
            line.Children.Add(restore);
            content.Children.Add(line);
        }
        container.Children.Add(new Expander
        {
            Header = $"ARCHIVED BRANCHES ({index.ArchivedBranches.Count})",
            Foreground = (System.Windows.Media.Brush)FindResource("StoryGold"),
            Content = content,
            Margin = new Thickness(0, 0, 0, 12)
        });
    }

    private void ShowArchivedBranchRestoreConfirmation(StackPanel host, StoryBranchInfo branch)
    {
        if (_generationCts is not null || _checkpointRecoveryFailed)
            return;
        host.Children.Clear();
        host.Children.Add(BranchInfoText($"Restore '{branch.Name}' to the branch list? This will not switch the active story."));
        AddBranchConfirmButtons(host, "Restore branch", async project =>
        {
            await _branches.RestoreArchivedBranchAsync(project.Id, branch.Id);
            return "Archived branch restored. Your active story was not changed.";
        });
    }

    private void ShowBranchArchiveConfirmation(StackPanel host, StoryBranchInfo branch)
    {
        if (_generationCts is not null || _checkpointRecoveryFailed)
            return;
        host.Children.Clear();
        host.Children.Add(BranchInfoText($"Archive '{branch.Name}'? You can restore it later. " +
            "No chapters, snapshots or memory are deleted. The active branch will remain unchanged."));
        AddBranchConfirmButtons(host, "Archive branch", async project =>
        {
            await _branches.ArchiveBranchAsync(project.Id, branch.Id);
            return "Branch archived without deleting story data.";
        });
    }

    private void AddBranchConfirmButtons(StackPanel host, string buttonLabel,
        Func<StoryProject, Task<string>> operation)
    {
        var buttons = new WrapPanel { Margin = new Thickness(0, 7, 0, 0) };
        var cancel = CreateSmallBranchButton("Cancel");
        cancel.Margin = new Thickness(0, 0, 6, 4);
        cancel.Click += (_, _) => RenderCheckpointHistory();
        buttons.Children.Add(cancel);
        var confirm = CreateSmallBranchButton(buttonLabel);
        confirm.Click += async (_, _) => await RunBranchOperationAsync(operation, refreshStory: false);
        buttons.Children.Add(confirm);
        host.Children.Add(buttons);
    }

    private TextBlock BranchInfoText(string text) => new()
    {
        Text = text,
        Foreground = (System.Windows.Media.Brush)FindResource("StorySub"),
        TextWrapping = TextWrapping.Wrap,
        Margin = new Thickness(0, 0, 0, 5)
    };

    private async Task ShowBranchComparisonAsync(StackPanel host, StoryBranchInfo branch)
    {
        if (_activeProject is null || _generationCts is not null || _checkpointRecoveryFailed)
            return;
        string projectId = _activeProject.Id;
        StoryBranchIndex? index = _branches.Load(projectId);
        if (index is null || index.ActiveBranchId == branch.Id)
            return;
        string currentName = index.Branches.Single(b => b.Id == index.ActiveBranchId).Name;
        host.Children.Clear();
        host.Children.Add(BranchInfoText("Comparing saved branch memory…"));
        try
        {
            StoryMemoryDocument target = await _checkpoints.ReadMemorySnapshotAsync(projectId,
                branch.HeadCheckpointId);
            // UI could have navigated to another story while disk I/O was in progress.
            if (_activeProject?.Id != projectId || _generationCts is not null ||
                _branches.Load(projectId)?.ActiveBranchId != index.ActiveBranchId)
                return;
            host.Children.Clear();
            host.Children.Add(new TextBlock
            {
                Text = $"BRANCH COMPARISON · {branch.Name}",
                Foreground = (System.Windows.Media.Brush)FindResource("StoryGold"),
                FontWeight = FontWeights.SemiBold,
                Margin = new Thickness(0, 0, 0, 7)
            });
            foreach (string detail in StoryBranchComparison.Describe(_memory, target, currentName, branch.Name))
                host.Children.Add(BranchInfoText(detail));
        }
        catch (Exception ex)
        {
            AppLogger.Error("STORY-BRANCH", "Read-only branch comparison failed", ex);
            host.Children.Clear();
            host.Children.Add(BranchInfoText("This branch cannot be compared. Check the Logs; no story data was changed."));
        }
        var back = CreateSmallBranchButton("Back to branches");
        back.Margin = new Thickness(0, 7, 0, 0);
        back.Click += (_, _) => RenderCheckpointHistory();
        host.Children.Add(back);
    }

    private async Task RunBranchOperationAsync(Func<StoryProject, Task<string>> operation, bool refreshStory)
    {
        if (_activeProject is null || _generationCts is not null || _checkpointRecoveryFailed ||
            _storageAuditBusy || _creativeDecisionBusy)
            return;
        StoryProject project = _activeProject;
        _generationCts = new CancellationTokenSource();
        ProjectBox.IsEnabled = false;
        SetGeneratingState(true);
        try
        {
            SetStatus(refreshStory ? "Switching story branch…" : "Updating story branches…");
            string message = await operation(project);
            if (refreshStory)
            {
                await ActivateProjectAsync(project);
                if (_checkpointRecoveryFailed)
                    throw new System.IO.IOException("Branch recovery was not completed.");
            }
            if (InspectorPanel.Visibility == Visibility.Visible && InspectorMemoryView.Visibility == Visibility.Visible)
                RenderMemoryInspector(_activeInspectorSection ?? "Overview");
            SetStatus(message);
        }
        catch (Exception ex)
        {
            AppLogger.Error("STORY-BRANCH", "Branch operation failed", ex);
            // A pending switch can be resumed on the next project load. Do not allow
            // further writes until recovery has either finished or been inspected.
            try
            {
                if (_branches.Load(project.Id)?.PendingSwitch is not null)
                    _checkpointRecoveryFailed = true;
            }
            catch (Exception) { _checkpointRecoveryFailed = true; }
            SetStatus("Error · Branch change failed. See Logs; reopen the story if recovery is needed.");
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
