using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using WpfButton = System.Windows.Controls.Button;

namespace WorkChatMini;

public partial class StoryPanel
{
    private IReadOnlyList<int> _conversationSearchMatches = Array.Empty<int>();
    private int _conversationSearchCursor = -1;
    private int? _conversationFocusIndex;
    private int _conversationSearchRequestId;
    private bool _conversationSearchBusy;
    private bool _conversationSearchError;
    private CancellationTokenSource? _conversationSearchCts;
    private bool _conversationSearchOpen;

    private void ToggleConversationSearch(bool? show = null)
    {
        bool target = show ?? !_conversationSearchOpen;
        if (_creatingStory || _activeProject is null || _messages.Count == 0)
            target = false;
        _conversationSearchOpen = target;
        if (!target)
        {
            bool focused = _conversationFocusIndex is not null;
            ResetConversationSearch();
            if (focused) RenderConversation(scrollToEnd: false);
            FocusComposer();
        }
        UpdateConversationSearchVisibility();
        if (target && ConversationSearchToolbar.IsEnabled)
        {
            ConversationSearchBox.Focus();
            ConversationSearchBox.SelectAll();
        }
    }

    private void UpdateConversationSearchVisibility() =>
        ConversationSearchToolbar.Visibility = _conversationSearchOpen && !_creatingStory &&
            _activeProject is not null && _messages.Count > 0
                ? Visibility.Visible : Visibility.Collapsed;

    private void ResetConversationSearch()
    {
        _conversationSearchCts?.Cancel();
        _conversationSearchCts = null;
        ++_conversationSearchRequestId; // invalidates results from an older Story/branch
        _conversationSearchMatches = Array.Empty<int>();
        _conversationSearchCursor = -1;
        _conversationFocusIndex = null;
        _conversationSearchBusy = false;
        _conversationSearchError = false;
        ConversationSearchBox.Clear();
        UpdateConversationSearchUi();
    }


    private void StoryPanel_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Alt)
        {
            ToggleConversationSearch();
            e.Handled = true;
        }
    }

    private void ConversationSearchBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        _conversationSearchCts?.Cancel();
        _conversationSearchCts = null;
        ++_conversationSearchRequestId;
        _conversationSearchMatches = Array.Empty<int>();
        _conversationSearchCursor = -1;
        // An in-flight search belongs to the previous query. Releasing its UI
        // busy flag lets Enter submit the new query while the old task finishes.
        _conversationSearchBusy = false;
        _conversationSearchError = false;
        UpdateConversationSearchUi();
    }

    private async void ConversationSearchBox_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            e.Handled = true;
            ToggleConversationSearch(false);
            return;
        }
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        await SearchConversationAsync();
    }

    private async void ConversationSearchButton_Click(object sender, RoutedEventArgs e) =>
        await SearchConversationAsync();

    private async Task SearchConversationAsync()
    {
        if (_activeProject is null || _loadingProject || _generationCts is not null ||
            _checkpointRecoveryFailed || _conversationSearchBusy) return;
        string query = ConversationSearchBox.Text.Trim();
        if (query.Length < 2)
        {
            ConversationSearchCount.Text = "Enter 2+ characters";
            return;
        }
        string projectId = _activeProject.Id;
        int requestId = ++_conversationSearchRequestId;
        var searchCts = new CancellationTokenSource();
        _conversationSearchCts = searchCts;
        _conversationSearchError = false;
        // Capture immutable message strings on the UI thread; do not read mutable
        // WPF-bound objects from a worker and do not serialize or copy whole JSON.
        string[] content = _messages.Select(m => m.Content ?? string.Empty).ToArray();
        _conversationSearchBusy = true;
        UpdateConversationSearchUi();
        try
        {
            IReadOnlyList<int> found = await Task.Run(() =>
                StoryConversationSearch.FindMatches(content, query, searchCts.Token), searchCts.Token);
            if (requestId != _conversationSearchRequestId || _activeProject?.Id != projectId ||
                _loadingProject || _generationCts is not null) return;
            _conversationSearchMatches = found;
            _conversationSearchCursor = found.Count > 0 ? 0 : -1;
            if (found.Count > 0) FocusConversationSearchMatch();
        }
        catch (OperationCanceledException) when (searchCts.IsCancellationRequested)
        {
            // Superseded searches are not errors and must not alter the new UI.
        }
        catch (Exception ex)
        {
            if (requestId == _conversationSearchRequestId && _activeProject?.Id == projectId)
            {
                AppLogger.Error("STORY-SEARCH", "Local conversation search failed", ex);
                _conversationSearchError = true;
            }
        }
        finally
        {
            if (ReferenceEquals(_conversationSearchCts, searchCts))
                _conversationSearchCts = null;
            searchCts.Dispose();
            // Never let an old Story/query clear the busy state of a newer search.
            if (requestId == _conversationSearchRequestId && _activeProject?.Id == projectId)
            {
                _conversationSearchBusy = false;
                UpdateConversationSearchUi();
            }
        }
    }

    private void ConversationSearchPrevious_Click(object sender, RoutedEventArgs e) => MoveSearchResult(-1);
    private void ConversationSearchNext_Click(object sender, RoutedEventArgs e) => MoveSearchResult(1);

    private void MoveSearchResult(int delta)
    {
        if (_conversationSearchBusy || _generationCts is not null || _loadingProject ||
            _conversationSearchMatches.Count == 0) return;
        _conversationSearchCursor = (_conversationSearchCursor + delta + _conversationSearchMatches.Count)
            % _conversationSearchMatches.Count;
        FocusConversationSearchMatch();
        UpdateConversationSearchUi();
    }

    private void FocusConversationSearchMatch()
    {
        if (_conversationSearchCursor < 0 || _conversationSearchCursor >= _conversationSearchMatches.Count)
            return;
        int index = _conversationSearchMatches[_conversationSearchCursor];
        if (index < 0 || index >= _messages.Count) return;
        _conversationFocusIndex = index;
        RenderConversation(scrollToEnd: false);
    }

    private void UpdateConversationSearchUi()
    {
        if (ConversationSearchCount is null) return;
        bool hasResults = _conversationSearchMatches.Count > 0;
        ConversationSearchCount.Text = _conversationSearchBusy ? "Searching…"
            : _conversationSearchError ? "Search error"
            : hasResults ? $"{_conversationSearchCursor + 1} / {_conversationSearchMatches.Count}"
            : string.IsNullOrWhiteSpace(ConversationSearchBox.Text) ? "" : "No matches";
        ConversationSearchPrevious.IsEnabled = hasResults && !_conversationSearchBusy && _generationCts is null;
        ConversationSearchNext.IsEnabled = hasResults && !_conversationSearchBusy && _generationCts is null;
        ConversationSearchButton.IsEnabled = !_conversationSearchBusy && _generationCts is null;
    }

    private void AddConversationWindowNavigation(int targetIndex, string label)
    {
        var button = new WpfButton
        {
            Content = label,
            Style = (Style)FindResource("StoryButton"),
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin = new Thickness(0, 5, 0, 15),
            Padding = new Thickness(14, 7, 14, 7)
        };
        button.Click += (_, _) =>
        {
            if (_generationCts is not null) return;
            _conversationFocusIndex = targetIndex;
            RenderConversation(scrollToEnd: false);
        };
        MessagesPanel.Children.Add(button);
    }
}
