using System.IO;
using System.Text.Json;

namespace WorkChatMini;

// Called as part of the existing Story memory regression case; does not add a new
// test case or require an AI provider. Uses the same disposable story fixture.
internal static class StoryBranchSelfTest
{
    public static async Task VerifyAsync(string projectId, StoryMemoryStore memories,
        StoryProjectStore projects)
    {
        var branches = new StoryBranchStore();
        StoryMemoryDocument initial = await memories.LoadAsync(projectId);
        IReadOnlyList<StoryMessage> initialMessages = await projects.LoadMessagesAsync(projectId);
        int baseline = initial.Chapters.Count;
        StoryBranchIndex index = await branches.CreateBranchAsync(projectId, initial, initialMessages, "Alternate B");
        if (index.Branches.Count != 2 || index.ActiveBranchId != index.Branches[0].Id)
            throw new InvalidOperationException("Creating a branch did not preserve the Main branch.");
        string mainId = index.ActiveBranchId;
        string otherId = index.Branches[1].Id;
        await branches.RenameBranchAsync(projectId, otherId, "Alternate B renamed");
        if (branches.Load(projectId)?.Branches[1].Name != "Alternate B renamed")
            throw new InvalidOperationException("Renamed branch metadata did not persist.");

        // Two independently constructed stores can rename the same branch at
        // nearly the same time. Their writes must serialize, never interleave
        // two stale copies of branch-index.json or leave a partially written file.
        await Task.WhenAll(
            branches.RenameBranchAsync(projectId, otherId, "Concurrent rename A"),
            new StoryBranchStore().RenameBranchAsync(projectId, otherId, "Concurrent rename B"));
        string? renamed = branches.Load(projectId)?.Branches.Single(b => b.Id == otherId).Name;
        if (renamed is not ("Concurrent rename A" or "Concurrent rename B"))
            throw new InvalidOperationException("Concurrent branch metadata updates corrupted the branch index.");
        await branches.RenameBranchAsync(projectId, otherId, "Alternate B renamed");

        await AppendAsync(projectId, memories, projects, "MainOnly", initial, initialMessages);
        await branches.SwitchAsync(projectId, otherId, await memories.LoadAsync(projectId),
            await projects.LoadMessagesAsync(projectId));
        await AssertActiveAsync(projectId, branches, memories, projects, otherId,
            baseline, forbidden: "MainOnly");

        StoryMemoryDocument otherMemory = await memories.LoadAsync(projectId);
        IReadOnlyList<StoryMessage> otherMessages = await projects.LoadMessagesAsync(projectId);
        await AppendAsync(projectId, memories, projects, "AlternateBOnly", otherMemory, otherMessages);
        await branches.SwitchAsync(projectId, mainId, await memories.LoadAsync(projectId),
            await projects.LoadMessagesAsync(projectId));
        await AssertActiveAsync(projectId, branches, memories, projects, mainId,
            baseline + 1, required: "MainOnly", forbidden: "AlternateBOnly");
        await branches.SwitchAsync(projectId, otherId, await memories.LoadAsync(projectId),
            await projects.LoadMessagesAsync(projectId));
        await AssertActiveAsync(projectId, branches, memories, projects, otherId,
            baseline + 1, required: "AlternateBOnly", forbidden: "MainOnly");

        // Simulate crash just after the branch-switch intent was persisted but
        // before any chapter/memory/conversation file was replaced.
        StoryBranchIndex pendingIndex = branches.Load(projectId)
            ?? throw new InvalidOperationException("Branch index unexpectedly missing.");
        string savedB = await new StoryCheckpointStore().CaptureAsync(projectId,
            await memories.LoadAsync(projectId), await projects.LoadMessagesAsync(projectId),
            "Self-test crash during branch switch");
        StoryBranchInfo active = pendingIndex.Branches.Single(b => b.Id == otherId);
        active.HeadCheckpointId = savedB;
        string mainHead = pendingIndex.Branches.Single(b => b.Id == mainId).HeadCheckpointId;
        pendingIndex.PendingSwitch = new StoryPendingBranchSwitch
        {
            FromBranchId = otherId, ToBranchId = mainId,
            SourceCheckpointId = savedB, TargetCheckpointId = mainHead
        };
        string indexPath = Path.Combine(projects.GetProjectDirectory(projectId),
            "branches", "branch-index.json");
        await File.WriteAllTextAsync(indexPath, JsonSerializer.Serialize(pendingIndex));
        await branches.RecoverIfNeededAsync(projectId);
        await AssertActiveAsync(projectId, branches, memories, projects, mainId,
            baseline + 1, required: "MainOnly", forbidden: "AlternateBOnly");
        if (branches.Load(projectId)?.PendingSwitch is not null)
            throw new InvalidOperationException("Recovered branch switch left a pending journal.");

        // Reopen the other named head after recovery. It must still be intact.
        await branches.SwitchAsync(projectId, otherId, await memories.LoadAsync(projectId),
            await projects.LoadMessagesAsync(projectId));
        await AssertActiveAsync(projectId, branches, memories, projects, otherId,
            baseline + 1, required: "AlternateBOnly", forbidden: "MainOnly");

        // Preview5: compare without switching or mutating the active branch, then
        // archive an inactive branch without losing its saved memory or checkpoint.
        StoryBranchIndex beforeArchive = branches.Load(projectId)
            ?? throw new InvalidOperationException("Branch index missing before archive test.");
        string originalMainHead = beforeArchive.Branches.Single(b => b.Id == mainId).HeadCheckpointId;
        var snapshots = new StoryCheckpointStore();
        StoryMemoryDocument mainSnapshot = await snapshots.ReadMemorySnapshotAsync(projectId, originalMainHead);
        IReadOnlyList<string> comparison = StoryBranchComparison.Describe(
            await memories.LoadAsync(projectId), mainSnapshot, "Alternate B", "Main");
        if (!comparison.Any(line => line.Contains("MainOnly", StringComparison.Ordinal)) ||
            !comparison.Any(line => line.Contains("AlternateBOnly", StringComparison.Ordinal)))
            throw new InvalidOperationException("Read-only comparison did not distinguish the branch memories.");
        StoryBranchIndex archived = await branches.ArchiveBranchAsync(projectId, mainId);
        if (archived.Branches.Count != 1 || archived.ArchivedBranches.Count != 1 ||
            archived.ActiveBranchId != otherId || archived.ArchivedBranches[0].HeadCheckpointId != originalMainHead)
            throw new InvalidOperationException("Archiving an inactive branch lost its head or active pointer.");
        bool activeRejected = false;
        try { await branches.ArchiveBranchAsync(projectId, otherId); }
        catch (InvalidOperationException ex) when (ex.Message.Contains(
            "archiving the active one", StringComparison.OrdinalIgnoreCase))
        {
            activeRejected = true;
        }
        if (!activeRejected)
            throw new InvalidOperationException("The active branch was not protected from archiving.");
        StoryBranchIndex restored = await branches.RestoreArchivedBranchAsync(projectId, mainId);
        if (restored.ArchivedBranches.Count != 0 || restored.Branches.Count != 2 ||
            restored.Branches.Single(b => b.Id == mainId).HeadCheckpointId != originalMainHead)
            throw new InvalidOperationException("Restoring the archived branch lost its original checkpoint.");
        await branches.SwitchAsync(projectId, mainId, await memories.LoadAsync(projectId),
            await projects.LoadMessagesAsync(projectId));
        await AssertActiveAsync(projectId, branches, memories, projects, mainId,
            baseline + 1, required: "MainOnly", forbidden: "AlternateBOnly");

        // An archived name may be reused. Restoration must resolve the collision
        // while keeping the original archived ID and snapshot intact.
        await branches.ArchiveBranchAsync(projectId, otherId);
        await branches.CreateBranchAsync(projectId, await memories.LoadAsync(projectId),
            await projects.LoadMessagesAsync(projectId), "Alternate B renamed");
        StoryBranchIndex nameConflict = await branches.RestoreArchivedBranchAsync(projectId, otherId);
        StoryBranchInfo recoveredB = nameConflict.Branches.Single(b => b.Id == otherId);
        if (string.Equals(recoveredB.Name, "Alternate B renamed", StringComparison.OrdinalIgnoreCase) ||
            !recoveredB.Name.Contains("restored", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Archived branch name conflict was not resolved.");
        await branches.SwitchAsync(projectId, otherId, await memories.LoadAsync(projectId),
            await projects.LoadMessagesAsync(projectId));
        await AssertActiveAsync(projectId, branches, memories, projects, otherId,
            baseline + 1, required: "AlternateBOnly", forbidden: "MainOnly");
    }

    private static async Task AppendAsync(string projectId, StoryMemoryStore memories,
        StoryProjectStore projects, string marker, StoryMemoryDocument memory,
        IReadOnlyList<StoryMessage> messages)
    {
        memory.Canon.Add(new StoryCanonFact { Fact = marker + " private canon" });
        memory.NarrativePromises.Add(new StoryNarrativePromise
        {
            Title = marker + " private setup", Status = "Open", Evidence = marker
        });
        memory.CausalLinks.Add(new StoryCausalLink
        {
            From = marker + " clue", To = marker + " motive", Relation = "MOTIVATES"
        });
        StoryChapterMemory? chapter = await memories.AppendChapterAsync(projectId, memory,
            marker, marker, marker + " unique chapter content");
        if (chapter is null)
            throw new InvalidOperationException("Branch fixture chapter was not created.");
        await memories.SaveAsync(projectId, memory);
        await projects.SaveMessagesAsync(projectId, messages.Concat(new[]
        {
            new StoryMessage { Role = "user", Action = StoryActionType.Chat.ToString(),
                Content = "Chốt: " + marker + " working direction",
                CreativeDirection = marker + " private direction" },
            new StoryMessage { Role = "assistant", Content = marker + " private conversation",
                IsNarrativeDraft = true, ManuscriptCommitted = true,
                ManuscriptChapterNumber = chapter.Number }
        }).ToList());
    }

    private static async Task AssertActiveAsync(string projectId, StoryBranchStore branches,
        StoryMemoryStore memories, StoryProjectStore projects, string expectedBranch,
        int chapterCount, string required = "", string forbidden = "")
    {
        StoryBranchIndex index = branches.Load(projectId)
            ?? throw new InvalidOperationException("Branch index missing after switching.");
        StoryMemoryDocument memory = await memories.LoadAsync(projectId);
        IReadOnlyList<StoryMessage> messages = await projects.LoadMessagesAsync(projectId);
        if (index.ActiveBranchId != expectedBranch || memory.Chapters.Count != chapterCount)
            throw new InvalidOperationException("Branch switch selected the wrong chapter state.");
        if (required.Length > 0 && (!memory.Canon.Any(c => c.Fact.Contains(required,
            StringComparison.Ordinal)) || !messages.Any(m => m.Content.Contains(required, StringComparison.Ordinal)) ||
            !memory.NarrativePromises.Any(p => p.Title.Contains(required, StringComparison.Ordinal)) ||
            !memory.CausalLinks.Any(link => link.From.Contains(required, StringComparison.Ordinal)) ||
            !StoryCoCreation.BuildContext(messages).Contains(required, StringComparison.Ordinal)))
            throw new InvalidOperationException("Branch switch lost canon, conversation, promise or causality.");
        if (forbidden.Length == 0)
            return;
        if (memory.Canon.Any(c => c.Fact.Contains(forbidden, StringComparison.Ordinal)) ||
            messages.Any(m => m.Content.Contains(forbidden, StringComparison.Ordinal)) ||
            memory.NarrativePromises.Any(p => p.Title.Contains(forbidden, StringComparison.Ordinal)) ||
            memory.CausalLinks.Any(link => link.From.Contains(forbidden, StringComparison.Ordinal)) ||
            StoryCoCreation.BuildContext(messages).Contains(forbidden, StringComparison.Ordinal))
            throw new InvalidOperationException("Inactive branch canon, dialogue, promise or causality leaked.");
        IReadOnlyList<StoryChapterSearchHit> hits = await memories.SearchChapterTextAsync(projectId, forbidden);
        if (hits.Any(h => h.Excerpt.Contains(forbidden, StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Inactive branch prose leaked into active retrieval.");
    }
}
