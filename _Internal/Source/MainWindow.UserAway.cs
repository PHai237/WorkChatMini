namespace WorkChatMini;

public partial class MainWindow
{
    private void UpdateUserAwayState()
    {
        if (IsSystemAwakeActivityActive())
        {
            if (_userAwayMode || _awaySuspendedYouTube)
                HandleUserReturnFromAway("realtime-media");
            return;
        }

        if (!UserIdleDetector.TryGetIdleTime(out TimeSpan idle))
            return;

        bool away = idle >= UserAwayThreshold;
        if (away == _userAwayMode)
            return;

        _userAwayMode = away;
        if (away)
        {
            AppLogger.Info("RESOURCE", $"User away mode entered | systemIdleMinutes={idle.TotalMinutes:0.0}");
            RecordPerformanceEvent("AWAY", $"enter idleMin={idle.TotalMinutes:0.0}");
        }
        else
        {
            HandleUserReturnFromAway("idle-detector");
        }
    }

    private void MainWindow_UserMouseMove(object sender, System.Windows.Input.MouseEventArgs e)
    {
        if (_userAwayMode || _awaySuspendedYouTube)
            HandleUserReturnFromAway("mouse");
    }

    private void MainWindow_UserMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_userAwayMode || _awaySuspendedYouTube)
            HandleUserReturnFromAway("mouse-button");
    }

    private void MainWindow_UserKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (_userAwayMode || _awaySuspendedYouTube)
            HandleUserReturnFromAway("keyboard");
    }

    private void HandleUserReturnFromAway(string source)
    {
        bool wasAway = _userAwayMode;
        _userAwayMode = false;

        if (_awaySuspendedYouTube)
        {
            _awaySuspendedYouTube = false;
            WakeSiteIfNeeded(MiniSite.YouTube, markUsed: false);
            if (_activeSite == MiniSite.YouTube && IsWindowPresented)
                YouTubeWebView.Visibility = System.Windows.Visibility.Visible;
        }

        if (wasAway)
        {
            AppLogger.Info("RESOURCE", $"User away mode exited | source={source}");
            RecordPerformanceEvent("AWAY", $"exit source={source}");
        }
    }
}
