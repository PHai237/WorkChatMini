using Microsoft.Web.WebView2.Core;

namespace WorkChatMini;

public partial class MainWindow
{
    private const string DiscordCallMessagePrefix = "wcm-discord-call:";
    private static readonly TimeSpan DiscordCallEndGracePeriod = TimeSpan.FromMinutes(2);
    private bool _discordCallActive;
    private DateTimeOffset _discordCallProtectionUntilUtc = DateTimeOffset.MinValue;

    private async Task ConfigureDiscordCallGuardAsync(CoreWebView2 core)
    {
        core.WebMessageReceived -= DiscordCallGuard_WebMessageReceived;
        core.WebMessageReceived += DiscordCallGuard_WebMessageReceived;
        await core.AddScriptToExecuteOnDocumentCreatedAsync(DiscordCallGuardScript);
        AppLogger.Info("CALL", "Discord call guard installed | detector=webrtc | graceMinutes=2");
    }

    private void DiscordCallGuard_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (sender is not CoreWebView2 core ||
            !_configuredCoreSites.TryGetValue(core, out MiniSite site) ||
            site != MiniSite.Discord)
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

        if (!message.StartsWith(DiscordCallMessagePrefix, StringComparison.Ordinal))
            return;

        ReadOnlySpan<char> payload = message.AsSpan(DiscordCallMessagePrefix.Length);
        bool active = payload.Length > 0 && payload[0] == '1';
        int separator = payload.IndexOf(':');
        string reason = separator >= 0 && separator + 1 < payload.Length
            ? payload[(separator + 1)..].ToString()
            : "webrtc";

