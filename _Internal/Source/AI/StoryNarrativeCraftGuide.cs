using System.Text;

namespace WorkChatMini;

// Guidance for the EXISTING preparation / draft / editor calls only. This
// never creates canon, adds a network stage, or changes the user's genres.
internal static class StoryNarrativeCraftGuide
{
    // Only no-direction, everyday continuation requests can skip the
    // REAL-WORLD research gate. Any subject, location, period or factual
    // constraint in the creator's message still takes the normal route.
    internal static bool IsRoutineContinuation(string? creatorDirection)
    {
        string normalized = (creatorDirection ?? string.Empty).Trim().TrimEnd('.', '!', '?', '…').Trim().ToLowerInvariant();
        return normalized is "" or "tiếp" or "tiếp đi" or "tiếp tục" or
            "viết tiếp" or "kể tiếp" or "tự biên đi" or "continue" or
            "continue story" or "continue the story" or "keep going" or "go on";
    }

    internal static string ForPreparation => """

NARRATIVE CRAFT (Continue Story only; private choices, not a checklist to print):
- Choose the next meaningful change from committed consequences and active character motives. Make the scene move at least one situation or relationship, without requiring a fight, revelation, or cliffhanger.
- Before adding a complication, identify the prior cause, which character chooses to act, the credible cost and what changes afterward. No cause or motivation: do not manufacture the event.
- Avoid unearned "freestyle" detours: a new character, organization, mystery or power must serve an existing goal, relationship, setup or the creator's explicit request. Novelty alone is not progress.
- If the last chapter ended with a decision, injury, reveal, confrontation or emotional turn, consider its immediate aftermath before escalating again; choose stillness when the scene needs it.
- Keep the planned number of substantial twists small. If an arc is already active, advance it instead of opening unrelated subplots.
- Think about contrast with RECENT COMMITTED BEATS if present: tension can rise, settle, or transform. A quiet chapter may still advance the story. Do not alternate moods on a fixed schedule.
- Give characters credible agency: their choices should emerge from established motives, obligations, fears and imperfect information. An earned reversal is allowed; an unexplained reset is not.
- Select at most one main dramatic question and a supporting thread when useful. Preserve distant mysteries and setup/payoff timing; do not resolve an old setup solely because it is old.
- In the private plan, consider POV knowledge, the emotional turn, what stays unsaid in dialogue, and one grounded consequence that carries forward. This is guidance, not a demand for all beats in every chapter.
- RECENT CONSEQUENCES are verified past outcomes, not suggested twists or scene requirements. Give immediate consequences room when relevant; do not manufacture a callback to each one.
- Pick a natural stopping point: aftermath, decision, changed relationship, or an earned unresolved question. A surprise ending is optional, not the default.
- If REPEATED PLOT SHAPE appears, treat it as historical craft feedback, not a ban on revelations, losses or conflict; repetition might be intended. Do not announce the feedback in prose.
- If LAST COMMITTED CAST appear, interpret Personality as baseline, tempered by later committed turning points. They do not authorize new catchphrases or invented backstory.
- Scene diversity can come from changed stakes, viewpoint, emotional cost, setting or consequence rather than a forced new twist. Never invent an event to satisfy a diversity metric.
- Current creator direction and genre/tone tags override stylistic defaults when consistent with confirmed canon.
""";

    internal static string ForDraft => """

CONTINUE STORY — PROSE CRAFT:
- Dramatize the scene through specific actions, sensory detail when useful, and character choices. Let important relationships show in behavior, not recap speeches.
- Keep dialogue distinct by speaker, intention, subtext, and social context. Avoid characters explaining facts both already know for the reader's benefit.
- Vary sentence rhythm and scene intensity with purpose; do not fill every passage with threat, exposition or artificial twists. Quiet progress counts.
- Earn turns and consequences on-page. Keep mysteries alive without endlessly repeating clues or abruptly paying all of them off.
- Favor a satisfying local movement over a formulaic cliffhanger, and never add canon-shifting twists just to make an ending loud.
- Let actions have observable consequences, including silence, hesitation or a cost, without turning every chapter into a crisis. Preserve what happened in the previous committed scene without recapping it at length.
- Do not recycle the same reveal → argument → cliffhanger structure merely to imitate a previous chapter. If a similar event is earned, make the new choice and consequences genuinely different.
- Give speakers distinct intentions and knowledge, not caricatured speech tics. Silence, a withheld answer or a small concrete action may carry a turning point when earned.
- Respect current creator instruction, project genres/tags, established POV knowledge and the private continuity constraints.
""";

    internal static string ForEditor => """

CONTINUE STORY — EDITOR CHECK (use the existing edit pass only):
- Smooth uneven pacing without erasing a deliberate slow scene, suspense or the creator's genre/tone.
- Remove repeated recap, redundant internal monologue, generic dialogue and speeches that merely explain known lore. Preserve important clues, intent and character voice.
- Check that each consequential choice belongs to a character with a plausible motive and knowledge at that moment; do not manufacture motivations as established canon.
- Preserve distinct voices and earned changes in relationships. Prefer subtext to exposition where clear, but never make essential story facts incomprehensible.
- Keep the scene's actual outcome, unresolved promises and long-term arcs intact. Do not insert a mandatory hook or surprise ending.
- Preserve the draft's on-page decisions, revelations, injuries, relationship state and ending outcome: edit the *expression* of events rather than adding, removing, or silently changing those events.
- Check whether a new twist has a believable on-page trigger, an active character motive and a consequence; remove arbitrary embellishment without changing the draft's settled outcome.
- Remove repetitive scene architecture only where this can be achieved WITHOUT rewriting established plot facts; do not force variety as a new plot point.
- Do not invent new backstory, revelations, injuries or relationship changes during polish. If the draft's quiet ending is earned, keep it quiet.
""";

