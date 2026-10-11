using System.Text;

namespace WorkChatMini;

public enum StoryDeterministicSeverity
{
    Info,
    Risk,
    Conflict
}

public sealed class StoryDeterministicIssue
{
    public string Code { get; init; } = string.Empty;
    public StoryDeterministicSeverity Severity { get; init; }
    public string Message { get; init; } = string.Empty;
}

public sealed class StoryDeterministicReport
{
    public List<StoryDeterministicIssue> Issues { get; } = new();
    public bool HasConflict => Issues.Any(x => x.Severity == StoryDeterministicSeverity.Conflict);
    public bool HasRisk => Issues.Any(x => x.Severity == StoryDeterministicSeverity.Risk);

    public string BuildPromptContext(int maxItems = 10)
    {
        if (Issues.Count == 0)
            return string.Empty;
        var sb = new StringBuilder("DETERMINISTIC STATE CHECK — derived from structured Story Memory. Treat conflicts as hard evidence; risks require interpretation:");
        foreach (StoryDeterministicIssue issue in Issues
                     .OrderByDescending(x => x.Severity)
                     .Take(maxItems))
        {
            sb.AppendLine();
            sb.Append("- [").Append(issue.Severity).Append("] ").Append(issue.Message);
        }
        return sb.ToString();
    }
}

public static class StoryDeterministicContinuity
{
    private static readonly string[] DeadMarkers =
    {
        "confirmed dead", "is dead", "was killed", "has died", "deceased", "corpse",
        "đã chết", "đã tử vong", "tử vong", "đã thiệt mạng", "thiệt mạng"
    };

    private static readonly string[] UnavailableLocationMarkers =
    {
        "destroyed", "collapsed", "sealed", "inaccessible", "unavailable", "ruined",
        "bị phá hủy", "sụp đổ", "niêm phong", "không thể tiếp cận"
    };

    private static readonly string[] RevivalMarkers =
    {
        "revival", "revived", "resurrect", "resurrection", "returned to life", "hồi sinh", "sống lại"
    };

    public static StoryDeterministicReport AnalyzeMemory(StoryMemoryDocument memory)
    {
        var report = new StoryDeterministicReport();
        CheckDuplicateCurrentPresence(memory, report);
        CheckCharacterLocationAgainstWorld(memory, report);
        CheckRelationshipDuplicates(memory, report);
        CheckCanonState(memory, report);
        CheckNarrativePromiseState(memory, report);
        return report;
    }

    public static StoryDeterministicReport ValidateUpdate(StoryMemoryDocument memory, StoryMemoryUpdate update)
    {
        // Bookkeeping JSON may contain explicit null arrays; validate the same
        // normalized shape that will later be merged into durable memory.
        update = StoryMemoryUpdateNormalizer.Normalize(update);
        var report = new StoryDeterministicReport();
        CheckRetiredCanonResurrection(memory, update, report);
        CheckCharacterDeltas(memory, update, report);
        StoryEpistemicUpdateGuard.Check(memory, update, report);
        StoryCharacterDevelopmentGuard.Check(memory, update, report);
        CheckEventDeltas(memory, update, report);
        CheckSceneStates(memory, update, report);
        CheckLocationPatches(memory, update, report);
        CheckRelationshipPatches(update, report);
        StoryRelationshipDecisionGuard.Check(memory, update, report);
        StoryCharacterMotivationGuard.Check(memory, update, report);
        return report;
    }

    public static StoryMemoryUpdate SanitizeUnsafeUpdate(StoryMemoryDocument memory, StoryMemoryUpdate update)
    {
        update.Canon.RemoveAll(patch => memory.Canon.Any(fact => IsRetiredCanon(fact) && Same(fact.Fact, patch.Fact)));
        update.Relationships.RemoveAll(patch =>
            !string.IsNullOrWhiteSpace(patch.From) &&
            (Same(patch.From, patch.To) ||
             (!string.IsNullOrWhiteSpace(patch.To) &&
              !StoryRelationshipLedger.TryCanonicalPair(memory, patch.From, patch.To, out _, out _))));

        var deadWithoutRevival = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (StoryCharacterMemory character in memory.Characters.Where(x => LooksDead(x.Condition)))
        {
            if (UpdateContainsRevival(update, character.Name))
                continue;
            deadWithoutRevival.Add(character.Name);
            foreach (string alias in character.Aliases.Where(x => !string.IsNullOrWhiteSpace(x)))
                deadWithoutRevival.Add(alias);
        }

        if (deadWithoutRevival.Count > 0)
        {
            update.SceneStates.RemoveAll(scene => scene.PresentCharacters.Any(name => deadWithoutRevival.Contains(name)));
            foreach (StoryLocationPatch location in update.Locations)
                location.PresentCharacters.RemoveAll(name => deadWithoutRevival.Contains(name));
            foreach (StoryCharacterPatch character in update.Characters.Where(x => deadWithoutRevival.Contains(x.Name)))
                character.CurrentLocation = string.Empty;
        }

        if (HasSameTimeDuplicatePresence(update.SceneStates))
            update.SceneStates.Clear();

        return update;
    }

