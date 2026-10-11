using System.Collections.Concurrent;
using System.Threading;

namespace WorkChatMini;

// Serializes dangerous story-file mutations across *all* store instances in one process.
// The WPF busy flag prevents normal double-clicks, but it cannot prevent two independent
// store objects (or a recovery and a branch operation) from racing on the same journals.
// A lease covers each complete restore / branch mutation, never just the preflight check.
internal static class StoryOperationGate
{
    private static readonly ConcurrentDictionary<string, SemaphoreSlim> Gates =
        new(StringComparer.OrdinalIgnoreCase);

    internal static async Task<IDisposable> EnterAsync(string projectId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        SemaphoreSlim gate = Gates.GetOrAdd(projectId, static _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Lease(gate);
    }

    private sealed class Lease : IDisposable
    {
        private SemaphoreSlim? _gate;
        internal Lease(SemaphoreSlim gate) => _gate = gate;
        public void Dispose() => Interlocked.Exchange(ref _gate, null)?.Release();
    }
}
