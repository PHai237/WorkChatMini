using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;

namespace WorkChatMini;

/// <summary>
/// Installs an official, unpacked Vencord Web extension into stable user-data paths.
/// A WebView2 extension references files in place: do not modify an installed folder.
/// </summary>
internal static class VencordWebPackage
{
    // Pinned official Vendicated/Vencord devbuild, published 2026-11-04.
    // Deliberately fail closed if the mutable devbuild asset changes; a user can
    // then obtain an official updated ZIP and select it manually from Settings.
    internal const string OfficialZipUrl =
        "https://github.com/Vendicated/Vencord/releases/download/devbuild/extension-chrome.zip";
    private const string OfficialSha256 = "49c2e9862f9f184ad1202cc1170189a671b9344bb4e2d319f6cb211df165fedd";
    private const int MaxDownloadBytes = 16 * 1024 * 1024;
    private const long MaxUnpackedBytes = 64L * 1024 * 1024;
    private const int MaxEntries = 500;

    internal static string PackagesDirectory =>
        Path.Combine(AppSettings.AppDataDirectory, "VencordWeb", "Packages");

    internal static string? GetCachedPackage(string? sha256)
    {
        if (sha256 is null || sha256.Length != 64 ||
            !sha256.All(Uri.IsHexDigit))
            return null;
        string path = Path.Combine(PackagesDirectory, sha256.ToLowerInvariant());
        try
        {
            if (File.Exists(Path.Combine(path, "manifest.json")) &&
                IsVencordManifest(File.ReadAllText(Path.Combine(path, "manifest.json"))))
                return path;
        }
        catch (Exception) { /* A deleted/broken cache is not an install candidate. */ }
        return null;
    }

    internal static async Task<(string Path, string Sha256)> DownloadPinnedAsync()
    {
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(45) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("WorkChatMini/2.60.10");
        using var response = await client.GetAsync(OfficialZipUrl,
            HttpCompletionOption.ResponseHeadersRead).ConfigureAwait(false);
        response.EnsureSuccessStatusCode();
        if (response.Content.Headers.ContentLength is > MaxDownloadBytes)
            throw new InvalidDataException("Vencord download is unexpectedly large.");
        using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
        using var memory = new MemoryStream();
        byte[] buffer = new byte[32768];
        int count;
        while ((count = await stream.ReadAsync(buffer).ConfigureAwait(false)) != 0)
        {
            if (memory.Length + count > MaxDownloadBytes)
                throw new InvalidDataException("Vencord download exceeded its size limit.");
            memory.Write(buffer, 0, count);
        }
        byte[] zip = memory.ToArray();
        string sha256 = Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant();
        if (!sha256.Equals(OfficialSha256, StringComparison.Ordinal))
            throw new InvalidDataException("The official Vencord devbuild has changed. " +
                "Download extension-chrome.zip from the official Vencord GitHub release " +
                "and use 'Install from ZIP' instead.");
        return InstallZipBytes(zip);
    }

    internal static (string Path, string Sha256) InstallLocalZip(string file)
    {
        var info = new FileInfo(file);
        if (!info.Exists || info.Length is <= 0 or > MaxDownloadBytes)
            throw new InvalidDataException("Choose a Vencord extension ZIP smaller than 16 MB.");
        return InstallZipBytes(File.ReadAllBytes(file));
    }

