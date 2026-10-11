namespace WorkChatMini;

public static class StoryWorldStateGraph
{
    private const int MaxSceneSnapshots = 240;

    public static void Merge(
        StoryMemoryDocument memory,
        IEnumerable<StoryLocationPatch> locationPatches,
        IEnumerable<StorySceneStatePatch> scenePatches,
        string sourceLabel,
        StoryChapterMemory? chapter)
    {
        foreach (StoryLocationPatch patch in locationPatches.Where(x => !string.IsNullOrWhiteSpace(x.Name)))
            MergeLocation(memory, patch, sourceLabel, chapter);

        foreach (StorySceneStatePatch patch in scenePatches.Where(IsUsable))
            MergeScene(memory, patch, sourceLabel, chapter);

        if (memory.SceneStates.Count > MaxSceneSnapshots)
            memory.SceneStates.RemoveRange(0, memory.SceneStates.Count - MaxSceneSnapshots);
    }

    public static string FormatLocationForContext(StoryLocationMemory location)
    {
        string people = location.PresentCharacters.Count == 0 ? string.Empty : $" | present: {string.Join(", ", location.PresentCharacters.Take(10))}";
        string objects = location.RelevantObjects.Count == 0 ? string.Empty : $" | objects: {string.Join(", ", location.RelevantObjects.Take(8))}";
        string links = location.Connections.Count == 0 ? string.Empty : $" | connects: {string.Join(", ", location.Connections.Take(8))}";
        string chapter = location.LastChapter is int ch ? $" | last seen Ch.{ch}" : string.Empty;
        return $"[{location.Status}] {location.Name}: {location.Summary}{people}{objects}{links}{chapter}";
    }

    public static string FormatSceneForContext(StorySceneStateMemory scene)
    {
        string chapter = scene.ChapterNumber is int ch ? $"Ch.{ch}" : "Scene";
        string people = scene.PresentCharacters.Count == 0 ? string.Empty : $" | cast: {string.Join(", ", scene.PresentCharacters.Take(10))}";
        string pov = string.IsNullOrWhiteSpace(scene.PovCharacter) ? string.Empty : $" | POV: {scene.PovCharacter}";
        string time = string.IsNullOrWhiteSpace(scene.Time) ? string.Empty : $" | time: {scene.Time}";
        return $"{chapter} {scene.SceneLabel} @ {scene.Location}: {scene.Summary}{people}{pov}{time}";
    }

    public static string BuildLocationSearchText(StoryLocationMemory location) => string.Join(" ", new[]
    {
        location.Name, location.Summary, location.Parent, location.Status, string.Join(" ", location.Connections),
        string.Join(" ", location.PresentCharacters), string.Join(" ", location.RelevantObjects), location.Source
    });

    public static string BuildSceneSearchText(StorySceneStateMemory scene) => string.Join(" ", new[]
    {
        scene.SceneLabel, scene.Location, scene.Time, scene.PovCharacter, string.Join(" ", scene.PresentCharacters),
        string.Join(" ", scene.DepartedCharacters), scene.Summary, scene.ChapterTitle, scene.Source
    });

    private static void MergeLocation(StoryMemoryDocument memory, StoryLocationPatch patch, string sourceLabel, StoryChapterMemory? chapter)
    {
        StoryLocationMemory? location = EnsureLocation(memory, patch.Name);
        location.Summary = Choose(patch.Summary, location.Summary);
        location.Parent = Choose(patch.Parent, location.Parent);
        location.Status = NormalizeStatus(patch.Status, location.Status);
        Union(location.Connections, patch.Connections);
        Union(location.RelevantObjects, patch.RelevantObjects);
        if (patch.PresentCharacters.Count > 0)
        {
            location.PresentCharacters.Clear();
            Union(location.PresentCharacters, patch.PresentCharacters);
        }
        location.LastChapter = chapter?.Number ?? location.LastChapter;
        location.Source = sourceLabel;
        location.UpdatedUtc = DateTimeOffset.UtcNow;
    }

