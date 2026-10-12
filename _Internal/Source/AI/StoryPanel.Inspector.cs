using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;
using WpfButton = System.Windows.Controls.Button;
using WpfBrush = System.Windows.Media.Brush;
using WpfBrushConverter = System.Windows.Media.BrushConverter;
using WpfBrushes = System.Windows.Media.Brushes;
using WpfHorizontalAlignment = System.Windows.HorizontalAlignment;
using WpfVerticalAlignment = System.Windows.VerticalAlignment;

namespace WorkChatMini;

public partial class StoryPanel
{
    private bool _creatingStory;
    private bool _loadingInspector;
    private string? _activeInspectorSection;
    private StoryProject? _pendingDeleteProject;
    private readonly HashSet<string> _newStoryCoreGenres = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _newStoryTemporaryTags = new(StringComparer.OrdinalIgnoreCase);

    private void InitializeInspectorUi()
    {
        SettingsProviderBox.ItemsSource = StoryProviderFactory.Providers;
        SettingsAutonomyBox.ItemsSource = StoryAutonomyModes.All;
        SettingsDirectorBox.ItemsSource = StoryDirector.Modes;
        SettingsNarrativeFocusBox.ItemsSource = StoryNarrativeSteering.All;
        SettingsCraftFeedbackBox.ItemsSource = StoryCraftFeedback.All;
        SettingsManuscriptModeBox.ItemsSource = StoryManuscriptModes.All;
        SettingsMaturityBox.ItemsSource = StoryContentProfiles.MaturityLevels;
        SettingsViolenceBox.ItemsSource = StoryContentProfiles.ViolenceLevels;
        SettingsHorrorBox.ItemsSource = StoryContentProfiles.HorrorLevels;
        SettingsIntimacyBox.ItemsSource = StoryContentProfiles.IntimacyLevels;
        BuildNewStoryGenreButtons();
        InitializeIdeaOrganizerUi();
    }

    private void BeginNewStoryEditor()
    {
        SetSelectedStoryAction(StoryActionType.Chat);
        _creatingStory = true;
        _newStoryCoreGenres.Clear();
        _newStoryTemporaryTags.Clear();
        NewStoryTitleBox.Clear();
        NewStoryGenresPanel.Visibility = Visibility.Collapsed;
        BuildNewStoryGenreButtons();
        CloseInspector();
        UpdateStorySurfaceState();
        NewStoryTitleBox.Focus();
    }

    private void ToggleNewStoryGenres_Click(object sender, RoutedEventArgs e)
    {
        NewStoryGenresPanel.Visibility = NewStoryGenresPanel.Visibility == Visibility.Visible
            ? Visibility.Collapsed : Visibility.Visible;
    }

    private void CancelNewStoryButton_Click(object sender, RoutedEventArgs e)
    {
        _creatingStory = false;
        UpdateStorySurfaceState();
    }

    private async void CreateStoryInlineButton_Click(object sender, RoutedEventArgs e)
    {
        string title = NewStoryTitleBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(title))
        {
            SetStatus("Error · Give the story a title first.");
            NewStoryTitleBox.Focus();
            return;
        }

