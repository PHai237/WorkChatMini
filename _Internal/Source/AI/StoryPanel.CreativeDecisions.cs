using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfButton = System.Windows.Controls.Button;

namespace WorkChatMini;

public partial class StoryPanel
{
    private bool _creativeDecisionBusy;

    private sealed record CreativeBefore(
        StoryMessage Message, string Direction, bool Archived, string? Category,
        List<StoryCreativeRevision>? Revisions, List<string>? Declines)
    {
        internal static CreativeBefore Capture(StoryMessage message) => new(
            message, message.CreativeDirection, message.CreativeDirectionArchived, message.CreativeDirectionCategory,
            message.CreativeDirectionRevisions is null ? null :
                new List<StoryCreativeRevision>(message.CreativeDirectionRevisions),
            message.DeclinedCreativeSuggestions is null ? null :
                new List<string>(message.DeclinedCreativeSuggestions));

        internal void Restore()
        {
            Message.CreativeDirection = Direction;
            Message.CreativeDirectionArchived = Archived;
            Message.CreativeDirectionCategory = Category;
            Message.CreativeDirectionRevisions = Revisions;
            Message.DeclinedCreativeSuggestions = Declines;
        }
    }

    // A multi-message replacement/rejection is written as a single conversation
    // save. On persistence failure, restore the in-memory state as well.
    private async Task<bool> CommitCreativeDecisionAsync(
        IReadOnlyList<StoryMessage> affected, Func<bool> apply, string failure)
    {
        if (_creativeDecisionBusy || _loadingProject || _activeProject is null || _generationCts is not null ||
            _storageAuditBusy || _checkpointRecoveryFailed || affected.Count == 0 ||
            affected.Any(m => !_messages.Contains(m))) return false;
        _creativeDecisionBusy = true;
        ProjectBox.IsEnabled = false;
        string projectId = _activeProject.Id;
        StoryMessage[] conversationSnapshot = _messages.ToArray();
        var before = affected.Distinct().Select(CreativeBefore.Capture).ToArray();
        bool persisted = false;
        try
        {
            if (!apply()) return false;
            await _store.SaveMessagesAsync(projectId, conversationSnapshot);
            persisted = true;
            if (_activeProject?.Id == projectId)
            {
                foreach (StoryMessage changed in affected.Distinct())
                    RefreshCoCreationUi(changed);
            }
            return true;
        }
        catch (Exception ex)
        {
            if (!persisted)
                foreach (CreativeBefore saved in before) saved.Restore();
            AppLogger.Error("STORY-IDEAS", failure, ex);
            SetStatus(persisted ? "Idea saved, but the UI could not refresh. Reopen Creative Ideas."
                : "Error · Could not save creative decision. No changes kept.");
            return false;
        }
        finally
        {
            _creativeDecisionBusy = false;
            ProjectBox.IsEnabled = !_loadingProject && _generationCts is null;
        }
    }

    private void RenderDeclinedCreativeIdeasInspector()
    {
        IReadOnlyList<string> declines = StoryCreativeDecision.ActiveDeclines(_messages)
            .Where(item => StoryIdeaOrganizer.MatchesDecline(item, _ideaSearch, _ideaStatus, _ideaCategory))
            .ToArray();
        if (declines.Count == 0) return;
        InspectorIdeasList.Children.Add(new TextBlock
        {
            Text = "PASSED IDEAS · explicit decisions only",
            FontSize = 10.5, FontWeight = FontWeights.SemiBold,
            Foreground = (SolidColorBrush)FindResource("StorySub"),
            Margin = new Thickness(0, 16, 0, 10)
        });
        foreach (string idea in declines)
        {
            var row = new StackPanel { Margin = new Thickness(0, 0, 0, 11) };
            row.Children.Add(new TextBlock
            {
                Text = idea, TextWrapping = TextWrapping.Wrap,
                Foreground = (SolidColorBrush)FindResource("StoryText"), FontSize = 11.5
            });
            var undo = new WpfButton
            {
                Content = "Undo pass", Style = (Style)FindResource("StoryInlineActionButton"),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                Margin = new Thickness(0, 5, 0, 0)
            };
            undo.Click += async (_, _) =>
            {
                // Repeated proposals can be declined on several messages.
                // Undo ALL matching entries so the decision really disappears.
                StoryMessage[] sources = _messages.Where(m => m.DeclinedCreativeSuggestions?.Contains(
                    idea, StringComparer.OrdinalIgnoreCase) == true).ToArray();
                await CommitCreativeDecisionAsync(sources, () =>
                {
                    bool updated = false;
                    foreach (StoryMessage source in sources)
                        updated |= StoryCreativeDecision.SetDeclined(source, idea, false);
                    return updated;
                }, "Could not undo declined suggestion");
            };
            row.Children.Add(undo);
            InspectorIdeasList.Children.Add(new Border
            {
                Child = row, Padding = new Thickness(10),
                CornerRadius = new CornerRadius(8),
                Background = (SolidColorBrush)FindResource("StorySurface"),
                BorderBrush = (SolidColorBrush)FindResource("StoryBorder"),
                BorderThickness = new Thickness(1)
            });
        }
    }
}
