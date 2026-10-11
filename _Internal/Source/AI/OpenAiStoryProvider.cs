using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace WorkChatMini;

public sealed class OpenAiStoryProvider : IStoryAiProvider, IStoryWebResearchProvider
{
    private static readonly TimeSpan ResponseHeadersTimeout = TimeSpan.FromSeconds(20);
    private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };
    public string Id => "OpenAI";
    public string DisplayName => "OpenAI API";

    public async Task StreamAsync(StoryAiRequest request, string apiKey, Action<string> onDelta, CancellationToken cancellationToken)
    {
        var stopwatch = Stopwatch.StartNew();
        int streamedCharacters = 0;
        bool firstTextLogged = false;
        AppLogger.Info("STORY-AI", $"OpenAI stream request started | model={request.Model} | messages={request.Messages.Count}");

        object body = new
        {
            model = request.Model,
            instructions = request.SystemPrompt,
            input = request.Messages.Select(m => new
            {
                role = string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? "assistant" : "user",
                content = m.Content
            }),
            stream = true
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        message.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using var headerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        headerCts.CancelAfter(ResponseHeadersTimeout);
        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, headerCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException($"OpenAI returned no response headers within {(int)ResponseHeadersTimeout.TotalSeconds}s. Check your network, proxy, VPN, or firewall.");
        }
        using (response)
        {
        AppLogger.Info("STORY-AI", $"OpenAI response headers received | status={(int)response.StatusCode} | elapsedMs={stopwatch.ElapsedMilliseconds}");
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadErrorAsync(response, cancellationToken).ConfigureAwait(false));

        await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        using var reader = new StreamReader(stream);
        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            string? line = await StorySseReader.ReadLineAsync(reader, "OpenAI", cancellationToken).ConfigureAwait(false);
            if (line is null)
                break;
            if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.Ordinal))
                continue;
            string json = line[5..].Trim();
            if (json.Length == 0 || json == "[DONE]")
                continue;
            using JsonDocument doc = JsonDocument.Parse(json);
            JsonElement root = doc.RootElement;
            if (root.TryGetProperty("type", out JsonElement type) &&
                string.Equals(type.GetString(), "response.output_text.delta", StringComparison.Ordinal) &&
                root.TryGetProperty("delta", out JsonElement deltaElement))
            {
                string? delta = deltaElement.GetString();
                if (!string.IsNullOrEmpty(delta))
                {
                    streamedCharacters += delta.Length;
                    if (!firstTextLogged)
                    {
                        firstTextLogged = true;
                        AppLogger.Info("STORY-AI", $"OpenAI first text received | elapsedMs={stopwatch.ElapsedMilliseconds} | chars={delta.Length}");
                    }
                    onDelta(delta);
                }
            }
            else if (root.TryGetProperty("error", out JsonElement error))
            {
                throw new InvalidOperationException($"OpenAI API error: {error}");
            }
        }

        AppLogger.Info("STORY-AI", $"OpenAI stream completed | elapsedMs={stopwatch.ElapsedMilliseconds} | chars={streamedCharacters}");
        if (streamedCharacters == 0)
            throw new InvalidOperationException("OpenAI completed the request without returning any text.");
        }
    }

    public async Task<StoryWebResearchResult> ResearchWebAsync(
        string model,
        string apiKey,
        string query,
        CancellationToken cancellationToken)
    {
        object body = new
        {
            model,
            instructions = "Research this real-world topic for fiction accuracy. Be concise, factual, and practical. Prefer reliable sources. Do not invent story canon.",
            input = query,
            tools = new[] { new { type = "web_search", search_context_size = "low" } }
        };

        using var message = new HttpRequestMessage(HttpMethod.Post, "https://api.openai.com/v1/responses");
        message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        message.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        using HttpResponseMessage response = await Http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException(await ReadErrorAsync(response, cancellationToken).ConfigureAwait(false));

        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument doc = JsonDocument.Parse(json);
        var result = new StoryWebResearchResult();
        if (!doc.RootElement.TryGetProperty("output", out JsonElement output) || output.ValueKind != JsonValueKind.Array)
            return result;

        var text = new StringBuilder();
        foreach (JsonElement item in output.EnumerateArray())
        {
            if (!item.TryGetProperty("type", out JsonElement type) || !string.Equals(type.GetString(), "message", StringComparison.Ordinal))
                continue;
            if (!item.TryGetProperty("content", out JsonElement content) || content.ValueKind != JsonValueKind.Array)
                continue;

            foreach (JsonElement part in content.EnumerateArray())
            {
                if (part.TryGetProperty("type", out JsonElement partType) &&
                    string.Equals(partType.GetString(), "output_text", StringComparison.Ordinal) &&
                    part.TryGetProperty("text", out JsonElement textElement) && textElement.ValueKind == JsonValueKind.String)
                {
                    if (text.Length > 0)
                        text.AppendLine();
                    text.Append(textElement.GetString());
                }

                if (!part.TryGetProperty("annotations", out JsonElement annotations) || annotations.ValueKind != JsonValueKind.Array)
                    continue;
                foreach (JsonElement annotation in annotations.EnumerateArray())
                {
                    if (!annotation.TryGetProperty("type", out JsonElement annotationType) ||
                        !string.Equals(annotationType.GetString(), "url_citation", StringComparison.Ordinal))
                        continue;
                    string url = ReadString(annotation, "url");
                    string title = ReadString(annotation, "title");
                    if (string.IsNullOrWhiteSpace(url) && annotation.TryGetProperty("url_citation", out JsonElement nested))
                    {
                        url = ReadString(nested, "url");
                        title = ReadString(nested, "title");
                    }
                    if (!string.IsNullOrWhiteSpace(url))
                        result.Sources.Add(new StoryResearchSource { Url = url, Title = string.IsNullOrWhiteSpace(title) ? url : title });
                }
            }
        }

        result.Summary = text.ToString().Trim();
        result.Sources = result.Sources
            .GroupBy(source => source.Url, StringComparer.OrdinalIgnoreCase)
            .Select(group => group.First())
            .Take(8)
            .ToList();
        return result;
    }

    private static string ReadString(JsonElement element, string property) =>
        element.TryGetProperty(property, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty
            : string.Empty;

    private static async Task<string> ReadErrorAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        return $"OpenAI API {(int)response.StatusCode}: {(body.Length <= 600 ? body : body[..600] + "…")}";
    }
}
