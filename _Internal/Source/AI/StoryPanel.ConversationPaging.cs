using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using WpfButton = System.Windows.Controls.Button;

namespace WorkChatMini;

public partial class StoryPanel
{
    private int _visibleConversationMessages = StoryConversationPaging.PageSize;
    private int _conversationPageEnd; // 0=tail; otherwise the exclusive end of an older window
    private bool _renderingConversation;
    private int _conversationRenderRevision;

    private void AddEarlierMessagesButton(int hiddenCount)
    {
        AddReaderNavigation($"↑  Earlier messages · {hiddenCount} remaining", older: true);
    }

    private void AddLaterMessagesButton(int hiddenCount)
    {
        AddReaderNavigation($"↓  Later messages · {hiddenCount} remaining", older: false);
    }

    private void AddReaderNavigation(string label, bool older)
    {
        var button = new WpfButton
        {
            Content = label,
            Style = (Style)FindResource("StoryButton"),
            ToolTip = "Navigate the complete saved conversation with a bounded WPF message window",
            HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
            Margin = new Thickness(0, 1, 0, 18),
            Padding = new Thickness(17, 8, 17, 8)
        };
        button.Click += (_, _) =>
        {
            if (_generationCts is not null || _loadingProject || _activeProject is null) return;
            string storyId = _activeProject.Id;
            (int oldStart, int oldEnd) = StoryConversationPaging.Window(
                _messages.Count, _visibleConversationMessages, _conversationPageEnd);
            if (older && oldStart == 0 || !older && oldEnd == _messages.Count) return;

            // Keep the first message's screen position when prepending older content;
            // unlike extent-height compensation this still works after trimming the tail.
            var previousRows = MessagesPanel.Children.OfType<Grid>()
                .Where(x => Equals(x.Tag, "StoryMessageRow")).ToList();
            Grid? anchor = older ? previousRows.FirstOrDefault() : previousRows.LastOrDefault();
            int anchorIndex = older ? oldStart : oldEnd - 1;
            double anchorY = anchor?.TranslatePoint(new System.Windows.Point(0, 0), ChatScroll).Y ?? 0;
            (_visibleConversationMessages, _conversationPageEnd) = older
                ? StoryConversationPaging.Older(_messages.Count, _visibleConversationMessages, _conversationPageEnd)
                : StoryConversationPaging.Later(_messages.Count, _visibleConversationMessages, _conversationPageEnd);
            int newStart = StoryConversationPaging.Window(
                _messages.Count, _visibleConversationMessages, _conversationPageEnd).Start;
            RenderConversation(scrollToEnd: false);
            int renderRevision = _conversationRenderRevision;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (_activeProject?.Id != storyId || _loadingProject ||
                    _conversationRenderRevision != renderRevision) return;
                ChatScroll.UpdateLayout();
                var rows = MessagesPanel.Children.OfType<Grid>()
                    .Where(x => Equals(x.Tag, "StoryMessageRow")).ToList();
                if (rows.Count == 0) return;
                int anchorRow = Math.Clamp(anchorIndex - newStart, 0, rows.Count - 1);
                double currentY = rows[anchorRow].TranslatePoint(new System.Windows.Point(0, 0), ChatScroll).Y;
                ChatScroll.ScrollToVerticalOffset(ChatScroll.VerticalOffset + currentY - anchorY);
                UpdateLatestJumpUi();
            }), DispatcherPriority.Loaded);
        };
        MessagesPanel.Children.Add(button);
    }
}
