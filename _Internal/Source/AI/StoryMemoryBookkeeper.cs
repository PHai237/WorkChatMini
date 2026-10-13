using System.Text.Json;

namespace WorkChatMini;

public sealed class StoryMemoryBookkeeper
{
    private static readonly JsonSerializerOptions JsonOptions = new() { PropertyNameCaseInsensitive = true };
    private readonly StoryAiStageRunner _runner = new();

    public async Task<StoryMemoryUpdate?> ExtractAsync(
        StoryProject project,
        StoryMemoryDocument currentMemory,
        string userInstruction,
        string assistantResponse,
        string providerId,
        string model,
        StoryActionType action,
        CancellationToken cancellationToken)
    {
        string compactMemory = StoryMemoryRetriever.BuildContext(currentMemory, userInstruction);
        // Sample opening, midpoint, and ending of long chapters. This remains
        // partial evidence coverage; the model must not infer omitted events.
        // The omitted middle is explicitly marked; it is never evidence for canon.
        string responseText = BuildBookkeepingExcerpt(assistantResponse);
        if (assistantResponse.Length > MaxBookkeepingCharacters)
            AppLogger.Warn("STORY-MEMORY", $"Long chapter excerpted for bookkeeping | originalChars={assistantResponse.Length} | excerptChars={responseText.Length} | partialCoverage=true");
        string raw = await _runner.CompleteAsync(
            providerId,
            model,
            BuildSystemPrompt(project, compactMemory, action),
            new[]
            {
                new StoryMessage
                {
                    Role = "user",
                    Content = $"CREATOR INSTRUCTION:\n{userInstruction}\n\nASSISTANT OUTPUT TO BOOKKEEP:\n{responseText}"
                }
            },
            cancellationToken,
            temperature: 0.2,
            workload: StoryAiWorkload.Utility,
            stageName: "bookkeeping");
        string json = ExtractJson(raw);
        if (json.Length == 0)
            return null;
        try
        {
            StoryMemoryUpdate? update = JsonSerializer.Deserialize<StoryMemoryUpdate>(json, JsonOptions);
            return update is null ? null : StoryMemoryUpdateNormalizer.Normalize(update);
        }
        catch (Exception ex)
        {
            AppLogger.Warn("STORY-MEMORY", $"Bookkeeping JSON rejected | {ex.Message}");
            return null;
        }
    }

    internal const int MaxBookkeepingCharacters = 30000;

    internal static string BuildBookkeepingExcerpt(string chapter)
    {
        if (chapter.Length <= MaxBookkeepingCharacters)
            return chapter;
        // Three evidence windows cover opening, midpoint and ending. Two explicit
        // gaps prevent the model from inventing transitions across missing prose.
        const string gap1 = "\n\n[OMITTED PASSAGE 1 — missing prose is not evidence]\n\n";
        const string gap2 = "\n\n[OMITTED PASSAGE 2 — missing prose is not evidence]\n\n";
        int remaining = MaxBookkeepingCharacters - gap1.Length - gap2.Length;
        int headLength = remaining * 2 / 5;
        int middleLength = remaining * 3 / 10;
        int tailLength = remaining - headLength - middleLength;
        // Keep windows disjoint even just above the budget threshold.
        // Centering without a clamp would duplicate head/tail text for a 30,001-character chapter.
        int middleStart = Math.Clamp((chapter.Length - middleLength) / 2,
            headLength, chapter.Length - tailLength - middleLength);
        return chapter[..headLength] + gap1 +
            chapter.Substring(middleStart, middleLength) + gap2 +
            chapter[^tailLength..];
    }

