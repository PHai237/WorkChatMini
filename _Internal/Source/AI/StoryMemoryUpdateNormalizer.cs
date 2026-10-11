namespace WorkChatMini;

public static class StoryMemoryUpdateNormalizer
{
    public static StoryMemoryUpdate Normalize(StoryMemoryUpdate update)
    {
        update.Canon ??= new List<StoryCanonFactPatch>();
        update.Characters ??= new List<StoryCharacterPatch>();
        update.Relationships ??= new List<StoryRelationshipPatch>();
        update.Timeline ??= new List<StoryTimelinePatch>();
        update.OpenThreads ??= new List<StoryThreadPatch>();
        update.ResolvedThreads ??= new List<string>();
        update.Lore ??= new List<StoryLorePatch>();
        update.Events ??= new List<StoryEventPatch>();
        update.Arcs ??= new List<StoryArcPatch>();
        update.CharacterArcs ??= new List<StoryCharacterArcPatch>();
        update.Locations ??= new List<StoryLocationPatch>();
        update.SceneStates ??= new List<StorySceneStatePatch>();
        update.Revisions ??= new List<StoryCanonRevisionPatch>();
        update.NarrativePromises ??= new List<StoryNarrativePromisePatch>();
        update.CausalLinks ??= new List<StoryCausalLinkPatch>();

        foreach (StoryCharacterPatch character in update.Characters)
        {
            character.Aliases ??= new List<string>();
            character.Goals ??= new List<string>();
            character.RemovedGoals ??= new List<string>();
            character.Fears ??= new List<string>();
            character.Secrets ??= new List<string>();
            character.Knowledge ??= new List<string>();
            character.ForgottenKnowledge ??= new List<string>();
            character.Abilities ??= new List<string>();
            character.RemovedAbilities ??= new List<string>();
            character.Inventory ??= new List<string>();
            character.RemovedInventory ??= new List<string>();
        }

        foreach (StoryEventPatch storyEvent in update.Events)
        {
            storyEvent.RelatedCharacters ??= new List<string>();
            storyEvent.Tags ??= new List<string>();
        }

        foreach (StoryArcPatch arc in update.Arcs)
        {
            arc.Goals ??= new List<string>();
            arc.RelatedCharacters ??= new List<string>();
            arc.RelatedThreads ??= new List<string>();
        }

        foreach (StoryLocationPatch location in update.Locations)
        {
            location.Connections ??= new List<string>();
            location.PresentCharacters ??= new List<string>();
            location.RelevantObjects ??= new List<string>();
        }

        foreach (StorySceneStatePatch scene in update.SceneStates)
        {
            scene.PresentCharacters ??= new List<string>();
            scene.DepartedCharacters ??= new List<string>();
        }

        foreach (StoryCanonRevisionPatch revision in update.Revisions)
            revision.RelatedCharacters ??= new List<string>();

        foreach (StoryNarrativePromisePatch promise in update.NarrativePromises)
        {
            promise.RelatedCharacters ??= new List<string>();
            promise.RelatedArcs ??= new List<string>();
            promise.RelatedThreads ??= new List<string>();
        }

        foreach (StoryCausalLinkPatch link in update.CausalLinks)
            link.RelatedCharacters ??= new List<string>();

        return update;
    }
}
