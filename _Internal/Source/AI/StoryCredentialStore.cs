using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace WorkChatMini;

public static class StoryCredentialStore
{
    private static string CredentialDirectory => Path.Combine(AppSettings.AppDataDirectory, "AI", "Credentials");

    public static void Save(string providerId, string apiKey)
    {
        Directory.CreateDirectory(CredentialDirectory);
        string path = GetPath(providerId);
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            if (File.Exists(path))
                File.Delete(path);
            return;
        }

        byte[] clear = Encoding.UTF8.GetBytes(apiKey.Trim());
        byte[] encrypted = ProtectedData.Protect(clear, OptionalEntropy(), DataProtectionScope.CurrentUser);
        File.WriteAllBytes(path, encrypted);
        CryptographicOperations.ZeroMemory(clear);
    }

    public static string? Load(string providerId)
    {
        string path = GetPath(providerId);
        if (!File.Exists(path))
            return null;
        try
        {
            byte[] encrypted = File.ReadAllBytes(path);
            byte[] clear = ProtectedData.Unprotect(encrypted, OptionalEntropy(), DataProtectionScope.CurrentUser);
            string value = Encoding.UTF8.GetString(clear);
            CryptographicOperations.ZeroMemory(clear);
            return value;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("STORY", $"Secure credential load failed | provider={providerId} | {ex.Message}");
            return null;
        }
    }

    public static bool HasKey(string providerId) => !string.IsNullOrWhiteSpace(Load(providerId));

    private static string GetPath(string providerId)
    {
        string safe = new(providerId.Where(char.IsLetterOrDigit).ToArray());
        if (string.IsNullOrWhiteSpace(safe))
            safe = "provider";
        return Path.Combine(CredentialDirectory, safe + ".bin");
    }

    private static byte[] OptionalEntropy() => Encoding.UTF8.GetBytes("WorkChatMini.StoryCompanion.v1");
}