    // Local author-facing evidence, not a pacing score or a prediction. Only
    // summaries of chapters listed in the CURRENT active branch are eligible.
    internal static IReadOnlyList<string> RecentBeatEvidence(StoryMemoryDocument memory)
    {
        var lines = new List<string>();
        foreach (StoryChapterMemory chapter in memory.Chapters
                     .Where(c => c.Number > 0 && !string.IsNullOrWhiteSpace(c.Summary))
                     .GroupBy(c => c.Number).Where(g => g.Count() == 1)
                     .Select(g => g.Single())
                     .OrderByDescending(c => c.Number).Take(2))
        {
            string summary = Normalize(chapter.Summary, 105);
            if (summary.Length > 0)
                lines.Add($"- Ch.{chapter.Number}: {summary}");
        }
        return lines;
    }

    private static string Normalize(string text, int limit)
    {
        string collapsed = string.Join(" ", text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return collapsed.Length <= limit ? collapsed : collapsed[..(limit - 1)] + "…";
    }
}

// Very narrow, opt-in advisory for the ALREADY-EXISTING editor pass. Never
// rewrites prose or labels an artistic choice as a continuity violation.
internal static class StoryProseRepetitionHints
{
    internal static string Analyze(string? draft)
    {
        if (string.IsNullOrWhiteSpace(draft) || draft.Length < 240)
            return string.Empty;

        // Match the editor's existing 60k draft input cap, so diagnostics do
        // not keep an unexpectedly huge candidate in temporary allocations.
        string sample = draft.Length <= 60000 ? draft : draft[..60000];
        var paragraphs = sample.Split(new[] { "\r\n\r\n", "\n\n" }, StringSplitOptions.RemoveEmptyEntries)
            .Select(p => string.Join(" ", p.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)))
            .Where(p => p.Length >= 50).ToArray();
        var hints = new List<string>();
        // Identical spoken lines are a much stronger diagnostic signal than
        // subjective estimates of dialogue quality. A deliberate refrain can
        // still be retained by the writer.
        var spoken = sample.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Trim())
            .Where(line => line.Length >= 18 &&
                (line.StartsWith('“') || line.StartsWith('"') || line.StartsWith('—')))
            .ToArray();
        if (spoken.GroupBy(line => line, StringComparer.OrdinalIgnoreCase)
                .Any(group => group.Count() >= 3))
            hints.Add("The same dialogue line appears at least three times. Check whether it is a deliberate refrain or a repeated draft artifact.");
        var repeatedOpenings = paragraphs.GroupBy(p => Opening(p), StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Key.Length >= 20 && g.Count() >= 3).ToArray();
        if (repeatedOpenings.Length > 0)
            hints.Add("Several paragraphs begin with nearly identical wording. If unintentional, vary the entrances while preserving deliberate refrains.");

        // Exact duplicates are strong evidence; fuzzy stylistic judgments are not.
        if (paragraphs.GroupBy(p => p, StringComparer.OrdinalIgnoreCase)
                .Any(g => g.Count() > 1))
            hints.Add("A full paragraph repeats verbatim. Check whether the repetition is deliberate before keeping it.");
        if (RepeatedLongSentence(sample))
            hints.Add("A long sentence repeats at least three times inside the draft. Check for accidental restatement; retain an intentional refrain.");
        if (RepeatedParagraphEndings(paragraphs))
            hints.Add("Three substantial paragraphs end with the same six words. Check for a copy-pattern; an intentional refrain is fine.");
        if (hints.Count == 0) return string.Empty;
        var sb = new StringBuilder("OPTIONAL LOCAL PROSE DIAGNOSTICS — editorial suggestions, not canon:\n");
        foreach (string hint in hints) sb.Append("- ").AppendLine(hint);
        return sb.ToString();
    }

    private static bool RepeatedLongSentence(string sample)
    {
        // Narrow signal: exact, lengthy sentences repeated three times.
        // Split without heavy NLP or a network call; never change the draft.
        var occurrences = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        int start = 0;
        for (int i = 0; i < sample.Length; i++)
        {
            if (sample[i] is not ('.' or '!' or '?' or '…') ||
                (i + 1 < sample.Length && !char.IsWhiteSpace(sample[i + 1])))
                continue;
            string sentence = string.Join(" ", sample[start..(i + 1)].Split(
                (char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            start = i + 1;
            if (sentence.Length < 90 || sentence.Length > 600) continue;
            if (occurrences.TryGetValue(sentence, out int count))
            {
                if (count >= 2) return true;
                occurrences[sentence] = count + 1;
            }
            else
                occurrences[sentence] = 1;
        }
        return false;
    }

    private static bool RepeatedParagraphEndings(IReadOnlyList<string> paragraphs)
    {
        // High bar: substantial paragraphs and an identical SIX-word ending.
        // This is only editorial advice; dialogue repetitions and short poetic
        // refrains should not be misclassified as whole-paragraph copy patterns.
        var endings = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (string paragraph in paragraphs)
        {
            if (paragraph.Length < 120) continue;
            string[] words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (words.Length < 20) continue;
            string ending = string.Join(" ", words.TakeLast(6)).TrimEnd('.', '!', '?', '…', '\"', '”');
            if (ending.Length < 28) continue;
            endings.TryGetValue(ending, out int previous);
            if (previous >= 2) return true;
            endings[ending] = previous + 1;
        }
        return false;
    }

    private static string Opening(string paragraph)
    {
        string[] words = paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return words.Length >= 6 ? string.Join(" ", words.Take(6)).TrimEnd('.', ',', ';', ':', '!', '?') : string.Empty;
    }
}
