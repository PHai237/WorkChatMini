# WorkChat Mini 2.32.0 — Literary Craft 2.0

## AI Story

- The existing preparation stage produces an **optional private `craftBrief`** for Continue Story. It describes why the scene matters, a credible viewpoint desire, subtext, rhythm and what changes by the close. This is editorial direction, **not canon**, and is bounded to 1,400 characters. Older responses without `craftBrief` remain valid.
- Existing draft and editor stages adopt **literary craft guidance**: character agency, dialogue with intent, POV distance, concrete detail, meaningful stillness, controlled exposition and earned endings. Genre-specific emphasis is deliberately limited and never forces new plot events.
- **Cross-chapter echo watch:** a strictly local advisory detects identical eleven-word openings or endings versus up to three unique committed manuscript chapters from the current story. It excludes uncommitted/abandoned drafts, interrupted responses and ambiguous duplicate chapter numbers. It does not write to Story Memory.
- Existing **Chapter craft feedback** dropdown adds **Stiff dialogue** and **Over-explaining**, without adding new controls or API stages.
- Story Director, Scene Compass, Story Memory v6, continuity repair, checkpoint/branch backups, manual commit review and app updates are preserved.

## Performance and safety

- No new network stage or API model request; the existing preparation JSON gains one optional field and the existing editor receives at most two succinct echo warnings.
- No migrations, new persistent settings, automatic canon edits or changes to saved Story data.
- The release ZIP format remains unchanged (`WorkChat-Setup.exe` and `WorkChat-Update.zip` on GitHub). Source delivery is `WorkChat.zip`.

## Validation

- Static source checks and package integrity are performed in the authoring environment.
- This source has NOT been compiled with Windows WPF in the authoring environment; GitHub Actions BUILD and Quick/Stress must pass before installing the binary release.
- Editorial quality still needs real-reader testing across 5–10 consecutive chapters. Prompt improvements do not guarantee novelist-level prose.
