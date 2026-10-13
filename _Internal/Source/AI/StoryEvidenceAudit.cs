using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace WorkChatMini;

// On-demand, read-only review of provenance in committed manuscripts. This
// NEVER promotes, modifies, or rejects a canon fact. A matching excerpt only
// proves the quote exists in chapter prose, not that a model interpreted it well.
internal static class StoryEvidenceAudit
{
    internal const int MaxClaims = 120;
    internal const int MaxChaptersRead = 20;
    private const int MaxFindingsShown = 14;
    private static readonly Regex ChapterReference = new(
        @"^\s*(?:Chapter|Chương)\s*(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    internal sealed record Claim(string Kind, string Label, int? ChapterNumber, string Evidence);
    internal sealed record Finding(Claim Claim, string Result);
    internal sealed record Snapshot(StoryChapterMemory[] Chapters, Claim[] Claims, int TotalClaims);

    internal sealed class Report
    {
        internal int TotalClaims { get; init; }
        internal int CheckedClaims { get; init; }
        internal int ChaptersRead { get; init; }
        internal IReadOnlyList<Finding> Findings { get; init; } = Array.Empty<Finding>();
        internal string? StopReason { get; init; }

        internal string Display()
        {
            var body = new StringBuilder();
            body.AppendLine("READ-ONLY SOURCE AUDIT");
            body.AppendLine("Examines existing Memory references against committed chapter text. No AI requests, canon edits, or storage writes.");
            body.AppendLine();
            if (!string.IsNullOrEmpty(StopReason))
                return body.AppendLine(StopReason).ToString();
            int exact = Findings.Count(f => f.Result == "Exact excerpt");
            int references = Findings.Count(f => f.Result == "Source only");
            int nonExact = Findings.Count(f => f.Result == "Quote not found");
            int missing = Findings.Count(f => f.Result is "Unlinked" or "Chapter missing" or "File unavailable" or "Not scanned");
            body.AppendLine($"Entries checked: {CheckedClaims}/{TotalClaims} · Chapters read: {ChaptersRead}");
            body.AppendLine($"Exact excerpt: {exact} · Source only: {references} · Quote not found: {nonExact} · Missing/not scanned: {missing}");
            if (TotalClaims > CheckedClaims)
                body.AppendLine($"Only the first {MaxClaims} entries were checked. Remaining entries were NOT audited.");
            body.AppendLine();
            body.AppendLine("A matching quote is NOT proof the extracted fact or causal interpretation is correct. 'Quote not found' may be a paraphrase, not necessarily a contradiction. Source-only references are NOT verified quotes.");
            var issues = Findings.Where(f => f.Result != "Exact excerpt")
                .Take(MaxFindingsShown).ToArray();
            if (issues.Length == 0)
                body.AppendLine("No missing citations among the checked entries.");
            else
            {
                body.AppendLine();
                body.AppendLine("REVIEW THESE SOURCES");
                foreach (Finding item in issues)
                {
                    string label = item.Claim.Label.Replace('\n', ' ').Replace('\r', ' ').Trim();
                    if (label.Length > 95) label = label[..95] + "…";
                    body.AppendLine($"• {item.Result} · {item.Claim.Kind} · " +
                        (item.Claim.ChapterNumber is int number ? $"chapter {number}" : "no chapter") + $" · {label}");
                }
                if (Findings.Count(f => f.Result != "Exact excerpt") > issues.Length)
                    body.AppendLine("Further entries omitted from this compact view.");
            }
            body.AppendLine();
            body.AppendLine("Select Overview again to return. This report is temporary and is not used as canon evidence.");
            return body.ToString();
        }
    }

    internal static int? ParseChapter(string? source)
    {
        if (string.IsNullOrWhiteSpace(source)) return null;
        Match match = ChapterReference.Match(source);
        return match.Success && int.TryParse(match.Groups[1].Value, out int number) && number > 0
            ? number : null;
    }

    internal static Snapshot Capture(StoryMemoryDocument memory)
    {
        // Capture immutable lightweight fields before crossing into background IO.
        // Prioritize events and causes; do not silently turn an AI summary into proof.
        var claims = new List<Claim>(MaxClaims);
        int total = 0;
        void Add(string kind, string label, int? chapter, string? evidence)
        {
            total++;
            if (claims.Count < MaxClaims)
                claims.Add(new Claim(kind, label ?? string.Empty, chapter, evidence ?? string.Empty));
        }

        foreach (StorySignificantEvent item in memory.Events.Where(x =>
            !string.Equals(x.Status, "Proposed", StringComparison.OrdinalIgnoreCase)))
            Add("Event", item.Summary, item.ChapterNumber ?? ParseChapter(item.Source), item.Evidence);
        foreach (StoryCausalLink item in memory.CausalLinks)
            Add("Causal link", item.Summary, item.ChapterNumber ?? ParseChapter(item.Source), item.Evidence);
        foreach (StoryNarrativePromise item in memory.NarrativePromises)
            Add("Narrative promise", item.Title, item.IntroducedChapter ?? ParseChapter(item.Source), item.Evidence);
        foreach (StoryTimelineEvent item in memory.Timeline)
            Add("Timeline", item.Event, ParseChapter(item.Source), string.Empty);
        foreach (StoryCanonFact item in memory.Canon.Where(x =>
            !string.Equals(x.Status, "Proposed", StringComparison.OrdinalIgnoreCase)))
            Add("Canon", item.Fact, ParseChapter(item.Source), string.Empty);
        StoryChapterMemory[] chapters = memory.Chapters.Select(c => new StoryChapterMemory
        {
            Number = c.Number, Title = c.Title, FileName = c.FileName
        }).ToArray();
        return new Snapshot(chapters, claims.ToArray(), total);
    }

    internal static string Evaluate(Claim claim, bool chapterListed, string? prose)
    {
        if (claim.ChapterNumber is not int number || number <= 0) return "Unlinked";
        if (!chapterListed) return "Chapter missing";
        if (string.IsNullOrWhiteSpace(prose)) return "File unavailable";
        if (string.IsNullOrWhiteSpace(claim.Evidence)) return "Source only";
        // Evidence is historically a paraphrase. Do not claim that a failed
        // string match disproves a legitimate event.
        return prose.Contains(claim.Evidence.Trim(), StringComparison.OrdinalIgnoreCase)
            ? "Exact excerpt" : "Quote not found";
    }

    internal static async Task<Report> InspectAsync(
        StoryMemoryStore store, string projectId, Snapshot snapshot, CancellationToken token)
    {
        if (snapshot.Chapters.Length == 0 || !StoryMemoryRecovery.HasValidChapterSet(new StoryMemoryDocument
            { Chapters = snapshot.Chapters.ToList() }))
            return new Report { StopReason = "Chapter ledger is empty, unsafe or ambiguous. No files were read. Inspect Storage Health first." };

        var byNumber = snapshot.Chapters.ToDictionary(x => x.Number);
        var bodies = new Dictionary<int, string?>();
        var findings = new List<Finding>();
        foreach (Claim claim in snapshot.Claims)
        {
            token.ThrowIfCancellationRequested();
            int? number = claim.ChapterNumber;
            if (number is null || !byNumber.TryGetValue(number.Value, out StoryChapterMemory? chapter))
            {
                findings.Add(new Finding(claim, Evaluate(claim, chapterListed: false, prose: null)));
                continue;
            }
            if (!bodies.TryGetValue(number.Value, out string? body))
            {
                if (bodies.Count >= MaxChaptersRead)
                {
                    findings.Add(new Finding(claim, "Not scanned"));
                    continue;
                }
                try
                {
                    body = await StoryMemoryRecovery.ReadCommittedChapterAsync(store, projectId, chapter, token)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    body = null; // Never read an orphan, linked or broken chapter.
                }
                bodies[number.Value] = body;
            }
            findings.Add(new Finding(claim, Evaluate(claim, chapterListed: true, prose: body)));
        }
        return new Report
        {
            TotalClaims = snapshot.TotalClaims, CheckedClaims = findings.Count,
            ChaptersRead = bodies.Count, Findings = findings
        };
    }
}
