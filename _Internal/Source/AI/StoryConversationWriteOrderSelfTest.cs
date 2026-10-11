using System.IO;

namespace WorkChatMini;

// Deterministic version-order and failed-write regression assertions. Uses a
// unique virtual file path; touches no real Story, chapter or AppData record.
internal static class StoryConversationWriteOrderSelfTest
{
    internal static async Task VerifyAsync()
    {
        string path = Path.Combine(Path.GetTempPath(),
            "wcm-conversation-test-" + Guid.NewGuid().ToString("N"), "conversation.json");
        long older = StoryConversationWriteOrder.Reserve(path);
        long newer = StoryConversationWriteOrder.Reserve(path);
        string recorded = string.Empty;
        bool wroteNew = await StoryConversationWriteOrder.SaveIfNewestAsync(path, newer, () =>
        {
            recorded = "latest";
            return Task.CompletedTask;
        });
        bool wroteOld = await StoryConversationWriteOrder.SaveIfNewestAsync(path, older, () =>
        {
            recorded = "stale";
            return Task.CompletedTask;
        });
        if (!wroteNew || wroteOld || recorded != "latest")
            throw new InvalidOperationException("Late completion overwrote the newer conversation snapshot.");

        long failed = StoryConversationWriteOrder.Reserve(path);
        bool raised = false;
        try
        {
            await StoryConversationWriteOrder.SaveIfNewestAsync(path, failed,
                () => throw new IOException("Simulated cancelled/failed write"));
        }
        catch (IOException) { raised = true; }
        if (!raised || recorded != "latest")
            throw new InvalidOperationException("Failed save changed the persisted conversation order.");

        long afterFailure = StoryConversationWriteOrder.Reserve(path);
        bool retried = await StoryConversationWriteOrder.SaveIfNewestAsync(path, afterFailure, () =>
        {
            recorded = "recovered";
            return Task.CompletedTask;
        });
        if (!retried || recorded != "recovered")
            throw new InvalidOperationException("Write failure permanently blocked later conversation saves.");

        // A case-insensitive key shares one coordinator across store instances.
        long uppercase = StoryConversationWriteOrder.Reserve(path.ToUpperInvariant());
        bool final = await StoryConversationWriteOrder.SaveIfNewestAsync(path, uppercase, () =>
        {
            recorded = "shared";
            return Task.CompletedTask;
        });
        if (!final || recorded != "shared")
            throw new InvalidOperationException("Conversation writers do not share their Story coordinator.");

        // Exercise the public persistence path with two independent store
        // instances on a disposable SelfTest Story (never a personal project).
        var firstStore = new StoryProjectStore();
        StoryProject tempProject = await firstStore.CreateProjectAsync("Conversation queue regression");
        try
        {
            var secondStore = new StoryProjectStore();
            await firstStore.SaveMessagesAsync(tempProject.Id, new[]
            {
                new StoryMessage { Role = "user", Content = "First store" }
            });
            await secondStore.SaveMessagesAsync(tempProject.Id, new[]
            {
                new StoryMessage { Role = "user", Content = "Second store" }
            });
            IReadOnlyList<StoryMessage> last = await firstStore.LoadMessagesAsync(tempProject.Id);
            if (last.Count != 1 || last[0].Content != "Second store")
                throw new InvalidOperationException("Cross-instance conversation persistence lost the newest save.");
        }
        finally { await firstStore.DeleteProjectAsync(tempProject.Id); }
    }
}
