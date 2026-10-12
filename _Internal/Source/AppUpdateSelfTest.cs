using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text.Json;

namespace WorkChatMini;

internal static class AppUpdateSelfTest
{
    internal static async Task RunAsync()
    {
        static void Require(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("Update regression: " + message);
        }
        Require(AppUpdatePackage.IsNewerVersion("2.30.0-preview18", "2.30.0-preview17"), "preview ordering");
        Require(AppUpdatePackage.IsNewerVersion("2.30.0-preview15", "2.30.0-preview14.1"), "preview hotfix ordering");
        Require(AppUpdatePackage.IsNewerVersion("2.30.0", "2.30.0-preview18"), "stable must follow preview");
        Require(!AppUpdatePackage.IsNewerVersion("2.30.0-preview18", "2.30.0-preview18"), "same version accepted");
        Require(!AppUpdatePackage.IsNewerVersion("2.30.0-preview16", "2.30.0-preview17"), "downgrade accepted");
        Require(!AppUpdatePackage.IsNewerVersion("invalid", "2.30.0-preview17"), "invalid version accepted");
        Require(!AppUpdateFeed.IsHttps("http://example.com/latest.json"), "HTTP feed accepted");
        Require(!AppUpdateFeed.IsHttps("https://user:secret@example.com/latest.json"), "user-info feed accepted");
        Require(AppUpdateFeed.IsHttps("https://example.com/latest.json"), "HTTPS feed rejected");
        Require(AppUpdatePackage.HasBundledUpdater(), "single-file EXE must embed updater helper");
        Require(string.IsNullOrWhiteSpace(AppRuntime.DefaultUpdateFeedUrl) ||
                AppUpdateFeed.IsHttps(AppRuntime.DefaultUpdateFeedUrl),
                "embedded publisher update feed must use HTTPS");

        string folder = Path.Combine(AppRuntime.DataDirectory, "UpdateSelfTest", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(folder);
        try
        {
            byte[] fakeExecutable = new byte[2048];
            RandomNumberGenerator.Fill(fakeExecutable);
            string hash = Convert.ToHexString(SHA256.HashData(fakeExecutable));
            // A structurally valid binary ZIP with deliberately wrong checksum must be rejected
            // without launching anything or accepting this fake executable.
            string invalidHashZip = Path.Combine(folder, "invalid-hash.zip");
            WritePackage(invalidHashZip, fakeExecutable, new string('A', 64), includeUnsafeEntry: false);
            await RequireRejectedAsync(invalidHashZip, "checksum mismatch was accepted");
            string unsafeZip = Path.Combine(folder, "unsafe-entry.zip");
            WritePackage(unsafeZip, fakeExecutable, hash, includeUnsafeEntry: true);
            await RequireRejectedAsync(unsafeZip, "unexpected ZIP entry was accepted");
            // Verification can succeed but the author may close Updates without
            // installing. The staged executable must be removable, without touching
            // arbitrary paths or a user-supplied ZIP.
            string pendingRoot = Path.Combine(AppUpdatePackage.WorkDirectory, "Pending");
            string stage = Path.Combine(pendingRoot, Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(stage);
            string stagedExe = Path.Combine(stage, "WorkChatMini.exe");
            await File.WriteAllBytesAsync(stagedExe, fakeExecutable);
            PreparedAppUpdate abandoned = new("2.30.0-preview18", stagedExe, hash);
            Require(AppUpdatePackage.DiscardPrepared(abandoned) && !Directory.Exists(stage),
                "an abandoned verified update kept its staging folder");
            string unrelated = Path.Combine(folder, "WorkChatMini.exe");
            await File.WriteAllBytesAsync(unrelated, fakeExecutable);
            Require(!AppUpdatePackage.DiscardPrepared(new PreparedAppUpdate(
                "2.30.0-preview18", unrelated, hash)) && File.Exists(unrelated),
                "staging cleanup touched a file outside WorkChat Pending");
        }
        finally
        {
            try { Directory.Delete(folder, recursive: true); } catch (IOException) { } catch (UnauthorizedAccessException) { }
        }
    }

    private static async Task RequireRejectedAsync(string path, string message)
    {
        bool rejected = false;
        try { await AppUpdatePackage.PrepareAsync(path, "2.30.0-preview17", CancellationToken.None); }
        catch (InvalidDataException) { rejected = true; }
        if (!rejected) throw new InvalidOperationException("Update regression: " + message);
    }

    private static void WritePackage(string path, byte[] fakeExe, string hash, bool includeUnsafeEntry)
    {
        using ZipArchive zip = ZipFile.Open(path, ZipArchiveMode.Create);
        using (Stream entry = zip.CreateEntry("WorkChatMini.exe").Open()) entry.Write(fakeExe);
        var manifest = new AppUpdateManifest
        {
            Version = "2.30.0-preview18", File = "WorkChatMini.exe", Sha256 = hash, Size = fakeExe.Length
        };
        using (Stream entry = zip.CreateEntry("update.json").Open())
            JsonSerializer.Serialize(entry, manifest);
        if (includeUnsafeEntry)
            zip.CreateEntry("../untrusted.ps1");
    }
}
