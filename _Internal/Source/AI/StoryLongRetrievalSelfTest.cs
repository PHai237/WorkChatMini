using System.IO;
using System.Text.Json;

namespace WorkChatMini;

internal static class StoryLongRetrievalSelfTest
{
    internal static async Task VerifyAsync()
    {
        var projects = new StoryProjectStore();
        var memories = new StoryMemoryStore();
        StoryProject project = await projects.CreateProjectAsync($"SelfTest Long Retrieval {Guid.NewGuid():N}");
        try
        {
            var memory = new StoryMemoryDocument();
            string longText = string.Join(" ", Enumerable.Range(0, 2500).Select(i => $"unusedterm{i:0000}")) +
                              " LastPageSecretBeacon";
            StoryChapterMemory? chapter = await memories.AppendChapterAsync(
                project.Id, memory, "Extended chapter", "Summary without the secret", longText);
            if (chapter is null)
                throw new InvalidOperationException("Long chapter fixture not created.");

            // Retrieval now trusts the committed manuscript ledger, never raw
            // orphan files left behind by an interrupted chapter operation.
            await memories.SaveAsync(project.Id, memory);

            async Task RegisterFixtureChapterAsync(int number, string path)
            {
                memory.Chapters.Add(new StoryChapterMemory
                {
                    Number = number,
                    FileName = Path.GetFileName(path),
                    Title = "Self-test chapter " + number
                });
                await memories.SaveAsync(project.Id, memory);
            }

            // A v1 sample-only index should be rebuilt into schema v3, whose
            // fixed-size signature covers *all* chapter tokens. Terms remains
            // a bounded sample and need not contain a late-chapter clue.
            string indexPath = Path.Combine(projects.GetProjectDirectory(project.Id), "memory", "chapter-index.json");
            Directory.CreateDirectory(Path.GetDirectoryName(indexPath) ?? projects.GetProjectDirectory(project.Id));
            var oldIndex = new StoryChapterIndexDocument
            {
                SchemaVersion = 1,
                Chapters = new List<StoryChapterIndexEntry>
                {
                    new()
                    {
                        Number = chapter.Number,
                        FileName = chapter.FileName,
                        LastWriteTicks = File.GetLastWriteTimeUtc(Path.Combine(memories.GetChaptersDirectory(project.Id), chapter.FileName)).Ticks,
                        Terms = new List<string> { "unusedterm0000" }
                    }
                }
            };
            await File.WriteAllTextAsync(indexPath, JsonSerializer.Serialize(oldIndex));

            IReadOnlyList<StoryChapterSearchHit> hits = await memories.SearchChapterTextAsync(project.Id, "LastPageSecretBeacon");
            if (hits.Count == 0 || !hits[0].Excerpt.Contains("LastPageSecretBeacon", StringComparison.Ordinal))
                throw new InvalidOperationException("Late chapter clue missed by indexed retrieval.");
            IReadOnlyList<StoryChapterSearchHit> unrelated = await memories.SearchChapterTextAsync(
                project.Id, "KeywordWithNoOverlapAnywhere");
            if (unrelated.Count > 0)
                throw new InvalidOperationException("Unrelated recent chapter was incorrectly returned as evidence.");
            StoryChapterIndexDocument? upgraded = JsonSerializer.Deserialize<StoryChapterIndexDocument>(
                await File.ReadAllTextAsync(indexPath));
            StoryChapterIndexEntry? upgradedChapter = upgraded?.Chapters.SingleOrDefault();
            StoryChapterTokenFilter? fullChapterSignature =
                StoryChapterTokenFilter.TryRead(upgradedChapter?.TermSignature);
            if (upgraded?.SchemaVersion != 3 ||
                upgradedChapter is null ||
                upgradedChapter.Terms.Count is 0 or > 1800 ||
                fullChapterSignature is null ||
                !fullChapterSignature.MightContain("unusedterm0000") ||
                !fullChapterSignature.MightContain("unusedterm1250") ||
                !fullChapterSignature.MightContain("unusedterm2499") ||
                !fullChapterSignature.MightContain("lastpagesecretbeacon"))
                throw new InvalidOperationException("Legacy index was not upgraded to a valid full-chapter v3 signature.");

            // Reusing chapter filenames on an alternate must not expose previously cached prose.
            string chapterPath = Path.Combine(memories.GetChaptersDirectory(project.Id), chapter.FileName);
            string revisedText = (await File.ReadAllTextAsync(chapterPath)).Replace(
                "LastPageSecretBeacon", "AlternateBranchBeacon", StringComparison.Ordinal);
            await File.WriteAllTextAsync(chapterPath, revisedText);
            StoryMemoryStore.InvalidateChapterCache(project.Id);
            IReadOnlyList<StoryChapterSearchHit> oldHits = await memories.SearchChapterTextAsync(project.Id, "LastPageSecretBeacon");
            if (oldHits.Any(x => x.Excerpt.Contains("LastPageSecretBeacon", StringComparison.Ordinal)))
                throw new InvalidOperationException("Old branch text leaked through retrieval caches.");
            IReadOnlyList<StoryChapterSearchHit> newHits = await memories.SearchChapterTextAsync(project.Id, "AlternateBranchBeacon");
            if (!newHits.Any(x => x.Excerpt.Contains("AlternateBranchBeacon", StringComparison.Ordinal)))
                throw new InvalidOperationException("New branch text was not reindexed after restore.");

            // Many different chapter reads must not turn into an unlimited global RAM cache.
            for (int i = 0; i < 42; i++)
            {
                string path = Path.Combine(memories.GetChaptersDirectory(project.Id), $"cache-fixture-{i:000}.md");
                await File.WriteAllTextAsync(path, "# Cache fixture\nSmall text " + i);
                if (!StoryChapterTextCache.Read(path).Contains("Small text", StringComparison.Ordinal))
                    throw new InvalidOperationException("Bounded chapter cache returned invalid text.");
            }
            if (StoryChapterTextCache.CachedEntries > 32)
                throw new InvalidOperationException("Chapter text cache exceeded its entry limit.");

            // Alias resolution uses a canonical identity, but shared nicknames
            // cannot silently bind to one of several characters.
            var aliasMemory = new StoryMemoryDocument();
            aliasMemory.Characters.Add(new StoryCharacterMemory
            {
                Name = "Magica", Aliases = new List<string> { "Silver Crow", "The Watcher" }
            });
            aliasMemory.Characters.Add(new StoryCharacterMemory
            {
                Name = "Luna", Aliases = new List<string> { "The Watcher" }
            });
            StoryRetrievalPlan aliasPlan = StoryRetrievalPlanner.Build(aliasMemory, "Where did Silver Crow hide the prism?");
            if (!aliasPlan.Terms.Contains("magica", StringComparer.OrdinalIgnoreCase) ||
                !aliasPlan.ResolvedEntityTerms.Contains("magica", StringComparer.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unique character alias failed to resolve to its canonical name.");
            StoryAliasResolver.Result shared = StoryAliasResolver.Resolve(aliasMemory, "What happened to The Watcher?");
            if (shared.Names.Count != 0 || shared.Terms.Count != 0)
                throw new InvalidOperationException("A shared alias was wrongly assigned to one character.");
            StoryAliasResolver.Result partial = StoryAliasResolver.Resolve(aliasMemory, "Where is the Silver Crowbar?");
            if (partial.Names.Count != 0)
                throw new InvalidOperationException("An alias matched inside a larger unrelated word.");

            // The best evidence excerpt should contain the rare requested clue
            // even when a character's name appears much earlier in the chapter.
            string prose = "Magica walked across the courtyard. " +
                           new string('z', 4300) +
                           " Magica discovered the FracturedMirrorSeal beside the old gate.";
            string excerpt = StoryChapterExcerptSelector.Select(prose,
                new[] { "magica", "fracturedmirrorseal" }, Array.Empty<string>(), new[] { "courtyard" });
            if (!excerpt.Contains("FracturedMirrorSeal", StringComparison.Ordinal))
                throw new InvalidOperationException("Evidence excerpt picked an early weak hit instead of the late clue.");

            // Rare query terms should break ties in a large indexed corpus.
            var syntheticIndex = new StoryChapterIndexDocument();
            for (int i = 1; i <= 30; i++)
                syntheticIndex.Chapters.Add(new StoryChapterIndexEntry
                {
                    Number = i, FileName = $"{i:0000}-sample.md", Title = "A journey",
                    Terms = i == 4 ? new List<string> { "magica", "fracturedmirrorseal" }
                                   : new List<string> { "magica", "journey" }
                });
            var rarityPlan = new StoryRetrievalPlan
            {
                Terms = new[] { "magica", "fracturedmirrorseal" },
                DirectTerms = new[] { "magica", "fracturedmirrorseal" }
            };
            if (StoryChapterIndexStore.FindCandidates(syntheticIndex, rarityPlan, 5).FirstOrDefault()?.Number != 4)
                throw new InvalidOperationException("Rare direct query clue was not prioritized.");

            // Large-story ranking reads each disposable signature once per query.
            // The full-chapter filter must find clues omitted by bounded term samples.
            var largeIndex = new StoryChapterIndexDocument();
            for (int i = 1; i <= 360; i++)
            {
                string proseForIndex = i == 144
                    ? "commonmarker rarehiddenbeacon forgotten attic"
                    : "commonmarker everyday travel discussion";
                largeIndex.Chapters.Add(new StoryChapterIndexEntry
                {
                    Number = i,
                    FileName = $"{i:0000}-sample.md",
                    Title = "Untitled",
                    Terms = new List<string> { "commonmarker" },
                    TermSignature = StoryChapterTokenFilter.Build(proseForIndex).Serialize()
                });
            }
            var hiddenPlan = new StoryRetrievalPlan
            {
                Terms = new[] { "commonmarker", "rarehiddenbeacon" },
                DirectTerms = new[] { "rarehiddenbeacon" }
            };
            IReadOnlyList<StoryChapterIndexCandidate> hiddenMatches =
                StoryChapterIndexStore.FindCandidates(largeIndex, hiddenPlan, 4);
            if (hiddenMatches.Count == 0 || hiddenMatches[0].Number != 144)
                throw new InvalidOperationException("Rare full-text clue lost priority in the large chapter index.");
            if (StoryChapterIndexStore.FindCandidates(largeIndex, new StoryRetrievalPlan
                { Terms = new[] { "unmentionedquerytoken" }, DirectTerms = new[] { "unmentionedquerytoken" } }, 4).Count > 0)
                throw new InvalidOperationException("Unrelated chapters were returned for a specific query.");
            IReadOnlyList<StoryChapterIndexCandidate> recentMatches =
                StoryChapterIndexStore.FindCandidates(largeIndex,
                    new StoryRetrievalPlan { PreferRecentChapters = true }, 5);
            if (recentMatches.Count != 3 || recentMatches[0].Number != 360)
                throw new InvalidOperationException("Empty Continue Story lost its recent-chapter fallback.");

            // One active project's index should not pin every story visited in RAM.
            var smallCache = new StoryChapterIndexMemoryCache();
            for (int i = 0; i < 6; i++)
                smallCache.Put($"temporary-index-{i}", new StoryChapterIndexDocument());
            if (!smallCache.TryGet("temporary-index-0", out _))
                throw new InvalidOperationException("Recent-index cache missed a stored project.");
            smallCache.Put("temporary-index-6", new StoryChapterIndexDocument());
            if (smallCache.Count != 6 || smallCache.TryGet("temporary-index-1", out _) ||
                !smallCache.TryGet("temporary-index-0", out _))
                throw new InvalidOperationException("Bounded chapter-index cache did not evict the least recently used project.");
            smallCache.Remove("temporary-index-0");
            if (smallCache.TryGet("temporary-index-0", out _) || smallCache.Count != 5)
                throw new InvalidOperationException("Chapter-index cache invalidation did not remove the project.");

            // Follow cross-chapter causal evidence only via explicit event IDs.
            var causalMemory = new StoryMemoryDocument();
            causalMemory.Chapters.Add(new StoryChapterMemory { Number = 3 });
            causalMemory.Chapters.Add(new StoryChapterMemory { Number = 17 });
            causalMemory.Events.Add(new StorySignificantEvent
            {
                Id = "event-relic-discovery", Type = "Discovery", ChapterNumber = 3,
                Summary = "Magica finds the lunar relic"
            });
            causalMemory.Events.Add(new StorySignificantEvent
            {
                Id = "event-ward-opened", Type = "Discovery", ChapterNumber = 17,
                Summary = "The lunar ward opens"
            });
            causalMemory.CausalLinks.Add(new StoryCausalLink
            {
                From = "event-relic-discovery", To = "event-ward-opened",
                Relation = "ENABLES", Summary = "Magica relic unlocks a lunar ward", ChapterNumber = 17
            });
            StoryRetrievalPlan chainPlan = StoryRetrievalPlanner.Build(causalMemory, "Magica relic cause");
            if (!chainPlan.ChapterBoosts.ContainsKey(3) || !chainPlan.ChapterBoosts.ContainsKey(17))
                throw new InvalidOperationException("Explicit causal event provenance across chapters was lost.");

            // A causal trail is grounded in Confirmed events linked explicitly
            // across chapters; neither chronology nor a matching item alone
            // justifies connecting another character's history.
            var tracedMemory = new StoryMemoryDocument();
            foreach (int committedNumber in new[] { 3, 9, 12, 27 })
                tracedMemory.Chapters.Add(new StoryChapterMemory { Number = committedNumber });
            tracedMemory.Characters.Add(new StoryCharacterMemory
            {
                Name = "Magica", Aliases = new List<string> { "Silver Crow", "The Watcher" }
            });
            tracedMemory.Characters.Add(new StoryCharacterMemory
            {
                Name = "Luna", Aliases = new List<string> { "The Watcher" }
            });
            tracedMemory.Events.Add(new StorySignificantEvent
            {
                Id = "lost-compass", Type = "ItemLost", Subject = "Magica",
                Target = "obsidian compass", Summary = "Magica lost the obsidian compass", ChapterNumber = 3
            });
            tracedMemory.Events.Add(new StorySignificantEvent
            {
                Id = "found-gate", Type = "Discovery", Subject = "Magica",
                Target = "hidden gate", Summary = "Magica discovered the hidden gate", ChapterNumber = 12
            });
            tracedMemory.Events.Add(new StorySignificantEvent
            {
                Id = "learned-navigation", Type = "AbilityLearned", Subject = "Magica",
                Target = "astral navigation", Summary = "Magica learned astral navigation", ChapterNumber = 27
            });
            tracedMemory.Events.Add(new StorySignificantEvent
            {
                Id = "luna-compass", Type = "ItemLost", Subject = "Luna",
                Target = "obsidian compass", Summary = "Luna lost an obsidian compass", ChapterNumber = 9
            });
            tracedMemory.Events.Add(new StorySignificantEvent
            {
                Id = "unconfirmed-secret", Type = "Discovery", Subject = "Magica", Status = "Proposed",
                Summary = "Magica unlocked an unconfirmed chamber", ChapterNumber = 44
            });
            tracedMemory.CausalLinks.Add(new StoryCausalLink
            {
                Id = "trace-link-a", From = "lost-compass", To = "found-gate",
                Relation = "MOTIVATES", ChapterNumber = 12
            });
            tracedMemory.CausalLinks.Add(new StoryCausalLink
            {
                Id = "trace-link-b", From = "found-gate", To = "learned-navigation",
                Relation = "ENABLES", ChapterNumber = 27
            });
            tracedMemory.CausalLinks.Add(new StoryCausalLink
            {
                Id = "unconfirmed-link", From = "found-gate", To = "unconfirmed-secret",
                Relation = "REVEALS", ChapterNumber = 44
            });
            tracedMemory.CausalLinks.Add(new StoryCausalLink
            {
                Id = "unsupported-link", From = "luna-compass", To = "learned-navigation",
                Relation = "MIGHT_CAUSE", ChapterNumber = 99
            });
            StoryRetrievalPlan chain = StoryRetrievalPlanner.Build(
                tracedMemory, "Magica lost obsidian compass");
            if (chain.VerifiedCausalContext.Length == 0 ||
                !chain.ChapterBoosts.ContainsKey(3) ||
                !chain.ChapterBoosts.ContainsKey(12) ||
                !chain.ChapterBoosts.ContainsKey(27) ||
                chain.VerifiedCausalContext.Contains("unconfirmed chamber", StringComparison.Ordinal) ||
                chain.VerifiedCausalContext.Contains("Luna lost", StringComparison.Ordinal))
                throw new InvalidOperationException("Grounded two-hop causal provenance was not isolated.");
            string tracedContext = StoryMemoryRetriever.BuildContext(
                tracedMemory, chain.ExpandedQuery, retrievalPlan: chain);
            if (!tracedContext.Contains("VERIFIED CAUSAL EVENT CHAINS", StringComparison.Ordinal) ||
                !tracedContext.Contains("MOTIVATES", StringComparison.Ordinal) ||
                !tracedContext.Contains("ENABLES", StringComparison.Ordinal))
                throw new InvalidOperationException("Causal event chain was not included in writer context.");
            StoryRetrievalPlan unrelatedChain = StoryRetrievalPlanner.Build(
                tracedMemory, "Luna lost obsidian compass");
            if (unrelatedChain.VerifiedCausalContext.Length != 0 ||
                StoryRetrievalPlanner.Build(tracedMemory, "The Watcher lost obsidian compass")
                    .VerifiedCausalContext.Length != 0 ||
                StoryRetrievalPlanner.Build(tracedMemory, "tiếp tục", autoContinue: true)
                    .VerifiedCausalContext.Length != 0)
                throw new InvalidOperationException("Causal chain leaked through another actor or weak prompt.");
            tracedMemory.Events.Add(new StorySignificantEvent
            {
                Id = "lost-compass", Type = "ItemLost", Subject = "Magica",
                Target = "obsidian compass", Summary = "Duplicate event identifier", ChapterNumber = 50
            });
            if (StoryRetrievalPlanner.Build(tracedMemory, "Magica lost obsidian compass")
                    .VerifiedCausalContext.Length != 0)
                throw new InvalidOperationException("Duplicate event ID was accepted as causal provenance.");

            // An unsampled mid-chapter secret must still be discoverable. The
            // fixed-size filter sees all tokens, not merely sampled windows.
            string hidden = "DeepMiddleAsterCipher";
            string midText = string.Join(" ", Enumerable.Range(0, 14000).Select(i => $"filler{i:00000}"));
            int midpoint = midText.Length / 4;
            midText = midText.Insert(midpoint, " " + hidden + " ");
            string midPath = Path.Combine(memories.GetChaptersDirectory(project.Id), "0060-middle.md");
            await File.WriteAllTextAsync(midPath, "# Middle\n\n" + midText);
            await RegisterFixtureChapterAsync(60, midPath);
            StoryMemoryStore.InvalidateChapterCache(project.Id);
            IReadOnlyList<StoryChapterSearchHit> middleHits = await memories.SearchChapterTextAsync(project.Id, hidden);
            if (!middleHits.Any(x => x.Number == 60 && x.Excerpt.Contains(hidden, StringComparison.Ordinal)))
                throw new InvalidOperationException("Full-chapter token filter missed an unsampled clue.");
            StoryChapterIndexDocument? withFilter = JsonSerializer.Deserialize<StoryChapterIndexDocument>(
                await File.ReadAllTextAsync(indexPath));
            StoryChapterIndexEntry? midEntry = withFilter?.Chapters.SingleOrDefault(x => x.Number == 60);
            if (midEntry is null || midEntry.Terms.Contains(hidden.ToLowerInvariant()) ||
                StoryChapterTokenFilter.TryRead(midEntry.TermSignature)?.MightContain(hidden.ToLowerInvariant()) != true)
                throw new InvalidOperationException("Full-chapter signature did not cover an unsampled clue.");

            // Continue Story with no typed prompt must still retrieve recent
            // committed chapters when no lexical query terms are available.
            var autoPlan = new StoryRetrievalPlan { PreferRecentChapters = true };
            IReadOnlyList<StoryChapterSearchHit> autoHits = await memories.SearchChapterTextAsync(
                project.Id, autoPlan);
            if (!autoHits.Any(x => x.Number == 60))
                throw new InvalidOperationException("Empty-prompt Continue Story lost its recent-chapter fallback.");

            // A saturated approximate filter can report a term as present
            // despite no underlying text. That alone must never become evidence.
            StoryChapterTokenFilter? saturated = StoryChapterTokenFilter.TryRead(
                Convert.ToBase64String(Enumerable.Repeat((byte)255, 4096).ToArray()));
            if (saturated?.MightContain("ghostcluewithnoevidence") != true ||
                StoryMemoryStore.AcceptVerifiedEvidence(0, 0, false) ||
                !StoryMemoryStore.AcceptVerifiedEvidence(0, 1, false) ||
                !StoryMemoryStore.AcceptVerifiedEvidence(0, 0, true))
                throw new InvalidOperationException("Approximate filter or evidence verification policy regressed.");

            // Local paraphrase candidates should work without an API call,
            // but never accept a Bloom false positive or substring match as proof.
            if (StoryLocalSemanticBridge.Expand("vanished").All(x => x != "disappeared") ||
                StoryLocalSemanticBridge.Expand("ordinary unrelated question").Count != 0 ||
                StoryLocalSemanticBridge.ContainsWholePhrase("The sentry disappearedly", "disappeared"))
                throw new InvalidOperationException("Local paraphrase matching has unsafe boundaries.");
            string decoyPath = Path.Combine(memories.GetChaptersDirectory(project.Id), "0061-decoy.md");
            await File.WriteAllTextAsync(decoyPath, "# Decoy\n\nThe sentry disappearedly, a made-up term.");
            await RegisterFixtureChapterAsync(61, decoyPath);
            StoryMemoryStore.InvalidateChapterCache(project.Id);
            if ((await memories.SearchChapterTextAsync(project.Id, "vanished")).Count != 0)
                throw new InvalidOperationException("Paraphrase search returned a substring-only false match.");
            if ((await memories.SearchChapterTextAsync(project.Id, "disappeared")).Count != 0)
                throw new InvalidOperationException("A substring in another word was treated as direct evidence.");
            string paraphrasePath = Path.Combine(memories.GetChaptersDirectory(project.Id), "0062-paraphrase.md");
            await File.WriteAllTextAsync(paraphrasePath, "# Bridge chapter\n\nThe sentry disappeared beneath the stone arch.");
            await RegisterFixtureChapterAsync(62, paraphrasePath);
            StoryMemoryStore.InvalidateChapterCache(project.Id);
            IReadOnlyList<StoryChapterSearchHit> paraphraseHits = await memories.SearchChapterTextAsync(project.Id, "vanished");
            if (paraphraseHits.Count != 1 || paraphraseHits[0].Number != 62 ||
                !paraphraseHits[0].Excerpt.Contains("sentry disappeared", StringComparison.OrdinalIgnoreCase) ||
                !paraphraseHits[0].Excerpt.Contains("paraphrase candidate", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Local paraphrase search did not verify original chapter prose.");
            // Exact lexical hits win. The weaker paraphrase fallback must not
            // replace a direct result even when a newer semantic candidate exists.
            string directPath = Path.Combine(memories.GetChaptersDirectory(project.Id), "0063-direct.md");
            await File.WriteAllTextAsync(directPath, "# Direct evidence\n\nThe sentry vanished in the snow.");
            await RegisterFixtureChapterAsync(63, directPath);
            StoryMemoryStore.InvalidateChapterCache(project.Id);
            IReadOnlyList<StoryChapterSearchHit> directHits = await memories.SearchChapterTextAsync(project.Id, "vanished");
            if (!directHits.Any(h => h.Number == 63) || directHits.Any(h => h.Number == 62))
                throw new InvalidOperationException("Weak paraphrase hit displaced direct story evidence.");
            File.Delete(directPath);
            File.Delete(paraphrasePath);
            StoryMemoryStore.InvalidateChapterCache(project.Id);
            if ((await memories.SearchChapterTextAsync(project.Id, "vanished")).Count != 0)
                throw new InvalidOperationException("Alternate chapter leaked through semantic retrieval cache.");

            // Accent-free Vietnamese phrasing can discover a real phrase in
            // chapter prose, without requiring translation or an embedding API.
            string vietPath = Path.Combine(memories.GetChaptersDirectory(project.Id), "0064-viet.md");
            await File.WriteAllTextAsync(vietPath, "# Người mất tích\n\nNgười gác đền biến mất dưới cơn mưa.");
            await RegisterFixtureChapterAsync(64, vietPath);
            StoryMemoryStore.InvalidateChapterCache(project.Id);
            IReadOnlyList<StoryChapterSearchHit> vietnamese = await memories.SearchChapterTextAsync(project.Id, "mat tich");
            if (!vietnamese.Any(x => x.Number == 64 && x.Excerpt.Contains("biến mất", StringComparison.Ordinal)))
                throw new InvalidOperationException("Vietnamese paraphrase without accents was not retrieved.");
            // An identified character must not inherit someone else's
            // disappearance just because a paraphrase is found in prose.
            var identifiedMemory = new StoryMemoryDocument();
            identifiedMemory.Characters.Add(new StoryCharacterMemory { Name = "Magica" });
            StoryRetrievalPlan identified = StoryRetrievalPlanner.Build(identifiedMemory, "Magica vanished");
            if ((await memories.SearchChapterTextAsync(project.Id, identified)).Any(x => x.Number == 64))
                throw new InvalidOperationException("Paraphrase retrieval transferred an event to the wrong character.");
            File.Delete(vietPath);
            StoryMemoryStore.InvalidateChapterCache(project.Id);

            // A plausible chapter file without a committed ledger entry must
            // never contaminate the writer's retrieval context.
            string orphanPath = Path.Combine(memories.GetChaptersDirectory(project.Id), "0065-uncommitted.md");
            await File.WriteAllTextAsync(orphanPath, "# Uncommitted\n\nUncommittedNightglassBeacon");
            StoryMemoryStore.InvalidateChapterCache(project.Id);
            try
            {
                IReadOnlyList<StoryChapterSearchHit> orphanHits = await memories.SearchChapterTextAsync(
                    project.Id, "UncommittedNightglassBeacon");
                if (orphanHits.Any(hit => hit.Number == 65 ||
                        hit.Excerpt.Contains("UncommittedNightglassBeacon", StringComparison.OrdinalIgnoreCase)))
                    throw new InvalidOperationException("Uncommitted orphan chapter entered indexed retrieval.");

                // Force the LEGACY FALLBACK directly. Indexed retrieval being
                // clean is not enough: a corrupt cache/IO error must not allow
                // a previously abandoned scene to become prompt evidence.
                string directory = memories.GetChaptersDirectory(project.Id);
                var orphanPlan = new StoryRetrievalPlan
                {
                    Terms = new[] { "UncommittedNightglassBeacon" },
                    DirectTerms = new[] { "UncommittedNightglassBeacon" }
                };
                HashSet<string> allowed = StoryChapterIndexStore.CommittedChapterFileNames(memory);
                IReadOnlyList<StoryChapterSearchHit> fallbackOrphan =
                    await StoryMemoryStore.SearchChapterTextLegacyAsync(directory, orphanPlan, 3, allowed);
                if (fallbackOrphan.Count != 0)
                    throw new InvalidOperationException("Uncommitted draft leaked through fallback chapter scan.");
                IReadOnlyList<StoryChapterSearchHit> fallbackEmpty =
                    await StoryMemoryStore.SearchChapterTextLegacyAsync(directory, orphanPlan, 3,
                        new HashSet<string>(StringComparer.OrdinalIgnoreCase));
                if (fallbackEmpty.Count != 0)
                    throw new InvalidOperationException("Fallback without committed ledger returned chapter text.");
                var committedPlan = new StoryRetrievalPlan
                {
                    Terms = new[] { "AlternateBranchBeacon" },
                    DirectTerms = new[] { "AlternateBranchBeacon" }
                };
                IReadOnlyList<StoryChapterSearchHit> fallbackCommitted =
                    await StoryMemoryStore.SearchChapterTextLegacyAsync(directory, committedPlan, 3, allowed);
                if (!fallbackCommitted.Any(hit => hit.Number == chapter.Number &&
                        hit.Excerpt.Contains("AlternateBranchBeacon", StringComparison.Ordinal)))
                    throw new InvalidOperationException("Ledger-scoped fallback lost committed chapter evidence.");
            }
            finally
            {
                File.Delete(orphanPath);
                StoryMemoryStore.InvalidateChapterCache(project.Id);
            }

            // Historical prose must be labeled as provenance, while recent
            // confirmed revisions precede unrelated chapter excerpts.
            var canonMemory = new StoryMemoryDocument();
            canonMemory.Canon.Add(new StoryCanonFact
            {
                Fact = "The mirror is destroyed", Status = "Retconned", UpdatedUtc = DateTimeOffset.UtcNow
            });
            canonMemory.Canon.Add(new StoryCanonFact
            {
                Fact = "The mirror is intact", Status = "Confirmed", UpdatedUtc = DateTimeOffset.UtcNow
            });
            string canonContext = StoryMemoryRetriever.BuildContext(canonMemory, "mirror",
                new[] { new StoryChapterSearchHit { Number = 1, Title = "Old mirror", Excerpt = "The mirror is destroyed" } });
            if (!canonContext.Contains("The mirror is intact", StringComparison.Ordinal) ||
                canonContext.IndexOf("The mirror is intact", StringComparison.Ordinal) >
                canonContext.IndexOf("RETRIEVED HISTORICAL", StringComparison.Ordinal) ||
                !canonContext.Contains("Current Confirmed Canon", StringComparison.Ordinal))
                throw new InvalidOperationException("Historical chapter evidence outranked active canon.");

            // Deterministic event retrieval requires matching actor + action +
            // specific clue, with confirmed per-chapter provenance.
            var eventMemory = new StoryMemoryDocument();
            // Event provenance must resolve to a unique committed chapter.
            foreach (int committedNumber in new[] { 3, 4, 5, 6 })
                eventMemory.Chapters.Add(new StoryChapterMemory { Number = committedNumber,
                    Title = "Event chapter " + committedNumber });
            eventMemory.Characters.Add(new StoryCharacterMemory
            {
                Name = "Magica", Aliases = new List<string> { "Silver Crow", "The Watcher" }
            });
            eventMemory.Characters.Add(new StoryCharacterMemory
            {
                Name = "Luna", Aliases = new List<string> { "The Watcher" }
            });
            eventMemory.Events.Add(new StorySignificantEvent
            {
                Id = "magica-lost-blade", Type = "ItemLost", Status = "Confirmed",
                Subject = "Magica", Target = "SilverBlade", Summary = "Magica lost SilverBlade",
                ChapterNumber = 3, When = "First winter"
            });
            eventMemory.Events.Add(new StorySignificantEvent
            {
                Id = "luna-lost-blade", Type = "ItemLost", Status = "Confirmed",
                Subject = "Luna", Target = "SilverBlade", Summary = "Luna lost SilverBlade",
                ChapterNumber = 4
            });
            eventMemory.Events.Add(new StorySignificantEvent
            {
                Id = "proposed-loss", Type = "ItemLost", Status = "Proposed",
                Subject = "Magica", Target = "SilverBlade", ChapterNumber = 5
            });
            eventMemory.Events.Add(new StorySignificantEvent
            {
                Id = "magica-recovered-blade", Type = "ItemAcquired", Status = "Confirmed",
                Subject = "Magica", Target = "SilverBlade", ChapterNumber = 6
            });
            IReadOnlyList<StoryEventContextMatcher.Match> matched = StoryEventContextMatcher.Find(
                eventMemory, "Silver Crow lost SilverBlade in which chapter?", new[] { "Magica" });
            if (matched.Count != 1 || matched[0].Event.ChapterNumber != 3)
                throw new InvalidOperationException("Event context matched the wrong character or action.");
            if (StoryEventContextMatcher.Find(eventMemory, "Magica lost OtherRelic", new[] { "Magica" }).Count != 0 ||
                StoryEventContextMatcher.Find(eventMemory, "The Watcher lost SilverBlade", Array.Empty<string>()).Count != 0 ||
                StoryEventContextMatcher.Find(eventMemory, "Magica lost SilverBlade chapter 4", new[] { "Magica" }).Count != 0 ||
                StoryEventContextMatcher.HasActionIntent("Magica mất tích trong rừng"))
                throw new InvalidOperationException("Event context ignored a constraint or misread disappearance.");
            StoryRetrievalPlan exactEventPlan = StoryRetrievalPlanner.Build(eventMemory, "Magica lost SilverBlade");
            if (!exactEventPlan.ChapterBoosts.ContainsKey(3) ||
                !exactEventPlan.FocusReasons.Any(x => x.Contains("Confirmed event provenance", StringComparison.Ordinal)))
                throw new InvalidOperationException("Confirmed event chapter provenance did not reach retrieval.");

            string eventContext = StoryMemoryRetriever.BuildContext(eventMemory,
                exactEventPlan.ExpandedQuery, maxCharacters: 12000, retrievalPlan: exactEventPlan);
            if (!eventContext.Contains("CONFIRMED EVENT PROVENANCE", StringComparison.Ordinal) ||
                !eventContext.Contains("magica-lost-blade", StringComparison.OrdinalIgnoreCase) &&
                !eventContext.Contains("Magica lost SilverBlade", StringComparison.Ordinal) ||
                eventContext.Contains("Luna lost SilverBlade", StringComparison.Ordinal) ||
                eventContext.Contains("proposed-loss", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Writer context included unrelated or proposed events.");

            // Bookkeeping must not mutate an arbitrary owner of a shared alias.
            var aliasState = new StoryMemoryDocument();
            aliasState.Characters.Add(new StoryCharacterMemory
            {
                Name = "Magica", Aliases = new List<string> { "The Watcher" }
            });
            aliasState.Characters.Add(new StoryCharacterMemory
            {
                Name = "Luna", Aliases = new List<string> { "The Watcher" }
            });
            StoryEventLedger.Merge(aliasState, new[]
            {
                new StoryEventPatch
                {
                    Type = "KnowledgeGained", Subject = "The Watcher",
                    Target = "Hidden bridge code", Summary = "The Watcher learned a bridge code"
                }
            }, "SelfTest");
            if (aliasState.Characters.Count != 2 ||
                aliasState.Characters.Any(c => c.Knowledge.Contains("Hidden bridge code")))
                throw new InvalidOperationException("Ambiguous event alias mutated a character's knowledge.");
            StoryEventLedger.Merge(aliasState, new[]
            {
                new StoryEventPatch
                {
                    Type = "KnowledgeGained", Subject = "Magica",
                    Target = "Copper gate code", Summary = "Magica learned a gate code"
                }
            }, "SelfTest");
            if (!aliasState.Characters[0].Knowledge.Contains("Copper gate code") ||
                aliasState.Characters[1].Knowledge.Count != 0)
                throw new InvalidOperationException("Canonical event subject did not update only its owner.");

            // All character bookkeeping (not only Event Ledger) must respect
            // unique identity. A shared alias must never mutate the first owner.
            var ambiguousCharacterUpdate = new StoryMemoryUpdate
            {
                Characters = new List<StoryCharacterPatch>
                {
                    new()
                    {
                        Name = "The Watcher",
                        Knowledge = new List<string> { "Bridge code" },
                        Inventory = new List<string> { "Golden compass" },
                        Abilities = new List<string> { "Shadow Step" }
                    }
                }
            };
            StoryDeterministicReport ambiguousReport = StoryDeterministicContinuity.ValidateUpdate(
                aliasState, ambiguousCharacterUpdate);
            if (!ambiguousReport.Issues.Any(i => i.Code == "ambiguous-character-update" &&
                                                  i.Severity == StoryDeterministicSeverity.Risk))
                throw new InvalidOperationException("Ambiguous character bookkeeping was not flagged for review.");
            StoryMemoryMerger.Apply(aliasState, ambiguousCharacterUpdate, "SelfTest");
            if (aliasState.Characters.Count != 2 || aliasState.Characters.Any(c =>
                    c.Knowledge.Contains("Bridge code") ||
                    c.Inventory.Contains("Golden compass") ||
                    c.Abilities.Contains("Shadow Step")))
                throw new InvalidOperationException("Character patch changed the wrong owner of a shared alias.");

            StoryMemoryMerger.Apply(aliasState, new StoryMemoryUpdate
            {
                Characters = new List<StoryCharacterPatch>
                {
                    new() { Name = "Magica", Inventory = new List<string> { "Crystal map" } },
                    new() { Name = "Newcomer", Knowledge = new List<string> { "Town bell" } }
                }
            }, "SelfTest");
            if (!aliasState.Characters[0].Inventory.Contains("Crystal map") ||
                aliasState.Characters[1].Inventory.Contains("Crystal map") ||
                aliasState.Characters.Count != 3 ||
                !aliasState.Characters[2].Knowledge.Contains("Town bell"))
                throw new InvalidOperationException("Canonical updates or genuinely new characters stopped working.");

            // An author-private secret entering another character's Knowledge
            // without a verified learning event is a RISK, never a hard conflict.
            aliasState.Characters[0].Secrets.Add("The silver bridge password");
            var suspiciousKnowledge = new StoryMemoryUpdate
            {
                Characters = new List<StoryCharacterPatch>
                {
                    new() { Name = "Luna", Knowledge = new List<string> { "The silver bridge password" } }
                }
            };
            StoryDeterministicReport secretRisk = StoryDeterministicContinuity.ValidateUpdate(
                aliasState, suspiciousKnowledge);
            if (secretRisk.HasConflict ||
                !secretRisk.Issues.Any(i => i.Code == "unverified-secret-transfer" &&
                                            i.Severity == StoryDeterministicSeverity.Risk))
                throw new InvalidOperationException("Possible secret knowledge leak was not flagged conservatively.");
            suspiciousKnowledge.Events.Add(new StoryEventPatch
            {
                Type = "KnowledgeGained", Status = "Proposed", Subject = "Luna",
                Target = "The silver bridge password", Summary = "Unconfirmed disclosure"
            });
            if (!StoryDeterministicContinuity.ValidateUpdate(aliasState, suspiciousKnowledge).Issues
                    .Any(i => i.Code == "unverified-secret-transfer"))
                throw new InvalidOperationException("Proposed learning improperly authorized secret knowledge.");
            suspiciousKnowledge.Events[0].Status = "Confirmed";
            if (StoryDeterministicContinuity.ValidateUpdate(aliasState, suspiciousKnowledge).Issues
                    .Any(i => i.Code == "unverified-secret-transfer"))
                throw new InvalidOperationException("Confirmed learning still triggered an unsupported leak warning.");

            // Character POV is an epistemic boundary, not a view into author-only canon.
            var epistemic = new StoryMemoryDocument();
            epistemic.Canon.Add(new StoryCanonFact
            {
                Fact = "The Obsidian Regent secretly controls the gate", Status = "Confirmed"
            });
            epistemic.Characters.Add(new StoryCharacterMemory
            {
                Name = "Magica", Aliases = new List<string> { "Silver Crow", "Watcher" },
                Knowledge = new List<string> { "The gate requires a copper key" },
                Secrets = new List<string> { "The Regent controls the gate" }
            });
            epistemic.Characters.Add(new StoryCharacterMemory
            {
                Name = "Luna", Aliases = new List<string> { "Watcher" }
            });
            epistemic.Chapters.Add(new StoryChapterMemory { Number = 12, Title = "The Locked Gate" });
            epistemic.SceneStates.Add(new StorySceneStateMemory
            {
                ChapterNumber = 12, PovCharacter = "Magica", PresentCharacters = new List<string> { "Magica", "Luna" }
            });
            string continuing = StoryKnowledgeBoundary.Build(epistemic, "tiếp tục", autoContinue: true);
            if (!continuing.Contains("Last committed scene POV: Magica", StringComparison.Ordinal) ||
                !continuing.Contains("gate requires a copper key", StringComparison.Ordinal) ||
                continuing.Contains("recorded beliefs/knowledge: The Regent controls", StringComparison.Ordinal))
                throw new InvalidOperationException("POV knowledge was missing or a private secret was elevated to knowledge.");
            string alternatePov = StoryKnowledgeBoundary.Build(epistemic, "POV: Luna", autoContinue: true);
            if (!alternatePov.Contains("Creator-requested POV: Luna", StringComparison.Ordinal) ||
                alternatePov.Contains("Last committed scene POV: Magica", StringComparison.Ordinal) ||
                !alternatePov.Contains("no current knowledge entries recorded", StringComparison.Ordinal))
                throw new InvalidOperationException("Creator POV did not override the last committed scene safely.");
            string ambiguousPov = StoryKnowledgeBoundary.Build(epistemic, "POV: Watcher", autoContinue: true);
            if (!ambiguousPov.Contains("identity could not be uniquely resolved", StringComparison.Ordinal) ||
                ambiguousPov.Contains("Last committed scene POV: Magica", StringComparison.Ordinal))
                throw new InvalidOperationException("Shared nickname improperly resolved to a POV.");
            // Provenance is allowed only for CURRENT knowledge backed by a
            // Confirmed KnowledgeGained event in a committed chapter.
            epistemic.Events.Add(new StorySignificantEvent
            {
                Type = "KnowledgeGained", Status = "Confirmed", Subject = "Magica",
                Target = "The gate requires a copper key", ChapterNumber = 12
            });
            epistemic.Events.Add(new StorySignificantEvent
            {
                Type = "KnowledgeGained", Status = "Proposed", Subject = "Magica",
                Target = "The Regent controls the gate", ChapterNumber = 12
            });
            epistemic.Events.Add(new StorySignificantEvent
            {
                Type = "KnowledgeGained", Status = "Confirmed", Subject = "Watcher",
                Target = "The gate requires a copper key", ChapterNumber = 12
            });
            epistemic.Events.Add(new StorySignificantEvent
            {
                Type = "KnowledgeGained", Status = "Confirmed", Subject = "Luna",
                Target = "The Regent controls the gate", ChapterNumber = 12
            });
            epistemic.Events.Add(new StorySignificantEvent
            {
                Type = "KnowledgeGained", Status = "Confirmed", Subject = "Magica",
                Target = "The gate requires a copper key", ChapterNumber = 99
            });
            StoryCharacterMemory magica = epistemic.Characters[0];
            IReadOnlyList<string> knowledgeEvidence = StoryKnowledgeProvenance.Find(epistemic, magica);
            if (knowledgeEvidence.Count != 1 ||
                !knowledgeEvidence[0].Contains("learned Ch.12", StringComparison.Ordinal) ||
                !StoryKnowledgeBoundary.Build(epistemic, "POV: Magica", autoContinue: true)
                    .Contains("evidence: The gate requires a copper key", StringComparison.Ordinal))
                throw new InvalidOperationException("Confirmed character knowledge provenance was lost or incorrectly attributed.");
            magica.Knowledge.Clear();
            if (StoryKnowledgeProvenance.Find(epistemic, magica).Count != 0)
                throw new InvalidOperationException("Historical learning resurrected forgotten knowledge.");
            magica.Knowledge.Add("The gate requires a copper key");
            epistemic.Chapters.Clear();
            if (StoryKnowledgeProvenance.Find(epistemic, magica).Count != 0)
                throw new InvalidOperationException("Orphan chapter event was used as character knowledge provenance.");
            epistemic.Chapters.Add(new StoryChapterMemory { Number = 12, Title = "The Locked Gate" });
            string chatOnly = StoryKnowledgeBoundary.Build(epistemic, "What happened at the gate?", autoContinue: false);
            if (chatOnly.Contains("Last committed scene POV:", StringComparison.Ordinal))
                throw new InvalidOperationException("Chat query was assigned an unrequested narrative POV.");
            epistemic.SceneStates[0].ChapterNumber = 11;
            if (StoryKnowledgeBoundary.Build(epistemic, "tiếp tục", autoContinue: true)
                .Contains("Last committed scene POV:", StringComparison.Ordinal))
                throw new InvalidOperationException("Old scene POV was mistaken for the current chapter.");
            string guardedContext = StoryMemoryRetriever.BuildContext(epistemic, "POV: Luna", maxCharacters: 4000);
            if (!guardedContext.Contains("CHARACTER KNOWLEDGE / POV BOUNDARY", StringComparison.Ordinal) ||
                !guardedContext.Contains("The Obsidian Regent secretly controls the gate", StringComparison.Ordinal))
                throw new InvalidOperationException("The writer lost author-level canon or the knowledge boundary.");

            StoryKnowledgeTimelineSelfTest.Verify();
            StoryCharacterDevelopmentSelfTest.Verify();
            StoryCharacterDecisionSelfTest.Verify();
            StoryCharacterMotivationSelfTest.Verify();
            StoryCharacterPrioritySelfTest.Verify();
            StoryNarrativeCraftSelfTest.Verify();
            StoryNarrativeQualitySelfTest.Verify();

            string context = StoryContextBudget.Compose(
                new string('M', 24000), "CONFLICT safety check", "NARRATIVE focus",
                "PROVENANCE chapter 1", "GRAPH " + new string('G', 9000));
            if (context.Length > StoryContextBudget.MaxTotalCharacters ||
                !context.Contains("CONFLICT safety check", StringComparison.Ordinal) ||
                !context.Contains("NARRATIVE focus", StringComparison.Ordinal) ||
                !context.Contains("PROVENANCE chapter 1", StringComparison.Ordinal) ||
                !context.Contains("GRAPH ", StringComparison.Ordinal))
                throw new InvalidOperationException("Long-story context exceeded budget or omitted priority sections.");
        }
        finally
        {
            StoryMemoryStore.InvalidateChapterCache(project.Id);
            string directory = projects.GetProjectDirectory(project.Id);
            if (Directory.Exists(directory)) Directory.Delete(directory, true);
        }
    }
}
