# Story Companion UI Design v2 — Chat-first Medieval Fantasy

Target: WorkChat Mini v2.26.1.2

## Why v1 failed

The earlier Story UI behaved visually like a settings form placed above a blank canvas:

- project/mode/model controls dominated the top of the screen;
- native WPF ComboBox chrome leaked a bright/light appearance;
- conversation messages did not establish a strong sender/receiver rhythm;
- the composer floated in a large empty area rather than anchoring a chat flow;
- medieval fantasy was represented mostly by brown/gold color and the Story icon;
- the empty state consumed too much visual space while offering little hierarchy.

## Research principles used

### Chat UX

1. Conversation is the primary surface.
2. Identity/configuration belongs in a rail or secondary surface, not the main conversation flow.
3. Keep the composer anchored to the conversation edge.
4. Visually differentiate the human and the AI with position, avatar, surface and typography.
5. Keep secondary settings available but out of the reading path (progressive disclosure).
6. Preserve readable line length for long-form AI responses.
7. Do not force-scroll while the user is reading older output.

### Medieval fantasy / MMORPG visual language

The goal is not a fake stone-frame game HUD. The UI should feel like a modern fantasy writing tool:

- charcoal / obsidian base;
- old leather / warm dark parchment surfaces;
- brass/gold borders used sparingly for hierarchy;
- tiny heraldic/sigil accents;
- Georgia for fantasy-flavored headings and long-form AI prose;
- square-ish / lightly beveled geometry rather than generic large SaaS pills;
- pixel book/quill icon remains the strongest branded visual;
- modern spacing and legibility remain more important than decoration.

## New information architecture

### Left rail

Keep it quiet and literal:

- project selector only when it is useful;
- add project;
- Memory;
- Genres;
- genre chips only when genres actually exist;
- Role selector;
- AI provider settings.

Do not show product branding, decorative fantasy labels, section headers such as “Story Tools”, or an empty dropdown.

Project selector behavior:

- zero projects → only **New story**;
- one project → plain project name, no dropdown;
- two or more projects → dropdown becomes visible.

### Conversation

The conversation owns the main surface. There is no permanent top header for “New Chronicle”, no local-memory badge, and no oversized empty-state card. If no project exists, show only a small create-story prompt. Once a project exists, leave the conversation area clean and let messages establish the hierarchy.

Messages remain:

- Story Companion on the left with restrained gold/warm parchment styling;
- user on the right with cool dark-blue styling;
- readable long-form width;
- small author/timestamp metadata.

### Composer

Anchor it to the bottom and keep it ordinary:

- multiline input;
- **Enter** sends;
- **Shift+Enter** creates a newline;
- Stop appears only while generating;
- one clear Send button;
- status text is hidden while idle and shown only for active generation, bookkeeping, cancellation or errors.

No “Write to the Scribe”, no idle/local-storage sentence and no keyboard-shortcut tutorial in the permanent UI.

## Performance rules

- Native WPF only.
- No new WebView.
- No animated backgrounds.
- No blur or expensive effects.
- Only simple brushes/gradients/borders.
- Existing streaming coalescing remains unchanged.

## Figma AI fallback prompt

Design a desktop AI chat interface for a personal long-form fiction co-writer called “Story Companion” inside a dark Windows app. The layout must feel immediately familiar as a chat application first, with a restrained medieval-fantasy/MMORPG identity second.

Use a narrow left rail and a large conversation area. Keep the rail minimal: project selector only when multiple projects exist, add-project action, Memory, Genres, compact genre tags only when present, Role, and AI settings. Do not add branding blocks, fantasy names for ordinary controls, explanatory section labels, local-memory badges, or empty dropdowns.

The main area should be almost entirely conversation. AI messages align left with warm dark-parchment/brass styling; user messages align right with cool dark-blue styling. Long AI prose must remain comfortable to read. Avoid a permanent conversation header unless it contains information the user actually needs.

Anchor a simple multiline composer to the bottom. Enter sends and Shift+Enter makes a newline. Show Stop only while generating. Keep status hidden while idle. Use one clear Send action.

Palette: near-black charcoal, dark leather/parchment, muted brass/gold, bone-white text, restrained steel-blue accents. Medieval-fantasy identity should come from materials, typography, borders and pixel iconography—not decorative copy. Avoid glassmorphism, neon, giant cards, fake stone textures, ornate RPG HUD framing, excessive pills, redundant helper text and tutorial labels.

The result should feel like a mature chat client with tasteful fantasy-codex styling, not a fantasy-themed settings form.
