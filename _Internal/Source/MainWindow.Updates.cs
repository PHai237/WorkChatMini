using System.Windows;

namespace WorkChatMini;

public partial class MainWindow
{
    private AppUpdateWindow? _updateWindow;

    private void ShowUpdates()
    {
        if (AppRuntime.IsSelfTest) return;
        if (_updateWindow is { IsLoaded: true })
        {
            _updateWindow.Activate();
            return;
        }
        _updateWindow = new AppUpdateWindow(ExitApplication, GetUpdateBlocker) { Owner = this };
        _updateWindow.Closed += (_, _) => _updateWindow = null;
        _updateWindow.Show();
    }

    private string? GetUpdateBlocker()
    {
        if (_discordCallActive || IsSitePlayingAudio(MiniSite.Discord))
            return "A Discord call or Discord audio is active. End it before updating.";
        if (IsSitePlayingAudio(MiniSite.YouTube))
            return "YouTube audio/video is playing. Pause it before restarting for the update.";
        if (_browserTabs.Any(tab => tab.ActiveDownloadCount > 0))
            return "A Web download is active. Finish it before updating.";
        if (_storyInitialized && StoryPanel.HasPendingWorkForUpdate)
            return "Story has an AI request, storage operation or recovery in progress. Finish it before updating.";
        return null;
    }

    private void UpdatesMenuItem_Click(object sender, RoutedEventArgs e) => ShowUpdates();
}
