namespace WorkChatMini;

// Parsing helpers for the "recently watched" list. Only https YouTube links with a plausible id are accepted.
internal static class YouTubeLinks
{
    internal static bool TryGetVideo(string? url, out string id, out bool isShort)
    {
        id = string.Empty;
        isShort = false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) || uri.Scheme != Uri.UriSchemeHttps)
            return false;
        string host = uri.Host.ToLowerInvariant();
        string candidate = string.Empty;
        if (host == "youtu.be")
        {
            candidate = uri.AbsolutePath.Trim('/');
        }
        else if (host == "youtube.com" || host.EndsWith(".youtube.com", StringComparison.Ordinal))
        {
            string path = uri.AbsolutePath;
            if (path.StartsWith("/shorts/", StringComparison.Ordinal))
            {
                candidate = path["/shorts/".Length..].Trim('/');
                isShort = true;
            }
            else if (path == "/watch")
            {
                candidate = QueryValue(uri.Query, "v");
            }
        }
        int slash = candidate.IndexOf('/');
        if (slash >= 0)
            candidate = candidate[..slash];
        if (candidate.Length is < 6 or > 20)
            return false;
        foreach (char c in candidate)
            if (!(char.IsAsciiLetterOrDigit(c) || c is '-' or '_'))
                return false;
        id = candidate;
        return true;
    }

    internal static string BuildUrl(string id, bool isShort, bool mobile)
    {
        string host = mobile ? "m.youtube.com" : "www.youtube.com";
        return isShort ? $"https://{host}/shorts/{id}" : $"https://{host}/watch?v={id}";
    }

    // "(3) Song - YouTube" -> "Song"; the bare site title becomes empty so callers can fall back.
    internal static string CleanTitle(string? raw)
    {
        string title = raw?.Trim() ?? string.Empty;
        if (title.StartsWith('('))
        {
            int close = title.IndexOf(')');
            if (close > 1 && title[1..close].All(char.IsDigit))
                title = title[(close + 1)..].TrimStart();
        }
        const string suffix = " - YouTube";
        if (title.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            title = title[..^suffix.Length].TrimEnd();
        return title.Equals("YouTube", StringComparison.OrdinalIgnoreCase) ? string.Empty : title;
    }

    internal static void VerifySelfTest()
    {
        if (!TryGetVideo("https://m.youtube.com/watch?v=dQw4w9WgXcQ&t=10", out string id, out bool isShort) ||
            id != "dQw4w9WgXcQ" || isShort ||
            !TryGetVideo("https://youtu.be/dQw4w9WgXcQ?si=x", out id, out _) || id != "dQw4w9WgXcQ" ||
            !TryGetVideo("https://www.youtube.com/shorts/abcDEF12345", out id, out isShort) || !isShort || id != "abcDEF12345" ||
            TryGetVideo("https://m.youtube.com/", out _, out _) ||
            TryGetVideo("https://evil.example/watch?v=dQw4w9WgXcQ", out _, out _) ||
            TryGetVideo("http://m.youtube.com/watch?v=dQw4w9WgXcQ", out _, out _) ||
            TryGetVideo("https://m.youtube.com/watch?v=../../x", out _, out _) ||
            BuildUrl("dQw4w9WgXcQ", false, true) != "https://m.youtube.com/watch?v=dQw4w9WgXcQ" ||
            CleanTitle("(3) Song name - YouTube") != "Song name" || CleanTitle("YouTube") != "")
            throw new InvalidOperationException("YouTube link parsing is broken.");
    }

    private static string QueryValue(string query, string key)
    {
        foreach (string part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int eq = part.IndexOf('=');
            if (eq > 0 && part[..eq] == key)
                return Uri.UnescapeDataString(part[(eq + 1)..]);
        }
        return string.Empty;
    }
}
