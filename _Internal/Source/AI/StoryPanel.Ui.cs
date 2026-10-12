using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace WorkChatMini;

public partial class StoryPanel
{
    private bool _sidebarCollapsed;

    private void SidebarCollapseButton_Click(object sender, RoutedEventArgs e)
    {
        _sidebarCollapsed = !_sidebarCollapsed;
        SidebarColumn.Width = new GridLength(_sidebarCollapsed ? 48 : 196);
        StoriesLabel.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        NewStorySidebarText.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        SidebarCollapseButton.Content = _sidebarCollapsed ? "›" : "‹";
        SidebarCollapseButton.ToolTip = _sidebarCollapsed ? "Expand story sidebar" : "Collapse story sidebar";
        NewProjectButton.Margin = _sidebarCollapsed ? new Thickness(6, 10, 6, 4) : new Thickness(8, 10, 8, 4);
        NewProjectButton.HorizontalContentAlignment = _sidebarCollapsed ? System.Windows.HorizontalAlignment.Center : System.Windows.HorizontalAlignment.Stretch;
        NewProjectButton.Padding = _sidebarCollapsed ? new Thickness(0, 7, 0, 7) : new Thickness(10, 7, 10, 7);

        CharactersNavText.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        CanonNavText.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        TimelineNavText.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        LoreNavText.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        ThreadsNavText.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        ArcsNavText.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        ResearchNavText.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        MemoryNavText.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        SceneCompassNavText.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        IdeasNavText.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        SettingsNavText.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        CharactersCountBadge.Visibility = _sidebarCollapsed ? Visibility.Collapsed : Visibility.Visible;
        RebuildProjectList();
        UpdateProjectSelectorUi();
    }

    private void InputBox_TextChanged(object sender, TextChangedEventArgs e)
    {
        bool empty = string.IsNullOrEmpty(InputBox.Text);
        InputHintText.Visibility = empty ? Visibility.Visible : Visibility.Collapsed;
        if (_generationCts is null)
            SendButton.IsEnabled = CanSubmitCurrentStoryAction();
    }

    private void UpdateStorySurfaceState()
    {
        bool hasProject = _activeProject is not null;
        bool hasMessages = hasProject && _messages.Count > 0;
        bool canEdit = hasProject && !_creatingStory && !_loadingProject &&
            _generationCts is null && !_checkpointRecoveryFailed && !_creativeDecisionBusy && !_storageAuditBusy;

        NewStoryEditor.Visibility = _creatingStory ? Visibility.Visible : Visibility.Collapsed;
        EmptyState.Visibility = !_creatingStory && !hasMessages ? Visibility.Visible : Visibility.Collapsed;
        NoProjectState.Visibility = !_creatingStory && !hasProject ? Visibility.Visible : Visibility.Collapsed;
        ProjectWelcomeState.Visibility = !_creatingStory && hasProject && !hasMessages ? Visibility.Visible : Visibility.Collapsed;
        ChatScroll.Visibility = !_creatingStory && hasMessages ? Visibility.Visible : Visibility.Collapsed;
        UpdateConversationSearchVisibility();
        ConversationSearchToolbar.IsEnabled = canEdit;
        ContextBar.Visibility = !_creatingStory && hasMessages ? Visibility.Visible : Visibility.Collapsed;
        SuggestionStrip.Visibility = !_creatingStory && hasProject ? Visibility.Visible : Visibility.Collapsed;
        SuggestionStrip.IsEnabled = canEdit;

        RoleBox.IsEnabled = canEdit;
        MemoryButton.IsEnabled = canEdit;
        GenresButton.IsEnabled = canEdit;
        ProviderButton.IsEnabled = !_creatingStory && !_loadingProject;
        InputBox.IsEnabled = canEdit;
        if (_generationCts is null)
            SendButton.IsEnabled = canEdit && CanSubmitCurrentStoryAction();
        StoryNavPanel.IsEnabled = canEdit;
        // Keep Inspector chrome and Close available while a slow API request runs.
        // Only mutation controls are disabled; it must never look like a frozen dialog.
        InspectorPanel.IsEnabled = hasProject && !_creatingStory && !_loadingProject;
        InspectorMemoryView.IsEnabled = canEdit;
        InspectorSettingsView.IsEnabled = canEdit;
        UpdateRetryLastUi();

        if (hasProject)
        {
            ProjectWelcomeTitle.Text = _activeProject!.Title;
        }
        else
        {
            ProjectWelcomeTitle.Text = string.Empty;
        }

        UpdateComposerHint();
        UpdateProjectSelectorUi();
        UpdateLatestJumpUi();
    }