        SetDiscordCallState(active, reason);
    }

    private void SetDiscordCallState(bool active, string reason)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        if (active)
        {
            _discordCallProtectionUntilUtc = DateTimeOffset.MaxValue;
            if (_discordCallActive)
                return;

            _discordCallActive = true;
            KeepDiscordAwakeForCall("webrtc-active");
            AppLogger.Info("CALL", $"Discord call protection enabled | reason={reason}");
            RecordPerformanceEvent("CALL", $"discord active; reason={reason}");
            UpdateAudioState();
            UpdateSystemAwakeGuard("discord-call-active");
            return;
        }

        if (!_discordCallActive)
            return;

        _discordCallActive = false;
        _discordCallProtectionUntilUtc = now.Add(DiscordCallEndGracePeriod);
        AppLogger.Info("CALL", $"Discord call ended | reason={reason} | suspendGraceSeconds={DiscordCallEndGracePeriod.TotalSeconds:0}");
        RecordPerformanceEvent("CALL", $"discord ended; graceSec={DiscordCallEndGracePeriod.TotalSeconds:0}");
        UpdateAudioState();
        UpdateSystemAwakeGuard("discord-call-ended");
    }

    private void ResetDiscordCallStateForNavigation()
    {
        if (!_discordCallActive)
            return;

        _discordCallActive = false;
        _discordCallProtectionUntilUtc = DateTimeOffset.UtcNow.Add(DiscordCallEndGracePeriod);
        AppLogger.Info("CALL", $"Discord navigation reset active-call signal | suspendGraceSeconds={DiscordCallEndGracePeriod.TotalSeconds:0}");
        UpdateAudioState();
        UpdateSystemAwakeGuard("discord-navigation");
    }

    private bool IsDiscordCallProtectionActive()
    {
        if (_discordCallActive || DateTimeOffset.UtcNow < _discordCallProtectionUntilUtc)
            return true;

        // Secondary safety net: WebView2's audio signal protects active Discord playback even if
        // the site's WebRTC implementation changes and the injected detector misses a peer.
        return IsSiteInitialized(MiniSite.Discord) && IsSitePlayingAudio(MiniSite.Discord);
    }

    private void KeepDiscordAwakeForCall(string source)
    {
        if (!IsSiteInitialized(MiniSite.Discord) || _hibernatedSites.Contains(MiniSite.Discord))
            return;

        try
        {
            CoreWebView2? core = DiscordWebView.CoreWebView2;
            if (core is null)
                return;

            bool changed = false;
            if (core.IsSuspended)
            {
                core.Resume();
                changed = true;
            }

            if (_sleepingSites.Remove(MiniSite.Discord))
                changed = true;

            _lastSleepingSyncAt[MiniSite.Discord] = DateTimeOffset.MinValue;
            _sleepingSyncBackoffStep[MiniSite.Discord] = 0;
            _nextSuspendAttemptAt[MiniSite.Discord] = DateTimeOffset.MinValue;
            if (changed)
            {
                AppLogger.Info("CALL", $"Discord kept awake for call | source={source}");
                RecordPerformanceEvent("CALL", $"discord wake; source={source}");
                UpdateToolbarState();
            }
        }
        catch (Exception ex)
        {
            AppLogger.Warn("CALL", $"Discord call wake failed | source={source} | {ex.Message}");
        }
    }

    private const string DiscordCallGuardScript = """
(() => {
  if (window.top !== window) return;
  if (window.__workChatDiscordCallGuardInstalled) return;
  window.__workChatDiscordCallGuardInstalled = true;

  const peers = new Set();
  let lastState = null;

  const post = (active, reason) => {
    if (lastState === active) return;
    lastState = active;
    try {
      window.chrome.webview.postMessage(`wcm-discord-call:${active ? '1' : '0'}:${reason || 'webrtc'}`);
    } catch (_) { }
  };

  const hasAudio = (pc) => {
    try {
      if (pc.getSenders && pc.getSenders().some(s => s && s.track && s.track.kind === 'audio')) return true;
      if (pc.getReceivers && pc.getReceivers().some(r => r && r.track && r.track.kind === 'audio')) return true;
      if (pc.getTransceivers && pc.getTransceivers().some(t =>
          (t.sender && t.sender.track && t.sender.track.kind === 'audio') ||
          (t.receiver && t.receiver.track && t.receiver.track.kind === 'audio'))) return true;
    } catch (_) { }
    return false;
  };

  const isActive = (pc) => {
    if (!pc || !hasAudio(pc)) return false;
    const connection = pc.connectionState || '';
    const ice = pc.iceConnectionState || '';
    return connection === 'connected' || connection === 'connecting' ||
           ice === 'connected' || ice === 'completed' || ice === 'checking';
  };

  const evaluate = () => {
    let active = false;
    for (const pc of [...peers]) {
      const connection = pc.connectionState || '';
      const ice = pc.iceConnectionState || '';
      if (connection === 'closed' || connection === 'failed' || ice === 'closed' || ice === 'failed') {
        peers.delete(pc);
        continue;
      }
      if (isActive(pc)) {
        active = true;
        break;
      }
    }
    post(active, active ? 'webrtc-active' : 'webrtc-idle');
  };

  const track = (pc) => {
    if (!pc || peers.has(pc)) return;
    peers.add(pc);
    try {
      pc.addEventListener('connectionstatechange', evaluate);
      pc.addEventListener('iceconnectionstatechange', evaluate);
      pc.addEventListener('track', evaluate);
    } catch (_) { }
    queueMicrotask(evaluate);
  };

  const Native = window.RTCPeerConnection || window.webkitRTCPeerConnection;
  if (!Native || !Native.prototype) {
    post(false, 'webrtc-unavailable');
    return;
  }

  const proto = Native.prototype;
  for (const name of ['addTrack', 'removeTrack', 'addTransceiver', 'addStream', 'removeStream',
                      'setLocalDescription', 'setRemoteDescription', 'close']) {
    const original = proto[name];
    if (typeof original !== 'function' || original.__workChatWrapped) continue;
    const wrapped = function(...args) {
      track(this);
      const result = original.apply(this, args);
      queueMicrotask(evaluate);
      return result;
    };
    try { Object.defineProperty(wrapped, '__workChatWrapped', { value: true }); } catch (_) { }
    try { proto[name] = wrapped; } catch (_) { }
  }

  post(false, 'guard-ready');
})();
""";
}
