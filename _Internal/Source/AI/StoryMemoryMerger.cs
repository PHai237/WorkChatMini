namespace WorkChatMini;

public static class StoryMemoryMerger
{
    public static void Apply(
        StoryMemoryDocument memory,
        StoryMemoryUpdate update,
        string sourceLabel,
        StoryChapterMemory? chapter = null)
    {
        update = StoryMemoryUpdateNormalizer.Normalize(update);
        foreach (StoryCanonFactPatch patch in update.Canon.Where(x => !string.IsNullOrWhiteSpace(x.Fact)))
        {
            StoryCanonFact? existing = memory.Canon.FirstOrDefault(x => Same(x.Fact, patch.Fact));
            if (existing is null)
            {
                memory.Canon.Add(new StoryCanonFact
                {
                    Fact = patch.Fact.Trim(),
                    Status = NormalizeStatus(patch.Status),
                    Source = sourceLabel
                });
            }
            else
            {
                existing.Status = PreferStatus(existing.Status, patch.Status);
                existing.Source = sourceLabel;
                existing.UpdatedUtc = DateTimeOffset.UtcNow;
            }
        }

        foreach (StoryCharacterPatch patch in update.Characters.Where(x => !string.IsNullOrWhiteSpace(x.Name)))
            MergeCharacter(memory, patch);

        StoryRelationshipLedger.Merge(memory, update.Relationships, sourceLabel, chapter);

        foreach (StoryTimelinePatch patch in update.Timeline.Where(x => !string.IsNullOrWhiteSpace(x.Event)))
        {
            if (!memory.Timeline.Any(x => Same(x.When, patch.When) && Same(x.Event, patch.Event)))
                memory.Timeline.Add(new StoryTimelineEvent { When = patch.When.Trim(), Event = patch.Event.Trim(), Source = sourceLabel });
        }

        foreach (StoryThreadPatch patch in update.OpenThreads.Where(x => !string.IsNullOrWhiteSpace(x.Title)))
        {
            StoryPlotThread? thread = memory.Threads.FirstOrDefault(x => Same(x.Title, patch.Title));
            if (thread is null)
            {
                memory.Threads.Add(new StoryPlotThread
                {
                    Title = patch.Title.Trim(),
                    Detail = patch.Detail.Trim(),
                    Status = "Open",
                    Importance = StoryNarrativeFocus.NormalizeImportance(patch.Importance),
                    OpenedChapter = chapter?.Number,
                    LastTouchedChapter = chapter?.Number,
                    Source = sourceLabel
                });
            }
            else
            {
                thread.Detail = Choose(patch.Detail, thread.Detail);
                thread.Status = "Open";
                thread.Importance = PreferImportance(thread.Importance, patch.Importance);
                thread.LastTouchedChapter = chapter?.Number ?? thread.LastTouchedChapter;
                thread.Source = sourceLabel;
                thread.UpdatedUtc = DateTimeOffset.UtcNow;
            }
        }

        foreach (string resolved in update.ResolvedThreads.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            StoryPlotThread? thread = memory.Threads.FirstOrDefault(x => Similar(x.Title, resolved) || Similar(x.Detail, resolved));
            if (thread is not null)
            {
                thread.Status = "Resolved";
                thread.LastTouchedChapter = chapter?.Number ?? thread.LastTouchedChapter;
                thread.UpdatedUtc = DateTimeOffset.UtcNow;
            }
        }

        foreach (StoryLorePatch patch in update.Lore.Where(x => !string.IsNullOrWhiteSpace(x.Name) && !string.IsNullOrWhiteSpace(x.Detail)))
        {
            StoryLoreEntry? entry = memory.Lore.FirstOrDefault(x => Same(x.Category, patch.Category) && Same(x.Name, patch.Name));
            if (entry is null)
            {
                memory.Lore.Add(new StoryLoreEntry
                {
                    Category = string.IsNullOrWhiteSpace(patch.Category) ? "World" : patch.Category.Trim(),
                    Name = patch.Name.Trim(),
                    Detail = patch.Detail.Trim(),
                    Status = NormalizeStatus(patch.Status)
                });
            }
            else
            {
                entry.Detail = patch.Detail.Trim();
                entry.Status = PreferStatus(entry.Status, patch.Status);
                entry.UpdatedUtc = DateTimeOffset.UtcNow;
            }
        }

        StoryEventLedger.Merge(memory, update.Events, sourceLabel, chapter);
        StoryArcLedger.Merge(memory, update.Arcs, sourceLabel, chapter);
        StoryCharacterArcLedger.Merge(memory, update.CharacterArcs, sourceLabel, chapter);
        StoryWorldStateGraph.Merge(memory, update.Locations, update.SceneStates, sourceLabel, chapter);
        StoryCanonRevisionLedger.Apply(memory, update.Revisions, sourceLabel, chapter);
        StoryCausalMemoryLedger.Merge(memory, update.NarrativePromises, update.CausalLinks, sourceLabel, chapter);

        memory.SchemaVersion = Math.Max(memory.SchemaVersion, 6);
        memory.UpdatedUtc = DateTimeOffset.UtcNow;
    }

