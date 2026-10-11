using System.Windows;
using System.Windows.Controls;

namespace WorkChatMini;

public partial class StoryPanel
{
    private void RenderBackupImport(StackPanel container)
    {
        var result = new StackPanel { Margin = new Thickness(0, 7, 0, 4) };
        var button = CreateSmallBranchButton("Restore backup as a new Story…");
        button.Margin = new Thickness(0, 8, 0, 0);
        button.ToolTip = "Choose a verified WorkChat Story ZIP. Restores a separate copy; never replaces the current Story.";
        button.Click += async (_, _) =>
        {
            if (_activeProject is null || _generationCts is not null || _storageAuditBusy ||
                _creativeDecisionBusy || _loadingProject || _checkpointRecoveryFailed) return;

            var picker = new Microsoft.Win32.OpenFileDialog
            {
                Title = "Choose a WorkChat Story backup",
                Filter = "Story backup ZIP (*.zip)|*.zip",
                CheckFileExists = true,
                Multiselect = false
            };
            if (picker.ShowDialog() != true) return;

            _storageAuditBusy = true;
            button.IsEnabled = false;
            SetGeneratingState(false);
            result.Children.Clear();
            AddStorageLine(result, "Verifying ZIP and creating an independent Story copy…");
            StoryProject? imported = null;
            try
            {
                imported = await new StoryBackupService().ImportAsNewStoryAsync(picker.FileName);
                result.Children.Clear();
                AddStorageLine(result, $"Verified backup restored as: {imported.Title}");
                AddStorageLine(result, "The previous Story and original ZIP were not changed.");
            }
            catch (Exception ex)
            {
                AppLogger.Error("STORY-BACKUP", "Story backup import was rejected; existing Stories preserved", ex);
                result.Children.Clear();
                AddStorageLine(result, "Import could not complete. Existing Stories were not replaced. See Logs.");
            }
            finally
            {
                _storageAuditBusy = false;
                button.IsEnabled = true;
                SetGeneratingState(false);
            }
            if (imported is not null)
            {
                _projects.Insert(0, imported);
                ProjectBox.ItemsSource = null;
                ProjectBox.ItemsSource = _projects;
                RebuildProjectList();
                ProjectBox.SelectedItem = imported;
            }
        };
        container.Children.Add(button);
        container.Children.Add(new TextBlock
        {
            Text = "Import always creates a separate Story. No overwriting, automatic merging, or import of API keys.",
            Foreground = (System.Windows.Media.Brush)FindResource("StorySub"),
            FontSize = 10.5,
            TextWrapping = TextWrapping.Wrap,
            Margin = new Thickness(0, 5, 0, 0)
        });
        container.Children.Add(result);
    }
}
