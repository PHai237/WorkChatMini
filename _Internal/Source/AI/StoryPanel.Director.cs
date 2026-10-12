using System.Windows;
using System.Windows.Controls;

namespace WorkChatMini;

public partial class StoryPanel
{
    private readonly StoryDirectorPlanner _directorPlanner = new();
    private StoryDirectorProposal? _directorProposal;
    private string? _directorPreviewProjectId;
    private string _directorPreviewInstruction = string.Empty;
    private string? _directorApprovedDirection;
    private bool _directorWriteFreelyOnce;

    // The preview is not a message, chapter, creative direction or saved canon.
    private void ClearDirectorPreview()
    {
        _directorProposal = null;
        _directorPreviewProjectId = null;
        _directorPreviewInstruction = string.Empty;
        _directorApprovedDirection = null;
        _directorWriteFreelyOnce = false;
        DirectorPanel.Visibility = Visibility.Collapsed;
        DirectorOptionA.IsEnabled = false;
        DirectorOptionB.IsEnabled = false;
        DirectorOptionC.IsEnabled = false;
        DirectorReviewBox.Clear();
    }

    private bool NeedsDirectorReview(string prompt, StoryActionType action) =>
        action == StoryActionType.ContinueStory && _activeProject is not null &&
        StoryDirector.Normalize(_activeProject.StoryDirectorMode) == StoryDirector.Review &&
        !_directorWriteFreelyOnce &&
        (_directorApprovedDirection is null || _directorPreviewProjectId != _activeProject.Id ||
         _directorPreviewInstruction != prompt);

    private async Task PreviewDirectorAsync(string prompt)
    {
        if (_activeProject is null || _settings is null || _generationCts is not null)
            return;
        StoryProject project = _activeProject;
        IStoryAiProvider provider = StoryProviderFactory.Get(_settings.StoryProvider);
        if (!StoryCredentialStore.HasKey(provider.Id))
        {
            OpenInspector("Settings");
            SetStatus("Error · Connect an AI provider first.");
            return;
        }
        // Keep the composer text in place. The user may still discard this plan
        // without ever creating a conversation turn or a draft.
        ClearDirectorPreview();
        _generationCts = new CancellationTokenSource();
        SetGeneratingState(true);
        SetStatus("Story Director · considering three next-chapter directions…");
        try
        {
            string context = await BuildStoryMemoryContextAsync(prompt, StoryActionType.ContinueStory, _generationCts.Token);
            StoryDirectorProposal proposals = await _directorPlanner.PreviewAsync(project, prompt, context,
                _settings.StoryProvider, _settings.StoryModel, _generationCts.Token);
            if (_activeProject?.Id != project.Id)
                return;
            _directorProposal = proposals;
            _directorPreviewProjectId = project.Id;
            _directorPreviewInstruction = prompt;
            DirectorOptionA.IsEnabled = DirectorOptionB.IsEnabled = DirectorOptionC.IsEnabled = true;
            DirectorReviewBox.Text = proposals.OptionA;
            DirectorPanel.Visibility = Visibility.Visible;
            SetStatus("Story Director · choose, edit, or write freely. Nothing has been committed.");
        }
        catch (OperationCanceledException)
        {
            SetStatus("Story Director preview cancelled · no chapter or memory changed.");
        }
        catch (Exception ex)
        {
            // Leave an explicit free-writing exit even if the preview API fails.
            _directorPreviewProjectId = project.Id;
            DirectorPanel.Visibility = Visibility.Visible;
            AppLogger.Error("STORY-DIRECTOR", "Unable to preview direction", ex);
            SetStatus("Error · No directions generated. Use Write freely, or discard and try again.");
        }
        finally
        {
            _generationCts.Dispose();
            _generationCts = null;
            SetGeneratingState(false);
        }
    }

    private void DirectorOption_Click(object sender, RoutedEventArgs e)
    {
        if (_directorProposal is null || sender is not System.Windows.Controls.Button button || button.Tag is not string option)
            return;
        DirectorReviewBox.Text = option switch
        {
            "B" => _directorProposal.OptionB,
            "C" => _directorProposal.OptionC,
            _ => _directorProposal.OptionA
        };
        DirectorReviewBox.Focus();
    }

