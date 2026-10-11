using System.IO;
using System.Text;
using System.Text.Json;

namespace WorkChatMini;

public sealed class StoryResearchStore
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly StoryProjectStore _projects = new();

    public async Task<StoryResearchDocument> LoadAsync(string projectId, CancellationToken cancellationToken = default)
    {
        string path = GetResearchPath(projectId);
        if (!File.Exists(path))
            return new StoryResearchDocument();
        try
        {
            string json = await File.ReadAllTextAsync(path, cancellationToken);
            StoryResearchDocument document = JsonSerializer.Deserialize<StoryResearchDocument>(json, JsonOptions) ?? new StoryResearchDocument();
            document.Notes ??= new List<StoryResearchNote>();
            document.Notes = document.Notes.Where(note => note is not null).ToList();
            foreach (StoryResearchNote note in document.Notes)
            {
                note.Query ??= string.Empty;
                note.KeyFact ??= string.Empty;
                note.Relevance ??= string.Empty;
                note.Confidence ??= "Low";
                note.Sources ??= new List<StoryResearchSource>();
                note.Sources = note.Sources
                    .Where(source => source is not null && IsSafeSourceUrl(source.Url))
                    .ToList();
            }
            return document;
        }
        catch (Exception ex)
        {
            AppLogger.Warn("STORY-RESEARCH", $"Research cache load failed | project={projectId} | {ex.Message}");
            return new StoryResearchDocument();
        }
    }

    public async Task SaveAsync(string projectId, StoryResearchDocument document, CancellationToken cancellationToken = default)
    {
        document.UpdatedUtc = DateTimeOffset.UtcNow;
        string path = GetResearchPath(projectId);
        string directory = Path.GetDirectoryName(path) ?? _projects.GetProjectDirectory(projectId);
        Directory.CreateDirectory(directory);
        string temp = path + ".tmp";
        string json = JsonSerializer.Serialize(document, JsonOptions);
        await File.WriteAllTextAsync(temp, json, cancellationToken);
        File.Move(temp, path, true);
    }

    public static StoryResearchNote? FindFresh(StoryResearchDocument document, string query, TimeSpan maxAge)
    {
        string normalized = NormalizeQuery(query);
        DateTimeOffset cutoff = DateTimeOffset.UtcNow - maxAge;
        return document.Notes
            .Where(note => note.AccessedUtc >= cutoff && NormalizeQuery(note.Query) == normalized)
            .OrderByDescending(note => note.AccessedUtc)
            .FirstOrDefault();
    }

    public static void Upsert(StoryResearchDocument document, StoryResearchNote note)
    {
        string normalized = NormalizeQuery(note.Query);
        document.Notes.RemoveAll(existing => NormalizeQuery(existing.Query) == normalized);
        document.Notes.Insert(0, note);
        if (document.Notes.Count > 80)
            document.Notes.RemoveRange(80, document.Notes.Count - 80);
    }

    public static string BuildContext(IEnumerable<StoryResearchNote> notes)
    {
        var sb = new StringBuilder();
        StoryResearchNote[] selected = notes.Take(6).ToArray();
        if (selected.Length == 0)
            return string.Empty;

        sb.AppendLine("RESEARCH NOTES — lower priority than confirmed story canon/lore. Use for real-world accuracy only; never convert them into story canon automatically.");
        foreach (StoryResearchNote note in selected)
        {
            sb.AppendLine();
            sb.AppendLine($"Query: {note.Query}");
            sb.AppendLine($"Relevance: {note.Relevance}");
            sb.AppendLine($"Confidence: {note.Confidence} · accessed {note.AccessedUtc:yyyy-MM-dd}");
            sb.AppendLine(Trim(note.KeyFact, 3200));
            foreach (StoryResearchSource source in note.Sources.Where(source => IsSafeSourceUrl(source.Url)).Take(5))
                sb.AppendLine($"Source: {source.Title} — {source.Url}");
        }
        string result = sb.ToString().Trim();
        return result.Length <= 12000 ? result : result[..12000] + "\n[Research context truncated]";
    }

    public string GetResearchPath(string projectId) =>
        Path.Combine(_projects.GetProjectDirectory(projectId), "research", "research-cache.json");

    public static bool IsSafeSourceUrl(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out Uri? uri) &&
        (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase) ||
         string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase));

    private static string NormalizeQuery(string query)
    {
        var sb = new StringBuilder(query.Length);
        bool previousSpace = false;
        foreach (char c in query.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c))
            {
                sb.Append(c);
                previousSpace = false;
            }
            else if (!previousSpace)
            {
                sb.Append(' ');
                previousSpace = true;
            }
        }
        return sb.ToString().Trim();
    }

    private static string Trim(string value, int max) => value.Length <= max ? value : value[..max] + "…";
}
