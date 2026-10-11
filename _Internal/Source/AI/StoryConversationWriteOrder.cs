using System.Collections.Concurrent;
using System.IO;
using System.Threading;

namespace WorkChatMini;

// All StoryProjectStore instances in this process share one file-write queue per
// actual Story directory. Version is assigned when a save is requested, BEFORE
// background JSON serialization: a slow earlier save cannot overwrite a newer
// successful save simply because its serializer completed later.
// This coordinates conversation autosaves; it is not an inter-process file lock.
internal static class StoryConversationWriteOrder
{
    private sealed class State
    {
        internal readonly SemaphoreSlim Gate = new(1, 1);
        internal long NextVersion;
        internal long PersistedVersion;
    }

    private static readonly ConcurrentDictionary<string, State> States =
        new(StringComparer.OrdinalIgnoreCase);

    private static State ForPath(string path) => States.GetOrAdd(
        Path.GetFullPath(path), static _ => new State());

    internal static long Reserve(string path) => Interlocked.Increment(ref ForPath(path).NextVersion);

    internal static async Task<bool> SaveIfNewestAsync(string path, long version,
        Func<Task> persist, CancellationToken token = default)
    {
        State state = ForPath(path);
        await state.Gate.WaitAsync(token).ConfigureAwait(false);
        try
        {
            if (version <= state.PersistedVersion)
                return false;
            token.ThrowIfCancellationRequested();
            await persist().ConfigureAwait(false);
            // Do not advance on failed / cancelled writes. A previously queued
            // autosave may still be the only recoverable data in that case.
            state.PersistedVersion = version;
            return true;
        }
        finally { state.Gate.Release(); }
    }
}
