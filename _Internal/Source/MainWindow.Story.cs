using System.Windows;

namespace WorkChatMini;

public partial class MainWindow
{
    private bool _storyInitialized;

    private async Task EnsureStoryInitializedAsync()
    {
        if (_storyInitialized)
            return;
        await StoryPanel.InitializeAsync(_settings);
        _storyInitialized = true;
        AppLogger.Info("STORY", "Story Companion initialized");
    }

    private async void StoryButton_Click(object sender, RoutedEventArgs e) =>
        await SwitchToSiteSafeAsync(MiniSite.Story);
}
