namespace WorkChatMini;

/// <summary>Deterministic regressions for long-session navigation and visual paging.
/// Runs inside the existing Story foundation test without a real AI or user data.</summary>
internal static class StoryLongSessionSelfTest
{
    internal static void Verify()
    {
        StoryConversationContextSelfTest.Verify();
        // Interrupted provider responses are readable history, not accepted
        // ideas or chapter drafts, including after JSON snapshots/reloads.
        const string delivered = "1. Bí mật của nhân vật có thể đổi hướng.\nSome streamed prose...";
        var incompleteChat = new StoryMessage
        {
            Role = "assistant", Action = StoryActionType.Chat.ToString()
        };
        if (!StoryInterruptedGeneration.TryPrepare(incompleteChat, delivered, stopped: false) ||
            !incompleteChat.IsInterruptedResponse || !incompleteChat.Content.StartsWith(delivered, StringComparison.Ordinal) ||
            !incompleteChat.Content.EndsWith("[Generation interrupted · not committed to Story]", StringComparison.Ordinal) ||
            incompleteChat.IsNarrativeDraft || incompleteChat.ManuscriptCommitted ||
            StoryInterruptedGeneration.TryPrepare(new StoryMessage(), "  ", stopped: true))
            throw new InvalidOperationException("Interrupted output was truncated or marked as canon.");
        var incompleteNarrative = new StoryMessage
        {
            Role = "assistant", Action = StoryActionType.ContinueStory.ToString(),
            ProposalKind = "Lore", ProposalJson = "invalid proposal"
        };
        if (!StoryInterruptedGeneration.TryPrepare(incompleteNarrative, "Partial chapter", stopped: true) ||
            incompleteNarrative.ProposalJson.Length != 0 || incompleteNarrative.ProposalKind.Length != 0 ||
            !incompleteNarrative.Content.Contains("[Stopped before completion · not committed to Story]", StringComparison.Ordinal))
            throw new InvalidOperationException("Stopped draft retained a writable proposal or lost its incomplete marker.");
        // Retry is offered only for the last unanswered/incomplete prompt.
        var unanswered = new StoryMessage { Role = "user", Content = "Continue this scene" };
        var completed = new StoryMessage { Role = "assistant", Content = "Completed scene", ManuscriptCommitted = true };
        if (!ReferenceEquals(StoryRetryEligibility.FindLastUnansweredPrompt(new[] { unanswered }), unanswered) ||
            !ReferenceEquals(StoryRetryEligibility.FindLastUnansweredPrompt(new[] { unanswered, incompleteChat }), unanswered) ||
            StoryRetryEligibility.FindLastUnansweredPrompt(new[] { unanswered, completed }) is not null ||
            StoryRetryEligibility.FindLastUnansweredPrompt(Array.Empty<StoryMessage>()) is not null)
            throw new InvalidOperationException("Retry offered for a finished Story or lost an unanswered prompt.");
        // Editing never destroys an earlier response: it is persisted as history,
        // excluded from LLM context and skipped when looking for a retryable prompt.
        var archivedResponse = new StoryMessage
        {
            Role = "assistant", Action = StoryActionType.Chat.ToString(),
            Content = "Old answer", IsSupersededResponse = true
        };
        if (!ReferenceEquals(StoryRetryEligibility.FindLastUnansweredPrompt(
                new[] { unanswered, archivedResponse, incompleteChat }), unanswered) ||
            !ReferenceEquals(StoryRetryEligibility.FindLastUnansweredPrompt(
                new[] { unanswered, archivedResponse }), unanswered) ||
            StoryDraftIsolation.ForGeneration(new[] { unanswered, archivedResponse }).Count != 1)
            throw new InvalidOperationException("Edited prompt could not retry, or old response leaked into context.");
        StoryMessage[] archivedSnapshot = StoryMessageSnapshot.Capture(new[] { archivedResponse });
        if (!StoryProjectStore.ParseConversationMessages(
                System.Text.Json.JsonSerializer.Serialize(archivedSnapshot))[0].IsSupersededResponse ||
            System.Text.Json.JsonSerializer.Serialize(new StoryMessage())
                .Contains("IsSupersededResponse", StringComparison.Ordinal))
            throw new InvalidOperationException("Regenerated response history was not backwards-compatible.");
        var incompleteHistory = new[]
        {
            new StoryMessage { Role = "user", Action = StoryActionType.Chat.ToString(), Content = "Brainstorm options" },
            incompleteChat,
            new StoryMessage { Role = "user", Action = StoryActionType.ContinueStory.ToString(), Content = "Continue" },
            incompleteNarrative
        };
        StoryMessage[] filteredHistory = StoryDraftIsolation.ForGeneration(incompleteHistory).ToArray();
        if (filteredHistory.Length != 1 || filteredHistory[0] != incompleteHistory[0])
            throw new InvalidOperationException("An incomplete response leaked into future model conversation context.");
        var proposedApproval = new StoryMessage
        {
            Role = "user", Action = StoryActionType.Chat.ToString(), Content = "Chốt: phương án 1"
        };
        StoryCoCreation.CaptureCreatorConfirmation(proposedApproval, new[] { incompleteChat });
        if (proposedApproval.CreativeDirection.Length > 0 ||
            StoryCreativeDecision.SetDeclined(incompleteChat,
                StoryCoCreation.NormalizeDirection("Bí mật của nhân vật có thể đổi hướng."), true))
            throw new InvalidOperationException("Incomplete AI output was accidentally approved as a creative idea.");
        string interruptedJson = System.Text.Json.JsonSerializer.Serialize(StoryMessageSnapshot.Capture(incompleteHistory));
        IReadOnlyList<StoryMessage> restoredInterrupted = StoryProjectStore.ParseConversationMessages(interruptedJson);
        if (!restoredInterrupted[1].IsInterruptedResponse || !restoredInterrupted[3].IsInterruptedResponse ||
            !restoredInterrupted[1].Content.StartsWith(delivered, StringComparison.Ordinal) ||
            System.Text.Json.JsonSerializer.Serialize(new StoryMessage()).Contains("IsInterruptedResponse", StringComparison.Ordinal))
            throw new InvalidOperationException("Interrupted reply status was lost, or old conversation JSON changed unnecessarily.");

        if (StoryInterruptedGeneration.TryPrepare(incompleteChat, "another interrupted reply", stopped: true) ||
            !incompleteChat.Content.StartsWith(delivered, StringComparison.Ordinal))
            throw new InvalidOperationException("Reprocessing an incomplete response appended a second stop marker.");
        var completeChapter = new StoryMessage
        {
            Role = "assistant", Action = StoryActionType.ContinueStory.ToString(),
            IsNarrativeDraft = true, ManuscriptCommitted = true, Content = "Committed chapter"
        };
        if (StoryInterruptedGeneration.TryPrepare(completeChapter, "partial bytes", stopped: false) ||
            completeChapter.Content != "Committed chapter")
            throw new InvalidOperationException("A committed chapter was overwritten by interruption recovery.");

        string longLastPage = new string('a', 69000) + "Bí mật chiếc đồng hồ" + new string('z', 1000);
        int lateMatch = StoryConversationSearch.FindMatchPosition(longLastPage, "bi mat chiec dong ho");
        int sliceStart = StoryLongMessageWindow.StartForMatch(longLastPage.Length, lateMatch);
        (int fromMatch, int toMatch) = StoryLongMessageWindow.Slice(longLastPage.Length, sliceStart);
        if (lateMatch < 69000 || fromMatch > lateMatch || toMatch < lateMatch + 21 ||
            toMatch - fromMatch > StoryLongMessageWindow.ChunkCharacters ||
            StoryLongMessageWindow.StartForMatch(500, 400) != 0 ||
            StoryConversationSearch.FindMatchPosition("A\u0301bcd", "abcd") != 0 ||
            StoryConversationSearch.FindMatchPosition("completely unrelated", "missing") != -1)
            throw new InvalidOperationException("Searching a long message failed to show the actual matched excerpt.");

        // Fail closed on damaged JSON instead of replacing an unreadable canon.
        foreach (string broken in new[] { "", "null", "{ broken json" })
        {
            try
            {
                _ = StoryMemoryStore.ParseMemoryDocument(broken);
                throw new InvalidOperationException("Malformed Story Memory was accepted as a new blank document.");
            }
            catch (System.IO.InvalidDataException) { /* expected */ }
        }
        var original = new StoryMemoryDocument();
        original.Canon.Add(new StoryCanonFact { Fact = "Old truth" });
        StoryMemoryDocument staged = StoryMemoryStaging.Clone(original);
        staged.Canon[0].Fact = "Updated truth";
        staged.Canon.Add(new StoryCanonFact { Fact = "Uncommitted proposal" });
        if (original.Canon.Count != 1 || original.Canon[0].Fact != "Old truth")
            throw new InvalidOperationException("Proposal staging mutated live canon before successful save.");

        var proposal = new StoryMessage
        {
            CreatedUtc = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero),
            ProposalKind = "Lore", ProposalJson = "{\"Lore\":[]}", Content = "Proposal"
        };
        StoryProposalCommit.MarkApplied(staged, proposal);
        StoryMemoryDocument reopened = StoryMemoryStore.ParseMemoryDocument(
            System.Text.Json.JsonSerializer.Serialize(staged));
        if (!StoryProposalCommit.AlreadyApplied(reopened, proposal) ||
            StoryProposalCommit.AlreadyApplied(original, proposal))
            throw new InvalidOperationException("Proposal receipt was not durable or leaked into original Memory.");
        StoryProposalCommit.MarkApplied(reopened, proposal);
        if (reopened.AppliedProposalKeys?.Count != 1)
            throw new InvalidOperationException("Repeating a proposal receipt created duplicate bookkeeping.");

