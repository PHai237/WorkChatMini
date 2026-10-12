namespace WorkChatMini;

// Private literary direction added to the EXISTING writing pipeline.
// No new provider request, project setting, canon event or storage format.
internal static class StoryLiteraryCraft
{
    private const int MaxBriefLength = 1400;

    internal static string NormalizeBrief(string? brief)
    {
        if (string.IsNullOrWhiteSpace(brief)) return string.Empty;
        string condensed = string.Join(" ", brief.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        return condensed.Length <= MaxBriefLength ? condensed : condensed[..MaxBriefLength].TrimEnd() + "…";
    }

    internal static string WithSceneBrief(StoryDraftPreparation preparation)
    {
        string plan = preparation.Plan ?? string.Empty;
        string brief = NormalizeBrief(preparation.CraftBrief);
        if (brief.Length == 0) return plan;
        // Put the brief first so the bounded writer input cannot truncate it
        // when an exceptionally long plan is returned. The plot plan is still
        // authoritative for events; the brief is expression guidance only.
        return "PRIVATE SCENE CRAFT INTENT (not canon or a required plot beat):\n" +
            brief + "\n\nPLOT PLAN (confirmed facts and creator intent remain authoritative):\n" + plan;
    }

    internal static string ForPreparation => """

LITERARY SCENE INTENT:
- Prefer one dramatic focus that matters to the current viewpoint character, not a list of mandatory beats.
- Identify the scene's quiet pressure: what the character hopes to gain, what they fear exposing, and the choice that makes the tension visible.
- A reader should notice a change in trust, understanding, distance, agency or stakes. This can be minute and need not be a twist.
- Choose telling detail from setting only when it affects action or perception. No stock weather metaphors or decorative lore dumps.
- Match the established narrator's distance, tense and language. Do not invent a style guide that contradicts the creator's voice.
""";

    internal static string ForDraft => """

LITERARY CRAFT — THE SCENE IS FOR READING, NOT A SUMMARY:
- Let a character try to do something observable. Use resistance and response to reveal stakes; avoid explaining an emotion immediately after an action already conveys it.
- Maintain one stable viewpoint and perceptual distance unless a transition is explicitly requested or clearly established in the work.
- Dialogue is an exchange of intentions: give speakers different goals, information and degrees of candor. Avoid speeches where every character sounds like the narrator.
- Select concrete, consequential sensory details. Do not decorate every sentence with a metaphor, adjective or atmospheric reaction.
- Let stillness have dramatic function. Use summary for routine transitions and full scene time for difficult choices, encounters and aftermath.
- Ground the ending in a changed fact, decision, relationship or understanding; an ordinary, earned close is often stronger than an artificial ominous last line.
- Follow the optional PRIVATE SCENE CRAFT INTENT in PLAN only when compatible with canon, scene logic and the creator's direction.
- Avoid the synthetic cadence of every scene opening with weather, every exchange ending in a vague foreboding line, and every emotion being named for the reader. Let a scene open at a specific purposeful action when earned.
- A character should sometimes misunderstand, sidestep or only partly answer another character; dialogue is not a clean sequence of perfect answers. Keep the meaning and speakers' distinct values legible.
- In Vietnamese prose, prefer idiomatic everyday phrasing where the character and setting call for it. Do not translate English idioms literally or force elegant Sino-Vietnamese diction into casual dialogue.
""";

    internal static string ForEditor => """

LITERARY LINE EDIT — PRESERVE EVENTS, IMPROVE THE READING EXPERIENCE:
- First check the scene's emotional logic: the central choice, what resists it, what the viewpoint character notices, and the ending's aftertaste. Do not invent events to fill an empty arc.
- Preserve the scene's established POV, tense, plot outcome, names, facts and character-specific knowledge. Keep ambiguity where it is deliberate.
- Prefer verbs and purposeful details to stacks of abstract emotion labels, familiar ornamental comparisons or explanatory speeches.
- Trim redundant internal monologue, scene recaps, rhetorical questions and repeated beats while retaining necessary psychological depth.
- Preserve idiosyncratic cadence and restraint; do not make every paragraph short, vivid, lyrical or dramatic by force.
- Polish in the language of the prose being edited. Avoid translation-like syntax and generic phrase templates.
- If a paragraph sounds impressive but communicates nothing new, make its action, perception or meaning clearer without changing canon.
- Read the first and last paragraphs for continuity of emotional distance: eliminate generic teaser lines, empty promises of destiny, and recaps that repeat what the scene already demonstrated. Keep a deliberate poetic or suspenseful line when it actually earns its place.
- Dialogue should sound speakable for the individual character: vary directness, omissions, interruptions and register when motivated, without imposing quirks or gratuitous slang.
- In Vietnamese, remove translated-English sentence frames when they sound unnatural; preserve the creator's chosen dialect, register and period. Never flatten distinct voices into uniformly literary dialogue.
- The creator's explicit style, chosen genre, quiet ending and unusual character voice outrank generic polish rules.
""";

    // Used only when continuity verification has identified concrete problems.
    // The repair pass must not turn a literary chapter into an outline or a
    // fresh chapter with different character motivations. No extra model call.
    internal static string ForContinuityRepair => """

MINIMAL LITERARY REPAIR:
- Fix only the specific grounded contradictions listed in ISSUES TO FIX; keep every unrelated scene decision, paragraph and beat intact.
- Prefer a local adjustment of a name, timeline, location, knowledge boundary or causal detail over rewriting an entire exchange.
- Preserve the narrator's original language, rhythm, subtext and character-specific dialogue. Do not add an explanatory paragraph to justify a small factual fix.
- If the supplied facts cannot support a confident repair, keep uncertainty explicit rather than inventing an event or retcon.
- Never add a new twist, scene, character insight or cliffhanger during factual repair.
""";

    // At most one narrow note: genre creates an emphasis, never a rigid template
    // or a license to manufacture spectacle, lore, relationships or new canon.
    internal static string GenreNuance(StoryProject project)
    {
        var genres = project.CoreGenres ?? new List<string>();
        bool Has(string value) => genres.Any(g => string.Equals(g, value, StringComparison.OrdinalIgnoreCase));
        string? note = null;
        if (Has("Romance") || Has("Romcom"))
            note = "For romantic scenes, show changes in trust, vulnerability and social distance through choices; attraction does not excuse personality resets or forced confessions.";
        else if (Has("Mystery") || Has("Horror"))
            note = "For suspense, separate what the viewpoint knows from what the reader suspects. Let clues have consequences; obscurity alone is not tension.";
        else if (Has("Slice of Life") || Has("School Life"))
            note = "For everyday-life scenes, locate significance in a small changed habit, relationship or expectation; do not insert a crisis simply to create plot.";
        else if (Has("Action") || Has("Adventure") || Has("Wuxia"))
            note = "For action, make spatial cause, physical limits and personal stakes legible; vary pressure with decision and aftermath rather than uninterrupted spectacle.";
        else if (Has("Political Fantasy"))
            note = "For political scenes, make negotiation and betrayal flow from established incentives, constraints and information, not sudden mastermind reveals.";
        else if (Has("Sci-Fi") || Has("Medieval Fantasy") || Has("Dark Fantasy") || Has("Xianxia / Tu tiên"))
            note = "For speculative scenes, reveal world rules through meaningful use and cost; avoid expository catalogs or adding a power to resolve an earned problem.";
        return note is null ? string.Empty : "\nGENRE-SENSITIVE EMPHASIS (not a formula): " + note;
    }
}
