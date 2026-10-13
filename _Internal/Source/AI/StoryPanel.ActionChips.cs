using System.Windows;
using System.Windows.Controls;
using WpfButton = System.Windows.Controls.Button;

namespace WorkChatMini;

public partial class StoryPanel
{
    private bool _advancedStoryToolsVisible;

    private void ToggleAdvancedStoryActions_Click(object sender, RoutedEventArgs e)
    {
        _advancedStoryToolsVisible = !_advancedStoryToolsVisible;
        AdvancedStoryActions.Visibility = _advancedStoryToolsVisible ? Visibility.Visible : Visibility.Collapsed;
        AdvancedStoryActionsButton.Content = _advancedStoryToolsVisible ? "Fewer tools  ▴" : "More tools  ▾";
        AdvancedStoryActionsButton.ToolTip = _advancedStoryToolsVisible
            ? "Hide optional writing, lore and continuity tools"
            : "Show additional writing, lore and continuity tools";
    }

    private void UpdateActionChipSelection()
    {
        if (PrimaryStoryActions is null || AdvancedStoryActions is null)
            return;
        foreach (var button in PrimaryStoryActions.Children.OfType<WpfButton>()
                     .Concat(AdvancedStoryActions.Children.OfType<WpfButton>()))
        {
            if (button == AdvancedStoryActionsButton || button == SceneCompassActionButton)
                continue;
            bool selected = button == DiscussStoryActionButton
                ? _selectedAction == StoryActionType.Chat
                : button.Tag is string tag && StoryActions.TryParse(tag, out var action) && action == _selectedAction;
            // Use ClearValue on the unselected buttons so theme resources, hover
            // and disabled states work normally rather than keeping stale local brushes.
            if (selected)
            {
                button.Background = (System.Windows.Media.Brush)FindResource("StoryGoldDim");
                button.BorderBrush = (System.Windows.Media.Brush)FindResource("StoryGoldBorder");
                button.Foreground = (System.Windows.Media.Brush)FindResource("StoryGold");
            }
            else
            {
                button.ClearValue(System.Windows.Controls.Control.BackgroundProperty);
                button.ClearValue(System.Windows.Controls.Control.BorderBrushProperty);
                button.ClearValue(System.Windows.Controls.Control.ForegroundProperty);
            }
        }
    }
}