        if (StoryIdeaOrganizer.CategoryFilters.Count < 6 ||
            StoryIdeaOrganizer.StatusFilters.Count != 4)
            throw new InvalidOperationException("Creative Ideas toolbar filters are incomplete.");
        var navigation = new StoryProjectSwitchQueue();
        if (!navigation.TryBegin("story-a") || !navigation.IsBusy ||
            navigation.TryBegin("story-b") || navigation.TryBegin("story-c") ||
            navigation.Complete() != "story-c")
            throw new InvalidOperationException("Rapid Story selection did not retain the latest request.");

        // A new selection while C is loading must not launch a parallel restore.
        if (!navigation.IsBusy || navigation.TryBegin("story-d") ||
            navigation.Complete() != "story-d" || navigation.Complete() is not null ||
            navigation.IsBusy)
            throw new InvalidOperationException("Story load queue overlapped or failed to drain.");

        if (!navigation.TryBegin("story-e"))
            throw new InvalidOperationException("Story load queue did not release after completion.");
        navigation.TryBegin("story-f");
        navigation.Abort();
        if (navigation.IsBusy || !navigation.TryBegin("story-g") || navigation.Complete() is not null)
            throw new InvalidOperationException("Story load queue did not recover after an abort.");

        // Search must see stored messages outside the initial 60 rendered bubbles.
        var texts = Enumerable.Range(0, 1000).Select(i => i == 14
            ? "A rare clue: the copper compass" : i == 811
                ? "Later, the COPPER COMPASS returned" : $"Message {i}").ToArray();
        IReadOnlyList<int> found = StoryConversationSearch.FindMatches(texts, "copper compass");
        if (!found.SequenceEqual(new[] { 14, 811 }) ||
            StoryConversationSearch.FindMatches(texts, "   ").Count != 0 ||
            StoryConversationSearch.FindMatches(texts, "Not present").Count != 0 ||
            StoryConversationSearch.FindMatches(texts, "  COPPER Compass  ").Count != 2)
            throw new InvalidOperationException("Full-history conversation search lost or fabricated results.");
        string[] vietnamese = { "Chốt hướng nhân vật này", "Bí mật ở quán cà phê", "Đang tìm chiếc đồng hồ" };
        if (!StoryConversationSearch.FindMatches(vietnamese, "chot huong").SequenceEqual(new[] { 0 }) ||
            !StoryConversationSearch.FindMatches(vietnamese, "ca phe").SequenceEqual(new[] { 1 }) ||
            !StoryConversationSearch.FindMatches(vietnamese, "dang tim").SequenceEqual(new[] { 2 }) ||
            StoryConversationSearch.FindMatches(vietnamese, "khong co").Count != 0)
            throw new InvalidOperationException("Vietnamese accent-insensitive Chat search lost or invented a match.");

