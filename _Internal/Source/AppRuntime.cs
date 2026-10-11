using System.IO;
using System.Reflection;

namespace WorkChatMini;

internal static class AppRuntime
{
    internal static bool IsSelfTest { get; private set; }
    internal static bool IsStressSelfTest { get; private set; }
    internal static string DataDirectory { get; private set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "WorkChatMini");

    internal static string ExecutableDirectory => Path.GetDirectoryName(Environment.ProcessPath) ?? AppContext.BaseDirectory;
    internal static string VersionText
    {
        get
        {
            Version? version = Assembly.GetExecutingAssembly().GetName().Version;
            if (version is null)
                return "unknown";
            return version.Revision > 0 ? version.ToString(4) : version.ToString(3);
        }
    }
    internal static string BuildText => Assembly.GetExecutingAssembly()
        .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? VersionText;

    // Populated by GitHub Actions at build time. Empty for local/offline builds.
    internal static string DefaultUpdateFeedUrl => Assembly.GetExecutingAssembly()
        .GetCustomAttributes<AssemblyMetadataAttribute>()
        .FirstOrDefault(attribute => attribute.Key == "WorkChatUpdateFeedUrl")?.Value ?? string.Empty;

    internal static void Configure(string[] args)
    {
        IsSelfTest = args.Any(a => string.Equals(a, "--selftest", StringComparison.OrdinalIgnoreCase));
        IsStressSelfTest = IsSelfTest && args.Any(a => string.Equals(a, "stress", StringComparison.OrdinalIgnoreCase) ||
                                                      string.Equals(a, "--stress", StringComparison.OrdinalIgnoreCase));

        if (IsSelfTest)
        {
            DataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "WorkChatMini-SelfTest");
        }
    }
}
