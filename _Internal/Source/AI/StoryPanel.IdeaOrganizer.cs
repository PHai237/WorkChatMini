using System.Windows;
using System.Windows.Controls;

namespace WorkChatMini;

public partial class StoryPanel
{
    private string _ideaSearch = string.Empty;
    private string _ideaStatus = StoryIdeaOrganizer.All;
    private string _ideaCategory = StoryIdeaOrganizer.All;

    private void InitializeIdeaOrganizerUi()
    {
        InspectorIdeaStatus.ItemsSource = StoryIdeaOrganizer.StatusFilters;
        InspectorIdeaCategory.ItemsSource = StoryIdeaOrganizer.CategoryFilters;
        InspectorIdeaStatus.SelectedItem = StoryIdeaOrganizer.All;
        InspectorIdeaCategory.SelectedItem = StoryIdeaOrganizer.All;
    }

    private void InspectorIdeaSearch_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (InspectorIdeasToolbar is null) return;
        _ideaSearch = InspectorIdeaSearch.Text;
        _visibleCreativeIdeas = IdeasPerPage;
        if (_activeInspectorSection == "Ideas") RenderCoCreationInspector();
    }

    private void InspectorIdeaFilter_Changed(object sender, SelectionChangedEventArgs e)
    {
        if (InspectorIdeaStatus is null || InspectorIdeaCategory is null) return;
        _ideaStatus = InspectorIdeaStatus.SelectedItem as string ?? StoryIdeaOrganizer.All;
        _ideaCategory = InspectorIdeaCategory.SelectedItem as string ?? StoryIdeaOrganizer.All;
        _visibleCreativeIdeas = IdeasPerPage;
        if (_activeInspectorSection == "Ideas") RenderCoCreationInspector();
    }

    private void ResetIdeaOrganizerUi()
    {
        _ideaSearch = string.Empty;
        _ideaStatus = StoryIdeaOrganizer.All;
        _ideaCategory = StoryIdeaOrganizer.All;
        InspectorIdeaSearch.Clear();
        InspectorIdeaStatus.SelectedItem = StoryIdeaOrganizer.All;
        InspectorIdeaCategory.SelectedItem = StoryIdeaOrganizer.All;
    }
}
