namespace WorkChatMini;

// Editorial feedback is deliberately NOT a memory fact, a character trait or a
// retrospective instruction to rewrite approved chapters. One choice per Story.
internal static class StoryCraftFeedback
{
    internal const string None = "No correction";
    internal const string TooFast = "Too fast";
    internal const string TooSlow = "Too slow";
    internal const string OutOfCharacter = "Out of character";
    internal const string RandomTwists = "Unmotivated twists";
    internal const string Repetitive = "Repetitive prose";
    internal const string StiffDialogue = "Stiff dialogue";
    internal const string OverExplaining = "Over-explaining";

    internal static readonly IReadOnlyList<string> All = new[]
        { None, TooFast, TooSlow, OutOfCharacter, RandomTwists, Repetitive, StiffDialogue, OverExplaining };

    internal static string Normalize(string? value) =>
        value is TooFast or TooSlow or OutOfCharacter or RandomTwists or Repetitive or StiffDialogue or OverExplaining ? value : None;

    internal static string Guidance(string? feedback) => Normalize(feedback) switch
    {
        TooFast => "CREATOR CRAFT FEEDBACK (not canon): The previous writing felt rushed. Give established decisions and their aftermath enough on-page room; do NOT simply add filler or delay every plot event.",
        TooSlow => "CREATOR CRAFT FEEDBACK (not canon): The previous writing felt slow. Advance a meaningful existing choice, relationship or consequence with focused scenes; do NOT force a crisis or a cliffhanger.",
        OutOfCharacter => "CREATOR CRAFT FEEDBACK (not canon): Characters recently felt inconsistent. Recheck confirmed motives, knowledge and committed turning points before dialogue or action. Show an earned reason for a genuine change; never silently reset personalities.",
        RandomTwists => "CREATOR CRAFT FEEDBACK (not canon): The previous writing had unearned surprises. Ground new turns in already planted causes and credible character choices; do NOT invent factions, secrets or powers merely to escalate tension.",
        Repetitive => "CREATOR CRAFT FEEDBACK (not canon): The previous writing felt repetitive. Avoid duplicate recaps, repeated internal monologue and identical dialogue patterns while preserving deliberate refrains and any canon fact.",
        StiffDialogue => "CREATOR CRAFT FEEDBACK (not canon): Dialogue felt stiff. Let characters speak to achieve different goals, interrupt or hold back naturally, and reveal emotion through speech choices. Do not invent accents, quirks or character knowledge.",
        OverExplaining => "CREATOR CRAFT FEEDBACK (not canon): The writing over-explained thoughts or lore. Trust meaningful action and subtext where context is clear; preserve vital facts and deliberate introspection. Do not trade clarity for obscurity.",
        _ => string.Empty
    };
}
