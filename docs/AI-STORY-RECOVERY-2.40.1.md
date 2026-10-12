# AI Story bugfix / field test — 2.40.1

**Source-only candidate:** no Windows .NET/WPF compiler or real Gemini credentials were available in the source-patching environment. `PUSH.bat` (which requests a GitHub Windows CI release) must run only after source review and with user approval; do not claim a Windows build passed without the workflow and reports.

## Confirmed from supplied 12 Oct 2026 log

- Installed app: `2.40.0`, build `ce1b866...`.
- 20:52 model `gemini-3.8-flash` responded 503, fallback `gemini-3.5-flash-lite` succeeded; recovery repeated later.
- 21:01 primary non-streaming request timed out; the code retried primary, timed out a second time at 21:03, raised generic failure. Old resilience code handled capacity 503 fallback but *not* transport timeouts.
- System memory later hit Critical while WebView2 tabs were open (app private memory ~2591 MiB, free system memory ~1.45 GiB). No conclusive inspector UI-blocking stack or Inspector event was logged; the new `STORY-UI` timing marks let future tests distinguish slow UI work from unavailable controls.
- Old UI disabled whole Inspector panel while generating, including its Close button; 2.40.1 keeps panel chrome responsive, disables editor sections during writes.

## Test on Windows before release

1. Run `SOURCE-CHECK.bat`, `BUILD.bat`, `AUTO-TEST.bat` Quick and Stress; check that there are no WPF/XAML compile errors and no failing selftests.
2. Start disposable test Story. Type `**bold**`, `*italic*`, backtick-code, `- list item` in a simulated assistant response: rendered emphasis should work; copying must retain the literal raw Markdown. Long chapters should stay paged and responsive.
3. Press Alt+F twice (open/close), press Escape within Find, use Find while viewing a paged conversation. Hidden bar must not cover the chat.
4. Simulate provider failure/timeout: Retry AI appears, and Retry must not add a second user message. If partial output is already saved, it stays incomplete/non-canon, and successful retry adds the complete output once.
5. With a committed chapter, Retry AI must not appear. A completed response must never be overwritten or marked uncommitted.
6. Start a slow Gemini request, open Story Settings from sidebar, then close it while generation is active. Inspect controls that could mutate a Story must stay disabled until safe; Close must work immediately.
7. Repeat with YouTube/Discord/WebViews open, monitor system RAM and new `STORY-UI` `Inspector opened | elapsedMs` entries. If an Inspector operation takes too long, collect `latest.log` and a short screen recording; the exact lag remains unproven.
8. Ensure existing Story/Memory and API keys survive update. Do not delete `%LOCALAPPDATA%\WorkChatMini` or install source ZIP via Updates.

## Files to use

The source archive is named `WorkChat.zip`. After GitHub CI publishes a successful versioned release, update with **More → Updates → Check GitHub**. Separate release artifacts (`WorkChat-Setup.exe`, `WorkChat-Update.zip`) are not included in this source ZIP.
