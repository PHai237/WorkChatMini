using System.Security.Cryptography;
using System.Text;

namespace WorkChatMini;

/// <summary>
/// Exactly-once guard for user-approved memory proposals. The marker is saved
/// in the SAME atomic memory file as the proposal's state change, so a crash
/// between saving memory.json and conversation.json cannot reapply the delta.
/// </summary>
internal static class StoryProposalCommit
{
    internal static string KeyFor(StoryMessage message)
    {
        string identity = message.CreatedUtc.ToUniversalTime().Ticks.ToString(
            System.Globalization.CultureInfo.InvariantCulture) + "\n" +
            message.ProposalKind + "\n" + message.ProposalJson;
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(identity)));
    }

    internal static bool AlreadyApplied(StoryMemoryDocument memory, StoryMessage message) =>
        memory.AppliedProposalKeys?.Contains(KeyFor(message), StringComparer.Ordinal) == true;

    internal static void MarkApplied(StoryMemoryDocument memory, StoryMessage message)
    {
        string key = KeyFor(message);
        memory.AppliedProposalKeys ??= new List<string>();
        if (!memory.AppliedProposalKeys.Contains(key, StringComparer.Ordinal))
            memory.AppliedProposalKeys.Add(key);
    }
}
