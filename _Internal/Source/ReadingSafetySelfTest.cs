using System.IO;

namespace WorkChatMini;

// Runs only against private temporary files; never touches the user's shelf.
internal static class ReadingSafetySelfTest
{
    internal static void VerifyPersistence()
    {
        string directory = Path.Combine(Path.GetTempPath(), "WorkChatReadingGuard-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        string path = Path.Combine(directory, "library.json");
        try
        {
            // Legacy JSON intentionally omits LastReadUrl, Id and LastReadAt.
            const string legacy = "{\"Books\":[{\"Title\":\"Legacy novel\",\"Url\":\"https://example.org/novel/123\",\"LastReadUrl\":\"\",\"ScrollFraction\":0.3}]}";
            File.WriteAllText(path, legacy);
            ReadingLibraryStore library = ReadingLibraryStore.LoadForTest(path);
            if (library.LoadError is not null || library.Books.Count != 1 ||
                library.Books[0].LastReadUrl != "https://example.org/novel/123" ||
                string.IsNullOrWhiteSpace(library.Books[0].Id))
                throw new InvalidOperationException("Legacy book lost its bookmark while loading.");
            if (!library.Save() || File.ReadAllText(path + ".pre-migration.bak") != legacy)
                throw new InvalidOperationException("Pre-migration shelf backup was not preserved.");
            library.Books[0].LastReadAt = new DateTimeOffset(2025, 1, 2, 3, 4, 5, TimeSpan.Zero);
            if (!library.Save()) throw new InvalidOperationException("New reading timestamps did not save.");
            ReadingLibraryStore reloaded = ReadingLibraryStore.LoadForTest(path);
            if (reloaded.Books.Count != 1 || reloaded.Books[0].LastReadAt != library.Books[0].LastReadAt)
                throw new InvalidOperationException("Reading timestamps failed to round-trip.");
            if (File.ReadAllText(path + ".pre-migration.bak") != legacy)
                throw new InvalidOperationException("Subsequent saves replaced the pre-migration backup.");

            // A corrupt file and a partially invalid shelf must never be overwritten.
            foreach (string broken in new[]
            {
                "{ definitely not JSON",
                "{\"Books\":[{\"Id\":\"ok\",\"Url\":\"https://example.org/book/a\"},{\"Id\":\"bad\",\"Url\":\"javascript:alert(1)\"}]}"
            })
            {
                File.WriteAllText(path, broken);
                ReadingLibraryStore blocked = ReadingLibraryStore.LoadForTest(path);
                if (blocked.LoadError is null || blocked.Save() || File.ReadAllText(path) != broken)
                    throw new InvalidOperationException("A damaged shelf was silently replaced.");
            }
        }
        finally { try { Directory.Delete(directory, recursive: true); } catch { } }
    }

    internal static void VerifyUrlIsolation()
    {
        static void Check(string original, string candidate, bool expected)
        {
            var book = new ReadingBook { Url = original, LastReadUrl = candidate };
            if (ReadingUrlMatcher.BelongsToBook(book, candidate) != expected)
                throw new InvalidOperationException("Reading URL isolation failed: " + original + " -> " + candidate);
        }
        const string idA = "2582a87d-6fa5-4b0f-85ef-51a0fd1f232a";
        const string idB = "7d3a63ca-149c-4293-a829-572358fbfc5c";
        Check($"https://mangadex.org/title/{idA}", $"https://mangadex.org/title/{idA}/name", true);
        Check($"https://mangadex.org/title/{idA}", $"https://mangadex.org/title/{idB}", false);
        Check($"https://mangadex.org/title/{idA}", $"https://mangadex.org/chapter/{idB}", false);
        Check("https://docln.net/truyen/101-story-a", "https://docln.sbs/truyen/101-story-a/vol-2", true);
        Check("https://docln.net/truyen/101-story-a", "https://docln.net/truyen/102-story-b", false);
        Check("https://docln.net/truyen/101-story-a", "https://docln.net/chuong/333", false);
        Check("https://www.webnovel.com/book/a123", "https://www.webnovel.com/book/a123/chapter/123", true);
        Check("https://www.webnovel.com/book/a123", "https://www.webnovel.com/book/b456", false);
        Check("https://www.webnovel.com/book/a123", "https://en.webnovel.com/book/a123", true);
        Check("https://example.org/novel/a", "https://example.org/novel/a/chapter/1", true);
        Check("https://example.org/novel/a", "https://example.org/novel/b/chapter/2", false);
        Check("https://example.org/novel/a", "https://example.org/profile/user", false);
        Check("https://example.org/novel/a", "https://example.org.evil.test/novel/a", false);
        Check("https://example.org/novel/a", "https://example.org/novel/a#paragraph", true);
        Check("https://example.org/novel/a", "https://www.example.org/novel/a", true);
        Check("https://example.org/chapter/42", "https://example.org/chapter/42", true);
        Check("https://example.org/chapter/42", "https://example.org/chapter/43", false);
        if (!ReadingUrlMatcher.IsDefinitelyDifferentBook(
                new ReadingBook { Url = "https://docln.net/truyen/101-story-a" },
                "https://docln.net/truyen/102-story-b") ||
            ReadingUrlMatcher.IsDefinitelyDifferentBook(
                new ReadingBook { Url = "https://docln.net/truyen/101-story-a" },
                "https://docln.net/chuong/333"))
            throw new InvalidOperationException("Legacy resume links were filtered too broadly or not enough.");
    }
}
