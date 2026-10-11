# WorkChat Mini 2.30.0-preview21 — Static & Packaging Audit

**Date:** 2026-10-11  
**Base:** preview20. User Quick and Stress both 22 PASS / 0 FAIL / 0 WARN.  
**Windows validation for preview21:** NOT RUN (no .NET/WPF or Windows Inno Setup in this container).  

- 201 C#/XAML source files, max 977 lines (MainWindow.SelfTest.cs) — all below 1000.
- XML for all XAML and csproj parsed successfully; GitHub Actions YAML parsed.
- Six batch files examined for CRLF/no-BOM; UTF-8 starts with `@echo off`.
- `AUTO-TEST.bat` contains NO call to BUILD; shared PowerShell runner checks both Quick/Stress reports and exit codes.
- Build/Release products placed under `_Internal/Build`, `_Internal/Releases`, not root.
- New Inno Setup script has valid locally available input paths (except EXE produced by CI), and per-user install layout.
- All source paths archived without compiled binary, profile data, or temporary .NET artifacts.
- ZIP integrity checked after writing by `ZipFile.testzip()` and exact re-listing.

**Not proven:** CS/XAML compilation, Inno Setup compilation, actual install/uninstall, online GitHub Release + Pages and in-place update, real Story UI acceptance. Authenticode signing is not configured.

**Performance note:** Preview20 Quick startup 896 ms, 482 MiB peak, 7 ms median switch; Stress startup 1813 ms, 764 MiB peak, 10 ms median switch. One stress run alone does not prove a memory leak (end 119 MiB vs start 78 MiB). Recommend following the stress peak over several runs.
