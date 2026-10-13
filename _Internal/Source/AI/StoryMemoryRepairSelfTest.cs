namespace WorkChatMini;

internal static class StoryMemoryRepairSelfTest
{
    internal static void Verify()
    {
        StoryMemoryDocument legacy = StoryMemoryStore.ParseMemoryDocument(
            "{\"chapters\":[{\"number\":1,\"fileName\":\"0001-Old.md\"}],\"characters\":[{\"name\":\"Mai\"}]}");
        if (legacy.Chapters[0].BookkeepingStatus != "Unknown")
            throw new InvalidOperationException("Old chapter data did not deserialize as Unknown bookkeeping.");
        StoryMemoryDocument invalidReceipt = StoryMemoryStore.ParseMemoryDocument(
            "{\"chapters\":[{\"number\":1,\"fileName\":\"0001-Old.md\",\"bookkeepingStatus\":\"FakeComplete\"}]}");
        if (invalidReceipt.Chapters[0].BookkeepingStatus != "Unknown")
            throw new InvalidOperationException("Unrecognized extraction receipt was trusted.");
        var memory = new StoryMemoryDocument();
        memory.Chapters.Add(new StoryChapterMemory { Number = 1, FileName = "0001-Arrival.md", BookkeepingStatus = "Reviewed" });
        memory.Chapters.Add(new StoryChapterMemory { Number = 2, FileName = "0002-Harbor.md", BookkeepingStatus = "Skipped" });
        memory.Characters.Add(new StoryCharacterMemory { Name = "An", CurrentLocation = "Old Harbor", Inventory = { "blue key" } });
        memory.Locations.Add(new StoryLocationMemory { Name = "Old Harbor", Status = "Destroyed" });
        var pending = StoryMemoryRepair.PendingChapters(memory);
        if (!StoryMemoryRepair.BuildCoverageWarning(memory).Contains("NOT proof", StringComparison.Ordinal))
            throw new InvalidOperationException("Partial Memory coverage did not warn the writer about unknown canon.");
        if (pending.Count != 1 || pending[0].Number != 2)
            throw new InvalidOperationException("Partial repair included a previously reviewed chapter.");
        var extracted = new StoryMemoryUpdate();
        extracted.Characters.Add(new StoryCharacterPatch { Name = "An", CurrentLocation = "River", Inventory = { "fake key" } });
        extracted.Characters.Add(new StoryCharacterPatch { Name = "Mai", Condition = "Dead", Knowledge = { "secret" } });
        extracted.Characters.Add(new StoryCharacterPatch { Name = "Ghost", Condition = "Alive" });
        extracted.Locations.Add(new StoryLocationPatch { Name = "Old Harbor", Status = "Active" });
        extracted.Locations.Add(new StoryLocationPatch { Name = "Silent River", PresentCharacters = { "An" } });
        extracted.Canon.Add(new StoryCanonFactPatch { Fact = "An can fly" });
        extracted.SceneStates.Add(new StorySceneStatePatch { Location = "Silent River", Summary = "An moved there", PresentCharacters = { "An" } });
        StoryMemoryUpdate safe = StoryMemoryRepair.NewNamesOnly(memory, extracted,
            "Mai met An beside Silent River. Ghostly shadows filled the sky.");
        if (safe.Characters.Count != 1 || safe.Characters[0].Name != "Mai" ||
            safe.Characters[0].Condition.Length != 0 || safe.Characters[0].Knowledge.Count != 0 ||
            safe.Locations.Count != 1 || safe.Locations[0].Name != "Silent River" ||
            safe.Locations[0].PresentCharacters.Count != 0 || safe.Canon.Count > 0 || safe.SceneStates.Count > 0)
            throw new InvalidOperationException("Partial repair copied unproven state or failed to discover names.");
        StoryMemoryRepair.ApplySafeNames(memory, safe, memory.Chapters[1]);
        if (memory.Characters.First(x => x.Name == "An").CurrentLocation != "Old Harbor" ||
            memory.Characters.First(x => x.Name == "An").Inventory.Single() != "blue key" ||
            memory.Locations.First(x => x.Name == "Old Harbor").Status != "Destroyed" ||
            memory.Locations.First(x => x.Name == "Silent River").Status != "Unverified")
            throw new InvalidOperationException("Partial repair overwrote durable existing state.");
        if (StoryMemoryRepair.MentionedInChapter("Amana", "An") ||
            !StoryMemoryRepair.MentionedInChapter("Hôm nay Mai đến.", "Mai"))
            throw new InvalidOperationException("Unicode/entity boundary matching is unsafe.");
        memory.Chapters[1].BookkeepingStatus = "Reviewed";
        if (StoryMemoryRepair.CanRepair(memory) || StoryMemoryRepair.BuildCoverageWarning(memory).Length != 0)
            throw new InvalidOperationException("Repeated AI requests or false coverage warning for a reviewed chapter.");
        memory.Chapters[1].BookkeepingStatus = "Skipped";
        memory.Chapters[1].FileName = "../unsafe.md";
        if (StoryMemoryRepair.CanRepair(memory))
            throw new InvalidOperationException("Partial repair accepted an unsafe chapter path.");
    }
}
