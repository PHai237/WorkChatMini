using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace WorkChatMini;

// A small, read-only MangaDex metadata client. No pages, images or login data are fetched.
// Checks are user-initiated and individually throttled by the Reading Hub.
public sealed record MangaDexChapterSnapshot(string MangaId, string ChapterId, string Label);

public static class MangaDexChapterTracker
{
    private static readonly HttpClient Client = CreateClient();

    private static HttpClient CreateClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromSeconds(12) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("WorkChatMini/2.60.1 (+https://github.com/PHai237/WorkChatMini)");
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    public static bool TryParseMangaDexUrl(string? url, out string id, out bool isChapter)
    {
        id = "";
        isChapter = false;
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) ||
            uri.Scheme != Uri.UriSchemeHttps || uri.UserInfo.Length != 0 ||
            !(string.Equals(uri.Host, "mangadex.org", StringComparison.OrdinalIgnoreCase) ||
              string.Equals(uri.Host, "www.mangadex.org", StringComparison.OrdinalIgnoreCase))) return false;
        string[] parts = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length < 2 || parts[0] is not ("title" or "manga" or "chapter") ||
            !Guid.TryParseExact(parts[1], "D", out Guid parsed)) return false;
        id = parsed.ToString("D");
        isChapter = parts[0] == "chapter";
        return true;
    }

    public static string NormalizeLanguage(string? language) =>
        language is "vi" or "en" ? language : "all";

    // Empty baseline never becomes an unread alert on the first successful check.
    public static bool IsNewChapter(string? previousId, string currentId) =>
        !string.IsNullOrEmpty(previousId) &&
        !string.Equals(previousId, currentId, StringComparison.Ordinal);

    public static string? ExtractMangaIdFromChapter(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out JsonElement data) ||
            data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("relationships", out JsonElement relationships) ||
            relationships.ValueKind != JsonValueKind.Array) return null;
        foreach (JsonElement relation in relationships.EnumerateArray())
        {
            if (relation.TryGetProperty("type", out JsonElement type) && type.GetString() == "manga" &&
                relation.TryGetProperty("id", out JsonElement value) &&
                Guid.TryParseExact(value.GetString(), "D", out Guid id))
                return id.ToString("D");
        }
        return null;
    }

    public static MangaDexChapterSnapshot? ParseFeed(string mangaId, string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("data", out JsonElement items) ||
            items.ValueKind != JsonValueKind.Array) throw new FormatException("Invalid MangaDex chapter feed.");
        foreach (JsonElement entry in items.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object ||
                !entry.TryGetProperty("id", out JsonElement rawId) ||
                !Guid.TryParseExact(rawId.GetString(), "D", out Guid chapterId)) continue;
            if (!entry.TryGetProperty("attributes", out JsonElement attributes) ||
                attributes.ValueKind != JsonValueKind.Object) continue;
            string? chapter = attributes.TryGetProperty("chapter", out JsonElement rawChapter) &&
                              rawChapter.ValueKind == JsonValueKind.String ? rawChapter.GetString() : null;
            string? title = attributes.TryGetProperty("title", out JsonElement rawTitle) &&
                            rawTitle.ValueKind == JsonValueKind.String ? rawTitle.GetString() : null;
            string label = !string.IsNullOrWhiteSpace(chapter) ? $"Ch. {chapter}" :
                           !string.IsNullOrWhiteSpace(title) ? title : "Oneshot / unnumbered";
            return new MangaDexChapterSnapshot(mangaId, chapterId.ToString("D"), label.Length > 90 ? label[..90] : label);
        }
        return null;
    }

    public static async Task<MangaDexChapterSnapshot?> CheckAsync(
        string bookUrl, string? knownMangaId, string language, CancellationToken cancellationToken = default)
    {
        if (!TryParseMangaDexUrl(bookUrl, out string id, out bool isChapter))
            throw new ArgumentException("Save a MangaDex title or chapter URL before checking updates.");

        string mangaId = knownMangaId is not null &&
                         Guid.TryParseExact(knownMangaId, "D", out Guid known) ? known.ToString("D") : id;
        if (isChapter && mangaId == id)
        {
            string detail = await FetchJsonAsync($"https://api.mangadex.org/chapter/{id}", cancellationToken);
            mangaId = ExtractMangaIdFromChapter(detail) ??
                throw new FormatException("MangaDex did not identify the manga for this chapter.");
        }
        string lang = NormalizeLanguage(language);
        string filter = lang == "all" ? "" : $"&translatedLanguage%5B%5D={lang}";
        string feed = await FetchJsonAsync(
            $"https://api.mangadex.org/manga/{mangaId}/feed?limit=1&order%5BpublishAt%5D=desc{filter}",
            cancellationToken);
        return ParseFeed(mangaId, feed);
    }

    private static async Task<string> FetchJsonAsync(string url, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await Client.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
            throw new HttpRequestException("MangaDex rate limit reached. Try again later.");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }
}
