namespace WorkChatMini;

/// <summary>
/// Coalesces rapid Story selections while the current project is recovering/loading.
/// The latest explicit selection wins, but project loads never overlap.
/// </summary>
internal sealed class StoryProjectSwitchQueue
{
    private bool _busy;
    private string? _pendingId;

    internal bool IsBusy => _busy;

    internal bool TryBegin(string projectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(projectId);
        if (_busy)
        {
            _pendingId = projectId;
            return false;
        }
        _busy = true;
        return true;
    }

    // Leaves the lease active when another project is queued. The caller must
    // finish or Abort even if an individual project failed to load.
    internal string? Complete()
    {
        if (!_busy) throw new InvalidOperationException("No project load is active.");
        string? next = _pendingId;
        _pendingId = null;
        if (next is null) _busy = false;
        return next;
    }

    internal void Abort()
    {
        _pendingId = null;
        _busy = false;
    }
}
