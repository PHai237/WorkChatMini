namespace WorkChatMini;

public static class StoryManuscriptModes
{
    public const string AutoCommit = "Auto-commit narrative";
    public const string ReviewBeforeCommit = "Review before commit";

    public static readonly IReadOnlyList<string> All = new[] { AutoCommit, ReviewBeforeCommit };

    public static string Normalize(string? value) =>
        string.Equals(value, ReviewBeforeCommit, StringComparison.OrdinalIgnoreCase)
            ? ReviewBeforeCommit
            : AutoCommit;

    public static string Description(string? value) => Normalize(value) == ReviewBeforeCommit
        ? "Generated narrative stays a draft until you press Commit to Story. Drafts do not change canon, chapters, arcs, or durable memory."
        : "Successful Continue Story and Draft Scene outputs are committed as manuscript chapters and bookkept automatically.";
}
