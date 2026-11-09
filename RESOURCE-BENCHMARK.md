# WorkChat Mini 2.60.11 — RAM/CPU experiment

This version intentionally keeps the 2.60.10 suspension/hibernation thresholds, notifications, Vencord, shared WebView2 profile, Reading Hub and call protection unchanged. The goal is to measure first; do not lower idle thresholds based on Task Manager process count alone.

## How to record a comparable session

1. Close all other WorkChat instances. Start WorkChat normally. Open the services/tabs you actually use, and wait until navigation has settled.
2. In the Settings flyout choose **Playback > Resource Benchmark > Record RAM / CPU (up to 45 min)**. The recording is opt-in, ends after up to 45 minutes or when you select **Stop resource recording**, and writes 3 CSVs.
3. For the **background** scenario: keep your usual Messenger/Discord/Zalo alerts enabled, enable background audio if you rely on it, then leave the Web tab(s) and WorkChat window idle as you normally would. Do not clear cookies, disable Vencord or log out. If you listen to YouTube or are in a call, do that in a separate run so the results are comparable.
4. For an **active** baseline: record another session with your normal switching/browsing pattern. Use the same sites, number of tabs and audio/alert settings. Compare like with like; RAM may go up if pages simply load more content.
5. Settings > Playback > **Open resource reports folder**, or visit `%LOCALAPPDATA%\WorkChatMini\Benchmarks\Resources`. Run **RESOURCE-REPORT.bat** in the source package for a plain-text summary of the latest recording; you can also inspect CSVs in Excel.

## Files

- `resource-*-summary.csv`: roughly one row per 30-second saver sweep plus lifecycle checkpoints. Includes private memory, sum of resident working sets, CPU as a percentage of **one logical CPU core**, site/tab states, alert and audio flags, memory-pressure mode, and process count.
- `resource-*-processes.csv`: memory and CPU **per PID and WebView2 process kind**. The renderer may be shared by more than one website; **do not label a PID as Discord/YouTube/Zalo without independent evidence**.
- `resource-*-events.csv`: privacy-minimized event categories such as `SUSPEND`, `HIBERNATE`, `TAB-SUSPEND`, `TAB-HIBERNATE`, `RESUME`, `CALL`, `POWER`, `TRIM`. No page URLs, history, messages, cookie values or login information are written to these CSVs.

## How to interpret

- **Private RAM** is allocated private commit for the host plus its reported WebView2 processes. It is not the same as the Windows Task Manager default **Memory** column.
- **Resident RAM sum** adds `WorkingSet64` for each process. Shared pages may be counted multiple times, so this sum is a *diagnostic trend*, not unique physical RAM.
- **WebView2 CPU core %** = sum of delta CPU time divided by elapsed wall time; 100% roughly means one busy logical core. Treat the first reading for a PID as blank because there is not yet a prior sample.
- **Suspension** may save CPU without immediately shrinking private memory. **Hibernation** disposes a controller; expect RAM to decline after WebView2 processes settle, not necessarily at the exact event timestamp.
- **TRIM** only trims the WPF host's working set while hidden; a resident-memory drop after `TRIM` alone does not prove WebView hibernation works.
- **Notifications**: if enabled, Messenger, Discord and Zalo are forced to KeepAlive, intentionally limiting how much RAM Resource Saver may reclaim without breaking alerts. Discord calls and YouTube playback also prevent suspension.
- **Normal idle thresholds in 2.60.11**: browser tab suspend about 2 minutes, hibernate about 20 minutes (unless pinned); Facebook aggressive hibernate about 10 minutes; YouTube idle about 8 minutes. Some exceptions apply when there is system memory pressure, active audio or foreground use.

The report's before/after deltas are **whole-environment** differences; they cannot isolate one tab from a shared browser/GPU process and they are not a controlled A/B experiment. If you send the three CSVs and the current Task Manager screenshots back, we can identify the safest next threshold changes using measured results.

## Safety and release

- No process is killed and no flags are added to Chromium; no clearing or moving the shared login profile.
- Existing `PUSH.bat`, GitHub Actions, and Windows `.bat` CRLF requirements are unchanged.
- Windows WPF/.NET build **must** be verified with `SOURCE-CHECK.bat` and the GitHub preflight; the source was prepared in an environment without `dotnet`/Windows WebView2 runtime and should not be called a tested binary.