    private static string BuildSystemPrompt(StoryProject project, string compactMemory, StoryActionType action)
    {
        const string template = """
You are the bookkeeping engine for WorkChat Story Companion.
Project: __PROJECT_TITLE__
Workflow action: __ACTION__
Your task is NOT to continue the story and NOT to improve the prose. Extract durable memory from the supplied assistant output.

Rules:
- Return ONE JSON object only. No markdown fences, no commentary.
- Treat the final assistant narrative as the source of truth for what actually happened in this generation.
- When a long chapter contains [OMITTED PASSAGE] markers, each missing passage is NOT available as evidence. Extract only explicitly stated facts from visible excerpts; never infer missing transitions, off-page events or causal links across the gap.
- Record only facts explicitly established in the final output or explicitly established by the creator instruction.
- Never record private plans, research possibilities, discarded options, or implied off-page events unless they actually appear in the final narrative.
- Do not infer hidden motives, romance, knowledge, inventory, power, deaths, or lore that were not actually established.
- Use status "Confirmed" for explicit facts. Use "Proposed" only when wording is ambiguous but potentially useful. Never promote a Proposed fact by yourself.
- NEVER retcon or correct established canon during ordinary bookkeeping. Canon revisions require the explicit Revise Canon workflow, so revisions MUST be an empty array here.
- Character Knowledge must contain only things that character explicitly learned, witnessed, was told, or already demonstrably knew. Do not copy world canon, narrator-only reveals, another character's secrets, or POV assumptions into every character's Knowledge. Existing knowledge must not be re-added from mere proximity or an uncommitted plan.
- When final prose shows a character learning a durable secret, use that character's UNIQUE CANONICAL NAME in characters[].name and in a matching Confirmed KnowledgeGained event (subject=learner, target=the same knowledge text, evidence=on-page witness/disclosure). Do not treat relatedCharacters, a shared nickname, POV, or simply being present as proof of learning. If no explicit recipient is established, do not grant knowledge.
- Open threads are unresolved promises, mysteries, threats, foreshadowing, goals, or questions. Give each thread importance Critical, Major, or Minor based on how strongly future continuity depends on it.
- Track NARRATIVE PROMISES separately from generic open threads. A narrative promise is a reader-facing setup that future prose may need to revisit or pay off: clue, foreshadowing, explicit promise, mystery setup, world-rule setup, Chekhov object, or similar. Do not create one for every detail.
- Narrative promise status must be Open, Developing, PaidOff, Abandoned, or Superseded. Use Developing when the setup is revisited/advanced without resolution. Use PaidOff only when the final prose actually delivers the promised reveal/consequence/resolution. Use Abandoned/Superseded only when the prose or creator explicitly invalidates/replaces it.
- Narrative promise kind must be Setup, Clue, Promise, CharacterPromise, Foreshadowing, Mystery, WorldRuleSetup, ChekhovObject, or Custom. Horizon is Short, Medium, Long, or Unknown; it is planning guidance, never a deadline.
- Track CAUSAL LINKS only when the final prose explicitly supports a durable causal relation. Allowed relations: CAUSES, ENABLES, BLOCKS, MOTIVATES, REVEALS, RESOLVES, ESCALATES. Do not infer causation merely because two events occur near each other.
- A causal link should describe why one durable story fact/event/setup leads to another, using compact human-readable from/to labels. Evidence must be supported by the final prose.
- For events[], narrativePromises[] and causalLinks[], copy a SHORT EXACT TEXT EXCERPT (ideally 12-200 characters) from the VISIBLE narrative into evidence, retaining original spelling. Do not put your own paraphrase inside evidence or pretend it is a quotation. If an exact excerpt is unavailable, leave evidence empty; never make one up. A quote is an audit pointer, not permission to invent implications.
- ResolvedThreads should reference the title or meaning of an existing open thread that this output clearly resolves.
- IsChapter should be true only if the assistant output is substantial narrative prose intended as a chapter/scene to keep in the manuscript. Brainstorming, outlines, critique, and discussion are false. For ContinueStory or DraftScene, a successful substantial narrative response should normally be true so the app can stamp chapter provenance.
- For autonomous continuations, capture the durable AFTER-SCENE state: what changed, who learned what, relationship changes, location/condition/inventory changes, timeline progress, newly opened threads, and clearly resolved threads.
- Track RELATIONSHIP HISTORY through relationships[]. Use UNIQUE CANONICAL NAMES for both from and to (never an ambiguous shared alias); from/to are directional, and must be different characters. Set state to the current relationship and change to a specific on-page action/revelation that caused the durable transition. Do not emit a relationship patch for ordinary conversation when the durable relationship did not change. The app stamps chapter provenance.
- Track CHARACTER ARCS separately from plot arcs. A character arc is an internal/behavioral trajectory for one character (trust, grief, corruption, confidence, identity, responsibility, etc.). Update it only when the final prose materially changes or demonstrates that trajectory. Do not create an arc for every mood fluctuation.
- For a genuine turning point, use the existing character arc title and populate lastChange with what actually happened on-page. If direction or stage changes, cite the final-prose cause; do not infer a personality reset from a temporary mood or an uncommitted plan.
- Personality is a current shorthand, not a reset button: change characters[].personality only when the final prose shows a durable personality shift. Leave it blank when only a temporary reaction or no actual change occurs.
- Track LOCATION / SCENE STATE only from explicit final prose. sceneStates should describe actual scenes that occurred, including location, POV, time if stated, and characters physically present. Never place an off-page character in a scene by inference. locations is durable place-state (destroyed/inaccessible, important objects, explicit connections), not decorative scenery.
- Also extract a SIGNIFICANT EVENT LEDGER. Events are durable story changes that future writing may need to know happened, when, and to whom. Do NOT create events for routine movement, ordinary dialogue, meals, or decorative actions with no lasting consequence.
- Track STORY ARCS separately from scene-level threads. An arc is a multi-scene trajectory such as a major mystery, faction conflict, training/progression journey, relationship arc, survival objective, political struggle, or long-running character goal. Create or update an arc only when the final prose materially starts, advances, pauses, or resolves that trajectory. Do not turn every scene into a new arc.
- Arc status must be Active, Paused, or Resolved. Arc priority is Core, Major, or Minor. Momentum is Rising, Steady, or Cooling and describes current narrative movement, not quality. Arc progress should describe the durable current position, not a speculative future plan. If a chapter materially advances an existing arc, update that arc rather than creating a near-duplicate.
- For every event that changes present state, ALSO emit the matching state patch in characters/relationships/timeline/threads/lore/canon. The ledger is history; those sections are current state. Relationship history, character arcs, and scene/location state must agree with those current-state patches.
- Character patches are deltas. Use removedAbilities/removedInventory/removedGoals/forgottenKnowledge when the final prose explicitly removes or invalidates prior state. Never leave a lost item or lost power in current state.
- Character choices: create DecisionMade only for an explicit consequential choice, SacrificeMade for an explicit cost paid, GoalAbandoned when a character demonstrably gives up a goal, PromiseKept or PromiseBroken only after the vow is visibly fulfilled/broken. Use PromiseMade when a character actually makes a vow; a private intention is not a promise. Subject MUST be the unique canonical actor, target the relevant goal/vow/object, and evidence must identify the on-page act. Do not add these event types for routine decisions or presumed motives.
- For GoalAbandoned also remove the exact goal via characters[].removedGoals. Promise events are historical facts; update an existing CharacterPromise in narrativePromises only if its reader-facing status truly changed, never mark it PaidOff because time elapsed.
- Consequences: emit causalLinks only if the final prose explicitly supports the relation; do not treat a later chapter or a similar event as proof of causation. Never pre-populate future consequences.
- Typical event types: AbilityLearned, AbilityLost, ItemAcquired, ItemLost, KnowledgeGained, RelationshipChanged, Injury, Recovery, Death, LocationChanged, GoalChanged, SecretRevealed, PromiseMade, Betrayal, Discovery, ThreadOpened, ThreadResolved, WorldStateChanged, IdentityChanged, DecisionMade, SacrificeMade, GoalAbandoned, PromiseKept, PromiseBroken, Custom.
- Importance: Critical = irreversible/identity/death/world-changing; Major = future continuity materially depends on it (new ability, important item, secret learned, relationship threshold, meaningful injury, major discovery); Minor = durable but lower-impact. Omit ephemeral details instead of logging them as Minor.
- Example: if Magica explicitly learns "Starfall Severance" in the final prose, add it to Magica.abilities AND emit an AbilityLearned event with subject Magica, target Starfall Severance, after=current capability, and Major importance. Do not invent a chapter number; the app stamps chapter provenance after the prose is persisted.
- Event ledger entries must be explicitly established and should normally be Confirmed. If an event itself is ambiguous, omit that event instead of updating current state from uncertainty.
- Evidence is a short factual paraphrase of what in the final prose established the event, never private reasoning.
- Keep summaries factual and compact.
- Empty sections must be empty arrays/strings, not invented filler.

JSON schema:
{
  "isChapter": true|false,
  "chapterTitle": "",
  "chapterSummary": "",
  "canon": [{"fact":"", "status":"Confirmed|Proposed"}],
  "characters": [{
    "name":"", "aliases":[], "appearance":"", "personality":"", "goals":[], "removedGoals":[], "fears":[],
    "secrets":[], "knowledge":[], "forgottenKnowledge":[], "currentLocation":"", "condition":"", "abilities":[], "removedAbilities":[],
    "inventory":[], "removedInventory":[], "progression":"", "notes":""
  }],
  "relationships": [{"from":"", "to":"", "state":"", "change":""}],
  "timeline": [{"when":"", "event":""}],
  "openThreads": [{"title":"", "detail":"", "importance":"Critical|Major|Minor"}],
  "resolvedThreads": [],
  "lore": [{"category":"", "name":"", "detail":"", "status":"Confirmed|Proposed"}],
  "events": [{
    "type":"AbilityLearned|AbilityLost|ItemAcquired|ItemLost|KnowledgeGained|RelationshipChanged|Injury|Recovery|Death|LocationChanged|GoalChanged|SecretRevealed|PromiseMade|Betrayal|Discovery|ThreadOpened|ThreadResolved|WorldStateChanged|IdentityChanged|DecisionMade|SacrificeMade|GoalAbandoned|PromiseKept|PromiseBroken|Custom",
    "subject":"character/entity/world", "target":"ability/item/secret/place/thread/etc",
    "summary":"durable change", "before":"", "after":"",
    "importance":"Critical|Major|Minor", "status":"Confirmed|Proposed", "when":"story-relative time if stated",
    "evidence":"short exact excerpt from visible text, or empty", "relatedCharacters":[], "tags":[]
  }],
  "arcs": [{
    "title":"stable arc name", "summary":"what this long-running arc is about", "status":"Active|Paused|Resolved",
    "priority":"Core|Major|Minor", "momentum":"Rising|Steady|Cooling",
    "stakes":"what can be gained/lost", "progress":"durable current position after this prose",
    "goals":[], "relatedCharacters":[], "relatedThreads":[]
  }],
  "characterArcs": [{
    "character":"", "title":"stable internal arc name", "summary":"", "status":"Active|Paused|Resolved",
    "importance":"Critical|Major|Minor", "stage":"current stage", "direction":"Growing|Regressing|Transforming|Fracturing|Stable",
    "internalConflict":"", "externalPressure":"", "progress":"durable current position", "lastChange":"what changed in this prose"
  }],
  "locations": [{
    "name":"", "summary":"durable place state", "parent":"", "status":"Active|Destroyed|Inaccessible",
    "connections":[], "presentCharacters":[], "relevantObjects":[]
  }],
  "sceneStates": [{
    "sceneLabel":"", "location":"explicit scene location", "time":"only if stated", "povCharacter":"",
    "presentCharacters":[], "departedCharacters":[], "summary":"compact factual scene-state summary"
  }],
  "narrativePromises": [{
    "kind":"Setup|Clue|Promise|CharacterPromise|Foreshadowing|Mystery|WorldRuleSetup|ChekhovObject|Custom",
    "title":"stable concise label", "detail":"what has been promised/set up",
    "status":"Open|Developing|PaidOff|Abandoned|Superseded", "importance":"Critical|Major|Minor",
    "horizon":"Short|Medium|Long|Unknown", "evidence":"short exact excerpt from visible text, or empty",
    "payoffSummary":"only when PaidOff", "relatedCharacters":[], "relatedArcs":[], "relatedThreads":[]
  }],
  "causalLinks": [{
    "relation":"CAUSES|ENABLES|BLOCKS|MOTIVATES|REVEALS|RESOLVES|ESCALATES",
    "from":"durable cause/setup/event label", "to":"durable consequence/reveal/event label",
    "summary":"compact causal meaning", "importance":"Critical|Major|Minor",
    "evidence":"short exact excerpt from visible text, or empty", "relatedCharacters":[]
  }],
  "revisions": []
}

Existing project memory, for deduplication/context only:
__COMPACT_MEMORY__
""";

        return template
            .Replace("__PROJECT_TITLE__", project.Title, StringComparison.Ordinal)
            .Replace("__ACTION__", action.ToString(), StringComparison.Ordinal)
            .Replace("__COMPACT_MEMORY__", compactMemory, StringComparison.Ordinal);
    }

    private static string ExtractJson(string value)
    {
        string text = value.Trim();
        if (text.StartsWith("```", StringComparison.Ordinal))
        {
            int firstNewLine = text.IndexOf('\n');
            int lastFence = text.LastIndexOf("```", StringComparison.Ordinal);
            if (firstNewLine >= 0 && lastFence > firstNewLine)
                text = text[(firstNewLine + 1)..lastFence].Trim();
        }
        int start = text.IndexOf('{');
        int end = text.LastIndexOf('}');
        return start >= 0 && end > start ? text[start..(end + 1)] : string.Empty;
    }
}