        StoryProject project = await _store.CreateProjectAsync(title);
        project.CoreGenres = _newStoryCoreGenres.OrderBy(x => x).ToList();
        project.TemporaryTags = _newStoryTemporaryTags.OrderBy(x => x).ToList();
        await _store.SaveProjectAsync(project);
        _projects.Insert(0, project);
        ProjectBox.Items.Refresh();
        _creatingStory = false;
        RebuildProjectList();
        ProjectBox.SelectedItem = project;
        SetStatus(null);
    }

    private void BuildNewStoryGenreButtons()
    {
        if (NewStoryCoreGenresPanel is null || NewStoryTemporaryTagsPanel is null)
            return;
        NewStoryCoreGenresPanel.Children.Clear();
        NewStoryTemporaryTagsPanel.Children.Clear();
        foreach (string genre in StoryGenres.All)
        {
            NewStoryCoreGenresPanel.Children.Add(CreateGenreToggle(genre, _newStoryCoreGenres.Contains(genre), NewStoryGenreToggle_Click, "core"));
            NewStoryTemporaryTagsPanel.Children.Add(CreateGenreToggle(genre, _newStoryTemporaryTags.Contains(genre), NewStoryGenreToggle_Click, "temp"));
        }
    }

    private void NewStoryGenreToggle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not ToggleButton button || button.Tag is not string tag)
            return;
        int split = tag.IndexOf('|');
        if (split < 0)
            return;
        string group = tag[..split];
        string genre = tag[(split + 1)..];
        HashSet<string> target = group == "core" ? _newStoryCoreGenres : _newStoryTemporaryTags;
        if (button.IsChecked == true)
            target.Add(genre);
        else
            target.Remove(genre);
        UpdateGenreToggleContent(button, genre);
    }

    private ToggleButton CreateGenreToggle(string genre, bool selected, RoutedEventHandler click, string group)
    {
        var button = new ToggleButton
        {
            Tag = group + "|" + genre,
            IsChecked = selected,
            Style = (Style)FindResource("StoryToggleChip")
        };
        UpdateGenreToggleContent(button, genre);
        button.Click += click;
        return button;
    }

    private static void UpdateGenreToggleContent(ToggleButton button, string genre) =>
        button.Content = button.IsChecked == true ? "✓  " + genre : genre;

    private void RebuildProjectList()
    {
        if (ProjectsListPanel is null)
            return;

        ProjectsListPanel.Children.Clear();
        ProjectsLabel.Visibility = _projects.Count > 0 && !_sidebarCollapsed ? Visibility.Visible : Visibility.Collapsed;
        ProjectsScroll.Visibility = _projects.Count > 0 && !_sidebarCollapsed ? Visibility.Visible : Visibility.Collapsed;

        foreach (StoryProject project in _projects)
        {
            bool active = _activeProject?.Id == project.Id;
            var rowBorder = new Border
            {
                Margin = new Thickness(0, 0, 0, 3),
                CornerRadius = new CornerRadius(6),
                Background = BrushFrom(active ? "#1D1A13" : "#00000000"),
                BorderBrush = BrushFrom(active ? "#C8A84B" : "#00000000"),
                BorderThickness = active ? new Thickness(2, 0, 0, 0) : new Thickness(0),
                ClipToBounds = true
            };
            var row = new Grid();
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            row.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            string genre = project.CoreGenres.Count == 0 ? "Explore genres in Chat" : string.Join(" · ", project.CoreGenres.Take(2));
            var switchButton = new WpfButton
            {
                Tag = project,
                Style = (Style)FindResource("ProjectListButton"),
                Background = BrushFrom(active ? "#1D1A13" : "#00000000"),
                Padding = new Thickness(9, 6, 6, 6),
                HorizontalContentAlignment = WpfHorizontalAlignment.Stretch,
                ToolTip = active ? "Current story" : $"Open {project.Title}",
                Content = new StackPanel
                {
                    Children =
                    {
                        new TextBlock
                        {
                            Text = project.Title,
                            Foreground = BrushFrom(active ? "#F3D77D" : "#E8E0D0"),
                            FontSize = 11.5,
                            FontWeight = active ? FontWeights.SemiBold : FontWeights.Normal,
                            TextTrimming = TextTrimming.CharacterEllipsis
                        },
                        new TextBlock
                        {
                            Text = genre,
                            Foreground = BrushFrom("#756A55"),
                            FontSize = 9.5,
                            Margin = new Thickness(0, 3, 0, 0),
                            TextTrimming = TextTrimming.CharacterEllipsis
                        }
                    }
                }
            };
            switchButton.Click += ProjectListItem_Click;

            var deleteButton = new WpfButton
            {
                Tag = project,
                Content = "×",
                Width = 26,
                Height = 26,
                Margin = new Thickness(0, 4, 4, 4),
                VerticalAlignment = WpfVerticalAlignment.Center,
                Style = (Style)FindResource("StoryButton"),
                BorderThickness = new Thickness(0),
                Foreground = BrushFrom("#9D7A68"),
                ToolTip = $"Delete {project.Title}",
                Visibility = Visibility.Visible,
                Opacity = active ? 0.82 : 0.48
            };
            deleteButton.Click += DeleteProjectButton_Click;
            Grid.SetColumn(deleteButton, 1);
            row.Children.Add(switchButton);
            row.Children.Add(deleteButton);
            rowBorder.Child = row;
            ProjectsListPanel.Children.Add(rowBorder);

            if (_pendingDeleteProject?.Id == project.Id)
                ProjectsListPanel.Children.Add(CreateDeleteConfirmationCard(project));
        }
    }

    private Border CreateDeleteConfirmationCard(StoryProject project)
    {
        var card = new Border
        {
            Margin = new Thickness(4, 0, 4, 7),
            Padding = new Thickness(10, 9, 10, 10),
            CornerRadius = new CornerRadius(8),
            Background = BrushFrom("#211715"),
            BorderBrush = BrushFrom("#704037"),
            BorderThickness = new Thickness(1)
        };

        var stack = new StackPanel();
        stack.Children.Add(new TextBlock
        {
            Text = $"Delete ‘{project.Title}’?",
            Foreground = BrushFrom("#F2D5CF"),
            FontSize = 11.5,
            FontWeight = FontWeights.SemiBold,
            TextWrapping = TextWrapping.Wrap
        });
        stack.Children.Add(new TextBlock
        {
            Text = "Chapters, memory and conversation will be removed permanently.",
            Foreground = BrushFrom("#A9867D"),
            FontSize = 9.5,
            Margin = new Thickness(0, 4, 0, 8),
            TextWrapping = TextWrapping.Wrap
        });

        var actions = new Grid();
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        actions.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

        var cancel = new WpfButton
        {
            Content = "Cancel",
            Tag = project,
            Style = (Style)FindResource("StoryButton"),
            Padding = new Thickness(9, 4, 9, 4),
            MinHeight = 28
        };
        cancel.Click += CancelDeleteProjectButton_Click;
        Grid.SetColumn(cancel, 1);

        var confirm = new WpfButton
        {
            Content = "Delete",
            Tag = project,
            Style = (Style)FindResource("StoryDangerButton"),
            Margin = new Thickness(6, 0, 0, 0),
            Padding = new Thickness(9, 4, 9, 4),
            MinHeight = 28
        };
        confirm.Click += ConfirmDeleteProjectButton_Click;
        Grid.SetColumn(confirm, 2);

        actions.Children.Add(cancel);
        actions.Children.Add(confirm);
        stack.Children.Add(actions);
        card.Child = stack;
        return card;
    }

    private void ProjectListItem_Click(object sender, RoutedEventArgs e)
    {
        if (sender is WpfButton button && button.Tag is StoryProject project)
        {
            _pendingDeleteProject = null;
            _creatingStory = false;
            if (_activeProject?.Id == project.Id)
                RebuildProjectList();
            else
                ProjectBox.SelectedItem = project;
        }
    }

    private void DeleteProjectButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton button || button.Tag is not StoryProject project)
            return;
        if (_generationCts is not null || _loadingProject || _creativeDecisionBusy || _storageAuditBusy)
        {
            SetStatus("Error · Finish the current Story operation before deleting.");
            return;
        }

        _pendingDeleteProject = _pendingDeleteProject?.Id == project.Id ? null : project;
        RebuildProjectList();
    }

    private void CancelDeleteProjectButton_Click(object sender, RoutedEventArgs e)
    {
        _pendingDeleteProject = null;
        RebuildProjectList();
    }

    private async void ConfirmDeleteProjectButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not WpfButton button || button.Tag is not StoryProject project)
            return;
        if (_generationCts is not null || _loadingProject || _creativeDecisionBusy || _storageAuditBusy)
        {
            SetStatus("Error · Finish the current Story operation before deleting.");
            return;
        }

        bool wasActive = _activeProject?.Id == project.Id;
        try
        {
            await _store.DeleteProjectAsync(project.Id);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("STORY", $"Project delete failed | project={project.Id} | {ex.Message}");
            SetStatus("Error · Could not delete this story.");
            return;
        }

        _pendingDeleteProject = null;
        _projects.RemoveAll(p => p.Id == project.Id);
        ProjectBox.Items.Refresh();

        if (wasActive)
        {
            _activeProject = null;
            ProjectBox.SelectedItem = null;
            CloseInspector();
            StoryProject? next = _projects.FirstOrDefault();
            if (next is not null)
                ProjectBox.SelectedItem = next;
            else
            {
                if (_settings is not null)
                {
                    _settings.StoryActiveProjectId = string.Empty;
                    _settings.Save();
                }
                UpdateEmptyState();
            }
        }
        RebuildProjectList();
    }

    private void MemoryNav_Click(object sender, RoutedEventArgs e)
    {
        if (_activeProject is null || sender is not WpfButton button)
            return;
        OpenInspector(button.Tag as string ?? "Overview");
    }

    private void ProjectSettingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeProject is null)
        {
            BeginNewStoryEditor();
            return;
        }
        OpenInspector("Settings");
    }

    private void ProviderButton_Click(object sender, RoutedEventArgs e) => OpenInspector("Settings");

    private void GenresButton_Click(object sender, RoutedEventArgs e) => ProjectSettingsButton_Click(sender, e);

    private void CloseInspectorButton_Click(object sender, RoutedEventArgs e) => CloseInspector();

    private void CloseInspector()
    {
        _activeInspectorSection = null;
        InspectorColumn.Width = new GridLength(0);
        InspectorPanel.Visibility = Visibility.Collapsed;
        ApplyNavSelection(null);
        ApplyResponsiveLayout();
    }

    private void OpenInspector(string section)
    {
        if (_activeProject is null)
            return;
        var watch = System.Diagnostics.Stopwatch.StartNew();
        AppLogger.Info("STORY-UI", $"Inspector opening | section={section} | generating={_generationCts is not null}");

        _activeInspectorSection = section;
        InspectorColumn.Width = new GridLength(372);
        InspectorPanel.Visibility = Visibility.Visible;
        ApplyResponsiveLayout();
        bool settings = string.Equals(section, "Settings", StringComparison.OrdinalIgnoreCase);
        InspectorMemoryView.Visibility = settings ? Visibility.Collapsed : Visibility.Visible;
        InspectorSettingsView.Visibility = settings ? Visibility.Visible : Visibility.Collapsed;

        if (settings)
        {
            InspectorTitle.Text = "Story Settings";
            InspectorSubtitle.Text = _activeProject.Title;
            LoadSettingsInspector();
            ApplyNavSelection("Settings");
        }
        else
        {
            RenderMemoryInspector(section);
            ApplyNavSelection(section);
        }
        AppLogger.Info("STORY-UI", $"Inspector opened | section={section} | elapsedMs={watch.ElapsedMilliseconds}");
    }

    private void RenderMemoryInspector(string section)
    {
        if (_activeProject is null)
            return;

        string normalized = string.IsNullOrWhiteSpace(section) ? "Overview" : section;
        InspectorTitle.Text = normalized == "World / Lore" ? "Lore" : normalized;
        InspectorSubtitle.Text = _activeProject.Title;
        InspectorMemoryHeading.Text = normalized == "Overview" ? "Story Memory" : normalized;
        SceneCompassUseCueButton.Visibility = normalized == "Scene Compass" &&
            StorySceneCompass.Build(_memory).HasCommittedChapter ? Visibility.Visible : Visibility.Collapsed;
        InspectorIdeasList.Visibility = normalized == "Ideas" ? Visibility.Visible : Visibility.Collapsed;
        InspectorIdeasToolbar.Visibility = normalized == "Ideas" ? Visibility.Visible : Visibility.Collapsed;
        if (normalized == "Ideas") RenderCoCreationInspector();
        if (string.Equals(normalized, "Research", StringComparison.OrdinalIgnoreCase))
        {
            RenderResearchInspectorBody();
        }
        else
        {
            InspectorMemoryBodyText.Inlines.Clear();
            InspectorMemoryBodyText.Text = BuildMemorySectionText(normalized);
        }

        int proposed = _memory.Canon.Count(x => IsProposed(x.Status)) +
                       _memory.Lore.Count(x => IsProposed(x.Status)) +
                       _memory.Events.Count(x => IsProposed(x.Status));
        bool memoryReviewSection = string.Equals(normalized, "Overview", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(normalized, "Canon", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(normalized, "Timeline", StringComparison.OrdinalIgnoreCase) ||
                                   string.Equals(normalized, "World / Lore", StringComparison.OrdinalIgnoreCase);
        InspectorAutoBookkeepingButton.Visibility = string.Equals(normalized, "Overview", StringComparison.OrdinalIgnoreCase) ? Visibility.Visible : Visibility.Collapsed;
        InspectorApproveButton.Visibility = proposed > 0 && memoryReviewSection ? Visibility.Visible : Visibility.Collapsed;
        InspectorDiscardButton.Visibility = proposed > 0 && memoryReviewSection ? Visibility.Visible : Visibility.Collapsed;
        InspectorVersionExpander.Visibility = string.Equals(normalized, "Overview", StringComparison.OrdinalIgnoreCase)
            ? Visibility.Visible : Visibility.Collapsed;
        if (InspectorVersionExpander.IsExpanded && InspectorVersionExpander.Visibility == Visibility.Visible)
            RenderCheckpointHistory();
        InspectorMemoryActions.Visibility = InspectorAutoBookkeepingButton.Visibility == Visibility.Visible ||
                                            InspectorApproveButton.Visibility == Visibility.Visible ||
                                            InspectorDiscardButton.Visibility == Visibility.Visible
            ? Visibility.Visible
            : Visibility.Collapsed;
        UpdateAutoBookkeepingButtons();
    }

    private string BuildMemorySectionText(string section)
    {
        if (_activeProject is null)
            return string.Empty;
        if (string.Equals(section, "Scene Compass", StringComparison.OrdinalIgnoreCase))
            return StorySceneCompass.Build(_memory).Display;
        if (string.Equals(section, "Ideas", StringComparison.OrdinalIgnoreCase))
        {
            int active = StoryCoCreation.ActiveDirections(_messages).Count;
            return $"{active} working directions saved from Chat.\n\n" +
                "Discuss freely with Gemini. When you agree on an idea, write “Chốt: ...” " +
                "in Chat or use Keep direction on a message. You can edit the summary before saving. " +
                "These notes guide future Chat and Continue Story, but never become confirmed canon until they occur in a committed chapter.";
        }
        if (string.Equals(section, "Overview", StringComparison.OrdinalIgnoreCase))
        {
            int openThreads = _memory.Threads.Count(x => x.Status == "Open");
            int activePromises = _memory.NarrativePromises.Count(StoryCausalMemoryLedger.IsActive);
            int activeCanon = _memory.Canon.Count(x => !IsRetiredCanon(x.Status));
            int pendingDrafts = _messages.Count(x => x.IsNarrativeDraft && !x.ManuscriptCommitted && !x.DraftDiscarded);
            StoryDeterministicReport integrity = StoryDeterministicContinuity.AnalyzeMemory(_memory);
            int integrityConflicts = integrity.Issues.Count(x => x.Severity == StoryDeterministicSeverity.Conflict);
            int integrityRisks = integrity.Issues.Count(x => x.Severity == StoryDeterministicSeverity.Risk);
            string integrityText = integrityConflicts == 0 && integrityRisks == 0
                ? "State integrity · clean"
                : $"State integrity · {integrityConflicts} conflict(s) · {integrityRisks} risk(s)";
            string recentEvents = BuildRecentEventsText(5);
            return $"{_memory.Chapters.Count} committed chapters\n{pendingDrafts} uncommitted narrative drafts\n{_memory.Characters.Count} characters\n{activeCanon} current canon facts\n{_memory.Revisions.Count} canon revisions\n{_memory.Relationships.Count} relationships · {_memory.RelationshipHistory.Count} relationship changes\n{_memory.Timeline.Count} timeline events\n{_memory.Events.Count} significant events\n{_memory.Arcs.Count} story arcs · {_memory.CharacterArcs.Count} character arcs\n{_memory.Locations.Count} tracked locations · {_memory.SceneStates.Count} scene-state snapshots\n{openThreads} open threads\n{activePromises} active narrative promises · {_memory.CausalLinks.Count} causal links\n{_memory.Lore.Count} lore entries\n{_researchDocument.Notes.Count} research notes\n{integrityText}\n\nConfirmed memory is recalled automatically before generation. Proposed canon, lore, or events stay uncertain until you approve them. Retconned/Contradicted canon is kept only as revision history and is not treated as current truth. Story Arcs track long-running plot trajectories; Character Arcs track durable personal development. Relationship and scene-state history preserve when changes occurred without replacing current state. Narrative Promises track setups, clues, foreshadowing and character promises until payoff/abandonment; Causal Memory records explicit why-A-led-to-B links. A derived Knowledge Graph expands long-story retrieval but never becomes canon by itself. Deterministic state checks protect structured memory from clear contradictions before merge. Research notes remain reference material and never become canon automatically."
                + (recentEvents.Length == 0 ? string.Empty : $"\n\nRECENT SIGNIFICANT EVENTS\n{recentEvents}");
        }
        if (string.Equals(section, "Canon", StringComparison.OrdinalIgnoreCase))
        {
            string current = JoinOrEmpty(_memory.Canon
                .Where(x => !IsRetiredCanon(x.Status))
                .OrderByDescending(x => x.UpdatedUtc)
                .Select(x => $"[{x.Status}] {x.Fact}\nSource · {x.Source}"), "No current canon facts yet.");
            string revisions = JoinOrEmpty(_memory.Revisions
                .OrderByDescending(x => x.CreatedUtc)
                .Select(x => $"[{x.Kind}] {x.OriginalFact}\n→ {x.ReplacementFact}" +
                    (string.IsNullOrWhiteSpace(x.Reason) ? string.Empty : $"\nReason · {x.Reason}") +
                    $"\nSource · {x.Source}"), string.Empty);
            return revisions.Length == 0 ? current : $"{current}\n\nREVISION HISTORY\n{revisions}";
        }
        if (string.Equals(section, "Characters", StringComparison.OrdinalIgnoreCase))
            return JoinOrEmpty(_memory.Characters.OrderBy(x => x.Name).Select(FormatCharacterInspector), "No characters have been captured yet.");
        if (string.Equals(section, "Timeline", StringComparison.OrdinalIgnoreCase))
        {
            string timeline = JoinOrEmpty(_memory.Timeline.Select(x => $"{x.When}\n{x.Event}\nSource · {x.Source}"), "No timeline events yet.");
            string events = BuildRecentEventsText(30);
            string relationships = JoinOrEmpty(_memory.RelationshipHistory
                .OrderByDescending(x => x.CreatedUtc).Take(20)
                .Select(StoryRelationshipLedger.FormatForContext), string.Empty);
            string scenes = JoinOrEmpty(_memory.SceneStates
                .OrderByDescending(x => x.CreatedUtc).Take(12)
                .Select(StoryWorldStateGraph.FormatSceneForContext), string.Empty);
            var parts = new List<string> { timeline };
            if (events.Length > 0) parts.Add($"SIGNIFICANT EVENT LEDGER\n{events}");
            if (relationships.Length > 0) parts.Add($"RELATIONSHIP HISTORY\n{relationships}");
            if (scenes.Length > 0) parts.Add($"RECENT SCENE / LOCATION STATE\n{scenes}");
            return string.Join("\n\n", parts);
        }
        if (string.Equals(section, "Open Threads", StringComparison.OrdinalIgnoreCase))
        {
            string threads = JoinOrEmpty(_memory.Threads.Where(x => x.Status == "Open").Select(x =>
                $"[{StoryNarrativeFocus.NormalizeImportance(x.Importance)}] {x.Title}\n{x.Detail}" +
                (x.OpenedChapter is int opened ? $"\nOpened · Chapter {opened}" : string.Empty) +
                (x.LastTouchedChapter is int touched ? $"\nLast touched · Chapter {touched}" : string.Empty) +
                $"\nSource · {x.Source}"), "No open threads yet.");
            string promises = JoinOrEmpty(_memory.NarrativePromises
                .OrderBy(x => !StoryCausalMemoryLedger.IsActive(x))
                .ThenByDescending(x => x.UpdatedUtc)
                .Take(20)
                .Select(StoryCausalMemoryLedger.FormatPromiseForContext), string.Empty);
            return promises.Length == 0 ? threads : $"{threads}\n\nSETUP / PAYOFF LEDGER\n{promises}";
        }
        if (string.Equals(section, "Story Arcs", StringComparison.OrdinalIgnoreCase))
        {
            string plotArcs = JoinOrEmpty(_memory.Arcs
                .OrderBy(x => string.Equals(x.Status, "Resolved", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(x => x.UpdatedUtc)
                .Select(FormatArcInspector), "No story arcs have been identified yet.");
            string characterArcs = JoinOrEmpty(_memory.CharacterArcs
                .OrderBy(x => string.Equals(x.Status, "Resolved", StringComparison.OrdinalIgnoreCase))
                .ThenByDescending(x => x.UpdatedUtc)
                .Select(StoryCharacterArcLedger.FormatForContext), string.Empty);
            return characterArcs.Length == 0 ? plotArcs : $"{plotArcs}\n\nCHARACTER ARCS\n{characterArcs}";
        }
        if (string.Equals(section, "World / Lore", StringComparison.OrdinalIgnoreCase))
            return JoinOrEmpty(_memory.Lore.Select(x => $"[{x.Status}] {x.Category} · {x.Name}\n{x.Detail}"), "No lore entries yet.");
        return "Nothing to show yet.";
    }

    private static string FormatCharacterInspector(StoryCharacterMemory c)
    {
        var sb = new StringBuilder();
        sb.AppendLine(c.Name);
        if (!string.IsNullOrWhiteSpace(c.Personality)) sb.AppendLine("Personality · " + c.Personality);
        if (!string.IsNullOrWhiteSpace(c.Appearance)) sb.AppendLine("Appearance · " + c.Appearance);
        if (c.Goals.Count > 0) sb.AppendLine("Goals · " + string.Join("; ", c.Goals));
        if (c.Knowledge.Count > 0) sb.AppendLine("Knows · " + string.Join("; ", c.Knowledge));
        if (c.Abilities.Count > 0) sb.AppendLine("Abilities · " + string.Join("; ", c.Abilities));
        if (c.Inventory.Count > 0) sb.AppendLine("Inventory · " + string.Join("; ", c.Inventory));
        if (!string.IsNullOrWhiteSpace(c.Progression)) sb.AppendLine("Progression · " + c.Progression);
        if (!string.IsNullOrWhiteSpace(c.CurrentLocation)) sb.AppendLine("Location · " + c.CurrentLocation);
        if (!string.IsNullOrWhiteSpace(c.Condition)) sb.AppendLine("Condition · " + c.Condition);
        if (!string.IsNullOrWhiteSpace(c.Notes)) sb.AppendLine("Notes · " + c.Notes);
        return sb.ToString().TrimEnd();
    }

    private static string FormatArcInspector(StoryArcMemory arc)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"[{arc.Status} · {StoryNarrativeFocus.NormalizePriority(arc.Priority)} · {StoryNarrativeFocus.NormalizeMomentum(arc.Momentum)}] {arc.Title}");
        if (!string.IsNullOrWhiteSpace(arc.Summary)) sb.AppendLine(arc.Summary);
        if (!string.IsNullOrWhiteSpace(arc.Progress)) sb.AppendLine("Progress · " + arc.Progress);
        if (!string.IsNullOrWhiteSpace(arc.Stakes)) sb.AppendLine("Stakes · " + arc.Stakes);
        if (arc.Goals.Count > 0) sb.AppendLine("Goals · " + string.Join("; ", arc.Goals));
        if (arc.RelatedCharacters.Count > 0) sb.AppendLine("Characters · " + string.Join(", ", arc.RelatedCharacters));
        if (arc.StartedChapter is int started)
            sb.AppendLine(arc.LastChapter is int last && last != started ? $"Chapters · {started} → {last}" : $"Started · Chapter {started}");
        if (!string.IsNullOrWhiteSpace(arc.Source)) sb.AppendLine("Source · " + arc.Source);
        return sb.ToString().TrimEnd();
    }

    private string BuildRecentEventsText(int max)
    {
        return JoinOrEmpty(
            _memory.Events
                .OrderByDescending(x => x.ChapterNumber ?? 0)
                .ThenByDescending(x => x.CreatedUtc)
                .Take(Math.Max(1, max))
                .Select(x => $"[{x.Importance}/{x.Status}] {x.Type} · {x.Subject}"
                    + (string.IsNullOrWhiteSpace(x.Target) ? string.Empty : $" → {x.Target}")
                    + $"\n{x.Summary}\nSource · {StoryEventLedger.BuildSource(x)}"),
            string.Empty);
    }

    private static string JoinOrEmpty(IEnumerable<string> values, string empty)
    {
        string[] items = values.Where(x => !string.IsNullOrWhiteSpace(x)).ToArray();
        return items.Length == 0 ? empty : string.Join("\n\n", items);
    }

    private static bool IsProposed(string? status) => string.Equals(status, "Proposed", StringComparison.OrdinalIgnoreCase);
    private static bool IsRetiredCanon(string? status) =>
        string.Equals(status, "Retconned", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(status, "Contradicted", StringComparison.OrdinalIgnoreCase);

    private async void InspectorApproveButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeProject is null)
            return;
        if (StoryMemoryMerger.ApproveAllProposed(_memory) > 0)
            await _memoryStore.SaveAsync(_activeProject.Id, _memory);
        UpdateMemoryButton();
        UpdateProjectSummaryUi();
        RenderMemoryInspector(_activeInspectorSection ?? "Overview");
    }

    private async void InspectorDiscardButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeProject is null)
            return;
        if (StoryMemoryMerger.DiscardAllProposed(_memory) > 0)
            await _memoryStore.SaveAsync(_activeProject.Id, _memory);
        UpdateMemoryButton();
        UpdateProjectSummaryUi();
        RenderMemoryInspector(_activeInspectorSection ?? "Overview");
    }

    private async void InspectorAutoBookkeepingButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeProject is null)
            return;
        _activeProject.AutoBookkeeping = !_activeProject.AutoBookkeeping;
        await _store.SaveProjectAsync(_activeProject);
        UpdateMemoryButton();
        UpdateAutoBookkeepingButtons();
    }

    private void UpdateAutoBookkeepingButtons()
    {
        if (_activeProject is null)
            return;
        string text = _activeProject.AutoBookkeeping ? "✓  Auto-bookkeeping on" : "Auto-bookkeeping off";
        InspectorAutoBookkeepingButton.Content = text;
        InspectorAutoBookkeepingButton.Background = BrushFrom(_activeProject.AutoBookkeeping ? "#352C16" : "#171512");
        InspectorAutoBookkeepingButton.BorderBrush = BrushFrom(_activeProject.AutoBookkeeping ? "#9B8036" : "#403722");
        InspectorAutoBookkeepingButton.Foreground = BrushFrom(_activeProject.AutoBookkeeping ? "#F0D680" : "#8F836A");
    }

    private void LoadSettingsInspector()
    {
        if (_settings is null || _activeProject is null)
            return;
        _loadingInspector = true;
        try
        {
            SettingsProviderBox.SelectedItem = StoryProviderFactory.Get(_settings.StoryProvider);
            SettingsModelBox.Text = string.IsNullOrWhiteSpace(_settings.StoryModel)
                ? StoryProviderFactory.DefaultModel(_settings.StoryProvider)
                : _settings.StoryModel;
            SettingsApiKeyBox.Clear();
            SettingsAutonomyBox.SelectedItem = StoryAutonomyModes.Normalize(_activeProject.AutonomyMode);
            SettingsDirectorBox.SelectedItem = StoryDirector.Normalize(_activeProject.StoryDirectorMode);
            SettingsNarrativeFocusBox.SelectedItem = StoryNarrativeSteering.Normalize(_activeProject.NarrativeFocusPreference);
            SettingsNarrativeGuardrailsBox.Text = _activeProject.NarrativeGuardrails ?? string.Empty;
            SettingsCraftFeedbackBox.SelectedItem = StoryCraftFeedback.Normalize(_activeProject.ChapterCraftFeedback);
            SettingsManuscriptModeBox.SelectedItem = StoryManuscriptModes.Normalize(_activeProject.ManuscriptMode);
            StoryContentProfiles.Normalize(_activeProject);
            SettingsMaturityBox.SelectedItem = _activeProject.ContentProfile.Maturity;
            SettingsViolenceBox.SelectedItem = _activeProject.ContentProfile.ViolenceGore;
            SettingsHorrorBox.SelectedItem = _activeProject.ContentProfile.HorrorIntensity;
            SettingsIntimacyBox.SelectedItem = _activeProject.ContentProfile.IntimacyBoundary;
            SettingsContentNotesBox.Text = _activeProject.ContentProfile.SensitiveThemesNotes;
            UpdateAutonomyDescription();
            UpdateManuscriptDescription();
            BuildSettingsGenreButtons();
            UpdateInspectorConnectionStatus();
            UpdateAutoBookkeepingButtons();
        }
        finally
        {
            _loadingInspector = false;
        }
    }

    private void BuildSettingsGenreButtons()
    {
        if (_activeProject is null)
            return;
        SettingsCoreGenresPanel.Children.Clear();
        SettingsTemporaryTagsPanel.Children.Clear();
        foreach (string genre in StoryGenres.All)
        {
            SettingsCoreGenresPanel.Children.Add(CreateGenreToggle(genre, _activeProject.CoreGenres.Contains(genre), SettingsGenreToggle_Click, "core"));
            SettingsTemporaryTagsPanel.Children.Add(CreateGenreToggle(genre, _activeProject.TemporaryTags.Contains(genre), SettingsGenreToggle_Click, "temp"));
        }
    }

    private async void SettingsGenreToggle_Click(object sender, RoutedEventArgs e)
    {
        if (_activeProject is null || sender is not ToggleButton button || button.Tag is not string tag)
            return;
        int split = tag.IndexOf('|');
        if (split < 0)
            return;
        string group = tag[..split];
        string genre = tag[(split + 1)..];
        List<string> target = group == "core" ? _activeProject.CoreGenres : _activeProject.TemporaryTags;
        if (button.IsChecked == true)
        {
            if (!target.Contains(genre)) target.Add(genre);
        }
        else
        {
            target.RemoveAll(x => string.Equals(x, genre, StringComparison.OrdinalIgnoreCase));
        }
        UpdateGenreToggleContent(button, genre);
        await _store.SaveProjectAsync(_activeProject);
        UpdateGenreSummary();
        RebuildProjectList();
    }

    private async void SettingsAutonomyBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingInspector || _activeProject is null || SettingsAutonomyBox.SelectedItem is not string mode)
            return;
        _activeProject.AutonomyMode = StoryAutonomyModes.Normalize(mode);
        await _store.SaveProjectAsync(_activeProject);
        UpdateAutonomyDescription();
        if (_selectedAction == StoryActionType.ContinueStory)
            SetSelectedStoryAction(_selectedAction);
        AppLogger.Info("STORY", $"Story autonomy changed | project={_activeProject.Id} | mode={_activeProject.AutonomyMode}");
    }

    private void UpdateAutonomyDescription()
    {
        if (SettingsAutonomyDescriptionText is null || _activeProject is null)
            return;
        SettingsAutonomyDescriptionText.Text = StoryAutonomyModes.Description(_activeProject.AutonomyMode);
    }

    private async void SettingsManuscriptModeBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingInspector || _activeProject is null || SettingsManuscriptModeBox.SelectedItem is not string mode)
            return;
        _activeProject.ManuscriptMode = StoryManuscriptModes.Normalize(mode);
        await _store.SaveProjectAsync(_activeProject);
        UpdateManuscriptDescription();
        RenderConversation();
        AppLogger.Info("STORY-MANUSCRIPT", $"Manuscript mode changed | project={_activeProject.Id} | mode={_activeProject.ManuscriptMode}");
    }

    private void UpdateManuscriptDescription()
    {
        if (SettingsManuscriptDescriptionText is null || _activeProject is null)
            return;
        SettingsManuscriptDescriptionText.Text = StoryManuscriptModes.Description(_activeProject.ManuscriptMode);
    }

    private async void SaveContentProfileButton_Click(object sender, RoutedEventArgs e)
    {
        if (_activeProject is null)
            return;
        _activeProject.ContentProfile = new StoryContentProfile
        {
            Maturity = SettingsMaturityBox.SelectedItem as string ?? StoryContentProfiles.Standard,
            ViolenceGore = SettingsViolenceBox.SelectedItem as string ?? StoryContentProfiles.ViolenceModerate,
            HorrorIntensity = SettingsHorrorBox.SelectedItem as string ?? StoryContentProfiles.HorrorModerate,
            IntimacyBoundary = SettingsIntimacyBox.SelectedItem as string ?? StoryContentProfiles.IntimacyFadeToBlack,
            SensitiveThemesNotes = SettingsContentNotesBox.Text.Trim()
        };
        StoryContentProfiles.Normalize(_activeProject);
        await _store.SaveProjectAsync(_activeProject);
        SetStatus(null);
        AppLogger.Info("STORY", $"Content profile saved | project={_activeProject.Id} | maturity={_activeProject.ContentProfile.Maturity} | violence={_activeProject.ContentProfile.ViolenceGore} | horror={_activeProject.ContentProfile.HorrorIntensity}");
    }

    private void SettingsProviderBox_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loadingInspector || SettingsProviderBox.SelectedItem is not IStoryAiProvider provider)
            return;
        SettingsModelBox.Text = StoryProviderFactory.DefaultModel(provider.Id);
        UpdateInspectorConnectionStatus();
    }

    private void UpdateInspectorConnectionStatus()
    {
        if (SettingsProviderBox.SelectedItem is not IStoryAiProvider provider)
            provider = StoryProviderFactory.Get(_settings?.StoryProvider);
        bool hasKey = StoryCredentialStore.HasKey(provider.Id);
        string selectedModel = SettingsModelBox.Text.Trim();
        string utilityModel = StoryModelRouter.Resolve(provider.Id, selectedModel, StoryAiWorkload.Utility);
        string routingNote = !string.IsNullOrWhiteSpace(selectedModel) &&
                             !string.Equals(selectedModel, utilityModel, StringComparison.OrdinalIgnoreCase)
            ? $" Utility passes use {utilityModel}."
            : string.Empty;
        SettingsConnectionStatusText.Text = hasKey
            ? $"{provider.DisplayName} connected · {selectedModel}. Key encrypted locally.{routingNote}"
            : $"{provider.DisplayName} selected · {selectedModel} · no API key saved yet.{routingNote}";
        SettingsConnectionStatusText.Foreground = BrushFrom(hasKey ? "#A9D39E" : "#D8B86A");
        ClearInspectorKeyButton.IsEnabled = hasKey;
    }

    private void SaveInspectorAiButton_Click(object sender, RoutedEventArgs e)
    {
        if (_settings is null || SettingsProviderBox.SelectedItem is not IStoryAiProvider provider)
            return;
        string model = SettingsModelBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(model))
        {
            SetStatus("Error · Enter an AI model name.");
            return;
        }
        if (!string.IsNullOrWhiteSpace(SettingsApiKeyBox.Password))
            StoryCredentialStore.Save(provider.Id, SettingsApiKeyBox.Password);
        _settings.StoryProvider = provider.Id;
        _settings.StoryModel = model;
        _settings.Save();
        AppLogger.Info("STORY-AI", $"Story AI settings saved | provider={provider.Id} | model={model}");
        SettingsApiKeyBox.Clear();
        RefreshProviderStatus();
        UpdateInspectorConnectionStatus();
        SetStatus(null);
    }

    private void ClearInspectorKeyButton_Click(object sender, RoutedEventArgs e)
    {
        if (SettingsProviderBox.SelectedItem is not IStoryAiProvider provider)
            return;
        StoryCredentialStore.Save(provider.Id, string.Empty);
        SettingsApiKeyBox.Clear();
        RefreshProviderStatus();
        UpdateInspectorConnectionStatus();
    }

    private void ApplyNavSelection(string? section)
    {
        var entries = new (WpfButton WpfButton, string Tag)[]
        {
            (CharactersNavButton, "Characters"),
            (CanonNavButton, "Canon"),
            (TimelineNavButton, "Timeline"),
            (LoreNavButton, "World / Lore"),
            (ThreadsNavButton, "Open Threads"),
            (ArcsNavButton, "Story Arcs"),
            (ResearchNavButton, "Research"),
            (MemoryNavButton, "Overview"),
            (SceneCompassNavButton, "Scene Compass"),
            (IdeasNavButton, "Ideas"),
            (SettingsNavButton, "Settings")
        };
        foreach ((WpfButton button, string tag) in entries)
        {
            bool selected = string.Equals(section, tag, StringComparison.OrdinalIgnoreCase);
            button.Background = BrushFrom(selected ? "#2C2618" : "#00000000");
            button.BorderBrush = BrushFrom(selected ? "#D0AE45" : "#00000000");
            button.Foreground = BrushFrom(selected ? "#F0D680" : "#A99B7B");
        }
    }

    private static WpfBrush BrushFrom(string value) =>
        (WpfBrush)new WpfBrushConverter().ConvertFromString(value)!;
}
