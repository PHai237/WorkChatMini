using System.Text;
using System.Text.RegularExpressions;

namespace WorkChatMini;

/// <summary>Creator-approved directions live in conversation.json, which branch checkpoints
/// already snapshot. Unaccepted assistant brainstorming never becomes durable direction.</summary>
internal static class StoryCoCreation
{
    private static readonly string[] ExplicitPrefixes =
    {
        "chốt ý:", "chốt:", "ghi nhớ ý này:", "keep this idea:", "story direction:"
    };
    private static readonly Regex IdeaSelection = new(
        @"^(?:chốt|chọn)\s*:?[ ]*(?:ý|phương án|option)\s*([1-3])[.!]?\s*$",
        RegexOptions.Compiled | RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal const int MaxDirectionLength = 480;
    internal const int MaxContextLength = 2300;

    internal static string NormalizeDirection(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return string.Empty;
        string singleLine = Regex.Replace(text.Trim(), @"\s+", " ");
        return singleLine.Length <= MaxDirectionLength ? singleLine : singleLine[..MaxDirectionLength].TrimEnd();
    }

    internal static void CaptureCreatorConfirmation(
        StoryMessage message, IReadOnlyList<StoryMessage>? earlierMessages = null)
    {
        if (message.Role != "user" || message.Action != StoryActionType.Chat.ToString()) return;
        string value = message.Content.TrimStart();
        Match selection = IdeaSelection.Match(value);
        if (selection.Success && earlierMessages is not null)
        {
            int number = int.Parse(selection.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            // Only resolve against the immediately preceding response. Searching
            // backward through older replies could accept an unrelated old idea.
            StoryMessage? lastProposal = earlierMessages.LastOrDefault();
            if (lastProposal?.Role != "assistant" ||
                lastProposal.Action != StoryActionType.Chat.ToString() ||
                lastProposal.DraftDiscarded || lastProposal.IsNarrativeDraft ||
                lastProposal.IsInterruptedResponse)
                return;
            string selected = StoryCreativeSuggestionExtractor.ResolveSelection(lastProposal.Content, number);
            if (!string.IsNullOrWhiteSpace(selected))
                message.CreativeDirection = NormalizeDirection(selected);
            return;
        }
        foreach (string prefix in ExplicitPrefixes)
        {
            if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) continue;
            // "Chốt: phương án 2" must resolve to the actual idea, not store a
            // meaningless pointer when the matching assistant proposal is missing.
            if (Regex.IsMatch(value[prefix.Length..].Trim(),
                @"^(?:ý|phương án|option)\s*\d+\b", RegexOptions.IgnoreCase))
                return;
            message.CreativeDirection = NormalizeDirection(value[prefix.Length..]);
            return;
        }
    }

    internal static IReadOnlyList<StoryMessage> ActiveDirections(IReadOnlyList<StoryMessage> messages) =>
        messages.Where(m => !m.CreativeDirectionArchived &&
                !string.IsNullOrWhiteSpace(m.CreativeDirection) &&
                !m.DraftDiscarded && !m.IsInterruptedResponse &&
                (!m.IsNarrativeDraft || m.ManuscriptCommitted) &&
                (m.Action == StoryActionType.Chat.ToString() || m.ManuscriptCommitted))
            .ToArray();

    internal static string BuildContext(IReadOnlyList<StoryMessage> messages)
    {
        IReadOnlyList<string> declined = StoryCreativeDecision.ActiveDeclines(messages);
        var chosen = new List<string>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int used = 0;
        foreach (StoryMessage message in ActiveDirections(messages).Reverse().Take(32))
        {
            string direction = NormalizeDirection(message.CreativeDirection);
            if (direction.Length == 0 || seen.Contains(direction)) continue;
            string category = StoryIdeaOrganizer.NormalizeCategory(message.CreativeDirectionCategory);
            string label = category == StoryIdeaOrganizer.General ? string.Empty : $"[{category}] ";
            string line = "- " + label + direction;
            // Reserve space for headers and explicitly declined suggestions.
            if (used + line.Length + 1 > MaxContextLength - (declined.Count > 0 ? 900 : 330)) break;
            chosen.Add(line);
            seen.Add(direction);
            used += line.Length + 1;
            if (chosen.Count >= 12) break;
        }
        if (chosen.Count == 0 && declined.Count == 0) return string.Empty;
        chosen.Reverse();
        var context = new StringBuilder(MaxContextLength);
        if (chosen.Count > 0)
        {
            context.Append("CREATOR-APPROVED WORKING DIRECTIONS (NOT STORY CANON):\n")
                .Append("These are editorial decisions, not events in the fictional world. ")
                .Append("Later explicit user instructions and confirmed canon take precedence.\n")
                .Append(string.Join("\n", chosen));
        }
        if (declined.Count > 0)
        {
            const string header = "CREATOR-DECLINED SUGGESTIONS (NOT STORY CANON):\n" +
                "The creator explicitly passed on these proposals. Avoid proposing the same ideas again unless asked; do not treat this list as events or world facts.\n";
            int remaining = MaxContextLength - context.Length - header.Length - 2;
            var compact = new List<string>();
            foreach (string item in declined)
            {
                string line = "- " + item;
                if (line.Length + 1 > remaining) break;
                compact.Add(line);
                remaining -= line.Length + 1;
                if (compact.Count >= 3) break;
            }
            if (compact.Count > 0)
            {
                if (context.Length > 0) context.Append("\n\n");
                context.Append(header).Append(string.Join("\n", compact));
            }
        }
        return context.ToString();
    }

}
