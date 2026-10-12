# WorkChat Mini 2.39.0 — Provenance and Long-Story Continuity

## Changes

1. Shared verified evidence rules: unique committed chapter number, Confirmed event, unique nonblank event ID, meaningful event summary.
2. Removed unverified events and relationship turns from Knowledge Graph links, subject retrieval, chapter boosting and auto-continue history expansion. Ambiguous aliases are not treated as character identities.
3. The existing causal trace now rejects endpoints from orphan/ambiguous chapters and links with invalid chapter citations.
4. Scene and relationship retrieval only use provenanced chapter states. Kept latest scene location and POV high in Narrative Focus before optional literary cues.
5. A 20-chapter pure-memory regression exercises proposed events, orphan chapters, duplicate IDs, shared aliases, spatial state and duplicate chapter numbers. Added to the existing Story retrieval/quality self-test path.

## Compatibility

- Full Story Memory v6, project data, backups, media tabs and update manifests unchanged.
- No additional AI calls, new UI controls, new root-level tools or data migrations.
- Tests authored and static-audited on Linux; .NET/WPF BUILD and Windows Quick/Stress not run here.