    public static int ApproveAllProposed(StoryMemoryDocument memory)
    {
        int changed = 0;
        foreach (StoryCanonFact fact in memory.Canon.Where(x => string.Equals(x.Status, "Proposed", StringComparison.OrdinalIgnoreCase)))
        {
            fact.Status = "Confirmed";
            fact.UpdatedUtc = DateTimeOffset.UtcNow;
            changed++;
        }
        foreach (StoryLoreEntry lore in memory.Lore.Where(x => string.Equals(x.Status, "Proposed", StringComparison.OrdinalIgnoreCase)))
        {
            lore.Status = "Confirmed";
            lore.UpdatedUtc = DateTimeOffset.UtcNow;
            changed++;
        }
        foreach (StorySignificantEvent storyEvent in memory.Events.Where(x => string.Equals(x.Status, "Proposed", StringComparison.OrdinalIgnoreCase)))
        {
            storyEvent.Status = "Confirmed";
            changed++;
        }
        return changed;
    }

    public static int DiscardAllProposed(StoryMemoryDocument memory)
    {
        int before = memory.Canon.Count + memory.Lore.Count + memory.Events.Count;
        memory.Canon.RemoveAll(x => string.Equals(x.Status, "Proposed", StringComparison.OrdinalIgnoreCase));
        memory.Lore.RemoveAll(x => string.Equals(x.Status, "Proposed", StringComparison.OrdinalIgnoreCase));
        memory.Events.RemoveAll(x => string.Equals(x.Status, "Proposed", StringComparison.OrdinalIgnoreCase));
        return before - memory.Canon.Count - memory.Lore.Count - memory.Events.Count;
    }

    private static void MergeCharacter(StoryMemoryDocument memory, StoryCharacterPatch patch)
    {
        StoryCharacterMemory? character = StoryCharacterIdentity.ResolveUnique(memory, patch.Name, out bool ambiguous);
        if (ambiguous)
            return; // Reject ambiguous state delta; never mutate a random alias owner.
        if (character is null)
        {
            character = new StoryCharacterMemory { Name = patch.Name.Trim() };
            memory.Characters.Add(character);
        }
        Union(character.Aliases, patch.Aliases);
        character.Appearance = Choose(patch.Appearance, character.Appearance);
        character.Personality = Choose(patch.Personality, character.Personality);
        Union(character.Goals, patch.Goals);
        Remove(character.Goals, patch.RemovedGoals);
        Union(character.Fears, patch.Fears);
        Union(character.Secrets, patch.Secrets);
        Union(character.Knowledge, patch.Knowledge);
        Remove(character.Knowledge, patch.ForgottenKnowledge);
        character.CurrentLocation = Choose(patch.CurrentLocation, character.CurrentLocation);
        character.Condition = Choose(patch.Condition, character.Condition);
        Union(character.Abilities, patch.Abilities);
        Remove(character.Abilities, patch.RemovedAbilities);
        Union(character.Inventory, patch.Inventory);
        Remove(character.Inventory, patch.RemovedInventory);
        character.Progression = Choose(patch.Progression, character.Progression);
        character.Notes = Choose(patch.Notes, character.Notes);
        character.UpdatedUtc = DateTimeOffset.UtcNow;
    }

    private static void Remove(List<string> target, IEnumerable<string> values)
    {
        foreach (string value in values.Where(x => !string.IsNullOrWhiteSpace(x)))
            target.RemoveAll(x => Same(x, value));
    }

    private static void Union(List<string> target, IEnumerable<string> values)
    {
        foreach (string value in values.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            string trimmed = value.Trim();
            if (!target.Any(x => Same(x, trimmed)))
                target.Add(trimmed);
        }
    }

    private static string NormalizeStatus(string status) => string.Equals(status, "Proposed", StringComparison.OrdinalIgnoreCase) ? "Proposed" : "Confirmed";
    private static string PreferStatus(string existing, string incoming)
    {
        if (string.Equals(existing, "Retconned", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(existing, "Contradicted", StringComparison.OrdinalIgnoreCase))
            return existing;
        return string.Equals(existing, "Confirmed", StringComparison.OrdinalIgnoreCase) || !string.Equals(incoming, "Proposed", StringComparison.OrdinalIgnoreCase)
            ? "Confirmed" : "Proposed";
    }
    private static string PreferImportance(string existing, string incoming)
    {
        static int Rank(string value) => StoryNarrativeFocus.NormalizeImportance(value) switch { "Critical" => 3, "Major" => 2, _ => 1 };
        return Rank(incoming) > Rank(existing) ? StoryNarrativeFocus.NormalizeImportance(incoming) : StoryNarrativeFocus.NormalizeImportance(existing);
    }
    private static string Choose(string candidate, string fallback) => string.IsNullOrWhiteSpace(candidate) ? fallback : candidate.Trim();
    private static bool Same(string left, string right) => string.Equals(left.Trim(), right.Trim(), StringComparison.OrdinalIgnoreCase);
    private static bool Similar(string left, string right) => left.Contains(right, StringComparison.OrdinalIgnoreCase) || right.Contains(left, StringComparison.OrdinalIgnoreCase);
}