        (int firstStart, int firstEnd) = StoryConversationSearch.Around(1000, found[0]);
        (int laterStart, int laterEnd) = StoryConversationSearch.Around(1000, found[1]);
        (int emptyStart, int emptyEnd) = StoryConversationSearch.Around(0, 0);
        if (firstStart != 0 || firstEnd != 60 ||
            laterStart > 811 || laterEnd <= 811 || laterEnd - laterStart != 60 ||
            emptyStart != 0 || emptyEnd != 0)
            throw new InvalidOperationException("Conversation search navigation did not bound the WPF window.");

        // Bounded reader: traverse 2,000 messages in both directions without
        // constructing an ever-growing native WPF visual tree. Consecutive
        // windows must overlap so the reader never loses place or skips content.
        int visible = StoryConversationPaging.PageSize;
        int pageEnd = 0;
        (int start, int end) = StoryConversationPaging.Window(2000, visible, pageEnd);
        var covered = new bool[2000];
        int previousStart = start;
        int steps = 0;
        while (true)
        {
            if (end - start > StoryConversationPaging.MaxVisible || start < 0 || end > 2000)
                throw new InvalidOperationException("Conversation render window exceeded its 180-message budget.");
            for (int n = start; n < end; n++) covered[n] = true;
            if (start == 0) break;
            (visible, pageEnd) = StoryConversationPaging.Older(2000, visible, pageEnd);
            (start, end) = StoryConversationPaging.Window(2000, visible, pageEnd);
            if (start >= previousStart || ++steps > 100)
                throw new InvalidOperationException("Older message navigation stalled or skipped its boundary.");
            previousStart = start;
        }
        if (covered.Any(value => !value))
            throw new InvalidOperationException("Bounded history navigation lost a persisted message.");
        steps = 0;
        while (end < 2000)
        {
            int oldEnd = end;
            (visible, pageEnd) = StoryConversationPaging.Later(2000, visible, pageEnd);
            (start, end) = StoryConversationPaging.Window(2000, visible, pageEnd);
            if (end <= oldEnd || end - start > StoryConversationPaging.MaxVisible || ++steps > 100)
                throw new InvalidOperationException("Later message navigation stalled or exceeded its window.");
        }
        if (pageEnd != 0 || StoryConversationPaging.Window(2000, 60, 0) != (1940, 2000) ||
            StoryConversationPaging.Window(0, 60, 0) != (0, 0) ||
            StoryConversationPaging.Window(20, 180, 100) != (0, 20))
            throw new InvalidOperationException("Conversation latest/empty window math regressed.");
        // Reading an old page remains fixed when a new message arrives.
        (visible, pageEnd) = StoryConversationPaging.Older(2000, 180, 2000);
        if (StoryConversationPaging.Window(2001, visible, pageEnd).End != 1940)
            throw new InvalidOperationException("Appending a message unexpectedly moved the history reader.");

