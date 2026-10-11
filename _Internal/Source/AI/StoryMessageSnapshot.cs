namespace WorkChatMini;

/// <summary>
/// A consistent conversation snapshot before background JSON serialization.
/// Never copies chapter files and never shares mutable idea/revision lists with UI.
/// Immutable strings are intentionally reused to keep large chats lightweight.
/// </summary>
internal static class StoryMessageSnapshot
{
    internal static StoryMessage[] Capture(IReadOnlyList<StoryMessage> source)
    {
        var result = new StoryMessage[source.Count];
        for (int i = 0; i < result.Length; i++)
        {
            StoryMessage msg = source[i];
            result[i] = new StoryMessage
            {
                Role = msg.Role,
                Content = msg.Content,
                CreatedUtc = msg.CreatedUtc,
                Action = msg.Action,
                ProposalKind = msg.ProposalKind,
                ProposalJson = msg.ProposalJson,
                ProposalApplied = msg.ProposalApplied,
                SourceInstruction = msg.SourceInstruction,
                DraftGroupId = msg.DraftGroupId,
                DraftVersionId = msg.DraftVersionId,
                DraftDiscarded = msg.DraftDiscarded,
                IsInterruptedResponse = msg.IsInterruptedResponse,
                IsNarrativeDraft = msg.IsNarrativeDraft,
                ManuscriptCommitted = msg.ManuscriptCommitted,
                ManuscriptChapterNumber = msg.ManuscriptChapterNumber,
                ManuscriptChapterTitle = msg.ManuscriptChapterTitle,
                CreativeDirection = msg.CreativeDirection,
                CreativeDirectionArchived = msg.CreativeDirectionArchived,
                CreativeDirectionCategory = msg.CreativeDirectionCategory,
                CreativeDirectionRevisions = msg.CreativeDirectionRevisions?.Select(r =>
                    new StoryCreativeRevision { PreviousText = r.PreviousText, ChangedUtc = r.ChangedUtc }).ToList(),
                DeclinedCreativeSuggestions = msg.DeclinedCreativeSuggestions is null
                    ? null : new List<string>(msg.DeclinedCreativeSuggestions),
                ManuscriptCommittedUtc = msg.ManuscriptCommittedUtc
            };
        }
        return result;
    }
}
