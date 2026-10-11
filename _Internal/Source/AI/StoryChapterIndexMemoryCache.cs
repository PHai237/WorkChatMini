namespace WorkChatMini;

/// <summary>Bounded, process-local cache of disposable chapter indexes.
/// Chapter evidence and canon still live on disk, never inside this cache.</summary>
internal sealed class StoryChapterIndexMemoryCache
{
    private const int MaxProjects = 6;
    private readonly object _sync = new();
    private readonly Dictionary<string, StoryChapterIndexDocument> _items =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, LinkedListNode<string>> _nodes =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly LinkedList<string> _recent = new();

    internal int Count
    {
        get { lock (_sync) return _items.Count; }
    }

    internal bool TryGet(string projectId, out StoryChapterIndexDocument? index)
    {
        lock (_sync)
        {
            if (!_items.TryGetValue(projectId, out index)) return false;
            LinkedListNode<string> node = _nodes[projectId];
            _recent.Remove(node);
            _recent.AddLast(node);
            return true;
        }
    }

    internal void Put(string projectId, StoryChapterIndexDocument index)
    {
        lock (_sync)
        {
            _items[projectId] = index;
            if (_nodes.TryGetValue(projectId, out LinkedListNode<string>? oldNode) && oldNode is not null)
                _recent.Remove(oldNode);
            _nodes[projectId] = _recent.AddLast(projectId);
            while (_items.Count > MaxProjects)
            {
                LinkedListNode<string>? first = _recent.First;
                if (first is null) break;
                _recent.RemoveFirst();
                _nodes.Remove(first.Value);
                _items.Remove(first.Value);
            }
        }
    }

    internal void Remove(string projectId)
    {
        lock (_sync)
        {
            _items.Remove(projectId);
            if (_nodes.Remove(projectId, out LinkedListNode<string>? node) && node is not null)
                _recent.Remove(node);
        }
    }
}
