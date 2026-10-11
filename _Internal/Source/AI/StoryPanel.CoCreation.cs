using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using WpfButton = System.Windows.Controls.Button;
using WpfTextBox = System.Windows.Controls.TextBox;
using WpfComboBox = System.Windows.Controls.ComboBox;

namespace WorkChatMini;

public partial class StoryPanel
{
    private const int IdeasPerPage = 40;
    private int _visibleCreativeIdeas = IdeasPerPage;

    // Small, modeless action: only the creator can make an idea durable.
    private void AttachCoCreationActions(StackPanel content, StoryMessage message)
    {
        if (message.IsInterruptedResponse || message.Action != StoryActionType.Chat.ToString() ||
            string.IsNullOrWhiteSpace(message.Content))
            return;

        var root = new StackPanel { Margin = new Thickness(0, 10, 0, 0), Tag = message };
        var editButton = new WpfButton
        {
            Style = (Style)FindResource("StoryInlineActionButton"),
            Content = string.IsNullOrWhiteSpace(message.CreativeDirection) || message.CreativeDirectionArchived
                ? "✧ Keep direction" : "✦ Edit direction",
            ToolTip = "Save one agreed creative direction. Brainstorming is not canon.",
            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
            Padding = new Thickness(8, 4, 8, 4)
        };
        root.Children.Add(editButton);

        if (!string.IsNullOrWhiteSpace(message.CreativeDirection) && !message.CreativeDirectionArchived)
        {
            root.Children.Add(new TextBlock
            {
                Text = "✦ " + StoryIdeaOrganizer.NormalizeCategory(message.CreativeDirectionCategory) +
                    " · Working direction · " + message.CreativeDirection,
                Foreground = (SolidColorBrush)FindResource("StorySub"),
                FontSize = 10.5, TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 5, 0, 0)
            });
        }

