using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace WorkChatMini;

public sealed partial class GeminiStoryProvider : IStoryAiProvider, IStoryWebResearchProvider
{
    private static readonly TimeSpan ResponseHeadersTimeout = TimeSpan.FromSeconds(12);
    private static readonly Lazy<HttpClient> HttpHolder = new(CreateHttpClientSafe);
    private static HttpClient Http => HttpHolder.Value;
    private static int _preferNonStreaming;

    public string Id => "Gemini";
    public string DisplayName => "Gemini API";

    public async Task StreamAsync(StoryAiRequest request, string apiKey, Action<string> onDelta, CancellationToken cancellationToken)
    {
        string primaryModel = NormalizeModel(request.Model);
        await ExecuteTextRequestWithResilienceAsync(
            primaryModel,
            request,
            apiKey,
            onDelta,
            cancellationToken).ConfigureAwait(false);
    }

    private static string BuildBodyJson(StoryAiRequest request, string model)
    {
        var generationConfig = new Dictionary<string, object>
        {
            ["temperature"] = request.Temperature
        };
        if (SupportsThinkingLevel(model))
        {
            generationConfig["thinkingConfig"] = new
            {
                thinkingLevel = ResolveThinkingLevel(model, request.ThinkingLevel)
            };
        }

        return JsonSerializer.Serialize(new
        {
            systemInstruction = new { parts = new[] { new { text = request.SystemPrompt } } },
            contents = request.Messages.Select(m => new
            {
                role = string.Equals(m.Role, "assistant", StringComparison.OrdinalIgnoreCase) ? "model" : "user",
                parts = new[] { new { text = m.Content } }
            }),
            generationConfig
        });
    }

    private static async Task StreamSseAsync(
        string model,
        string apiKey,
        string bodyJson,
        Action<string> onDelta,
        CancellationToken cancellationToken)
    {
        string endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:streamGenerateContent?alt=sse";
        var stopwatch = Stopwatch.StartNew();
        int streamedCharacters = 0;
        bool firstTextLogged = false;
        AppLogger.Info("STORY-AI", $"Gemini stream request started | model={model}");

        using var message = CreateRequest(endpoint, apiKey, bodyJson);
        using var headerCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        headerCts.CancelAfter(ResponseHeadersTimeout);

        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(message, HttpCompletionOption.ResponseHeadersRead, headerCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new GeminiHeadersTimeoutException(
                $"Gemini streaming endpoint returned no response headers within {(int)ResponseHeadersTimeout.TotalSeconds}s.");
        }

        using (response)
        {
            AppLogger.Info("STORY-AI", $"Gemini response headers received | status={(int)response.StatusCode} | elapsedMs={stopwatch.ElapsedMilliseconds}");
            if (!response.IsSuccessStatusCode)
                throw await CreateApiExceptionAsync(response, model, cancellationToken).ConfigureAwait(false);

            await using Stream stream = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
            using var reader = new StreamReader(stream);
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string? line = await StorySseReader.ReadLineAsync(reader, "Gemini", cancellationToken).ConfigureAwait(false);
                if (line is null)
                    break;
                if (string.IsNullOrWhiteSpace(line) || !line.StartsWith("data:", StringComparison.Ordinal))
                    continue;
                string json = line[5..].Trim();
                if (json.Length == 0 || json == "[DONE]")
                    continue;

                using JsonDocument doc = JsonDocument.Parse(json);
                if (!doc.RootElement.TryGetProperty("candidates", out JsonElement candidates) || candidates.GetArrayLength() == 0)
                    continue;
                JsonElement candidate = candidates[0];
                if (!candidate.TryGetProperty("content", out JsonElement content) ||
                    !content.TryGetProperty("parts", out JsonElement parts))
                    continue;

                foreach (JsonElement part in parts.EnumerateArray())
                {
                    if (!part.TryGetProperty("text", out JsonElement text) || text.ValueKind != JsonValueKind.String)
                        continue;
                    string? delta = text.GetString();
                    if (string.IsNullOrEmpty(delta))
                        continue;

                    streamedCharacters += delta.Length;
                    if (!firstTextLogged)
                    {
                        firstTextLogged = true;
                        AppLogger.Info("STORY-AI", $"Gemini first text received | elapsedMs={stopwatch.ElapsedMilliseconds} | chars={delta.Length}");
                    }
                    onDelta(delta);
                }
            }
        }

