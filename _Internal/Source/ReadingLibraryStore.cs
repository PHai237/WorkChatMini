using System.IO;
using System.Text.Json;

namespace WorkChatMini;

// A local, source-independent shelf. No chapter scraping, credentials or page content is stored.
public sealed class ReadingBook
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Title { get; set; } = "";
    public string Url { get; set; } = "";
    public string LastReadUrl { get; set; } = "";
    public string Source { get; set; } = "Other";
    public string Kind { get; set; } = "Novel";
    public string Status { get; set; } = "Reading";
    public string ProgressNote { get; set; } = "";
    public bool OpenTab { get; set; } = true;
    public bool IsFavorite { get; set; }
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    // Scroll is best-effort (websites with custom scroll panes may not expose document scroll).
    public string ScrollUrl { get; set; } = "";
    public double ScrollFraction { get; set; }
    public string ScrollTarget { get; set; } = "document";
    // MangaDex metadata is separate from the reading position and never stores chapter images.
    public string MangaDexMangaId { get; set; } = "";
    public string MangaDexLanguage { get; set; } = "all";
    public string LatestKnownChapterId { get; set; } = "";
    public string LatestKnownChapterLabel { get; set; } = "";
    public DateTimeOffset? LastChapterCheckUtc { get; set; }
    public bool HasNewChapter { get; set; }
}

public sealed class ReadingLibraryStore
{
    public const int MaxBooks = 500;
    public List<ReadingBook> Books { get; set; } = new();
    public string? LoadError { get; private set; }

    private static string LibraryPath => Path.Combine(AppSettings.AppDataDirectory, "Reading", "library.json");

    public static ReadingLibraryStore Load()
    {
        if (!File.Exists(LibraryPath))
            return new ReadingLibraryStore();
        try
        {
            ReadingLibraryStore result = JsonSerializer.Deserialize<ReadingLibraryStore>(File.ReadAllText(LibraryPath))
                ?? throw new InvalidDataException("Empty reading library file.");
            result.Books ??= new List<ReadingBook>();
            result.Books = result.Books
                .Where(book => book is not null && !string.IsNullOrWhiteSpace(book.Id) &&
                               IsSafeHttpUrl(book.Url) && IsSafeHttpUrl(book.LastReadUrl))
                .Take(MaxBooks).ToList();
            foreach (ReadingBook book in result.Books)
            {
                book.ScrollFraction = double.IsFinite(book.ScrollFraction)
                    ? Math.Clamp(book.ScrollFraction, 0, 1) : 0;
                book.MangaDexLanguage = MangaDexChapterTracker.NormalizeLanguage(book.MangaDexLanguage);
                if (!IsSafeHttpUrl(book.ScrollUrl)) book.ScrollUrl = "";
                book.ScrollTarget = ReadingScrollScripts.NormalizeTarget(book.ScrollTarget);
            }
            return result;
        }
        catch (Exception ex)
        {
            // Do not destroy a damaged file by saving an empty shelf on top of it.
            return new ReadingLibraryStore { LoadError = ex.Message };
        }
    }

    public bool Save()
    {
        if (LoadError is not null) return false;
        string temp = LibraryPath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(LibraryPath)!);
            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(temp, json);
            File.Move(temp, LibraryPath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("READING", $"Library save failed: {ex.Message}");
            return false;
        }
    }

    public static bool IsSafeHttpUrl(string? value) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= 2048 &&
        Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) &&
        (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps) &&
        !string.IsNullOrWhiteSpace(uri.Host) && !uri.IsLoopback;

    public static string DetectSource(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri)) return "Other";
        string host = uri.Host.ToLowerInvariant();
        if (host is "mangadex.org" or "www.mangadex.org") return "MangaDex";
        if (host is "webnovel.com" or "www.webnovel.com") return "Webnovel";
        if (host is "docln.net" or "www.docln.net" or "docln.sbs" or "www.docln.sbs") return "Hako / DocLN";
        return host;
    }

    public static string DetectKind(string source) => source switch
    {
        "MangaDex" => "Manga",
        "Webnovel" => "Web novel",
        "Hako / DocLN" => "Light novel",
        _ => "Novel / Other"
    };
}
