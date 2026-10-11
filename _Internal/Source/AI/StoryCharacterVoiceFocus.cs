namespace WorkChatMini;

// No invented catchphrases or speech traits. Only record-backed current-cast
// personality and character-arc state; the personality is a starting point,
// never permission to reset a character after later development.
internal static class StoryCharacterVoiceFocus
{
    internal static IReadOnlyList<string> Build(StoryMemoryDocument memory)
    {
        int[] chapters = memory.Chapters.Where(c => c.Number > 0)
            .GroupBy(c => c.Number).Where(g => g.Count() == 1)
            .Select(g => g.Key).OrderByDescending(x => x).ToArray();
        if (chapters.Length == 0) return Array.Empty<string>();
        int newest = chapters[0];
        StorySceneStateMemory? scene = memory.SceneStates
            .Where(s => s.ChapterNumber == newest)
            .OrderByDescending(s => s.CreatedUtc).FirstOrDefault();
        if (scene is null) return Array.Empty<string>();

        var names = new[] { scene.PovCharacter }.Concat(scene.PresentCharacters)
            .Where(n => !string.IsNullOrWhiteSpace(n)).ToArray();
        var result = new List<string>(2);
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (string label in names)
        {
            StoryCharacterMemory? character = StoryCharacterIdentity.ResolveUnique(memory, label, out bool ambiguous);
            if (ambiguous || character is null || !used.Add(character.Name)) continue;
            StoryCharacterArcMemory? arc = memory.CharacterArcs
                .Where(a => string.Equals(a.Character, character.Name, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(a.Status, "Active", StringComparison.OrdinalIgnoreCase) &&
                    a.LastChapter is int ch && chapters.Contains(ch))
                .OrderByDescending(a => a.LastChapter).FirstOrDefault();
            if (string.IsNullOrWhiteSpace(character.Personality) && (arc is null ||
                string.IsNullOrWhiteSpace(arc.Stage))) continue;
            string role = string.Equals(label, scene.PovCharacter, StringComparison.OrdinalIgnoreCase)
                ? "POV" : "present";
            string line = $"- {Clip(character.Name, 30)} ({role})";
            if (!string.IsNullOrWhiteSpace(character.Personality))
                line += $" | baseline: {Clip(character.Personality, 62)}";
            if (arc is not null && !string.IsNullOrWhiteSpace(arc.Stage))
                line += $" | arc: {Clip(arc.Stage, 42)}";
            result.Add(line);
            if (result.Count == 2) break;
        }
        return result;
    }

    private static string Clip(string input, int max)
    {
        string value = string.Join(" ", input.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return value.Length <= max ? value : value[..(max - 1)] + "…";
    }
}
