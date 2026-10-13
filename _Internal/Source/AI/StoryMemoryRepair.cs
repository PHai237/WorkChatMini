namespace WorkChatMini;

// Conservative partial-memory repair. An AI extraction is not proof of a
// historical state: only previously unseen names explicitly appearing in the
// committed chapter may be added. Existing state is NEVER updated here.
internal static class StoryMemoryRepair
{
    internal const int MaxChaptersPerBatch = 20;
    internal const int MaxIndexedChapters = 500;

    // Names-only has never verified the chapter's relationships/events/timeline.
    // The warning includes it even though re-running the SAME name-only scan
    // would spend more API quota without providing a fuller audit.
    internal static int UnreviewedCount(StoryMemoryDocument memory) =>
        memory.Chapters.Count(chapter =>
            !string.Equals(chapter.BookkeepingStatus, "Reviewed", StringComparison.OrdinalIgnoreCase));

    internal static int NamesOnlyCount(StoryMemoryDocument memory) =>
        memory.Chapters.Count(chapter =>
            string.Equals(chapter.BookkeepingStatus, "NamesOnly", StringComparison.OrdinalIgnoreCase));

    internal static int PendingCount(StoryMemoryDocument memory) =>
        memory.Chapters.Count(chapter =>
            chapter.BookkeepingStatus is not ("Reviewed" or "NamesOnly"));

    internal static string BuildCoverageWarning(StoryMemoryDocument memory)
    {
        int missing = UnreviewedCount(memory);
        if (missing == 0) return string.Empty;
        int namesOnly = NamesOnlyCount(memory);
        return $"MEMORY COVERAGE: {missing} committed chapter(s) lack complete structured bookkeeping " +
            $"({namesOnly} were scanned for names only). " +
            "Names-only repair does NOT verify canon, events, relationships or timeline. " +
            "The absence of a character, relationship, event or fact from structured Memory is NOT proof it never occurred. " +
            "Use retrieved committed prose as evidence; do not invent missing details or treat proposals as canon.";
    }

    internal static IReadOnlyList<StoryChapterMemory> PendingChapters(StoryMemoryDocument memory)
    {
        if (memory.Chapters.Count is < 1 or > MaxIndexedChapters ||
            // Small completely empty stories should use chronological FULL
            // recovery instead of a limited names-only operation.
            (!StoryMemoryRecovery.HasStructuredMemory(memory) &&
             memory.Chapters.Count <= StoryMemoryRecovery.MaxChaptersPerRecovery) ||
            !StoryMemoryRecovery.HasValidChapterSet(memory))
            return Array.Empty<StoryChapterMemory>();

        // Never make one click fire 500 AI calls. Subsequent clicks pick up
        // the next unscanned chapters, with a durable receipt for each batch.
        return memory.Chapters
            .Where(chapter => chapter.BookkeepingStatus is not ("Reviewed" or "NamesOnly"))
            .OrderBy(chapter => chapter.Number)
            .Take(MaxChaptersPerBatch)
            .ToArray();
    }

    internal static bool CanRepair(StoryMemoryDocument memory) => PendingChapters(memory).Count > 0;

    internal static StoryMemoryUpdate NewNamesOnly(
        StoryMemoryDocument existing, StoryMemoryUpdate extracted, string committedText)
    {
        StoryMemoryUpdateNormalizer.Normalize(extracted);
        var added = new StoryMemoryUpdate();
        var foundCharacters = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var foundLocations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (StoryCharacterPatch entry in extracted.Characters.Take(60))
        {
            string name = entry.Name?.Trim() ?? string.Empty;
            if (!AllowedName(name) || !MentionedInChapter(committedText, name) ||
                !foundCharacters.Add(name)) continue;
            StoryCharacterMemory? matched = StoryCharacterIdentity.ResolveUnique(existing, name, out bool ambiguous);
            if (matched is not null || ambiguous) continue;
            // Deliberately do not copy invented personality, inventory, knowledge,
            // position, condition or aliases from a retrospective AI request.
            added.Characters.Add(new StoryCharacterPatch { Name = name });
            if (added.Characters.Count == 20) break;
        }
        foreach (StoryLocationPatch entry in extracted.Locations.Take(60))
        {
            string name = entry.Name?.Trim() ?? string.Empty;
            if (!AllowedName(name) || !MentionedInChapter(committedText, name) ||
                !foundLocations.Add(name) ||
                existing.Locations.Any(x => string.Equals(x.Name.Trim(), name, StringComparison.OrdinalIgnoreCase)))
                continue;
            // No current occupants/status or location relationship updates.
            added.Locations.Add(new StoryLocationPatch { Name = name });
            if (added.Locations.Count == 20) break;
        }
        return added;
    }

    internal static void ApplySafeNames(
        StoryMemoryDocument candidate, StoryMemoryUpdate additions, StoryChapterMemory chapter)
    {
        // Avoid generic Merger.Apply: it may infer current location availability,
        // character state, or other mutable facts from historical evidence.
        foreach (StoryCharacterPatch character in additions.Characters)
        {
            StoryCharacterMemory? found = StoryCharacterIdentity.ResolveUnique(
                candidate, character.Name, out bool ambiguous);
            if (found is not null || ambiguous) continue;
            candidate.Characters.Add(new StoryCharacterMemory
            {
                Name = character.Name,
                Notes = $"Name appears in committed chapter {chapter.Number}; attributes not verified by repair."
            });
        }
        foreach (StoryLocationPatch location in additions.Locations)
        {
            if (candidate.Locations.Any(x =>
                string.Equals(x.Name.Trim(), location.Name.Trim(), StringComparison.OrdinalIgnoreCase)))
                continue;
            candidate.Locations.Add(new StoryLocationMemory
            {
                Name = location.Name,
                Status = "Unverified",
                LastChapter = chapter.Number,
                Source = $"Chapter {chapter.Number} (name-only repair)"
            });
        }
        candidate.UpdatedUtc = DateTimeOffset.UtcNow;
    }

    private static bool AllowedName(string name) =>
        name.Length is >= 3 and <= 80 &&
        !name.Any(char.IsControl) &&
        name.Any(char.IsLetter);

    internal static bool MentionedInChapter(string text, string name)
    {
        if (string.IsNullOrWhiteSpace(text) || !AllowedName(name)) return false;
        int start = 0;
        while (start < text.Length)
        {
            int pos = text.IndexOf(name, start, StringComparison.OrdinalIgnoreCase);
            if (pos < 0) return false;
            int end = pos + name.Length;
            if ((pos == 0 || !char.IsLetterOrDigit(text[pos - 1])) &&
                (end == text.Length || !char.IsLetterOrDigit(text[end])))
                return true;
            start = pos + 1;
        }
        return false;
    }
}
