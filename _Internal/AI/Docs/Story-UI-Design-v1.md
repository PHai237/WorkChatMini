# WorkChat Story Companion — UI Design v1

Build target: v2.25.3 Story UI Refresh

## Design goal

Make Story Companion feel like a calm personal writing room, not a settings panel with a chat box attached.
The interface should stay lightweight, native WPF, low-clutter, and future-ready for Story Memory.

## Research-derived principles

1. Keep frequent actions on the primary canvas; push advanced/rare controls behind secondary surfaces.
   - Microsoft Windows command guidance: https://learn.microsoft.com/en-us/windows/apps/design/basics/commanding-basics
   - Progressive disclosure: https://www.nngroup.com/articles/progressive-disclosure/

2. Use consistent spacing and typography hierarchy instead of more borders and panels.
   - Windows content layout/spacing: https://learn.microsoft.com/en-us/windows/apps/design/basics/content-basics

3. AI empty states should explain capabilities and offer clickable prompt suggestions rather than only blank text.
   - NN/g AI chatbot guidance: https://www.nngroup.com/articles/ai-chatbots-design-guidelines/
   - Prompt controls: https://www.nngroup.com/articles/prompt-controls-genai/

4. Do not yank users to the bottom while they are reading a long AI response. Auto-follow only while the user remains near the bottom.
   - NN/g AI chatbot guidance: https://www.nngroup.com/articles/ai-chatbots-design-guidelines/

5. For writing products, separate the manuscript-like reading surface from controls. Writing tools such as Sudowrite keep project/document context and AI tooling around a central reading/writing area rather than turning every output into identical chat bubbles.
   - Sudowrite interface: https://docs.sudowrite.com/getting-started/dQph1snuwbfMWG9wRjsNug/interface/ubBg2ZEoAwasV98E3ZBwjn

6. Keep readable text contrast at or above normal Windows accessibility guidance.
   - Accessible text requirements: https://learn.microsoft.com/en-us/windows/apps/design/accessibility/accessible-text-requirements

## Visual direction

- Theme: dark writing desk / ink + parchment + brass.
- Medieval-fantasy flavor is subtle and comes from the pixel book/quill icon, warm brass accents, and manuscript typography.
- The app must still fit romcom, sci-fi, xianxia, rebirth, school-life, etc.; therefore the full surface is not decorated like a medieval game UI.
- No textures, wallpaper, glow effects, gradients, animated ornaments, or expensive WPF effects.

Palette:
- Base: #0C0E11
- Chrome: #111419
- Surface: #15191F
- Border: #2A3039
- Main text: #EDE7DC
- Muted text: #8E98A8
- Brass accent: #C49A5A
- Temporary-context blue: #8FA9D2

## Main screen hierarchy

### Header

Primary:
- Story icon
- Project selector
- New project
- AI mode
- Provider/model

Secondary row:
- Core genre chips in warm brass
- Temporary tags in cool blue
- Small `Edit genres` action

Do not restore a full-width status strip.

### Conversation canvas

- User messages remain compact right-aligned prompts.
- AI output is wider and document-like, with a thin brass left rule instead of a heavy bubble.
- AI prose uses a readable serif face (Cambria) while controls remain Segoe UI.
- Long-form response width is constrained for reading comfort.

### Empty state

When no project exists:
- explain Story Companion briefly;
- one primary `Create first project` button.

When a project exists but has no messages:
- show three clickable prompt starters:
  - shape a premise;
  - plan the next chapter;
  - brainstorm a twist/character/world detail.

These only prefill the composer; they do not send automatically.

### Composer

- Floating rounded composer, centered with the conversation width.
- Large multiline text area.
- Status remains low-emphasis.
- Send is the only persistent primary action.
- Stop appears only while generation is active.

## Interaction changes

- Streaming response only auto-scrolls while the user is near the bottom.
- If the user scrolls upward to reread, generation continues without pulling the viewport away.
- Genre chips summarize the current project directly in the header.
- Provider button shows provider/model state directly instead of a separate status label.

## Future Story Memory integration

Do not add the Story Memory sidebar yet.
When v2.26 is ready, add a single optional drawer entry for:
- Canon
- Characters
- Timeline
- Open Threads
- Lore
- Research

The drawer should stay closed by default so the writing surface remains primary.

## Figma AI fallback prompt

Use this only if a visual prototype is needed before another WPF pass:

> Design a desktop Windows dark-mode UI for a personal AI fiction co-writer called “Story Companion”, embedded inside an existing productivity app. The product is for long-form novels across many genres: fantasy, MMORPG, romcom, isekai, rebirth, xianxia, mystery, sci-fi, school life, and more. Do NOT make the whole UI look like a medieval game. Use only a subtle medieval-fantasy identity through a small pixel-art open-book-and-quill icon and warm brass accents. The main aesthetic is a calm modern writing room: near-black background, layered charcoal surfaces, parchment-white text, muted steel-gray secondary text, warm brass core-genre chips, cool blue temporary-scene tags. Keep it native-Windows-like, compact, elegant, and practical.
>
> Layout: a compact top header with the pixel Story icon, project dropdown, + new project, AI mode selector, and provider/model pill. A second very slim row contains rounded genre chips and a subtle “Edit genres” text action. The center is a wide readable conversation/manuscript canvas. User prompts appear as compact right-aligned dark-blue-gray bubbles. AI output should NOT look like the same chat bubble; render it like a manuscript block with a thin brass vertical rule on the left, generous line spacing, and a serif reading font. Constrain the reading width to around 900–1000 px. At the bottom, use a floating rounded multiline composer with a low-emphasis status line, a primary brass Send button, and a Stop button visible only during generation.
>
> Empty state: centered pixel book/quill icon, title “Your writing room”, one short line explaining that the user can develop stories together with the AI. If a project exists but is empty, show three low-key clickable prompt cards: “Shape the premise from an idea I already have”, “Plan the next chapter before writing it”, and “Brainstorm a twist, character, or world detail”. Avoid giant cards, excessive borders, gradients, glassmorphism, neon, decorative fantasy frames, sidebars that are always open, or dense settings. The UI should look excellent at 1366x768, 1920x1080, and mixed-DPI Windows monitors.
>
> Make a desktop mockup focused on visual hierarchy, spacing, component states, and real usability. The future app will later add a collapsible Story Memory drawer for Canon, Characters, Timeline, Open Threads, Lore, and Research, but do not show that drawer open in the primary mockup.
