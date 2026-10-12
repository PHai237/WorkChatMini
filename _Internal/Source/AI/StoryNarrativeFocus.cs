using System.Text;

namespace WorkChatMini;

public static class StoryNarrativeFocus
{
    // This entire block has to fit the *actual* writer-context allowance (2400).
    // An unbounded character summary must never hide the primary arc, open
    // thread or a setup/payoff just because it was appended first.
    private const int MaxNarrativeCharacters = 2380;

    public static string Build(StoryMemoryDocument memory)
    {
        int currentChapter = memory.Chapters.Count == 0 ? 0 : memory.Chapters.Max(x => x.Number);
        var activeArcs = memory.Arcs
            .Where(x => string.Equals(x.Status, "Active", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => ArcScore(x, currentChapter)).Take(3).ToArray();
        var openThreads = memory.Threads
            .Where(x => string.Equals(x.Status, "Open", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => ThreadScore(x, currentChapter)).Take(3).ToArray();
        var promises = memory.NarrativePromises.Where(StoryCausalMemoryLedger.IsActive)
            .Where(x => x.IntroducedChapter is int n && memory.Chapters.Any(c => c.Number == n))
            .OrderByDescending(x => PromiseScore(x, currentChapter)).Take(3).ToArray();
        var characterArcs = memory.CharacterArcs
            .Where(x => string.Equals(x.Status, "Active", StringComparison.OrdinalIgnoreCase))
            .OrderByDescending(x => CharacterArcScore(x, currentChapter)).Take(2).ToArray();
        string decisionFocus = StoryCharacterDecisionFocus.Build(memory);
        string motivationFocus = StoryCharacterMotivationFocus.Build(memory);
        string priorities = StoryCharacterPriorityFocus.Build(memory);
        IReadOnlyList<string> recentBeats = StoryNarrativeCraftGuide.RecentBeatEvidence(memory);
        IReadOnlyList<string> recentConsequences = StoryRecentConsequenceFocus.Build(memory);
        IReadOnlyList<string> relationshipHandoff = StoryRelationshipContinuityFocus.Build(memory);
        IReadOnlyList<string> patternWatch = StoryPlotPatternFocus.Build(memory);
        IReadOnlyList<string> voiceAnchors = StoryCharacterVoiceFocus.Build(memory);
        if (activeArcs.Length == 0 && openThreads.Length == 0 && promises.Length == 0 &&
            characterArcs.Length == 0 && string.IsNullOrEmpty(decisionFocus) &&
            string.IsNullOrEmpty(motivationFocus) && string.IsNullOrEmpty(priorities) &&
            memory.SceneStates.Count == 0 && recentBeats.Count == 0 && recentConsequences.Count == 0 &&
            relationshipHandoff.Count == 0 &&
            patternWatch.Count == 0 && voiceAnchors.Count == 0)
            return string.Empty;

        var output = new StringBuilder(MaxNarrativeCharacters);
        AddLine("NARRATIVE FOCUS — guidance, NOT canon or a required plot:");
        AddLine("- Keep a primary arc and at most one supporting thread in focus. Stale setups are not deadlines; never force a payoff merely because it is old.");
        AddLine("- Past choices, trust and promises matter, but an earned change, relapse or surprise is allowed. Do not infer causality or character knowledge from proximity.");

        // A duplicate chapter number cannot establish an unambiguous
        // spatial handoff for Continue Story.
        var uniqueCommittedChapters = memory.Chapters.Where(c => c.Number > 0)
            .GroupBy(c => c.Number).Where(g => g.Count() == 1)
            .Select(g => g.Key).ToHashSet();
        StorySceneStateMemory? latest = memory.SceneStates
            .Where(x => x.ChapterNumber is int chapter && uniqueCommittedChapters.Contains(chapter))
            .OrderByDescending(x => x.ChapterNumber).ThenByDescending(x => x.CreatedUtc)
            .FirstOrDefault();
        if (latest is not null)
            AddSection("LATEST COMMITTED SPATIAL STATE",
                new[] { "- " + Trim(StoryWorldStateGraph.FormatSceneForContext(latest), 190) }, 1, 230);
        AddSection("ACTIVE ARC ATTENTION", activeArcs.Select(x =>
            $"- [{NormalizePriority(x.Priority)}/{NormalizeMomentum(x.Momentum)}] {Trim(x.Title, 54)}: {Trim(x.Progress, 112)}"), 2, 340);
        AddSection("OPEN THREAD ATTENTION", openThreads.Select(x =>
            $"- [{NormalizeImportance(x.Importance)}] {Trim(x.Title, 48)}: {Trim(x.Detail, 110)}"), 2, 255);
        AddSection("SETUP / PAYOFF ATTENTION", promises.Select(x =>
            $"- [{NormalizeImportance(x.Importance)}/{x.Status}] {Trim(x.Title, 46)}: {Trim(x.Detail, 95)}"), 2, 280);
        AddSection("CHARACTER ARC ATTENTION", characterArcs.Select(x =>
            $"- [{NormalizeImportance(x.Importance)}/{StoryCharacterArcLedger.NormalizeDirection(x.Direction)}] {Trim(x.Character, 35)} — {Trim(x.Title, 50)}: {Trim(x.Progress, 100)}"), 2, 260);
        // Confirmed, chapter-backed consequences belong near the main plot focus;
        // they are historical outcomes, NOT mandatory new plot beats.
        AddSection("RECENT CONSEQUENCES — historical; no forced callback", recentConsequences, 2, 255);
        // Quotas give each independent kind of evidence a fair chance to appear.
        // Do not let one prolific character consume the entire context budget.
        AddExcerpt(priorities, 420, 2);
        AddExcerpt(decisionFocus, 380, 2);
        AddExcerpt(motivationFocus, 380, 2);
        // Only currently present/POV characters are eligible; these are voice
        // anchors, not dialogue templates or permission to reset an arc.
        AddSection("LAST COMMITTED CAST — VOICE ANCHORS, not dialogue templates",
            voiceAnchors, 2, 350);
        // Relational evidence is subordinate to committed spatial state.
        AddSection("RECENT RELATIONSHIP TURN — chapter-backed; current canon wins",
            relationshipHandoff, 2, 335);
        // Lower-priority craft evidence must never evict confirmed character,
        // arc, thread or spatial state from the fixed narrative budget.
        // Chapter summaries are historical: current canon/retcons always win.
        AddSection("RECENT COMMITTED BEATS — historical; canon wins", recentBeats, 2, 295);
        // Low-priority, optional craft signal: never displace canon/scene state.
        AddSection("REPEATED PLOT SHAPE — optional variation, not a rule",
            patternWatch, 1, 260);
        return output.ToString().TrimEnd();

        void AddLine(string line)
        {
            if (output.Length + line.Length + Environment.NewLine.Length <= MaxNarrativeCharacters)
                output.AppendLine(line);
        }
        void AddSection(string header, IEnumerable<string> source, int maxLines, int quota)
        {
            string[] lines = source.Take(maxLines).ToArray();
            if (lines.Length == 0) return;
            int sectionLength = header.Length + Environment.NewLine.Length;
            var kept = new List<string>();
            foreach (string line in lines)
            {
                if (sectionLength + line.Length + Environment.NewLine.Length > quota) break;
                kept.Add(line);
                sectionLength += line.Length + Environment.NewLine.Length;
            }
            if (kept.Count == 0 || output.Length + sectionLength > MaxNarrativeCharacters)
                return;
            AddLine(header);
            foreach (string line in kept) AddLine(line);
        }
        void AddExcerpt(string block, int quota, int maxEvidenceLines)
        {
            if (string.IsNullOrWhiteSpace(block)) return;
            string[] lines = block.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length < 2) return;
            // First line is an actual label; after that prefer evidence lines
            // over generic commentary so a budget does not hide provenance.
            var evidence = lines.Skip(1).Where(x => x.TrimStart().StartsWith("- ", StringComparison.Ordinal))
                .Where(x => !x.Contains("not instructions", StringComparison.OrdinalIgnoreCase) &&
                            !x.Contains("not a required", StringComparison.OrdinalIgnoreCase) &&
                            !x.Contains("are present state", StringComparison.OrdinalIgnoreCase) &&
                            !x.Contains("Respect lasting", StringComparison.OrdinalIgnoreCase) &&
                            !x.Contains("Preserve earned", StringComparison.OrdinalIgnoreCase))
                .Take(maxEvidenceLines).ToArray();
            if (evidence.Length == 0) return;
            AddSection(lines[0].TrimEnd('\r'), evidence.Select(x => x.TrimEnd('\r')), maxEvidenceLines, quota);
        }
    }

    private static int PromiseScore(StoryNarrativePromise promise, int currentChapter)
    {
        int score = NormalizeImportance(promise.Importance) switch { "Critical" => 52, "Minor" => 12, _ => 30 };
        score += promise.Status == "Developing" ? 7 : 3;
        score += promise.Horizon switch { "Short" => 6, "Long" => 1, _ => 3 };
        int age = Age(currentChapter, promise.LastTouchedChapter ?? promise.IntroducedChapter);
        score += Math.Min(age * 2, 16);
        return score;
    }

    private static int CharacterArcScore(StoryCharacterArcMemory arc, int currentChapter)
    {
        int score = NormalizeImportance(arc.Importance) switch { "Critical" => 45, "Minor" => 12, _ => 28 };
        score += StoryCharacterArcLedger.NormalizeDirection(arc.Direction) switch
        {
            "Transforming" => 10,
            "Fracturing" => 9,
            "Growing" => 7,
            "Regressing" => 7,
            _ => 3
        };
        int age = Age(currentChapter, arc.LastChapter ?? arc.StartedChapter);
        score += Math.Min(age * 2, 14);
        return score;
    }

    private static int ArcScore(StoryArcMemory arc, int currentChapter)
    {
        int score = NormalizePriority(arc.Priority) switch { "Core" => 50, "Minor" => 15, _ => 30 };
        score += NormalizeMomentum(arc.Momentum) switch { "Rising" => 8, "Cooling" => 5, _ => 3 };
        int age = Age(currentChapter, arc.LastChapter ?? arc.StartedChapter);
        score += Math.Min(age * 3, 18);
        return score;
    }

    private static int ThreadScore(StoryPlotThread thread, int currentChapter)
    {
        int score = NormalizeImportance(thread.Importance) switch { "Critical" => 45, "Minor" => 10, _ => 25 };
        int age = Age(currentChapter, thread.LastTouchedChapter ?? thread.OpenedChapter);
        score += Math.Min(age * 2, 14);
        return score;
    }

    private static int Age(int currentChapter, int? touchedChapter) =>
        currentChapter <= 0 || touchedChapter is null ? 0 : Math.Max(0, currentChapter - touchedChapter.Value);

    public static string NormalizePriority(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "core" => "Core",
        "minor" => "Minor",
        _ => "Major"
    };

    public static string NormalizeMomentum(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "rising" => "Rising",
        "cooling" => "Cooling",
        _ => "Steady"
    };

    public static string NormalizeImportance(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        "critical" => "Critical",
        "minor" => "Minor",
        _ => "Major"
    };

    private static string Trim(string value, int max) =>
        string.IsNullOrWhiteSpace(value) ? "No durable progress recorded yet." : value.Length <= max ? value : value[..max] + "…";
}
