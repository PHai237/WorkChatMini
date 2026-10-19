using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;

namespace WorkChatMini;

public partial class MainWindow
{
    private BrowserHistoryStore? _browserHistory;
    private Popup? _browserHistoryPopup;
    private StackPanel? _browserHistoryList;
    private readonly List<BrowserHistoryEntry> _browserHistoryVisibleEntries = new();
    private int _browserHistorySelection = -1;
    private bool _browserAddressInternalUpdate;

    private void EnsureBrowserHistoryReady()
    {
        _browserHistory ??= BrowserHistoryStore.Load();
        if (_browserHistoryPopup is not null)
            return;

        var list = new StackPanel { Orientation = System.Windows.Controls.Orientation.Vertical };
        var border = new Border
        {
            Background = FrozenBrush(24, 29, 38),
            BorderBrush = FrozenBrush(55, 64, 79),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(10),
            Padding = new Thickness(4),
            Child = list
        };
        _browserHistoryList = list;
        _browserHistoryPopup = new Popup
        {
            PlacementTarget = BrowserAddressBox,
            Placement = PlacementMode.Bottom,
            VerticalOffset = 4,
            AllowsTransparency = true,
            StaysOpen = false,
            Child = border
        };
    }

    private void RecordBrowserHistory(BrowserTabState tab)
    {
        if (tab.IsBlank || tab.Closing)
            return;
        EnsureBrowserHistoryReady();
        _browserHistory?.Record(tab.Url, tab.Title);
    }

    private void SetBrowserAddressText(string value)
    {
        _browserAddressInternalUpdate = true;
        try
        {
            if (!string.Equals(BrowserAddressBox.Text, value, StringComparison.Ordinal))
                BrowserAddressBox.Text = value;
        }
        finally
        {
            _browserAddressInternalUpdate = false;
        }
    }

    private void UpdateBrowserHistorySuggestions()
    {
        if (_browserAddressInternalUpdate || !BrowserAddressBox.IsKeyboardFocusWithin)
        {
            CloseBrowserHistorySuggestions();
            return;
        }

        string query = BrowserAddressBox.Text.Trim();
        if (query.Length == 0)
        {
            CloseBrowserHistorySuggestions();
            return;
        }

        EnsureBrowserHistoryReady();
        IReadOnlyList<BrowserHistoryEntry> matches = _browserHistory?.Search(query, 6)
            ?? Array.Empty<BrowserHistoryEntry>();
        if (matches.Count == 0)
        {
            CloseBrowserHistorySuggestions();
            return;
        }

        _browserHistoryVisibleEntries.Clear();
        _browserHistoryVisibleEntries.AddRange(matches);
        _browserHistorySelection = -1;
        RebuildBrowserHistoryRows();
        if (_browserHistoryPopup is not null)
        {
            _browserHistoryPopup.Width = Math.Max(120, Math.Min(BrowserAddressBox.ActualWidth, 620));
            _browserHistoryPopup.IsOpen = true;
        }
    }

    private void RebuildBrowserHistoryRows()
    {
        if (_browserHistoryList is null)
            return;

        _browserHistoryList.Children.Clear();
        for (int i = 0; i < _browserHistoryVisibleEntries.Count; i++)
        {
            BrowserHistoryEntry entry = _browserHistoryVisibleEntries[i];
            int index = i;
            var title = new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(entry.Title) ? BrowserTitleFromUrl(entry.Url) : entry.Title,
                Foreground = BrowserTabTitleActiveBrush,
                FontSize = 12.5,
                FontWeight = FontWeights.SemiBold,
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var address = new TextBlock
            {
                Text = CompactHistoryAddress(entry.Url),
                Foreground = BrowserTabTitleInactiveBrush,
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0),
                TextTrimming = TextTrimming.CharacterEllipsis
            };
            var content = new StackPanel { Margin = new Thickness(10, 7, 10, 7) };
            content.Children.Add(title);
            content.Children.Add(address);
            var row = new Border
            {
                Background = index == _browserHistorySelection ? FrozenBrush(43, 51, 66) : System.Windows.Media.Brushes.Transparent,
                CornerRadius = new CornerRadius(7),
                Cursor = System.Windows.Input.Cursors.Hand,
                Child = content,
                Tag = index
            };
            row.MouseLeftButtonUp += BrowserHistoryRow_MouseLeftButtonUp;
            _browserHistoryList.Children.Add(row);
        }
    }

    private void BrowserHistoryRow_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
    {
        if (sender is not FrameworkElement element || element.Tag is not int index)
            return;
        e.Handled = true;
        _ = NavigateBrowserHistorySelectionAsync(index);
    }

    private bool HandleBrowserHistoryKey(System.Windows.Input.KeyEventArgs e)
    {
        if (_browserHistoryPopup?.IsOpen != true || _browserHistoryVisibleEntries.Count == 0)
            return false;

        if (e.Key == Key.Down)
        {
            _browserHistorySelection = (_browserHistorySelection + 1) % _browserHistoryVisibleEntries.Count;
            RebuildBrowserHistoryRows();
            e.Handled = true;
            return true;
        }
        if (e.Key == Key.Up)
        {
            _browserHistorySelection = (_browserHistorySelection - 1 + _browserHistoryVisibleEntries.Count) % _browserHistoryVisibleEntries.Count;
            RebuildBrowserHistoryRows();
            e.Handled = true;
            return true;
        }
        if (e.Key == Key.Escape)
        {
            CloseBrowserHistorySuggestions();
            e.Handled = true;
            return true;
        }
        if (e.Key == Key.Enter && _browserHistorySelection >= 0)
        {
            int index = _browserHistorySelection;
            e.Handled = true;
            _ = NavigateBrowserHistorySelectionAsync(index);
            return true;
        }
        return false;
    }

    private async Task NavigateBrowserHistorySelectionAsync(int index)
    {
        if (index < 0 || index >= _browserHistoryVisibleEntries.Count)
            return;
        string url = _browserHistoryVisibleEntries[index].Url;
        CloseBrowserHistorySuggestions();
        await RunBrowserUiActionAsync(() => NavigateActiveBrowserTabAsync(url), "history-suggestion");
    }

    private void CloseBrowserHistorySuggestions()
    {
        if (_browserHistoryPopup is not null)
            _browserHistoryPopup.IsOpen = false;
        _browserHistoryVisibleEntries.Clear();
        _browserHistorySelection = -1;
    }

    private static string CompactHistoryAddress(string url)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out Uri? uri))
            return url;
        string path = uri.PathAndQuery == "/" ? string.Empty : uri.PathAndQuery;
        string value = uri.Host.Replace("www.", string.Empty, StringComparison.OrdinalIgnoreCase) + path;
        return value.Length <= 78 ? value : value[..77] + "…";
    }

    private void DisposeBrowserHistory()
    {
        CloseBrowserHistorySuggestions();
        try { _browserHistory?.Dispose(); } catch { }
        _browserHistory = null;
    }
}
