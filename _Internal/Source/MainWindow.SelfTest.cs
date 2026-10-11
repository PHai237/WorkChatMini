using System.IO;
using System.Diagnostics;
using System.Text;
using Microsoft.Web.WebView2.Core;

namespace WorkChatMini;

public partial class MainWindow
{
    private sealed class SelfTestCaseResult
    {
        public required string Name { get; init; }
        public required string Status { get; init; }
        public required long ElapsedMs { get; init; }
        public string Details { get; init; } = string.Empty;
    }

    private async Task RunSelfTestAfterEnvironmentAsync()
    {
        string mode = AppRuntime.IsStressSelfTest ? "stress" : "quick";
        var results = new List<SelfTestCaseResult>();
        var switchSamples = new List<long>();
        ResourceSnapshot startSnapshot = ResourceTelemetry.Capture(_webEnvironment);
        long peakBytes = startSnapshot.AppPrivateBytes;
        long startupMs = _startupStopwatch.ElapsedMilliseconds;
        int exitCode = 0;

        Title = $"WorkChat Mini Self Test ({mode})";
        AppLogger.Info("SELFTEST", $"Starting | mode={mode} | startupMs={startupMs}");
        CrashDiagnostics.UpdateContext("SelfTest", string.Empty, $"selftest-{mode}-start");

        async Task RunCase(string name, Func<Task> action)
        {
            Stopwatch sw = Stopwatch.StartNew();
            try
            {
                await action().WaitAsync(TimeSpan.FromSeconds(AppRuntime.IsStressSelfTest ? 30 : 20));
                sw.Stop();
                results.Add(new SelfTestCaseResult { Name = name, Status = "PASS", ElapsedMs = sw.ElapsedMilliseconds });
                AppLogger.Info("SELFTEST", $"PASS | {name} | {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                sw.Stop();
                results.Add(new SelfTestCaseResult { Name = name, Status = "FAIL", ElapsedMs = sw.ElapsedMilliseconds, Details = ex.Message });
                AppLogger.Error("SELFTEST", $"FAIL | {name}", ex);
                exitCode = 1;
            }
            finally
            {
                ResourceSnapshot sample = ResourceTelemetry.Capture(_webEnvironment);
                peakBytes = Math.Max(peakBytes, sample.AppPrivateBytes);
            }
        }

        try
        {
            await RunCase("Environment", AppUpdateSelfTest.RunAsync);

            await RunCase("Browser initialize", async () =>
            {
                await EnsureBrowserInitializedAsync();
                await SwitchToSiteAsync(MiniSite.Web);
                if (_browserTabs.Count != 1 || _activeBrowserTab is null || !_activeBrowserTab.IsBlank)
                    throw new InvalidOperationException("Expected one blank lazy tab.");
            });

            string[] testUrls =
            {
                "https://example.com/",
                "https://example.org/",
                "https://example.net/",
                "https://www.iana.org/domains/reserved"
            };

            await RunCase("Browser history autocomplete", () =>
            {
                EnsureBrowserHistoryReady();
                _browserHistory!.Clear();
                _browserHistory.Record("https://example.com/docs", "Example Documentation");
                _browserHistory.Record("https://github.com/MicrosoftEdge/WebView2Samples", "WebView2 Samples");
                _browserHistory.Record("https://getadblock.com/en/update/", "AdBlock Updated");
                IReadOnlyList<BrowserHistoryEntry> matches = _browserHistory.Search("example", 6);
                if (matches.Count == 0 || !matches[0].Url.Contains("example.com", StringComparison.OrdinalIgnoreCase))
                    throw new InvalidOperationException("History autocomplete did not return the expected URL.");
                if (_browserHistory.Search("adblock", 6).Count != 0)
                    throw new InvalidOperationException("Ad-blocker nuisance page leaked into browser history.");
                if (!AdBlockNuisanceGuard.IsKnownNuisancePage("https://getadblock.com/en/update/"))
                    throw new InvalidOperationException("AdBlock update-page guard did not recognize a known nuisance URL.");
                if (AdBlockNuisanceGuard.IsKnownNuisancePage("https://helpcenter.getadblock.com/adblock-help-center/"))
                    throw new InvalidOperationException("AdBlock guard incorrectly classified Help Center documentation.");
                _browserHistory.Clear();
                return Task.CompletedTask;
            });

            await RunCase("Five-tab lazy creation", async () =>
            {
                foreach (string url in testUrls)
                {
                    BrowserTabState? tab = await AddBrowserTabAsync(url, activate: false);
                    if (tab is null)
                        throw new InvalidOperationException("Tab creation unexpectedly hit max early.");
                }
                if (_browserTabs.Count != MaxBrowserTabs)
                    throw new InvalidOperationException($"Expected {MaxBrowserTabs} tabs, got {_browserTabs.Count}.");
                int initialized = _browserTabs.Count(t => t.Initialized);
                if (initialized > 1)
                    throw new InvalidOperationException($"Lazy creation regression: {initialized} tabs initialized before activation.");
            });

            await RunCase("Five-tab limit", async () =>
            {
                BrowserTabState? sixth = await AddBrowserTabAsync("https://example.com/test", activate: false);
                if (sixth is not null || _browserTabs.Count != MaxBrowserTabs)
                    throw new InvalidOperationException("Sixth tab was not rejected.");
            });

            await RunCase("Initialize all tabs", async () =>
            {
                foreach (BrowserTabState tab in _browserTabs.Where(t => !t.IsBlank).ToArray())
                {
                    await ActivateBrowserTabAsync(tab);
                    await Task.Delay(80);
                }
                if (_browserTabs.Count(t => t.Initialized) < 4)
                    throw new InvalidOperationException("Expected all URL tabs to initialize.");
            });

            await RunCase("Telemetry process sampling", () =>
            {
                ResourceSnapshot sample = ResourceTelemetry.Capture(_webEnvironment);
                if (sample.CapturedTimestamp <= 0)
                    throw new InvalidOperationException("Telemetry timestamp was not captured.");
                if (sample.ProcessSamples.Count == 0)
                    throw new InvalidOperationException("No WebView process samples were captured.");
                if (sample.ProcessSamples.Any(process => process.PrivateBytes < 0 || process.TotalProcessorTimeTicks < 0))
                    throw new InvalidOperationException("Invalid process telemetry values.");
                return Task.CompletedTask;
            });

            await RunCase("Adaptive sleeping sync backoff", () =>
            {
                const MiniSite site = MiniSite.Discord;
                int originalStep = _sleepingSyncBackoffStep[site];
                try
                {
                    _sleepingSyncBackoffStep[site] = 0;
                    TimeSpan first = GetSleepingMessageSyncInterval(site);
                    _sleepingSyncBackoffStep[site] = 1;
                    TimeSpan second = GetSleepingMessageSyncInterval(site);
                    _sleepingSyncBackoffStep[site] = 2;
                    TimeSpan third = GetSleepingMessageSyncInterval(site);
                    if (!(first <= second && second < third && third <= TimeSpan.FromMinutes(30)))
                        throw new InvalidOperationException($"Unexpected sync cadence: {first.TotalMinutes:0}/{second.TotalMinutes:0}/{third.TotalMinutes:0} minutes.");
                }
                finally
                {
                    _sleepingSyncBackoffStep[site] = originalStep;
                }
                return Task.CompletedTask;
            });

            await RunCase("Notification quiet policy", () =>
            {
                if (!IsNotificationPermissionBlocked(CoreWebView2PermissionKind.Notifications))
                    throw new InvalidOperationException("Notification permission is not hard-blocked.");
                if (FindName("MessengerUnreadDot") is not null || FindName("DiscordUnreadDot") is not null ||
                    FindName("ZaloUnreadDot") is not null || FindName("CurrentAppUnreadDot") is not null)
                    throw new InvalidOperationException("Unread badge UI still exists in the visual tree.");
                return Task.CompletedTask;
            });

            await RunCase("Discord call protection", () =>
            {
                bool originalActive = _discordCallActive;
                DateTimeOffset originalUntil = _discordCallProtectionUntilUtc;
                bool originalYouTubePlayback = _youtubePlaybackActive;
                try
                {
                    _youtubePlaybackActive = false;
                    _discordCallActive = true;
                    _discordCallProtectionUntilUtc = DateTimeOffset.MaxValue;
                    if (!IsDiscordCallProtectionActive())
                        throw new InvalidOperationException("Active Discord call was not protected.");
                    if (!IsSystemAwakeActivityActive())
                        throw new InvalidOperationException("Active Discord call did not request awake protection.");

                    _discordCallActive = false;
                    _discordCallProtectionUntilUtc = DateTimeOffset.UtcNow.Add(DiscordCallEndGracePeriod);
                    if (!IsDiscordCallProtectionActive())
                        throw new InvalidOperationException("Discord post-call grace protection was not active.");
                    if (IsSystemAwakeActivityActive())
                        throw new InvalidOperationException("Post-call grace incorrectly kept the display awake.");

                    _youtubePlaybackActive = true;
                    if (!IsSystemAwakeActivityActive())
                        throw new InvalidOperationException("YouTube playback did not request awake protection.");
                }
                finally
                {
                    _youtubePlaybackActive = originalYouTubePlayback;
                    _discordCallActive = originalActive;
                    _discordCallProtectionUntilUtc = originalUntil;
                }
                return Task.CompletedTask;
            });

            await RunCase("Story foundation persistence", async () =>
            {
                var store = new StoryProjectStore();
                StoryProject project = await store.CreateProjectAsync($"SelfTest Story {Guid.NewGuid():N}");
                try
                {
                    project.CoreGenres = new List<string> { "Romcom", "Rebirth / Trọng sinh" };
                    project.TemporaryTags = new List<string> { "Mystery" };
                    project.ManuscriptMode = StoryManuscriptModes.ReviewBeforeCommit;
                    project.ContentProfile = new StoryContentProfile
                    {
                        Maturity = StoryContentProfiles.Mature,
                        ViolenceGore = StoryContentProfiles.ViolenceGraphic,
                        HorrorIntensity = StoryContentProfiles.HorrorIntense,
                        IntimacyBoundary = StoryContentProfiles.IntimacyFadeToBlack,
                        SensitiveThemesNotes = "Self-test mature theme note"
                    };
                    await store.SaveProjectAsync(project);
                    StoryProject? reloadedProject = (await store.LoadProjectsAsync()).FirstOrDefault(x => x.Id == project.Id);
                    if (reloadedProject is null ||
                        reloadedProject.ManuscriptMode != StoryManuscriptModes.ReviewBeforeCommit ||
                        reloadedProject.ContentProfile.Maturity != StoryContentProfiles.Mature ||
                        reloadedProject.ContentProfile.ViolenceGore != StoryContentProfiles.ViolenceGraphic)
                    {
                        throw new InvalidOperationException("Story manuscript/content-profile persistence failed.");
                    }
                    var savedMessages = new List<StoryMessage>
                    {
                        new() { Role = "user", Content = "Remember the red umbrella." },
                        new()
                        {
                            Role = "assistant", Content = "The red umbrella is part of this project context.",
                            Action = StoryActionType.DraftScene.ToString(), SourceInstruction = "Draft the umbrella scene.",
                            IsNarrativeDraft = true, ManuscriptCommitted = false
                        }
                    };
                    await store.SaveMessagesAsync(project.Id, savedMessages);
                    IReadOnlyList<StoryMessage> loadedMessages = await store.LoadMessagesAsync(project.Id);
                    if (loadedMessages.Count != 2 || loadedMessages[0].Content != savedMessages[0].Content ||
                        !loadedMessages[1].IsNarrativeDraft || loadedMessages[1].ManuscriptCommitted ||
                        loadedMessages[1].SourceInstruction != "Draft the umbrella scene.")
                    {
                        throw new InvalidOperationException("Story conversation/manuscript-draft persistence failed.");
                    }

                    await StoryCoCreationSelfTest.VerifyAsync(store, project.Id);

                    const string credentialId = "SelfTestProvider";
                    StoryCredentialStore.Save(credentialId, "selftest-secret");
                    if (StoryCredentialStore.Load(credentialId) != "selftest-secret")
                        throw new InvalidOperationException("DPAPI story credential round-trip failed.");
                    StoryCredentialStore.Save(credentialId, string.Empty);

                    await SwitchToSiteAsync(MiniSite.Story);
                    if (StorySurfaceGrid.Visibility != System.Windows.Visibility.Visible)
                        throw new InvalidOperationException("Story surface did not become visible.");
                    if (StoryPanel.FindName("IdeasNavButton") is not System.Windows.Controls.Button ||
                        StoryPanel.FindName("InspectorIdeasList") is not System.Windows.Controls.StackPanel ||
                        StoryPanel.FindName("ChatScroll") is not System.Windows.Controls.ScrollViewer ||
                        StoryPanel.FindName("JumpToLatestButton") is not System.Windows.Controls.Button ||
                        StoryPanel.TryFindResource("StoryInlineActionButton") is not System.Windows.Style)
                        throw new InvalidOperationException("Co-creation UI elements are missing from the Story panel.");
                    if (System.Windows.Application.Current.TryFindResource("ModernDialogButton") is not System.Windows.Style)
                        throw new InvalidOperationException("Modern browser dialog style resource was not loaded.");
                    await SwitchToSiteAsync(MiniSite.Web);
                }
                finally
                {
                    string directory = store.GetProjectDirectory(project.Id);
                    if (Directory.Exists(directory))
                        Directory.Delete(directory, true);
                }
            });

            await RunCase("Story memory persistence and retrieval", async () =>
            {
                var projectStore = new StoryProjectStore();
                var memoryStore = new StoryMemoryStore();
                StoryProject project = await projectStore.CreateProjectAsync($"SelfTest Memory {Guid.NewGuid():N}");
                try
                {
                    var memory = new StoryMemoryDocument();
                    memory.Canon.Add(new StoryCanonFact
                    {
                        Fact = "Linh keeps a red umbrella from the first rain festival.",
                        Status = "Confirmed",
                        Source = "Chapter 1"
                    });
                    memory.Characters.Add(new StoryCharacterMemory
                    {
                        Name = "Linh",
                        Knowledge = new List<string> { "The umbrella was a gift from Minh." },
                        CurrentLocation = "Old Quarter"
                    });
                    StoryChapterMemory? selfTestChapter = await memoryStore.AppendChapterAsync(
                        project.Id,
                        memory,
                        "The First Rain",
                        "Linh receives a red umbrella from Minh during the rain festival.",
                        "Linh held the old red umbrella beneath the lanterns. Minh had given it to her before the first rain festival ended. She finally learned Rain Echo by listening to the umbrella's stored memory.");
                    if (selfTestChapter is null)
                        throw new InvalidOperationException("Story chapter persistence failed before event-ledger test.");
                    var eventUpdate = new StoryMemoryUpdate
                    {
                        Characters = new List<StoryCharacterPatch>
                        {
                            new() { Name = "Linh", Abilities = new List<string> { "Rain Echo" }, Progression = "Learned Rain Echo." }
                        },
                        Relationships = new List<StoryRelationshipPatch>
                        {
                            new() { From = "Linh", To = "Minh", State = "Cautious trust", Change = "Linh chose to trust Minh with the umbrella secret." }
                        },
                        Events = new List<StoryEventPatch>
                        {
                            new()
                            {
                                Type = "AbilityLearned", Subject = "Linh", Target = "Rain Echo",
                                Summary = "Linh learned Rain Echo from the umbrella's stored memory.",
                                After = "Linh can use Rain Echo.", Importance = "Major", Status = "Confirmed",
                                RelatedCharacters = new List<string> { "Linh" }, Tags = new List<string> { "ability", "progression" }
                            }
                        },
                        CharacterArcs = new List<StoryCharacterArcPatch>
                        {
                            new()
                            {
                                Character = "Linh", Title = "Learning to trust", Summary = "Linh is learning to rely on someone else without surrendering caution.",
                                Status = "Active", Importance = "Major", Stage = "First deliberate trust", Direction = "Growing",
                                InternalConflict = "Self-protection versus connection", ExternalPressure = "The umbrella mystery",
                                Progress = "Linh trusted Minh with one dangerous secret.", LastChange = "She chose disclosure instead of isolation."
                            }
                        },
                        Locations = new List<StoryLocationPatch>
                        {
                            new()
                            {
                                Name = "Old Quarter Archive", Summary = "A sealed archive room beneath the lantern district.",
                                Connections = new List<string> { "Old Quarter" }, RelevantObjects = new List<string> { "memory umbrella" },
                                PresentCharacters = new List<string> { "Linh", "Minh" }
                            }
                        },
                        SceneStates = new List<StorySceneStatePatch>
                        {
                            new()
                            {
                                SceneLabel = "Umbrella memory awakening", Location = "Old Quarter Archive", Time = "Night",
                                PovCharacter = "Linh", PresentCharacters = new List<string> { "Linh", "Minh" },
                                Summary = "Linh learns Rain Echo and chooses to trust Minh with the secret."
                            }
                        },
                        OpenThreads = new List<StoryThreadPatch>
                        {
                            new() { Title = "Origin of the umbrella", Detail = "Why does the umbrella store memories?", Importance = "Major" }
                        },
                        Arcs = new List<StoryArcPatch>
                        {
                            new()
                            {
                                Title = "Rain Memory Mystery",
                                Summary = "Linh is uncovering why the umbrella stores memories.",
                                Status = "Active",
                                Priority = "Core",
                                Momentum = "Rising",
                                Stakes = "The truth behind the first rain festival.",
                                Progress = "Rain Echo was learned from the umbrella.",
                                RelatedCharacters = new List<string> { "Linh", "Minh" },
                                RelatedThreads = new List<string> { "Origin of the umbrella" }
                            }
                        },
                        NarrativePromises = new List<StoryNarrativePromisePatch>
                        {
                            new()
                            {
                                Kind = "Clue", Title = "Umbrella remembers names",
                                Detail = "The memory umbrella appears to preserve more than images and may reveal whose memories it carries.",
                                Status = "Open", Importance = "Major", Horizon = "Long",
                                Evidence = "Rain Echo awakened from the umbrella's stored memory.",
                                RelatedCharacters = new List<string> { "Linh", "Minh" },
                                RelatedArcs = new List<string> { "Rain Memory Mystery" },
                                RelatedThreads = new List<string> { "Origin of the umbrella" }
                            }
                        },
                        CausalLinks = new List<StoryCausalLinkPatch>
                        {
                            new()
                            {
                                Relation = "ENABLES", From = "The umbrella stores memories", To = "Linh learns Rain Echo",
                                Summary = "The umbrella's stored memory enables Linh to awaken Rain Echo.", Importance = "Major",
                                Evidence = "Linh learned Rain Echo by listening to the umbrella's stored memory.",
                                RelatedCharacters = new List<string> { "Linh" }
                            }
                        }
                    };
                    StoryMemoryMerger.Apply(memory, eventUpdate, $"Chapter {selfTestChapter.Number}: {selfTestChapter.Title}", selfTestChapter);
                    var revisionUpdate = new StoryMemoryUpdate
                    {
                        Revisions = new List<StoryCanonRevisionPatch>
                        {
                            new()
                            {
                                Kind = "Retcon",
                                OriginalFact = "Linh keeps a red umbrella from the first rain festival.",
                                ReplacementFact = "Linh keeps a blue umbrella from the first rain festival.",
                                Reason = "Self-test canon revision"
                            }
                        }
                    };
                    StoryMemoryMerger.Apply(memory, revisionUpdate, "Creator-approved canon revision");
                    await memoryStore.SaveAsync(project.Id, memory);

                    StoryMemoryDocument loaded = await memoryStore.LoadAsync(project.Id);
                    if (loaded.SchemaVersion != 6 || loaded.Chapters.Count != 1 || loaded.Canon.Count != 2 || loaded.Revisions.Count != 1 ||
                        loaded.Characters.Count < 2 || loaded.Events.Count != 1 || loaded.Arcs.Count != 1 || loaded.CharacterArcs.Count != 1 ||
                        loaded.RelationshipHistory.Count != 1 || loaded.Locations.Count != 1 || loaded.SceneStates.Count != 1 ||
                        loaded.NarrativePromises.Count != 1 || loaded.CausalLinks.Count != 1)
                        throw new InvalidOperationException("Structured Story Memory v6 persistence failed.");
                    StorySignificantEvent learnedEvent = loaded.Events[0];
                    StoryCharacterMemory? linhMemory = loaded.Characters.FirstOrDefault(x => string.Equals(x.Name, "Linh", StringComparison.OrdinalIgnoreCase));
                    if (learnedEvent.ChapterNumber != 1 ||
                        !string.Equals(learnedEvent.Type, "AbilityLearned", StringComparison.OrdinalIgnoreCase) ||
                        linhMemory is null || !linhMemory.Abilities.Contains("Rain Echo", StringComparer.OrdinalIgnoreCase) ||
                        !string.Equals(linhMemory.CurrentLocation, "Old Quarter Archive", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("Story Event Ledger / spatial-state current-state merge failed.");
                    }
                    if (loaded.RelationshipHistory[0].ChapterNumber != 1 ||
                        loaded.CharacterArcs[0].LastChapter != 1 ||
                        loaded.SceneStates[0].ChapterNumber != 1 ||
                        !loaded.Locations[0].PresentCharacters.Contains("Linh", StringComparer.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("Story relationship/character-arc/location provenance merge failed.");
                    }
                    if (loaded.NarrativePromises[0].IntroducedChapter != 1 || loaded.NarrativePromises[0].LastTouchedChapter != 1 ||
                        loaded.CausalLinks[0].ChapterNumber != 1 ||
                        !string.Equals(loaded.CausalLinks[0].Relation, "ENABLES", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("Story setup/payoff or causal provenance merge failed.");
                    }

                    StoryCanonFact? retiredFact = loaded.Canon.FirstOrDefault(x => x.Fact.Contains("red umbrella", StringComparison.OrdinalIgnoreCase));
                    StoryCanonFact? currentFact = loaded.Canon.FirstOrDefault(x => x.Fact.Contains("blue umbrella", StringComparison.OrdinalIgnoreCase));
                    if (retiredFact is null || currentFact is null ||
                        !string.Equals(retiredFact.Status, "Retconned", StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(currentFact.Status, "Confirmed", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Canon revision ledger did not retire/replace the expected fact.");

                    StoryRetrievalPlan retrievalPlan = StoryRetrievalPlanner.Build(
                        loaded,
                        "Who gave Linh the umbrella and when did she learn Rain Echo?",
                        autoContinue: false);
                    if (!retrievalPlan.ChapterBoosts.TryGetValue(1, out int provenanceBoost) || provenanceBoost <= 0 ||
                        !retrievalPlan.ExpandedQuery.Contains("Rain Echo", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Long-story retrieval planner did not expand event provenance to Chapter 1.");
                    StoryRetrievalPlan vietnameseIntentPlan = StoryRetrievalPlanner.Build(
                        loaded,
                        "Linh học kỹ năng gì ở chương mấy?",
                        autoContinue: false);
                    if (!vietnameseIntentPlan.ChapterBoosts.TryGetValue(1, out int vietnameseBoost) || vietnameseBoost <= 0 ||
                        !vietnameseIntentPlan.ExpandedQuery.Contains("AbilityLearned", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Vietnamese retrieval intent did not map skill/provenance language to the event ledger.");

                    StoryRetrievalPlan causalIntentPlan = StoryRetrievalPlanner.Build(
                        loaded,
                        "Vì sao Linh học được Rain Echo và manh mối chiếc ô đã gài điều gì?",
                        autoContinue: false);
                    if (!causalIntentPlan.ChapterBoosts.TryGetValue(1, out int causalBoost) || causalBoost <= 0 ||
                        !causalIntentPlan.ExpandedQuery.Contains("ENABLES", StringComparison.OrdinalIgnoreCase) ||
                        !causalIntentPlan.ExpandedQuery.Contains("Umbrella remembers names", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Causal/setup retrieval did not expand promise and causal provenance.");

                    IReadOnlyList<StoryChapterSearchHit> hits = await memoryStore.SearchChapterTextAsync(project.Id, retrievalPlan);
                    if (hits.Count == 0 || hits[0].Number != 1 || !hits[0].Excerpt.Contains("umbrella", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Indexed old-chapter retrieval did not find the expected provenance detail.");
                    string retrievalIndexPath = Path.Combine(projectStore.GetProjectDirectory(project.Id), "memory", "chapter-index.json");
                    if (!File.Exists(retrievalIndexPath))
                        throw new InvalidOperationException("Long-story chapter index was not persisted.");
                    await StoryLongRetrievalSelfTest.VerifyAsync();

                    string context = StoryMemoryRetriever.BuildContext(loaded, retrievalPlan.ExpandedQuery, hits);
                    string retrievalFocus = retrievalPlan.BuildFocusContext();
                    string focus = StoryNarrativeFocus.Build(loaded);
                    var missingRetrievalSignals = new List<string>();
                    void RequireSignal(bool present, string label)
                    {
                        if (!present)
                            missingRetrievalSignals.Add(label);
                    }
                    RequireSignal(context.Contains("gift from Minh", StringComparison.OrdinalIgnoreCase), "character knowledge");
                    RequireSignal(context.Contains("blue umbrella", StringComparison.OrdinalIgnoreCase), "current canon");
                    RequireSignal(context.Contains("CANON REVISION HISTORY", StringComparison.OrdinalIgnoreCase), "revision history");
                    RequireSignal(context.Contains("AbilityLearned", StringComparison.OrdinalIgnoreCase), "event ledger");
                    RequireSignal(context.Contains("Chapter 1", StringComparison.OrdinalIgnoreCase), "chapter provenance");
                    RequireSignal(context.Contains("Rain Memory Mystery", StringComparison.OrdinalIgnoreCase), "story arc");
                    RequireSignal(context.Contains("Learning to trust", StringComparison.OrdinalIgnoreCase), "character arc");
                    RequireSignal(context.Contains("Old Quarter Archive", StringComparison.OrdinalIgnoreCase), "spatial state");
                    RequireSignal(context.Contains("RELATIONSHIP HISTORY", StringComparison.OrdinalIgnoreCase), "relationship history");
                    RequireSignal(context.Contains("NARRATIVE PROMISES / SETUP-PAYOFF", StringComparison.OrdinalIgnoreCase), "narrative promise ledger");
                    RequireSignal(context.Contains("Umbrella remembers names", StringComparison.OrdinalIgnoreCase), "narrative promise content");
                    RequireSignal(context.Contains("CAUSAL MEMORY", StringComparison.OrdinalIgnoreCase), "causal memory section");
                    RequireSignal(context.Contains("ENABLES", StringComparison.OrdinalIgnoreCase), "causal relation");
                    RequireSignal(retrievalFocus.Contains("Chapter 1", StringComparison.OrdinalIgnoreCase), "retrieval focus provenance");
                    RequireSignal(focus.Contains("Rain Memory Mystery", StringComparison.OrdinalIgnoreCase), "narrative story arc");
                    RequireSignal(focus.Contains("Learning to trust", StringComparison.OrdinalIgnoreCase), "narrative character arc");
                    RequireSignal(focus.Contains("Old Quarter Archive", StringComparison.OrdinalIgnoreCase), "narrative spatial state");
                    RequireSignal(focus.Contains("Origin of the umbrella", StringComparison.OrdinalIgnoreCase), "narrative open thread");
                    RequireSignal(focus.Contains("Umbrella remembers names", StringComparison.OrdinalIgnoreCase), "narrative setup/payoff focus");
                    RequireSignal(retrievalPlan.KnowledgeGraphContext.Contains("Rain Echo", StringComparison.OrdinalIgnoreCase), "knowledge graph ability node");
                    RequireSignal(retrievalPlan.KnowledgeGraphContext.Contains("HAS_ABILITY", StringComparison.OrdinalIgnoreCase), "knowledge graph ability edge");
                    if (missingRetrievalSignals.Count > 0)
                        throw new InvalidOperationException("Story Memory v6 retrieval signals missing: " + string.Join(", ", missingRetrievalSignals));

                    StoryMemoryMerger.Apply(loaded, new StoryMemoryUpdate
                    {
                        NarrativePromises = new List<StoryNarrativePromisePatch>
                        {
                            new()
                            {
                                Kind = "Clue", Title = "Umbrella remembers names", Status = "PaidOff", Importance = "Major",
                                PayoffSummary = "The stored name is finally identified.", Evidence = "Self-test payoff."
                            }
                        }
                    }, $"Chapter {selfTestChapter.Number}: {selfTestChapter.Title}", selfTestChapter);
                    StoryMemoryMerger.Apply(loaded, new StoryMemoryUpdate
                    {
                        NarrativePromises = new List<StoryNarrativePromisePatch>
                        {
                            new() { Kind = "Clue", Title = "Umbrella remembers names", Status = "Open", Importance = "Major" }
                        }
                    }, "Later bookkeeping replay", selfTestChapter);
                    StoryNarrativePromise payoffPromise = loaded.NarrativePromises.Single(x =>
                        string.Equals(x.Title, "Umbrella remembers names", StringComparison.OrdinalIgnoreCase));
                    if (!string.Equals(payoffPromise.Status, "PaidOff", StringComparison.OrdinalIgnoreCase) || payoffPromise.PaidOffChapter != 1)
                        throw new InvalidOperationException("Narrative promise terminal payoff state was reopened by ordinary bookkeeping.");

                    StoryDeterministicReport cleanIntegrity = StoryDeterministicContinuity.AnalyzeMemory(loaded);
                    if (cleanIntegrity.HasConflict)
                        throw new InvalidOperationException("Deterministic continuity reported a hard conflict for valid Story Memory.");

                    var unsafeUpdate = new StoryMemoryUpdate
                    {
                        IsChapter = true,
                        ChapterTitle = "Unsafe self-test chapter",
                        ChapterSummary = "This update intentionally attempts to resurrect retired canon.",
                        Canon = new List<StoryCanonFactPatch>
                        {
                            new() { Fact = "Linh keeps a red umbrella from the first rain festival.", Status = "Confirmed" }
                        }
                    };
                    StoryDeterministicReport unsafeReport = StoryDeterministicContinuity.ValidateUpdate(loaded, unsafeUpdate);
                    StoryMemoryUpdate sanitizedUpdate = StoryDeterministicContinuity.SanitizeUnsafeUpdate(loaded, unsafeUpdate);
                    StoryDeterministicReport sanitizedReport = StoryDeterministicContinuity.ValidateUpdate(loaded, sanitizedUpdate);
                    if (!unsafeReport.HasConflict || sanitizedReport.HasConflict || !sanitizedUpdate.IsChapter || sanitizedUpdate.Canon.Count != 0 ||
                        !string.Equals(sanitizedUpdate.ChapterTitle, unsafeUpdate.ChapterTitle, StringComparison.Ordinal))
                        throw new InvalidOperationException("Deterministic continuity did not prune retired-canon resurrection while preserving safe manuscript metadata.");

                    string previousCondition = linhMemory.Condition;
                    linhMemory.Condition = "Dead";
                    var deadPresenceUpdate = new StoryMemoryUpdate
                    {
                        SceneStates = new List<StorySceneStatePatch>
                        {
                            new()
                            {
                                SceneLabel = "Impossible return", Location = "Old Quarter Archive", Time = "Later that night",
                                PresentCharacters = new List<string> { "Linh" }, Summary = "Linh appears without a revival event."
                            }
                        }
                    };
                    StoryDeterministicReport deadPresenceReport = StoryDeterministicContinuity.ValidateUpdate(loaded, deadPresenceUpdate);
                    linhMemory.Condition = previousCondition;
                    if (!deadPresenceReport.HasConflict || !deadPresenceReport.Issues.Any(x => x.Code == "dead-character-present"))
                        throw new InvalidOperationException("Deterministic continuity did not catch a dead-character presence conflict.");

                    var researchStore = new StoryResearchStore();
                    var research = new StoryResearchDocument();
                    research.Notes.Add(new StoryResearchNote
                    {
                        Query = "traditional umbrella materials",
                        KeyFact = "Traditional oil-paper umbrellas commonly use bamboo ribs and treated paper.",
                        Relevance = "Scene material detail",
                        Confidence = "High",
                        Sources = new List<StoryResearchSource>
                        {
                            new() { Title = "Self-test source", Url = "https://example.com/umbrella" }
                        }
                    });
                    await researchStore.SaveAsync(project.Id, research);
                    StoryResearchDocument loadedResearch = await researchStore.LoadAsync(project.Id);
                    StoryResearchNote? cachedResearch = StoryResearchStore.FindFresh(loadedResearch, "traditional umbrella materials", TimeSpan.FromDays(30));
                    if (cachedResearch is null || !StoryResearchStore.BuildContext(new[] { cachedResearch }).Contains("bamboo ribs", StringComparison.OrdinalIgnoreCase))
                        throw new InvalidOperationException("Story research cache persistence failed.");

                    project.Role = "Companion";
                    if (!StoryActions.UsesWritingPipeline(StoryActionType.DraftScene) ||
                        !StoryActions.UsesWritingPipeline(StoryActionType.ContinueStory) ||
                        StoryActions.UsesWritingPipeline(StoryActionType.Chat) ||
                        StoryActions.UsesWritingPipeline(StoryActionType.PlanChapter) ||
                        StoryActions.UsesWritingPipeline(StoryActionType.CheckContinuity))
                    {
                        throw new InvalidOperationException("Story explicit-action routing regression.");
                    }
                    project.Role = "Writer";
                    if (StoryActions.UsesWritingPipeline(StoryActionType.Chat))
                        throw new InvalidOperationException("Story role must not force a writing workflow.");
                    if (StoryAutonomyModes.Normalize(null) != StoryAutonomyModes.Balanced ||
                        StoryAutonomyModes.Normalize("Autonomous") != StoryAutonomyModes.Autonomous)
                        throw new InvalidOperationException("Story autonomy defaults/routing regression.");
                    if (!StoryActions.TryParse("ReviseCanon", out StoryActionType parsedAction) ||
                        parsedAction != StoryActionType.ReviseCanon)
                    {
                        throw new InvalidOperationException("Story action routing regression.");
                    }
                    var proposalUpdate = new StoryMemoryUpdate
                    {
                        Lore = new List<StoryLorePatch>
                        {
                            new() { Category = "World", Name = "Rain Law", Detail = "Umbrellas remember names.", Status = "Proposed" }
                        }
                    };
                    StoryMemoryUpdate? proposalRoundTrip = StoryProposalSerializer.DeserializeUpdate(
                        StoryProposalSerializer.SerializeUpdate(proposalUpdate));
                    if (proposalRoundTrip?.Lore.Count != 1 ||
                        !string.Equals(proposalRoundTrip.Lore[0].Status, "Proposed", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("Story proposal persistence regression.");
                    }
                    if (StoryProviderFactory.Get("Gemini") is not IStoryWebResearchProvider ||
                        StoryProviderFactory.Get("OpenAI") is not IStoryWebResearchProvider)
                    {
                        throw new InvalidOperationException("Story web-research provider capability is missing.");
                    }

                    // Branch checkpoint regression: rollback must restore chapter prose, memory,
                    // and conversation together; the abandoned direction must remain recoverable.
                    StoryProject branchProject = await projectStore.CreateProjectAsync($"SelfTest Branch {Guid.NewGuid():N}");
                    try
                    {
                        var branchMemory = new StoryMemoryDocument();
                        branchMemory.Canon.Add(new StoryCanonFact { Fact = "The gate is shut." });
                        StoryChapterMemory? first = await memoryStore.AppendChapterAsync(branchProject.Id,
                            branchMemory, "Opening", "The gate is shut", "The gate is firmly shut.");
                        if (first is null) throw new InvalidOperationException("Checkpoint fixture chapter missing.");
                        await memoryStore.SaveAsync(branchProject.Id, branchMemory);
                        var branchMessages = new List<StoryMessage>
                        {
                            new() { Role = "user", Content = "Write opening" },
                            new() { Role = "assistant", Content = "The gate is firmly shut.",
                                Action = "DraftScene", IsNarrativeDraft = true, ManuscriptCommitted = true,
                                ManuscriptChapterNumber = 1 }
                        };
                        await projectStore.SaveMessagesAsync(branchProject.Id, branchMessages);
                        var checkpointStore = new StoryCheckpointStore();
                        string checkpointId = await checkpointStore.CaptureAsync(branchProject.Id, branchMemory,
                            branchMessages, "Self-test pre-branch");
                        branchMemory.Canon.Add(new StoryCanonFact { Fact = "AlternateOnly gate was destroyed." });
                        StoryChapterMemory? alternate = await memoryStore.AppendChapterAsync(branchProject.Id,
                            branchMemory, "Branch A", "The gate is destroyed", "AlternateOnly gate was destroyed.");
                        if (alternate is null) throw new InvalidOperationException("Checkpoint fixture second chapter missing.");
                        await memoryStore.SaveAsync(branchProject.Id, branchMemory);
                        branchMessages.Add(new StoryMessage { Role = "assistant", Content = "AlternateOnly gate was destroyed.",
                            IsNarrativeDraft = true, ManuscriptCommitted = true, ManuscriptChapterNumber = 2 });
                        await projectStore.SaveMessagesAsync(branchProject.Id, branchMessages);
                        string rescueId = await checkpointStore.RestoreAsync(branchProject.Id, checkpointId,
                            branchMemory, branchMessages);
                        string historyDir = Path.Combine(projectStore.GetProjectDirectory(branchProject.Id),
                            "branches", "history");
                        if (Directory.Exists(historyDir) && Directory.EnumerateDirectories(historyDir).Any())
                            throw new InvalidOperationException("Successful rollback retained duplicate manuscript directories.");
                        StoryMemoryDocument rewound = await memoryStore.LoadAsync(branchProject.Id);
                        IReadOnlyList<StoryMessage> rewindMessages = await projectStore.LoadMessagesAsync(branchProject.Id);
                        if (rewound.Chapters.Count != 1 || rewound.Canon.Count != 1 ||
                            rewindMessages.Count != 2 ||
                            File.Exists(Path.Combine(memoryStore.GetChaptersDirectory(branchProject.Id), alternate.FileName)))
                            throw new InvalidOperationException("Rollback leaked an alternate chapter, canon, or conversation.");
                        IReadOnlyList<StoryChapterSearchHit> rollbackHits = await memoryStore.SearchChapterTextAsync(branchProject.Id, "AlternateOnly");
                        if (rollbackHits.Any(hit => hit.Excerpt.Contains("AlternateOnly", StringComparison.OrdinalIgnoreCase)))
                            throw new InvalidOperationException("Rollback leaked discarded chapter text into retrieval.");
                        await checkpointStore.RestoreAsync(branchProject.Id, rescueId, rewound, rewindMessages);
                        StoryMemoryDocument redone = await memoryStore.LoadAsync(branchProject.Id);
                        if (redone.Chapters.Count != 2 || redone.Canon.Count != 2 ||
                            !File.Exists(Path.Combine(memoryStore.GetChaptersDirectory(branchProject.Id), alternate.FileName)) ||
                            (await projectStore.LoadMessagesAsync(branchProject.Id)).Count != 3)
                            throw new InvalidOperationException("Alternate branch could not be restored from safety checkpoint.");
                        await checkpointStore.RecoverIfNeededAsync(branchProject.Id);
                        await StoryCheckpointCommitSelfTest.VerifyAsync(branchProject.Id,
                            checkpointStore, memoryStore, projectStore);
                        await StoryCheckpointStorageSelfTest.VerifyAsync(branchProject.Id,
                            checkpointStore, memoryStore, projectStore);
                        await StoryCheckpointStorageAuditSelfTest.VerifyAsync(branchProject.Id, projectStore);
                        await StoryCheckpointIntegrityAuditSelfTest.VerifyAsync(branchProject.Id, projectStore);
                        await StoryBranchSelfTest.VerifyAsync(branchProject.Id,
                            memoryStore, projectStore);
                        if (StoryDraftIsolation.ForGeneration(new[]
                        {
                            new StoryMessage { Role = "user", Action = "DraftScene", Content = "Try rejected A" },
                            new StoryMessage { Role = "assistant", Action = "DraftScene", Content = "Rejected A",
                                IsNarrativeDraft = true, ManuscriptCommitted = false },
                            new StoryMessage { Role = "user", Action = "Chat", Content = "Continue with B" }
                        }).Count != 1)
                            throw new InvalidOperationException("Uncommitted narrative leaked into the model context.");
                    }
                    finally
                    {
                        string branchDirectory = projectStore.GetProjectDirectory(branchProject.Id);
                        if (Directory.Exists(branchDirectory)) Directory.Delete(branchDirectory, true);
                    }

                    string utilityModel = StoryModelRouter.Resolve("Gemini", "gemini-3.8-flash", StoryAiWorkload.Utility);
                    string creativeModel = StoryModelRouter.Resolve("Gemini", "gemini-3.8-flash", StoryAiWorkload.Creative);
                    if (!string.Equals(utilityModel, "gemini-3.5-flash-lite", StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(creativeModel, "gemini-3.8-flash", StringComparison.OrdinalIgnoreCase))
                    {
                        throw new InvalidOperationException("Story model workload routing regression.");
                    }
                }
                finally
                {
                    string directory = projectStore.GetProjectDirectory(project.Id);
                    if (Directory.Exists(directory))
                        Directory.Delete(directory, true);
                }
            });

            int switchLoops = AppRuntime.IsStressSelfTest ? 120 : 20;
            await RunCase($"Rapid tab switching x{switchLoops}", async () =>
            {
                BrowserTabState[] candidates = _browserTabs.Where(t => !t.IsBlank).ToArray();
                if (candidates.Length < 2)
                    throw new InvalidOperationException("Not enough initialized tabs for switch test.");

                for (int i = 0; i < switchLoops; i++)
                {
                    BrowserTabState target = candidates[i % candidates.Length];
                    Stopwatch sw = Stopwatch.StartNew();
                    await ActivateBrowserTabAsync(target);
                    sw.Stop();
                    switchSamples.Add(sw.ElapsedMilliseconds);
                    if (_activeBrowserTab != target)
                        throw new InvalidOperationException("Active tab mismatch after switch.");
                }
            });

            await RunCase("Suspend and resume", async () =>
            {
                BrowserTabState? target = _browserTabs.FirstOrDefault(t => t.Initialized && t != _activeBrowserTab);
                if (target?.WebView.CoreWebView2 is not CoreWebView2 core)
                    throw new InvalidOperationException("No inactive initialized tab available.");
                target.WebView.Visibility = System.Windows.Visibility.Collapsed;
                bool suspended = core.IsSuspended || await core.TrySuspendAsync();
                if (!suspended && !core.IsSuspended)
                    throw new InvalidOperationException("TrySuspendAsync returned false.");
                target.Sleeping = true;
                core.Resume();
                target.Sleeping = false;
            });

            await RunCase("Pinned tab hibernation guard", async () =>
            {
                BrowserTabState? target = _browserTabs.FirstOrDefault(t => t.Initialized && t != _activeBrowserTab && !t.IsBlank);
                if (target is null)
                    throw new InvalidOperationException("No pin-test candidate.");

                target.Pinned = true;
                PersistBrowserTabs();
                int persistedIndex = _browserTabs
                    .Where(tab => !tab.Closing)
                    .Take(MaxBrowserTabs)
                    .ToList()
                    .IndexOf(target);
                if (persistedIndex < 0 || persistedIndex >= _settings.BrowserTabPinned.Count || !_settings.BrowserTabPinned[persistedIndex])
                    throw new InvalidOperationException("Pinned tab state was not persisted.");

                await HibernateBrowserTabAsync(target, "selftest-pinned");
                if (target.Hibernated || !target.Initialized)
                    throw new InvalidOperationException("Pinned tab was hibernated.");
                target.Pinned = false;
                PersistBrowserTabs();
            });

            await RunCase("Active download hibernation guard", async () =>
            {
                BrowserTabState? target = _browserTabs.FirstOrDefault(t => t.Initialized && t != _activeBrowserTab && !t.IsBlank);
                if (target is null)
                    throw new InvalidOperationException("No download-guard candidate.");
                target.ActiveDownloadCount = 1;
                try
                {
                    await HibernateBrowserTabAsync(target, "selftest-download");
                    if (target.Hibernated || !target.Initialized)
                        throw new InvalidOperationException("Tab with an active download was hibernated.");
                }
                finally
                {
                    target.ActiveDownloadCount = 0;
                }
            });

            await RunCase("Hibernate and restore", async () =>
            {
                BrowserTabState? target = _browserTabs.FirstOrDefault(t => t.Initialized && t != _activeBrowserTab && !t.IsBlank);
                if (target is null)
                    throw new InvalidOperationException("No hibernation candidate.");
                int id = target.Id;
                await HibernateBrowserTabAsync(target, "selftest");
                if (!target.Hibernated || target.Initialized)
                    throw new InvalidOperationException("Tab did not enter hibernated state.");
                await ActivateBrowserTabAsync(target);
                if (!target.Initialized || target.Hibernated || target.Id != id)
                    throw new InvalidOperationException("Tab did not restore after hibernation.");
            });

            await RunCase("Browser controller recovery", async () =>
            {
                BrowserTabState? target = _browserTabs.FirstOrDefault(t => t.Initialized && t != _activeBrowserTab && !t.IsBlank);
                if (target is null)
                    throw new InvalidOperationException("No recovery-test candidate.");
                int id = target.Id;
                await RecreateBrowserTabAfterFailureAsync(target, "selftest");
                if (target.Id != id || !target.Initialized || target.Hibernated)
                    throw new InvalidOperationException("Browser controller did not recover cleanly.");
            });

            await RunCase("Close inactive tab", async () =>
            {
                BrowserTabState? target = _browserTabs.FirstOrDefault(t => t != _activeBrowserTab);
                if (target is null) throw new InvalidOperationException("No inactive tab.");
                int before = _browserTabs.Count;
                await CloseBrowserTabAsync(target);
                if (_browserTabs.Count != before - 1)
                    throw new InvalidOperationException("Inactive tab count did not decrease.");
                await Task.Delay(120);
            });

            await RunCase("Close active tab", async () =>
            {
                BrowserTabState? target = _activeBrowserTab;
                if (target is null) throw new InvalidOperationException("No active tab.");
                int before = _browserTabs.Count;
                await CloseBrowserTabAsync(target);
                if (_browserTabs.Count != Math.Max(1, before - 1) || _activeBrowserTab is null)
                    throw new InvalidOperationException("Active tab close did not select a replacement.");
                await Task.Delay(120);
            });

            int churnLoops = AppRuntime.IsStressSelfTest ? 30 : 6;
            await RunCase($"Create-close churn x{churnLoops}", async () =>
            {
                for (int i = 0; i < churnLoops; i++)
                {
                    if (_browserTabs.Count >= MaxBrowserTabs)
                    {
                        BrowserTabState victim = _browserTabs.First(t => t != _activeBrowserTab);
                        await CloseBrowserTabAsync(victim);
                    }
                    BrowserTabState? created = await AddBrowserTabAsync($"https://example.com/?selftest={i}", activate: true);
                    if (created is null)
                        throw new InvalidOperationException("Churn tab creation failed.");
                    await Task.Delay(AppRuntime.IsStressSelfTest ? 25 : 60);
                }
            });

            await Task.Delay(600);
            await RunCase("Final browser cleanup", async () =>
            {
                DisposeBrowserTabs();
                await Dispatcher.InvokeAsync(() => { }, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
                await Task.Delay(900);
            });
        }
        catch (Exception ex)
        {
            exitCode = 1;
            results.Add(new SelfTestCaseResult { Name = "Self-test harness", Status = "FAIL", ElapsedMs = 0, Details = ex.Message });
            AppLogger.Error("SELFTEST", "Harness failed", ex);
        }

        ResourceSnapshot endSnapshot = ResourceTelemetry.Capture(_webEnvironment);
        peakBytes = Math.Max(peakBytes, endSnapshot.AppPrivateBytes);
        long medianSwitch = switchSamples.Count == 0 ? 0 : switchSamples.OrderBy(v => v).ElementAt(switchSamples.Count / 2);
        int passed = results.Count(r => r.Status == "PASS");
        int failed = results.Count(r => r.Status == "FAIL");
        int warnings = 0;
        long growthMiB = (endSnapshot.AppPrivateBytes - startSnapshot.AppPrivateBytes) / 1024 / 1024;
        if (growthMiB > (AppRuntime.IsStressSelfTest ? 900 : 650))
            warnings++;

        var baseline = new SelfTestBaseline
        {
            Mode = mode,
            StartupMs = startupMs,
            MedianSwitchMs = medianSwitch,
            PeakMemoryMiB = peakBytes / 1024 / 1024,
            EndMemoryMiB = endSnapshot.AppPrivateBytes / 1024 / 1024,
            Passed = passed,
            Failed = failed,
            Warnings = warnings
        };
        SelfTestBaseline? previous = PerformanceBaselineStore.LoadLast();
        PerformanceBaselineStore.Append(baseline);

        string report = BuildSelfTestReport(results, baseline, previous, startSnapshot, endSnapshot, growthMiB);
        // Self-tests never write into the installed app directory. All reports
        // are isolated from the user's real Story and WebView2 profile.
        Directory.CreateDirectory(AppRuntime.DataDirectory);
        string reportPath = Path.Combine(AppRuntime.DataDirectory, "SELFTEST-REPORT.txt");
        File.WriteAllText(reportPath, report, Encoding.UTF8);

        AppLogger.Info("SELFTEST", $"Finished | pass={passed} | fail={failed} | warnings={warnings} | report={reportPath}");
        CrashDiagnostics.UpdateContext("SelfTest", string.Empty, "selftest-finished");
        await Task.Delay(300);

        _isExiting = true;
        _memorySaverTimer.Stop();
        _smartWakeHoverTimer.Stop();
        _responsiveZoomTimer?.Stop();
        if (_browserTabs.Count > 0)
            DisposeBrowserTabs();
        System.Windows.Application.Current.Shutdown(exitCode);
    }

    private static string BuildSelfTestReport(
        IReadOnlyList<SelfTestCaseResult> results,
        SelfTestBaseline current,
        SelfTestBaseline? previous,
        ResourceSnapshot start,
        ResourceSnapshot end,
        long growthMiB)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"WorkChat Mini {AppRuntime.BuildText} Self Test");
        sb.AppendLine(new string('=', 56));
        sb.AppendLine($"Mode: {current.Mode}");
        sb.AppendLine($"Time: {DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss zzz}");
        sb.AppendLine();
        foreach (SelfTestCaseResult result in results)
        {
            sb.Append($"{result.Status,-5}  {result.Name,-32} {result.ElapsedMs,6} ms");
            if (!string.IsNullOrWhiteSpace(result.Details)) sb.Append($"  {result.Details}");
            sb.AppendLine();
        }
        sb.AppendLine();
        sb.AppendLine($"Result: {current.Passed} PASS / {current.Failed} FAIL / {current.Warnings} WARN");
        sb.AppendLine($"Startup: {current.StartupMs} ms");
        sb.AppendLine($"Median tab switch: {current.MedianSwitchMs} ms");
        sb.AppendLine($"Memory start: {start.AppPrivateMiB:0} MiB");
        sb.AppendLine($"Memory peak: {current.PeakMemoryMiB} MiB");
        sb.AppendLine($"Memory end: {current.EndMemoryMiB} MiB");
        sb.AppendLine($"Memory growth: {growthMiB:+0;-0;0} MiB");
        sb.AppendLine($"WebView processes at end: {end.WebViewProcessCount}");
        sb.AppendLine($"Process breakdown: {end.ProcessBreakdownText()}");
        if (current.Warnings > 0)
            sb.AppendLine("WARN: memory growth exceeded the soft self-test threshold; send this report with latest.log if it repeats.");

        if (previous is not null)
        {
            sb.AppendLine();
            sb.AppendLine("Compared with previous self test:");
            sb.AppendLine($"  Previous: v{previous.Version} ({previous.Mode}) at {previous.Timestamp:yyyy-MM-dd HH:mm}");
            sb.AppendLine($"  Startup delta: {current.StartupMs - previous.StartupMs:+0;-0;0} ms");
            sb.AppendLine($"  Median switch delta: {current.MedianSwitchMs - previous.MedianSwitchMs:+0;-0;0} ms");
            sb.AppendLine($"  Peak memory delta: {current.PeakMemoryMiB - previous.PeakMemoryMiB:+0;-0;0} MiB");
        }

        sb.AppendLine();
        sb.AppendLine($"Logs: {AppLogger.LogsDirectory}");
        sb.AppendLine($"Crash reports: {CrashDiagnostics.CrashReportsDirectory}");
        return sb.ToString();
    }
}