        // Editors and the optional suggestion list are lazy: rendering old messages
        // does not allocate a TextBox or dozens of off-screen controls.
        StackPanel? editor = null;
        WpfTextBox? editorInput = null;
        WpfComboBox? replaceChoice = null;
        WpfComboBox? categoryChoice = null;
        void OpenEditor(string? suggestedDirection)
        {
            if (editor is null)
            {
                var created = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 8, 0, 0) };
                editor = created;
                created.Children.Add(new TextBlock
                {
                    Text = "WORKING DIRECTION · edit before saving · not canon",
                    Foreground = (SolidColorBrush)FindResource("StoryMuted"), FontSize = 10,
                    Margin = new Thickness(0, 0, 0, 5)
                });
                editorInput = new WpfTextBox
                {
                    // Never prefill a saved direction with a truncated Gemini reply.
                    // The creator must deliberately phrase the idea to keep.
                    Text = !string.IsNullOrWhiteSpace(message.CreativeDirection)
                        ? message.CreativeDirection
                        : string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase)
                            ? string.Empty : StoryCoCreation.NormalizeDirection(message.Content),
                    Style = (Style)FindResource("StoryTextBox"),
                    TextWrapping = TextWrapping.Wrap, AcceptsReturn = true,
                    MinHeight = 55, MaxHeight = 110, MaxLength = StoryCoCreation.MaxDirectionLength,
                    VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                    ToolTip = "Write one short, creator-approved direction. Never paste entire Gemini replies as canon."
                };
                created.Children.Add(editorInput);
                categoryChoice = new WpfComboBox
                {
                    ItemsSource = StoryIdeaOrganizer.Categories,
                    SelectedItem = StoryIdeaOrganizer.NormalizeCategory(message.CreativeDirectionCategory),
                    Margin = new Thickness(0, 7, 0, 0), MinHeight = 32,
                    Style = (Style)FindResource("StoryCombo"),
                    ToolTip = "Optional editorial category; not story canon"
                };
                created.Children.Add(categoryChoice);
                replaceChoice = new WpfComboBox
                {
                    Margin = new Thickness(0, 7, 0, 0), MinHeight = 32,
                    Style = (Style)FindResource("StoryCombo"),
                    ToolTip = "Optional: replace an earlier approved direction. The old one is archived, not deleted."
                };
                created.Children.Add(replaceChoice);
                var actions = new StackPanel
                {
                    Orientation = System.Windows.Controls.Orientation.Horizontal,
                    Margin = new Thickness(0, 7, 0, 0)
                };
                var save = new WpfButton { Content = "Keep idea", Style = (Style)FindResource("StoryAccentButton") };
                var cancel = new WpfButton
                {
                    Content = "Cancel", Style = (Style)FindResource("StoryButton"),
                    Margin = new Thickness(8, 0, 0, 0)
                };
                save.Click += async (_, _) =>
                {
                    if (_activeProject is null || _generationCts is not null || _storageAuditBusy ||
                        _checkpointRecoveryFailed || !_messages.Contains(message) || editorInput is null) return;
                    string direction = StoryCoCreation.NormalizeDirection(editorInput.Text);
                    if (direction.Length == 0) return;
                    StoryMessage? replaced = (replaceChoice?.SelectedItem as ComboBoxItem)?.Tag as StoryMessage;
                    StoryMessage[] targets = replaced is null ? new[] { message } : new[] { message, replaced };
                    string category = categoryChoice?.SelectedItem as string ?? StoryIdeaOrganizer.General;
                    await CommitCreativeDecisionAsync(targets, () => replaced is null
                        ? StoryCreativeDecision.Revise(message, direction, DateTimeOffset.UtcNow, category)
                        : StoryCreativeDecision.Replace(message, replaced, direction, DateTimeOffset.UtcNow, category),
                        "Could not save creative direction");
                };
                cancel.Click += (_, _) => created.Visibility = Visibility.Collapsed;
                actions.Children.Add(save);
                actions.Children.Add(cancel);
                created.Children.Add(actions);
                root.Children.Add(created);
            }
            if (editor is null || editorInput is null) return;
            if (replaceChoice is not null)
            {
                replaceChoice.Items.Clear();
                replaceChoice.Items.Add(new ComboBoxItem { Content = "Keep as a separate idea" });
                foreach (StoryMessage earlier in StoryCoCreation.ActiveDirections(_messages))
                {
                    if (ReferenceEquals(earlier, message)) continue;
                    string summary = earlier.CreativeDirection;
                    replaceChoice.Items.Add(new ComboBoxItem
                    {
                        Content = "Replace: " + (summary.Length > 68 ? summary[..68] + "…" : summary),
                        ToolTip = summary, Tag = earlier
                    });
                }
                replaceChoice.Visibility = replaceChoice.Items.Count > 1
                    ? Visibility.Visible : Visibility.Collapsed;
                replaceChoice.SelectedIndex = 0;
            }
            if (suggestedDirection is not null)
            {
                editorInput.Text = suggestedDirection;
                editor.Visibility = Visibility.Visible;
            }
            else
            {
                editor.Visibility = editor.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
            }
            if (editor.Visibility == Visibility.Visible)
            {
                editorInput.Focus();
                editorInput.CaretIndex = editorInput.Text.Length;
            }
        }
        editButton.Click += (_, _) => OpenEditor(null);

        if (string.Equals(message.Role, "assistant", StringComparison.OrdinalIgnoreCase))
        {
            IReadOnlyList<StoryCreativeSuggestion> suggestions = StoryCreativeSuggestionExtractor.ExtractDetailed(message.Content);
            if (suggestions.Count > 0)
            {
                var review = new WpfButton
                {
                    Content = $"Review {suggestions.Count} proposed ideas",
                    Style = (Style)FindResource("StoryInlineActionButton"),
                    ToolTip = "Suggestions from Gemini, not saved. Select one to edit and approve.",
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                    Margin = new Thickness(0, 4, 0, 0)
                };
                var choices = new StackPanel { Visibility = Visibility.Collapsed, Margin = new Thickness(0, 5, 0, 0) };
                review.Click += (_, _) => choices.Visibility = choices.Visibility == Visibility.Visible
                    ? Visibility.Collapsed : Visibility.Visible;
                root.Children.Add(review);
                root.Children.Add(choices);
                // Build the small suggestion list only if the creator asks to view it.
                review.Click += (_, _) =>
                {
                    if (choices.Children.Count != 0) return;
                    for (int i = 0; i < suggestions.Count; i++)
                    {
                        StoryCreativeSuggestion suggestion = suggestions[i];
                        string candidate = suggestion.Text;
                        string label = suggestion.Number is int number ? $"Option {number}" : $"Idea {i + 1}";
                        var choice = new WpfButton
                        {
                            Content = new TextBlock
                            {
                                Text = label + " · " + candidate, TextWrapping = TextWrapping.Wrap,
                                MaxWidth = 405, Foreground = (SolidColorBrush)FindResource("StorySub")
                            },
                            Style = (Style)FindResource("StoryInlineActionButton"),
                            HorizontalAlignment = System.Windows.HorizontalAlignment.Stretch,
                            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left,
                            Margin = new Thickness(0, 3, 0, 0),
                            ToolTip = "Edit and approve; not saved until Keep idea"
                        };
                        choice.Click += (_, _) =>
                        {
                            OpenEditor(candidate);
                            choices.Visibility = Visibility.Collapsed;
                        };
                        bool declined = message.DeclinedCreativeSuggestions?.Contains(candidate,
                            StringComparer.OrdinalIgnoreCase) == true;
                        bool alreadyApproved = StoryCoCreation.ActiveDirections(_messages)
                            .Any(m => string.Equals(StoryCoCreation.NormalizeDirection(m.CreativeDirection),
                                candidate, StringComparison.OrdinalIgnoreCase));
                        var pass = new WpfButton
                        {
                            Content = declined ? "Undo pass" : "Pass",
                            Style = (Style)FindResource("StoryInlineActionButton"),
                            Margin = new Thickness(6, 3, 0, 0),
                            IsEnabled = !alreadyApproved || declined,
                            ToolTip = alreadyApproved && !declined
                                ? "Already approved. Archive the active direction before declining it."
                                : "Only an explicit Pass is remembered. Skipping an idea does not reject it."
                        };
                        pass.Click += async (_, _) => await CommitCreativeDecisionAsync(new[] { message },
                            () => StoryCreativeDecision.SetDeclined(message, candidate, !declined),
                            "Could not update declined suggestion");
                        var option = new DockPanel();
                        System.Windows.Controls.DockPanel.SetDock(pass, System.Windows.Controls.Dock.Right);
                        option.Children.Add(pass);
                        option.Children.Add(choice);
                        choices.Children.Add(option);
                    }
                };
            }
        }
        content.Children.Add(root);
    }

    // Refresh only the modified bubble. Rebuilding hundreds of old messages on each
    // pin/unpin would cause unnecessary layout work on low-power laptops.
    private void RefreshCoCreationUi(StoryMessage changed)
    {
        foreach (Grid row in MessagesPanel.Children.OfType<Grid>())
        {
            foreach (Border bubble in row.Children.OfType<Border>())
            {
                if (bubble.Child is not StackPanel body) continue;
                StackPanel? existing = body.Children.OfType<StackPanel>()
                    .FirstOrDefault(panel => ReferenceEquals(panel.Tag, changed));
                if (existing is null) continue;
                int position = body.Children.IndexOf(existing);
                body.Children.Remove(existing);
                AttachCoCreationActions(body, changed);
                if (body.Children.Count > position)
                {
                    var replacement = body.Children[body.Children.Count - 1];
                    body.Children.RemoveAt(body.Children.Count - 1);
                    body.Children.Insert(position, replacement);
                }
                break;
            }
        }
        if (_activeInspectorSection == "Ideas") RenderMemoryInspector("Ideas");
    }

    private void RenderCoCreationInspector()
    {
        InspectorIdeasList.Children.Clear();
        var notes = _messages.Where(m => StoryIdeaOrganizer.Matches(
                m, _ideaSearch, _ideaStatus, _ideaCategory))
            .OrderBy(m => m.CreativeDirectionArchived)
            .ThenByDescending(m => m.CreatedUtc).ToArray();
        int passedMatches = StoryCreativeDecision.ActiveDeclines(_messages).Count(idea =>
            StoryIdeaOrganizer.MatchesDecline(idea, _ideaSearch, _ideaStatus, _ideaCategory));
        InspectorIdeaCount.Text = $"{notes.Length} directions · {passedMatches} passed ideas";
        if (notes.Length == 0 && passedMatches == 0)
        {
            InspectorIdeasList.Children.Add(new TextBlock
            {
                Text = string.IsNullOrWhiteSpace(_ideaSearch) && _ideaStatus == StoryIdeaOrganizer.All
                    ? "No directions saved yet. Discuss ideas in Chat, then use Keep direction to approve one."
                    : "No ideas match the current filters.",
                TextWrapping = TextWrapping.Wrap,
                Foreground = (SolidColorBrush)FindResource("StorySub"), FontSize = 11,
                Margin = new Thickness(0, 0, 0, 12)
            });
        }
        foreach (StoryMessage note in notes.Take(_visibleCreativeIdeas))
        {
            var body = new StackPanel();
            body.Children.Add(new TextBlock
            {
                Text = note.CreativeDirection, TextWrapping = TextWrapping.Wrap,
                Foreground = (SolidColorBrush)FindResource("StoryText"), FontSize = 12
            });
            body.Children.Add(new TextBlock
            {
                Text = (note.CreativeDirectionArchived ? "ARCHIVED" : "WORKING DIRECTION") +
                    " · " + StoryIdeaOrganizer.NormalizeCategory(note.CreativeDirectionCategory) +
                    " · " + note.CreatedUtc.ToLocalTime().ToString("dd MMM HH:mm"),
                FontSize = 9.5, Foreground = (SolidColorBrush)FindResource("StoryMuted"),
                Margin = new Thickness(0, 8, 0, 0)
            });
            var commands = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(0, 9, 0, 0)
            };
            var edit = new WpfButton
            {
                Content = "Edit", Style = (Style)FindResource("StoryButton")
            };
            commands.Children.Add(edit);
            var toggle = new WpfButton
            {
                Content = note.CreativeDirectionArchived ? "Restore idea" : "Archive idea",
                Style = (Style)FindResource("StoryButton"),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                Margin = new Thickness(7, 0, 0, 0)
            };
            toggle.Click += async (_, _) =>
            {
                if (_activeProject is null || _generationCts is not null || _storageAuditBusy ||
                    _checkpointRecoveryFailed || !_messages.Contains(note)) return;
                await CommitCreativeDecisionAsync(new[] { note }, () =>
                {
                    note.CreativeDirectionArchived = !note.CreativeDirectionArchived;
                    return true;
                }, "Could not archive creative direction");
            };
            commands.Children.Add(toggle);
            body.Children.Add(commands);

            // A direction is mutable editorial intent, not immutable story canon.
            // Editing it in place avoids stacking contradictory old/new notes.
            var editPanel = new StackPanel
            {
                Visibility = Visibility.Collapsed, Margin = new Thickness(0, 10, 0, 0)
            };
            var input = new WpfTextBox
            {
                Text = note.CreativeDirection,
                MaxLength = StoryCoCreation.MaxDirectionLength,
                Style = (Style)FindResource("StoryTextBox"),
                AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
                MinHeight = 65, MaxHeight = 115,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto
            };
            editPanel.Children.Add(input);
            var categoryEdit = new WpfComboBox
            {
                ItemsSource = StoryIdeaOrganizer.Categories,
                SelectedItem = StoryIdeaOrganizer.NormalizeCategory(note.CreativeDirectionCategory),
                Margin = new Thickness(0, 7, 0, 0), MinHeight = 32,
                Style = (Style)FindResource("StoryCombo"),
                ToolTip = "Organize this direction; no changes to canon"
            };
            editPanel.Children.Add(categoryEdit);
            var editActions = new StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                Margin = new Thickness(0, 7, 0, 0)
            };
            var saveEdit = new WpfButton
            {
                Content = "Save direction", Style = (Style)FindResource("StoryAccentButton")
            };
            saveEdit.Click += async (_, _) =>
            {
                if (_activeProject is null || _generationCts is not null || _storageAuditBusy ||
                    _checkpointRecoveryFailed || !_messages.Contains(note)) return;
                string replacement = StoryCoCreation.NormalizeDirection(input.Text);
                if (replacement.Length == 0) return;
                await CommitCreativeDecisionAsync(new[] { note }, () =>
                    StoryCreativeDecision.Revise(note, replacement, DateTimeOffset.UtcNow,
                        categoryEdit.SelectedItem as string ?? StoryIdeaOrganizer.General),
                    "Could not revise creative direction");
            };
            var cancelEdit = new WpfButton
            {
                Content = "Cancel", Style = (Style)FindResource("StoryButton"),
                Margin = new Thickness(7, 0, 0, 0)
            };
            cancelEdit.Click += (_, _) => editPanel.Visibility = Visibility.Collapsed;
            editActions.Children.Add(saveEdit);
            editActions.Children.Add(cancelEdit);
            editPanel.Children.Add(editActions);
            body.Children.Add(editPanel);
            List<StoryCreativeRevision>? revisions = note.CreativeDirectionRevisions;
            if (revisions is { Count: > 0 })
            {
                var history = new WpfButton
                {
                    Content = $"Revision history · {revisions.Count}",
                    Style = (Style)FindResource("StoryInlineActionButton"),
                    HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                    Margin = new Thickness(0, 7, 0, 0)
                };
                var historyList = new StackPanel { Visibility = Visibility.Collapsed };
                history.Click += (_, _) =>
                {
                    historyList.Visibility = historyList.Visibility == Visibility.Visible
                        ? Visibility.Collapsed : Visibility.Visible;
                    if (historyList.Children.Count != 0) return;
                    foreach (StoryCreativeRevision revision in revisions.AsEnumerable().Reverse().Take(12))
                    {
                        string former = revision.PreviousText;
                        var restore = new WpfButton
                        {
                            Content = "Restore · " + (former.Length > 75 ? former[..75] + "…" : former),
                            ToolTip = former + " · " + revision.ChangedUtc.ToLocalTime().ToString("g"),
                            Style = (Style)FindResource("StoryInlineActionButton"),
                            HorizontalAlignment = System.Windows.HorizontalAlignment.Left,
                            HorizontalContentAlignment = System.Windows.HorizontalAlignment.Left,
                            Margin = new Thickness(0, 3, 0, 0)
                        };
                        restore.Click += async (_, _) => await CommitCreativeDecisionAsync(new[] { note },
                            () => StoryCreativeDecision.Revise(note, former, DateTimeOffset.UtcNow),
                            "Could not restore earlier direction");
                        historyList.Children.Add(restore);
                    }
                };
                body.Children.Add(history);
                body.Children.Add(historyList);
            }
            edit.Click += (_, _) =>
            {
                editPanel.Visibility = editPanel.Visibility == Visibility.Visible
                    ? Visibility.Collapsed : Visibility.Visible;
                if (editPanel.Visibility == Visibility.Visible) input.Focus();
            };
            InspectorIdeasList.Children.Add(new Border
            {
                Background = (SolidColorBrush)FindResource("StorySurface"),
                BorderBrush = (SolidColorBrush)FindResource("StoryBorder"),
                BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(10),
                Padding = new Thickness(12), Margin = new Thickness(0, 0, 0, 10), Child = body
            });
        }
        if (notes.Length > _visibleCreativeIdeas)
        {
            var more = new WpfButton
            {
                Content = $"Show older ideas · {notes.Length - _visibleCreativeIdeas} remaining",
                Style = (Style)FindResource("StoryButton"),
                HorizontalAlignment = System.Windows.HorizontalAlignment.Center,
                Margin = new Thickness(0, 4, 0, 12)
            };
            more.Click += (_, _) =>
            {
                _visibleCreativeIdeas += IdeasPerPage;
                RenderCoCreationInspector();
            };
            InspectorIdeasList.Children.Add(more);
        }
        RenderDeclinedCreativeIdeasInspector();
    }
}
