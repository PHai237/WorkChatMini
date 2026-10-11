using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfButton = System.Windows.Controls.Button;

namespace WorkChatMini;

public partial class StoryPanel
{
    // A long message should not force WPF to measure tens of thousands of
    // glyphs on every resize. No content is discarded or rewritten on disk.
    private void AttachLongMessageReader(StackPanel container, TextBlock text,
        StoryMessage message, int preferredTextPosition = -1)
    {
        string full = message.Content ?? string.Empty;
        if (full.Length <= StoryLongMessageWindow.ChunkCharacters)
        {
            text.Text = full;
            return;
        }

        int start = StoryLongMessageWindow.StartForMatch(full.Length, preferredTextPosition);
        var controls = new WrapPanel
        {
            Orientation = System.Windows.Controls.Orientation.Horizontal,
            Margin = new Thickness(0, 12, 0, 2)
        };
        var previous = new WpfButton
        {
            Content = "← Previous part",
            Style = (Style)FindResource("StoryButton"),
            Padding = new Thickness(8, 4, 8, 4),
            MinHeight = 26,
            Margin = new Thickness(0, 0, 8, 3)
        };
        var next = new WpfButton
        {
            Content = "Next part →",
            Style = (Style)FindResource("StoryButton"),
            Padding = new Thickness(8, 4, 8, 4),
            MinHeight = 26,
            Margin = new Thickness(8, 0, 0, 3)
        };
        var progress = new TextBlock
        {
            Foreground = (SolidColorBrush)FindResource("StoryMuted"),
            FontSize = 10,
            VerticalAlignment = VerticalAlignment.Center
        };

        void ShowSegment()
        {
            (int from, int to) = StoryLongMessageWindow.Slice(full.Length, start);
            text.Text = full.Substring(from, to - from);
            progress.Text = $"{from + 1:N0}–{to:N0} / {full.Length:N0} characters";
            previous.IsEnabled = from > 0;
            next.IsEnabled = to < full.Length;
        }

        previous.Click += (_, _) =>
        {
            start = StoryLongMessageWindow.Previous(start);
            ShowSegment();
        };
        next.Click += (_, _) =>
        {
            start = StoryLongMessageWindow.Next(full.Length, start);
            ShowSegment();
        };
        controls.Children.Add(previous);
        controls.Children.Add(progress);
        controls.Children.Add(next);
        // Insert immediately after the prose, before proposal/commit/copy actions.
        int textIndex = container.Children.IndexOf(text);
        container.Children.Insert(Math.Max(0, textIndex + 1), controls);
        ShowSegment();
    }
}
