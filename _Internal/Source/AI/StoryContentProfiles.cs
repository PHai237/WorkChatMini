namespace WorkChatMini;

public sealed class StoryContentProfile
{
    public string Maturity { get; set; } = StoryContentProfiles.Standard;
    public string ViolenceGore { get; set; } = StoryContentProfiles.ViolenceModerate;
    public string HorrorIntensity { get; set; } = StoryContentProfiles.HorrorModerate;
    public string IntimacyBoundary { get; set; } = StoryContentProfiles.IntimacyFadeToBlack;
    public string SensitiveThemesNotes { get; set; } = string.Empty;
}

public static class StoryContentProfiles
{
    public const string Standard = "Standard";
    public const string Mature = "Mature / adult themes";
    public const string Dark = "Dark / extreme horror";

    public const string ViolenceMild = "Mild";
    public const string ViolenceModerate = "Moderate";
    public const string ViolenceGraphic = "Graphic";

    public const string HorrorLight = "Light";
    public const string HorrorModerate = "Moderate";
    public const string HorrorIntense = "Intense";
    public const string HorrorExtreme = "Extreme";

    public const string IntimacyRomance = "Romance only";
    public const string IntimacyFadeToBlack = "Fade to black";
    public const string IntimacyMatureNonGraphic = "Mature, non-graphic";

    public static readonly IReadOnlyList<string> MaturityLevels = new[] { Standard, Mature, Dark };
    public static readonly IReadOnlyList<string> ViolenceLevels = new[] { ViolenceMild, ViolenceModerate, ViolenceGraphic };
    public static readonly IReadOnlyList<string> HorrorLevels = new[] { HorrorLight, HorrorModerate, HorrorIntense, HorrorExtreme };
    public static readonly IReadOnlyList<string> IntimacyLevels = new[] { IntimacyRomance, IntimacyFadeToBlack, IntimacyMatureNonGraphic };

    public static void Normalize(StoryProject project)
    {
        project.ContentProfile ??= new StoryContentProfile();
        StoryContentProfile profile = project.ContentProfile;
        profile.Maturity = Normalize(profile.Maturity, MaturityLevels, Standard);
        profile.ViolenceGore = Normalize(profile.ViolenceGore, ViolenceLevels, ViolenceModerate);
        profile.HorrorIntensity = Normalize(profile.HorrorIntensity, HorrorLevels, HorrorModerate);
        profile.IntimacyBoundary = Normalize(profile.IntimacyBoundary, IntimacyLevels, IntimacyFadeToBlack);
        profile.SensitiveThemesNotes ??= string.Empty;
    }

    public static string BuildPrompt(StoryProject project)
    {
        Normalize(project);
        StoryContentProfile profile = project.ContentProfile;
        string notes = string.IsNullOrWhiteSpace(profile.SensitiveThemesNotes)
            ? "No additional creator content notes."
            : profile.SensitiveThemesNotes.Trim();
        return $"""
PROJECT CONTENT PROFILE
- Maturity: {profile.Maturity}
- Violence / gore: {profile.ViolenceGore}
- Horror intensity: {profile.HorrorIntensity}
- Intimacy boundary: {profile.IntimacyBoundary}
- Creator content notes: {notes}

Use this profile as tone/intensity guidance for fiction. Mature themes, graphic violence/gore, disturbing horror, trauma, crime, addiction, war, death, and other sensitive subject matter may be handled when the creator requests them and they fit the project. Do not intensify sensitive material merely for shock value. Keep intimacy within the selected boundary and within provider/platform limits. Never infer that a dark genre automatically requires maximum intensity.
""";
    }

    private static string Normalize(string? value, IReadOnlyList<string> allowed, string fallback) =>
        allowed.FirstOrDefault(x => string.Equals(x, value, StringComparison.OrdinalIgnoreCase)) ?? fallback;
}
