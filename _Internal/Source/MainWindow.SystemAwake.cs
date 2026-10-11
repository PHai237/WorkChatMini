using Microsoft.Web.WebView2.Core;
using System.Runtime.InteropServices;

namespace WorkChatMini;

public partial class MainWindow
{
    private const string YouTubePlaybackMessagePrefix = "wcm-youtube-playback:";
    private bool _youtubePlaybackActive;
    private bool _systemAwakeRequestActive;

    [Flags]
    private enum ExecutionState : uint
    {
        SystemRequired = 0x00000001,
        DisplayRequired = 0x00000002,
        Continuous = 0x80000000
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern ExecutionState SetThreadExecutionState(ExecutionState esFlags);

    private async Task ConfigureYouTubePlaybackGuardAsync(CoreWebView2 core)
    {
        core.WebMessageReceived -= YouTubePlaybackGuard_WebMessageReceived;
        core.WebMessageReceived += YouTubePlaybackGuard_WebMessageReceived;
        await core.AddScriptToExecuteOnDocumentCreatedAsync(YouTubePlaybackGuardScript);
        AppLogger.Info("POWER", "YouTube playback guard installed | detector=html-video+webview-audio");
    }

    private void YouTubePlaybackGuard_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (sender is not CoreWebView2 core ||
            !_configuredCoreSites.TryGetValue(core, out MiniSite site) ||
            site != MiniSite.YouTube)
            return;

        string message;
        try
        {
            message = e.TryGetWebMessageAsString();
        }
        catch
        {
            return;
        }

        if (!message.StartsWith(YouTubePlaybackMessagePrefix, StringComparison.Ordinal))
            return;

        ReadOnlySpan<char> payload = message.AsSpan(YouTubePlaybackMessagePrefix.Length);
        bool active = payload.Length > 0 && payload[0] == '1';
        if (active == _youtubePlaybackActive)
            return;

        _youtubePlaybackActive = active;
        AppLogger.Info("POWER", $"YouTube playback state changed | active={active}");
        RecordPerformanceEvent("POWER", $"youtube playback active={active}");

        if (active && (_userAwayMode || _awaySuspendedYouTube))
            HandleUserReturnFromAway("youtube-playback");

        UpdateSystemAwakeGuard("youtube-playback");
    }

    private void ConfiguredCore_IsDocumentPlayingAudioChanged(object? sender, object e)
    {
        if (sender is not CoreWebView2 core ||
            !_configuredCoreSites.TryGetValue(core, out MiniSite site) ||
            site is not (MiniSite.YouTube or MiniSite.Discord))
            return;

        UpdateSystemAwakeGuard($"audio-{site}");
    }

    private void ResetYouTubePlaybackStateForNavigation()
    {
        if (!_youtubePlaybackActive)
            return;

        _youtubePlaybackActive = false;
        UpdateSystemAwakeGuard("youtube-navigation");
    }

    private bool IsYouTubePlaybackProtectionActive() =>
        _youtubePlaybackActive || IsSitePlayingAudio(MiniSite.YouTube);

    private bool IsDiscordPowerActivityActive() =>
        _discordCallActive || IsSitePlayingAudio(MiniSite.Discord);

    private bool IsSystemAwakeActivityActive() =>
        IsYouTubePlaybackProtectionActive() || IsDiscordPowerActivityActive();

    private void UpdateSystemAwakeGuard(string source)
    {
        if (_isExiting)
        {
            ReleaseSystemAwakeGuard("app-exit");
            return;
        }

        bool youtube = IsYouTubePlaybackProtectionActive();
        bool discord = IsDiscordPowerActivityActive();
        bool shouldKeepAwake = youtube || discord;
        if (shouldKeepAwake == _systemAwakeRequestActive)
            return;

        ExecutionState requested = shouldKeepAwake
            ? ExecutionState.Continuous | ExecutionState.SystemRequired | ExecutionState.DisplayRequired
            : ExecutionState.Continuous;

        ExecutionState previous = SetThreadExecutionState(requested);
        if (previous == 0)
        {
            int error = Marshal.GetLastWin32Error();
            AppLogger.Warn("POWER", $"Windows awake request failed | active={shouldKeepAwake} | source={source} | win32={error}");
            return;
        }

        _systemAwakeRequestActive = shouldKeepAwake;
        AppLogger.Info(
            "POWER",
            shouldKeepAwake
                ? $"Windows sleep/display idle blocked | source={source} | youtube={youtube} | discord={discord}"
                : $"Windows sleep/display idle block released | source={source}");
        RecordPerformanceEvent(
            "POWER",
            shouldKeepAwake
                ? $"awake on; source={source}; youtube={youtube}; discord={discord}"
                : $"awake off; source={source}");
    }

    private void ReleaseSystemAwakeGuard(string source)
    {
        if (!_systemAwakeRequestActive)
            return;

        ExecutionState previous = SetThreadExecutionState(ExecutionState.Continuous);
        if (previous == 0)
        {
            int error = Marshal.GetLastWin32Error();
            AppLogger.Warn("POWER", $"Windows awake release failed | source={source} | win32={error}");
            return;
        }

        _systemAwakeRequestActive = false;
        AppLogger.Info("POWER", $"Windows sleep/display idle block released | source={source}");
    }

    private const string YouTubePlaybackGuardScript = """
(() => {
  if (window.top !== window) return;
  if (window.__workChatYouTubePlaybackGuardInstalled) return;
  window.__workChatYouTubePlaybackGuardInstalled = true;

  let lastState = null;
  const post = () => {
    let active = false;
    try {
      for (const video of document.querySelectorAll('video')) {
        if (video && !video.paused && !video.ended) {
          active = true;
          break;
        }
      }
    } catch (_) { }

    if (lastState === active) return;
    lastState = active;
    try {
      window.chrome.webview.postMessage(`wcm-youtube-playback:${active ? '1' : '0'}`);
    } catch (_) { }
  };

  for (const eventName of ['play', 'playing', 'pause', 'ended', 'emptied', 'abort']) {
    document.addEventListener(eventName, post, true);
  }

  try {
    const observer = new MutationObserver(post);
    observer.observe(document.documentElement || document, { childList: true, subtree: true });
  } catch (_) { }

  setInterval(post, 15000);
  queueMicrotask(post);
})();
""";
}
