# WorkChat 2.32.0 — static audit

- Source based on the provided `WorkChat.zip` version **2.31.0**, retaining GitHub update workflow and short release filenames.
- 203 C# files + 5 XAML files, all under 1,000 lines; largest C#/XAML source file is 976 lines.
- 5 XAML files + 1 project XML parse correctly.
- 97 XAML event handlers identified and each name matched a method in C# source.
- GitHub Actions YAML parses; `build-test-release` and `publish-latest-feed` jobs still exist.
- Windows BATs use CRLF; `AUTO-TEST.bat` does not BUILD; version labels in BUILD, csproj and VERSION.txt agree.
- Embedded updater and binary release checks are retained without modification.
- New literary self-test is wired into the existing Story memory self-test, but **has not yet run on Windows**.
- The built-in C# compiler, WPF runtime, Quick and Stress self-tests are **not available in this authoring environment**. A passing source audit is not a verified Windows BUILD.
- Do not install/update until GitHub Actions finishes with BUILD + Quick/Stress + GitHub Release + Pages checks green.
