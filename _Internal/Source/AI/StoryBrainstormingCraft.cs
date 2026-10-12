namespace WorkChatMini;

// Tight guidance for Chat/brainstorming turns so the assistant does not
// unexpectedly jump into fully-written fiction when the creator really wants
// discussion, diagnosis or option comparison. No new workflow or storage.
internal static class StoryBrainstormingCraft
{
    internal static string ForPreparation => """

DISCUSSION-FIRST CHAT MODE:
- The creator is asking for brainstorming, critique, planning or back-and-forth help rather than an automatic scene/chapter.
- Prepare a response that is useful to read as conversation: identify the question, the trade-offs, the best next move, and only the minimum structure needed.
- If options are helpful, prefer two or three concise distinct possibilities with clear differences.
- Do not silently convert the request into long narrative prose.
- Tiny illustrative snippets are allowed only when they clearly function as examples, not as committed story text.
""";

    internal static string ForDraft => """

BRAINSTORMING DELIVERY:
- Produce a discussion answer, not a chapter, unless the creator explicitly asks for prose.
- Lead with the most useful point: answer the question, diagnose the weak spot, or compare the options.
- Keep structure readable. Numbered options or short sections are preferred over a wall of text.
- If giving examples, keep them short and label them as examples.
- Do not end by inventing canon or pretending a decision has already been locked.
""";

    internal static string ForEditor => """

DISCUSSION POLISH:
- Preserve the answer's discussion-first purpose. Do not rewrite it into fiction.
- Remove rambling filler, repeated reassurance and vague generic advice.
- Make each option distinct and actionable; if several options sound interchangeable, sharpen the trade-offs.
- Keep any illustrative prose snippet brief and clearly framed as optional.
""";
}
