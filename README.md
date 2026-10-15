# WorkChat Mini Core 2.44.0

This source retires the built-in AI Story feature and keeps WorkChat as a lightweight multi-site desktop app (Messenger, Facebook, Discord, YouTube, Zalo, Web).

**User Story data is NOT removed.** Existing `%LOCALAPPDATA%\WorkChatMini\AI` remains on disk, untouched by the application and updater. Back it up before changing computers or removing the app.

The Story button, Story popup, StoryPanel WPF object, and AI Story source modules have been removed from the application. Legacy Story settings are retained only for backward compatibility.

Build and release scripts still need a successful Windows WPF build, Quick/Stress tests, and installer verification before deployment.

## Build
- `SOURCE-CHECK.bat` for static validation
- `BUILD.bat` for Release WPF compilation (Windows/.NET SDK)
- `AUTO-TEST.bat` for app tests
- `PUSH.bat` for protected GitHub preflight and release