        // Long single messages are separate from the 180-row budget. A huge
        // response must remain fully navigable without a huge WPF TextBlock.
        const int longLength = 71000;
        var longCoverage = new bool[longLength];
        int messageStart = 0;
        int segmentSteps = 0;
        while (true)
        {
            (int from, int to) = StoryLongMessageWindow.Slice(longLength, messageStart);
            if (to - from > StoryLongMessageWindow.ChunkCharacters || to <= from)
                throw new InvalidOperationException("Long message excerpt exceeded visual text budget.");
            for (int n = from; n < to; n++) longCoverage[n] = true;
            int next = StoryLongMessageWindow.Next(longLength, messageStart);
            if (to == longLength)
            {
                if (next != messageStart) throw new InvalidOperationException("Long message end did not stop.");
                break;
            }
            if (next <= messageStart || ++segmentSteps > 30)
                throw new InvalidOperationException("Long message navigation stalled.");
            messageStart = next;
        }
        if (longCoverage.Any(x => !x) || StoryLongMessageWindow.Previous(0) != 0)
            throw new InvalidOperationException("A portion of the full saved response became inaccessible.");
        for (int i = 0; i < segmentSteps; i++)
            messageStart = StoryLongMessageWindow.Previous(messageStart);
        if (messageStart != 0 || StoryLongMessageWindow.Slice(0, 0) != (0, 0) ||
            StoryLongMessageWindow.Slice(120, 0) != (0, 120))
            throw new InvalidOperationException("Long message reverse navigation or short text failed.");
        var growing = new System.Text.StringBuilder(new string('x', 72000));
        string tail = StoryLongMessageWindow.LiveTail(growing);
        if (tail.Length > StoryLongMessageWindow.ChunkCharacters + 130 ||
            !tail.EndsWith(new string('x', StoryLongMessageWindow.ChunkCharacters), StringComparison.Ordinal) ||
            !string.Equals(growing.ToString(), new string('x', 72000), StringComparison.Ordinal))
            throw new InvalidOperationException("Streaming display lost its full source or grew without bound.");
        if (StoryLongMessageWindow.FullForCopy(growing.ToString(), null, tail).Length != 72000 ||
            StoryLongMessageWindow.FullForCopy(string.Empty, growing, tail).Length != 72000 ||
            StoryLongMessageWindow.FullForCopy("full message", null, "excerpt") != "full message")
            throw new InvalidOperationException("Copy must use the complete saved or streamed text, not an excerpt.");
        using (var cancelled = new CancellationTokenSource())
        {
            cancelled.Cancel();
            try
            {
                _ = StoryConversationSearch.FindMatches(new[] { "long story" }, "story", cancelled.Token);
                throw new InvalidOperationException("Cancelled history search continued scanning.");
            }
            catch (OperationCanceledException) { /* expected */ }
        }

        if (StoryConversationPaging.FirstVisible(1000, StoryConversationPaging.PageSize) != 940 ||
            StoryConversationPaging.NextVisible(1000, StoryConversationPaging.PageSize) != 120 ||
            StoryConversationPaging.FirstVisible(10, StoryConversationPaging.PageSize) != 0 ||
            StoryConversationPaging.NextVisible(10, StoryConversationPaging.PageSize) != 10 ||
            StoryConversationPaging.NextVisible(1000, 960) != 1000 ||
            StoryConversationPaging.FirstVisible(0, StoryConversationPaging.PageSize) != 0)
            throw new InvalidOperationException("Long conversation visual paging dropped or overran messages.");
    }
}
