using System.Windows;

namespace WorkChatMini;

public partial class StoryPanel
{
    private void SceneCompassChip_Click(object sender, RoutedEventArgs e) => OpenInspector("Scene Compass");

    private void SceneCompassUseCue_Click(object sender, RoutedEventArgs e)
    {
        if (_activeProject is null || _loadingProject || _checkpointRecoveryFailed ||
            _generationCts is not null || !InputBox.IsEnabled)
            return;
        StorySceneCompassBrief brief = StorySceneCompass.Build(_memory);
        if (!brief.HasCommittedChapter || string.IsNullOrWhiteSpace(brief.SuggestedCue))
        {
            SetStatus("Scene Compass · Commit a chapter before using a continuation cue.");
            return;
        }
        string original = InputBox.Text.Trim();
        // Never overwrite a creator's unsent idea, never send or canonize automatically.
        InputBox.Text = original.Length == 0 ? brief.SuggestedCue :
            original + "\n\n" + brief.SuggestedCue;
        SetSelectedStoryAction(StoryActionType.ContinueStory);
        CloseInspector();
        InputBox.Focus();
        InputBox.CaretIndex = InputBox.Text.Length;
        SetStatus("Scene Compass · Edit this direction, then send when ready. Nothing was committed.");
    }
}
