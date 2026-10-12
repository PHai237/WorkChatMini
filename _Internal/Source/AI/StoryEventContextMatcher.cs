using System.Text.RegularExpressions;

namespace WorkChatMini;

// A narrow, deterministic event lookup. It ranks existing CONFIRMED ledger
// records, never invents an event, and never turns lexical similarity into canon.
internal static class StoryEventContextMatcher
{
    internal sealed record Match(StorySignificantEvent Event, int Score);

    private static readonly string[] ItemLoss = { "mất", "mat", "đánh rơi", "danh roi", "bị cướp", "bi cuop", "lost", "stolen" };
    private static readonly string[] AbilityLoss = { "mất năng lực", "mat nang luc", "mất kỹ năng", "mat ky nang", "lost ability", "lost skill" };
    private static readonly string[] AbilityGain = { "học được", "hoc duoc", "học chiêu", "hoc chieu", "lĩnh hội", "linh hoi", "learned", "mastered" };
    private static readonly string[] ItemGain = { "nhặt được", "nhat duoc", "nhận được", "nhan duoc", "thu được", "thu duoc", "picked up", "acquired", "obtained" };
    private static readonly string[] Death = { "chết", "chet", "qua đời", "qua doi", "hy sinh", "died", "killed" };
    private static readonly string[] Injury = { "bị thương", "bi thuong", "trọng thương", "trong thuong", "injured", "wounded" };
    private static readonly string[] Discovery = { "phát hiện", "phat hien", "khám phá", "kham pha", "discovered", "found out" };

    private static readonly HashSet<string> Uninformative = new(StringComparer.OrdinalIgnoreCase)
    {
        "mất", "mat", "đánh", "rơi", "danh", "roi", "bị", "bi", "cướp", "cuop", "lost", "stolen",
        "năng", "lực", "nang", "luc", "học", "được", "hoc", "duoc", "lĩnh", "hội", "linh", "hoi",
        "learned", "mastered", "nhặt", "nhat", "nhận", "nhan", "thu", "picked", "acquired", "obtained",
        "chết", "chet", "qua", "đời", "doi", "hy", "sinh", "died", "killed", "thương", "thuong",
        "injured", "wounded", "phát", "hiện", "phat", "hien", "khám", "phá", "kham", "pha", "discovered",
        "ở", "đâu", "khi", "nào", "bao", "giờ", "chuong", "chương", "chapter", "may", "mấy", "lần",
        "where", "when", "which", "what", "was", "were", "did", "how", "whose", "who", "the", "does", "that"
    };

    internal static bool HasActionIntent(string query) => ResolveType(query) is not null;

    internal static IReadOnlyList<Match> Find(StoryMemoryDocument memory, string query,
        IReadOnlyList<string> resolvedCharacters)
    {
        string? type = ResolveType(query);
        if (type is null || string.IsNullOrWhiteSpace(query))
            return Array.Empty<Match>();

        // A shared/ambiguous nickname may not be assigned to a character.
        // If the resolver flagged one, do not invoke the contextual path.
        StoryAliasResolver.Result aliases = StoryAliasResolver.Resolve(memory, query);
        if (aliases.Reasons.Any(x => x.StartsWith("Ambiguous character alias", StringComparison.Ordinal)))
            return Array.Empty<Match>();

        string[] actorWords = resolvedCharacters.Concat(memory.Characters
            .Where(c => resolvedCharacters.Contains(c.Name, StringComparer.OrdinalIgnoreCase))
            .SelectMany(c => c.Aliases)).ToArray();
        string[] relevant = StoryMemoryRetriever.Tokenize(query)
            .Except(StoryMemoryRetriever.Tokenize(string.Join(" ", actorWords)), StringComparer.OrdinalIgnoreCase)
            .Where(x => !Uninformative.Contains(x) && x.Length >= 3 && !int.TryParse(x, out _))
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(16).ToArray();
        int? exactChapter = ExplicitChapter(query);
        HashSet<int> committed = StoryCommittedEvidence.UniqueChapters(memory);
        HashSet<string> duplicateIds = StoryCommittedEvidence.DuplicateEventIds(memory);
        var matches = new List<Match>();
        foreach (StorySignificantEvent item in memory.Events)
        {
            if (!StoryCommittedEvidence.IsVerifiedEvent(item, committed, duplicateIds) ||
                !string.Equals(item.Type, type, StringComparison.OrdinalIgnoreCase) ||
                item.ChapterNumber is not int chapter || chapter <= 0 ||
                (exactChapter.HasValue && exactChapter.Value != chapter))
                continue;

            // Match structured subject/target/related cast, not mere mentions
            // of a name somewhere in an unrelated event's prose summary.
            if (resolvedCharacters.Count > 0 && resolvedCharacters.Any(name =>
                !Same(item.Subject, name) && !Same(item.Target, name) &&
                !item.RelatedCharacters.Any(c => Same(c, name))))
                continue;

            string evidence = string.Join(" ", item.Target, item.Summary, item.Before,
                item.After, item.Evidence, item.When, string.Join(" ", item.Tags));
            int objectHits = relevant.Count(token => StoryLocalSemanticBridge.ContainsWholePhrase(evidence, token));
            // If a question specifies a concrete object or clue, action and
            // character alone do not prove this was the event being sought.
            if (relevant.Length > 0 && objectHits == 0)
                continue;

            int score = 12 + objectHits * 5 + (resolvedCharacters.Count > 0 ? 8 : 0) +
                        (exactChapter.HasValue ? 8 : 0);
            matches.Add(new Match(item, score));
        }
        return matches.OrderByDescending(x => x.Score)
            .ThenByDescending(x => x.Event.ChapterNumber).Take(6).ToArray();
    }

    private static string? ResolveType(string query)
    {
        if (string.IsNullOrWhiteSpace(query)) return null;
        if (ContainsAny(query, AbilityLoss)) return "AbilityLost";
        if (ContainsAny(query, AbilityGain)) return "AbilityLearned";
        if (ContainsAny(query, ItemGain)) return "ItemAcquired";
        if (ContainsAny(query, Death)) return "Death";
        if (ContainsAny(query, Injury)) return "Injury";
        if (ContainsAny(query, Discovery)) return "Discovery";
        // "mất tích" means disappearance, not loss of an inventory item.
        if (ContainsAny(query, ItemLoss) && !ContainsAny(query,
            new[] { "mất tích", "mat tich", "mất dấu", "mat dau", "disappeared", "vanished" }))
            return "ItemLost";
        return null;
    }

    private static bool ContainsAny(string input, IReadOnlyList<string> phrases) =>
        phrases.Any(x => StoryLocalSemanticBridge.ContainsWholePhrase(input, x));

    private static int? ExplicitChapter(string query)
    {
        System.Text.RegularExpressions.Match match = Regex.Match(query, @"(?:chapter|chương|chap\.?|ch\.?)\s*[#:\-]?\s*(\d{1,5})",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        return match.Success && int.TryParse(match.Groups[1].Value, out int chapter) && chapter > 0
            ? chapter : null;
    }

    private static bool Same(string left, string right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);
}
