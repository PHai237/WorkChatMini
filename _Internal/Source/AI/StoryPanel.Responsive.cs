using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace WorkChatMini;

public partial class StoryPanel
{
    private const double NormalSidebarWidth = 196;
    private const double CompactSidebarWidth = 168;

    private void StoryPanel_SizeChanged(object sender, SizeChangedEventArgs e) => ApplyResponsiveLayout();

    private void ApplyResponsiveLayout()
    {
        if (ChatScroll is null || ComposerShell is null || SidebarColumn is null ||
            WelcomeActionCards is null || NoProjectState is null ||
            EditLastPromptButton is null || RetryLastButton is null)
            return;

        double width = Math.Max(0, ActualWidth);
        double height = Math.Max(0, ActualHeight);
        bool compactWidth = width > 0 && width < 1400;
        bool narrowWidth = width > 0 && width < 1050;
        bool compactHeight = height > 0 && height < 800;
        bool shortHeight = height > 0 && height < 680;

        if (!_sidebarCollapsed)
            SidebarColumn.Width = new GridLength(narrowWidth ? 150 : compactWidth ? CompactSidebarWidth : NormalSidebarWidth);

        if (InspectorPanel.Visibility == Visibility.Visible)
        {
            // Never let the inspector consume the entire conversation on a
            // narrow laptop/second monitor. Reserve space for chat and composer.
            double desired = narrowWidth ? 300 : compactWidth ? 332 : 372;
            double sidebar = _sidebarCollapsed ? 48 : narrowWidth ? 150 : compactWidth ? CompactSidebarWidth : NormalSidebarWidth;
            double inspectorWidth = Math.Min(desired, Math.Max(220, width - sidebar - 350));
            InspectorColumn.Width = new GridLength(inspectorWidth);
        }

        ChatScroll.Padding = narrowWidth
            ? new Thickness(14, 14, 14, 5)
            : compactWidth
                ? new Thickness(22, 18, 22, 6)
                : new Thickness(34, 26, 34, 8);

        ComposerShell.Padding = shortHeight
            ? new Thickness(12, 7, 12, 9)
            : compactHeight
                ? new Thickness(16, 8, 16, 11)
                : new Thickness(20, 11, 20, 17);

        double composerMinHeight = shortHeight ? 58 : compactHeight ? 72 : 92;
        double composerMaxHeight = shortHeight ? 104 : compactHeight ? 140 : 190;
        ComposerInputHost.MinHeight = composerMinHeight;
        ComposerInputHost.MaxHeight = composerMaxHeight;
        InputBox.MinHeight = composerMinHeight;
        InputBox.MaxHeight = composerMaxHeight;

        ContextBar.Height = shortHeight ? 30 : compactHeight ? 32 : 36;
        // The centered WrapPanel otherwise measures all seven cards in a single
        // unbounded line and overflows the narrow Story welcome screen.
        double availableWelcomeWidth = width - SidebarColumn.Width.Value - InspectorColumn.Width.Value - 55;
        WelcomeActionCards.Width = Math.Clamp(availableWelcomeWidth, 164, 790);
        NoProjectState.Width = Math.Clamp(availableWelcomeWidth, 230, 430);
        RoleBox.Width = narrowWidth ? 108 : 126;
        EditLastPromptButton.Content = narrowWidth ? "Edit" : "✎ Edit prompt";
        RetryLastButton.Content = narrowWidth ? "↻ Retry" : "↻ Retry AI";
        SuggestionStrip.Margin = new Thickness(0, 0, 0, shortHeight ? 6 : 9);

        foreach (Grid row in MessagesPanel.Children.OfType<Grid>().Where(row => Equals(row.Tag, "StoryMessageRow")))
            UpdateMessageRowLayout(row);
    }