        AppLogger.Info("STORY-AI", $"Gemini stream completed | elapsedMs={stopwatch.ElapsedMilliseconds} | chars={streamedCharacters}");
        if (streamedCharacters == 0)
            throw new InvalidOperationException("Gemini completed the request without returning any text.");
    }

    private static async Task GenerateNonStreamingAsync(
        string model,
        string apiKey,
        string bodyJson,
        TimeSpan timeout,
        Action<string> onDelta,
        CancellationToken cancellationToken)
    {
        string endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent";
        var stopwatch = Stopwatch.StartNew();
        AppLogger.Info("STORY-AI", $"Gemini non-stream request started | model={model}");

        using var message = CreateRequest(endpoint, apiKey, bodyJson);
        using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        requestCts.CancelAfter(timeout);

        HttpResponseMessage response;
        try
        {
            response = await Http.SendAsync(message, HttpCompletionOption.ResponseContentRead, requestCts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new TimeoutException(
                $"Gemini did not respond within {(int)timeout.TotalSeconds}s. Check whether generativelanguage.googleapis.com is blocked by a proxy, VPN, firewall, or company network policy.");
        }

        using (response)
        {
            AppLogger.Info("STORY-AI", $"Gemini non-stream response received | status={(int)response.StatusCode} | elapsedMs={stopwatch.ElapsedMilliseconds}");
            if (!response.IsSuccessStatusCode)
                throw await CreateApiExceptionAsync(response, model, cancellationToken).ConfigureAwait(false);

            string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
            string text = ExtractResponseText(json);
            if (string.IsNullOrWhiteSpace(text))
                throw new InvalidOperationException("Gemini completed the fallback request without returning any text.");

            AppLogger.Info("STORY-AI", $"Gemini non-stream text received | elapsedMs={stopwatch.ElapsedMilliseconds} | chars={text.Length}");
            onDelta(text);
        }
    }

    public async Task<StoryWebResearchResult> ResearchWebAsync(
        string model,
        string apiKey,
        string query,
        CancellationToken cancellationToken)
    {
        string primaryModel = NormalizeModel(model);
        return await ExecuteResearchWithResilienceAsync(
            primaryModel,
            apiKey,
            query,
            cancellationToken).ConfigureAwait(false);
    }

    private static async Task<StoryWebResearchResult> ResearchOnceAsync(
        string model,
        string apiKey,
        string query,
        CancellationToken cancellationToken)
    {
        string endpoint = $"https://generativelanguage.googleapis.com/v1beta/models/{Uri.EscapeDataString(model)}:generateContent";
        object body = new
        {
            systemInstruction = new
            {
                parts = new[]
                {
                    new
                    {
                        text = "Research this real-world topic for fiction accuracy. Be concise, factual, and practical. Prefer reliable sources. Do not invent story canon. Return a short synthesis only; source metadata is collected separately by the app."
                    }
                }
            },
            contents = new[]
            {
                new { role = "user", parts = new[] { new { text = query } } }
            },
            tools = new[] { new { googleSearch = new Dictionary<string, object>() } },
            generationConfig = BuildResearchGenerationConfig(model)
        };

        using var message = CreateRequest(endpoint, apiKey, JsonSerializer.Serialize(body));
        using HttpResponseMessage response = await Http.SendAsync(message, cancellationToken).ConfigureAwait(false);
        if (!response.IsSuccessStatusCode)
            throw await CreateApiExceptionAsync(response, model, cancellationToken).ConfigureAwait(false);

        string json = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
        using JsonDocument doc = JsonDocument.Parse(json);
        var result = new StoryWebResearchResult();
        if (!doc.RootElement.TryGetProperty("candidates", out JsonElement candidates) || candidates.GetArrayLength() == 0)
            return result;

        JsonElement candidate = candidates[0];
        if (candidate.TryGetProperty("content", out JsonElement content) &&
            content.TryGetProperty("parts", out JsonElement parts))
        {
            var text = new StringBuilder();
            foreach (JsonElement part in parts.EnumerateArray())
            {
                if (part.TryGetProperty("text", out JsonElement textElement) && textElement.ValueKind == JsonValueKind.String)
                    text.Append(textElement.GetString());
            }
            result.Summary = text.ToString().Trim();
        }

        if (candidate.TryGetProperty("groundingMetadata", out JsonElement grounding) &&
            grounding.TryGetProperty("groundingChunks", out JsonElement chunks) &&
            chunks.ValueKind == JsonValueKind.Array)
        {
            foreach (JsonElement chunk in chunks.EnumerateArray())
            {
                if (!chunk.TryGetProperty("web", out JsonElement web))
                    continue;
                string url = web.TryGetProperty("uri", out JsonElement uri) ? uri.GetString() ?? string.Empty : string.Empty;
                string title = web.TryGetProperty("title", out JsonElement titleElement) ? titleElement.GetString() ?? string.Empty : string.Empty;
                if (!string.IsNullOrWhiteSpace(url))
                    result.Sources.Add(new StoryResearchSource { Url = url, Title = string.IsNullOrWhiteSpace(title) ? url : title });
            }
        }
        return result;
    }

    private static HttpRequestMessage CreateRequest(string endpoint, string apiKey, string bodyJson)
    {
        var message = new HttpRequestMessage(HttpMethod.Post, endpoint);
        message.Headers.TryAddWithoutValidation("x-goog-api-key", apiKey);
        message.Content = new StringContent(bodyJson, Encoding.UTF8, "application/json");
        return message;
    }

    private static string ExtractResponseText(string json)
    {
        using JsonDocument doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("candidates", out JsonElement candidates) || candidates.GetArrayLength() == 0)
            return string.Empty;
        JsonElement candidate = candidates[0];
        if (!candidate.TryGetProperty("content", out JsonElement content) ||
            !content.TryGetProperty("parts", out JsonElement parts) ||
            parts.ValueKind != JsonValueKind.Array)
            return string.Empty;

        var text = new StringBuilder();
        foreach (JsonElement part in parts.EnumerateArray())
        {
            if (part.TryGetProperty("text", out JsonElement textElement) && textElement.ValueKind == JsonValueKind.String)
                text.Append(textElement.GetString());
        }
        return text.ToString().Trim();
    }

    private static Dictionary<string, object> BuildResearchGenerationConfig(string model)
    {
        var config = new Dictionary<string, object> { ["temperature"] = 0.2 };
        if (SupportsThinkingLevel(model))
        {
            config["thinkingConfig"] = new
            {
                thinkingLevel = ResolveThinkingLevel(model, "low")
            };
        }
        return config;
    }

    private static bool SupportsThinkingLevel(string model) =>
        model.StartsWith("gemini-3", StringComparison.OrdinalIgnoreCase);

    private static string ResolveThinkingLevel(string model, string requested)
    {
        string normalized = requested.Trim().ToLowerInvariant();
        if (normalized is not ("minimal" or "low" or "medium" or "high"))
            normalized = "low";

        if (model.Contains("flash-lite", StringComparison.OrdinalIgnoreCase) && normalized == "low")
            return "minimal";
        if (model.StartsWith("gemini-3.8", StringComparison.OrdinalIgnoreCase) && normalized == "minimal")
            return "low";
        return normalized;
    }

    private static string NormalizeModel(string model) =>
        model.StartsWith("models/", StringComparison.OrdinalIgnoreCase) ? model[7..] : model;

    private static string Trim(string value) => value.Length <= 600 ? value : value[..600] + "…";

    private sealed class GeminiHeadersTimeoutException(string message) : TimeoutException(message);
}