    public static StoryMemoryUpdate ToSafeChapterOnlyUpdate(StoryMemoryUpdate update)
    {
        return new StoryMemoryUpdate
        {
            IsChapter = update.IsChapter,
            ChapterTitle = update.ChapterTitle,
            ChapterSummary = update.ChapterSummary
        };
    }

    private static void CheckDuplicateCurrentPresence(StoryMemoryDocument memory, StoryDeterministicReport report)
    {
        var presence = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (StoryLocationMemory location in memory.Locations.Where(IsAvailableLocation))
        {
            foreach (string character in location.PresentCharacters.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                if (!presence.TryGetValue(character.Trim(), out List<string>? locations) || locations is null)
                {
                    locations = new List<string>();
                    presence[character.Trim()] = locations;
                }
                if (!locations.Any(x => Same(x, location.Name)))
                    locations.Add(location.Name);
            }
        }

        foreach ((string character, List<string> locations) in presence.Where(x => x.Value.Count > 1))
        {
            report.Issues.Add(new StoryDeterministicIssue
            {
                Code = "duplicate-current-presence",
                Severity = StoryDeterministicSeverity.Conflict,
                Message = $"{character} is marked present in multiple current locations: {string.Join(", ", locations)}."
            });
        }
    }

    private static void CheckCharacterLocationAgainstWorld(StoryMemoryDocument memory, StoryDeterministicReport report)
    {
        foreach (StoryCharacterMemory character in memory.Characters.Where(x => !string.IsNullOrWhiteSpace(x.CurrentLocation)))
        {
            StoryLocationMemory? location = memory.Locations.FirstOrDefault(x => Same(x.Name, character.CurrentLocation));
            if (location is not null && !IsAvailableLocation(location))
            {
                report.Issues.Add(new StoryDeterministicIssue
                {
                    Code = "character-at-unavailable-location",
                    Severity = StoryDeterministicSeverity.Risk,
                    Message = $"{character.Name} is currently located at '{location.Name}', whose state is '{location.Status}'."
                });
            }

            StoryLocationMemory[] listedElsewhere = memory.Locations
                .Where(IsAvailableLocation)
                .Where(x => !Same(x.Name, character.CurrentLocation) &&
                            x.PresentCharacters.Any(name => Same(name, character.Name) || character.Aliases.Any(alias => Same(alias, name))))
                .ToArray();
            if (listedElsewhere.Length > 0)
            {
                report.Issues.Add(new StoryDeterministicIssue
                {
                    Code = "character-location-state-mismatch",
                    Severity = StoryDeterministicSeverity.Conflict,
                    Message = $"{character.Name} currentLocation is '{character.CurrentLocation}' but location-state marks the character present at {string.Join(", ", listedElsewhere.Select(x => x.Name))}."
                });
            }
        }
    }