    private static (string Path, string Sha256) InstallZipBytes(byte[] zip)
    {
        string sha256 = Convert.ToHexString(SHA256.HashData(zip)).ToLowerInvariant();
        string? existing = GetCachedPackage(sha256);
        if (existing is not null) return (existing, sha256);

        Directory.CreateDirectory(PackagesDirectory);
        string staging = Path.Combine(PackagesDirectory, "staging-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            using var archive = new ZipArchive(new MemoryStream(zip, writable: false),
                ZipArchiveMode.Read, leaveOpen: false);
            if (archive.Entries.Count is < 1 or > MaxEntries)
                throw new InvalidDataException("Unexpected Vencord extension ZIP layout.");
            long unpacked = 0;
            foreach (ZipArchiveEntry entry in archive.Entries)
            {
                string normalized = entry.FullName.Replace('\\', '/');
                if (string.IsNullOrWhiteSpace(normalized) || normalized.StartsWith('/') ||
                    normalized.Contains(':') || normalized.Split('/').Any(part => part == "..") ||
                    (entry.ExternalAttributes >> 16 & 0xF000) == 0xA000)
                    throw new InvalidDataException("Unsafe path in Vencord ZIP.");
                if (entry.Length < 0 || entry.Length > MaxUnpackedBytes - unpacked)
                    throw new InvalidDataException("Vencord ZIP expands beyond its size limit.");
                unpacked += entry.Length;
                string target = Path.GetFullPath(Path.Combine(staging,
                    normalized.Replace('/', Path.DirectorySeparatorChar)));
                if (!target.StartsWith(staging + Path.DirectorySeparatorChar,
                        StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException("Vencord ZIP contains a path outside its folder.");
                if (normalized.EndsWith('/'))
                {
                    Directory.CreateDirectory(target);
                    continue;
                }
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (File.Exists(target))
                    throw new InvalidDataException("Vencord ZIP has duplicate entries.");
                entry.ExtractToFile(target);
            }
            string unpackedRoot = staging;
            if (!File.Exists(Path.Combine(unpackedRoot, "manifest.json")))
            {
                string[] children = Directory.GetDirectories(staging);
                if (children.Length != 1 || Directory.GetFiles(staging).Length != 0)
                    throw new InvalidDataException("Cannot find the Vencord extension manifest.");
                unpackedRoot = children[0];
            }
            string manifest = Path.Combine(unpackedRoot, "manifest.json");
            if (!File.Exists(manifest) || !IsVencordManifest(File.ReadAllText(manifest)))
                throw new InvalidDataException("ZIP does not contain the official Vencord Web extension layout.");
            if (!File.Exists(Path.Combine(unpackedRoot, "content.js")) ||
                !File.Exists(Path.Combine(unpackedRoot, "dist", "Vencord.js")))
                throw new InvalidDataException("Vencord Web scripts are missing from the ZIP.");
            string finalPath = Path.Combine(PackagesDirectory, sha256);
            if (Directory.Exists(finalPath))
            {
                // Never mutate an installed folder: WebView2 removes modified extensions.
                if (GetCachedPackage(sha256) is null)
                    throw new InvalidDataException("The cached Vencord package is damaged. Remove it while WorkChat is closed.");
                return (finalPath, sha256);
            }
            Directory.Move(unpackedRoot, finalPath);
            return (finalPath, sha256);
        }
        finally
        {
            try { if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true); }
            catch (IOException) { /* Never change the outcome of a successful install. */ }
        }
    }

    internal static bool IsVencordManifest(string json)
    {
        try
        {
            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (!root.TryGetProperty("manifest_version", out var manifestVersion) ||
                manifestVersion.GetInt32() != 3 ||
                !root.TryGetProperty("name", out var name) ||
                name.GetString() != "Vencord Web" ||
                !root.TryGetProperty("content_scripts", out var scripts) ||
                scripts.ValueKind != JsonValueKind.Array)
                return false;
            if (root.TryGetProperty("host_permissions", out var hosts))
            {
                if (hosts.ValueKind != JsonValueKind.Array ||
                    hosts.EnumerateArray().Any(host => host.ValueKind != JsonValueKind.String ||
                        host.GetString() is not ("*://*.discord.com/*" or
                            "https://raw.githubusercontent.com/*")))
                    return false;
            }
            bool hasVencordScript = false;
            bool hasBootstrap = false;
            foreach (JsonElement script in scripts.EnumerateArray())
            {
                if (!script.TryGetProperty("matches", out JsonElement matches) ||
                    matches.ValueKind != JsonValueKind.Array ||
                    !matches.EnumerateArray().Any(match =>
                        match.ValueKind == JsonValueKind.String &&
                        match.GetString() is "*://*.discord.com/*" or "https://discord.com/*"))
                    return false;
                // Never accept a ZIP that injects into other origins.
                if (matches.EnumerateArray().Any(match =>
                    match.ValueKind != JsonValueKind.String ||
                    match.GetString() is not ("*://*.discord.com/*" or "https://discord.com/*")))
                    return false;
                if (!script.TryGetProperty("js", out JsonElement js) || js.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (JsonElement file in js.EnumerateArray())
                {
                    if (file.ValueKind != JsonValueKind.String) continue;
                    hasVencordScript |= file.GetString() == "dist/Vencord.js";
                    hasBootstrap |= file.GetString() == "content.js";
                }
            }
            return hasVencordScript && hasBootstrap;
        }
        catch (Exception) { return false; }
    }
}
