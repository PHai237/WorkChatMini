using System.IO;
using System.Text.Json;

namespace WorkChatMini;

public sealed class StoryProjectStore
{
    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };
    private static string RootDirectory => Path.Combine(AppSettings.AppDataDirectory, "AI", "Projects");

    public async Task<IReadOnlyList<StoryProject>> LoadProjectsAsync(CancellationToken cancellationToken = default)
    {
        return await Task.Run(() =>
        {
            Directory.CreateDirectory(RootDirectory);
            var projects = new List<StoryProject>();
            foreach (string directory in Directory.EnumerateDirectories(RootDirectory))
            {
                cancellationToken.ThrowIfCancellationRequested();
                string path = Path.Combine(directory, "project.json");
                if (!File.Exists(path))
                    continue;
                try
                {
                    StoryProject? project = JsonSerializer.Deserialize<StoryProject>(File.ReadAllText(path), JsonOptions);
                    if (project is not null && !string.IsNullOrWhiteSpace(project.Id))
                    {
                        project.ManuscriptMode = StoryManuscriptModes.Normalize(project.ManuscriptMode);
                        StoryContentProfiles.Normalize(project);
                        projects.Add(project);
                    }
                }
                catch (Exception ex)
                {
                    AppLogger.Warn("STORY", $"Project metadata skipped | path={path} | {ex.Message}");
                }
            }
            return (IReadOnlyList<StoryProject>)projects.OrderByDescending(p => p.UpdatedUtc).ToList();
        }, cancellationToken);
    }

    public async Task<StoryProject> CreateProjectAsync(string title, CancellationToken cancellationToken = default)
    {
        var project = new StoryProject
        {
            Title = string.IsNullOrWhiteSpace(title) ? "Untitled Story" : title.Trim()
        };
        await SaveProjectAsync(project, cancellationToken);
        return project;
    }

    public async Task SaveProjectAsync(StoryProject project, CancellationToken cancellationToken = default)
    {
        project.ManuscriptMode = StoryManuscriptModes.Normalize(project.ManuscriptMode);
        StoryContentProfiles.Normalize(project);
        project.UpdatedUtc = DateTimeOffset.UtcNow;
        string directory = GetProjectDirectory(project.Id);
        string path = Path.Combine(directory, "project.json");
        string json = JsonSerializer.Serialize(project, JsonOptions);
        await WriteTextAtomicAsync(directory, path, json, cancellationToken);
    }

    public async Task<IReadOnlyList<StoryMessage>> LoadMessagesAsync(string projectId, CancellationToken cancellationToken = default)
    {
        string path = Path.Combine(GetProjectDirectory(projectId), "conversation.json");
        if (!File.Exists(path))
            return Array.Empty<StoryMessage>();
        try
        {
            string json = await File.ReadAllTextAsync(path, cancellationToken);
            // Large stories can have years of conversation history. JSON parsing
            // must not block the WPF dispatcher during a story switch.
            return await Task.Run(() => ParseConversationMessages(json), cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception ex)
        {
            // Fail closed. Returning [] here would make the next autosave overwrite
            // the unreadable conversation and silently erase the user's history.
            AppLogger.Error("STORY", $"Conversation is unreadable; no replacement will be saved | project={projectId}", ex);
            throw new InvalidDataException(
                "Conversation could not be read safely. Original conversation.json was preserved. See Logs.", ex);
        }
    }

    // Shared, pure parser: self-tests can check malformed data without creating
    // expected ERROR log entries or risking the user's personal conversations.
    internal static IReadOnlyList<StoryMessage> ParseConversationMessages(string json)
    {
        try
        {
            List<StoryMessage>? messages = JsonSerializer.Deserialize<List<StoryMessage>>(json, JsonOptions);
            if (messages is null || messages.Any(item => item is null))
                throw new InvalidDataException("Conversation JSON contained a null message/list.");
            return messages;
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Conversation JSON is malformed.", ex);
        }
    }

    public async Task SaveMessagesAsync(string projectId, IReadOnlyList<StoryMessage> messages, CancellationToken cancellationToken = default)
    {
        // Capture a value snapshot before yielding at the async semaphore.
        // ToArray() alone copies references: an in-flight background serializer
        // could otherwise observe newer edits and persist a half-old/half-new turn.
        // Text strings are immutable; only mutable metadata lists need deep copies.
        StoryMessage[] snapshot = StoryMessageSnapshot.Capture(messages);
        string directory = GetProjectDirectory(projectId);
        string path = Path.Combine(directory, "conversation.json");
        long version = StoryConversationWriteOrder.Reserve(path);
        // Serialization may finish out of order when different store instances
        // save the same Story. Only the newest *successfully persisted* version
        // may replace the file; queued older completions must not roll it back.
        string json = await Task.Run(() => JsonSerializer.Serialize(snapshot, JsonOptions), cancellationToken);
        await StoryConversationWriteOrder.SaveIfNewestAsync(path, version,
            () => WriteTextAtomicAsync(directory, path, json, cancellationToken), cancellationToken);
    }

    public async Task DeleteProjectAsync(string projectId, CancellationToken cancellationToken = default)
    {
        string directory = GetProjectDirectory(projectId);
        await Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (Directory.Exists(directory))
                Directory.Delete(directory, recursive: true);
        }, cancellationToken);
    }

    public string GetProjectDirectory(string projectId) => Path.Combine(RootDirectory, SanitizeId(projectId));

    private static string SanitizeId(string id)
    {
        string cleaned = new(id.Where(char.IsLetterOrDigit).ToArray());
        return string.IsNullOrWhiteSpace(cleaned) ? "project" : cleaned;
    }

    private static async Task WriteTextAtomicAsync(string directory, string path, string content, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(directory);
        string temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            await File.WriteAllTextAsync(temp, content, cancellationToken);
            File.Move(temp, path, true);
        }
        finally
        {
            if (File.Exists(temp)) File.Delete(temp);
        }
    }
}
