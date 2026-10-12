using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace WorkChatMini;

internal sealed class AppUpdateManifest
{
    public string Version { get; set; } = string.Empty;
    public string File { get; set; } = string.Empty;
    public string Sha256 { get; set; } = string.Empty;
    public long Size { get; set; }
}

internal sealed record PreparedAppUpdate(string Version, string StagedExecutable, string Sha256);

internal static class AppUpdatePackage
{
    private const long MaxExecutableBytes = 600L * 1024 * 1024;
    internal const long MaxDownloadBytes = 700L * 1024 * 1024;
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };

    internal static string WorkDirectory => AppRuntime.IsSelfTest
        ? Path.Combine(AppRuntime.DataDirectory, "Updates")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "WorkChatMini-Updates");

    internal static bool IsValidSha256(string? value) =>
        value is { Length: 64 } && value.All(Uri.IsHexDigit);

    // Keep preview ordering deterministic: 2.30.0-preview14.1 < preview15 < 2.30.0 release.
    internal static bool IsNewerVersion(string candidate, string current)
    {
        return TryVersionKey(candidate, out int[]? a) && TryVersionKey(current, out int[]? b) &&
               a is not null && b is not null && Compare(a, b) > 0;
    }

    private static int Compare(int[] a, int[] b)
    {
        for (int i = 0; i < a.Length; i++)
        {
            int difference = a[i].CompareTo(b[i]);
            if (difference != 0) return difference;
        }
        return 0;
    }

    private static bool TryVersionKey(string version, out int[]? key)
    {
        key = null;
        Match match = Regex.Match(version, @"^v?(\d+)\.(\d+)\.(\d+)(?:-preview(\d+)(?:\.(\d+))?)?$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success) return false;
        var pieces = new int[6];
        for (int i = 1; i <= 3; i++)
            if (!int.TryParse(match.Groups[i].Value, out pieces[i - 1])) return false;
        pieces[3] = match.Groups[4].Success ? 0 : 1;
        if (match.Groups[4].Success && !int.TryParse(match.Groups[4].Value, out pieces[4])) return false;
        if (match.Groups[5].Success && !int.TryParse(match.Groups[5].Value, out pieces[5])) return false;
        key = pieces;
        return true;
    }

    internal static async Task<PreparedAppUpdate> PrepareAsync(string packagePath, string installedVersion, CancellationToken cancellationToken)
    {
        if (!File.Exists(packagePath)) throw new FileNotFoundException("Update ZIP was not found.", packagePath);
        if (new FileInfo(packagePath).Length > MaxDownloadBytes) throw new InvalidDataException("Update ZIP is too large.");
        using ZipArchive zip = ZipFile.OpenRead(packagePath);
        // A binary update deliberately has exactly two root entries. No scripts or source files
        // from an untrusted ZIP are ever extracted or executed.
        if (zip.Entries.Count != 2 || zip.Entries.Any(e => e.FullName is not ("update.json" or "WorkChatMini.exe")))
            throw new InvalidDataException("Expected a binary update ZIP with only update.json and WorkChatMini.exe.");
        ZipArchiveEntry? manifestEntry = zip.GetEntry("update.json");
        ZipArchiveEntry? executableEntry = zip.GetEntry("WorkChatMini.exe");
        if (manifestEntry is null || executableEntry is null || manifestEntry.Length > 16384 ||
            executableEntry.Length < 1024 || executableEntry.Length > MaxExecutableBytes)
            throw new InvalidDataException("Invalid update ZIP entries or sizes.");

        AppUpdateManifest? manifest;
        await using (Stream jsonStream = manifestEntry.Open())
            manifest = await JsonSerializer.DeserializeAsync<AppUpdateManifest>(jsonStream, JsonOptions, cancellationToken);
        if (manifest is null || manifest.File != "WorkChatMini.exe" || !IsValidSha256(manifest.Sha256) ||
            manifest.Size != executableEntry.Length || !IsNewerVersion(manifest.Version, installedVersion))
            throw new InvalidDataException("Update manifest is invalid or its version is not newer.");

        string stagingFolder = Path.Combine(WorkDirectory, "Pending", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(stagingFolder);
        string stagedExecutable = Path.Combine(stagingFolder, "WorkChatMini.exe");
        try
        {
            await using (Stream input = executableEntry.Open())
            await using (var output = new FileStream(stagedExecutable, FileMode.CreateNew, FileAccess.Write, FileShare.None, 81920, true))
                await input.CopyToAsync(output, cancellationToken);
            string actualHash = await ComputeSha256Async(stagedExecutable, cancellationToken);
            if (!actualHash.Equals(manifest.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("SHA-256 mismatch: the update package may be damaged or altered.");
            string? productVersion = FileVersionInfo.GetVersionInfo(stagedExecutable).ProductVersion;
            if (string.IsNullOrEmpty(productVersion) ||
                !(productVersion.Equals(manifest.Version, StringComparison.OrdinalIgnoreCase) ||
                  productVersion.StartsWith(manifest.Version + "-", StringComparison.OrdinalIgnoreCase) ||
                  productVersion.StartsWith(manifest.Version + "+", StringComparison.OrdinalIgnoreCase)))
                throw new InvalidDataException("Executable version does not match the update manifest.");
            return new PreparedAppUpdate(manifest.Version, stagedExecutable, actualHash);
        }
        catch
        {
            try { Directory.Delete(stagingFolder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
            throw;
        }
    }

    // A verified EXE may remain in Pending when Updates is closed, changed, or rechecked
    // without installation. Only a WorkChat-generated Pending/<guid>/WorkChatMini.exe is
    // eligible for deletion; never remove a ZIP selected by the user or app data.
    internal static bool DiscardPrepared(PreparedAppUpdate prepared)
    {
        try
        {
            string path = Path.GetFullPath(prepared.StagedExecutable);
            if (!string.Equals(Path.GetFileName(path), "WorkChatMini.exe", StringComparison.OrdinalIgnoreCase))
                return false;
            string? staging = Path.GetDirectoryName(path);
            if (staging is null || !Guid.TryParseExact(Path.GetFileName(staging), "N", out _))
                return false;
            string? parent = Path.GetDirectoryName(staging);
            string pendingRoot = Path.GetFullPath(Path.Combine(WorkDirectory, "Pending"));
            if (parent is null || !string.Equals(Path.GetFullPath(parent), pendingRoot,
                StringComparison.OrdinalIgnoreCase))
                return false;
            if (Directory.Exists(staging))
                Directory.Delete(staging, recursive: true);
            return true;
        }
        catch (IOException ex)
        {
            AppLogger.Warn("UPDATE", $"Discard of unused verified update deferred | {ex.Message}");
            return false;
        }
        catch (UnauthorizedAccessException ex)
        {
            AppLogger.Warn("UPDATE", $"Discard of unused verified update denied | {ex.Message}");
            return false;
        }
        catch (ArgumentException)
        {
            return false;
        }
        catch (NotSupportedException)
        {
            return false;
        }
    }

    internal static async Task<string> ComputeSha256Async(string path, CancellationToken token)
    {
        await using var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 81920, true);
        byte[] hash = await SHA256.HashDataAsync(stream, token);
        return Convert.ToHexString(hash);
    }

    private const string BundledUpdaterName = "WorkChatMini.Updater.Apply-WorkChatUpdate.ps1";

    internal static bool HasBundledUpdater()
    {
        using Stream? input = Assembly.GetExecutingAssembly().GetManifestResourceStream(BundledUpdaterName);
        return input is { Length: > 128 };
    }

    private static string InstallBundledUpdater()
    {
        using Stream? input = Assembly.GetExecutingAssembly().GetManifestResourceStream(BundledUpdaterName);
        if (input is null || input.Length < 128 || input.Length > 1024 * 1024)
            throw new InvalidOperationException("The embedded updater resource is missing or invalid.");
        string folder = Path.Combine(AppRuntime.ExecutableDirectory, "_Updater");
        Directory.CreateDirectory(folder);
        string helper = Path.Combine(folder, "Apply-WorkChatUpdate.ps1");
        string temporary = Path.Combine(folder, "updater-" + Guid.NewGuid().ToString("N") + ".tmp");
        try
        {
            using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None))
                input.CopyTo(output);
            File.Move(temporary, helper, overwrite: true);
            return helper;
        }
        finally
        {
            if (File.Exists(temporary)) File.Delete(temporary);
        }
    }

    internal static void LaunchInstaller(PreparedAppUpdate prepared)
    {
        if (AppRuntime.IsSelfTest) throw new InvalidOperationException("Updates are disabled in self-test.");
        string target = Environment.ProcessPath ?? string.Empty;
        if (!File.Exists(target) ||
            !string.Equals(Path.GetFileName(target), "WorkChatMini.exe", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Run an installed WorkChatMini.exe. Do not attempt to update a source ZIP.");
        // Binary packages contain only WorkChatMini.exe and update.json for compatibility
        // with preview20+. The EXE always carries its matching helper as a resource.
        // Refresh the installed helper BEFORE launching it; future fixes now arrive by ZIP.
        string helper = InstallBundledUpdater();
        string powerShell = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),
            "WindowsPowerShell", "v1.0", "powershell.exe");
        if (!File.Exists(powerShell)) throw new FileNotFoundException("Windows PowerShell is unavailable.");
        var info = new ProcessStartInfo(powerShell) { UseShellExecute = false, CreateNoWindow = true };
        foreach (string argument in new[] { "-NoProfile", "-NonInteractive", "-ExecutionPolicy", "Bypass", "-File", helper,
            "-ParentPid", Environment.ProcessId.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "-StagedExe", prepared.StagedExecutable, "-TargetExe", target, "-ExpectedSha256", prepared.Sha256 })
            info.ArgumentList.Add(argument);
        using Process? installer = Process.Start(info);
        if (installer is null) throw new InvalidOperationException("Windows could not start the update helper.");
    }
}
