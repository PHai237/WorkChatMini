namespace WorkChatMini;

internal static class StoryOperationGateSelfTest
{
    internal static async Task VerifyAsync()
    {
        string firstId = Guid.NewGuid().ToString("N");
        string secondId = Guid.NewGuid().ToString("N");
        IDisposable first = await StoryOperationGate.EnterAsync(firstId);
        try
        {
            // Different store instances and differently cased IDs must share a gate.
            Task<IDisposable> queued = StoryOperationGate.EnterAsync(firstId.ToUpperInvariant());
            if (queued.IsCompleted)
                throw new InvalidOperationException("Two concurrent mutations acquired the same Story lock.");
            using (await StoryOperationGate.EnterAsync(secondId))
            {
                // Locking one story must never block another one.
            }
            using var cancellation = new CancellationTokenSource();
            Task<IDisposable> cancelled = StoryOperationGate.EnterAsync(firstId, cancellation.Token);
            cancellation.Cancel();
            bool correctlyCancelled = false;
            try { using var ignored = await cancelled; }
            catch (OperationCanceledException) { correctlyCancelled = true; }
            if (!correctlyCancelled)
                throw new InvalidOperationException("Cancelled Story mutation was not removed from the wait queue.");
            first.Dispose();
            using var next = await queued.WaitAsync(TimeSpan.FromSeconds(5));
            if (queued.IsFaulted)
                throw new InvalidOperationException("Story operation lock did not resume a queued writer.");
        }
        finally { first.Dispose(); }
    }
}
