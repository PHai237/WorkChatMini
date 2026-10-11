using System.IO;

namespace WorkChatMini;

internal static class StorySseReader
{
    private static readonly TimeSpan InactivityTimeout = TimeSpan.FromSeconds(45);

    internal static async Task<string?> ReadLineAsync(
        StreamReader reader,
        string providerName,
        CancellationToken cancellationToken)
    {
        using var readCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        readCts.CancelAfter(InactivityTimeout);
        try
        {
            return await reader.ReadLineAsync(readCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"{providerName} stream returned no data for {(int)InactivityTimeout.TotalSeconds} seconds.");
        }
    }
}
