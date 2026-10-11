using System.IO;
using System.Text.Json;

namespace WorkChatMini;

internal static class AdBlockImporter
{
    internal sealed record ImportResult(
        int Detected,
        int Copied,
        IReadOnlyList<string> ImportedNames,
        IReadOnlyList<string> Sources,
        IReadOnlyList<string> Errors);

    private sealed record BlockerDefinition(string Name, string[] ExtensionIds);
    private sealed record Candidate(string Name, string Browser, string ExtensionId, string Folder, string Version, DateTime LastWriteUtc);

    // Official-store IDs for the most common blockers. Edge can also contain Chrome-store IDs,
    // so every ID is searched in every Chromium profile we discover.
    private static readonly BlockerDefinition[] KnownBlockers =
    {
        new("uBlock Origin Lite", new[]
        {
            "cimighlppcgcoapaliogpjjdehbnofhn", // Edge Add-ons
            "ddkjiahejlhfcafbddmgiahcphecmpfh"  // Chrome Web Store
        }),
        new("AdBlock", new[]
        {
            "ndcileolkflehcjpmjnfbnaibdcgglog", // Edge Add-ons
            "gighmmpiobklfepjocnamgkkbiglidom"  // Chrome Web Store
        }),
        new("Adblock Plus", new[]
        {
            "gmgoamodcdcjnbaobigkjelfplakmdhh", // Edge Add-ons
            "cfhdojbkjhnklbpkdaibdccddilifddb"  // Chrome Web Store
        }),
        new("AdGuard AdBlocker", new[]
        {
            "pdffkfellgipmhklpdmokmckkkfcopbh", // Edge Add-ons
            "bgnkhhnnamicmpeenaelnjfhikgbkllg"  // Chrome Web Store
        }),
        new("Ghostery", new[]
        {
            "fclbdkbhjlgkbpfldjodgjncejkkjcme", // Edge Add-ons
            "mlomiejdfkolichcflejclcbmpeaniij"  // Chrome Web Store
        }),
        new("uBlock Origin (legacy)", new[]
        {
            "odfafepnkmbhccpbejgmiehpchacaeak", // Edge Add-ons
            "cjpalhdlnbpafiamejdnhcphjbkeiagm"  // Chrome Web Store
        })
    };

    internal static ImportResult ImportDetectedBlockers(string extensionsRoot)
    {
        Directory.CreateDirectory(extensionsRoot);

        var candidates = FindCandidates();
        var importedNames = new List<string>();
        var sources = new List<string>();
        var errors = new List<string>();
        int copied = 0;

        // One copy per blocker family. If the same blocker exists in multiple browsers,
        // prefer the most recently updated installed copy.
        foreach (var group in candidates.GroupBy(c => c.Name, StringComparer.OrdinalIgnoreCase))
        {
            Candidate best = group
                .OrderByDescending(c => c.LastWriteUtc)
                .ThenByDescending(c => c.Version, StringComparer.OrdinalIgnoreCase)
                .First();

            string safeName = MakeSafeFolderName(best.Name);
            string destination = Path.Combine(extensionsRoot, "Auto_" + safeName);

            try
            {
                string? currentVersion = ReadManifestVersion(destination);
                bool sameVersion = Directory.Exists(destination) &&
                                   File.Exists(Path.Combine(destination, "manifest.json")) &&
                                   string.Equals(currentVersion, best.Version, StringComparison.OrdinalIgnoreCase);

                if (!sameVersion)
                {
                    if (Directory.Exists(destination))
                        Directory.Delete(destination, recursive: true);

                    CopyDirectory(best.Folder, destination);
                    File.WriteAllText(
                        Path.Combine(destination, ".workchat-source.txt"),
                        $"{best.Name}\n{best.Browser}\n{best.ExtensionId}\n{best.Version}\n{best.Folder}\n");
                    copied++;
                }

                importedNames.Add($"{best.Name} {best.Version}".Trim());
                sources.Add($"{best.Name}: {best.Browser} ({Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(best.Folder)) ?? string.Empty)})");
            }
            catch (Exception ex)
            {
                errors.Add($"{best.Name}: {ex.Message}");
            }
        }

        return new ImportResult(candidates.Count, copied, importedNames, sources, errors);
    }