    private static void MergeScene(StoryMemoryDocument memory, StorySceneStatePatch patch, string sourceLabel, StoryChapterMemory? chapter)
    {
        var scene = new StorySceneStateMemory
        {
            ChapterNumber = chapter?.Number,
            ChapterTitle = chapter?.Title ?? string.Empty,
            SceneLabel = Clean(patch.SceneLabel),
            Location = Clean(patch.Location),
            Time = Clean(patch.Time),
            PovCharacter = Clean(patch.PovCharacter),
            PresentCharacters = CleanDistinct(patch.PresentCharacters),
            DepartedCharacters = CleanDistinct(patch.DepartedCharacters),
            Summary = Clean(patch.Summary),
            Source = sourceLabel,
            CreatedUtc = DateTimeOffset.UtcNow
        };

        bool duplicate = memory.SceneStates.Any(x =>
            x.ChapterNumber == scene.ChapterNumber && Same(x.Location, scene.Location) && Similar(x.Summary, scene.Summary));
        if (!duplicate)
            memory.SceneStates.Add(scene);

        StoryLocationMemory location = EnsureLocation(memory, scene.Location);
        location.LastChapter = chapter?.Number ?? location.LastChapter;
        location.Source = sourceLabel;
        foreach (string departed in scene.DepartedCharacters)
            location.PresentCharacters.RemoveAll(x => Same(x, departed));

        foreach (string characterName in scene.PresentCharacters)
        {
            foreach (StoryLocationMemory other in memory.Locations.Where(x => !Same(x.Name, location.Name)))
                other.PresentCharacters.RemoveAll(x => Same(x, characterName));
            AddUnique(location.PresentCharacters, characterName);

            StoryCharacterMemory? character = memory.Characters.FirstOrDefault(x =>
                Same(x.Name, characterName) || x.Aliases.Any(alias => Same(alias, characterName)));
            if (character is null)
            {
                character = new StoryCharacterMemory { Name = characterName };
                memory.Characters.Add(character);
            }
            character.CurrentLocation = scene.Location;
            character.UpdatedUtc = DateTimeOffset.UtcNow;
        }
        location.UpdatedUtc = DateTimeOffset.UtcNow;
    }

    private static StoryLocationMemory EnsureLocation(StoryMemoryDocument memory, string name)
    {
        string clean = Clean(name);
        StoryLocationMemory? location = memory.Locations.FirstOrDefault(x => Same(x.Name, clean));
        if (location is not null)
            return location;
        location = new StoryLocationMemory { Name = clean };
        memory.Locations.Add(location);
        return location;
    }

    private static bool IsUsable(StorySceneStatePatch patch) =>
        !string.IsNullOrWhiteSpace(patch.Location) &&
        (!string.IsNullOrWhiteSpace(patch.Summary) || patch.PresentCharacters.Count > 0);

    private static string NormalizeStatus(string candidate, string fallback)
    {
        if (string.Equals(candidate, "Destroyed", StringComparison.OrdinalIgnoreCase)) return "Destroyed";
        if (string.Equals(candidate, "Inaccessible", StringComparison.OrdinalIgnoreCase)) return "Inaccessible";
        if (string.Equals(candidate, "Active", StringComparison.OrdinalIgnoreCase)) return "Active";
        return string.IsNullOrWhiteSpace(fallback) ? "Active" : fallback;
    }

    private static void Union(List<string> target, IEnumerable<string> values)
    {
        foreach (string value in values.Where(x => !string.IsNullOrWhiteSpace(x)))
            AddUnique(target, value);
    }

    private static void AddUnique(List<string> target, string value)
    {
        string clean = Clean(value);
        if (clean.Length > 0 && !target.Any(x => Same(x, clean)))
            target.Add(clean);
    }

    private static List<string> CleanDistinct(IEnumerable<string> values)
    {
        var result = new List<string>();
        Union(result, values);
        return result;
    }

    private static string Choose(string candidate, string fallback) =>
        string.IsNullOrWhiteSpace(candidate) ? fallback : candidate.Trim();

    private static string Clean(string? value) => value?.Trim() ?? string.Empty;

    private static bool Same(string left, string right) =>
        string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static bool Similar(string left, string right)
    {
        if (Same(left, right)) return true;
        if (string.IsNullOrWhiteSpace(left) || string.IsNullOrWhiteSpace(right) || left.Length < 12 || right.Length < 12)
            return false;
        return left.Contains(right, StringComparison.OrdinalIgnoreCase) || right.Contains(left, StringComparison.OrdinalIgnoreCase);
    }
}
