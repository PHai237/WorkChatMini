using System.Text;

namespace WorkChatMini;

// A single character budget for the memory + derived context sent to writer stages.
// Limits are deterministic and do not alter durable story facts or retrieval data.
internal static class StoryContextBudget
{
    internal const int MaxTotalCharacters = 24000;
    internal const int MaxMemoryCharacters = 22000;

    internal static int MemoryAllowance(
        string deterministic,
        string narrative,
        string retrievalFocus,
        string knowledgeGraph,
        string creativeDirections = "")
    {
        int extras = Math.Min(1800, deterministic.Length) +
                     Math.Min(2400, narrative.Length) +
                     Math.Min(1000, retrievalFocus.Length) +
                     Math.Min(1200, knowledgeGraph.Length) +
                     Math.Min(StoryCoCreation.MaxContextLength, creativeDirections.Length);
        return Math.Clamp(MaxTotalCharacters - extras - 8, 1024, MaxMemoryCharacters);
    }

    internal static string Compose(
        string memory,
        string deterministic,
        string narrative,
        string retrievalFocus,
        string knowledgeGraph,
        string creativeDirections = "")
    {
        var result = new StringBuilder(MaxTotalCharacters);
        Append(result, memory, MemoryAllowance(deterministic, narrative, retrievalFocus, knowledgeGraph, creativeDirections));
        // Keep conflict warnings and current narrative state ahead of graph expansions.
        Append(result, deterministic, 1800);
        Append(result, creativeDirections, StoryCoCreation.MaxContextLength);
        Append(result, narrative, 2400);
        Append(result, retrievalFocus, 1000);
        Append(result, knowledgeGraph, 1200);
        return result.ToString();
    }

    private static void Append(StringBuilder output, string section, int sectionLimit)
    {
        if (string.IsNullOrWhiteSpace(section))
            return;
        int separator = output.Length == 0 ? 0 : 2;
        int allowed = Math.Min(sectionLimit, MaxTotalCharacters - output.Length - separator);
        if (allowed <= 0)
            return;
        if (separator > 0)
            output.Append("\n\n");
        if (section.Length <= allowed)
        {
            output.Append(section);
            return;
        }
        const string truncated = "\n[Context abbreviated for budget]";
        if (allowed <= truncated.Length + 40)
        {
            output.Append(section.AsSpan(0, allowed));
            return;
        }
        int cutoff = allowed - truncated.Length;
        int boundary = section.LastIndexOf('\n', cutoff - 1, cutoff);
        // Prefer complete lines so a truncated fact is not mistaken for current canon.
        if (boundary > cutoff / 2)
            cutoff = boundary;
        output.Append(section.AsSpan(0, cutoff));
        output.Append(truncated);
    }
}