    private static void CheckRelationshipDuplicates(StoryMemoryDocument memory, StoryDeterministicReport report)
    {
        foreach (IGrouping<string, StoryRelationshipMemory> group in memory.Relationships.GroupBy(
                     x => StoryRelationshipLedger.TryCanonicalPair(memory, x.From, x.To,
                         out string from, out string to)
                         ? PairKey(from, to) : PairKey(x.From, x.To),
                     StringComparer.OrdinalIgnoreCase))
        {
            string[] states = group.Select(x => x.State.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (states.Length <= 1)
                continue;
            StoryRelationshipMemory first = group.First();
            report.Issues.Add(new StoryDeterministicIssue
            {
                Code = "relationship-current-state-conflict",
                Severity = StoryDeterministicSeverity.Conflict,
                Message = $"Relationship {first.From} → {first.To} has multiple current states: {string.Join(", ", states)}."
            });
        }
    }

    private static void CheckCanonState(StoryMemoryDocument memory, StoryDeterministicReport report)
    {
        string[] retired = memory.Canon.Where(IsRetiredCanon).Select(x => x.Fact).ToArray();
        foreach (StoryCanonFact current in memory.Canon.Where(x => !IsRetiredCanon(x)))
        {
            if (!retired.Any(x => Same(x, current.Fact)))
                continue;
            report.Issues.Add(new StoryDeterministicIssue
            {
                Code = "canon-retired-and-current",
                Severity = StoryDeterministicSeverity.Conflict,
                Message = $"The same canon fact is both retired and current: '{current.Fact}'."
            });
        }
    }

    private static void CheckNarrativePromiseState(StoryMemoryDocument memory, StoryDeterministicReport report)
    {
        foreach (IGrouping<string, StoryNarrativePromise> group in memory.NarrativePromises
                     .Where(x => !string.IsNullOrWhiteSpace(x.Title))
                     .GroupBy(x => x.Title.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            StoryNarrativePromise[] active = group.Where(StoryCausalMemoryLedger.IsActive).ToArray();
            if (active.Length > 1)
            {
                report.Issues.Add(new StoryDeterministicIssue
                {
                    Code = "duplicate-active-narrative-promise",
                    Severity = StoryDeterministicSeverity.Risk,
                    Message = $"Multiple active setup/payoff records share title '{group.Key}'. They may be duplicates."
                });
            }
        }

        foreach (StoryNarrativePromise promise in memory.NarrativePromises)
        {
            if (StoryCausalMemoryLedger.IsActive(promise) && promise.PaidOffChapter is not null)
            {
                report.Issues.Add(new StoryDeterministicIssue
                {
                    Code = "active-promise-has-payoff",
                    Severity = StoryDeterministicSeverity.Conflict,
                    Message = $"Narrative promise '{promise.Title}' is active but already has payoff provenance."
                });
            }
            else if (string.Equals(promise.Status, "PaidOff", StringComparison.OrdinalIgnoreCase) && promise.PaidOffChapter is null)
            {
                report.Issues.Add(new StoryDeterministicIssue
                {
                    Code = "payoff-missing-provenance",
                    Severity = StoryDeterministicSeverity.Risk,
                    Message = $"Narrative promise '{promise.Title}' is PaidOff but has no payoff chapter provenance."
                });
            }
        }
    }

    private static void CheckRetiredCanonResurrection(StoryMemoryDocument memory, StoryMemoryUpdate update, StoryDeterministicReport report)
    {
        foreach (StoryCanonFactPatch patch in update.Canon.Where(x => !string.IsNullOrWhiteSpace(x.Fact)))
        {
            StoryCanonFact? retired = memory.Canon.FirstOrDefault(x => IsRetiredCanon(x) && Same(x.Fact, patch.Fact));
            if (retired is null)
                continue;
            report.Issues.Add(new StoryDeterministicIssue
            {
                Code = "retired-canon-reintroduced",
                Severity = StoryDeterministicSeverity.Conflict,
                Message = $"Bookkeeping attempted to reintroduce retired canon as current truth: '{patch.Fact}'. Use Revise Canon if this is intentional."
            });
        }
    }

    private static void CheckCharacterDeltas(StoryMemoryDocument memory, StoryMemoryUpdate update, StoryDeterministicReport report)
    {
        foreach (StoryCharacterPatch patch in update.Characters.Where(x => !string.IsNullOrWhiteSpace(x.Name)))
        {
            StoryCharacterMemory? current = FindCharacter(memory, patch.Name);
            if (current is null)
                continue;

            AddMissingRemovalRisks(report, patch.Name, "ability", patch.RemovedAbilities, current.Abilities);
            AddMissingRemovalRisks(report, patch.Name, "item", patch.RemovedInventory, current.Inventory);
            AddMissingRemovalRisks(report, patch.Name, "knowledge", patch.ForgottenKnowledge, current.Knowledge);
            AddMissingRemovalRisks(report, patch.Name, "goal", patch.RemovedGoals, current.Goals);

            foreach (string ability in patch.Abilities.Where(x => current.Abilities.Any(y => Same(x, y))))
                AddInfo(report, "ability-already-known", $"{patch.Name} already has ability '{ability}'; bookkeeping may be recording the same progression twice.");
            foreach (string item in patch.Inventory.Where(x => current.Inventory.Any(y => Same(x, y))))
                AddInfo(report, "item-already-held", $"{patch.Name} already possesses '{item}'; bookkeeping may be recording the same acquisition twice.");

            if (!string.IsNullOrWhiteSpace(patch.CurrentLocation))
            {
                StoryLocationMemory? location = memory.Locations.FirstOrDefault(x => Same(x.Name, patch.CurrentLocation));
                if (location is not null && !IsAvailableLocation(location) && !UpdateReactivatesLocation(update, location.Name))
                {
                    report.Issues.Add(new StoryDeterministicIssue
                    {
                        Code = "move-to-unavailable-location",
                        Severity = StoryDeterministicSeverity.Risk,
                        Message = $"{patch.Name} is being moved to '{location.Name}', currently marked '{location.Status}'."
                    });
                }
            }
        }
    }

    private static void CheckEventDeltas(StoryMemoryDocument memory, StoryMemoryUpdate update, StoryDeterministicReport report)
    {
        foreach (StoryEventPatch storyEvent in update.Events)
        {
            StoryCharacterMemory? subject = FindCharacter(memory, storyEvent.Subject);
            if (subject is null)
                continue;

            if (TypeIs(storyEvent, "AbilityLost") && !subject.Abilities.Any(x => Same(x, storyEvent.Target)))
                AddRisk(report, "ability-lost-without-possession", $"{storyEvent.Subject} is recorded as losing ability '{storyEvent.Target}', but current state does not contain it.");
            if (TypeIs(storyEvent, "ItemLost") && !subject.Inventory.Any(x => Same(x, storyEvent.Target)))
                AddRisk(report, "item-lost-without-possession", $"{storyEvent.Subject} is recorded as losing '{storyEvent.Target}', but current inventory does not contain it.");
            if (TypeIs(storyEvent, "AbilityLearned") && subject.Abilities.Any(x => Same(x, storyEvent.Target)))
                AddInfo(report, "ability-learned-twice", $"{storyEvent.Subject} is recorded as learning '{storyEvent.Target}', which is already in current abilities.");
            if (TypeIs(storyEvent, "ItemAcquired") && subject.Inventory.Any(x => Same(x, storyEvent.Target)))
                AddInfo(report, "item-acquired-twice", $"{storyEvent.Subject} is recorded as acquiring '{storyEvent.Target}', which is already in current inventory.");
        }
    }

    private static void CheckSceneStates(StoryMemoryDocument memory, StoryMemoryUpdate update, StoryDeterministicReport report)
    {
        foreach (StorySceneStatePatch scene in update.SceneStates)
        {
            StoryLocationMemory? knownLocation = memory.Locations.FirstOrDefault(x => Same(x.Name, scene.Location));
            if (knownLocation is not null && !IsAvailableLocation(knownLocation) && !UpdateReactivatesLocation(update, knownLocation.Name))
            {
                report.Issues.Add(new StoryDeterministicIssue
                {
                    Code = "scene-at-unavailable-location",
                    Severity = StoryDeterministicSeverity.Risk,
                    Message = $"Scene '{scene.SceneLabel}' is set at '{knownLocation.Name}', currently marked '{knownLocation.Status}'."
                });
            }

            foreach (string characterName in scene.PresentCharacters)
            {
                StoryCharacterMemory? character = FindCharacter(memory, characterName);
                if (character is null || !LooksDead(character.Condition) || UpdateContainsRevival(update, characterName))
                    continue;
                report.Issues.Add(new StoryDeterministicIssue
                {
                    Code = "dead-character-present",
                    Severity = StoryDeterministicSeverity.Conflict,
                    Message = $"{character.Name} is marked '{character.Condition}' but appears as present in scene '{scene.SceneLabel}' without an explicit revival event."
                });
            }
        }

        foreach (IGrouping<string, StorySceneStatePatch> timeGroup in update.SceneStates
                     .Where(x => !string.IsNullOrWhiteSpace(x.Time) && !string.IsNullOrWhiteSpace(x.Location))
                     .GroupBy(x => x.Time.Trim(), StringComparer.OrdinalIgnoreCase))
        {
            var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (StorySceneStatePatch scene in timeGroup)
            {
                foreach (string character in scene.PresentCharacters.Where(x => !string.IsNullOrWhiteSpace(x)))
                {
                    if (!seen.TryGetValue(character.Trim(), out string? priorLocation) || priorLocation is null)
                    {
                        seen[character.Trim()] = scene.Location;
                        continue;
                    }
                    if (Same(priorLocation, scene.Location))
                        continue;
                    bool precise = IsPreciseTime(timeGroup.Key);
                    report.Issues.Add(new StoryDeterministicIssue
                    {
                        Code = precise ? "same-time-multiple-locations" : "broad-time-multiple-locations",
                        Severity = precise ? StoryDeterministicSeverity.Conflict : StoryDeterministicSeverity.Risk,
                        Message = precise
                            ? $"{character} is present at both '{priorLocation}' and '{scene.Location}' at the same recorded time '{timeGroup.Key}'."
                            : $"{character} appears at both '{priorLocation}' and '{scene.Location}' under the broad time label '{timeGroup.Key}'. Verify that travel/order is explicit."
                    });
                }
            }
        }
    }

    private static bool HasSameTimeDuplicatePresence(IEnumerable<StorySceneStatePatch> scenes)
    {
        foreach (IGrouping<string, StorySceneStatePatch> timeGroup in scenes
                     .Where(x => !string.IsNullOrWhiteSpace(x.Time) && !string.IsNullOrWhiteSpace(x.Location))
                     .GroupBy(x => x.Time.Trim(), StringComparer.OrdinalIgnoreCase)
                     .Where(x => IsPreciseTime(x.Key)))
        {
            var seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (StorySceneStatePatch scene in timeGroup)
            {
                foreach (string character in scene.PresentCharacters.Where(x => !string.IsNullOrWhiteSpace(x)))
                {
                    if (!seen.TryGetValue(character.Trim(), out string? priorLocation) || priorLocation is null)
                    {
                        seen[character.Trim()] = scene.Location;
                        continue;
                    }
                    if (!Same(priorLocation, scene.Location))
                        return true;
                }
            }
        }
        return false;
    }

    private static void CheckLocationPatches(StoryMemoryDocument memory, StoryMemoryUpdate update, StoryDeterministicReport report)
    {
        var currentPresence = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (StoryLocationMemory location in memory.Locations.Where(IsAvailableLocation))
        {
            foreach (string character in location.PresentCharacters)
                currentPresence[character] = location.Name;
        }

        foreach (StoryLocationPatch location in update.Locations.Where(x => IsAvailableStatus(x.Status)))
        {
            foreach (string character in location.PresentCharacters.Where(x => !string.IsNullOrWhiteSpace(x)))
            {
                if (!currentPresence.TryGetValue(character.Trim(), out string? prior) || prior is null || Same(prior, location.Name))
                    continue;
                bool sceneExplainsMove = update.SceneStates.Any(x => Same(x.Location, location.Name) && x.PresentCharacters.Any(c => Same(c, character)));
                if (!sceneExplainsMove)
                    AddRisk(report, "location-presence-move-without-scene", $"{character} moves from '{prior}' to '{location.Name}' in current-location state without a matching scene-state record.");
            }
        }
    }

    private static void CheckRelationshipPatches(StoryMemoryUpdate update, StoryDeterministicReport report)
    {
        foreach (StoryRelationshipPatch patch in update.Relationships)
        {
            if (!string.IsNullOrWhiteSpace(patch.From) && Same(patch.From, patch.To))
            {
                report.Issues.Add(new StoryDeterministicIssue
                {
                    Code = "self-relationship",
                    Severity = StoryDeterministicSeverity.Conflict,
                    Message = $"Invalid relationship patch: '{patch.From}' points to itself."
                });
            }
        }

        foreach (IGrouping<string, StoryRelationshipPatch> group in update.Relationships
                     .Where(x => !string.IsNullOrWhiteSpace(x.From) && !string.IsNullOrWhiteSpace(x.To))
                     .GroupBy(x => PairKey(x.From, x.To), StringComparer.OrdinalIgnoreCase))
        {
            string[] states = group.Select(x => x.State.Trim()).Where(x => x.Length > 0).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
            if (states.Length <= 1)
                continue;
            StoryRelationshipPatch first = group.First();
            AddRisk(report, "multiple-relationship-states-in-update",
                $"One bookkeeping update assigns multiple relationship states to {first.From} → {first.To}: {string.Join(", ", states)}.");
        }
    }

    private static void AddMissingRemovalRisks(
        StoryDeterministicReport report,
        string character,
        string kind,
        IEnumerable<string> removals,
        IReadOnlyCollection<string> current)
    {
        foreach (string value in removals.Where(x => !string.IsNullOrWhiteSpace(x)))
        {
            if (current.Any(x => Same(x, value)))
                continue;
            AddRisk(report, $"{kind}-removal-not-in-current-state",
                $"{character} is recorded as losing/removing {kind} '{value}', but current structured state does not contain it.");
        }
    }

    private static StoryCharacterMemory? FindCharacter(StoryMemoryDocument memory, string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            return null;
        return StoryCharacterIdentity.ResolveUnique(memory, name, out _);
    }

    private static bool UpdateContainsRevival(StoryMemoryUpdate update, string character)
    {
        return update.Events.Any(x =>
            (Same(x.Subject, character) || x.RelatedCharacters.Any(c => Same(c, character))) &&
            (RevivalMarkers.Any(marker => x.Type.Contains(marker, StringComparison.OrdinalIgnoreCase)) ||
             RevivalMarkers.Any(marker => x.Summary.Contains(marker, StringComparison.OrdinalIgnoreCase)) ||
             RevivalMarkers.Any(marker => x.After.Contains(marker, StringComparison.OrdinalIgnoreCase))));
    }

    private static bool UpdateReactivatesLocation(StoryMemoryUpdate update, string location) =>
        update.Locations.Any(x => Same(x.Name, location) && IsAvailableStatus(x.Status));

    private static bool LooksDead(string condition)
    {
        if (string.IsNullOrWhiteSpace(condition))
            return false;
        string clean = condition.Trim();
        if (string.Equals(clean, "Dead", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(clean, "Deceased", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(clean, "Died", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(clean, "Killed", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(clean, "Đã chết", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(clean, "Tử vong", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(clean, "Thiệt mạng", StringComparison.OrdinalIgnoreCase))
            return true;
        return DeadMarkers.Any(marker => ContainsMarker(clean, marker));
    }

    private static bool IsPreciseTime(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        int digits = value.Count(char.IsDigit);
        if (digits >= 3 && value.Contains(':'))
            return true;
        string lower = value.ToLowerInvariant();
        return lower.Contains("same moment", StringComparison.Ordinal) ||
               lower.Contains("simultaneously", StringComparison.Ordinal) ||
               lower.Contains("cùng lúc", StringComparison.Ordinal) ||
               lower.Contains("đồng thời", StringComparison.Ordinal);
    }

    private static bool ContainsMarker(string value, string marker)
    {
        int start = 0;
        while ((start = value.IndexOf(marker, start, StringComparison.OrdinalIgnoreCase)) >= 0)
        {
            int before = start - 1;
            int after = start + marker.Length;
            bool leftBoundary = before < 0 || !char.IsLetterOrDigit(value[before]);
            bool rightBoundary = after >= value.Length || !char.IsLetterOrDigit(value[after]);
            if (leftBoundary && rightBoundary)
                return true;
            start += Math.Max(1, marker.Length);
        }
        return false;
    }

    private static bool IsAvailableLocation(StoryLocationMemory location) => IsAvailableStatus(location.Status);

    private static bool IsAvailableStatus(string status)
    {
        if (string.IsNullOrWhiteSpace(status))
            return true;
        return !UnavailableLocationMarkers.Any(marker => status.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool TypeIs(StoryEventPatch storyEvent, string type) =>
        string.Equals(storyEvent.Type?.Trim(), type, StringComparison.OrdinalIgnoreCase);

    private static bool IsRetiredCanon(StoryCanonFact fact) =>
        string.Equals(fact.Status, "Retconned", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(fact.Status, "Contradicted", StringComparison.OrdinalIgnoreCase);

    private static string PairKey(string from, string to) => from.Trim() + "→" + to.Trim();
    private static bool Same(string left, string right) => string.Equals(left?.Trim(), right?.Trim(), StringComparison.OrdinalIgnoreCase);

    private static void AddInfo(StoryDeterministicReport report, string code, string message) =>
        report.Issues.Add(new StoryDeterministicIssue { Code = code, Severity = StoryDeterministicSeverity.Info, Message = message });

    private static void AddRisk(StoryDeterministicReport report, string code, string message) =>
        report.Issues.Add(new StoryDeterministicIssue { Code = code, Severity = StoryDeterministicSeverity.Risk, Message = message });
}
