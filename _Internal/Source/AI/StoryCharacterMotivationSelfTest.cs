namespace WorkChatMini;

// Runs inside the existing Story memory/retrieval test case (still 22 total).
internal static class StoryCharacterMotivationSelfTest
{
    internal static void Verify()
    {
        var memory = new StoryMemoryDocument();
        memory.Characters.Add(new StoryCharacterMemory
        {
            Name = "Magica", Aliases = new List<string> { "Silver Crow", "Watcher" },
            Goals = new List<string> { "Defend the city", "Find the lost map" }
        });
        memory.Characters.Add(new StoryCharacterMemory
        {
            Name = "Luna", Aliases = new List<string> { "Watcher" }
        });
        StoryChapterMemory chapter3 = new() { Number = 3, Title = "The gate" };
        StoryChapterMemory chapter5 = new() { Number = 5, Title = "The aftermath" };
        StoryChapterMemory chapter8 = new() { Number = 8, Title = "The broken oath" };
        memory.Chapters.AddRange(new[] { chapter3, chapter5, chapter8 });

        StoryMemoryMerger.Apply(memory, new StoryMemoryUpdate
        {
            Events = new List<StoryEventPatch>
            {
                new() { Type = "DecisionMade", Subject = "Silver Crow", Target = "The city gate",
                    Summary = "Magica chose to close the city gate against the invaders.",
                    Evidence = "Magica shut the gate while Luna was outside." },
                new() { Type = "DecisionMade", Subject = "Watcher", Target = "A hidden alternative",
                    Summary = "Someone called Watcher chose a different route.",
                    Evidence = "The shared nickname did not identify a character." },
                new() { Type = "PromiseMade", Subject = "Magica", Target = "Protect Luna",
                    Summary = "Magica vowed to keep Luna safe.", Status = "Proposed",
                    Evidence = "Magica spoke the oath directly to Luna." }
            }
        }, "Ch.3", chapter3);

        if (memory.Events.Count != 3 || memory.Events.Any(e => e.Type == "Custom"))
            throw new InvalidOperationException("Choice/vow event types were not preserved by Event Ledger.");

        StoryMemoryMerger.Apply(memory, new StoryMemoryUpdate
        {
            Events = new List<StoryEventPatch>
            {
                new() { Type = "WorldStateChanged", Subject = "City gate", Target = "Invader attack",
                    Summary = "The closed gate prevented the first attack.",
                    Evidence = "The invaders collided with the sealed gate and retreated." }
            }
        }, "Ch.5", chapter5);
        var mismatch = new StoryMemoryUpdate
        {
            Events = new List<StoryEventPatch>
            {
                new() { Type = "GoalAbandoned", Subject = "Magica", Target = "Find the lost map",
                    Summary = "Magica gives up the search.", Evidence = "She burned the map." }
            }
        };
        if (!StoryDeterministicContinuity.ValidateUpdate(memory, mismatch).Issues.Any(x =>
                x.Code == "abandoned-goal-still-current" && x.Severity == StoryDeterministicSeverity.Risk))
            throw new InvalidOperationException("A goal abandonment without a state delta was not flagged.");
        mismatch.Characters.Add(new StoryCharacterPatch
        {
            Name = "Silver Crow", RemovedGoals = new List<string> { "Find the lost map" }
        });
        if (StoryDeterministicContinuity.ValidateUpdate(memory, mismatch).Issues.Any(x =>
                x.Code == "abandoned-goal-still-current"))
            throw new InvalidOperationException("An identity-safe goal removal was flagged as inconsistent.");
        mismatch.Events[0].Evidence = string.Empty;
        if (!StoryDeterministicContinuity.ValidateUpdate(memory, mismatch).Issues.Any(x =>
                x.Code == "decision-missing-evidence" && x.Severity == StoryDeterministicSeverity.Risk))
            throw new InvalidOperationException("An unsubstantiated decision did not receive a Risk warning.");

        StoryMemoryMerger.Apply(memory, new StoryMemoryUpdate
        {
            Characters = new List<StoryCharacterPatch>
            {
                new() { Name = "Magica", RemovedGoals = new List<string> { "Find the lost map" } }
            },
            Events = new List<StoryEventPatch>
            {
                new() { Type = "GoalAbandoned", Subject = "Magica", Target = "Find the lost map",
                    Summary = "Magica abandoned the search to protect the city.",
                    Evidence = "Magica burned her map and said she would stop searching." },
                new() { Type = "PromiseBroken", Subject = "Luna", Target = "Return before dawn",
                    Summary = "Luna broke her promise to return before dawn.",
                    Evidence = "Luna stayed away after dawn, knowingly breaking the oath." }
            }
        }, "Ch.8", chapter8);

        if (memory.Characters[0].Goals.Contains("Find the lost map", StringComparer.OrdinalIgnoreCase) ||
            !memory.Characters[0].Goals.Contains("Defend the city"))
            throw new InvalidOperationException("Abandoned goal remained in current state after bookkeeping.");

        StorySignificantEvent choice = memory.Events.Single(e => e.Type == "DecisionMade" && e.Subject == "Silver Crow");
        StorySignificantEvent effect = memory.Events.Single(e => e.Type == "WorldStateChanged");
        StorySignificantEvent abandoned = memory.Events.Single(e => e.Type == "GoalAbandoned");
        memory.CausalLinks.Add(new StoryCausalLink
        {
            Relation = "CAUSES", From = choice.Id, To = effect.Id, ChapterNumber = 5,
            Evidence = "The closed gate directly stopped the invaders."
        });
        memory.CausalLinks.Add(new StoryCausalLink
        {
            Relation = "MOTIVATES", From = effect.Id, To = abandoned.Id, ChapterNumber = 8,
            Evidence = "Seeing the saved city convinced Magica to stay and defend it."
        });
        string focus = StoryCharacterMotivationFocus.Build(memory);
        if (!focus.Contains("Magica | Ch.3 DecisionMade", StringComparison.Ordinal) ||
            !focus.Contains("explicit consequence Ch.5", StringComparison.Ordinal) ||
            !focus.Contains("explicit driver Ch.5", StringComparison.Ordinal) ||
            !focus.Contains("Ch.8 GoalAbandoned", StringComparison.Ordinal) ||
            !focus.Contains("current goal: Defend the city", StringComparison.Ordinal) ||
            !StoryNarrativeFocus.Build(memory).Contains("CHARACTER CHOICES & CONSEQUENCES", StringComparison.Ordinal))
            throw new InvalidOperationException("Committed choices/explicit causes are missing from Continue Story context.");
        if (focus.Contains("Someone called Watcher", StringComparison.Ordinal) ||
            focus.Contains("Magica vowed to keep Luna safe", StringComparison.Ordinal) || focus.Length > 1450)
            throw new InvalidOperationException("Ambiguous actor leaked into bounded motivation context.");

        // A fabricated edge, a Proposed event, or an event on an uncommitted
        // chapter must not be presented as committed evidence.
        memory.CausalLinks[0].Evidence = string.Empty;
        if (StoryCharacterMotivationFocus.Build(memory).Contains("explicit consequence", StringComparison.Ordinal))
            throw new InvalidOperationException("Unsubstantiated causal relationship became a consequence.");
        memory.CausalLinks[0].Evidence = "The city gate stopped the attack.";
        memory.CausalLinks[0].ChapterNumber = 3;
        if (StoryCharacterMotivationFocus.Build(memory).Contains("explicit consequence", StringComparison.Ordinal))
            throw new InvalidOperationException("A causal link dated before its endpoint was accepted as proof.");
        memory.CausalLinks[0].ChapterNumber = 5;
        effect.Status = "Proposed";
        if (StoryCharacterMotivationFocus.Build(memory).Contains("explicit consequence", StringComparison.Ordinal))
            throw new InvalidOperationException("Proposed consequence was treated as established fact.");
        effect.Status = "Confirmed";
        memory.Chapters.Remove(chapter5);
        string withoutChapter = StoryCharacterMotivationFocus.Build(memory);
        if (withoutChapter.Contains("explicit consequence", StringComparison.Ordinal) ||
            withoutChapter.Contains("explicit driver", StringComparison.Ordinal))
            throw new InvalidOperationException("Missing committed chapter leaked through causal history.");
        memory.Chapters.Add(chapter5);

        // A Proposed record sharing an ID with an otherwise Confirmed choice
        // must also invalidate that choice as an unambiguous causal endpoint.
        memory.Events.Add(new StorySignificantEvent
        {
            Id = choice.Id, Type = "DecisionMade", Subject = "Luna",
            Summary = "A proposed alternate shares an identifier.", Evidence = "Uncommitted idea",
            Status = "Proposed", ChapterNumber = 5
        });
        if (StoryCharacterMotivationFocus.Build(memory).Contains("explicit consequence", StringComparison.Ordinal))
            throw new InvalidOperationException("Proposed record reused a confirmed event ID without losing the causal anchor.");
        memory.Events.RemoveAt(memory.Events.Count - 1);

        // A duplicated event ID makes both endpoints ambiguous; never pick one.
        memory.Events.Add(new StorySignificantEvent
        {
            Id = choice.Id, Type = "DecisionMade", Subject = "Magica",
            Summary = "Duplicate event with the same ID", Evidence = "Another on-page choice",
            Status = "Confirmed", ChapterNumber = 5
        });
        if (StoryCharacterMotivationFocus.Build(memory).Contains("explicit consequence", StringComparison.Ordinal))
            throw new InvalidOperationException("Duplicate event IDs were accepted as causal provenance.");

        var uncommitted = new StoryMemoryDocument();
        uncommitted.Characters.AddRange(memory.Characters);
        uncommitted.Events.Add(new StorySignificantEvent
        {
            Type = "PromiseBroken", Subject = "Magica", ChapterNumber = 40,
            Summary = "A promise broken only in an uncommitted alternative.",
            Evidence = "An abandoned draft showed this event."
        });
        if (!string.IsNullOrEmpty(StoryCharacterMotivationFocus.Build(uncommitted)))
            throw new InvalidOperationException("Uncommitted draft leaked into motivation history.");
    }
}
