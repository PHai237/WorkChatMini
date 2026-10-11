namespace WorkChatMini;

// Piggybacks on the existing foundation test: no external AI, disposable fixture only.
internal static class StoryCoCreationSelfTest
{
    internal static async Task VerifyAsync(StoryProjectStore store, string projectId)
    {
        StoryLongSessionSelfTest.Verify();
        // Even a long brainstorm about chapters/scenes must not run the writing pipeline.
        if (StoryActions.UsesWritingPipeline(StoryActionType.Chat) ||
            StoryActions.IsNarrativeAction(StoryActionType.Chat) ||
            StoryActions.MayAutoBookkeep(StoryActionType.Chat))
            throw new InvalidOperationException("Chat brainstorming was classified as a narrative action.");
        IReadOnlyList<StoryMessage> previous = await store.LoadMessagesAsync(projectId);
        try
        {
            var confirmed = new StoryMessage
            {
                Role = "user", Action = StoryActionType.Chat.ToString(),
                Content = "Chốt: nhân vật chính dè dặt với người lạ, không phải vô cảm."
            };
            StoryCoCreation.CaptureCreatorConfirmation(confirmed);
            var tentative = new StoryMessage
            {
                Role = "user", Action = StoryActionType.Chat.ToString(),
                Content = "Hay thử để nhân vật phản bội?"
            };
            StoryCoCreation.CaptureCreatorConfirmation(tentative);
            var assistant = new StoryMessage
            {
                Role = "assistant", Action = StoryActionType.Chat.ToString(),
                Content = "Maybe the moon collapses?"
            };
            StoryCoCreation.CaptureCreatorConfirmation(assistant);
            var discarded = new StoryMessage
            {
                Role = "assistant", Action = StoryActionType.ContinueStory.ToString(),
                Content = "Alternative B forbidden information",
                CreativeDirection = "Alternative B must not leak",
                IsNarrativeDraft = true, ManuscriptCommitted = false
            };
            var archived = new StoryMessage
            {
                Role = "user", Action = StoryActionType.Chat.ToString(),
                Content = "Chốt: discard later",
                CreativeDirection = "old direction", CreativeDirectionArchived = true
            };
            await store.SaveMessagesAsync(projectId, new[] { confirmed, tentative, assistant, discarded, archived });
            IReadOnlyList<StoryMessage> restored = await store.LoadMessagesAsync(projectId);
            string context = StoryCoCreation.BuildContext(restored);
            if (restored.Count != 5 || restored[0].CreativeDirection.Length == 0 ||
                !context.Contains("dè dặt", StringComparison.Ordinal) ||
                context.Contains("Alternative B", StringComparison.Ordinal) ||
                context.Contains("old direction", StringComparison.Ordinal) ||
                context.Contains("moon collapses", StringComparison.Ordinal) ||
                context.Contains("phản bội", StringComparison.Ordinal))
                throw new InvalidOperationException("Co-creation confirmation or persistence leaked brainstorming/alternate drafts.");

            restored[0].CreativeDirectionArchived = true;
            if (StoryCoCreation.BuildContext(restored).Length != 0)
                throw new InvalidOperationException("Archived co-creation direction still entered context.");
            restored[0].CreativeDirectionArchived = false;
            string largeMemory = new('x', 32000);
            string bounded = StoryContextBudget.Compose(largeMemory, "WARN", "STORY", "", "",
                StoryCoCreation.BuildContext(restored));
            if (bounded.Length > StoryContextBudget.MaxTotalCharacters ||
                !bounded.Contains("dè dặt", StringComparison.Ordinal) ||
                !bounded.Contains("WARN", StringComparison.Ordinal))
                throw new InvalidOperationException("Co-creation context violated the global budget or displaced continuity.");

            var fantasy = StoryGenreChatIntent.FindDeclaredGenres("Thể loại: fantasy, MMORPG, romcom");
            if (!fantasy.Contains("Medieval Fantasy") || !fantasy.Contains("MMORPG / Game System") ||
                !fantasy.Contains("Romcom") || StoryGenreChatIntent.FindDeclaredGenres("Fantasy sounds interesting").Count != 0)
                throw new InvalidOperationException("Explicit genre chat parsing changed genres without consent.");

            // Model suggestions can appear as a list. They are never automatically
            // accepted, nor should quoted text, links, or code become suggestions.
            string assistantOptions = "Gemini proposes:\n" +
                "1. Magica distrusts strangers but protects children.\n" +
                "2. The ruins contain a quiet clue about the vanished archivist.\n" +
                "2. The ruins contain a quiet clue about the vanished archivist.\n" +
                "> - Quoted instructions must never be accepted as ideas.\n" +
                "```\n- Code blocks are not creative suggestions either.\n```";
            IReadOnlyList<string> suggestions = StoryCreativeSuggestionExtractor.Extract(assistantOptions);
            if (suggestions.Count != 2 || !suggestions[0].Contains("Magica", StringComparison.Ordinal) ||
                !suggestions[1].Contains("archivist", StringComparison.Ordinal) ||
                StoryCoCreation.BuildContext(new[] { new StoryMessage
                {
                    Role = "assistant", Action = StoryActionType.Chat.ToString(), Content = assistantOptions
                } }).Length != 0)
                throw new InvalidOperationException("Unapproved assistant suggestions entered creative memory.");

            var suggestedByGemini = new StoryMessage
            {
                Role = "assistant", Action = StoryActionType.Chat.ToString(), Content = assistantOptions
            };
            var choseSecond = new StoryMessage
            {
                Role = "user", Action = StoryActionType.Chat.ToString(), Content = "Chốt: phương án 2"
            };
            StoryCoCreation.CaptureCreatorConfirmation(choseSecond, new[] { suggestedByGemini });
            if (choseSecond.CreativeDirection != suggestions[1] ||
                !StoryCoCreation.BuildContext(new[] { choseSecond }).Contains("archivist", StringComparison.Ordinal))
                throw new InvalidOperationException("Creator's numbered selection did not resolve to the approved option.");
            var invalidChoice = new StoryMessage
            {
                Role = "user", Action = StoryActionType.Chat.ToString(), Content = "Chốt: phương án 4"
            };
            StoryCoCreation.CaptureCreatorConfirmation(invalidChoice, new[] { suggestedByGemini });
            if (invalidChoice.CreativeDirection.Length != 0)
                throw new InvalidOperationException("An unresolved numbered choice was stored as a direction.");
            var unresolvedWithEdit = new StoryMessage
            {
                Role = "user", Action = StoryActionType.Chat.ToString(),
                Content = "Chốt: phương án 2 nhưng thay đổi phần kết"
            };
            StoryCoCreation.CaptureCreatorConfirmation(unresolvedWithEdit, new[] { suggestedByGemini });
            if (unresolvedWithEdit.CreativeDirection.Length != 0)
                throw new InvalidOperationException("An unresolved modified option pointer was stored as a creative direction.");
            var staleChoice = new StoryMessage
            {
                Role = "user", Action = StoryActionType.Chat.ToString(), Content = "Chốt: phương án 2"
            };
            StoryCoCreation.CaptureCreatorConfirmation(staleChoice, new[]
            {
                suggestedByGemini,
                new StoryMessage { Role = "assistant", Action = StoryActionType.ContinueStory.ToString(), Content = "New chapter" }
            });
            if (staleChoice.CreativeDirection.Length != 0)
                throw new InvalidOperationException("A numbered choice incorrectly captured an old unrelated proposal.");

            // Filtered numbered options MUST retain Gemini's original labels.
            // Option 1 is too short, so blindly indexing the filtered list
            // would wrongly commit option 2 as the author's choice of option 1.
            string filteredOptions = "1. Not now\n" +
                "2. The librarian keeps a secret from her oldest friend.\n" +
                "3. The river ruins reveal a forgotten family promise.";
            var filteredProposal = new StoryMessage
            {
                Role = "assistant", Action = StoryActionType.Chat.ToString(), Content = filteredOptions
            };
            var firstFiltered = new StoryMessage
            {
                Role = "user", Action = StoryActionType.Chat.ToString(), Content = "Chốt: phương án 1"
            };
            StoryCoCreation.CaptureCreatorConfirmation(firstFiltered, new[] { filteredProposal });
            if (firstFiltered.CreativeDirection.Length != 0)
                throw new InvalidOperationException("Filtered Gemini option 1 incorrectly selected another option.");
            var thirdFiltered = new StoryMessage
            {
                Role = "user", Action = StoryActionType.Chat.ToString(), Content = "Chọn: ý 3"
            };
            StoryCoCreation.CaptureCreatorConfirmation(thirdFiltered, new[] { filteredProposal });
            if (!thirdFiltered.CreativeDirection.Contains("family promise", StringComparison.Ordinal))
                throw new InvalidOperationException("Original Gemini option 3 was not preserved.");

            var conflictingProposal = new StoryMessage
            {
                Role = "assistant", Action = StoryActionType.Chat.ToString(),
                Content = "2. The first option suggests a journey to the old valley.\n" +
                          "2. The second option suggests staying in the walled city."
            };
            var ambiguous = new StoryMessage
            {
                Role = "user", Action = StoryActionType.Chat.ToString(), Content = "Chốt: phương án 2"
            };
            StoryCoCreation.CaptureCreatorConfirmation(ambiguous, new[] { conflictingProposal });
            if (ambiguous.CreativeDirection.Length != 0)
                throw new InvalidOperationException("Conflicting duplicate option labels were accepted.");

            var bulletProposal = new StoryMessage
            {
                Role = "assistant", Action = StoryActionType.Chat.ToString(),
                Content = "- The village elder conceals a letter about the lost heir.\n" +
                          "- The apprentice finds the truth inside an old diary."
            };
            var bulletChoice = new StoryMessage
            {
                Role = "user", Action = StoryActionType.Chat.ToString(), Content = "Chốt: phương án 2"
            };
            StoryCoCreation.CaptureCreatorConfirmation(bulletChoice, new[] { bulletProposal });
            if (!bulletChoice.CreativeDirection.Contains("old diary", StringComparison.Ordinal))
                throw new InvalidOperationException("Ordered bullet-choice fallback stopped working.");

            if (StoryCreativeSuggestionExtractor.Extract("~~~\n1. A forbidden fake suggestion in a code fence.\n~~~\n" +
                    "1. A genuine clue appears on the village noticeboard.").Count != 1)
                throw new InvalidOperationException("Tilde code fences leaked into assistant suggestions.");

            // Co-Creation 3.0: declining is explicit, reversible, and never canon.
            // Skipping/keeping one option must not mark other ideas as rejected.
            if (!StoryCreativeDecision.SetDeclined(suggestedByGemini, suggestions[0], true) ||
                StoryCreativeDecision.ActiveDeclines(new[] { suggestedByGemini }).Count != 1 ||
                StoryCreativeDecision.SetDeclined(suggestedByGemini, "No such proposal", true) ||
                StoryCreativeDecision.SetDeclined(discarded, suggestions[0], true))
                throw new InvalidOperationException("Explicit proposal rejection accepted untrusted or unrelated content.");
            string declinedContext = StoryCoCreation.BuildContext(new[] { suggestedByGemini, choseSecond });
            if (!declinedContext.Contains("CREATOR-DECLINED", StringComparison.Ordinal) ||
                !declinedContext.Contains("CREATOR-APPROVED", StringComparison.Ordinal) ||
                declinedContext.Length > StoryCoCreation.MaxContextLength ||
                !declinedContext.Contains("Magica", StringComparison.Ordinal) ||
                StoryCreativeDecision.ActiveDeclines(new[] { suggestedByGemini, choseSecond }).Count != 1)
                throw new InvalidOperationException("Approved and declined intent leaked together or exceeded context budget.");
            // Later creator approval of the exact idea suppresses a stale rejection.
            var changedMind = new StoryMessage
            {
                Role = "user", Action = StoryActionType.Chat.ToString(),
                CreativeDirection = suggestions[0]
            };
            if (StoryCreativeDecision.ActiveDeclines(new[] { suggestedByGemini, changedMind }).Count != 0 ||
                StoryCoCreation.BuildContext(new[] { suggestedByGemini, changedMind })
                    .Contains("CREATOR-DECLINED", StringComparison.Ordinal))
                throw new InvalidOperationException("An explicitly approved direction remained declined.");
            if (!StoryCreativeDecision.SetDeclined(suggestedByGemini, suggestions[0], false) ||
                StoryCreativeDecision.ActiveDeclines(new[] { suggestedByGemini }).Count != 0)
                throw new InvalidOperationException("Undo Pass did not remove the rejected idea.");

            var priorDirection = new StoryMessage
            {
                Role = "user", Action = StoryActionType.Chat.ToString(),
                CreativeDirection = "Let their romance develop rapidly."
            };
            var newerDirection = new StoryMessage
            {
                Role = "assistant", Action = StoryActionType.Chat.ToString(), Content = assistantOptions
            };
            if (!StoryCreativeDecision.Replace(newerDirection, priorDirection,
                    "Let their romance develop slowly.", DateTimeOffset.UtcNow) ||
                !priorDirection.CreativeDirectionArchived ||
                StoryCoCreation.BuildContext(new[] { priorDirection, newerDirection })
                    .Contains("romance develop rapidly", StringComparison.Ordinal) ||
                !StoryCoCreation.BuildContext(new[] { priorDirection, newerDirection })
                    .Contains("romance develop slowly", StringComparison.Ordinal))
                throw new InvalidOperationException("Replacing a working direction did not archive the old one.");
            if (!StoryCreativeDecision.Revise(newerDirection, "Keep a gentle romantic pace.", DateTimeOffset.UtcNow) ||
                newerDirection.CreativeDirectionRevisions?.Count != 1 ||
                newerDirection.CreativeDirectionRevisions?[0].PreviousText != "Let their romance develop slowly.")
                throw new InvalidOperationException("Direction revisions did not retain the previous wording.");
            string formerVersion = newerDirection.CreativeDirectionRevisions?.FirstOrDefault()?.PreviousText ?? string.Empty;
            if (!StoryCreativeDecision.Revise(newerDirection, formerVersion, DateTimeOffset.UtcNow) ||
                newerDirection.CreativeDirectionRevisions?.Count != 2 ||
                newerDirection.CreativeDirection != formerVersion ||
                StoryCreativeDecision.Replace(newerDirection, newerDirection, "invalid", DateTimeOffset.UtcNow))
                throw new InvalidOperationException("Revision restoration/self-replacement violated intent history.");

            // Creator-assigned categories are optional and never guessed from
            // Gemini's prose. Search/filter must not change the source messages.
            if (!StoryCreativeDecision.Revise(newerDirection, newerDirection.CreativeDirection,
                    DateTimeOffset.UtcNow, "Characters") ||
                newerDirection.CreativeDirectionCategory != "Characters" ||
                !StoryIdeaOrganizer.Matches(newerDirection, "romance", StoryIdeaOrganizer.Active, "Characters") ||
                StoryIdeaOrganizer.Matches(newerDirection, "romance", StoryIdeaOrganizer.Archived, "Characters") ||
                StoryIdeaOrganizer.Matches(newerDirection, "romance", StoryIdeaOrganizer.Active, "World") ||
                StoryIdeaOrganizer.Matches(newerDirection, "unrelated", StoryIdeaOrganizer.All, StoryIdeaOrganizer.All) ||
                StoryIdeaOrganizer.MatchesDecline("A declined lead", "declined", StoryIdeaOrganizer.Active, StoryIdeaOrganizer.All) ||
                !StoryIdeaOrganizer.MatchesDecline("A declined lead", "declined", StoryIdeaOrganizer.Passed, StoryIdeaOrganizer.All) ||
                StoryIdeaOrganizer.NormalizeCategory("unknown") != StoryIdeaOrganizer.General)
                throw new InvalidOperationException("Author idea category/search/status filters leaked or misclassified data.");
            string organizedContext = StoryCoCreation.BuildContext(new[] { newerDirection });
            if (!organizedContext.Contains("[Characters]", StringComparison.Ordinal) ||
                organizedContext.Length > StoryCoCreation.MaxContextLength)
                throw new InvalidOperationException("Creative category exceeded context budget or disappeared.");
            StoryCreativeDecision.Revise(newerDirection, newerDirection.CreativeDirection,
                DateTimeOffset.UtcNow, StoryIdeaOrganizer.General);
            if (newerDirection.CreativeDirectionCategory is not null)
                throw new InvalidOperationException("General category should remain omitted for old JSON compatibility.");
            StoryCreativeDecision.Revise(newerDirection, newerDirection.CreativeDirection,
                DateTimeOffset.UtcNow, "Characters");

            // Persist all new fields with the normal conversation format: the same
            // file that branch checkpoints snapshot. No second global idea store.
            StoryCreativeDecision.SetDeclined(suggestedByGemini, suggestions[0], true);
            await store.SaveMessagesAsync(projectId, new[] { priorDirection, newerDirection, suggestedByGemini });
            IReadOnlyList<StoryMessage> decisionsRoundTrip = await store.LoadMessagesAsync(projectId);
            if (decisionsRoundTrip.Count != 3 || !decisionsRoundTrip[0].CreativeDirectionArchived ||
                decisionsRoundTrip[1].CreativeDirectionRevisions?.Count != 2 ||
                decisionsRoundTrip[1].CreativeDirection != formerVersion ||
                decisionsRoundTrip[1].CreativeDirectionCategory != "Characters" ||
                decisionsRoundTrip[2].DeclinedCreativeSuggestions?.Count != 1)
                throw new InvalidOperationException("Co-Creation 3.0 decision history was lost during JSON round-trip.");

            // Many old decisions are retained on disk, but only a bounded set
            // is sent to a model, without crowding out canonical evidence.
            var longHistory = new List<StoryMessage>();
            for (int i = 0; i < 80; i++)
            {
                longHistory.Add(new StoryMessage
                {
                    Role = "user", Action = StoryActionType.Chat.ToString(),
                    CreativeDirection = $"Approved direction {i}: " + new string('c', 130)
                });
                var options = new StoryMessage
                {
                    Role = "assistant", Action = StoryActionType.Chat.ToString(),
                    Content = $"1. This proposal number {i} is explicitly declined, and should not be repeated."
                };
                StoryCreativeDecision.SetDeclined(options,
                    StoryCreativeSuggestionExtractor.Extract(options.Content).Single(), true);
                longHistory.Add(options);
            }
            string compactIntent = StoryCoCreation.BuildContext(longHistory);
            if (compactIntent.Length > StoryCoCreation.MaxContextLength ||
                !compactIntent.Contains("CREATOR-APPROVED", StringComparison.Ordinal) ||
                !compactIntent.Contains("CREATOR-DECLINED", StringComparison.Ordinal))
                throw new InvalidOperationException("Long-session approved/rejected context exceeded the limit.");

            // A queued/background JSON write must never capture later edits to
            // the same live UI objects. Immutable snapshot holds value and lists.
            var live = new StoryMessage
            {
                Role = "user", Action = StoryActionType.Chat.ToString(),
                Content = "Original committed message",
                CreativeDirection = "Original direction",
                CreativeDirectionCategory = "Plot",
                CreativeDirectionRevisions = new() { new StoryCreativeRevision { PreviousText = "Older" } },
                DeclinedCreativeSuggestions = new() { "Passed option" }
            };
            Task valueWrite = store.SaveMessagesAsync(projectId, new[] { live });
            live.Content = "Changed after save began";
            live.CreativeDirectionCategory = "World";
            if (live.CreativeDirectionRevisions is { Count: > 0 } revisions)
                revisions[0].PreviousText = "Changed revision";
            if (live.DeclinedCreativeSuggestions is { } declines)
                declines.Add("Another decline");
            await valueWrite;
            StoryMessage captured = (await store.LoadMessagesAsync(projectId)).Single();
            if (captured.Content != "Original committed message" ||
                captured.CreativeDirectionCategory != "Plot" ||
                captured.CreativeDirectionRevisions?[0].PreviousText != "Older" ||
                captured.DeclinedCreativeSuggestions?.Count != 1)
                throw new InvalidOperationException("Background conversation writer observed mutable UI state after snapshot.");

            // Concurrent UI saves must commit in requested order, and approval
            // metadata must survive the atomic JSON round-trip.
            Task firstWrite = store.SaveMessagesAsync(projectId, new[] { suggestedByGemini });
            Task secondWrite = store.SaveMessagesAsync(projectId, new[] { suggestedByGemini, choseSecond });
            await Task.WhenAll(firstWrite, secondWrite);
            IReadOnlyList<StoryMessage> acceptedRoundTrip = await store.LoadMessagesAsync(projectId);
            if (acceptedRoundTrip.Count != 2 ||
                acceptedRoundTrip[1].CreativeDirection != suggestions[1])
                throw new InvalidOperationException("Concurrent co-creation conversation writes lost an accepted idea.");

            // The production load path uses this same pure parser. Exercise
            // malformed JSON without creating an expected ERROR in a PASS report.
            bool refusedCorruption = false;
            try { StoryProjectStore.ParseConversationMessages("{corrupt conversation json"); }
            catch (System.IO.InvalidDataException) { refusedCorruption = true; }
            bool refusedNullMessage = false;
            try { StoryProjectStore.ParseConversationMessages("[null]"); }
            catch (System.IO.InvalidDataException) { refusedNullMessage = true; }
            if (!refusedCorruption || !refusedNullMessage)
                throw new InvalidOperationException("Invalid conversation JSON was silently accepted.");

            // Chat UI must window large histories without truncating durable data.
            if (StoryConversationPaging.FirstVisible(59, 60) != 0 ||
                StoryConversationPaging.FirstVisible(250, 60) != 190 ||
                StoryConversationPaging.NextVisible(250, 60) != 120 ||
                StoryConversationPaging.FirstVisible(250, 120) != 130 ||
                StoryConversationPaging.NextVisible(65, 60) != 65)
                throw new InvalidOperationException("Long conversation paging boundaries regressed.");
        }
        finally
        {
            await store.SaveMessagesAsync(projectId, previous);
        }
    }
}
