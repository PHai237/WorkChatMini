namespace WorkChatMini;

// Pure offline test: exercises provenance without files, model API, or data edits.
internal static class StoryEvidenceAuditSelfTest
{
    internal static void Verify()
    {
        var memory = new StoryMemoryDocument();
        memory.Chapters.Add(new StoryChapterMemory { Number = 1, FileName = "0001-Harbor.md" });
        memory.Events.Add(new StorySignificantEvent
        {
            Summary = "Mai crossed the harbor", ChapterNumber = 1,
            Evidence = "Mai stepped onto the harbor bridge."
        });
        memory.Events.Add(new StorySignificantEvent
        {
            Summary = "Discarded rumor", Status = "Proposed", ChapterNumber = 1,
            Evidence = "A rumor that never happened"
        });
        memory.Canon.Add(new StoryCanonFact { Fact = "The lantern is blue", Source = "Chapter 1" });
        memory.Canon.Add(new StoryCanonFact { Fact = "Unverified writer idea", Status = "Proposed", Source = "Chapter 1" });
        memory.Timeline.Add(new StoryTimelineEvent { Event = "Lost key", Source = "Chapter 99" });
        StoryEvidenceAudit.Snapshot snapshot = StoryEvidenceAudit.Capture(memory);
        if (snapshot.Claims.Length != 3 || snapshot.TotalClaims != 3 ||
            snapshot.Claims[0].ChapterNumber != 1 || snapshot.Claims[1].ChapterNumber != 99 ||
            snapshot.Claims[2].ChapterNumber != 1)
            throw new InvalidOperationException("Audit used proposed facts or lost committed provenance.");
        string body = "Mai stepped onto the harbor bridge. Then she saw An.";
        if (StoryEvidenceAudit.Evaluate(snapshot.Claims[0], true, body) != "Exact excerpt" ||
            StoryEvidenceAudit.Evaluate(snapshot.Claims[0], true, "Mai stood elsewhere") != "Quote not found" ||
            StoryEvidenceAudit.Evaluate(snapshot.Claims[0], false, null) != "Chapter missing" ||
            StoryEvidenceAudit.Evaluate(snapshot.Claims[2], true, body) != "Source only" ||
            StoryEvidenceAudit.Evaluate(snapshot.Claims[0], true, null) != "File unavailable" ||
            StoryEvidenceAudit.Evaluate(new StoryEvidenceAudit.Claim("Event", "orphan", null, ""), false, null) != "Unlinked" ||
            StoryEvidenceAudit.ParseChapter("Chapter 2: Wake") != 2 ||
            StoryEvidenceAudit.ParseChapter("Chương 4") != 4 ||
            StoryEvidenceAudit.ParseChapter("Not Chapter 8") is not null)
            throw new InvalidOperationException("Audit implied proof from a paraphrase or an unlinked source.");
        if (memory.Events.Count != 2 || memory.Canon.Count != 2)
            throw new InvalidOperationException("Read-only audit changed structured Memory.");
        var large = new StoryMemoryDocument();
        large.Chapters.Add(new StoryChapterMemory { Number = 1, FileName = "0001-Harbor.md" });
        for (int i = 0; i < StoryEvidenceAudit.MaxClaims + 3; i++)
            large.Events.Add(new StorySignificantEvent { Summary = $"Event {i}", ChapterNumber = 1 });
        StoryEvidenceAudit.Snapshot capped = StoryEvidenceAudit.Capture(large);
        if (capped.Claims.Length != StoryEvidenceAudit.MaxClaims ||
            capped.TotalClaims != StoryEvidenceAudit.MaxClaims + 3)
            throw new InvalidOperationException("Audit claim budget did not stop unbounded work.");
    }
}
