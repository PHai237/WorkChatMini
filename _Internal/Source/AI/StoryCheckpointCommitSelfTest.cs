using System.IO;
using System.Text.Json;

namespace WorkChatMini;

// Exercised by the existing Story memory persistence self-test case. No extra WebViews,
// artificial process kills or external AI calls are required.
internal static class StoryCheckpointCommitSelfTest
{
    public static async Task VerifyAsync(string projectId, StoryCheckpointStore checkpoints,
        StoryMemoryStore memories, StoryProjectStore projects)
    {
        StoryTransactionGuardSelfTest.Verify();
        await StoryOperationGateSelfTest.VerifyAsync();
        await StoryConversationWriteOrderSelfTest.VerifyAsync();
        StoryMemoryDocument beforeMemory = await memories.LoadAsync(projectId);
        IReadOnlyList<StoryMessage> beforeMessages = await projects.LoadMessagesAsync(projectId);
        int chaptersBefore = beforeMemory.Chapters.Count;
        string checkpoint = await checkpoints.CaptureAsync(projectId, beforeMemory,
            beforeMessages, "Self-test before interrupted chapter commit");

        // Simulate termination after the new prose and both metadata files were written,
        // but BEFORE the committed marker was saved.
        await checkpoints.BeginCommitAsync(projectId, checkpoint);
        string pendingJournal = Path.Combine(projects.GetProjectDirectory(projectId),
            "branches", "pending-commit.json");
        StoryPendingCommit pendingBeforeCrash = JsonSerializer.Deserialize<StoryPendingCommit>(
            await File.ReadAllTextAsync(pendingJournal))
            ?? throw new InvalidDataException("Pending commit journal missing in fixture.");
        // This note was never referenced by Memory and so could not be captured
        // by a normal checkpoint. Recovery must not silently discard it.
        string orphanName = "selftest-unsnapshotted-note.txt";
        await File.WriteAllTextAsync(Path.Combine(memories.GetChaptersDirectory(projectId), orphanName),
            "Draft note written after the checkpoint.");
        beforeMemory.Canon.Add(new StoryCanonFact { Fact = "CrashOnly world state must not survive." });
        StoryChapterMemory? abandoned = await memories.AppendChapterAsync(projectId, beforeMemory,
            "Abandoned transaction", "CrashOnly", "CrashOnly chapter leaked after interruption.");
        if (abandoned is null)
            throw new InvalidOperationException("Interrupted commit test chapter was not created.");
        await memories.SaveAsync(projectId, beforeMemory);
        var brokenMessages = beforeMessages.ToList();
        brokenMessages.Add(new StoryMessage
        {
            Role = "assistant", Content = "CrashOnly broken commit", Action = "DraftScene",
            IsNarrativeDraft = true, ManuscriptCommitted = true,
            ManuscriptChapterNumber = abandoned.Number
        });
        await projects.SaveMessagesAsync(projectId, brokenMessages);
        await checkpoints.RecoverIfNeededAsync(projectId);

        string recoveredNote = Path.Combine(projects.GetProjectDirectory(projectId),
            "branches", "recovered", pendingBeforeCrash.TransactionId, orphanName);
        if (!File.Exists(recoveredNote) || await File.ReadAllTextAsync(recoveredNote) !=
            "Draft note written after the checkpoint.")
            throw new InvalidOperationException("Interrupted commit did not preserve the unsnapshotted note under its transaction ID.");
        if (checkpoints.HasPendingTransaction(projectId) ||
            File.Exists(Path.Combine(memories.GetChaptersDirectory(projectId), orphanName)))
            throw new InvalidOperationException("Interrupted commit left a pending journal or duplicate unsnapshotted note.");
        // Recovery may be invoked again after a restart. The already rescued
        // note must remain intact without performing a second rollback.
        await checkpoints.RecoverIfNeededAsync(projectId);
        if (await File.ReadAllTextAsync(recoveredNote) != "Draft note written after the checkpoint.")
            throw new InvalidOperationException("Repeated recovery changed a rescued manuscript note.");
        StoryMemoryDocument recovered = await memories.LoadAsync(projectId);
        IReadOnlyList<StoryMessage> recoveredMessages = await projects.LoadMessagesAsync(projectId);
        if (recovered.Chapters.Count != chaptersBefore ||
            recovered.Canon.Any(c => c.Fact.Contains("CrashOnly", StringComparison.Ordinal)) ||
            recoveredMessages.Count != beforeMessages.Count ||
            File.Exists(Path.Combine(memories.GetChaptersDirectory(projectId), abandoned.FileName)))
            throw new InvalidOperationException("Interrupted commit leaked manuscript, canon or conversation.");
        IReadOnlyList<StoryChapterSearchHit> hits = await memories.SearchChapterTextAsync(projectId, "CrashOnly");
        if (hits.Any(h => h.Excerpt.Contains("CrashOnly", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Interrupted commit leaked abandoned prose into retrieval.");

        // Simulate upgrading an older preview after a crash in the narrow
        // window between publishing pending-restore.json and Aborting=true.
        // Such a journal used an independently generated restore transaction ID.
        // The pair must be resumed as ONE abort, without losing untracked data.
        string legacyCheckpoint = await checkpoints.CaptureAsync(projectId, recovered,
            recoveredMessages, "Self-test legacy interrupted abort journal");
        await checkpoints.BeginCommitAsync(projectId, legacyCheckpoint);
        string legacyRestoreId = Guid.NewGuid().ToString("N");
        string legacyNoteName = "legacy-abort-unsnapshotted.txt";
        await File.WriteAllTextAsync(Path.Combine(memories.GetChaptersDirectory(projectId), legacyNoteName),
            "Legacy recovery note survived.");
        string legacyRestoreJournal = Path.Combine(projects.GetProjectDirectory(projectId),
            "branches", "pending-restore.json");
        await File.WriteAllTextAsync(legacyRestoreJournal, JsonSerializer.Serialize(new StoryPendingRestore
        {
            TransactionId = legacyRestoreId,
            TargetCheckpointId = legacyCheckpoint,
            SafetyCheckpointId = legacyCheckpoint
        }));
        await checkpoints.RecoverIfNeededAsync(projectId);
        string legacyRescue = Path.Combine(projects.GetProjectDirectory(projectId),
            "branches", "recovered", legacyRestoreId, legacyNoteName);
        if (!File.Exists(legacyRescue) ||
            await File.ReadAllTextAsync(legacyRescue) != "Legacy recovery note survived." ||
            checkpoints.HasPendingTransaction(projectId) ||
            (await memories.LoadAsync(projectId)).Chapters.Count != chaptersBefore ||
            (await projects.LoadMessagesAsync(projectId)).Count != beforeMessages.Count)
            throw new InvalidOperationException("Legacy interrupted abort was not recovered exactly once.");
        await checkpoints.RecoverIfNeededAsync(projectId);
        if (!File.Exists(legacyRescue) ||
            await File.ReadAllTextAsync(legacyRescue) != "Legacy recovery note survived.")
            throw new InvalidOperationException("Legacy recovered note was changed on second activation.");

        // Candidate Memory must be detached deeply: failed bookkeeping saves
        // may not leave ghost facts/chapters in the active UI's in-memory graph.
        StoryMemoryDocument activeBeforeClone = await memories.LoadAsync(projectId);
        StoryMemoryDocument candidate = await StoryMemoryStore.CloneForCommitAsync(activeBeforeClone);
        candidate.Canon.Add(new StoryCanonFact { Fact = "CandidateOnly must stay uncommitted." });
        candidate.Chapters.Add(new StoryChapterMemory
        {
            Number = 98765, FileName = "candidate-only.md", Summary = "Never committed"
        });
        if (activeBeforeClone.Canon.Any(f => f.Fact.Contains("CandidateOnly", StringComparison.Ordinal)) ||
            activeBeforeClone.Chapters.Any(c => c.Number == 98765))
            throw new InvalidOperationException("Bookkeeping candidate mutated active Story Memory.");
        if (activeBeforeClone.Chapters.Count > 0)
        {
            string oldSummary = activeBeforeClone.Chapters[0].Summary;
            candidate.Chapters[0].Summary = "CandidateOnly changed nested chapter metadata";
            if (activeBeforeClone.Chapters[0].Summary != oldSummary)
                throw new InvalidOperationException("Bookkeeping candidate shares mutable chapter objects.");
        }

        // A completed chapter is not rolled back on the next application launch.
        string completedBase = await checkpoints.CaptureAsync(projectId, recovered,
            recoveredMessages, "Self-test before completed chapter commit");
        await checkpoints.BeginCommitAsync(projectId, completedBase);
        StoryChapterMemory? completeChapter = await memories.AppendChapterAsync(projectId, recovered,
            "Durable transaction", "DurableOnly", "DurableOnly confirmed narrative.");
        if (completeChapter is null)
            throw new InvalidOperationException("Completed commit test chapter was not created.");
        completeChapter.CheckpointId = completedBase;
        await memories.SaveAsync(projectId, recovered);
        var completeMessages = recoveredMessages.ToList();
        completeMessages.Add(new StoryMessage
        {
            Role = "assistant", Content = "DurableOnly confirmed narrative.", Action = "DraftScene",
            IsNarrativeDraft = true, ManuscriptCommitted = true,
            ManuscriptChapterNumber = completeChapter.Number
        });
        await projects.SaveMessagesAsync(projectId, completeMessages);
        await checkpoints.CompleteCommitAsync(projectId);
        // Simulate a crash after the committed marker was written but before
        // its journal could be removed. Recovery must NOT undo a finished commit.
        string journal = Path.Combine(projects.GetProjectDirectory(projectId), "branches", "pending-commit.json");
        await File.WriteAllTextAsync(journal, JsonSerializer.Serialize(new StoryPendingCommit
        {
            CheckpointId = completedBase,
            TransactionId = Guid.NewGuid().ToString("N"),
            Committed = true
        }));
        await checkpoints.RecoverIfNeededAsync(projectId);
        if ((await memories.LoadAsync(projectId)).Chapters.Count != chaptersBefore + 1 ||
            (await projects.LoadMessagesAsync(projectId)).Count != beforeMessages.Count + 1 ||
            !File.Exists(Path.Combine(memories.GetChaptersDirectory(projectId), completeChapter.FileName)))
            throw new InvalidOperationException("A successfully committed chapter was rolled back.");

        if (File.Exists(journal))
            throw new InvalidOperationException("Commit journal was not cleared after recovery.");

        // Simulate a crash AFTER the active Story was safely published but
        // BEFORE original chapter archive cleanup. Only cleanup should resume;
        // active canon/conversation/chapter must not be restored/rewound again.
        string rescueId = Guid.NewGuid().ToString("N");
        string branches = Path.Combine(projects.GetProjectDirectory(projectId), "branches");
        string oldChapters = Path.Combine(branches, "history", rescueId + "-previous-chapters");
        Directory.CreateDirectory(oldChapters);
        await File.WriteAllTextAsync(Path.Combine(oldChapters, "unsnapshotted-note.txt"),
            "Original unpublished note survived a post-restore crash.");
        string markerDirectory = Path.Combine(branches, "pending-rescues");
        Directory.CreateDirectory(markerDirectory);
        string marker = Path.Combine(markerDirectory, rescueId + ".json");
        await File.WriteAllTextAsync(marker, JsonSerializer.Serialize(new StoryPendingRescue
        {
            TransactionId = rescueId, SafetyCheckpointId = completedBase
        }));
        int stableChapterCount = (await memories.LoadAsync(projectId)).Chapters.Count;
        int stableMessageCount = (await projects.LoadMessagesAsync(projectId)).Count;
        if (!checkpoints.HasPendingTransaction(projectId))
            throw new InvalidOperationException("Unfinished manuscript rescue was not recognized as pending.");
        bool commitBlocked = false;
        try { await checkpoints.BeginCommitAsync(projectId, completedBase); }
        catch (InvalidOperationException) { commitBlocked = true; }
        if (!commitBlocked)
            throw new InvalidOperationException("New commit started before unfinished rescue cleanup.");
        await checkpoints.RecoverIfNeededAsync(projectId);
        string rescuedNote = Path.Combine(branches, "recovered", rescueId, "unsnapshotted-note.txt");
        if (!File.Exists(rescuedNote) || File.Exists(marker) || Directory.Exists(oldChapters) ||
            await File.ReadAllTextAsync(rescuedNote) !=
                "Original unpublished note survived a post-restore crash." ||
            (await memories.LoadAsync(projectId)).Chapters.Count != stableChapterCount ||
            (await projects.LoadMessagesAsync(projectId)).Count != stableMessageCount)
            throw new InvalidOperationException("Interrupted post-restore cleanup lost notes or modified committed Story.");

        // A caller must not be able to mark an incomplete transaction as
        // committed if it failed before the new chapter was durably published.
        StoryMemoryDocument preflightMemory = await memories.LoadAsync(projectId);
        IReadOnlyList<StoryMessage> preflightMessages = await projects.LoadMessagesAsync(projectId);
        string preflightCheckpoint = await checkpoints.CaptureAsync(projectId,
            preflightMemory, preflightMessages, "Self-test incomplete finalization guard");
        await checkpoints.BeginCommitAsync(projectId, preflightCheckpoint);
        bool incompleteRejected = false;
        try { await checkpoints.CompleteCommitAsync(projectId); }
        catch (InvalidDataException) { incompleteRejected = true; }
        if (!incompleteRejected)
            throw new InvalidOperationException("Incomplete chapter commit was marked as successful.");
        await checkpoints.AbortCommitAsync(projectId);
        if ((await memories.LoadAsync(projectId)).Chapters.Count != stableChapterCount ||
            checkpoints.HasPendingTransaction(projectId))
            throw new InvalidOperationException("Incomplete chapter commit guard was not recoverable.");

        // Simulate termination AFTER abort restore retired its pending-restore
        // journal but BEFORE pending-commit cleanup. A second rollback here would
        // destroy unrelated work saved after the restore completed.
        StoryMemoryDocument stableAfterAbort = await memories.LoadAsync(projectId);
        stableAfterAbort.Canon.Add(new StoryCanonFact { Fact = "PostAbortDurable sentinel" });
        await memories.SaveAsync(projectId, stableAfterAbort);
        await File.WriteAllTextAsync(journal, JsonSerializer.Serialize(new StoryPendingCommit
        {
            CheckpointId = preflightCheckpoint,
            TransactionId = Guid.NewGuid().ToString("N"),
            Aborting = true
        }));
        await checkpoints.RecoverIfNeededAsync(projectId);
        if (File.Exists(journal) || checkpoints.HasPendingTransaction(projectId) ||
            !(await memories.LoadAsync(projectId)).Canon.Any(c =>
                c.Fact == "PostAbortDurable sentinel"))
            throw new InvalidOperationException("Post-abort recovery replayed a completed rollback.");
        stableAfterAbort.Canon.RemoveAll(c => c.Fact == "PostAbortDurable sentinel");
        await memories.SaveAsync(projectId, stableAfterAbort);

        // Reject a corrupt journal that simultaneously reports success and abort.
        await File.WriteAllTextAsync(journal, JsonSerializer.Serialize(new StoryPendingCommit
        {
            CheckpointId = preflightCheckpoint,
            TransactionId = Guid.NewGuid().ToString("N"),
            Committed = true,
            Aborting = true
        }));
        bool contradictoryRejected = false;
        try { await checkpoints.RecoverIfNeededAsync(projectId); }
        catch (InvalidDataException) { contradictoryRejected = true; }
        if (!contradictoryRejected || !File.Exists(journal))
            throw new InvalidOperationException("Contradictory recovery journal was silently accepted.");
        File.Delete(journal); // Test-only malformed journal, no real Story data affected.

        // Never borrow a restore journal from a different operation when aborting.
        string restoreJournal = Path.Combine(branches, "pending-restore.json");
        await File.WriteAllTextAsync(journal, JsonSerializer.Serialize(new StoryPendingCommit
        {
            CheckpointId = preflightCheckpoint,
            TransactionId = Guid.NewGuid().ToString("N")
        }));
        await File.WriteAllTextAsync(restoreJournal, JsonSerializer.Serialize(new StoryPendingRestore
        {
            TargetCheckpointId = completedBase,
            SafetyCheckpointId = completedBase,
            TransactionId = Guid.NewGuid().ToString("N")
        }));
        bool unrelatedRejected = false;
        try { await checkpoints.AbortCommitAsync(projectId); }
        catch (InvalidDataException) { unrelatedRejected = true; }
        if (!unrelatedRejected || !File.Exists(journal) || !File.Exists(restoreJournal))
            throw new InvalidOperationException("Chapter abort improperly adopted an unrelated recovery journal.");
        File.Delete(journal);
        File.Delete(restoreJournal); // Both journals were injected by this fixture only.
    }
}