    private static List<Candidate> FindCandidates()
    {
        var result = new List<Candidate>();
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        var browserRoots = new (string Browser, string UserDataRoot)[]
        {
            ("Microsoft Edge", Path.Combine(local, "Microsoft", "Edge", "User Data")),
            ("Google Chrome", Path.Combine(local, "Google", "Chrome", "User Data")),
            ("Brave", Path.Combine(local, "BraveSoftware", "Brave-Browser", "User Data"))
        };

        foreach ((string browser, string userDataRoot) in browserRoots)
        {
            if (!Directory.Exists(userDataRoot))
                continue;

            IEnumerable<string> profiles;
            try
            {
                // Only Chromium user profiles can contain installed extensions. Avoid scanning
                // cache/service folders under User Data because that can make the shield look frozen.
                profiles = Directory.EnumerateDirectories(userDataRoot)
                    .Where(path =>
                    {
                        string name = Path.GetFileName(path);
                        return name.Equals("Default", StringComparison.OrdinalIgnoreCase) ||
                               name.StartsWith("Profile ", StringComparison.OrdinalIgnoreCase) ||
                               name.Equals("Guest Profile", StringComparison.OrdinalIgnoreCase);
                    })
                    .ToArray();
            }
            catch
            {
                continue;
            }

            foreach (string profile in profiles)
            {
                string extensionsDir = Path.Combine(profile, "Extensions");
                if (!Directory.Exists(extensionsDir))
                    continue;

                foreach (BlockerDefinition blocker in KnownBlockers)
                {
                    foreach (string id in blocker.ExtensionIds)
                    {
                        string idDir = Path.Combine(extensionsDir, id);
                        if (!Directory.Exists(idDir))
                            continue;

                        foreach (string versionDir in SafeDirectories(idDir))
                        {
                            string manifest = Path.Combine(versionDir, "manifest.json");
                            if (!File.Exists(manifest))
                                continue;

                            string version = ReadManifestVersion(versionDir) ?? Path.GetFileName(versionDir);
                            DateTime lastWrite = SafeLastWriteUtc(versionDir);
                            result.Add(new Candidate(blocker.Name, browser, id, versionDir, version, lastWrite));
                        }
                    }
                }
            }
        }

        return result;
    }

    private static IEnumerable<string> SafeDirectories(string path)
    {
        try { return Directory.EnumerateDirectories(path).ToArray(); }
        catch { return Array.Empty<string>(); }
    }

    private static DateTime SafeLastWriteUtc(string path)
    {
        try { return Directory.GetLastWriteTimeUtc(path); }
        catch { return DateTime.MinValue; }
    }

    private static string? ReadManifestVersion(string folder)
    {
        try
        {
            string path = Path.Combine(folder, "manifest.json");
            if (!File.Exists(path))
                return null;

            using JsonDocument doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("version", out JsonElement value))
                return value.GetString();
        }
        catch { }

        return null;
    }

    private static string MakeSafeFolderName(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        return new string(name.Select(ch => invalid.Contains(ch) ? '_' : ch).ToArray())
            .Replace(' ', '_');
    }

    private static void CopyDirectory(string source, string destination)
    {
        Directory.CreateDirectory(destination);

        foreach (string file in Directory.EnumerateFiles(source))
        {
            string target = Path.Combine(destination, Path.GetFileName(file));
            File.Copy(file, target, overwrite: true);
        }

        foreach (string directory in Directory.EnumerateDirectories(source))
        {
            string name = Path.GetFileName(directory);
            CopyDirectory(directory, Path.Combine(destination, name));
        }
    }
}
