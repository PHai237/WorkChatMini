using System.IO;

namespace WorkChatMini;

public sealed partial class StoryMemoryStore
{
    // Only called when strict lexical/provenance retrieval returns zero hits.
    // The Bloom filter is a cheap *may contain* screen. Every accepted hit must
    // be verified against an actual whole phrase in committed chapter prose.
    private static async Task<IReadOnlyList<StoryChapterSearchHit>> SearchLocalParaphrasesAsync(
        StoryChapterIndexDocument index, string directory, StoryRetrievalPlan plan,
        int maxResults, CancellationToken cancellationToken)
    {
        IReadOnlyList<string> alternatives = StoryLocalSemanticBridge.Expand(plan.OriginalQuery);
        if (alternatives.Count == 0)
            return Array.Empty<StoryChapterSearchHit>();

        return await Task.Run(() =>
        {
            var results = new List<StoryChapterSearchHit>();
            const int MaxVerifiedCandidates = 24;
            int verifiedCandidates = 0;
            // Prefer newer chapter provenance when several chapters contain the
            // same phrase; do not scan unrelated old chapters just for a guess.
            foreach (StoryChapterIndexEntry entry in index.Chapters.OrderByDescending(x => x.Number))
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (verifiedCandidates >= MaxVerifiedCandidates || results.Count >= Math.Max(1, maxResults))
                    break;
                StoryChapterTokenFilter? signature = StoryChapterTokenFilter.TryRead(entry.TermSignature);
                if (signature is null)
                    continue; // Old/corrupt signatures are rebuilt by RefreshAsync.
                string[] matchingAlternatives = alternatives.Where(phrase =>
                {
                    string[] phraseTerms = StoryMemoryRetriever.Tokenize(phrase).ToArray();
                    return phraseTerms.Length > 0 && phraseTerms.All(signature.MightContain);
                }).ToArray();
                if (matchingAlternatives.Length == 0)
                    continue;

                verifiedCandidates++;
                string path = Path.Combine(directory, entry.FileName);
                string text;
                try { text = ReadChapterTextCached(path); }
                catch (IOException) { continue; }
                catch (UnauthorizedAccessException) { continue; }
                // Match entire phrases, not a shared token or a Bloom false positive.
                string? evidence = matchingAlternatives.FirstOrDefault(phrase =>
                    StoryLocalSemanticBridge.ContainsWholePhrase(text, phrase));
                if (evidence is null)
                    continue;
                // If a character has already been identified unambiguously,
                // never infer that a paraphrased event concerns someone else.
                if (plan.ResolvedEntityTerms.Count > 0 &&
                    !plan.ResolvedEntityTerms.All(name => StoryLocalSemanticBridge.ContainsWholePhrase(text, name)))
                    continue;
                string excerpt = StoryChapterExcerptSelector.Select(text,
                    new[] { evidence }, Array.Empty<string>(), Array.Empty<string>());
                results.Add(new StoryChapterSearchHit
                {
                    Number = entry.Number,
                    Title = entry.Title,
                    // Label this as a possible paraphrase, not proof that the
                    // alternate phrase means the same thing in this story.
                    Excerpt = $"[Local paraphrase candidate: phrase '{evidence}' occurs in chapter prose; verify meaning against context.]\n" + excerpt,
                    Score = 1
                });
            }
            return (IReadOnlyList<StoryChapterSearchHit>)results;
        }, cancellationToken);
    }
}