    private async void DirectorApprove_Click(object sender, RoutedEventArgs e)
    {
        if (_generationCts is not null || _directorProposal is null ||
            _activeProject?.Id != _directorPreviewProjectId ||
            _selectedAction != StoryActionType.ContinueStory)
        {
            SetStatus("Story Director · select Continue Story again to review a direction.");
            return;
        }
        if ((InputBox.Text.Trim().Length == 0 ? "Continue the story." : InputBox.Text.Trim()) != _directorPreviewInstruction)
        {
            SetStatus("Story Director · your instruction changed; preview a new direction before approval.");
            return;
        }
        string approved = DirectorReviewBox.Text.Trim();
        if (!StoryDirector.IsValidOption(approved))
        {
            SetStatus("Story Director · write a meaningful direction or use Write freely.");
            return;
        }
        _directorApprovedDirection = approved.Length <= StoryDirector.MaxDirectionCharacters
            ? approved : approved[..StoryDirector.MaxDirectionCharacters];
        DirectorPanel.Visibility = Visibility.Collapsed;
        await SendAsync();
    }

    private async void DirectorFreely_Click(object sender, RoutedEventArgs e)
    {
        if (_generationCts is not null || _activeProject?.Id != _directorPreviewProjectId)
            return;
        _directorWriteFreelyOnce = true;
        _directorApprovedDirection = null;
        DirectorPanel.Visibility = Visibility.Collapsed;
        SetSelectedStoryAction(StoryActionType.ContinueStory);
        await SendAsync();
    }

    private void DirectorDismiss_Click(object sender, RoutedEventArgs e)
    {
        ClearDirectorPreview();
        SetStatus("Story Director · preview discarded; no chapter or memory changed.");
    }

    private async void SettingsNarrativeFocusBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingInspector || _activeProject is null ||
            SettingsNarrativeFocusBox.SelectedItem is not string focus)
            return;
        StoryProject project = _activeProject;
        string? old = project.NarrativeFocusPreference;
        project.NarrativeFocusPreference = StoryNarrativeSteering.Normalize(focus) == StoryNarrativeSteering.Balanced
            ? null : StoryNarrativeSteering.Normalize(focus);
        try
        {
            await _store.SaveProjectAsync(project);
            ClearDirectorPreview();
        }
        catch (Exception ex)
        {
            project.NarrativeFocusPreference = old;
            AppLogger.Error("STORY-DIRECTOR", "Unable to save narrative preference", ex);
            SetStatus("Error · Writing preference was not saved.");
        }
    }

    private async void SaveNarrativeGuardrails_Click(object sender, RoutedEventArgs e)
    {
        if (_activeProject is null || _generationCts is not null || _loadingProject)
            return;
        StoryProject project = _activeProject;
        string? old = project.NarrativeGuardrails;
        string value = SettingsNarrativeGuardrailsBox.Text.Trim();
        project.NarrativeGuardrails = value.Length == 0 ? null : value[..Math.Min(value.Length, 480)];
        try
        {
            await _store.SaveProjectAsync(project);
            ClearDirectorPreview();
            SetStatus("Narrative direction saved · this guides future Continue Story only, not canon.");
        }
        catch (Exception ex)
        {
            project.NarrativeGuardrails = old;
            AppLogger.Error("STORY-DIRECTOR", "Unable to save narrative guardrails", ex);
            SetStatus("Error · Writing direction was not saved.");
        }
    }

    private async void SettingsCraftFeedbackBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingInspector || _activeProject is null || _loadingProject ||
            _generationCts is not null || SettingsCraftFeedbackBox.SelectedItem is not string selected)
            return;
        StoryProject project = _activeProject;
        string? previous = project.ChapterCraftFeedback;
        string normalized = StoryCraftFeedback.Normalize(selected);
        project.ChapterCraftFeedback = normalized == StoryCraftFeedback.None ? null : normalized;
        try
        {
            await _store.SaveProjectAsync(project);
            ClearDirectorPreview();
            SetStatus("Craft feedback saved · future Continue Story only; confirmed canon unchanged.");
        }
        catch (Exception ex)
        {
            project.ChapterCraftFeedback = previous;
            AppLogger.Error("STORY-CRAFT", "Failed to save creative feedback", ex);
            SetStatus("Error · Craft feedback was not saved.");
        }
    }

    private async void SettingsDirectorBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingInspector || _activeProject is null ||
            SettingsDirectorBox.SelectedItem is not string mode)
            return;
        StoryProject project = _activeProject;
        string? previous = project.StoryDirectorMode;
        project.StoryDirectorMode = StoryDirector.Normalize(mode) == StoryDirector.Off
            ? null : StoryDirector.Review;
        ClearDirectorPreview();
        try { await _store.SaveProjectAsync(project); }
        catch (Exception ex)
        {
            project.StoryDirectorMode = previous;
            AppLogger.Error("STORY-DIRECTOR", "Could not save Director setting", ex);
            SetStatus("Error · Story Director setting was not saved.");
        }
    }
}
