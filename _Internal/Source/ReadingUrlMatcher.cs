namespace WorkChatMini;

// Conservative by design. A chapter with an independent ID cannot be assigned
// to a title from the hostname alone (MangaDex and Hako both use such URLs).
internal static class ReadingUrlMatcher
{
    internal static bool BelongsToBook(ReadingBook book, string? url)
    {
        if (!ReadingLibraryStore.IsSafeHttpUrl(book.Url) ||
            !ReadingLibraryStore.IsSafeHttpUrl(url) ||
            !Uri.TryCreate(book.Url, UriKind.Absolute, out Uri? source) ||
            !Uri.TryCreate(url, UriKind.Absolute, out Uri? current)) return false;

        string originalSource = ReadingLibraryStore.DetectSource(book.Url);
        string currentSource = ReadingLibraryStore.DetectSource(url!);
        bool sameHost = string.Equals(NormalizeHost(source.Host), NormalizeHost(current.Host),
            StringComparison.OrdinalIgnoreCase);
        bool trustedMirror = (originalSource is "Hako / DocLN" or "Webnovel") &&
            currentSource == originalSource;
        if ((!sameHost && !trustedMirror) ||
            ((!source.IsDefaultPort || !current.IsDefaultPort) && source.Port != current.Port))
            return false;

        // Exact page, ignoring fragments/host www/Hako mirrors. This also supports a
        // chapter URL that was saved directly, even for websites with unknown routes.
        if (string.Equals(source.AbsolutePath.TrimEnd('/'), current.AbsolutePath.TrimEnd('/'),
                StringComparison.OrdinalIgnoreCase) &&
            string.Equals(source.Query, current.Query, StringComparison.Ordinal)) return true;

        // For nested routes only, both URLs must explicitly identify the same book.
        // Do not use LastReadUrl as proof: old builds may have saved another book there.
        string? identity = GetBookIdentity(source, originalSource);
        return identity is not null && string.Equals(identity,
            GetBookIdentity(current, currentSource), StringComparison.OrdinalIgnoreCase);
    }

    // Only reject an already persisted resume link if BOTH URLs carry a
    // verifiable but different book identity. Unknown chapter routes are kept.
    internal static bool IsDefinitelyDifferentBook(ReadingBook book, string? url)
    {
        if (!ReadingLibraryStore.IsSafeHttpUrl(book.Url) ||
            !ReadingLibraryStore.IsSafeHttpUrl(url) ||
            !Uri.TryCreate(book.Url, UriKind.Absolute, out Uri? original) ||
            !Uri.TryCreate(url, UriKind.Absolute, out Uri? current)) return false;
        string first = ReadingLibraryStore.DetectSource(book.Url);
        string second = ReadingLibraryStore.DetectSource(url!);
        bool sameHost = string.Equals(NormalizeHost(original.Host), NormalizeHost(current.Host),
            StringComparison.OrdinalIgnoreCase);
        bool trustedMirror = first == second && (first is "Hako / DocLN" or "Webnovel");
        if (!sameHost && !trustedMirror) return true;
        string? originalKey = GetBookIdentity(original, first);
        string? currentKey = GetBookIdentity(current, second);
        return originalKey is not null && currentKey is not null &&
            !string.Equals(originalKey, currentKey, StringComparison.OrdinalIgnoreCase);
    }

    private static string NormalizeHost(string host) =>
        host.StartsWith("www.", StringComparison.OrdinalIgnoreCase) ? host[4..] : host;

    private static string? GetBookIdentity(Uri uri, string source)
    {
        string[] parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2) return null;
        string root = parts[0].ToLowerInvariant();
        string token = parts[1];
        if (string.IsNullOrWhiteSpace(token) || token is "." or "..") return null;
        if (source == "MangaDex")
        {
            // /chapter/{chapterUuid} does not contain the corresponding manga UUID.
            if (root is not ("title" or "manga") || !Guid.TryParse(token, out Guid manga)) return null;
            return "mangadex:" + manga.ToString("D");
        }
        if (source == "Webnovel" && root == "book") return "webnovel:" + token;
        if (source == "Hako / DocLN" && root == "truyen")
        {
            // /truyen/{id}-slug is stable across translated/mirrored domains.
            int dash = token.IndexOf('-');
            string number = dash < 0 ? token : token[..dash];
            return "hako:" + (long.TryParse(number, out _) ? number : token);
        }
        // For arbitrary sites, accept only *nested* routes under a named book key;
        // a sibling /chapter/NN is never assumed to belong to this series.
        if (source is not ("Webnovel" or "Hako / DocLN" or "MangaDex") &&
            root is ("book" or "books" or "novel" or "novels" or "series" or "story" or "stories"))
            return NormalizeHost(uri.Host) + ":" + root + ":" + token;
        return null;
    }
}