    private void UpdateMessageRowLayout(Grid row)
    {
        if (row.Children.OfType<Border>().FirstOrDefault(border => border.Tag is string tag && tag.StartsWith("StoryMessageBubble:", StringComparison.Ordinal)) is not Border bubble)
            return;

        bool user = string.Equals(bubble.Tag as string, "StoryMessageBubble:user", StringComparison.Ordinal);
        // Use the actual available width: the old 420px minimum made bubbles
        // larger than the entire chat column when the inspector was open.
        double workspaceWidth = Math.Max(200, ActualWidth - SidebarColumn.ActualWidth - InspectorColumn.ActualWidth);
        double contentWidth = Math.Max(180, workspaceWidth - ChatScroll.Padding.Left - ChatScroll.Padding.Right - 20);
        bool narrow = contentWidth < 720;
        bool medium = contentWidth < 900;

        row.MaxWidth = Math.Min(900, contentWidth);
        if (user)
        {
            bubble.MaxWidth = Math.Min(540, Math.Max(160, contentWidth * (narrow ? 0.93 : 0.76)));
            bubble.Margin = new Thickness(contentWidth < 360 ? 5 : narrow ? 18 : medium ? 54 : 120, 0, 0, 0);
        }
        else
        {
            bubble.MaxWidth = Math.Min(660, Math.Max(160, contentWidth * (narrow ? 0.94 : 0.82)));
            bubble.Margin = new Thickness(0, 0, contentWidth < 360 ? 5 : narrow ? 12 : medium ? 44 : 100, 0);
        }
    }

    private FrameworkElement CreateMessageMetaRow(StoryMessage message, TextBlock messageText, DateTimeOffset createdUtc, bool user)
    {
        var meta = new StackPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Margin = new Thickness(0, 8, 0, 0),
            HorizontalAlignment = user ? System.Windows.HorizontalAlignment.Right : System.Windows.HorizontalAlignment.Left
        };

        meta.Children.Add(new TextBlock
        {
            Text = createdUtc.ToLocalTime().ToString("HH:mm"),
            Foreground = (SolidColorBrush)FindResource("StoryMuted"),
            FontSize = 9.5,
            VerticalAlignment = VerticalAlignment.Center
        });

        var copyButton = new System.Windows.Controls.Button
        {
            Content = "⧉",
            Style = (Style)FindResource("StoryMessageCopyButton")
        };
        copyButton.Click += async (_, _) =>
        {
            // UI excerpts and streaming tails are only visual. Copy the actual
            // persisted response, never the currently visible 6k-character part.
            string value = StoryLongMessageWindow.FullForCopy(message.Content,
                _streamingTextBlock == messageText ? _streamAccumulated : null, messageText.Text);
            if (string.IsNullOrWhiteSpace(value))
                return;
            try
            {
                System.Windows.Clipboard.SetText(value);
                copyButton.Content = "✓";
                copyButton.ToolTip = "Copied";
                await Task.Delay(900);
                if (copyButton.IsLoaded)
                {
                    copyButton.Content = "⧉";
                    copyButton.ToolTip = "Copy message";
                }
            }
            catch (Exception ex)
            {
                AppLogger.Warn("STORY", $"Could not copy story message | {ex.Message}");
            }
        };
        meta.Children.Add(copyButton);
        if (user && ReferenceEquals(GetEditablePrompt(), message))
        {
            var editButton = new System.Windows.Controls.Button
            {
                Content = "✎ Edit",
                ToolTip = "Edit this last uncommitted prompt and regenerate the response",
                Style = (Style)FindResource("StoryInlineActionButton"),
                MinWidth = 54,
                Margin = new Thickness(5, 0, 0, 0)
            };
            editButton.Click += (_, _) => BeginPromptEdit(message);
            meta.Children.Add(editButton);
        }
        return meta;
    }

    private void SuggestionStrip_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer viewer || viewer.ScrollableWidth <= 0)
            return;
        viewer.ScrollToHorizontalOffset(Math.Clamp(viewer.HorizontalOffset - e.Delta * 0.45, 0, viewer.ScrollableWidth));
        e.Handled = true;
    }
}
