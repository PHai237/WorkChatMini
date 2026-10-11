using System.Diagnostics;
using System.Windows;
using System.Windows.Documents;

namespace WorkChatMini;

public partial class StoryPanel
{
    private readonly StoryResearchStore _researchStore = new();
    private StoryResearchDocument _researchDocument = new();

    private async Task LoadStoryResearchAsync(StoryProject project, CancellationToken cancellationToken = default)
    {
        _researchDocument = await _researchStore.LoadAsync(project.Id, cancellationToken);
        UpdateResearchNavUi();
        if (string.Equals(_activeInspectorSection, "Research", StringComparison.OrdinalIgnoreCase))
        {
            if (_researchDocument.Notes.Count > 0)
                RenderMemoryInspector("Research");
            else
                OpenInspector("Overview");
        }
    }

    private void ResetStoryResearch()
    {
        _researchDocument = new StoryResearchDocument();
        UpdateResearchNavUi();
    }

    private void UpdateResearchNavUi()
    {
        if (ResearchNavButton is null)
            return;
        bool hasResearch = _activeProject is not null && _researchDocument.Notes.Count > 0;
        ResearchNavButton.Visibility = hasResearch ? Visibility.Visible : Visibility.Collapsed;
        ResearchNavButton.ToolTip = hasResearch
            ? $"{_researchDocument.Notes.Count} cached research note(s). Research never overrides story canon."
            : null;
    }

    private void RenderResearchInspectorBody()
    {
        InspectorMemoryBodyText.Inlines.Clear();
        if (_researchDocument.Notes.Count == 0)
        {
            InspectorMemoryBodyText.Text = "No external research has been cached for this story.";
            return;
        }

        AddResearchRun("Research is used only when real-world accuracy materially helps. These notes are reference material, not story canon.\n\n", true);
        foreach (StoryResearchNote note in _researchDocument.Notes.OrderByDescending(n => n.AccessedUtc).Take(24))
        {
            AddResearchRun(note.Query + "\n", true);
            AddResearchRun($"{note.Confidence} confidence · {note.AccessedUtc.ToLocalTime():yyyy-MM-dd HH:mm}\n");
            AddResearchRun(note.KeyFact.Trim() + "\n");
            if (!string.IsNullOrWhiteSpace(note.Relevance))
                AddResearchRun("Why it mattered · " + note.Relevance.Trim() + "\n");

            foreach (StoryResearchSource source in note.Sources.Take(8))
            {
                if (!StoryResearchStore.IsSafeSourceUrl(source.Url) ||
                    !Uri.TryCreate(source.Url, UriKind.Absolute, out Uri? uri))
                {
                    continue;
                }
                var link = new Hyperlink(new Run(string.IsNullOrWhiteSpace(source.Title) ? source.Url : source.Title))
                {
                    NavigateUri = uri,
                    Foreground = (System.Windows.Media.Brush)FindResource("StoryGold")
                };
                link.RequestNavigate += (_, e) =>
                {
                    try { Process.Start(new ProcessStartInfo(e.Uri.AbsoluteUri) { UseShellExecute = true }); }
                    catch (Exception ex) { AppLogger.Warn("STORY-RESEARCH", $"Could not open source URL | {ex.Message}"); }
                    e.Handled = true;
                };
                InspectorMemoryBodyText.Inlines.Add(new Run("Source · "));
                InspectorMemoryBodyText.Inlines.Add(link);
                InspectorMemoryBodyText.Inlines.Add(new LineBreak());
            }
            InspectorMemoryBodyText.Inlines.Add(new LineBreak());
        }
    }

    private void AddResearchRun(string text, bool emphasized = false)
    {
        var run = new Run(text);
        if (emphasized)
        {
            run.FontWeight = FontWeights.SemiBold;
            run.Foreground = (System.Windows.Media.Brush)FindResource("StoryText");
        }
        InspectorMemoryBodyText.Inlines.Add(run);
    }
}
