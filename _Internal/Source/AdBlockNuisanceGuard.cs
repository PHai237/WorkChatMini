namespace WorkChatMini;

/// <summary>
/// Keeps ad-blocker install/update/premium pages from taking over WorkChat's browser surface.
/// This intentionally targets only top-level vendor pages; extension network/filter-list traffic
/// is unaffected because it does not navigate a WebView document.
/// </summary>
internal static class AdBlockNuisanceGuard
{
    private static readonly string[] NuisanceTokens =
    {
        "update", "updated", "install", "installed", "welcome", "onboarding",
        "premium", "upgrade", "payment", "donate", "donation", "offer", "promo",
        "campaign", "trial", "subscription", "thank-you", "thankyou", "success"
    };

    internal static bool ShouldSuppressAutoNavigation(
        string? rawUrl,
        string? selectedBlocker,
        bool isUserInitiated,
        bool hostExplicitlyRequested)
    {
        if (isUserInitiated || hostExplicitlyRequested)
            return false;

        return IsSelectedBlockerVendorPage(rawUrl, selectedBlocker) || IsKnownNuisancePage(rawUrl);
    }

    internal static bool IsKnownNuisancePage(string? rawUrl)
    {
        if (!TryGetWebUri(rawUrl, out Uri uri))
            return false;

        string host = NormalizeHost(uri.Host);
        string target = (uri.AbsolutePath + uri.Query + uri.Fragment).ToLowerInvariant();

        // AdBlock is the blocker WorkChat historically selected by default. Its install/update
        // experience is hosted on getadblock.com. Help Center pages are ordinary documentation
        // and are intentionally left alone.
        if (IsHostOrSubdomain(host, "getadblock.com") && !IsHostOrSubdomain(host, "helpcenter.getadblock.com"))
            return NuisanceTokens.Any(target.Contains);

        if (IsHostOrSubdomain(host, "adblockplus.org"))
            return NuisanceTokens.Any(target.Contains) || target.Contains("first-run") || target.Contains("firstrun");

        if (IsHostOrSubdomain(host, "adguard.com"))
            return NuisanceTokens.Any(target.Contains) || target.Contains("thankyou");

        if (IsHostOrSubdomain(host, "ghostery.com"))
            return NuisanceTokens.Any(target.Contains);

        return false;
    }

    internal static bool IsSelectedBlockerVendorPage(string? rawUrl, string? selectedBlocker)
    {
        if (!TryGetWebUri(rawUrl, out Uri uri))
            return false;

        string host = NormalizeHost(uri.Host);
        string blocker = selectedBlocker?.Trim() ?? string.Empty;

        if (blocker.Equals("AdBlock", StringComparison.OrdinalIgnoreCase))
            return IsHostOrSubdomain(host, "getadblock.com") && !IsHostOrSubdomain(host, "helpcenter.getadblock.com");
        if (blocker.Equals("Adblock Plus", StringComparison.OrdinalIgnoreCase))
            return IsHostOrSubdomain(host, "adblockplus.org");
        if (blocker.Equals("AdGuard AdBlocker", StringComparison.OrdinalIgnoreCase))
            return IsHostOrSubdomain(host, "adguard.com");
        if (blocker.Equals("Ghostery", StringComparison.OrdinalIgnoreCase))
            return IsHostOrSubdomain(host, "ghostery.com");

        // uBlock Origin/Lite deliberately do not use a vendor marketing page in normal operation.
        return false;
    }

    internal static bool IsSafeToPersist(string? rawUrl) => !IsKnownNuisancePage(rawUrl);

    internal static bool IsSafeToPersist(string? rawUrl, string? selectedBlocker) =>
        !IsKnownNuisancePage(rawUrl) && !IsSelectedBlockerVendorPage(rawUrl, selectedBlocker);

    private static bool TryGetWebUri(string? rawUrl, out Uri uri)
    {
        uri = null!;
        if (!Uri.TryCreate(rawUrl, UriKind.Absolute, out Uri? parsed) || parsed is null)
            return false;
        if (parsed.Scheme != Uri.UriSchemeHttp && parsed.Scheme != Uri.UriSchemeHttps)
            return false;
        uri = parsed;
        return true;
    }

    private static string NormalizeHost(string host) => host.Trim().TrimEnd('.').ToLowerInvariant();

    private static bool IsHostOrSubdomain(string host, string domain) =>
        host.Equals(domain, StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith("." + domain, StringComparison.OrdinalIgnoreCase);
}