    private void UpdateProjectSummaryUi()
    {
        if (_activeProject is null)
        {
            SingleProjectText.Text = string.Empty;
            CurrentProjectGenreText.Text = string.Empty;
            CurrentProjectMetaText.Text = string.Empty;
            CharactersCountText.Text = "0";
            ContextCharactersText.Text = "Characters  0";
            ContextCanonText.Text = "Canon  0";
            ContextTimelineText.Text = "Timeline  0";
            ContextLoreText.Text = "Lore  0";
            ContextChaptersText.Text = "Chapters  0";
            return;
        }

        SingleProjectText.Text = _activeProject.Title;
        ProjectWelcomeTitle.Text = _activeProject.Title;

        string genre = _activeProject.CoreGenres.Count == 0
            ? "Genres can grow through Chat"
            : string.Join(" · ", _activeProject.CoreGenres.Take(2));
        if (_activeProject.CoreGenres.Count > 2)
            genre += $" +{_activeProject.CoreGenres.Count - 2}";
        CurrentProjectGenreText.Text = genre;

        int characters = _memory.Characters.Count;
        int chapters = _memory.Chapters.Count;
        int canon = _memory.Canon.Count(x => !IsRetiredCanon(x.Status));
        int timeline = _memory.Timeline.Count;
        int lore = _memory.Lore.Count;
        CurrentProjectMetaText.Text = $"{characters} chars · ch. {chapters}";
        CharactersCountText.Text = characters.ToString();

        ContextCharactersText.Text = $"Characters  {characters}";
        ContextCanonText.Text = $"Canon  {canon}";
        ContextTimelineText.Text = $"Timeline  {timeline}";
        ContextLoreText.Text = $"Lore  {lore}";
        ContextChaptersText.Text = $"Chapters  {chapters}";
    }

    private void AddSessionSeparator(DateTimeOffset firstVisibleUtc)
    {
        var row = new Grid { Margin = new Thickness(0, 0, 0, 22), MaxWidth = 900 };
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

        var left = new Border
        {
            Height = 1,
            Background = (SolidColorBrush)FindResource("StoryDivider"),
            VerticalAlignment = VerticalAlignment.Center
        };
        DateTimeOffset firstMessage = firstVisibleUtc.ToLocalTime();
        string dayLabel = firstMessage.Date == DateTimeOffset.Now.Date ? "Today" : firstMessage.ToString("MMM d");
        var label = new TextBlock
        {
            Text = $"{dayLabel} · {_activeProject?.Title ?? "Story"}",
            Foreground = (SolidColorBrush)FindResource("StoryMuted"),
            FontSize = 9.5,
            Margin = new Thickness(12, 0, 12, 0),
            VerticalAlignment = VerticalAlignment.Center
        };
        var right = new Border
        {
            Height = 1,
            Background = (SolidColorBrush)FindResource("StoryDivider"),
            VerticalAlignment = VerticalAlignment.Center
        };
        Grid.SetColumn(left, 0);
        Grid.SetColumn(label, 1);
        Grid.SetColumn(right, 2);
        row.Children.Add(left);
        row.Children.Add(label);
        row.Children.Add(right);
        MessagesPanel.Children.Add(row);
    }
}
