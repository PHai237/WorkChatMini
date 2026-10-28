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
    // Unlike UpdatedAt, this changes only after opening or reading the book.
    public DateTimeOffset? LastReadAt { get; set; }
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
    private string _filePath = LibraryPath;
    private bool _needsMigrationBackup;

    public static ReadingLibraryStore Load() => LoadFromPath(LibraryPath);

    // Self-tests exclusively use a temporary directory, never the live user library.
    internal static ReadingLibraryStore LoadForTest(string path) => LoadFromPath(path);

    private static ReadingLibraryStore LoadFromPath(string path)
    {
        if (!File.Exists(path)) return new ReadingLibraryStore { _filePath = path };
        try
        {
            ReadingLibraryStore result = JsonSerializer.Deserialize<ReadingLibraryStore>(File.ReadAllText(path))
                ?? throw new InvalidDataException("Empty reading library file.");
            result._filePath = path;
            result.Books ??= new List<ReadingBook>();
            // Never silently drop unknown/invalid entries: saving the shortened list would
            // permanently discard them. Keep the original file untouched for recovery.
            if (result.Books.Count > MaxBooks)
                throw new InvalidDataException("Reading library exceeds the 500-book limit.");
            foreach (ReadingBook book in result.Books)
            {
                if (book is null || !IsSafeHttpUrl(book.Url))
                    throw new InvalidDataException("A saved book has an invalid source URL.");
                if (string.IsNullOrWhiteSpace(book.Id))
                {
                    book.Id = Guid.NewGuid().ToString("N");
                    result._needsMigrationBackup = true;
                }
                if (!IsSafeHttpUrl(book.LastReadUrl))
                {
                    // Older versions could omit this field. Preserve the whole book.
                    book.LastReadUrl = book.Url;
                    result._needsMigrationBackup = true;
                }
                double scroll = double.IsFinite(book.ScrollFraction)
                    ? Math.Clamp(book.ScrollFraction, 0, 1) : 0;
                if (scroll != book.ScrollFraction)
                {
                    book.ScrollFraction = scroll;
                    result._needsMigrationBackup = true;
                }
                if (!IsSafeHttpUrl(book.ScrollUrl) && !string.IsNullOrEmpty(book.ScrollUrl))
                {
                    book.ScrollUrl = "";
                    book.ScrollFraction = 0;
                    result._needsMigrationBackup = true;
                }
                string normalizedTarget = ReadingScrollScripts.NormalizeTarget(book.ScrollTarget);
                if (book.ScrollTarget != normalizedTarget)
                {
                    book.ScrollTarget = normalizedTarget;
                    result._needsMigrationBackup = true;
                }
                book.MangaDexLanguage = MangaDexChapterTracker.NormalizeLanguage(book.MangaDexLanguage);
                book.Title ??= "";
                book.Source ??= "Other";
                book.Kind ??= "Novel";
                book.Status ??= "Reading";
                book.ProgressNote ??= "";
            }
            return result;
        }
        catch (Exception ex)
        {
            // A damaged file must not be overwritten with a truncated or empty shelf.
            AppLogger.Warn("READING", $"Reading library load blocked: {ex.Message}");
            return new ReadingLibraryStore { _filePath = path, LoadError = ex.Message };
        }
    }

    public bool Save()
    {
        if (LoadError is not null) return false;
        string temp = _filePath + ".tmp";
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
            if (_needsMigrationBackup && File.Exists(_filePath))
            {
                // Never overwrite the pre-migration copy with an already migrated file.
                string backup = _filePath + ".pre-migration.bak";
                if (!File.Exists(backup)) File.Copy(_filePath, backup);
            }
            string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(temp, json);
            File.Move(temp, _filePath, overwrite: true);
            _needsMigrationBackup = false;
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
        if (host is "webnovel.com" or "www.webnovel.com" or "en.webnovel.com" or "m.webnovel.com") return "Webnovel";
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
